#!/usr/bin/env bash
# Puts the master server (../src, run by workerd with master.capnp) on the release VM: no daily request allowance, as
# Cloudflare's free plan has for Durable Objects. Run from anywhere with ssh to the VM:
#
#   tools/master-server/selfhost/deploy.sh [user@host]
#
# The build goes to /srv/openigi/master (the one before kept as master.prev), the compose service `master` is restarted,
# and its storage in /srv/openigi/master-data is kept. Windows tars lose +x, so it is set again on the VM.
#
# Once, before the first deploy:
#   1. /srv/openigi/master.env (root, 600): TURN_SECRET=<coturn's static-auth-secret>, ADMIN_TOKEN=..., RELAY_KEY=<64 hex>,
#      the Worker's own secrets, so relay secrets sealed under RELAY_KEY and admin calls keep working.
#   2. compose.master.yml's service into /srv/openigi/config/compose.yml (and `master` in caddy's depends_on),
#      master.openigi.com.caddy into its Caddyfile.
#   3. openigi-master-probe.{service,timer} into /etc/systemd/system, then `systemctl enable --now openigi-master-probe.timer`.
# Then the cut-over, in Cloudflare: master.openigi.com off the Worker (its custom domain), a proxied A record to the VM,
# and Rules > Transform Rules > Managed Transforms > "Add visitor location headers" on (where a player is, for the
# relays). A relay added through the admin routes is added again on the VM: the Worker's storage stays on Cloudflare.
set -euo pipefail

HOST=${1:-shahriyar@sh-de}
# Two weeks old at least; any version that serves wrangler.toml's compatibility_date.
WORKERD=1.20261031.1

root=$(cd "$(dirname "$0")/.." && pwd)
stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT

mkdir -p "$stage/master/selfhost"
cp -r "$root/src" "$stage/master/"
cp "$root/selfhost/master.capnp" "$root/selfhost/probe.sh" "$stage/master/selfhost/"
(cd "$stage" && npm pack --silent "@cloudflare/workerd-linux-64@$WORKERD" >/dev/null && tar -xzf cloudflare-workerd-linux-64-*.tgz)
cp "$stage/package/bin/workerd" "$stage/master/workerd"
tar -C "$stage" -czf "$stage/master.tgz" master

scp "$stage/master.tgz" "$HOST:/tmp/openigi-master.tgz"
ssh "$HOST" 'set -eu
  test -f /srv/openigi/master.env || { echo "no /srv/openigi/master.env: see deploy.sh" >&2; exit 1; }
  sudo -n rm -rf /srv/openigi/master.new && sudo -n mkdir /srv/openigi/master.new
  sudo -n tar -xzf /tmp/openigi-master.tgz -C /srv/openigi/master.new --strip-components=1
  sudo -n chmod 755 /srv/openigi/master.new/workerd /srv/openigi/master.new/selfhost/probe.sh
  sudo -n mkdir -p /srv/openigi/master-data && sudo -n chown 1655:1655 /srv/openigi/master-data
  sudo -n rm -rf /srv/openigi/master.prev
  if [ -d /srv/openigi/master ]; then sudo -n mv /srv/openigi/master /srv/openigi/master.prev; fi
  sudo -n mv /srv/openigi/master.new /srv/openigi/master
  rm -f /tmp/openigi-master.tgz
  cd /srv/openigi/config && sudo -n docker compose up -d master && sudo -n docker compose restart master
  for i in $(seq 1 30); do curl -fsS -m 2 -o /dev/null http://127.0.0.1:8790/v1/stats && { echo "master up"; exit 0; }; sleep 1; done
  echo "master did not answer" >&2; sudo -n docker compose logs --tail 30 master; exit 1'
