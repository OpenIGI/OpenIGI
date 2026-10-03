#!/usr/bin/env bash
# The only thing the release workflow can run on the VM.
#
# The deploy key in ~deploy/.ssh/authorized_keys is pinned to this script with command="…",restrict, so
# CI cannot open a shell, forward a port or run anything else: it names one of the verbs below in
# SSH_ORIGINAL_COMMAND and streams a tarball on stdin. Arguments are validated before anything is touched.
#
#   release <version>            < downloads + release.json  publish a game release's downloads
#   launcher <version>           < downloads + release.json  publish a launcher release's downloads
#   web <channel> <version>      < site tarball              deploy the browser build and switch to it
#   rollback <channel>                                       switch the site back one deploy
#   api <commit>                 < service build tarball     deploy the update service and restart it
#   config                       < deploy/vm tarball         install compose.yml + Caddyfile, apply them
#   prune                                                    keep the newest betas, drop older ones
#   content-have                 < chunk hashes, one a line  print the ones the content store lacks
#   content <pack> <version>     < chunks + content.json     publish a version of an optional content pack
#   content-prune <pack>                                     keep a pack's newest versions, drop unused chunks
#   content-delete <pack> [<version>]                        delete a pack, or one version of it
#   content-info <pack>          < pack.json [+ picture]     rename, describe or picture a pack
#   status                                                   what is live
#
# Swaps are a symlink replaced with rename(2), so a request sees the old deploy or the new one, never half.
set -euo pipefail
umask 022

# OPENIGI_SRV only matters to a test run of this script; sshd passes no such variable to the forced command.
SRV="${OPENIGI_SRV:-/srv/openigi}"
KEEP_WEB=3          # deploys per site channel kept for rollback
KEEP_API=3
KEEP_BETAS=10       # beta releases kept downloadable; stable releases are never removed
KEEP_CONTENT=3      # versions kept per content pack: a launcher on an older one still updates from its chunks

VERSION_RE='^[0-9]+\.[0-9]+\.[0-9]+(-beta\.[0-9]+)?$'
BUILD_RE='^[0-9a-f]{7,40}$'   # the update service deploys per commit, not per release
PACK_RE='^[a-z0-9]([a-z0-9-]{0,62}[a-z0-9])?$'
PACK_VERSION_RE='^[0-9]+\.[0-9]+\.[0-9]+$'
CHUNK_RE='^[0-9a-f]{64}$'
THUMBNAIL_RE='^thumbnail-[0-9a-f]{12}[.](png|jpg|webp)$'
MAX_THUMBNAIL=2097152   # ContentPackInfo's limit, 2 MiB
CONTENT="$SRV/dl/content"

die() { echo "deploy: $*" >&2; exit 1; }
log() { echo "deploy: $*" >&2; }

cd "$SRV"
read -r -a ARGS <<< "${SSH_ORIGINAL_COMMAND:-${*:-}}"
VERB="${ARGS[0]:-}"

version_arg() {
  [[ "${1:-}" =~ $VERSION_RE ]] || die "'${1:-}' is not a release version"
  echo "$1"
}

channel_arg() {
  case "${1:-}" in stable|beta) echo "$1" ;; *) die "'${1:-}' is not a channel (stable or beta)" ;; esac
}

# Unpacks stdin into a fresh staging folder beside the destination (same filesystem, so the final move is
# a rename). GNU tar already strips leading / and ../ from member names; links are refused outright, so
# nothing in an upload can point outside the folder it lands in.
unpack() {
  local dest_parent="$1" staging
  mkdir -p "$dest_parent"
  staging="$(mktemp -d "$dest_parent/.incoming.XXXXXX")"
  tar -xzf - -C "$staging" --no-same-owner --no-same-permissions \
    || { rm -rf "$staging"; die "the upload is not a valid tarball"; }
  if find "$staging" -type l | grep -q .; then
    rm -rf "$staging"; die "the upload holds links"
  fi
  chmod -R a+rX "$staging"
  echo "$staging"
}

pack_arg() {
  [[ "${1:-}" =~ $PACK_RE && "$1" != chunks ]] || die "'${1:-}' is not a content pack name"
  echo "$1"
}

chunk_path() { echo "$CONTENT/chunks/${1:0:2}/$1.br"; }

# The content store is changed by publishes and prunes, and a prune must never see a publish's chunks moved
# in before its manifest is: it would find them used by nothing and delete them.
lock_content() {
  mkdir -p "$CONTENT/chunks"
  exec 9>"$CONTENT/.lock"
  flock -w 600 9 || die "the content store is busy"
}

# The chunk hashes a manifest names, one a line. Read straight out of content.json, which every published
# version has and which is what launchers install from, so nothing else can disagree with it.
manifest_chunks() {
  { grep -ohE '"hash": *"[0-9a-f]{64}"' "$@" || true; } | cut -d'"' -f4 | sort -u
}

# Deletes every stored chunk that no version of any pack names. Called with the content lock held.
collect_chunks() {
  local keep stored removed=0 manifest
  # A manifest that yields no hashes would make every chunk look unused; nothing is deleted then.
  while IFS= read -r -d '' manifest; do
    [ -n "$(manifest_chunks "$manifest" | head -n 1)" ] || die "$manifest names no chunks; collecting nothing"
  done < <(find "$CONTENT" -mindepth 3 -maxdepth 3 -name content.json -print0)
  keep="$(mktemp)"
  stored="$(mktemp)"
  find "$CONTENT" -mindepth 3 -maxdepth 3 -name content.json -print0 | xargs -0r cat | manifest_chunks - > "$keep"
  find "$CONTENT/chunks" -type f -name '*.br' -printf '%f\n' | sed 's/\.br$//' | sort -u > "$stored"
  while read -r H; do
    [[ "$H" =~ $CHUNK_RE ]] && rm -f "$(chunk_path "$H")" && removed=$((removed + 1))
  done < <(comm -23 "$stored" "$keep")
  rm -f "$keep" "$stored"
  log "removed $removed unused chunk(s)"
}

switch() {
  local base="$1" target="$2"
  ln -sfn "$target" "$base/current.next"
  mv -T "$base/current.next" "$base/current"
}

prune_versions() {
  local base="$1" keep="$2" live
  live="$(readlink "$base/current" 2>/dev/null || true)"
  # Version folders, oldest first by modification time, minus the newest $keep and the live one.
  find "$base" -mindepth 1 -maxdepth 1 -type d ! -name '.*' -printf '%T@ %f\n' | sort -n | head -n "-$keep" \
    | while read -r _ name; do
        [ "$name" = "$live" ] || { rm -rf "${base:?}/$name"; log "removed $base/$name"; }
      done
}

case "$VERB" in
  release|launcher)
    # A game release lives in releases/<version>, a launcher release in releases/launcher-<version>: the two
    # lines are numbered independently and will meet at some version sooner or later.
    V="$(version_arg "${ARGS[1]:-}")"
    [[ "$V" == *-* && "$VERB" == launcher ]] && die "the launcher has one channel, stable: $V is a pre-release"
    FOLDER="$V"
    [ "$VERB" = launcher ] && FOLDER="launcher-$V"
    DEST="$SRV/dl/releases/$FOLDER"
    [ -e "$DEST/release.json" ] && die "$FOLDER is already published"
    STAGING="$(unpack "$SRV/dl/releases")"
    [ -f "$STAGING/release.json" ] || { rm -rf "$STAGING"; die "the upload has no release.json"; }
    # Downloads land first; release.json last, because the update service treats a folder with a
    # manifest as a complete release.
    (cd "$STAGING" && sha256sum --quiet -c SHA256SUMS) || { rm -rf "$STAGING"; die "a download does not match SHA256SUMS"; }
    mkdir -p "$DEST"
    find "$STAGING" -mindepth 1 -maxdepth 1 ! -name release.json -exec mv -t "$DEST" {} +
    mv "$STAGING/release.json" "$DEST/release.json"
    rmdir "$STAGING"
    log "published $FOLDER ($(du -sh "$DEST" | cut -f1))"
    ;;

  web)
    C="$(channel_arg "${ARGS[1]:-}")"
    V="$(version_arg "${ARGS[2]:-}")"
    BASE="$SRV/web/$C"
    STAGING="$(unpack "$BASE")"
    [ -f "$STAGING/index.html" ] || { rm -rf "$STAGING"; die "the upload has no index.html"; }
    rm -rf "${BASE:?}/$V"
    mv -T "$STAGING" "$BASE/$V"
    switch "$BASE" "$V"
    prune_versions "$BASE" "$KEEP_WEB"
    log "$C site is now $V"
    ;;

  rollback)
    C="$(channel_arg "${ARGS[1]:-}")"
    BASE="$SRV/web/$C"
    LIVE="$(readlink "$BASE/current" 2>/dev/null || true)"
    PREVIOUS="$(find "$BASE" -mindepth 1 -maxdepth 1 -type d ! -name '.*' ! -name "$LIVE" -printf '%T@ %f\n' \
      | sort -n | tail -n 1 | cut -d' ' -f2)"
    [ -n "$PREVIOUS" ] || die "no earlier $C deploy to roll back to"
    switch "$BASE" "$PREVIOUS"
    touch "$BASE/$LIVE"   # the abandoned deploy becomes the newest, so a second rollback undoes this one
    log "$C site rolled back from $LIVE to $PREVIOUS"
    ;;

  api)
    V="${ARGS[1]:-}"
    [[ "$V" =~ $BUILD_RE ]] || die "'$V' is not a commit"
    BASE="$SRV/api"
    STAGING="$(unpack "$BASE")"
    [ -f "$STAGING/OpenIGI.Services.dll" ] || { rm -rf "$STAGING"; die "the upload is not the update service"; }
    rm -rf "${BASE:?}/$V"
    mv -T "$STAGING" "$BASE/$V"
    PREVIOUS="$(readlink "$BASE/current" 2>/dev/null || true)"
    switch "$BASE" "$V"
    docker compose -f "$SRV/config/compose.yml" up -d --force-recreate --wait api \
      || { [ -n "$PREVIOUS" ] && switch "$BASE" "$PREVIOUS" \
             && docker compose -f "$SRV/config/compose.yml" up -d --force-recreate api; die "$V failed its health check; kept ${PREVIOUS:-nothing}"; }
    prune_versions "$BASE" "$KEEP_API"
    log "update service is now $V"
    ;;

  config)
    STAGING="$(unpack "$SRV")"
    for f in compose.yml Caddyfile; do
      [ -f "$STAGING/$f" ] || { rm -rf "$STAGING"; die "the upload has no $f"; }
    done
    # The api mounts the content store; made here first, or docker would create it owned by root.
    mkdir -p "$SRV/config" "$CONTENT/chunks"
    install -m 0644 "$STAGING/compose.yml" "$SRV/config/compose.yml"
    # Caddy mounts this one file, which binds the file the container was started with: a new file put in
    # its place (as install does) is never seen, and every reload reads the old one. Its contents are
    # rewritten instead, so the mount sees them.
    if [ -f "$SRV/config/Caddyfile" ]; then
      cat "$STAGING/Caddyfile" > "$SRV/config/Caddyfile"
    else
      install -m 0644 "$STAGING/Caddyfile" "$SRV/config/Caddyfile"
    fi
    rm -rf "$STAGING"
    docker compose -f "$SRV/config/compose.yml" up -d --remove-orphans
    docker compose -f "$SRV/config/compose.yml" exec -T caddy caddy reload --config /etc/caddy/Caddyfile
    log "configuration applied"
    ;;

  prune)
    find "$SRV/dl/releases" -mindepth 1 -maxdepth 1 -type d -name '*-beta.*' -printf '%T@ %f\n' \
      | sort -n | head -n "-$KEEP_BETAS" | while read -r _ name; do
          rm -rf "${SRV:?}/dl/releases/$name"; log "removed beta $name"
        done
    ;;

  content-have)
    # Asked before a publish uploads anything, so only the chunks the store does not hold yet are sent.
    while read -r H; do
      [[ "$H" =~ $CHUNK_RE ]] || die "'$H' is not a chunk hash"
      [ -f "$(chunk_path "$H")" ] || echo "$H"
    done
    ;;

  content)
    # A version of a pack lives in content/<pack>/<version>, beside the chunk store every version shares:
    #   content.json   the manifest, which the update service serves; moved in last
    #   chunks/xx/<hash>.br, SHA256SUMS   the chunks the store lacked, and their checksums
    P="$(pack_arg "${ARGS[1]:-}")"
    V="${ARGS[2]:-}"
    [[ "$V" =~ $PACK_VERSION_RE ]] || die "'$V' is not a content version"
    DEST="$CONTENT/$P/$V"
    [ -e "$DEST/content.json" ] && die "$P $V is already published"
    lock_content
    STAGING="$(unpack "$CONTENT")"
    trap 'rm -rf "$STAGING"' EXIT
    for f in content.json SHA256SUMS; do
      [ -f "$STAGING/$f" ] || die "the upload has no $f"
    done
    grep -q "\"id\": \"$P\"" "$STAGING/content.json" && grep -q "\"version\": \"$V\"" "$STAGING/content.json" \
      || die "content.json is not $P $V"
    NAMED="$(manifest_chunks "$STAGING/content.json")"
    [ -n "$NAMED" ] || die "content.json names no chunks"
    # Nothing but the two documents and well-named chunks, each in the folder its hash says.
    while IFS= read -r -d '' f; do
      rel="${f#"$STAGING"/}"
      case "$rel" in
        content.json|SHA256SUMS|chunks|chunks/??) ;;
        chunks/??/*.br)
          name="$(basename "$rel" .br)"
          [[ "$name" =~ $CHUNK_RE && "$rel" == "chunks/${name:0:2}/$name.br" ]] || die "unexpected file $rel"
          ;;
        *) die "unexpected file $rel" ;;
      esac
    done < <(find "$STAGING" -mindepth 1 -print0)
    # An update that only reuses chunks uploads none, and an empty list is not one sha256sum accepts.
    [ ! -s "$STAGING/SHA256SUMS" ] || (cd "$STAGING" && sha256sum --quiet -c SHA256SUMS) \
      || die "a chunk does not match SHA256SUMS"
    # Chunks in first; one already in the store is the same bytes by its name, so it is left as it is.
    if [ -d "$STAGING/chunks" ]; then
      while IFS= read -r -d '' f; do
        rel="${f#"$STAGING"/}"
        mkdir -p "$(dirname "$CONTENT/$rel")"
        [ -e "$CONTENT/$rel" ] || mv "$f" "$CONTENT/$rel"
      done < <(find "$STAGING/chunks" -type f -print0)
    fi
    # Every chunk the version names must now be in the store, or a launcher would find the version
    # published and fail half way through installing it.
    MISSING=0
    while read -r H; do
      [ -f "$(chunk_path "$H")" ] || { log "missing chunk $H"; MISSING=$((MISSING + 1)); }
    done <<< "$NAMED"
    [ "$MISSING" -eq 0 ] || die "$MISSING chunk(s) of $P $V are neither uploaded nor in the store"
    mkdir -p "$DEST"
    mv "$STAGING/content.json" "$DEST/content.json"
    log "published $P $V ($(wc -l <<< "$NAMED") chunk(s); store $(du -sh "$CONTENT/chunks" | cut -f1))"
    ;;

  content-prune)
    P="$(pack_arg "${ARGS[1]:-}")"
    lock_content
    if [ -d "$CONTENT/$P" ]; then
      find "$CONTENT/$P" -mindepth 1 -maxdepth 1 -type d ! -name '.*' -printf '%f\n' | sort -V \
        | head -n "-$KEEP_CONTENT" | while read -r name; do
            rm -rf "${CONTENT:?}/$P/$name"; log "removed $P $name"
          done
    fi
    collect_chunks
    ;;

  content-delete)
    # A whole pack, or one version of it. Launchers that installed it keep their files and stop seeing
    # updates; a deleted version's chunks go once no other version names them.
    P="$(pack_arg "${ARGS[1]:-}")"
    V="${ARGS[2]:-}"
    [ -z "$V" ] || [[ "$V" =~ $PACK_VERSION_RE ]] || die "'$V' is not a content version"
    lock_content
    if [ -n "$V" ]; then
      [ -f "$CONTENT/$P/$V/content.json" ] || die "$P $V is not published"
      rm -rf "${CONTENT:?}/$P/$V"
      log "deleted $P $V"
    else
      [ -d "$CONTENT/$P" ] || die "there is no pack $P"
      rm -rf "${CONTENT:?}/$P"
      log "deleted $P and every version of it"
    fi
    collect_chunks
    ;;

  content-info)
    # A pack's own description: pack.json (its name and summary as players see them) and, optionally, its
    # picture. Nothing a player installed changes, so no version is published for it.
    P="$(pack_arg "${ARGS[1]:-}")"
    [ -d "$CONTENT/$P" ] || die "there is no pack $P; publish a version first"
    lock_content
    STAGING="$(unpack "$CONTENT")"
    trap 'rm -rf "$STAGING"' EXIT
    [ -f "$STAGING/pack.json" ] || die "the upload has no pack.json"
    grep -q "\"id\": \"$P\"" "$STAGING/pack.json" || die "pack.json does not describe $P"
    PICTURE=""
    while IFS= read -r -d '' f; do
      rel="${f#"$STAGING"/}"
      case "$rel" in
        pack.json) ;;
        thumbnail-*)
          [[ "$rel" =~ $THUMBNAIL_RE ]] || die "unexpected file $rel"
          [ -z "$PICTURE" ] || die "the upload has more than one picture"
          [ "$(stat -c %s "$f")" -le "$MAX_THUMBNAIL" ] || die "$rel is larger than $MAX_THUMBNAIL bytes"
          PICTURE="$rel"
          ;;
        *) die "unexpected file $rel" ;;
      esac
    done < <(find "$STAGING" -mindepth 1 -print0)
    # The picture lands first, then the description naming it; pictures it no longer names go last.
    [ -z "$PICTURE" ] || mv "$STAGING/$PICTURE" "$CONTENT/$P/$PICTURE"
    mv "$STAGING/pack.json" "$CONTENT/$P/pack.json"
    NAMED="$(grep -oE 'thumbnail-[0-9a-f]{12}\.(png|jpg|webp)' "$CONTENT/$P/pack.json" || true)"
    find "$CONTENT/$P" -maxdepth 1 -type f -name 'thumbnail-*' ! -name "${NAMED:-none}" -delete
    [ -z "$NAMED" ] || [ -f "$CONTENT/$P/$NAMED" ] || log "warning: pack.json names $NAMED, which is not here"
    log "described $P${NAMED:+ (picture $NAMED)}"
    ;;

  status)
    for C in stable beta; do echo "web/$C: $(readlink "$SRV/web/$C/current" 2>/dev/null || echo none)"; done
    echo "api: $(readlink "$SRV/api/current" 2>/dev/null || echo none)"
    echo "releases: $(find "$SRV/dl/releases" -mindepth 2 -maxdepth 2 -name release.json -printf '%h\n' 2>/dev/null \
      | xargs -rn1 basename | sort -V | tr '\n' ' ')"
    echo "content: $(find "$CONTENT" -mindepth 3 -maxdepth 3 -name content.json -printf '%h\n' 2>/dev/null \
      | sed "s|^$CONTENT/||" | sort -V | tr '\n' ' ')"
    ;;

  *)
    die "unknown command '${SSH_ORIGINAL_COMMAND:-}' (release, launcher, web, rollback, api, config, prune, content-have, content, content-prune, content-delete, content-info, status)"
    ;;
esac
