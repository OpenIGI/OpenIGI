#!/bin/sh
# Installs or updates coturn on the VM: copies compose.yml, writes turnserver.conf from the template with the shared
# secret (made once, kept in /srv/openigi-turn/secret), opens the firewall, and (re)starts the container.
# Run on the VM from a folder holding this script, compose.yml and turnserver.conf.template: sudo sh install.sh
set -eu
here=$(cd "$(dirname "$0")" && pwd)
dest=/srv/openigi-turn

mkdir -p "$dest"
if [ ! -s "$dest/secret" ]; then
  umask 077
  openssl rand -hex 32 > "$dest/secret"
fi

cp "$here/compose.yml" "$dest/compose.yml"
sed "s/@SECRET@/$(cat "$dest/secret")/" "$here/turnserver.conf.template" > "$dest/turnserver.conf"
# The image runs coturn as nobody; it has to read its config, nobody else does.
chown 65534:65534 "$dest/turnserver.conf"
chmod 600 "$dest/turnserver.conf" "$dest/secret"

install -m 755 "$here/tls-cert.sh" "$dest/tls-cert.sh"
install -m 644 "$here/openigi-turn-cert.service" "$here/openigi-turn-cert.timer" /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now openigi-turn-cert.timer
# The certificate comes from the web stack's Caddy (turn.openigi.com.caddy appended to its Caddyfile); until it has
# one, coturn starts without TLS.
"$dest/tls-cert.sh" || true

ufw allow 3478/udp comment 'openigi turn'
ufw allow 3478/tcp comment 'openigi turn'
ufw allow 5349/tcp comment 'openigi turns'
ufw allow 49160:49999/udp comment 'openigi turn relay'

cd "$dest"
docker compose pull
docker compose up -d --force-recreate
