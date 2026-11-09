// Parties: friends who play together. One player makes a party and hands its code round; the others join it with the
// code. Whatever server or room the party's leader then joins, the members' games join too: each member's game asks
// the party every couple of seconds where its leader plays (GET), and follows.
//
// Each party is a Durable Object of its own, named by its code, as a room is. A member is known by a token handed to it
// when it came in, never by its name, so nobody can speak for another member or move the party without being its leader.
// A member not heard from in a while has left; the oldest member left leads when the leader goes; an empty party is
// forgotten.

const CODE_ALPHABET = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
const CODE_LENGTH = 6;

// How long a member may go without asking before it counts as gone: a game asks every two seconds or so.
const MEMBER_TTL_MS = 30_000;

// Players in one party: enough for a team, few enough that nobody fills a server with a party nobody else can join.
const MAX_MEMBERS = 8;

const MAX_NAME = 32;

// What a party can follow: a room's code, or a server by address as the browser keys it (udp:host:port).
const MAX_TARGET = 96;

// How long a member the leader put out is told so when it asks, rather than only that it isn't in the party.
const KICKED_TTL_MS = 120_000;

/**
 * Serves /v1/parties: making one (POST /v1/parties), and joining, reading, moving and leaving it
 * (/v1/parties/{code}/join, GET /v1/parties/{code}, /v1/parties/{code}/target, /v1/parties/{code}/leave), and its leader
 * putting a member out or handing the lead over (/v1/parties/{code}/kick, /v1/parties/{code}/promote).
 */
export async function handleParties(request, env, url) {
  const parts = url.pathname.split("/").filter(Boolean); // ["v1", "parties", code?, action?]
  if (parts.length === 2 && request.method === "POST") {
    // A new code; one already taken is passed over, as a room's is.
    for (let tries = 0; tries < 8; tries++) {
      const code = newCode();
      const response = await partyStub(env, code).fetch(`https://party/${code}/create`, { method: "POST", body: await bodyText(request) });
      if (response.status !== 409) {
        return response;
      }
    }

    return json({ error: "no free code; try again" }, 503);
  }

  const code = (parts[2] ?? "").toUpperCase();
  if (!isCode(code)) {
    return json({ error: "not a party code" }, 400);
  }

  const action = parts[3] ?? "";
  if (!["", "join", "target", "leave", "kick", "promote"].includes(action) || (action === "" ? request.method !== "GET" : request.method !== "POST")) {
    return json({ error: "not found" }, 404);
  }

  const forward = new URL(`https://party/${code}/${action || "state"}`);
  forward.search = url.search;
  return partyStub(env, code).fetch(forward, { method: request.method, body: request.method === "POST" ? await bodyText(request) : undefined });
}

export class Party {
  constructor(state, env) {
    this.state = state;
    this.env = env;
    this.party = null;
    state.blockConcurrencyWhile(async () => {
      this.party = (await state.storage.get("party")) ?? null;
    });
  }

  async fetch(request) {
    const url = new URL(request.url);
    const action = url.pathname.split("/").filter(Boolean)[1];
    const now = Date.now();
    this.expire(now);

    if (action === "create") {
      if (this.party) {
        return json({ error: "taken" }, 409);
      }

      const body = await readJson(request);
      const code = url.pathname.split("/").filter(Boolean)[0];
      this.party = { code, members: [], target: null, version: 0 };
      const member = this.admit(body?.name, now);
      await this.save();
      return json({ code, member: member.id, token: member.token, ...this.view() });
    }

    if (!this.party) {
      return json({ error: "no such party" }, 404);
    }

    if (action === "join") {
      if (this.party.members.length >= MAX_MEMBERS) {
        return json({ error: "the party is full" }, 403);
      }

      const body = await readJson(request);
      const member = this.admit(body?.name, now);
      await this.save();
      return json({ code: this.party.code, member: member.id, token: member.token, ...this.view() });
    }

    // Everything else is a member speaking for itself, by its token.
    const body = action === "state" ? null : await readJson(request);
    const id = Number(action === "state" ? url.searchParams.get("member") : body?.member);
    const token = String(action === "state" ? url.searchParams.get("token") : body?.token ?? "");
    const member = this.party.members.find((m) => m.id === id && m.token === token);
    if (!member) {
      const kicked = (this.party.kicked ?? []).some((k) => k.id === id && k.token === token);
      return json({ error: kicked ? "the party's leader removed you from it" : "not in the party" }, 403);
    }

    member.seen = now;
    if (action === "state") {
      // Where the member plays, so every member sees who is with the leader.
      const at = cleanTarget(url.searchParams.get("at"));
      if (at !== undefined && at !== member.at) {
        member.at = at;
        member.saved = 0;
      }

      // Asking is what keeps a member in; only written down now and then, as storage writes are counted.
      if (now - (member.saved ?? 0) > MEMBER_TTL_MS / 3) {
        member.saved = now;
        await this.save();
      }

      return json(this.view());
    }

    if (action === "target") {
      if (this.party.members[0] !== member) {
        return json({ error: "only the party's leader moves it" }, 403);
      }

      const target = cleanTarget(body?.target);
      if (target === undefined) {
        return json({ error: "not a room code or a server address" }, 400);
      }

      if (target !== this.party.target) {
        this.party.target = target;
        this.party.version++;
      }

      await this.save();
      return json(this.view());
    }

    if (action === "kick" || action === "promote") {
      if (this.party.members[0] !== member) {
        return json({ error: "only the party's leader does that" }, 403);
      }

      const other = this.party.members.find((m) => m.id === Number(body?.target));
      if (!other || other === member) {
        return json({ error: "no such member" }, 404);
      }

      if (action === "kick") {
        this.party.members = this.party.members.filter((m) => m !== other);
        this.party.kicked = [...(this.party.kicked ?? []).filter((k) => now - k.at < KICKED_TTL_MS), { id: other.id, token: other.token, at: now }];
      } else {
        // The lead handed over: the new leader first, the old one next.
        this.party.members = [other, ...this.party.members.filter((m) => m !== other)];
      }

      await this.save();
      return json(this.view());
    }

    // Leave: the next oldest leads; nobody left, the party is gone.
    this.party.members = this.party.members.filter((m) => m !== member);
    await this.save();
    return json({ left: true });
  }

  async alarm() {
    this.expire(Date.now());
    await this.save();
  }

  // A member in, with an id the party hasn't used and a token only it is told.
  admit(name, now) {
    const id = this.party.members.reduce((most, m) => Math.max(most, m.id), 0) + 1;
    const member = { id, name: cleanName(name, id), token: randomToken(), seen: now, saved: now };
    this.party.members.push(member);
    return member;
  }

  // Members not heard from in a while are gone, and the oldest left leads.
  expire(now) {
    if (this.party) {
      this.party.members = this.party.members.filter((m) => now - m.seen < MEMBER_TTL_MS);
    }
  }

  async save() {
    if (!this.party || this.party.members.length === 0) {
      this.party = null;
      await this.state.storage.deleteAll();
      return;
    }

    await this.state.storage.put("party", this.party);

    // Looked at again once the members could all have gone quiet, so a party everybody walked away from is forgotten.
    await this.state.storage.setAlarm(Date.now() + MEMBER_TTL_MS + 1000);
  }

  // What every member sees: who is in, who leads, and where the leader plays.
  view() {
    return {
      code: this.party.code,
      leader: this.party.members[0]?.id ?? 0,
      members: this.party.members.map((m) => ({ id: m.id, name: m.name, at: m.at ?? null })),
      target: this.party.target,
      version: this.party.version,
    };
  }
}

// A room's code, a server as the browser keys it, or null for nowhere; undefined for anything else.
export function cleanTarget(target) {
  if (target === null || target === undefined || target === "") {
    return null;
  }

  if (typeof target !== "string" || target.length > MAX_TARGET) {
    return undefined;
  }

  if (isCode(target.toUpperCase())) {
    return target.toUpperCase();
  }

  return /^udp:[A-Za-z0-9.\-:[\]]+:\d{1,5}$/.test(target) ? target : undefined;
}

function cleanName(name, id) {
  const text = typeof name === "string" ? name.replace(/[\u0000-\u001f\u007f]/g, "").trim().slice(0, MAX_NAME) : "";
  return text || `Player ${id}`;
}

function partyStub(env, code) {
  return env.PARTIES.get(env.PARTIES.idFromName(code));
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

async function bodyText(request) {
  const text = await request.text();
  return text.length > 4096 ? "{}" : text;
}

async function readJson(request) {
  try {
    return JSON.parse(await request.text());
  } catch {
    return null;
  }
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { "content-type": "application/json" } });
}
