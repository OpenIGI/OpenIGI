#!/usr/bin/env bash
# Installs the OpenIGI Launcher on macOS (Apple silicon) or Linux (x64):
#
#     curl -fsSL https://openigi.com/install.sh | bash
#     curl -fsSL https://openigi.com/install.sh | bash -s -- 0.4.1     # one version in particular
#
# It fetches the launcher, checks it against its release's SHA256SUMS and installs it:
#   macOS   /Applications/OpenIGI Launcher.app (~/Applications when /Applications is not writable)
#   Linux   ~/.local/bin/openigi-launcher, with an entry in the applications menu
# The launcher then finds the game, installs OpenIGI and keeps both up to date.
#
# Why a script on macOS: the launcher is ad-hoc signed, not signed by an Apple developer or notarized.
# A browser marks what it downloads as quarantined, and Gatekeeper refuses a quarantined ad-hoc app as
# "damaged", which neither right-click > Open nor "Open Anyway" gets past. curl and wget mark nothing.
#
# Settings, as environment variables:
#   OPENIGI_INSTALL_DIR         where to install (macOS: the folder the app goes in; Linux: the bin folder)
#   OPENIGI_NO_LAUNCH=1         do not start the launcher afterwards (macOS)
#   OPENIGI_INSTALL_ALLOW_SUDO=1  install for root on purpose
#
# Windows: download the launcher from https://openigi.com instead.

# Everything is inside main, called on the last line, so a download cut short runs nothing.
main() {
  set -euo pipefail

  local target="${1:-latest}"
  if [[ ! "$target" =~ ^(latest|[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?)$ ]]; then
    printf 'Usage: install.sh [latest|VERSION]\n' >&2
    exit 1
  fi

  local site="https://openigi.com/download"
  local releases="https://github.com/OpenIGI/OpenIGI/releases/download"
  local name="OpenIGI Launcher"

  setup_colours
  refuse_sudo

  # --- What to install --------------------------------------------------------------------------------

  local os arch rid
  os="$(uname -s)"
  arch="$(uname -m)"

  # A shell running under Rosetta reports x86_64 on an Apple silicon Mac; the app is arm64 either way.
  if [ "$os" = Darwin ] && [ "$arch" = x86_64 ] && [ "$(sysctl -n sysctl.proc_translated 2>/dev/null || true)" = 1 ]; then
    arch=arm64
  fi

  case "$os/$arch" in
    Darwin/arm64) rid="osx-arm64" ;;
    Darwin/*) fail "OpenIGI needs a Mac with Apple silicon (M1 or later)." ;;
    Linux/x86_64 | Linux/amd64) rid="linux-x64" ;;
    Linux/*) fail "OpenIGI's Linux build is for x64; this machine is $arch." ;;
    MINGW* | MSYS* | CYGWIN*) fail "On Windows, download the launcher from https://openigi.com." ;;
    *) fail "Unsupported system: $os $arch." ;;
  esac

  # The Linux build is linked against glibc.
  if [ "$os" = Linux ] && { [ -f /lib/libc.musl-x86_64.so.1 ] || ldd /bin/ls 2>&1 | grep -q musl; }; then
    fail "OpenIGI's Linux build needs glibc; this system uses musl (Alpine and the like)."
  fi

  pick_downloader
  need tar
  need awk
  local sha
  if command -v sha256sum >/dev/null 2>&1; then
    sha="sha256sum"
  elif command -v shasum >/dev/null 2>&1; then
    sha="shasum -a 256"
  else
    fail "Neither sha256sum nor shasum is available to check the download."
  fi

  printf '\n%sOpenIGI Launcher installer%s\n\n' "$BOLD" "$RESET"

  local work
  work="$(mktemp -d)"
  # shellcheck disable=SC2064 # expanded now, on purpose
  trap "rm -rf '$work'" EXIT

  # The newest launcher's address is what the site's download link for the runtime holds: a file of its GitHub
  # release, whose tag names the version and which holds the checksums. An older one is asked for by name.
  local url=""
  if [ "$target" = latest ]; then
    step "Finding the latest release"
    download "$site/$rid/url" "$work/url" && url="$(tr -d '\r\n' < "$work/url")"
    [ -n "$url" ] || fail "Could not find the latest launcher for $rid (is $site reachable?)."
  else
    step "Finding release $target"
    local extension=""
    [ "$os" = Darwin ] && extension=".tar.gz"
    url="$releases/launcher-v$target/openigi-launcher-$target-$rid$extension"
  fi
  local file="${url##*/}"
  local folder="${url%/*}"
  # The folder is the GitHub release's tag (launcher-v0.4.1).
  local version="${folder##*/launcher-}"
  version="${version#v}"
  # Anything else (an error page, a moved store) stops here rather than becoming a path.
  [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+ ]] || fail "The download service answered with something unexpected: $url"
  done_step "$name $version for $rid"

  # --- Download and verify ----------------------------------------------------------------------------

  step "Downloading"
  local hint=""
  [ "$target" != latest ] && hint=" (is $target a released launcher version?)"
  download "$url" "$work/$file" progress || fail "The download failed$hint."
  done_step "$file"

  step "Verifying"
  download "$folder/SHA256SUMS" "$work/SHA256SUMS" || fail "Could not fetch the release's checksums."
  local expected actual
  expected="$(awk -v f="$file" '$2 == f || $2 == "*" f { print $1 }' "$work/SHA256SUMS")"
  [[ "$expected" =~ ^[0-9a-f]{64}$ ]] || fail "The release's checksums do not list $file."
  actual="$(cd "$work" && $sha "$file" | awk '{ print $1 }')"
  [ "$expected" = "$actual" ] || fail "Checksum mismatch for $file: expected $expected, got $actual."
  done_step "SHA-256 matches"

  # --- Install ----------------------------------------------------------------------------------------

  step "Installing"
  case "$rid" in
    osx-*) install_macos "$work" "$file" "$name" ;;
    linux-*) install_linux "$work" "$file" "$name" ;;
  esac
}

install_macos() {
  local work="$1" file="$2" name="$3"
  local app="$name.app"

  # /Applications when it can be written without sudo (an administrator's account), else the user's own.
  local dest="${OPENIGI_INSTALL_DIR:-/Applications}"
  if [ -z "${OPENIGI_INSTALL_DIR:-}" ] && [ ! -w "$dest" ]; then
    dest="$HOME/Applications"
  fi
  mkdir -p "$dest" || fail "Could not create $dest."

  tar -xzf "$work/$file" -C "$work" || fail "Could not unpack $file."
  [ -d "$work/$app" ] || fail "The download did not hold $app."

  # Quit a running copy first: its files are about to be replaced.
  osascript -e "quit app \"$name\"" >/dev/null 2>&1 || true
  rm -rf "${dest:?}/$app"
  mv "$work/$app" "$dest/" || fail "Could not move $app into $dest."

  # A copy dragged in earlier from a browser download keeps its quarantine; clear it either way.
  xattr -dr com.apple.quarantine "$dest/$app" 2>/dev/null || true
  done_step "$dest/$app"

  printf '\n%s%s is installed.%s It is in %s and in Launchpad.\n' "$GREEN" "$name" "$RESET" "$dest"
  if [ "${OPENIGI_NO_LAUNCH:-}" != 1 ]; then
    open "$dest/$app" >/dev/null 2>&1 || true
    printf 'Starting it now: it finds your copy of Project I.G.I. and installs the game.\n\n'
  else
    printf 'Open it to find your copy of Project I.G.I. and install the game.\n\n'
  fi
}

install_linux() {
  local work="$1" file="$2" name="$3"
  local bin_dir="${OPENIGI_INSTALL_DIR:-$HOME/.local/bin}"
  local exe="$bin_dir/openigi-launcher"

  mkdir -p "$bin_dir" || fail "Could not create $bin_dir."
  # Renamed into place in one step, so a launcher already running keeps its own file.
  cp "$work/$file" "$exe.new" && chmod 755 "$exe.new" || fail "Could not write $exe."
  mv -f "$exe.new" "$exe"
  done_step "$exe"

  local apps="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
  if mkdir -p "$apps" 2>/dev/null; then
    cat > "$apps/openigi-launcher.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=$name
Comment=Installs, updates and starts OpenIGI
Exec="$exe"
Terminal=false
Categories=Game;
EOF
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$apps" >/dev/null 2>&1 || true
    step "Added to the applications menu"
    done_step ""
  fi

  printf '\n%s%s is installed.%s Start it from the applications menu, or run:\n\n' "$GREEN" "$name" "$RESET"
  printf '    %sopenigi-launcher%s\n\n' "$BOLD" "$RESET"
  case ":$PATH:" in
    *":$bin_dir:"*) ;;
    *)
      printf '%sNote:%s %s is not on your PATH. Add it to your shell profile:\n\n' "$YELLOW" "$RESET" "$bin_dir"
      printf '    export PATH="%s:$PATH"\n\n' "$bin_dir"
      ;;
  esac
}

# Under sudo, $HOME is usually root's: the launcher would be installed for root, owned by root, and not
# be found from the user's own shell. Plain root (a container) is left alone.
refuse_sudo() {
  if [ "$(id -u)" -eq 0 ] && [ -n "${SUDO_USER:-}" ] && [ "$SUDO_USER" != root ] && [ -z "${OPENIGI_INSTALL_ALLOW_SUDO:-}" ]; then
    printf '%serror:%s do not run this installer with sudo.\n\n' "$RED" "$RESET" >&2
    printf 'The launcher installs for your own account and does not need root. Run it again without sudo:\n\n' >&2
    printf '    curl -fsSL https://openigi.com/install.sh | bash\n\n' >&2
    printf 'To install it for root on purpose, set OPENIGI_INSTALL_ALLOW_SUDO=1.\n' >&2
    exit 1
  fi
}

# curl where there is one, wget otherwise (a minimal Linux install may have only one of them).
pick_downloader() {
  if command -v curl >/dev/null 2>&1; then
    DOWNLOADER=curl
  elif command -v wget >/dev/null 2>&1; then
    DOWNLOADER=wget
  else
    fail "Either curl or wget is required."
  fi
}

# download <url> <file> [progress]
download() {
  local show=""
  [ "${3:-}" = progress ] && [ -t 2 ] && show=1
  if [ "$DOWNLOADER" = curl ]; then
    if [ -n "$show" ]; then
      printf '\n'
      curl -fL --progress-bar -o "$2" "$1"
    else
      curl -fsSL -o "$2" "$1"
    fi
  else
    if [ -n "$show" ]; then
      printf '\n'
      wget -q --show-progress -O "$2" "$1" 2>/dev/null || wget -q -O "$2" "$1"
    else
      wget -q -O "$2" "$1"
    fi
  fi
}

setup_colours() {
  BOLD="" GREEN="" YELLOW="" RED="" DIM="" RESET=""
  if [ -t 1 ] && [ -z "${NO_COLOR:-}" ] && [ "$(tput colors 2>/dev/null || echo 0)" -ge 8 ]; then
    BOLD="$(tput bold)"
    GREEN="$(tput setaf 2)"
    YELLOW="$(tput setaf 3)"
    RED="$(tput setaf 1)"
    DIM="$(tput dim 2>/dev/null || true)"
    RESET="$(tput sgr0)"
  fi
}

step() { printf '%s==>%s %s' "$BOLD" "$RESET" "$1"; }
done_step() { printf ' %s%s%s\n' "$DIM" "$1" "$RESET"; }
fail() {
  printf '\n%serror:%s %s\n' "${RED:-}" "${RESET:-}" "$1" >&2
  exit 1
}
need() { command -v "$1" >/dev/null 2>&1 || fail "$1 is required but not installed."; }

main "$@"
