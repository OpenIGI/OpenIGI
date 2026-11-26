#!/usr/bin/env bash
# Puts a release's checker for the mission boards on the VM: the dedicated server of that release, run as
# `OpenIGI.Server -verify-worker` (openigi-verifier@<version>.service), beside the checker of the release before it. Runs
# play again only in the build that recorded them, so two builds are checked at once and older ones are taken down.
#
#   tools/server/verifier/deploy.sh <version> [user@host]
#   TARBALL=<server tarball> tools/server/verifier/deploy.sh <name> [user@host]
#
# The second form puts up a build of one's own (tools/release/package.sh server linux-x64 0.0.0-dev), checked under <name>:
# a development build's runs carry its commit in their version, and only a checker of that very commit checks them.
#
# The release's server tarball (linux, the VM's own architecture) is downloaded on the VM and checked against the digest
# GitHub gives for it, unpacked to /opt/openigi-verifier/<version>, and its service started; every checker but the two
# newest is stopped and removed.
#
# Once, before the first deploy (as root on the VM):
#   1. useradd --system --home-dir /var/lib/openigi-verifier --shell /usr/sbin/nologin openigi   (if the server's
#      installer has not made it), mkdir -p /var/lib/openigi-verifier && chown openigi: /var/lib/openigi-verifier
#   2. /etc/openigi/verifier.env (644): IGI_GAME_PATH=<Project I.G.I.'s data> and IGI2_GAME_PATH=<I.G.I. 2's data>,
#      readable by openigi: a copy of the game servers' own, e.g. under /srv/openigi/verifier/games (a home folder is
#      not readable by it, and the unit sees home folders read-only at most).
#   3. After the first start, its public key is in its log ("Checking runs for ... as <id>: <key>"); register it once:
#        curl -fsS -X POST -H "Authorization: Bearer $ADMIN_TOKEN" https://master.openigi.com/v1/admin/run-verifiers \
#             -d '{"key":"<key>","name":"sh-de"}'
#      Every build's checker shares the one key (/var/lib/openigi-verifier/verifier.key), so this is done once.
set -euo pipefail

VERSION=${1:?usage: deploy.sh <version> [user@host]}
HOST=${2:-shahriyar@sh-de}
REPO=OpenIGI/OpenIGI
KEEP=2

here=$(cd "$(dirname "$0")" && pwd)
case "$VERSION" in
  *[!0-9A-Za-z.-]* | "") echo "a checker's name is letters, digits, dots and dashes" >&2; exit 1 ;;
esac

if [ -n "${TARBALL:-}" ]; then
  # A build of one's own: sent from here, its digest taken here.
  asset=$(basename "$TARBALL")
  digest=$(sha256sum "$TARBALL" | cut -d' ' -f1)
  scp "$TARBALL" "$HOST:/tmp/$asset"
else
  arch=$(ssh "$HOST" uname -m)
  case "$arch" in
    x86_64) rid=linux-x64 ;;
    aarch64 | arm64) rid=linux-arm64 ;;
    *) echo "no server build for $arch" >&2; exit 1 ;;
  esac

  asset="openigi-server-$VERSION-$rid.tar.gz"
  digest=$(gh api "repos/$REPO/releases/tags/v$VERSION" --jq ".assets[] | select(.name == \"$asset\") | .digest")
  case "$digest" in
    sha256:*) digest=${digest#sha256:} ;;
    *) echo "v$VERSION has no $asset with a digest" >&2; exit 1 ;;
  esac
fi

scp "$here/openigi-verifier@.service" "$HOST:/tmp/openigi-verifier@.service"
ssh "$HOST" "VERSION='$VERSION' ASSET='$asset' DIGEST='$digest' REPO='$REPO' KEEP='$KEEP' LOCAL='${TARBALL:+1}' bash -s" <<'REMOTE'
set -euo pipefail
test -f /etc/openigi/verifier.env || { echo "no /etc/openigi/verifier.env: see deploy.sh" >&2; exit 1; }
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

if [ -n "$LOCAL" ]; then
  mv "/tmp/$ASSET" "$work/$ASSET"
else
  curl -fsSL -o "$work/$ASSET" "https://github.com/$REPO/releases/download/v$VERSION/$ASSET"
fi
echo "$DIGEST  $work/$ASSET" | sha256sum -c -

sudo -n install -m 644 /tmp/openigi-verifier@.service /etc/systemd/system/openigi-verifier@.service
rm -f /tmp/openigi-verifier@.service
sudo -n rm -rf "/opt/openigi-verifier/$VERSION"
sudo -n mkdir -p "/opt/openigi-verifier/$VERSION"
sudo -n tar -xzf "$work/$ASSET" -C "/opt/openigi-verifier/$VERSION" --strip-components=1
sudo -n chmod 755 "/opt/openigi-verifier/$VERSION/OpenIGI.Server"
sudo -n systemctl daemon-reload
sudo -n systemctl enable --now "openigi-verifier@$VERSION"

# The two newest builds checked; the rest taken down.
for old in $(ls -1 /opt/openigi-verifier | sort -V | head -n -"$KEEP"); do
  sudo -n systemctl disable --now "openigi-verifier@$old" || true
  sudo -n rm -rf "/opt/openigi-verifier/$old"
  echo "took down the checker of $old"
done

sleep 3
systemctl is-active --quiet "openigi-verifier@$VERSION" || { sudo -n journalctl -u "openigi-verifier@$VERSION" -n 30 --no-pager; exit 1; }
sudo -n journalctl -u "openigi-verifier@$VERSION" -n 20 --no-pager | grep -m1 "Checking runs" || true
echo "checker $VERSION up"
REMOTE
