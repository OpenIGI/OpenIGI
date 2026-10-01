#!/usr/bin/env bash
# One-time (and re-runnable) setup of the release VM. Run as root from a checkout of deploy/vm:
#
#   sudo deploy/vm/bootstrap.sh "<ssh public key the release workflow deploys with>"
#
# Creates the deploy user and /srv/openigi, installs the deploy dispatcher where the deploy user cannot
# change it, and pins the workflow's key to that dispatcher. Nothing here touches anything else running
# on the machine; Caddy and the update service come up on the first `deploy.sh config` from CI (or by
# running /usr/local/bin/openigi-deploy config < tarball by hand).
set -euo pipefail

KEY="${1:?usage: bootstrap.sh '<deploy public key>'}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRV=/srv/openigi

[ "$(id -u)" -eq 0 ] || { echo "run as root" >&2; exit 1; }
command -v docker >/dev/null || { echo "docker is required" >&2; exit 1; }

# The deploy user runs the dispatcher and, through the docker group, the compose project. Its key can do
# nothing else (command=…,restrict below), so the docker group is reachable only through those verbs.
id deploy >/dev/null 2>&1 || useradd --system --create-home --shell /bin/bash deploy
usermod -aG docker deploy

install -d -o deploy -g deploy -m 0755 \
  "$SRV" "$SRV/web" "$SRV/web/stable" "$SRV/web/beta" "$SRV/dl" "$SRV/dl/releases" "$SRV/api" "$SRV/config"

# The dispatcher is root-owned: CI can run it but never replace it.
install -o root -g root -m 0755 "$HERE/deploy.sh" /usr/local/bin/openigi-deploy

install -d -o deploy -g deploy -m 0700 ~deploy/.ssh
printf 'command="/usr/local/bin/openigi-deploy",restrict %s\n' "$KEY" > ~deploy/.ssh/authorized_keys
chown deploy:deploy ~deploy/.ssh/authorized_keys
chmod 0600 ~deploy/.ssh/authorized_keys

# A page for each site until its first deploy, so Caddy has something to serve and nothing 404s.
for channel in stable beta; do
  if [ ! -e "$SRV/web/$channel/current" ]; then
    install -d -o deploy -g deploy "$SRV/web/$channel/0.0.0"
    printf '<!doctype html><meta charset="utf-8"><title>OpenIGI</title><p>OpenIGI %s is on its way.</p>\n' "$channel" \
      > "$SRV/web/$channel/0.0.0/index.html"
    chown deploy:deploy "$SRV/web/$channel/0.0.0/index.html"
    ln -sfn 0.0.0 "$SRV/web/$channel/current"
    chown -h deploy:deploy "$SRV/web/$channel/current"
  fi
done

# The api mount must exist as a link before compose first starts, or docker creates a directory in its
# place and the first service deploy cannot swap it. Empty until then; the container restarts until a
# build is deployed into it.
if [ ! -e "$SRV/api/current" ]; then
  install -d -o deploy -g deploy "$SRV/api/0.0.0"
  ln -sfn 0.0.0 "$SRV/api/current"
  chown -h deploy:deploy "$SRV/api/current"
fi

echo "bootstrap done. Next: deploy config, then the update service."
