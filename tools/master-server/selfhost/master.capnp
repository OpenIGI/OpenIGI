# The master server on our own machine: the same Worker (../src) run by workerd, Cloudflare's open-source Workers
# runtime, with no daily request allowance. wrangler.toml says the same for Cloudflare; keep the two alike.
#
#   workerd serve selfhost/master.capnp --directory-path storage=<dir>
#
# The Durable Objects (the server list, the rooms, the parties, the relays) keep their storage in <dir>. The secrets
# come from the environment: TURN_SECRET, ADMIN_TOKEN and RELAY_KEY, as `wrangler secret put` gives them on Cloudflare.
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
  ],
  compatibilityDate = "2026-10-01",
  globalOutbound = "internet",

  durableObjectNamespaces = [
    (className = "ServerList", uniqueKey = "openigi-master-ServerList", enableSql = true),
    (className = "Room", uniqueKey = "openigi-master-Room", enableSql = true),
    (className = "Party", uniqueKey = "openigi-master-Party", enableSql = true),
    (className = "Relays", uniqueKey = "openigi-master-Relays", enableSql = true),
  ],
  durableObjectStorage = (localDisk = "storage"),

  bindings = [
    (name = "SERVER_LIST", durableObjectNamespace = "ServerList"),
    (name = "ROOMS", durableObjectNamespace = "Room"),
    (name = "PARTIES", durableObjectNamespace = "Party"),
    (name = "RELAYS", durableObjectNamespace = "Relays"),

    (name = "TURN_HOST", text = "turn.openigi.com"),
    (name = "TURN_REGION", text = "Europe"),
    (name = "TURN_LAT", text = "50.1"),
    (name = "TURN_LON", text = "8.7"),
    # No edge here to keep anything at, and no allowance to spare it for.
    (name = "NO_EDGE_CACHE", text = "1"),

    (name = "TURN_SECRET", fromEnvironment = "TURN_SECRET"),
    (name = "ADMIN_TOKEN", fromEnvironment = "ADMIN_TOKEN"),
    (name = "RELAY_KEY", fromEnvironment = "RELAY_KEY"),
    # Only a test sets these (the built-in relay on another port, where a request says it is).
    (name = "TURN_PORT", fromEnvironment = "TURN_PORT"),
    (name = "TURN_TLS_PORT", fromEnvironment = "TURN_TLS_PORT"),
    (name = "ALLOW_TEST_GEO", fromEnvironment = "ALLOW_TEST_GEO"),
  ],
);
