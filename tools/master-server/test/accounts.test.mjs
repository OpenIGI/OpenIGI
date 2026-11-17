// Accounts on their own, in Node: `node test/accounts.test.mjs`. A game's player key asks for a link, the player goes to
// Discord and back (Discord faked), the game sees itself linked; the profile and the server lookup name it; a second
// install joins the same account; a key moves when linked elsewhere; codes are the asking key's alone, expire and are used
// once; map publisher keys and bad signatures are turned away; unlink and delete forget what they should.
import assert from "node:assert/strict";
import { webcrypto as crypto } from "node:crypto";
import { Accounts, avatarUrl, seal, unseal } from "../src/accounts.js";
import { signedBy } from "../src/signed.js";

function fakeStorage() {
  const store = new Map();
  return {
    store,
    async get(key) {
      if (Array.isArray(key)) return new Map(key.filter((k) => store.has(k)).map((k) => [k, structuredClone(store.get(k))]));
      return structuredClone(store.get(key));
    },
    async put(key, value) {
      if (typeof key === "object") for (const [k, v] of Object.entries(key)) store.set(k, structuredClone(v));
      else store.set(key, structuredClone(value));
    },
    async delete(key) {
      for (const k of Array.isArray(key) ? key : [key]) store.delete(k);
    },
    async list({ prefix }) {
      return new Map([...store].filter(([k]) => k.startsWith(prefix)).map(([k, v]) => [k, structuredClone(v)]));
    },
  };
}

const base64 = (bytes) => Buffer.from(bytes).toString("base64");
const text = (s) => new TextEncoder().encode(s);
const sha = async (bytes) => [...new Uint8Array(await crypto.subtle.digest("SHA-256", bytes))].map((b) => b.toString(16).padStart(2, "0")).join("");

// A player key as IPlayerIdentity holds one: its public key the 65 bytes of an uncompressed point.
async function playerKey() {
  const pair = await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"]);
  const raw = new Uint8Array(await crypto.subtle.exportKey("raw", pair.publicKey));
  return { pair, wire: base64(raw), id: (await sha(raw)).slice(0, 16) };
}

// A map publisher's key, sent as a SubjectPublicKeyInfo.
async function publisherKey() {
  const pair = await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"]);
  return { pair, wire: base64(new Uint8Array(await crypto.subtle.exportKey("spki", pair.publicKey))) };
}

async function signed(key, method, path, body = new Uint8Array(), time = Math.floor(Date.now() / 1000)) {
  const pathOnly = new URL(`https://master${path}`).pathname;
  const payload = text(`${method}\n${pathOnly}\n${time}\n${await sha(body)}`);
  const signature = base64(new Uint8Array(await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, key.pair.privateKey, payload)));
  return new Request(`https://master${path}`, {
    method,
    headers: { "x-openigi-key": key.wire, "x-openigi-signature": signature, "x-openigi-time": String(time) },
    ...(method === "GET" ? {} : { body }),
  });
}

// Discord, faked: one authorization code per user, its token, and who it belongs to.
const users = {
  "code-ana": { id: "111111111111111111", username: "ana", global_name: "Ana", avatar: "abc" },
  "code-bo": { id: "222222222222222222", username: "bo", global_name: null, avatar: null },
};
const roleConnections = [];
globalThis.fetch = async (input, init = {}) => {
  const url = String(input);
  if (url.endsWith("/oauth2/token")) {
    const form = new URLSearchParams(String(init.body));
    // A refresh grant names the code its refresh token came from.
    const code = form.get("grant_type") === "refresh_token" ? form.get("refresh_token").replace(/^refresh-/, "") : form.get("code");
    if (!users[code]) return new Response("{}", { status: 400 });
    return Response.json({ access_token: `token-${code}`, refresh_token: `refresh-${code}`, scope: "identify role_connections.write", expires_in: 604800 });
  }

  if (url.endsWith("/users/@me")) {
    const code = String(init.headers.authorization).replace("Bearer token-", "");
    return users[code] ? Response.json(users[code]) : new Response("{}", { status: 401 });
  }

  if (url.includes("/role-connection")) {
    roleConnections.push({ token: String(init.headers.authorization), body: JSON.parse(init.body) });
    return Response.json({});
  }

  throw new Error(`The test reached out to ${url}`);
};

const accountKey = base64(crypto.getRandomValues(new Uint8Array(32)));
const env = { DISCORD_CLIENT_ID: "123", DISCORD_CLIENT_SECRET: "shh", ACCOUNT_KEY: accountKey, PUBLIC_URL: "https://master.test/" };
const storage = fakeStorage();
const accounts = new Accounts({ storage }, env);
const ask = (request) => accounts.fetch(request);

// The whole round trip for one key, through Discord as the user behind `discordCode`.
async function link(key, discordCode, label = "Desktop") {
  const start = await ask(await signed(key, "POST", "/v1/link", text(JSON.stringify({ label }))));
  assert.equal(start.status, 201);
  const { code, url } = await start.json();
  assert.match(code, /^[A-Z2-9]{8}$/);
  assert.equal(url, `https://master.test/v1/auth/discord/start?code=${code}`);

  const pending = await ask(await signed(key, "GET", `/v1/link/${code}`));
  assert.equal((await pending.json()).state, "pending");

  const toDiscord = await ask(new Request(url));
  assert.equal(toDiscord.status, 302);
  const authorize = new URL(toDiscord.headers.get("location"));
  assert.equal(authorize.origin + authorize.pathname, "https://discord.com/oauth2/authorize");
  assert.equal(authorize.searchParams.get("redirect_uri"), "https://master.test/v1/auth/discord/callback");
  assert.equal(authorize.searchParams.get("scope"), "identify role_connections.write");
  const state = authorize.searchParams.get("state");

  const back = await ask(new Request(`https://master/v1/auth/discord/callback?code=${discordCode}&state=${state}`));
  assert.equal(back.status, 200, await back.clone().text());

  // A state works once.
  const again = await ask(new Request(`https://master/v1/auth/discord/callback?code=${discordCode}&state=${state}`));
  assert.equal(again.status, 400);

  const done = await (await ask(await signed(key, "GET", `/v1/link/${code}`))).json();
  assert.equal(done.state, "linked");
  return { code, account: done.account };
}

// Linked: the game sees its account.
const desk = await playerKey();
const { code: deskCode, account: ana } = await link(desk, "code-ana");
assert.deepEqual(ana, { id: "111111111111111111", name: "Ana", avatar: "https://cdn.discordapp.com/avatars/111111111111111111/abc.png?size=128", since: ana.since });

// Another key can't read someone else's code.
const stranger = await playerKey();
assert.equal((await ask(await signed(stranger, "GET", `/v1/link/${deskCode}`))).status, 404);

// The profile by account id and by key id, and the server's batch lookup.
assert.equal((await (await ask(new Request("https://master/v1/players/111111111111111111"))).json()).name, "Ana");
assert.equal((await (await ask(new Request(`https://master/v1/players/${desk.id}`))).json()).name, "Ana");
assert.equal((await ask(new Request(`https://master/v1/players/${stranger.id}`))).status, 404);
const lookup = await (await ask(new Request(`https://master/v1/players?ids=${desk.id},${stranger.id},nonsense`))).json();
assert.deepEqual(Object.keys(lookup), [desk.id]);

// A second install (the browser) joins the same account.
const web = await playerKey();
await link(web, "code-ana", "Browser");
const own = await (await ask(await signed(web, "GET", "/v1/account"))).json();
assert.equal(own.linked, true);
assert.deepEqual(own.keys.map((k) => [k.label, k.current]), [["Desktop", false], ["Browser", true]]);

// Signing in tells Discord's Linked Roles the player is an operative.
assert.ok(roleConnections.some((told) => told.token === "Bearer token-code-ana" && told.body.metadata.operative === 1 && told.body.platform_name === "OpenIGI"));

// Later, the stats' totals are told through a token traded from the kept refresh token.
{
  roleConnections.length = 0;
  const told = await accounts.fetch(new Request("https://accounts/internal/roles", { method: "POST", body: JSON.stringify({ players: [{ account: "111111111111111111", totals: { matches: 12, wins: 7, kills: 96 } }] }) }));
  assert.equal((await told.json()).updated, 1);
  assert.deepEqual(roleConnections[0].body.metadata, { operative: 1, matches: 12, wins: 7, kills: 96 });
}

// The refresh token is kept, sealed.
const sealed = storage.store.get("token:111111111111111111");
assert.ok(sealed && !JSON.stringify(sealed).includes("refresh-code-ana"));
assert.equal(JSON.parse(await unseal(accountKey, sealed)).refresh, "refresh-code-ana");
assert.equal(await seal(undefined, "x"), null);

// Linking a key to another Discord account moves it.
await link(web, "code-bo", "Browser");
const bo = await (await ask(new Request(`https://master/v1/players/${web.id}`))).json();
assert.equal(bo.name, "bo");
assert.match(bo.avatar, /^https:\/\/cdn\.discordapp\.com\/embed\/avatars\/[0-5]\.png$/);
assert.deepEqual(storage.store.get("account:111111111111111111").keys, [desk.id]);

// A cancelled sign-in changes nothing.
{
  const start = await (await ask(await signed(stranger, "POST", "/v1/link"))).json();
  const toDiscord = await ask(new Request(start.url));
  const state = new URL(toDiscord.headers.get("location")).searchParams.get("state");
  const back = await ask(new Request(`https://master/v1/auth/discord/callback?error=access_denied&state=${state}`));
  assert.equal(back.status, 200);
  assert.equal((await (await ask(await signed(stranger, "GET", `/v1/link/${start.code}`))).json()).state, "pending");

  // Asking again replaces the old code.
  const second = await (await ask(await signed(stranger, "POST", "/v1/link"))).json();
  assert.equal((await ask(await signed(stranger, "GET", `/v1/link/${start.code}`))).status, 404);

  // An expired code is refused at the start.
  const link = storage.store.get(`link:${second.code}`);
  storage.store.set(`link:${second.code}`, { ...link, expires: Date.now() - 1 });
  assert.equal((await ask(new Request(second.url))).status, 410);
  assert.equal((await (await ask(await signed(stranger, "GET", `/v1/link/${second.code}`))).json()).state, "expired");
}

// A map publisher's key isn't a player's; a bad signature and an old one are turned away.
const publisher = await publisherKey();
assert.equal((await ask(await signed(publisher, "POST", "/v1/link"))).status, 401);
{
  const good = await signed(desk, "POST", "/v1/link", text("{}"));
  const forged = new Request(good, { headers: { ...Object.fromEntries(good.headers), "x-openigi-key": stranger.wire }, body: text("{}") });
  assert.equal((await ask(forged)).status, 401);
  assert.equal((await ask(await signed(desk, "POST", "/v1/link", text("{}"), Math.floor(Date.now() / 1000) - 3600))).status, 401);
}

// The shared check names a player key by PlayerIdentities.IdOf: the first sixteen hex digits of the point's SHA-256.
{
  const request = await signed(desk, "POST", "/v1/link", text("{}"));
  const key = await signedBy(request, new URL(request.url), text("{}"));
  assert.deepEqual(key, { id: desk.id, raw: true });
}

// The profile kept in step: only a linked key reads or writes it, the newest write wins, and deleting the account drops it.
{
  assert.equal((await ask(await signed(stranger, "GET", "/v1/account/profile"))).status, 403);
  const empty = await (await ask(await signed(desk, "GET", "/v1/account/profile"))).json();
  assert.deepEqual(empty, { data: null, updated: 0 });

  const profile = { name: "Ana", missions: { igi1: 7, igi2: 3 } };
  const put = await ask(await signed(desk, "PUT", "/v1/account/profile", text(JSON.stringify({ data: profile }))));
  assert.equal(put.status, 200);
  const read = await (await ask(await signed(desk, "GET", "/v1/account/profile"))).json();
  assert.deepEqual(read.data, profile);
  assert.ok(read.updated > 0);

  assert.equal((await ask(await signed(desk, "PUT", "/v1/account/profile", text(JSON.stringify({ data: [1] }))))).status, 400);
  assert.equal((await ask(await signed(desk, "PUT", "/v1/account/profile", text(JSON.stringify({ data: { big: "x".repeat(70 * 1024) } }))))).status, 413);
  assert.ok(storage.store.has("profile:111111111111111111"));
}

// Unlinking takes the one key off; deleting takes the account and all of it.
assert.equal((await (await ask(await signed(web, "DELETE", "/v1/link"))).json()).unlinked, true);
assert.equal((await ask(new Request(`https://master/v1/players/${web.id}`))).status, 404);
assert.deepEqual(storage.store.get("account:222222222222222222").keys, []);

assert.equal((await (await ask(await signed(desk, "DELETE", "/v1/account"))).json()).deleted, true);
assert.equal(storage.store.has("account:111111111111111111"), false);
assert.equal(storage.store.has("token:111111111111111111"), false);
assert.equal(storage.store.has("profile:111111111111111111"), false);
assert.equal(storage.store.has(`key:${desk.id}`), false);

// Inside an Activity: the SDK's code traded for its token, and who the player is.
{
  const traded = await accounts.fetch(new Request("https://master/v1/auth/discord/activity", { method: "POST", body: JSON.stringify({ code: "code-ana" }) }));
  assert.equal(traded.status, 200);
  const said = await traded.json();
  assert.equal(said.access_token, "token-code-ana");
  assert.equal(said.account.name, "Ana");
  assert.equal((await accounts.fetch(new Request("https://master/v1/auth/discord/activity", { method: "POST", body: JSON.stringify({ code: "nonsense" }) }))).status, 401);

  // Signed with the player's key, the same sign-in links that key: the game in the Activity is signed in.
  const discordGame = await playerKey();
  const linking = await accounts.fetch(await signed(discordGame, "POST", "/v1/auth/discord/activity", text(JSON.stringify({ code: "code-ana" }))));
  assert.equal((await linking.json()).linked, true);
  assert.equal((await (await accounts.fetch(new Request(`https://master/v1/players/${discordGame.id}`))).json()).name, "Ana");
}

// Starting from Discord's Linked Roles: an account with no game yet, and its standing told.
{
  const to = await accounts.fetch(new Request("https://master/v1/auth/discord/verify"));
  assert.equal(to.status, 302);
  const state = new URL(to.headers.get("location")).searchParams.get("state");
  roleConnections.length = 0;
  const back = await accounts.fetch(new Request(`https://master/v1/auth/discord/callback?code=code-bo&state=${state}`));
  assert.equal(back.status, 200);
  assert.equal(roleConnections[0].body.metadata.operative, 1);
  assert.deepEqual(storage.store.get("account:222222222222222222").keys, []);
}

// Without the Discord app set up, the start page says so instead of sending anyone off.
{
  const bare = new Accounts({ storage: fakeStorage() }, {});
  const response = await bare.fetch(new Request("https://master/v1/auth/discord/start?code=ABCDEFGH"));
  assert.equal(response.status, 503);
}

assert.equal(avatarUrl({ id: "175928847299117063", avatar: "a_x" }), "https://cdn.discordapp.com/avatars/175928847299117063/a_x.gif?size=128");

console.log("accounts: ok");
