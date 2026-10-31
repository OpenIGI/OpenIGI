#!/bin/sh
# Makes the one file a relay operator runs: this kit packed inside a script, with the relay's domain and secret set,
# that unpacks itself and runs setup.sh. It holds the secret: send it privately, never commit it.
#   sh tools/turn/bundle.sh <domain or ""> <secret> <out-file>
set -eu
here=$(cd "$(dirname "$0")" && pwd)
[ $# -eq 3 ] || { echo "usage: sh bundle.sh <domain or \"\"> <secret> <out-file>" >&2; exit 1; }
domain=$1 secret=$2 out=$3
case "$secret$domain" in *[!A-Za-z0-9._~+=-]*) echo "domain and secret may only hold letters, digits and ._~+=-" >&2; exit 1 ;; esac

{
  cat <<EOF
#!/bin/sh
# OpenIGI relay setup. Run it on the machine that will be the relay: sudo sh $(basename "$out")
# It holds this relay's secret: keep the file private, and delete it once the relay is up.
set -eu
[ "\$(id -u)" = 0 ] || { echo "run it as root: sudo sh \$0" >&2; exit 1; }
kit=\$(mktemp -d)
trap 'rm -rf "\$kit"' EXIT
sed '1,/^__KIT__\$/d' "\$0" | base64 -d | tar -xz -C "\$kit"
RELAY_DOMAIN='$domain' RELAY_SECRET='$secret' sh "\$kit/setup.sh"
exit 0
__KIT__
EOF
  tar -cz -C "$here" compose.yml install.sh setup.sh tls-cert.sh turnserver.conf.template \
    openigi-turn-cert.service openigi-turn-cert.timer relay.env.example OPERATORS.md | base64
} > "$out"
echo "wrote $out"
