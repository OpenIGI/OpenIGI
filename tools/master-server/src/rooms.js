// Co-op rooms: how a player hosting from their own game, desktop or browser, is found and reached by friends on either.
//
// A room is one Durable Object, named by its code. It never carries game traffic: the host and each joiner hold a
// WebSocket to it only while they swap what WebRTC needs to connect them directly (offer, answer, ICE candidates), and
// the host keeps its own open for as long as the room lives so joiners can arrive. The game itself then runs over a
// data channel between the two players, or through a TURN relay when their routers won't allow that. Which relay is
// the relay list's choice (relays.js), from where the host and each joiner are; the logins expire.
//
// Hibernating WebSockets: a room spends nearly all its life idle with sockets open, and is only woken by a message.

import { builtinIce } from "./relays.js";

// Codes are read aloud and typed on phones: no 0/O, 1/I/L.
const CODE_ALPHABET = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
const CODE_LENGTH = 6;

// How long a created room waits for its host's socket, and how long a relay login lasts.
const HOST_CONNECT_MS = 60_000;

// How often an open room refreshes its entry in the list, which drops entries 90 s after they were last refreshed.
const PUBLISH_MS = 30_000;

// A signaling message is an SDP or a candidate: a few kilobytes. Anything larger is not one.
const MAX_MESSAGE = 16 * 1024;

// Joiners waiting or connected beyond the room's own player count: a few in the middle of connecting.
const JOINER_SLACK = 4;

const MAX_FIELD = 64;

/**
 * The rooms' routes, under /v1/rooms. `geo` is where the request came from ({lat, lon, country} from Cloudflare, or
 * null): the Worker's to say, since a room only sees the requests the Worker sends it.
 */
export async function handleRooms(request, env, url, geo) {
  const parts = url.pathname.split("/").filter(Boolean); // ["v1", "rooms", code?, "ws"?]

  if (request.method === "POST" && parts.length === 2) {
    const ip = request.headers.get("CF-Connecting-IP") ?? "";
    const body = await request.json().catch(() => null);
    const meta = validateRoom(body);
    if (typeof meta === "string") {
      return json({ error: meta }, 400);
    }

    // A fresh code: one in a billion collides with a live room, and the room says so if it does.
    for (let attempt = 0; attempt < 4; attempt++) {
      const code = newCode();
      const response = await roomStub(env, code).fetch("https://room/create", {
        method: "POST",
        body: JSON.stringify({ ...meta, code, ip, geo }),
      });
      if (response.status !== 409) {
        return response;
      }
    }

    return json({ error: "no free room code" }, 503);
  }

  if (parts.length < 3) {
    return json({ error: "not found" }, 404);
  }

  const code = parts[2].toUpperCase();
  if (!isCode(code)) {
    return json({ error: "no such room" }, 404);
  }

  if (request.method === "GET" && parts.length === 3) {
    return roomStub(env, code).fetch("https://room/info");
  }

  if (request.method === "GET" && parts.length === 4 && parts[3] === "ws") {
    if (request.headers.get("Upgrade") !== "websocket") {
      return json({ error: "expected a WebSocket" }, 426);
    }

    // Where the joiner is, said by the Worker alone: any such header the client sent is replaced.
    const headers = new Headers(request.headers);
    headers.set("x-openigi-geo", JSON.stringify(geo));
    return roomStub(env, code).fetch(new Request(`https://room/ws${url.search}`, { headers }));
  }

  return json({ error: "not found" }, 404);
}

export class Room {
  constructor(ctx, env) {
    this.ctx = ctx;
    this.env = env;
  }

  async fetch(request) {
    const url = new URL(request.url);
    const room = await this.ctx.storage.get("room");

    if (url.pathname === "/create") {
      if (room) {
        return json({ error: "code taken" }, 409);
      }

      const meta = await request.json();
      const hostToken = randomToken();
      const who = `${meta.code}-host`;
      const { relays, ice } = await this.iceServers([who], meta.geo, null, null);
      // The relay the host was handed: a joiner shares it when it is about as good as the best for the pair.
      const created = { ...meta, relay: relays[0] ?? null, hostTokenHash: await sha256(hostToken), hostConnected: false, nextPeer: 1, players: 1 };
      await this.ctx.storage.put("room", created);
      await this.ctx.storage.setAlarm(Date.now() + HOST_CONNECT_MS);
      await this.publish(created);
      return json({ code: meta.code, hostToken, ice: ice[who] });
    }

    if (!room) {
      return json({ error: "no such room" }, 404);
    }

    if (url.pathname === "/info") {
      return json(roomView(room));
    }

    if (url.pathname === "/ws") {
      return this.connect(url, room, readGeo(request.headers.get("x-openigi-geo")));
    }

    return json({ error: "not found" }, 404);
  }

  async connect(url, room, geo) {
    const role = url.searchParams.get("role");
    const pair = new WebSocketPair();
    const [client, server] = Object.values(pair);

    if (role === "host") {
      const token = url.searchParams.get("token") ?? "";
      if (room.hostConnected || (await sha256(token)) !== room.hostTokenHash) {
        return json({ error: "not this room's host" }, 403);
      }

      this.ctx.acceptWebSocket(server, ["host"]);
      server.serializeAttachment({ role: "host" });
      room.hostConnected = true;
      await this.ctx.storage.put("room", room);
      await this.ctx.storage.setAlarm(Date.now() + PUBLISH_MS);
      await this.publish(room);
      server.send(JSON.stringify({ type: "hosting", code: room.code }));
      return new Response(null, { status: 101, webSocket: client });
    }

    if (role === "join") {
      if (!room.hostConnected) {
        return json({ error: "the host isn't here yet" }, 409);
      }

      if (this.ctx.getWebSockets("join").length >= room.maxPlayers - 1 + JOINER_SLACK) {
        return json({ error: "room is full" }, 409);
      }

      const peer = room.nextPeer++;
      await this.ctx.storage.put("room", room);
      this.ctx.acceptWebSocket(server, ["join", `peer:${peer}`]);
      server.serializeAttachment({ role: "join", peer });
      // The joiner and the host each get logins on the relays chosen for the two of them. A host from before relays were
      // chosen per pair ignores the join's ice and keeps the relay it was given when it made the room.
      const joinerWho = `${room.code}-${peer}`;
      const hostWho = `${room.code}-host-${peer}`;
      const { ice } = await this.iceServers([joinerWho, hostWho], room.geo, geo, room.relay);
      server.send(JSON.stringify({ type: "welcome", peer, room: roomView(room), ice: ice[joinerWho] }));
      this.toHost({ type: "join", peer, ice: ice[hostWho] });
      return new Response(null, { status: 101, webSocket: client });
    }

    return json({ error: "role must be host or join" }, 400);
  }

  async webSocketMessage(ws, message) {
    if (typeof message !== "string" || message.length > MAX_MESSAGE) {
      ws.close(1009, "message too large");
      return;
    }

    let data;
    try {
      data = JSON.parse(message);
    } catch {
      ws.close(1007, "not JSON");
      return;
    }

    const from = ws.deserializeAttachment();
    if (from.role === "join") {
      // A joiner only ever talks to the host, about itself.
      if (["offer", "answer", "candidate"].includes(data.type)) {
        this.toHost({ type: data.type, peer: from.peer, data: data.data });
      }

      return;
    }

    // The host: signaling to one joiner, a joiner sent away, or how full the room is now.
    if (["offer", "answer", "candidate"].includes(data.type) && Number.isInteger(data.peer)) {
      this.toPeer(data.peer, { type: data.type, data: data.data });
    } else if (data.type === "kick" && Number.isInteger(data.peer)) {
      for (const socket of this.ctx.getWebSockets(`peer:${data.peer}`)) {
        socket.close(4000, "removed by the host");
      }
    } else if (data.type === "state") {
      const room = await this.ctx.storage.get("room");
      if (room) {
        const players = Number(data.players);
        room.players = Number.isInteger(players) ? Math.max(1, Math.min(players, room.maxPlayers)) : room.players;
        if (typeof data.map === "string" && data.map.length > 0 && data.map.length <= MAX_FIELD) {
          room.map = data.map;
        }

        await this.ctx.storage.put("room", room);
        await this.publish(room);
      }
    }
  }

  async webSocketClose(ws) {
    await this.closed(ws);
  }

  async webSocketError(ws) {
    await this.closed(ws);
  }

  async closed(ws) {
    const from = ws.deserializeAttachment();
    if (from?.role === "join") {
      this.toHost({ type: "leave", peer: from.peer });
      return;
    }

    // The host gone is the room gone: joiners still signaling are told, and the code is free again.
    await this.close("the host left");
  }

  // Before the host connects: it never came for the room it made. After: the room's entry in the list kept fresh, for
  // as long as the host's socket is open.
  async alarm() {
    const room = await this.ctx.storage.get("room");
    if (!room) {
      return;
    }

    if (!room.hostConnected || this.ctx.getWebSockets("host").length === 0) {
      await this.close(room.hostConnected ? "the host left" : "the host never connected");
      return;
    }

    // The next alarm first: a publish that fails -- the list restarting, a moment's error between the two -- must not
    // be the room's last, or it stays open but drops off the list for good once the runtime stops retrying.
    await this.ctx.storage.setAlarm(Date.now() + PUBLISH_MS);
    try {
      await this.publish(room);
    } catch {
      // The next alarm publishes it again.
    }
  }

  async close(reason) {
    const room = await this.ctx.storage.get("room");
    for (const socket of this.ctx.getWebSockets()) {
      try {
        socket.close(4001, reason);
      } catch {
        // Already closing.
      }
    }

    await this.ctx.storage.deleteAlarm();
    await this.ctx.storage.deleteAll();
    if (room) {
      await this.env.SERVER_LIST.get(this.env.SERVER_LIST.idFromName("global")).fetch("https://list/rooms/remove", {
        method: "POST",
        body: JSON.stringify({ code: room.code }),
      });
    }
  }

  toHost(message) {
    for (const socket of this.ctx.getWebSockets("host")) {
      socket.send(JSON.stringify(message));
    }
  }

  toPeer(peer, message) {
    for (const socket of this.ctx.getWebSockets(`peer:${peer}`)) {
      socket.send(JSON.stringify(message));
    }
  }

  // Every room is told to the list, which counts them against the host's address; a public one is also listed there
  // beside the servers reached by address.
  async publish(room) {
    await this.env.SERVER_LIST.get(this.env.SERVER_LIST.idFromName("global")).fetch("https://list/rooms/update", {
      method: "POST",
      // Listed only once its host is there to be joined.
      body: JSON.stringify({
        view: roomView(room),
        ip: room.ip,
        public: room.public && room.hostConnected,
        hosted: room.hostConnected,
        relay: room.relay ?? null,
      }),
    });
  }

  // STUN and TURN logins for each of `who`, on the relays the relay list picks for a host and joiner at those places.
  // If the list can't be reached, the built-in relay as before: a room is never left without one.
  async iceServers(who, host, joiner, prefer) {
    try {
      const response = await this.env.RELAYS.get(this.env.RELAYS.idFromName("global")).fetch("https://relays/ice", {
        method: "POST",
        body: JSON.stringify({ who, host, joiner, prefer }),
      });
      if (response.ok) {
        return await response.json();
      }
    } catch {
      // Below.
    }

    const ice = {};
    for (const name of who) {
      ice[name] = await builtinIce(this.env, name);
    }

    return { relays: this.env.TURN_HOST ? ["eu"] : [], ice };
  }
}

/** A place as the Worker passed it: {lat, lon} to a tenth of a degree, or null. */
export function readGeo(text) {
  try {
    const geo = typeof text === "string" ? JSON.parse(text) : text;
    const lat = Number(geo?.lat);
    const lon = Number(geo?.lon);
    if (geo && Number.isFinite(lat) && Number.isFinite(lon) && Math.abs(lat) <= 90 && Math.abs(lon) <= 180) {
      return { lat: Math.round(lat * 10) / 10, lon: Math.round(lon * 10) / 10 };
    }
  } catch {
    // Not a place.
  }

  return null;
}

function validateRoom(body) {
  if (!body || typeof body !== "object") {
    return "body is not JSON";
  }

  const maxPlayers = Number(body.maxPlayers ?? 4);
  if (!Number.isInteger(maxPlayers) || maxPlayers < 2 || maxPlayers > 32) {
    return "maxPlayers must be 2-32";
  }

  for (const field of ["title", "protocol", "name", "map", "mode", "version"]) {
    if (typeof body[field] !== "string" || body[field].length === 0 || body[field].length > MAX_FIELD) {
      return `${field} must be a string of 1-${MAX_FIELD} characters`;
    }
  }

  return {
    title: body.title,
    protocol: body.protocol,
    name: body.name,
    map: body.map,
    mode: body.mode,
    version: body.version,
    maxPlayers,
    public: body.public === true,
    passworded: body.passworded === true,
  };
}

/** What anyone may see of a room: never the host's address or token. */
export function roomView(room) {
  return {
    room: room.code,
    title: room.title,
    protocol: room.protocol,
    name: room.name,
    map: room.map,
    mode: room.mode,
    version: room.version,
    players: room.players,
    maxPlayers: room.maxPlayers,
    passworded: room.passworded,
  };
}

function roomStub(env, code) {
  return env.ROOMS.get(env.ROOMS.idFromName(code));
}

export function isCode(code) {
  return code.length === CODE_LENGTH && [...code].every((c) => CODE_ALPHABET.includes(c));
}

function newCode() {
  const bytes = crypto.getRandomValues(new Uint8Array(CODE_LENGTH));
  return [...bytes].map((b) => CODE_ALPHABET[b % CODE_ALPHABET.length]).join("");
}

function randomToken() {
  return [...crypto.getRandomValues(new Uint8Array(24))].map((b) => b.toString(16).padStart(2, "0")).join("");
}

async function sha256(text) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { "content-type": "application/json" } });
}
