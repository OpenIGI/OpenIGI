#!/usr/bin/env bash
# Installs the OpenIGI dedicated server as a systemd service, from the folder this script sits in.
#
#   sudo ./install.sh                       asks for everything it needs
#   sudo GAME=/srv/igi NAME="My server" PORT=26001 TITLE=igi1 RCON=secret PUBLIC=1 ./install.sh --yes
#
# It copies the server to /opt/openigi-server, writes /etc/openigi/server.cfg (kept if it is already there), keeps its
# bans and logs in /var/lib/openigi, and runs it as the unprivileged user "openigi", restarted if it ever stops.
# The game's data is not part of the server: point GAME at a copy of your own installation (the folder with the
# game's pc/ data, as the desktop game uses it). Nothing here is downloaded.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PREFIX=/opt/openigi-server
CONFIG_DIR=/etc/openigi
STATE_DIR=/var/lib/openigi
UNIT=/etc/systemd/system/openigi-server.service
YES=0
[ "${1:-}" = "--yes" ] && YES=1

if [ "$(id -u)" -ne 0 ]; then
  echo "Run it as root: sudo $0" >&2
  exit 1
fi

if ! command -v systemctl >/dev/null 2>&1; then
  echo "This installer needs systemd. Without it, run $HERE/OpenIGI.Server -config server.cfg -game <dir> yourself." >&2
  exit 1
fi

ask() {
  # ask <variable> <question> <default>: the environment's value wins; --yes takes the default.
  local var="$1" question="$2" default="$3" value="${!1:-}"
  if [ -z "$value" ]; then
    if [ "$YES" = 1 ]; then
      value="$default"
    else
      read -r -p "$question [$default]: " value
      value="${value:-$default}"
    fi
  fi
  printf -v "$var" '%s' "$value"
}

ask GAME "Where is the game's data (your own install's folder)" "/srv/igi"
ask TITLE "Which game: igi1 or igi2" "igi1"
ask NAME "The server's name" "OpenIGI server"
ask PORT "UDP port" "26001"
ask MAXPLAYERS "Most players (1-32)" "16"
ask RCON "Remote console password (empty: none)" ""
ask PUBLIC "List it in the server browser (1/0)" "1"

if [ ! -d "$GAME" ]; then
  echo "warning: $GAME does not exist yet. Copy your game's folder there before starting the service." >&2
fi

id openigi >/dev/null 2>&1 || useradd --system --home-dir "$STATE_DIR" --shell /usr/sbin/nologin openigi
mkdir -p "$PREFIX" "$CONFIG_DIR" "$STATE_DIR"
cp -a "$HERE"/. "$PREFIX"/
chmod 755 "$PREFIX/OpenIGI.Server"

if [ -f "$CONFIG_DIR/server.cfg" ]; then
  echo "Keeping the settings already in $CONFIG_DIR/server.cfg."
else
  sed -e "s|^name .*|name \"$NAME\"|" \
      -e "s|^port .*|port $PORT|" \
      -e "s|^maxplayers .*|maxplayers $MAXPLAYERS|" \
      -e "s|^rcon_password .*|rcon_password \"$RCON\"|" \
      -e "s|^public .*|public $PUBLIC|" \
      -e "s|^bans .*|bans $STATE_DIR/bans.json|" \
      -e "s|^log .*|log $STATE_DIR/server.log|" \
      "$PREFIX/server.cfg.example" > "$CONFIG_DIR/server.cfg"
  chmod 640 "$CONFIG_DIR/server.cfg"
fi

chown -R openigi:openigi "$STATE_DIR"
chown root:openigi "$CONFIG_DIR/server.cfg"

cat > "$UNIT" <<EOF
[Unit]
Description=OpenIGI dedicated server
After=network-online.target
Wants=network-online.target

[Service]
User=openigi
Group=openigi
WorkingDirectory=$STATE_DIR
ExecStart=$PREFIX/OpenIGI.Server -config $CONFIG_DIR/server.cfg -game "$GAME" -title $TITLE
Restart=on-failure
RestartSec=5
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=$STATE_DIR
PrivateTmp=true

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable --now openigi-server

echo
echo "The server is running:   systemctl status openigi-server"
echo "Its log:                 journalctl -u openigi-server -f   (and $STATE_DIR/server.log)"
echo "Its settings:            $CONFIG_DIR/server.cfg, then: systemctl restart openigi-server"
if command -v ufw >/dev/null 2>&1; then
  echo "Open the port:           ufw allow $PORT/udp"
else
  echo "Open UDP port $PORT in your firewall (and your provider's) for desktop players; browsers join through the room."
fi
