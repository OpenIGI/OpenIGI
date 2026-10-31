#!/bin/sh
# Sets up an OpenIGI relay from scratch on a Debian-family Linux box (Raspberry Pi OS, Ubuntu, Debian), at home behind
# a router or on a VPS: installs Docker (and certbot when there's a domain), works out the public and local addresses,
# asks what it can't find out, writes relay.env and runs install.sh. Re-running it is safe.
#
# Usually reached through the single file bundle.sh makes, which sets RELAY_DOMAIN and RELAY_SECRET. From the kit
# folder by hand: sudo RELAY_DOMAIN=relay.example.com RELAY_SECRET=... sh setup.sh
set -eu
here=$(cd "$(dirname "$0")" && pwd)
DOMAIN=${RELAY_DOMAIN:-}
SECRET=${RELAY_SECRET:-}

say() { printf '\n\033[1m%s\033[0m\n' "$*"; }
ask() { # ask VAR "question" default; with no terminal to ask on, the default
  answer=
  # A subshell: a redirection that fails on a builtin ends a POSIX shell, and here only the subshell.
  if ( : < /dev/tty ) 2>/dev/null; then
    printf '%s [%s]: ' "$2" "$3" > /dev/tty
    read -r answer < /dev/tty || answer=
  fi
  eval "$1=\${answer:-\$3}"
}

[ "$(id -u)" = 0 ] || { echo "run it as root: sudo sh $0" >&2; exit 1; }
command -v apt-get >/dev/null || { echo "this script knows apt-based systems (Raspberry Pi OS, Ubuntu, Debian)" >&2; exit 1; }

say "1/5 Software"
if ! command -v docker >/dev/null || ! docker compose version >/dev/null 2>&1; then
  echo "installing Docker..."
  apt-get update -qq
  apt-get install -y -qq curl ca-certificates >/dev/null
  curl -fsSL https://get.docker.com | sh
fi
apt-get install -y -qq curl openssl iproute2 >/dev/null
systemctl enable --now docker >/dev/null 2>&1 || true
echo "Docker $(docker version --format '{{.Server.Version}}' 2>/dev/null || echo '?') ready on $(uname -m)"

say "2/5 Addresses"
public=$(curl -fsS -4 -m 10 https://1.1.1.1/cdn-cgi/trace | sed -n 's/^ip=//p')
local_ip=$(ip -4 route get 1.1.1.1 | awk '{for (i = 1; i < NF; i++) if ($i == "src") print $(i + 1)}')
[ -n "$public" ] || { echo "could not find the public address (no internet?)" >&2; exit 1; }
case "$public" in
  100.6[4-9].*|100.[7-9][0-9].*|100.1[01][0-9].*|100.12[0-7].*)
    echo "the public address $public is a carrier-grade NAT one: this connection can't take incoming players" >&2; exit 1 ;;
esac
echo "public address: $public"
echo "this machine:   $local_ip"
if [ "$public" = "$local_ip" ]; then
  PRIVATE_IP=
  behind_router=no
else
  PRIVATE_IP=$local_ip
  behind_router=yes
  echo "-> behind a router: it will have to forward ports to $local_ip (listed at the end)"
fi

say "3/5 Questions"
[ -n "$SECRET" ] || ask SECRET "The relay secret the OpenIGI team sent you" ""
[ -n "$SECRET" ] || { echo "the secret is needed" >&2; exit 1; }
[ -n "$DOMAIN" ] || ask DOMAIN "Its domain name, if it has one (empty for none)" ""
ask upload "Your internet UPLOAD speed in Mbit/s (a speed test shows it)" "20"
upload=$(printf '%s' "$upload" | tr -cd '0-9')
[ -n "$upload" ] && [ "$upload" -gt 0 ] || upload=20
# Half the upload for the relay, in bytes a second; the rest stays for the house.
BPS_CAPACITY=$((upload * 1000000 / 16))
if [ "$behind_router" = yes ]; then
  # A smaller range is easier to forward on a home router, and 200 relayed players is plenty for one.
  MIN_PORT=49160 MAX_PORT=49359
else
  MIN_PORT=49160 MAX_PORT=49999
fi

TLS=none
if [ -n "$DOMAIN" ]; then
  resolved=$(getent ahostsv4 "$DOMAIN" | awk 'NR == 1 {print $1}')
  if [ "$resolved" != "$public" ]; then
    echo "note: $DOMAIN points at ${resolved:-nothing}, not $public; ask the OpenIGI team to point it here"
  fi
  ask tls "Is TCP port 80 forwarded to this machine, for a TLS certificate? (y/n; n is fine)" "n"
  case "$tls" in
    y*|Y*)
      apt-get install -y -qq certbot >/dev/null
      if [ -s "/etc/letsencrypt/live/$DOMAIN/fullchain.pem" ] ||
        certbot certonly --standalone --non-interactive --agree-tos --register-unsafely-without-email -d "$DOMAIN"; then
        TLS=certbot
      else
        echo "no certificate (port 80 not reaching this machine?): carrying on without TLS, which is fine"
      fi
      ;;
  esac
fi

say "4/5 Installing the relay"
umask 077
cat > "$here/relay.env" <<EOF
PUBLIC_IP=$public
PRIVATE_IP=$PRIVATE_IP
SECRET=$SECRET
DOMAIN=$DOMAIN
TLS=$TLS
EMAIL=
MIN_PORT=$MIN_PORT
MAX_PORT=$MAX_PORT
BPS_CAPACITY=$BPS_CAPACITY
EOF
umask 022
sh "$here/install.sh" "$here/relay.env"

sleep 3
if docker compose -f /srv/openigi-turn/compose.yml ps --status running -q | grep -q .; then
  running=yes
else
  running=no
fi

say "5/5 Done"
if [ "$running" = no ]; then
  echo "coturn is NOT running. Send the OpenIGI team this:"
  docker compose -f /srv/openigi-turn/compose.yml logs --tail 30
  exit 1
fi

echo "coturn is running."
if [ "$behind_router" = yes ]; then
  cat <<EOF

Now on your ROUTER (its admin page, usually http://$(ip -4 route | awk '/^default/ {print $3; exit}')):

  1. Give this machine a fixed address: a "DHCP reservation" for $local_ip.
  2. Forward these ports to $local_ip ("port forwarding" / "virtual server"):
       3478            UDP and TCP
       $MIN_PORT-$MAX_PORT     UDP
EOF
  [ "$TLS" = none ] || echo "       5349            TCP
       80              TCP   (already, for the certificate)"
fi

cat <<EOF

Then send the OpenIGI team this line:

  relay up: public $public, domain ${DOMAIN:-none}, TLS $TLS, upload ${upload} Mbit/s, router $behind_router

They check it from outside and switch it on. If your public address ever changes, run this script again and tell
them the new one.
EOF
