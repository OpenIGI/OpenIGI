// The OpenIGI master server: the list of running game servers that the in-game browser reads.
//
// A game server heartbeats every 30 seconds and drops off the list 90 seconds after its last one. It is always listed
// at the address its heartbeat came from (CF-Connecting-IP), never at an address it claims, so nobody can list a
// server on someone else's machine. One Durable Object holds the whole list: a few hundred entries at most.

const ENTRY_TTL_MS = 90_000;
const MAX_SERVERS_PER_IP = 8;
const MAX_FIELD = 64;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (request.method === "OPTIONS") {
      return cors(new Response(null, { status: 204 }));
    }

    if (!url.pathname.startsWith("/v1/servers")) {
      return cors(json({ error: "not found" }, 404));
    }

    const list = env.SERVER_LIST.get(env.SERVER_LIST.idFromName("global"));
    return cors(await list.fetch(request));
  },
};

export class ServerList {
  constructor(state) {
    this.state = state;
    this.servers = new Map();
  }

  async fetch(request) {
    const url = new URL(request.url);
    const now = Date.now();
    this.expire(now);

    if (request.method === "GET" && url.pathname === "/v1/servers") {
      const title = url.searchParams.get("title");
      const protocol = url.searchParams.get("protocol");
      const servers = [...this.servers.values()].filter(
        (s) => (!title || s.title === title) && (!protocol || s.protocol === protocol),
      );
      return json({ servers: servers.map(publicView) });
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

      const key = `${ip}|${entry.port}`;
      if (!this.servers.has(key) && countFrom(this.servers, ip) >= MAX_SERVERS_PER_IP) {
        return json({ error: "too many servers from this address" }, 429);
      }

      this.servers.set(key, entry);
      return json({ listed: true, address: ip, port: entry.port, ttl: ENTRY_TTL_MS / 1000 });
    }

    if (request.method === "POST" && url.pathname === "/v1/servers/shutdown") {
      const ip = request.headers.get("CF-Connecting-IP");
      const body = await request.json().catch(() => ({}));
      this.servers.delete(`${ip}|${Number(body.port)}`);
      return json({ listed: false });
    }

    return json({ error: "not found" }, 404);
  }

  expire(now) {
    for (const [key, entry] of this.servers) {
      if (now - entry.lastSeen > ENTRY_TTL_MS) {
        this.servers.delete(key);
      }
    }
  }
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
    lastSeen: now,
  };
}

function publicView(s) {
  const { lastSeen, ...rest } = s;
  return { ...rest, age: Math.round((Date.now() - lastSeen) / 1000) };
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
  headers.set("access-control-allow-headers", "content-type");
  return new Response(response.body, { status: response.status, headers });
}
