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

import { handleRooms, isCode as isRoomCode, readGeo } from "./rooms.js";
import { handleParties } from "./parties.js";

export { Room } from "./rooms.js";
export { Party } from "./parties.js";
export { Relays } from "./relays.js";

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

export default {
  async fetch(request, env) {
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

    if (url.pathname.startsWith("/v1/admin/")) {
      return cors(await admin(request, env, url));
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

    return cors(await list.fetch(request));
  },

  // Every minute: the relays probed, so one that stops answering stops being handed out.
  async scheduled(event, env, ctx) {
    ctx.waitUntil(relays(env).fetch("https://relays/probe", { method: "POST" }));
  },
};

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

  return readGeo({ lat: request.cf?.latitude, lon: request.cf?.longitude });
}

// The relay list's admin routes, for us alone: a bearer token checked against the Worker secret ADMIN_TOKEN, and no
// routes at all where it isn't set.
async function admin(request, env, url) {
  if (!env.ADMIN_TOKEN || !url.pathname.startsWith("/v1/admin/relays")) {
    return json({ error: "not found" }, 404);
  }

  const given = (request.headers.get("authorization") ?? "").replace(/^Bearer\s+/i, "");
  const digest = async (text) => new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text)));
  if (!crypto.subtle.timingSafeEqual(await digest(given), await digest(env.ADMIN_TOKEN))) {
    return json({ error: "unauthorised" }, 401);
  }

  const path = url.pathname.slice("/v1".length);
  return relays(env).fetch(new Request(`https://relays${path}`, { method: request.method, body: request.body, headers: request.headers }));
}

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
      const servers = [...this.servers.values()].filter(matches).map((s) => publicView(s, this.verified(s)));
      // A dedicated server's own room is the server, listed once.
      const servedRooms = new Set(servers.map((s) => s.room).filter(Boolean));
      const rooms = [...this.rooms.values()].filter((r) => r.public && matches(r.view) && !servedRooms.has(r.view.room)).map((r) => r.view);
      return json({ servers: [...servers, ...rooms] });
    }

    // How busy things are, as counts only: never a private room's code or anyone's address.
    if (request.method === "GET" && url.pathname === "/v1/stats") {
      // The relays' health from the relay list; the counts stand without it if it can't answer.
      let status = [];
      try {
        status = await (await relays(this.env).fetch("https://relays/status")).json();
      } catch {
        // Counts only.
      }

      return json(stats(this.servers, this.rooms, status));
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

      return json({ listed: true, address: ip, port: entry.port, ttl: ENTRY_TTL_MS / 1000, verified: this.verified(entry) });
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
  for (const field of ["port", "wsPort", "title", "protocol", "name", "map", "mode", "version", "maxPlayers", "passworded", "region", "room"]) {
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
export function stats(servers, rooms, relayStatus = []) {
  const tally = () => ({ open: 0, playing: 0, players: 0 });
  const out = {
    rooms: { public: tally(), private: tally(), waiting: 0 },
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
  }

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
  const { lastSeen, savedAt, ...rest } = s;
  return { ...rest, verified, age: Math.round((Date.now() - lastSeen) / 1000) };
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
  headers.set("access-control-allow-methods", "GET, POST, OPTIONS");
  headers.set("access-control-allow-headers", "content-type, x-openigi-token");
  headers.set("access-control-max-age", "86400");
  return new Response(response.body, { status: response.status, headers });
}
