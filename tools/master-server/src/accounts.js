// Player accounts: a Discord user and the installations' keys they linked to it. Signing in is optional -- a player who
// never links plays as their key alone, as before -- and a linked key gives the game servers, the site and the bot a
// name, an avatar and (later) stats that follow the player from install to install.
//
// Linking, from a game:
//   1. The game asks POST /v1/link, signed by its player key (signed.js); it is handed a short code and a URL.
//   2. The player opens the URL (/v1/auth/discord/start?code=...), which sends them on to Discord to approve.
//   3. Discord sends them back to /v1/auth/discord/callback; the key is linked to their Discord account there.
//   4. Meanwhile the game asks GET /v1/link/<code>, signed by the same key, until it is linked.
//
// An account's id is its Discord user id. Everything is kept in one Durable Object ("all"), in key-value storage:
//   account:<id>     { id, name, avatar, created, keys: [playerId] }
//   key:<playerId>   { account, label, added }
//   link:<code>      { player, label, expires, account? }
//   pending:<player> the code a key is waiting on (one at a time)
//   oauth:<state>    { code, expires }
//   token:<id>       the Discord refresh token, sealed with ACCOUNT_KEY (AES-GCM); kept only when that secret is set
//
// The secrets and settings, from the environment: DISCORD_CLIENT_ID, DISCORD_CLIENT_SECRET, ACCOUNT_KEY (32 bytes,
// base64) and PUBLIC_URL (https://master.openigi.com unless said).

import { Refusal, json, signedBy } from "./signed.js";

const LINK_SECONDS = 15 * 60;
const CODE_ALPHABET = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
const CODE_LENGTH = 8;
const MAX_KEYS = 20;
const MAX_LOOKUP = 64;
const MAX_LABEL = 40;
const MAX_NAME = 32;
const DISCORD = "https://discord.com";
const DISCORD_API = "https://discord.com/api/v10";
const SCOPES = "identify role_connections.write";

// The Worker's side: every account route goes to the one object that keeps them.
export async function handleAccounts(request, env) {
  if (!env.ACCOUNTS) {
    return json({ error: "Accounts are not served here." }, 503);
  }

  return env.ACCOUNTS.get(env.ACCOUNTS.idFromName("all")).fetch(request);
}

// Whether a path is one of the accounts'.
export function isAccountsPath(pathname) {
  return pathname === "/v1/link" || pathname.startsWith("/v1/link/") || pathname.startsWith("/v1/auth/discord/")
    || pathname === "/v1/players" || pathname.startsWith("/v1/players/") || pathname === "/v1/account";
}

export class Accounts {
  constructor(state, env) {
    this.state = state;
    this.env = env;
    this.storage = state.storage;
  }

  async fetch(request) {
    const url = new URL(request.url);
    const parts = url.pathname.split("/").filter(Boolean); // ["v1", ...]
    try {
      const body = request.method === "GET" || request.method === "HEAD" ? new Uint8Array() : new Uint8Array(await request.arrayBuffer());

      if (parts[1] === "link" && parts.length === 2 && request.method === "POST") {
        return await this.startLink(request, url, body);
      }

      if (parts[1] === "link" && parts.length === 3 && request.method === "GET") {
        return await this.linkState(request, url, body, parts[2]);
      }

      if (parts[1] === "auth" && parts[2] === "discord" && parts[3] === "start" && request.method === "GET") {
        return await this.toDiscord(url);
      }

      if (parts[1] === "auth" && parts[2] === "discord" && parts[3] === "callback" && request.method === "GET") {
        return await this.fromDiscord(url);
      }

      if (parts[1] === "players" && parts.length === 2 && request.method === "GET") {
        return json(await this.lookup(url.searchParams.get("ids")));
      }

      if (parts[1] === "players" && parts.length === 3 && request.method === "GET") {
        return await this.profile(parts[2]);
      }

      if (parts[1] === "account" && parts.length === 2 && request.method === "GET") {
        return await this.own(request, url, body);
      }

      if (parts[1] === "account" && parts.length === 2 && request.method === "DELETE") {
        return await this.forget(request, url, body);
      }

      if (parts[1] === "link" && parts.length === 2 && request.method === "DELETE") {
        return await this.unlink(request, url, body);
      }

      return json({ error: "Not found." }, 404);
    } catch (error) {
      if (error instanceof Refusal) {
        return json({ error: error.message }, error.status);
      }

      throw error;
    }
  }

  // 1. A key asks to be linked: a fresh code, replacing any it was waiting on.
  async startLink(request, url, body) {
    const key = await playerKey(request, url, body);
    const asked = parseJson(body);
    const label = cleanText(asked.label, MAX_LABEL) || "Game";

    const old = await this.storage.get(`pending:${key.id}`);
    if (old) {
      await this.storage.delete(`link:${old}`);
    }

    const code = newCode();
    const expires = Date.now() + LINK_SECONDS * 1000;
    await this.storage.put({ [`link:${code}`]: { player: key.id, label, expires }, [`pending:${key.id}`]: code });
    return json({ code, url: `${this.publicUrl()}/v1/auth/discord/start?code=${code}`, expiresIn: LINK_SECONDS }, 201);
  }

  // 4. The game asks how its code is doing. Only the key that asked for it may.
  async linkState(request, url, body, code) {
    const key = await playerKey(request, url, body);
    const link = await this.storage.get(`link:${code.toUpperCase()}`);
    if (!link || link.player !== key.id) {
      return json({ state: "unknown" }, 404);
    }

    if (link.account) {
      return json({ state: "linked", account: await this.publicAccount(link.account) });
    }

    return json({ state: link.expires < Date.now() ? "expired" : "pending" });
  }

  // 2. Off to Discord, the code carried in an OAuth state of our own making.
  async toDiscord(url) {
    if (!this.env.DISCORD_CLIENT_ID || !this.env.DISCORD_CLIENT_SECRET) {
      return page(503, "Signing in with Discord isn't set up on this server yet.");
    }

    const code = (url.searchParams.get("code") ?? "").toUpperCase();
    const link = code ? await this.storage.get(`link:${code}`) : undefined;
    if (!link || link.expires < Date.now()) {
      return page(410, "This link has expired. Ask the game for a new one.");
    }

    if (link.account) {
      return page(200, "This game is already linked. You can close this page.");
    }

    const state = randomToken();
    await this.storage.put(`oauth:${state}`, { code, expires: link.expires });
    const to = new URL(`${DISCORD}/oauth2/authorize`);
    to.searchParams.set("client_id", this.env.DISCORD_CLIENT_ID);
    to.searchParams.set("response_type", "code");
    to.searchParams.set("redirect_uri", this.redirectUri());
    to.searchParams.set("scope", SCOPES);
    to.searchParams.set("state", state);
    return Response.redirect(to.toString(), 302);
  }

  // 3. Back from Discord: who they are, and the key linked to them.
  async fromDiscord(url) {
    const state = url.searchParams.get("state") ?? "";
    const pending = state ? await this.storage.get(`oauth:${state}`) : undefined;
    if (!pending) {
      return page(400, "This sign-in wasn't started here, or it was already used.");
    }

    await this.storage.delete(`oauth:${state}`);
    if (url.searchParams.get("error")) {
      return page(200, "Linking was cancelled. Nothing was changed.");
    }

    const link = await this.storage.get(`link:${pending.code}`);
    if (!link || link.expires < Date.now()) {
      return page(410, "This link has expired. Ask the game for a new one.");
    }

    const grant = await this.exchange(url.searchParams.get("code") ?? "");
    if (!grant) {
      return page(502, "Discord didn't let us in. Try again.");
    }

    const user = await discordUser(grant.access_token);
    if (!user) {
      return page(502, "Discord didn't say who you are. Try again.");
    }

    const account = await this.upsertAccount(user);
    await this.attachKey(account, link.player, link.label);
    await this.keepToken(account.id, grant);
    await this.storage.put(`link:${pending.code}`, { ...link, account: account.id });
    await this.storage.delete(`pending:${link.player}`);
    return page(200, `Linked to ${account.name}. You can go back to the game.`);
  }

  // Many players' accounts at once, by key id, for a game server's scoreboard: { <playerId>: account } for those linked.
  async lookup(ids) {
    const wanted = [...new Set(String(ids ?? "").split(",").map((s) => s.trim().toLowerCase()).filter(isPlayerId))].slice(0, MAX_LOOKUP);
    const keys = await this.storage.get(wanted.map((id) => `key:${id}`));
    const out = {};
    for (const id of wanted) {
      const key = keys.get(`key:${id}`);
      if (key) {
        const account = await this.publicAccount(key.account);
        if (account) out[id] = account;
      }
    }

    return out;
  }

  // One profile, by account id (a Discord user id) or by a linked key's id.
  async profile(id) {
    const key = isPlayerId(id) ? await this.storage.get(`key:${id}`) : undefined;
    const account = await this.publicAccount(key ? key.account : id);
    return account ? json(account) : json({ error: "No such player." }, 404);
  }

  // The account the signing key is linked to, with its keys, for the game's profile panel.
  async own(request, url, body) {
    const key = await playerKey(request, url, body);
    const linked = await this.storage.get(`key:${key.id}`);
    if (!linked) {
      return json({ linked: false, player: key.id });
    }

    const account = await this.storage.get(`account:${linked.account}`);
    const keys = await this.storage.get(account.keys.map((k) => `key:${k}`));
    return json({
      linked: true,
      player: key.id,
      account: present(account),
      keys: account.keys.map((k) => ({ id: k, label: keys.get(`key:${k}`)?.label ?? "Game", added: keys.get(`key:${k}`)?.added, current: k === key.id })),
    });
  }

  // The signing key leaves its account; the account stays for its other keys (or until it is deleted).
  async unlink(request, url, body) {
    const key = await playerKey(request, url, body);
    const linked = await this.storage.get(`key:${key.id}`);
    if (!linked) {
      return json({ unlinked: false });
    }

    const account = await this.storage.get(`account:${linked.account}`);
    await this.storage.delete(`key:${key.id}`);
    if (account) {
      account.keys = account.keys.filter((k) => k !== key.id);
      await this.storage.put(`account:${account.id}`, account);
    }

    return json({ unlinked: true });
  }

  // The whole account and everything kept of it, asked by any of its keys.
  async forget(request, url, body) {
    const key = await playerKey(request, url, body);
    const linked = await this.storage.get(`key:${key.id}`);
    if (!linked) {
      return json({ deleted: false });
    }

    await this.deleteAccount(linked.account);
    return json({ deleted: true });
  }

  async deleteAccount(id) {
    const account = await this.storage.get(`account:${id}`);
    if (!account) return;
    await this.storage.delete([`account:${id}`, `token:${id}`, ...account.keys.map((k) => `key:${k}`)]);
  }

  async upsertAccount(user) {
    const existing = await this.storage.get(`account:${user.id}`);
    const account = {
      id: user.id,
      name: cleanText(user.global_name || user.username, MAX_NAME) || "Player",
      avatar: user.avatar ?? null,
      created: existing?.created ?? Date.now(),
      keys: existing?.keys ?? [],
    };
    await this.storage.put(`account:${account.id}`, account);
    return account;
  }

  // A key belongs to one account: linking it again moves it.
  async attachKey(account, player, label) {
    const before = await this.storage.get(`key:${player}`);
    if (before && before.account !== account.id) {
      const other = await this.storage.get(`account:${before.account}`);
      if (other) {
        other.keys = other.keys.filter((k) => k !== player);
        await this.storage.put(`account:${other.id}`, other);
      }
    }

    if (!account.keys.includes(player)) {
      account.keys.push(player);
      while (account.keys.length > MAX_KEYS) {
        await this.storage.delete(`key:${account.keys.shift()}`);
      }
    }

    await this.storage.put({ [`account:${account.id}`]: account, [`key:${player}`]: { account: account.id, label, added: Date.now() } });
  }

  async keepToken(id, grant) {
    const sealed = await seal(this.env.ACCOUNT_KEY, JSON.stringify({ refresh: grant.refresh_token, scope: grant.scope }));
    if (sealed) {
      await this.storage.put(`token:${id}`, sealed);
    }
  }

  // The code Discord handed back, traded for tokens.
  async exchange(code) {
    if (!code) return null;
    const form = new URLSearchParams({
      client_id: this.env.DISCORD_CLIENT_ID,
      client_secret: this.env.DISCORD_CLIENT_SECRET,
      grant_type: "authorization_code",
      code,
      redirect_uri: this.redirectUri(),
    });
    const answer = await fetch(`${DISCORD_API}/oauth2/token`, { method: "POST", headers: { "content-type": "application/x-www-form-urlencoded" }, body: form });
    if (!answer.ok) return null;
    const grant = await answer.json();
    return grant?.access_token ? grant : null;
  }

  async publicAccount(id) {
    const account = id ? await this.storage.get(`account:${id}`) : undefined;
    return account ? present(account) : null;
  }

  publicUrl() {
    return String(this.env.PUBLIC_URL || "https://master.openigi.com").replace(/\/+$/, "");
  }

  redirectUri() {
    return `${this.publicUrl()}/v1/auth/discord/callback`;
  }
}

// The request's player key: only a key as the game plays under it (a raw point), never a map publisher's.
async function playerKey(request, url, body) {
  const key = await signedBy(request, url, body);
  if (!key.raw) {
    throw new Refusal(401, "Sign with the key you play under.");
  }

  return key;
}

async function discordUser(accessToken) {
  const answer = await fetch(`${DISCORD_API}/users/@me`, { headers: { authorization: `Bearer ${accessToken}` } });
  if (!answer.ok) return null;
  const user = await answer.json();
  return typeof user?.id === "string" && /^\d{5,25}$/.test(user.id) ? user : null;
}

// What anyone may see of an account.
export function present(account) {
  return { id: account.id, name: account.name, avatar: avatarUrl(account), since: account.created };
}

export function avatarUrl(account) {
  if (account.avatar) {
    const kind = account.avatar.startsWith("a_") ? "gif" : "png";
    return `https://cdn.discordapp.com/avatars/${account.id}/${account.avatar}.${kind}?size=128`;
  }

  // Discord's default avatars, chosen from the id as Discord does for its new usernames.
  const index = Number((BigInt(account.id) >> 22n) % 6n);
  return `https://cdn.discordapp.com/embed/avatars/${index}.png`;
}

export function isPlayerId(id) {
  return typeof id === "string" && /^[0-9a-f]{16}$/.test(id);
}

function newCode() {
  const bytes = crypto.getRandomValues(new Uint8Array(CODE_LENGTH));
  return [...bytes].map((b) => CODE_ALPHABET[b % CODE_ALPHABET.length]).join("");
}

function randomToken() {
  return [...crypto.getRandomValues(new Uint8Array(24))].map((b) => b.toString(16).padStart(2, "0")).join("");
}

function cleanText(value, max) {
  return typeof value === "string" ? value.replace(/[\u0000-\u001f\u007f]/g, "").trim().slice(0, max) : "";
}

function parseJson(body) {
  if (body.length === 0) return {};
  try {
    const value = JSON.parse(new TextDecoder().decode(body));
    return value && typeof value === "object" ? value : {};
  } catch {
    throw new Refusal(400, "The body is JSON.");
  }
}

// AES-GCM under ACCOUNT_KEY; null when the secret isn't set (the token is then not kept).
export async function seal(secret, text) {
  const key = await aesKey(secret, "encrypt");
  if (!key) return null;
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const data = new Uint8Array(await crypto.subtle.encrypt({ name: "AES-GCM", iv }, key, new TextEncoder().encode(text)));
  return { iv: toBase64(iv), data: toBase64(data) };
}

export async function unseal(secret, sealed) {
  const key = await aesKey(secret, "decrypt");
  if (!key || !sealed) return null;
  const plain = await crypto.subtle.decrypt({ name: "AES-GCM", iv: fromB64(sealed.iv) }, key, fromB64(sealed.data));
  return new TextDecoder().decode(plain);
}

async function aesKey(secret, use) {
  if (!secret) return null;
  const bytes = fromB64(secret);
  if (bytes.length !== 32) return null;
  return crypto.subtle.importKey("raw", bytes, { name: "AES-GCM" }, false, [use]);
}

function toBase64(bytes) {
  return btoa(String.fromCharCode(...bytes));
}

function fromB64(text) {
  return Uint8Array.from(atob(text), (c) => c.charCodeAt(0));
}

// A plain page for the browser the player signed in with.
function page(status, message) {
  const safe = message.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]);
  const html = `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">`
    + `<title>OpenIGI account</title><style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#0f1412;color:#e8ece9;`
    + `font:16px/1.5 system-ui,sans-serif}main{max-width:28rem;padding:2rem;text-align:center}h1{font-size:1.25rem;margin:0 0 .5rem}`
    + `a{color:#8fd0a8}</style></head><body><main><h1>OpenIGI</h1><p>${safe}</p><p><a href="https://openigi.com">openigi.com</a></p></main></body></html>`;
  return new Response(html, { status, headers: { "content-type": "text/html; charset=utf-8", "cache-control": "no-store" } });
}
