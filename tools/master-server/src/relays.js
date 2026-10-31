// The TURN relays co-op rooms are handed, and which one each pair of players gets.
//
// A relay is coturn on a machine we run or one a community member runs for us; every one of them sees the addresses
// of the players it relays and how much they send (never what: the game's data channel is DTLS end to end). So relays
// are only ever added by us, through the admin routes, never by registering themselves. Each has its own secret, held
// here encrypted under RELAY_KEY, and its logins are minted from that secret alone: a secret leaked by one operator
// unlocks that one relay and nothing else.
//
// The EU relay is built in, from TURN_HOST/TURN_SECRET as before: it can't be removed, and it is every room's last
// resort, handed out even when its probe fails, because it is what every room used before there were others.
//
// Which relay: each player reaches the other through its own relay allocation, so a relay is chosen for the pair, as
// the one with the shortest way host -> relay -> joiner. ICE tries what it is given in priority order, not by latency,
// so the best relay goes first and one fallback after it (libdatachannel's ICE takes two TURN servers at most).
//
// Health: a cron probe from the Worker every minute (STUN binding, then a TURN allocate with a login from the relay's
// own secret). The master judges reachability itself instead of trusting a relay's word, the operator runs nothing
// beside coturn, and a relay given the wrong secret is caught before any player is sent to it.
//
// Adding one (tools/turn/OPERATORS.md is the operator's side), with A="authorization: Bearer $ADMIN_TOKEN":
//   curl -H "$A" -X PUT https://master.openigi.com/v1/admin/relays/mumbai \
//        -d '{"host":"relay.example.in","region":"South Asia","lat":19.1,"lon":72.9,"tlsPort":5349}'
//     -> the relay, disabled, and its secret, shown this once: hand it to the operator.
//   curl -H "$A" https://master.openigi.com/v1/admin/relays            health of each, once the operator is up
//   curl -H "$A" -X PUT .../relays/mumbai -d '{"enabled":true}'          once healthy and OPERATORS.md's UDP check passes
//   curl -H "$A" -X POST .../relays/mumbai/rotate                        a new secret; DELETE .../relays/mumbai to drop

import { connect } from "cloudflare:sockets";
import { probe } from "./stun.js";

const BUILTIN = "eu";

// How long a relay login lasts.
const TURN_TTL_S = 6 * 60 * 60;

// A relay is skipped after this many failed probes in a row, and taken back on its first good one.
const FAILS_TO_DOWN = 2;

// The relay the host already has goes first for a joiner too when it is this close to the best: one relay for both.
const SHARE_RATIO = 1.15;
const SHARE_SLACK_KM = 300;

const ID = /^[a-z0-9][a-z0-9-]{0,31}$/;
const HOSTNAME = /^(?=.{1,253}$)[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)*$/;

export class Relays {
  constructor(ctx, env) {
    this.ctx = ctx;
    this.env = env;
  }

  async fetch(request) {
    const url = new URL(request.url);
    const parts = url.pathname.split("/").filter(Boolean);

    // From the rooms: logins for a room's players, on the relays that suit them.
    if (request.method === "POST" && url.pathname === "/ice") {
      const { who, host, joiner, prefer } = await request.json();
      const relays = await this.relays();
      const order = choose(relays, host, joiner, prefer);
      const ice = {};
      for (const name of who) {
        ice[name] = await iceFor(order, name);
      }

      return json({ relays: order.map((r) => r.id), ice });
    }

    if (request.method === "GET" && url.pathname === "/status") {
      return json((await this.relays()).map((r) => ({ id: r.id, region: r.region, enabled: r.enabled, healthy: r.healthy })));
    }

    if (request.method === "POST" && url.pathname === "/probe") {
      return json(await this.probeAll());
    }

    // The admin routes, already authorised by the Worker: /admin/relays[/<id>[/rotate]].
    if (parts[0] === "admin" && parts[1] === "relays") {
      return this.admin(request, parts[2], parts[3]);
    }

    return json({ error: "not found" }, 404);
  }

  async admin(request, id, action) {
    if (request.method === "GET" && !id) {
      return json({ relays: (await this.relays()).map(adminView) });
    }

    if (request.method === "POST" && id === "probe" && !action) {
      return json(await this.probeAll());
    }

    if (!id || !ID.test(id)) {
      return json({ error: "a relay id is 1-32 of a-z, 0-9 and -" }, 400);
    }

    if (id === BUILTIN) {
      return json({ error: "the built-in relay is set by TURN_HOST and TURN_SECRET" }, 409);
    }

    const stored = await this.ctx.storage.get(`relay:${id}`);
    if (request.method === "DELETE" && !action) {
      await this.ctx.storage.delete([`relay:${id}`, `health:${id}`]);
      return json({ deleted: Boolean(stored) });
    }

    if (request.method === "POST" && action === "rotate") {
      if (!stored) {
        return json({ error: "no such relay" }, 404);
      }

      const secret = newSecret();
      await this.ctx.storage.put(`relay:${id}`, { ...stored, secret: await this.seal(secret) });
      await this.ctx.storage.delete(`health:${id}`);
      return json({ relay: adminView(await this.relay(id)), secret });
    }

    if (request.method === "PUT" && !action) {
      const body = await request.json().catch(() => null);
      const record = validateRelay(body, stored);
      if (typeof record === "string") {
        return json({ error: record }, 400);
      }

      if (!this.env.RELAY_KEY) {
        return json({ error: "RELAY_KEY is not set" }, 503);
      }

      // A new relay's secret is made here and shown once, to be handed to its operator; it starts disabled.
      let secret = typeof body.secret === "string" ? body.secret : null;
      if (secret !== null && secret.length < 16) {
        return json({ error: "secret must be at least 16 characters" }, 400);
      }

      if (!stored && secret === null) {
        secret = newSecret();
      }

      await this.ctx.storage.put(`relay:${id}`, { ...record, id, secret: secret === null ? stored.secret : await this.seal(secret) });
      if (secret !== null || (stored && (stored.host !== record.host || stored.port !== record.port))) {
        await this.ctx.storage.delete(`health:${id}`);
      }

      return json({ relay: adminView(await this.relay(id)), ...(secret !== null && !body.secret ? { secret } : {}) });
    }

    return json({ error: "not found" }, 404);
  }

  /** Every relay with its secret opened and its health: the built-in one first. */
  async relays() {
    const out = [];
    const builtin = this.builtin();
    if (builtin) {
      out.push(builtin);
    }

    const stored = await this.ctx.storage.list({ prefix: "relay:" });
    for (const record of stored.values()) {
      out.push({ ...record, secret: await this.open(record.secret) });
    }

    const health = await this.ctx.storage.get(out.map((r) => `health:${r.id}`));
    for (const r of out) {
      r.health = health.get(`health:${r.id}`) ?? null;
      r.healthy = r.health?.healthy === true;
    }

    return out;
  }

  async relay(id) {
    return (await this.relays()).find((r) => r.id === id);
  }

  builtin() {
    const env = this.env;
    if (!env.TURN_HOST) {
      return null;
    }

    return {
      id: BUILTIN,
      builtin: true,
      host: env.TURN_HOST,
      region: env.TURN_REGION || "Europe",
      lat: Number(env.TURN_LAT ?? 0),
      lon: Number(env.TURN_LON ?? 0),
      port: Number(env.TURN_PORT || 3478),
      tlsPort: env.TURN_TLS_PORT === "" || env.TURN_TLS_PORT === "0" ? null : Number(env.TURN_TLS_PORT || 5349),
      enabled: true,
      secret: env.TURN_SECRET || null,
    };
  }

  async probeAll() {
    const relays = (await this.relays()).filter((r) => r.enabled);
    const results = await Promise.all(
      relays.map(async (r) => {
        const username = `${Math.floor(Date.now() / 1000) + 60}:probe`;
        const error = r.secret
          ? await probe(connect, r, username, await hmacSha1Base64(r.secret, username))
          : "no secret";
        const fails = error ? (r.health?.fails ?? 0) + 1 : 0;
        // Never probed counts as down, so a new relay waits for its first good probe; then it takes two misses.
        const healthy = error ? r.healthy && fails < FAILS_TO_DOWN : true;
        const health = { healthy, fails, at: Date.now(), error };
        await this.ctx.storage.put(`health:${r.id}`, health);
        return { id: r.id, ...health };
      }),
    );

    return { probed: results };
  }

  // Secrets at rest: AES-GCM under RELAY_KEY (64 hex characters), a fresh IV each time.
  async key() {
    const hex = this.env.RELAY_KEY ?? "";
    if (!/^[0-9a-fA-F]{64}$/.test(hex)) {
      throw new Error("RELAY_KEY must be 64 hex characters");
    }

    const bytes = new Uint8Array(hex.match(/../g).map((b) => parseInt(b, 16)));
    return crypto.subtle.importKey("raw", bytes, "AES-GCM", false, ["encrypt", "decrypt"]);
  }

  async seal(secret) {
    const iv = crypto.getRandomValues(new Uint8Array(12));
    const sealed = new Uint8Array(await crypto.subtle.encrypt({ name: "AES-GCM", iv }, await this.key(), new TextEncoder().encode(secret)));
    return toBase64(iv) + "." + toBase64(sealed);
  }

  async open(sealed) {
    try {
      const [iv, data] = sealed.split(".").map(fromBase64);
      return new TextDecoder().decode(await crypto.subtle.decrypt({ name: "AES-GCM", iv }, await this.key(), data));
    } catch {
      // A relay whose secret can't be opened (RELAY_KEY changed) mints no logins and fails its probes.
      return null;
    }
  }
}

/**
 * The relays for a pair, best first: at most two, the second a fallback. `host` and `joiner` are {lat, lon} or null;
 * `prefer` is the relay the host was given when it made the room. The built-in relay is always one of the two.
 */
export function choose(relays, host, joiner, prefer) {
  const builtin = relays.find((r) => r.builtin && r.secret);
  const usable = relays.filter((r) => r.enabled && r.healthy && r.secret);
  const a = host ?? joiner;
  const b = joiner ?? host;
  const score = (r) => (a ? km(a, r) + km(r, b) : r.builtin ? 0 : 1);
  const ranked = usable.map((r) => ({ r, s: score(r) })).sort((x, y) => x.s - y.s);

  let best = ranked[0];
  const preferred = ranked.find((x) => x.r.id === prefer);
  if (best && preferred && preferred.s <= best.s * SHARE_RATIO + SHARE_SLACK_KM) {
    best = preferred;
  }

  const first = best?.r ?? builtin;
  if (!first) {
    return [];
  }

  const second = first === builtin ? ranked.find((x) => x.r !== first)?.r : builtin;
  return second ? [first, second] : [first];
}

/** The ICE servers for one player: STUN on the best relay, then each relay's TURN with a login from its own secret. */
export async function iceFor(order, who) {
  if (order.length === 0) {
    return [];
  }

  const servers = [{ urls: [`stun:${order[0].host}:${order[0].port}`] }];
  for (const relay of order) {
    const username = `${Math.floor(Date.now() / 1000) + TURN_TTL_S}:${who}`;
    // UDP first; then TCP, and TLS for networks that pass nothing that doesn't look like HTTPS, where it has a name.
    const urls = [`turn:${relay.host}:${relay.port}?transport=udp`, `turn:${relay.host}:${relay.port}?transport=tcp`];
    if (relay.tlsPort) {
      urls.push(`turns:${relay.host}:${relay.tlsPort}?transport=tcp`);
    }

    servers.push({ urls, username, credential: await hmacSha1Base64(relay.secret, username) });
  }

  return servers;
}

/** The built-in relay's servers straight from the Worker's env: what a room hands out if the relay list can't answer. */
export async function builtinIce(env, who) {
  if (!env.TURN_HOST) {
    return [];
  }

  const relays = new Relays(null, env);
  const builtin = relays.builtin();
  if (!builtin.secret) {
    return [{ urls: [`stun:${builtin.host}:${builtin.port}`] }];
  }

  return iceFor([builtin], who);
}

function validateRelay(body, stored) {
  if (!body || typeof body !== "object") {
    return "body is not JSON";
  }

  const merged = { ...stored, ...body };
  delete merged.secret;
  if (typeof merged.host !== "string" || !HOSTNAME.test(merged.host)) {
    return "host must be a domain name or an IPv4 address";
  }

  if (typeof merged.region !== "string" || merged.region.length === 0 || merged.region.length > 32) {
    return "region must be a string of 1-32 characters";
  }

  const lat = Number(merged.lat);
  const lon = Number(merged.lon);
  if (!Number.isFinite(lat) || !Number.isFinite(lon) || Math.abs(lat) > 90 || Math.abs(lon) > 180) {
    return "lat must be -90..90 and lon -180..180";
  }

  const port = Number(merged.port ?? 3478);
  const tlsPort = merged.tlsPort === null || merged.tlsPort === undefined ? null : Number(merged.tlsPort);
  if (!validPort(port) || (tlsPort !== null && !validPort(tlsPort))) {
    return "port and tlsPort must be 1-65535 (tlsPort null for a relay without TLS)";
  }

  return {
    host: merged.host,
    region: merged.region,
    lat,
    lon,
    port,
    tlsPort,
    // A new relay waits until we have checked it; an existing one keeps what it was unless told.
    enabled: typeof body.enabled === "boolean" ? body.enabled : stored?.enabled === true,
  };
}

function validPort(port) {
  return Number.isInteger(port) && port >= 1 && port <= 65535;
}

function adminView(r) {
  return {
    id: r.id,
    builtin: r.builtin === true,
    host: r.host,
    region: r.region,
    lat: r.lat,
    lon: r.lon,
    port: r.port,
    tlsPort: r.tlsPort,
    enabled: r.enabled,
    hasSecret: Boolean(r.secret),
    healthy: r.healthy,
    health: r.health,
  };
}

/** Great-circle distance in km. */
export function km(a, b) {
  const rad = Math.PI / 180;
  const dLat = (b.lat - a.lat) * rad;
  const dLon = (b.lon - a.lon) * rad;
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(a.lat * rad) * Math.cos(b.lat * rad) * Math.sin(dLon / 2) ** 2;
  return 2 * 6371 * Math.asin(Math.min(1, Math.sqrt(h)));
}

function newSecret() {
  return [...crypto.getRandomValues(new Uint8Array(32))].map((b) => b.toString(16).padStart(2, "0")).join("");
}

export async function hmacSha1Base64(secret, text) {
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(secret), { name: "HMAC", hash: "SHA-1" }, false, ["sign"]);
  const signature = await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(text));
  return toBase64(new Uint8Array(signature));
}

function toBase64(bytes) {
  return btoa(String.fromCharCode(...bytes));
}

function fromBase64(text) {
  return Uint8Array.from(atob(text), (c) => c.charCodeAt(0));
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { "content-type": "application/json" } });
}
