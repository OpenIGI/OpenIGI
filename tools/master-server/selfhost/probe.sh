#!/bin/sh
# What wrangler.toml's cron does on Cloudflare: the relays probed, through the admin route, as the timer runs it.
set -eu
ADMIN_TOKEN=$(sed -n 's/^ADMIN_TOKEN=//p' /srv/openigi/master.env)
exec curl -fsS -m 50 -o /dev/null -X POST -H "authorization: Bearer $ADMIN_TOKEN" http://127.0.0.1:8790/v1/admin/relays/probe
