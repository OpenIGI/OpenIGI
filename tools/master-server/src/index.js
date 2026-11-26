// The OpenIGI master server: the list of running game servers that the in-game browser reads.
//
// A game server heartbeats every 30 seconds and drops off the list 90 seconds after its last one. It is always listed
// at the address its heartbeat came from (CF-Connecting-IP), never at an address it claims, so nobody can list a
// server on someone else's machine. One Durable Object holds the whole list: a few hundred entries at most. The list is
// kept in the object's storage too, so a restart of the object does not empty the browser until the next heartbeats.
//
// A Worker can't send UDP, so the master can't ping a server's game port itself: a server is listed as "verified" only
// when the room browsers join it through is open and hosted from the same address as its heartbeat. The rest the
// browser checks for itself, with the info query it sends every listed server.

import { handleRooms, isCode as isRoomCode, netOf, readGeo } from "./rooms.js";
import { handleParties } from "./parties.js";
import { handleMaps } from "./maps.js";
import { handleAccounts, isAccountsPath } from "./accounts.js";
import { handleStats, isStatsPath, officialName } from "./stats.js";
import { handleRuns, isRunsAdminPath, isRunsPath } from "./runs.js";
import { isAdmin } from "./signed.js";
import { cleanName } from "./names.js";

export { Room } from "./rooms.js";
export { Party } from "./parties.js";
export { Relays } from "./relays.js";
export { Maps } from "./maps.js";
export { Accounts } from "./accounts.js";
export { Stats } from "./stats.js";
export { Runs } from "./runs.js";

const ENTRY_TTL_MS = 90_000;

// A server's entry is written to storage when it is new or changed, and otherwise at most this often: storage writes are
// counted, a heartbeat comes every 30 seconds, and an entry restored a minute stale still outlives the gap to the next.
const SAVE_EVERY_MS = 60_000;
const MAX_SERVERS_PER_IP = 8;
const MAX_FIELD = 64;

// The regions a server may say it plays from (server.cfg's region).
const REGIONS = ["eu", "na", "sa", "asia", "oce", "af", "me"];

// Live rooms one address may have open at once, public or not.
const MAX_ROOMS_PER_IP = 4;

// The parties one address may make in a minute.
const MAX_PARTIES_PER_MINUTE = 6;

// The seconds a GET of the list or the stats is kept at the edge, so a browser asking every few seconds -- every player
// on the multiplayer page -- costs the list's Durable Object one request a few seconds per Cloudflare location, not one
// each. Durable Object requests are counted, and the free plan has 100,000 a day.
const EDGE_SECONDS = { "/v1/servers": 5, "/v1/stats": 15 };

export default {
  async fetch(request, env, ctx) {
    try {
      return await route(request, env, ctx);
    } catch (error) {
      // An uncaught throw is Cloudflare's bare "error code: 1101"; a game is told something it can read instead.
      return cors(fallback(request, env, error) ?? failure(error));
    }
  },

  // Every minute: the relays probed, so one that stops answering stops being handed out.
  async scheduled(event, env, ctx) {
    ctx.waitUntil(relays(env).fetch("https://relays/probe", { method: "POST" }));
  },
};

async function route(request, env, ctx) {
    const url = new URL(request.url);
    if (request.method === "OPTIONS") {
      return cors(new Response(null, { status: 204 }));
    }

    const list = env.SERVER_LIST.get(env.SERVER_LIST.idFromName("global"));
    if (url.pathname === "/v1/rooms" && request.method === "POST") {
      // An address with too many rooms open already is turned away before a code is spent on it.
      const ip = request.headers.get("CF-Connecting-IP") ?? "";
      const reserve = await list.fetch("https://list/rooms/reserve", { method: "POST", body: JSON.stringify({ ip }) });
      if (!reserve.ok) {
        return cors(reserve);
      }
    }

    if (url.pathname === "/v1/parties" && request.method === "POST") {
      // And an address making parties faster than anyone gathers friends is turned away: each is an object kept.
      const ip = request.headers.get("CF-Connecting-IP") ?? "";
      const reserve = await list.fetch("https://list/parties/reserve", { method: "POST", body: JSON.stringify({ ip }) });
      if (!reserve.ok) {
        return cors(reserve);
      }
    }

    // A heartbeat signed by a registered server's key (stats.js) lists the server as official. The list takes the word
    // from here alone: whatever a heartbeat says of it itself is dropped.
    if (url.pathname === "/v1/servers/heartbeat" && request.method === "POST") {
      const body = new Uint8Array(await request.arrayBuffer());
      const headers = new Headers(request.headers);
      headers.delete(OFFICIAL_HEADER);
      const official = await officialName(request, url, body, env);
      if (official) headers.set(OFFICIAL_HEADER, encodeURIComponent(official));
      return cors(await list.fetch(new Request(request.url, { method: "POST", headers, body })));
    }

    if (url.pathname.startsWith("/v1/admin/")) {
      return cors(await admin(request, env, url));
    }

    // Community maps: published, listed, downloaded, rated and reported (maps.js).
    if (url.pathname === "/v1/maps" || url.pathname.startsWith("/v1/maps/")) {
      return cors(await handleMaps(request, env));
    }

    // Match results from the servers whose matches count, and what they add up to: records and leaderboards (stats.js).
    if (isStatsPath(url.pathname)) {
      return cors(await handleStats(request, env));
    }

    // Single-player runs: submitted, checked by the game itself, and the mission boards they make (runs.js).
    if (isRunsPath(url.pathname)) {
      return cors(await handleRuns(request, env));
    }

    // Accounts: a player's keys linked to their Discord account, and the profiles that come of it (accounts.js).
    if (isAccountsPath(url.pathname)) {
      return cors(await handleAccounts(request, env));
    }

    if (url.pathname === "/v1/parties" || url.pathname.startsWith("/v1/parties/")) {
      return cors(await handleParties(request, env, url));
    }

    if (url.pathname.startsWith("/v1/rooms")) {
      const response = await handleRooms(request, env, url, geoOf(request, env));
      // A WebSocket upgrade is passed back as it is; headers can't be added to it.
      return response.status === 101 ? response : cors(response);
    }

    if (!url.pathname.startsWith("/v1/servers") && url.pathname !== "/v1/stats") {
      return cors(json({ error: "not found" }, 404));
    }

    // The stats asked from an owner's address (OWNER_IPS, a secret: addresses apart by commas) list every room, private
    // ones too. Never kept at the edge, where the next asker would be handed them.
    if (request.method === "GET" && url.pathname === "/v1/stats" && isOwner(request, env)) {
      const answer = await list.fetch("https://list/stats/owner");
      const own = new Response(answer.body, answer);
      own.headers.set("cache-control", "private, no-store");
      return cors(own);
    }

    const seconds = request.method === "GET" ? EDGE_SECONDS[url.pathname] : undefined;
    if (seconds) {
      return cors(await kept(request, env, ctx, seconds, () => list.fetch(request)));
    }

    return cors(await list.fetch(request));
}

// Whether a request comes from one of the owner's addresses. CF-Connecting-IP is Cloudflare's word for it, and nothing
// but Cloudflare's edge reaches the master, so a visitor cannot say it for itself.
export function isOwner(request, env) {
  const ip = (request.headers.get("CF-Connecting-IP") ?? "").trim().toLowerCase();
  return ip.length > 0 && String(env.OWNER_IPS ?? "").split(",").some((own) => own.trim().toLowerCase() === ip);
}

// A GET answered from this location's cache while it is fresh, and otherwise asked and kept. NO_EDGE_CACHE=1 (the tests'
// env file, and the self-hosted master, which has no edge) skips that cache: the self-hosted one keeps its answers in its
// own memory instead (MEMO_CACHE=1, remembered), and a test asks every time, so it sees a change at once.
async function kept(request, env, ctx, seconds, ask) {
  if (env.NO_EDGE_CACHE === "1") {
    return env.MEMO_CACHE === "1" ? remembered(request.url, seconds, ask) : ask();
  }

  const cache = caches.default;
  const key = new Request(request.url, { method: "GET" });
  const hit = await cache.match(key);
  if (hit) {
    return hit;
  }

  const answer = await ask();
  if (!answer.ok) {
    return answer;
  }

  const keep = new Response(answer.body, answer);
  keep.headers.set("cache-control", `public, max-age=${seconds}`);
  ctx.waitUntil(cache.put(key, keep.clone()));
  return keep;
}

// The answers a self-hosted master keeps in its own memory, by address: the one process every request comes to is the cache,
// with nothing between to run (a separate store would be one more hop to the same machine). While an answer is fresh every
// asker is handed it; once it is stale, the first asker fetches it again and every asker that comes meanwhile waits for that
// one fetch rather than making its own, so a page full of visitors costs the list one request every few seconds.
const memo = new Map();
const MEMO_LIMIT = 64;

/** An answer kept in memory for some seconds, asked once however many want it at the same moment. */
export async function remembered(key, seconds, ask, now = () => Date.now()) {
  const entry = memo.get(key);
  if (entry?.kept && entry.until > now()) return reply(entry.kept, seconds);
  if (entry?.pending) return reply(await entry.pending, seconds);

  const pending = (async () => {
    const answer = await ask();
    return { status: answer.status, type: answer.headers.get("content-type") ?? "application/json", body: await answer.text() };
  })();
  if (memo.size >= MEMO_LIMIT && !memo.has(key)) memo.clear();
  memo.set(key, { ...entry, pending });
  try {
    const got = await pending;
    if (got.status === 200) memo.set(key, { kept: got, until: now() + seconds * 1000 });
    else memo.delete(key);
    return reply(got, seconds);
  } catch (error) {
    memo.delete(key);
    throw error;
  }
}

/** Forgets every kept answer: for the tests, between cases. */
export function forgetRemembered() {
  memo.clear();
}

// A kept answer as a response of its own: a fresh one marked for the browser (and Cloudflare in front) to keep as long.
function reply(got, seconds) {
  return new Response(got.body, {
    status: got.status,
    headers: { "content-type": got.type, "cache-control": got.status === 200 ? `public, max-age=${seconds}` : "no-store" },
  });
}

// What a throw becomes. Out of Durable Object requests (the free plan's daily allowance, back at 00:00 UTC) or an object
// overloaded is a 503 that says when to come back; anything else a 500.
function failure(error) {
  const message = String(error?.message ?? error);
  console.error(message);
  if (/Durable Objects free tier/i.test(message)) {
    const now = new Date();
    const midnight = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + 1);
    return withRetry(json({ error: "the master server is out of capacity for today; try again later" }, 503), Math.ceil((midnight - now) / 1000));
  }

  if (/overloaded/i.test(message)) {
    return withRetry(json({ error: "the master server is busy; try again shortly" }, 503), 10);
  }

  return json({ error: "internal error" }, 500);
}

// Out of Durable Object requests, the list and the stats are still answered: the servers named in FALLBACK_SERVERS
// (wrangler.toml), which a game then asks over UDP for what they really play and who is on them. Rooms and parties live
// in Durable Objects and have no stand-in until the allowance is back.
function fallback(request, env, error) {
  const path = new URL(request.url).pathname;
  if (request.method !== "GET" || !(path in EDGE_SECONDS) || !/Durable Objects free tier/i.test(String(error?.message ?? error))) {
    return null;
  }

  let named = [];
  try {
    named = JSON.parse(env.FALLBACK_SERVERS ?? "[]");
  } catch {
    // None.
  }

  const url = new URL(request.url);
  const title = url.searchParams.get("title");
  // The browser marks a line of another protocol unjoinable; the asked one is echoed, as the game's own handshake still
  // turns a mismatch away.
  const protocol = url.searchParams.get("protocol");
  const entries = named
    .filter((s) => !title || s.title === title)
    .map((s) => ({ wsPort: 0, players: 0, passworded: false, room: null, ...s, protocol: protocol ?? s.protocol ?? "", lastSeen: Date.now() }));
  if (path === "/v1/stats") {
    return json({ ...stats(new Map(entries.map((s) => [`${s.address}:${s.port}`, s])), new Map(), []), degraded: true });
  }

  return json({ servers: entries.map((s) => ({ ...publicView(s, false), degraded: true })) });
}

function withRetry(response, seconds) {
  response.headers.set("retry-after", String(seconds));
  return response;
}

function relays(env) {
  return env.RELAYS.get(env.RELAYS.idFromName("global"));
}

// Where a request came from, to a tenth of a degree, as Cloudflare places it. `wrangler dev` may set ALLOW_TEST_GEO
// to let a test say where it is (?testGeo=lat,lon); the deployed Worker never does.
function geoOf(request, env) {
  const test = new URL(request.url).searchParams.get("testGeo");
  if (env.ALLOW_TEST_GEO === "1" && test) {
    const [lat, lon] = test.split(",");
    return readGeo({ lat, lon });
  }

  // Run on our own machine behind Cloudflare's proxy (selfhost/), there is no request.cf: the zone's "add visitor location
  // headers" transform says the same, and the origin takes requests from Cloudflare's edge alone, so nobody else says it.
  return readGeo({
    lat: request.cf?.latitude ?? request.headers.get("cf-iplatitude"),
    lon: request.cf?.longitude ?? request.headers.get("cf-iplongitude"),
  });
}

// The admin routes, for us alone: a bearer token checked against the secret ADMIN_TOKEN, and no routes at all where it
// isn't set. The relay list's, and the stats' list of the servers whose matches count.
async function admin(request, env, url) {
  const relayRoute = url.pathname.startsWith("/v1/admin/relays");
  const statsRoute = url.pathname.startsWith("/v1/admin/stats-servers");
  const runsRoute = isRunsAdminPath(url.pathname);
  if (!env.ADMIN_TOKEN || (!relayRoute && !statsRoute && !runsRoute)) {
    return json({ error: "not found" }, 404);
  }

  if (!(await isAdmin(request, env))) {
    return json({ error: "unauthorised" }, 401);
  }

  if (statsRoute) {
    return handleStats(request, env);
  }

  if (runsRoute) {
    return handleRuns(request, env);
  }

  const path = url.pathname.slice("/v1".length);
  return relays(env).fetch(new Request(`https://relays${path}`, { method: request.method, body: request.body, headers: request.headers }));
}

// The header the Worker tells the list a heartbeat's server is official by: its registered name, URI-encoded.
const OFFICIAL_HEADER = "x-openigi-official";

export class ServerList {
  constructor(state, env) {
    this.state = state;
    this.env = env;
    this.servers = new Map();
    // Rooms by code, public or not: the public ones are listed, and all of them count against their host's address.
    this.rooms = new Map();
    // The servers listed before the object last stopped, back before any request is served.
    state.blockConcurrencyWhile(async () => {
      const saved = await state.storage.list({ prefix: "server:" });
      for (const [key, entry] of saved) {
        this.servers.set(key.slice("server:".length), entry);
      }
    });
  }

  async fetch(request) {
    const url = new URL(request.url);
    const now = Date.now();
    await this.expire(now);

    if (request.method === "GET" && url.pathname === "/v1/servers") {
      const title = url.searchParams.get("title");
      const protocol = url.searchParams.get("protocol");
      const matches = (s) => (!title || s.title === title) && (!protocol || s.protocol === protocol);
      // A server names the room browsers join it through; a room that has since closed is not passed on, or every browser
      // sent to it would be told there is no such room.
      const servers = [...this.servers.values()]
        .filter(matches)
        .map((s) => publicView(s.room !== null && !this.rooms.has(s.room) ? { ...s, room: null } : s, this.verified(s)));
      // A dedicated server's own room is the server, listed once.
      const servedRooms = new Set(servers.map((s) => s.room).filter(Boolean));
      const rooms = [...this.rooms.values()].filter((r) => r.public && matches(r.view) && !servedRooms.has(r.view.room)).map((r) => r.view);
      // Names are their hosts' own typing, and the list is on the website as well as in every game: a slur is masked (names.js).
      return json({ servers: [...servers, ...rooms].map((entry) => ({ ...entry, name: cleanName(entry.name) })) });
    }

    // How busy things are, as counts only: never a private room's code or anyone's address.
    // /stats/owner is the Worker's own asking for an owner's address (OWNER_IPS), never routed from outside: the rooms
    // listed too.
    const owner = url.pathname === "/stats/owner";
    if (request.method === "GET" && (url.pathname === "/v1/stats" || owner)) {
      // The relays' health from the relay list; the counts stand without it if it can't answer.
      let status = [];
      try {
        status = await (await relays(this.env).fetch("https://relays/status")).json();
      } catch {
        // Counts only.
      }

      return json(stats(this.servers, this.rooms, status, owner));
    }

    // Reached only from the Worker and the rooms, never routed from outside (only /v1/servers and /v1/stats are).
    if (request.method === "POST" && url.pathname === "/parties/reserve") {
      const { ip } = await request.json();
      this.partiesMade ??= new Map();
      const recent = (this.partiesMade.get(ip) ?? []).filter((at) => now - at < 60_000);
      if (ip && recent.length >= MAX_PARTIES_PER_MINUTE) {
        this.partiesMade.set(ip, recent);
        return json({ error: "too many parties from this address; wait a minute" }, 429);
      }
      recent.push(now);
      this.partiesMade.set(ip, recent);
      if (this.partiesMade.size > 4096) {
        for (const [who, made] of this.partiesMade) {
          if (made.every((at) => now - at >= 60_000)) {
            this.partiesMade.delete(who);
          }
        }
      }
      return json({ ok: true });
    }

    if (request.method === "POST" && url.pathname === "/rooms/reserve") {
      const { ip } = await request.json();
      const open = [...this.rooms.values()].filter((r) => r.ip === ip).length;
      return open >= MAX_ROOMS_PER_IP ? json({ error: "too many rooms from this address" }, 429) : json({ ok: true });
    }

    if (request.method === "POST" && url.pathname === "/rooms/update") {
      const { view, ip, public: listed, hosted, relay } = await request.json();
      this.rooms.set(view.room, { view, ip, public: listed === true, hosted: hosted === true, relay: relay ?? null, lastSeen: now });
      return json({ ok: true });
    }

    if (request.method === "POST" && url.pathname === "/rooms/remove") {
      const { code } = await request.json();
      this.rooms.delete(code);
      return json({ ok: true });
    }

    if (request.method === "POST" && url.pathname === "/v1/servers/heartbeat") {
      const ip = request.headers.get("CF-Connecting-IP");
      if (!ip) {
        return json({ error: "no client address" }, 400);
      }

      let body;
      try {
        body = await request.json();
      } catch {
        return json({ error: "body is not JSON" }, 400);
      }

      const entry = validate(body, ip, now);
      if (typeof entry === "string") {
        return json({ error: entry }, 400);
      }

      // Its registered name, when the Worker found its heartbeat signed by a registered server's key.
      const official = request.headers.get(OFFICIAL_HEADER);
      entry.official = official ? decodeURIComponent(official) : null;

      // A room is claimed only from the address that hosts it: nobody lists another's room as their server.
      const claimed = entry.room === null ? undefined : this.rooms.get(entry.room);
      if (claimed && claimed.ip !== ip) {
        return json({ error: "that room is hosted from another address" }, 403);
      }

      const key = `${ip}|${entry.port}`;
      const before = this.servers.get(key);
      if (!before && countFrom(this.servers, ip) >= MAX_SERVERS_PER_IP) {
        return json({ error: "too many servers from this address" }, 429);
      }

      this.servers.set(key, entry);
      if (!before || changed(before, entry) || now - (before.savedAt ?? 0) >= SAVE_EVERY_MS) {
        entry.savedAt = now;
        await this.state.storage.put(`server:${key}`, entry);
      } else {
        entry.savedAt = before.savedAt;
      }

      return json({ listed: true, address: ip, port: entry.port, ttl: ENTRY_TTL_MS / 1000, verified: this.verified(entry), official: entry.official !== null });
    }

    if (request.method === "POST" && url.pathname === "/v1/servers/shutdown") {
      const ip = request.headers.get("CF-Connecting-IP");
      const body = await request.json().catch(() => ({}));
      const key = `${ip}|${Number(body.port)}`;
      if (this.servers.delete(key)) {
        await this.state.storage.delete(`server:${key}`);
      }

      return json({ listed: false });
    }

    return json({ error: "not found" }, 404);
  }

  async expire(now) {
    const gone = [];
    for (const map of [this.servers, this.rooms]) {
      for (const [key, entry] of map) {
        if (now - entry.lastSeen > ENTRY_TTL_MS) {
          map.delete(key);
          if (map === this.servers) {
            gone.push(`server:${key}`);
          }
        }
      }
    }

    // Storage takes at most 128 keys a delete.
    for (let i = 0; i < gone.length; i += 128) {
      await this.state.storage.delete(gone.slice(i, i + 128));
    }
  }

  // Whether the master could see the server is real: the room it names is open, hosted, from the heartbeat's address.
  verified(entry) {
    const room = entry.room === null ? undefined : this.rooms.get(entry.room);
    return room !== undefined && room.hosted && room.ip === entry.address;
  }
}

// Whether an entry differs from the one before in anything but its time and its player count: worth writing again.
function changed(before, after) {
  for (const field of ["port", "wsPort", "title", "protocol", "name", "map", "mode", "version", "net", "maxPlayers", "passworded", "region", "room", "official"]) {
    if (before[field] !== after[field]) {
      return true;
    }
  }

  return false;
}

function validate(body, ip, now) {
  const port = Number(body.port);
  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    return "port must be 1-65535";
  }

  const wsPort = body.wsPort === undefined ? null : Number(body.wsPort);
  if (wsPort !== null && (!Number.isInteger(wsPort) || wsPort < 1 || wsPort > 65535)) {
    return "wsPort must be 1-65535";
  }

  const players = Number(body.players ?? 0);
  const maxPlayers = Number(body.maxPlayers ?? 0);
  if (!Number.isInteger(players) || !Number.isInteger(maxPlayers) || players < 0 || maxPlayers < 1 || maxPlayers > 64) {
    return "players/maxPlayers out of range";
  }

  // Where it plays from, for the browser's filter: one of the listed regions, or none said.
  const region = body.region === undefined || body.region === "" ? null : String(body.region).toLowerCase();
  if (region !== null && !REGIONS.includes(region)) {
    return `region must be one of ${REGIONS.join(", ")}`;
  }

  // The room browsers join it through, which the list shows once, as this server, not again as a room of its own.
  const room = body.room === undefined || body.room === "" ? null : String(body.room).toUpperCase();
  if (room !== null && !isRoomCode(room)) {
    return "room is not a room code";
  }

  for (const field of ["title", "protocol", "name", "map", "mode", "version"]) {
    if (typeof body[field] !== "string" || body[field].length === 0 || body[field].length > MAX_FIELD) {
      return `${field} must be a string of 1-${MAX_FIELD} characters`;
    }
  }

  return {
    address: ip,
    port,
    wsPort,
    title: body.title,
    protocol: body.protocol,
    name: body.name,
    map: body.map,
    mode: body.mode,
    version: body.version,
    net: netOf(body.net),
    players: Math.min(players, maxPlayers),
    maxPlayers,
    passworded: body.passworded === true,
    region,
    room,
    lastSeen: now,
  };
}

// A room counts once its host is connected (one still waiting for its host is only "waiting"), and as playing once
// anyone has joined its host. Each relay counts the rooms whose host it was handed first, and their players: what the
// master chose, not what ICE ended up using, which only the players' games know.
// A dedicated server's own room is the server: counted under servers (as the list shows it once), though its relay still
// counts it. The servers are listed too, as the server browser lists them, by name -- never an address, port or room code.
//
// For the owner (`rooms.list`, asked for with `detailed`) each hosted room is listed as well, private ones included, by
// name, map and mode -- still never its code or its host's address, so the list lets nobody in.
export function stats(servers, rooms, relayStatus = [], detailed = false) {
  const tally = () => ({ open: 0, playing: 0, players: 0 });
  const out = {
    rooms: { public: tally(), private: tally(), waiting: 0, ...(detailed ? { list: [] } : {}) },
    servers: { open: 0, playing: 0, players: 0, list: [] },
    byTitle: {},
    relays: {},
  };
  for (const r of relayStatus) {
    if (r.enabled) {
      out.relays[r.id] = { region: r.region, healthy: r.healthy, rooms: 0, players: 0 };
    }
  }

  const served = new Set([...servers.values()].map((s) => s.room).filter(Boolean));
  for (const r of rooms.values()) {
    if (!r.hosted) {
      out.rooms.waiting++;
      continue;
    }

    const players = r.view.players ?? 1;
    if (r.relay && out.relays[r.relay]) {
      out.relays[r.relay].rooms++;
      out.relays[r.relay].players += players;
    }

    if (served.has(r.view.room)) {
      continue;
    }

    const title = (out.byTitle[r.view.title] ??= { rooms: 0, servers: 0, players: 0 });
    title.rooms++;
    title.players += players;
    const kind = r.public ? out.rooms.public : out.rooms.private;
    kind.open++;
    kind.players += players;
    if (players > 1) {
      kind.playing++;
    }

    out.rooms.list?.push({
      name: r.view.name,
      title: r.view.title,
      version: r.view.version,
      mode: r.view.mode,
      map: r.view.map,
      players,
      maxPlayers: r.view.maxPlayers,
      public: r.public,
      passworded: r.view.passworded,
      relay: r.relay,
    });
  }

  out.rooms.list?.sort((a, b) => b.players - a.players || String(a.name).localeCompare(String(b.name)));

  for (const s of servers.values()) {
    const title = (out.byTitle[s.title] ??= { rooms: 0, servers: 0, players: 0 });
    title.servers++;
    title.players += s.players;
    out.servers.open++;
    out.servers.players += s.players;
    if (s.players > 0) {
      out.servers.playing++;
    }

    out.servers.list.push({
      name: s.name,
      title: s.title,
      version: s.version,
      mode: s.mode,
      map: s.map,
      players: s.players,
      maxPlayers: s.maxPlayers,
      region: s.region,
      passworded: s.passworded,
    });
  }

  // Busiest first, then by name, so the list reads the same between two looks.
  out.servers.list.sort((a, b) => b.players - a.players || String(a.name).localeCompare(String(b.name)));
  return out;
}

function publicView(s, verified) {
  const { lastSeen, savedAt, official, ...rest } = s;
  return { ...rest, verified, official: Boolean(official), age: Math.round((Date.now() - lastSeen) / 1000) };
}

function countFrom(servers, ip) {
  let count = 0;
  for (const entry of servers.values()) {
    if (entry.address === ip) {
      count++;
    }
  }

  return count;
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { "content-type": "application/json" } });
}

function cors(response) {
  const headers = new Headers(response.headers);
  headers.set("access-control-allow-origin", "*");
  headers.set("access-control-allow-methods", "GET, POST, PUT, DELETE, OPTIONS");
  headers.set("access-control-allow-headers", "content-type, x-openigi-token, x-openigi-key, x-openigi-signature, x-openigi-time");
  headers.set("access-control-max-age", "86400");
  return new Response(response.body, { status: response.status, headers });
}
