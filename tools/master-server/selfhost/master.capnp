# The master server on our own machine: the same Worker (../src) run by workerd, Cloudflare's open-source Workers
# runtime, with no daily request allowance. wrangler.toml says the same for Cloudflare; keep the two alike.
#
#   workerd serve selfhost/master.capnp --directory-path storage=<dir>
#
# The Durable Objects (the server list, the rooms, the parties, the relays) keep their storage in <dir>. The secrets
# come from the environment: TURN_SECRET, ADMIN_TOKEN and RELAY_KEY, as `wrangler secret put` gives them on Cloudflare,
# and for signing in with Discord DISCORD_CLIENT_ID, DISCORD_CLIENT_SECRET and ACCOUNT_KEY (32 random bytes, base64).
# Cloudflare's cron (the relays probed every minute) is openigi-master-probe.timer here, through the admin route.

using Workerd = import "/workerd/workerd.capnp";

const config :Workerd.Config = (
  services = [
    (name = "master", worker = .master),
    (name = "storage", disk = (writable = true)),
    # The relay probe's TCP connections (cloudflare:sockets) go out through this.
    (name = "internet", network = (allow = ["public"], tlsOptions = (trustBrowserCas = true))),
  ],
  sockets = [(name = "http", address = "*:8790", http = (), service = "master")],
);

const master :Workerd.Worker = (
  modules = [
    (name = "index.js", esModule = embed "../src/index.js"),
    (name = "rooms.js", esModule = embed "../src/rooms.js"),
    (name = "parties.js", esModule = embed "../src/parties.js"),
    (name = "relays.js", esModule = embed "../src/relays.js"),
    (name = "stun.js", esModule = embed "../src/stun.js"),
    (name = "maps.js", esModule = embed "../src/maps.js"),
    (name = "signed.js", esModule = embed "../src/signed.js"),
    (name = "accounts.js", esModule = embed "../src/accounts.js"),
    (name = "stats.js", esModule = embed "../src/stats.js"),
    (name = "medals.js", esModule = embed "../src/medals.js"),
  ],
  compatibilityDate = "2026-10-01",
  globalOutbound = "internet",

  durableObjectNamespaces = [
    (className = "ServerList", uniqueKey = "openigi-master-ServerList", enableSql = true),
    (className = "Room", uniqueKey = "openigi-master-Room", enableSql = true),
    (className = "Party", uniqueKey = "openigi-master-Party", enableSql = true),
    (className = "Relays", uniqueKey = "openigi-master-Relays", enableSql = true),
    (className = "Maps", uniqueKey = "openigi-master-Maps", enableSql = true),
    (className = "Accounts", uniqueKey = "openigi-master-Accounts", enableSql = true),
    (className = "Stats", uniqueKey = "openigi-master-Stats", enableSql = true),
  ],
  durableObjectStorage = (localDisk = "storage"),

  bindings = [
    (name = "SERVER_LIST", durableObjectNamespace = "ServerList"),
    (name = "ROOMS", durableObjectNamespace = "Room"),
    (name = "PARTIES", durableObjectNamespace = "Party"),
    (name = "RELAYS", durableObjectNamespace = "Relays"),
    # The community maps, every version's package kept in its storage.
    (name = "MAPS", durableObjectNamespace = "Maps"),
    # Player accounts: Discord users and the keys they linked (accounts.js).
    (name = "ACCOUNTS", durableObjectNamespace = "Accounts"),
    # Match results from the listed servers, and the records and boards they add up to (stats.js).
    (name = "STATS", durableObjectNamespace = "Stats"),

    (name = "TURN_HOST", text = "turn.openigi.com"),
    (name = "TURN_REGION", text = "Europe"),
    (name = "TURN_LAT", text = "50.1"),
    (name = "TURN_LON", text = "8.7"),
    # No edge here to keep anything at, and no allowance to spare it for.
    (name = "NO_EDGE_CACHE", text = "1"),

    (name = "TURN_SECRET", fromEnvironment = "TURN_SECRET"),
    (name = "ADMIN_TOKEN", fromEnvironment = "ADMIN_TOKEN"),
    (name = "RELAY_KEY", fromEnvironment = "RELAY_KEY"),
    # The addresses /v1/stats lists every room for, private ones too, apart by commas; unset, nobody's.
    (name = "OWNER_IPS", fromEnvironment = "OWNER_IPS"),
    # Signing in with Discord (accounts.js); ACCOUNT_KEY seals the refresh tokens kept for Linked Roles.
    (name = "DISCORD_CLIENT_ID", fromEnvironment = "DISCORD_CLIENT_ID"),
    (name = "DISCORD_CLIENT_SECRET", fromEnvironment = "DISCORD_CLIENT_SECRET"),
    (name = "ACCOUNT_KEY", fromEnvironment = "ACCOUNT_KEY"),
    (name = "PUBLIC_URL", text = "https://master.openigi.com"),
    # Only a test sets these (the built-in relay on another port, where a request says it is).
    (name = "TURN_PORT", fromEnvironment = "TURN_PORT"),
    (name = "TURN_TLS_PORT", fromEnvironment = "TURN_TLS_PORT"),
    (name = "ALLOW_TEST_GEO", fromEnvironment = "ALLOW_TEST_GEO"),
  ],
);
