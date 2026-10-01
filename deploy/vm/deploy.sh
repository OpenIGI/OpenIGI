#!/usr/bin/env bash
# The only thing the release workflow can run on the VM.
#
# The deploy key in ~deploy/.ssh/authorized_keys is pinned to this script with command="…",restrict, so
# CI cannot open a shell, forward a port or run anything else: it names one of the verbs below in
# SSH_ORIGINAL_COMMAND and streams a tarball on stdin. Arguments are validated before anything is touched.
#
#   release <version>            < archives + release.json   publish a release's downloads
#   web <channel> <version>      < site tarball              deploy the browser build and switch to it
#   rollback <channel>                                       switch the site back one deploy
#   api <commit>                 < service build tarball     deploy the update service and restart it
#   config                       < deploy/vm tarball         install compose.yml + Caddyfile, apply them
#   prune                                                    keep the newest betas, drop older ones
#   status                                                   what is live
#
# Swaps are a symlink replaced with rename(2), so a request sees the old deploy or the new one, never half.
set -euo pipefail
umask 022

SRV=/srv/openigi
KEEP_WEB=3          # deploys per site channel kept for rollback
KEEP_API=3
KEEP_BETAS=10       # beta releases kept downloadable; stable releases are never removed

VERSION_RE='^[0-9]+\.[0-9]+\.[0-9]+(-beta\.[0-9]+)?$'
BUILD_RE='^[0-9a-f]{7,40}$'   # the update service deploys per commit, not per release

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
  release)
    V="$(version_arg "${ARGS[1]:-}")"
    DEST="$SRV/dl/releases/$V"
    [ -e "$DEST/release.json" ] && die "$V is already published"
    STAGING="$(unpack "$SRV/dl/releases")"
    [ -f "$STAGING/release.json" ] || { rm -rf "$STAGING"; die "the upload has no release.json"; }
    # Archives land first; release.json last, because the update service treats a folder with a
    # manifest as a complete release.
    (cd "$STAGING" && sha256sum --quiet -c SHA256SUMS) || { rm -rf "$STAGING"; die "an archive does not match SHA256SUMS"; }
    mkdir -p "$DEST"
    find "$STAGING" -mindepth 1 -maxdepth 1 ! -name release.json -exec mv -t "$DEST" {} +
    mv "$STAGING/release.json" "$DEST/release.json"
    rmdir "$STAGING"
    log "published $V ($(du -sh "$DEST" | cut -f1))"
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
    mkdir -p "$SRV/config"
    install -m 0644 "$STAGING/compose.yml" "$SRV/config/compose.yml"
    install -m 0644 "$STAGING/Caddyfile" "$SRV/config/Caddyfile"
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

  status)
    for C in stable beta; do echo "web/$C: $(readlink "$SRV/web/$C/current" 2>/dev/null || echo none)"; done
    echo "api: $(readlink "$SRV/api/current" 2>/dev/null || echo none)"
    echo "releases: $(find "$SRV/dl/releases" -mindepth 2 -maxdepth 2 -name release.json -printf '%h\n' 2>/dev/null \
      | xargs -rn1 basename | sort -V | tr '\n' ' ')"
    ;;

  *)
    die "unknown command '${SSH_ORIGINAL_COMMAND:-}' (release, web, rollback, api, config, prune, status)"
    ;;
esac
