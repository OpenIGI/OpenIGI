# Running an OpenIGI relay

OpenIGI co-op connects players directly when their routers allow it. When they don't, the game sends its traffic
through a TURN relay. Relays near the players keep that path short: two players in Karachi and Delhi relaying through
Mumbai rather than Frankfurt cuts well over 100 ms from every shot. This kit runs a relay (coturn in Docker) on any
Linux VPS. The OpenIGI master server decides which relay each pair of players uses; players never pick one.

Relays are added by the OpenIGI team only. Tell us where yours is and we'll register it. There is no way for a relay
to sign itself up, because a relay sees players' addresses (see [What you can see](#what-you-can-see)).

## What you need

- A Linux VPS with a **static public IPv4 address** and root access. 1 vCPU and 512 MB RAM are plenty.
- Docker with the compose plugin (<https://docs.docker.com/engine/install/>).
- Optional: a domain name pointing at the VPS. With one, the relay also serves TURN over TLS on 5349 for networks
  that block everything that doesn't look like HTTPS. Without one, players reach it over UDP and TCP on 3478, which
  is what nearly all of them use.
- Ports open in **both** the VPS's own firewall and your provider's firewall or security group:

  | Port | Protocol | What for |
  |---|---|---|
  | 3478 | UDP and TCP | STUN and TURN, and the master's health probe (TCP) |
  | 5349 | TCP | TURN over TLS (only with a domain) |
  | 49160–49999 | UDP | One port per relayed player |
  | 80 | TCP | Only with `TLS=certbot`, while Let's Encrypt issues or renews the certificate |

## Bandwidth

A relayed player sends and receives roughly 30–100 kbit/s, and a relay carries each byte in and out again. coturn is
set to cap one session at 512 kbit/s and the whole relay at `BPS_CAPACITY` (48 Mbit/s by default; set it below your
link speed).

- 10 players relayed at once ≈ 1 Mbit/s each way.
- 100 players relayed at once ≈ 10 Mbit/s each way.
- Monthly transfer ≈ average concurrent relayed players × 100 kbit/s × 2.6 million seconds. Five players on average,
  around the clock, is about 160 GB a month out (and the same in). Most VPS plans include several TB.

## Setting up

**The short way:** we send you one file, `openigi-relay-<name>.sh`, with your relay's secret already inside. Copy it to
the machine and run `sudo sh openigi-relay-<name>.sh`. It installs Docker, works out your addresses (including
whether you're behind a home router), asks your upload speed, sets up the relay, and ends by telling you exactly which
ports to forward on your router and what to send back to us. Keep the file private and delete it once the relay is
up. The steps below are the same thing by hand.

1. Send us the VPS's public IP, its domain if it has one, and its city. We register the relay and send you **its
   secret**: a long random string that only this relay uses.
2. On the VPS, copy this folder (`tools/turn`) over, then:

   ```sh
   cp relay.env.example relay.env
   nano relay.env        # PUBLIC_IP, SECRET, and DOMAIN/TLS/EMAIL if you have a domain
   sudo sh install.sh
   ```

   On AWS, Oracle Cloud, GCP and other providers where the VPS has a private address behind a 1:1 NAT, also set
   `PRIVATE_IP` to the address `ip addr` shows.

   Re-running `install.sh` is safe. It keeps the saved secret when `SECRET` is empty, and it is also how you update.
3. Check it from another machine (coturn's `turnutils_uclient` is in the `coturn` package):

   ```sh
   # A login made from your secret, the way the master makes them:
   user="$(( $(date +%s) + 3600 )):selftest"
   pass=$(printf %s "$user" | openssl dgst -sha1 -hmac "YOUR-SECRET" -binary | base64)
   turnutils_uclient -y -u "$user" -w "$pass" -n 10 YOUR-PUBLIC-IP
   ```

   It should report 0 lost packets. A `401` means the secret in `relay.env` isn't the one we sent you, and a timeout
   usually means the UDP port range is closed in your provider's firewall.
4. Tell us it's up. Once the master's probe sees it working (it checks every minute), we run the same UDP check from
   our side, enable it, and confirm it with a real relayed game.

## How we look after it

- The master connects to your relay over TCP 3478 every minute. It makes a STUN request, then a TURN login using your
  secret, and frees that login straight away. You'll see these in coturn's log under the user `<number>:probe`.
- After two failed probes in a row the master stops handing your relay out, and it starts again on the first good
  probe. Players are then sent to the next relay and, in the end, to ours in Frankfurt. Restarts and maintenance are
  fine.
- The probe speaks TCP only, so it can't see a closed UDP port range. Step 3's check covers that; please re-run it
  after any firewall change.
- If we have to, we can disable your relay or change its secret from our side at any time. A changed secret takes
  your relay out of use until you put the new one in `relay.env` and re-run `install.sh`.

## Your secret

- Each player gets a TURN login from the master that expires after six hours. It is made from your relay's secret,
  so anyone holding the secret can make logins for your relay, and use your bandwidth.
- Keep it private: no screenshots or chat pastes, and `relay.env` stays readable by root only (`install.sh` sets
  that). It unlocks **only your relay**. Every relay has its own secret, so a leaked one never exposes anyone else's.
- If you think it has leaked, tell us. We'll issue a new one.

## What you can see

Running a relay means players' traffic passes through your machine. Be clear with yourself about what that means:

- **You can see:** the IP addresses of the players your relay serves, when they connected, for how long, and how
  much data went each way. coturn logs some of this to the container's output, and anyone with root on the VPS can
  capture the rest.
- **You can't see:** what the players are doing in the game, their names, chat, or anything else inside the
  connection. The game's data channel is encrypted end to end between the two players (DTLS), and the relay only
  forwards the encrypted packets.
- Treat player addresses as personal data. Don't publish, share or keep them longer than you need to debug your
  relay, and follow the privacy law where your VPS and you are.

## Stopping

`cd /srv/openigi-turn && docker compose down`, then tell us so we can remove the relay from the list.
