#!/bin/sh
# Copies Caddy's certificate for turn.openigi.com to coturn and tells coturn to reload it (SIGUSR2) when it changed.
# Run daily by openigi-turn-cert.timer; Caddy renews a month before expiry, so a day's lag is harmless.
set -eu
src=/var/lib/docker/volumes/openigi_caddy-data/_data/caddy/certificates/acme-v02.api.letsencrypt.org-directory/turn.openigi.com
dest=/srv/openigi-turn/tls

[ -s "$src/turn.openigi.com.crt" ] || { echo "no certificate yet at $src" >&2; exit 1; }
mkdir -p "$dest"
if cmp -s "$src/turn.openigi.com.crt" "$dest/cert.pem" && cmp -s "$src/turn.openigi.com.key" "$dest/key.pem"; then
  exit 0
fi

install -o 65534 -g 65534 -m 644 "$src/turn.openigi.com.crt" "$dest/cert.pem"
install -o 65534 -g 65534 -m 600 "$src/turn.openigi.com.key" "$dest/key.pem"
docker kill -s USR2 openigi-turn-coturn-1 >/dev/null 2>&1 || true
echo "turn.openigi.com certificate updated"
