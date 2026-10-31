#!/bin/sh
# Installs or updates an OpenIGI relay (coturn in Docker) on a Linux machine: writes turnserver.conf from the template
# with the settings in a relay.env (see relay.env.example; eu.relay.env is ours in Frankfurt) and the relay's secret,
# sets up its TLS certificate if it has a domain, opens the firewall, and (re)starts the container.
# Run as root from a folder holding this kit: sudo sh install.sh [relay.env]
set -eu
here=$(cd "$(dirname "$0")" && pwd)
dest=/srv/openigi-turn
settings=${1:-$here/relay.env}

[ -s "$settings" ] || { echo "no settings at $settings: copy relay.env.example to relay.env and fill it in" >&2; exit 1; }
PUBLIC_IP= PRIVATE_IP= SECRET= DOMAIN= TLS=none EMAIL= MIN_PORT=49160 MAX_PORT=49999 BPS_CAPACITY=6000000
. "$settings"
[ -n "$PUBLIC_IP" ] || { echo "PUBLIC_IP is not set in $settings" >&2; exit 1; }
[ -n "$DOMAIN" ] || TLS=none
case "$TLS" in none|caddy|certbot) ;; *) echo "TLS must be none, caddy or certbot" >&2; exit 1 ;; esac
command -v docker >/dev/null || { echo "Docker is needed: https://docs.docker.com/engine/install/" >&2; exit 1; }

mkdir -p "$dest"
umask 077
# The secret we were given, or the one kept from last time; a relay of our own may make its own.
if [ -n "$SECRET" ]; then
  printf '%s\n' "$SECRET" > "$dest/secret"
elif [ ! -s "$dest/secret" ]; then
  openssl rand -hex 32 > "$dest/secret"
  echo "made a new secret in $dest/secret: the master must be given the same one"
fi
secret=$(tr -d '\n' < "$dest/secret")
case "$secret" in *[!A-Za-z0-9._~+=-]*) echo "the secret may only hold letters, digits and ._~+=-" >&2; exit 1 ;; esac
umask 022

# Behind a 1:1 NAT coturn listens on the private address and tells players the public one.
if [ -n "$PRIVATE_IP" ]; then
  listen=$PRIVATE_IP
  external="$PUBLIC_IP/$PRIVATE_IP"
else
  listen=$PUBLIC_IP
  external=$PUBLIC_IP
fi

sed -e "s|@LISTEN_IP@|$listen|" -e "s|@EXTERNAL_IP@|$external|" -e "s|@SECRET@|$secret|" \
  -e "s|@MIN_PORT@|$MIN_PORT|" -e "s|@MAX_PORT@|$MAX_PORT|" -e "s|@BPS_CAPACITY@|$BPS_CAPACITY|" \
  "$here/turnserver.conf.template" > "$dest/turnserver.conf.new"
if [ "$TLS" = none ]; then
  # No domain, no certificate: the TLS block goes, and coturn is told not to listen for TLS at all.
  sed -i -e '/@TLS_BEGIN@/,/@TLS_END@/d' "$dest/turnserver.conf.new"
  printf 'no-tls\n' >> "$dest/turnserver.conf.new"
else
  sed -i -e '/@TLS_BEGIN@/d' -e '/@TLS_END@/d' "$dest/turnserver.conf.new"
fi
mv "$dest/turnserver.conf.new" "$dest/turnserver.conf"
# The image runs coturn as nobody; it has to read its config, nobody else does.
chown 65534:65534 "$dest/turnserver.conf"
chmod 600 "$dest/turnserver.conf" "$dest/secret"

cp "$here/compose.yml" "$dest/compose.yml"
cp "$settings" "$dest/relay.env"
chmod 600 "$dest/relay.env"

if [ "$TLS" != none ]; then
  if [ "$TLS" = certbot ] && [ ! -s "/etc/letsencrypt/live/$DOMAIN/fullchain.pem" ]; then
    command -v certbot >/dev/null || { echo "TLS=certbot needs certbot (apt install certbot)" >&2; exit 1; }
    if [ -n "$EMAIL" ]; then contact="-m $EMAIL"; else contact=--register-unsafely-without-email; fi
    # shellcheck disable=SC2086
    certbot certonly --standalone --non-interactive --agree-tos $contact -d "$DOMAIN"
  fi

  install -m 755 "$here/tls-cert.sh" "$dest/tls-cert.sh"
  install -m 644 "$here/openigi-turn-cert.service" "$here/openigi-turn-cert.timer" /etc/systemd/system/
  systemctl daemon-reload
  systemctl enable --now openigi-turn-cert.timer
  # Until there is a certificate, coturn starts without TLS.
  "$dest/tls-cert.sh" || true
fi

if command -v ufw >/dev/null; then
  ufw allow 3478/udp comment 'openigi turn'
  ufw allow 3478/tcp comment 'openigi turn'
  [ "$TLS" = none ] || ufw allow 5349/tcp comment 'openigi turns'
  ufw allow "$MIN_PORT:$MAX_PORT/udp" comment 'openigi turn relay'
else
  echo "no ufw: open 3478/udp, 3478/tcp$([ "$TLS" = none ] || echo ', 5349/tcp') and $MIN_PORT-$MAX_PORT/udp yourself (and in your provider's firewall)"
fi

cd "$dest"
docker compose pull
docker compose up -d --force-recreate
echo "relay running on $PUBLIC_IP${DOMAIN:+ ($DOMAIN)}"
