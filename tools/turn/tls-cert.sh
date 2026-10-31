#!/bin/sh
# Copies the relay's certificate to coturn and tells coturn to reload it (SIGUSR2) when it changed: Caddy's for the
# domain (TLS=caddy, the EU relay beside the web stack) or certbot's (TLS=certbot). Settings from relay.env, as
# install.sh left it. Run daily by openigi-turn-cert.timer; both renew a month before expiry, so a day's lag is harmless.
set -eu
dest=/srv/openigi-turn/tls
DOMAIN= TLS=none
. /srv/openigi-turn/relay.env

case "$TLS" in
  caddy)
    dir=/var/lib/docker/volumes/openigi_caddy-data/_data/caddy/certificates/acme-v02.api.letsencrypt.org-directory/$DOMAIN
    cert=$dir/$DOMAIN.crt
    key=$dir/$DOMAIN.key
    ;;
  certbot)
    cert=/etc/letsencrypt/live/$DOMAIN/fullchain.pem
    key=/etc/letsencrypt/live/$DOMAIN/privkey.pem
    ;;
  *) exit 0 ;;
esac

[ -s "$cert" ] || { echo "no certificate yet at $cert" >&2; exit 1; }
mkdir -p "$dest"
if cmp -s "$cert" "$dest/cert.pem" && cmp -s "$key" "$dest/key.pem"; then
  exit 0
fi

install -o 65534 -g 65534 -m 644 "$cert" "$dest/cert.pem"
install -o 65534 -g 65534 -m 600 "$key" "$dest/key.pem"
docker kill -s USR2 openigi-turn-coturn-1 >/dev/null 2>&1 || true
echo "$DOMAIN certificate updated"
