// Room signaling against a running Worker, then `node test/rooms.test.mjs [http://127.0.0.1:8787]`. The Worker runs as
//   npx wrangler dev --port 8799 --env-file test.env
// with test.env holding TURN_SECRET=localtest, TURN_HOST=127.0.0.1, TURN_PORT=34780, ADMIN_TOKEN=admintest,
// RELAY_KEY=<64 hex>, ALLOW_TEST_GEO=1 and NO_EDGE_CACHE=1 (an env file: wrangler drops `--var`s it has no other mention of). The test
// plays the built-in EU relay on TURN_PORT itself. Port 8787 may be an old dev server's.
// Walks a room's life: made, hosted, listed, joined, signaled both ways, left, closed with its host. Then the relays:
// added by us alone, chosen by where the players are, each login from its own relay's secret, skipped when down.
import assert from "node:assert/strict";
import { createHmac } from "node:crypto";
import { startFakeRelay } from "./fake-relay.mjs";

const base = process.argv[2] ?? "http://127.0.0.1:8787";
const EU = Number(process.argv[3] ?? 34780);
const wsBase = base.replace(/^http/, "ws");

const post = (path, body) =>
  fetch(base + path, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body) });

function open(url) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(url);
    const inbox = [];
    const waiters = [];
    ws.addEventListener("message", (e) => {
      const message = JSON.parse(e.data);
      const waiter = waiters.shift();
      waiter ? waiter(message) : inbox.push(message);
    });
    ws.next = () =>
      new Promise((res, rej) => {
        if (inbox.length) return res(inbox.shift());
        const timer = setTimeout(() => rej(new Error("no message")), 3000);
        waiters.push((m) => (clearTimeout(timer), res(m)));
      });
    ws.closedWith = new Promise((res) => ws.addEventListener("close", (e) => res(e.code)));
    ws.addEventListener("open", () => resolve(ws));
    ws.addEventListener("error", () => reject(new Error(`could not open ${url}`)));
  });
}

const room = {
  title: "igi1", protocol: "test", name: "Test room", map: "1", mode: "coop", version: "0.0.0", maxPlayers: 4, public: true,
};

// Bad requests.
assert.equal((await post("/v1/rooms", { ...room, maxPlayers: 99 })).status, 400);
assert.equal((await fetch(`${base}/v1/rooms/AAAAAA`)).status, 404);
assert.equal((await fetch(`${base}/v1/rooms/not-a-code`)).status, 404);

// Made.
const made = await (await post("/v1/rooms", room)).json();
assert.match(made.code, /^[A-Z2-9]{6}$/);
assert.ok(made.hostToken);
assert.ok(made.ice[0].urls[0].startsWith("stun:"));
const turn = made.ice.find((s) => s.username);
assert.ok(turn, "a TURN login (is TURN_SECRET set?)");
assert.match(turn.username, /^\d+:[A-Z2-9]{6}-host$/);

// Not listed, and not joinable, before its host is there.
let list = await (await fetch(`${base}/v1/servers?title=igi1`)).json();
assert.ok(!list.servers.some((s) => s.room === made.code));
assert.equal((await fetch(`${base}/v1/rooms/${made.code}`)).status, 200);

// A wrong token is not the host.
await assert.rejects(open(`${wsBase}/v1/rooms/${made.code}/ws?role=host&token=nope`));

// Hosted, then listed.
let host = await open(`${wsBase}/v1/rooms/${made.code}/ws?role=host&token=${made.hostToken}&returns=1`);
assert.deepEqual(await host.next(), { type: "hosting", code: made.code });
list = await (await fetch(`${base}/v1/servers?title=igi1`)).json();
const entry = list.servers.find((s) => s.room === made.code);
assert.ok(entry, "listed once hosted");
assert.equal(entry.address, undefined, "never the host's address");
assert.equal(entry.hostToken, undefined);

// Counted in the stats, as counts only.
const stats = await (await fetch(`${base}/v1/stats`)).json();
assert.ok(stats.rooms.public.open >= 1);
assert.ok(!JSON.stringify(stats).includes(made.code), "never a room's code");

// Joined, case-insensitively, and signaled both ways.
const joiner = await open(`${wsBase}/v1/rooms/${made.code.toLowerCase()}/ws?role=join`);
const welcome = await joiner.next();
assert.equal(welcome.type, "welcome");
assert.equal(welcome.room.room, made.code);
assert.ok(welcome.ice.some((s) => s.username?.endsWith(`-${welcome.peer}`)));
const joined = await host.next();
assert.equal(joined.type, "join");
assert.equal(joined.peer, welcome.peer);
assert.ok(joined.ice.some((s) => s.username?.endsWith(`-host-${welcome.peer}`)), "the host's logins for this joiner");

joiner.send(JSON.stringify({ type: "offer", data: { sdp: "v=0 offer" } }));
assert.deepEqual(await host.next(), { type: "offer", peer: welcome.peer, data: { sdp: "v=0 offer" } });
host.send(JSON.stringify({ type: "answer", peer: welcome.peer, data: { sdp: "v=0 answer" } }));
assert.deepEqual(await joiner.next(), { type: "answer", data: { sdp: "v=0 answer" } });
joiner.send(JSON.stringify({ type: "candidate", data: { candidate: "c1" } }));
assert.deepEqual(await host.next(), { type: "candidate", peer: welcome.peer, data: { candidate: "c1" } });

// A joiner can't pose as the host's side or address another joiner.
joiner.send(JSON.stringify({ type: "state", players: 32 }));
joiner.send(JSON.stringify({ type: "answer", peer: 999, data: {} }));
assert.equal((await host.next()).peer, welcome.peer);

// The host's state reaches the list.
host.send(JSON.stringify({ type: "state", players: 2, map: "2" }));
await new Promise((r) => setTimeout(r, 200));
const info = await (await fetch(`${base}/v1/rooms/${made.code}`)).json();
assert.equal(info.players, 2);
assert.equal(info.map, "2");

// Left.
joiner.close();
assert.deepEqual(await host.next(), { type: "leave", peer: welcome.peer });

// Kicked.
const second = await open(`${wsBase}/v1/rooms/${made.code}/ws?role=join`);
const secondWelcome = await second.next();
await host.next(); // join
host.send(JSON.stringify({ type: "kick", peer: secondWelcome.peer }));
assert.equal(await second.closedWith, 4000);

// The host's socket dropping without a goodbye (a line blinking, a deploy) leaves the room waiting for it: still there, not
// joinable meanwhile, and the host's own token takes it back -- nobody else's.
host.close(4100, "line dropped");
await new Promise((r) => setTimeout(r, 200));
assert.equal((await fetch(`${base}/v1/rooms/${made.code}`)).status, 200, "kept for its host");
await assert.rejects(open(`${wsBase}/v1/rooms/${made.code}/ws?role=join`), "no joining while the host is away");
await assert.rejects(open(`${wsBase}/v1/rooms/${made.code}/ws?role=host&token=nope`));
host = await open(`${wsBase}/v1/rooms/${made.code}/ws?role=host&token=${made.hostToken}&returns=1`);
assert.deepEqual(await host.next(), { type: "hosting", code: made.code });
await assert.rejects(open(`${wsBase}/v1/rooms/${made.code}/ws?role=host&token=${made.hostToken}&returns=1`), "one host socket at a time");

// The host saying goodbye is the room gone.
const third = await open(`${wsBase}/v1/rooms/${made.code}/ws?role=join`);
await third.next();
host.close(1000, "the host left");
assert.equal(await third.closedWith, 4001);
await new Promise((r) => setTimeout(r, 200));
assert.equal((await fetch(`${base}/v1/rooms/${made.code}`)).status, 404);
list = await (await fetch(`${base}/v1/servers?title=igi1`)).json();
assert.ok(!list.servers.some((s) => s.room === made.code));

// An older game's host never comes back for its room, so its socket dropping, goodbye or not, is the room gone at once.
const old = await (await post("/v1/rooms", room)).json();
const oldHost = await open(`${wsBase}/v1/rooms/${old.code}/ws?role=host&token=${old.hostToken}`);
await oldHost.next();
oldHost.close(4100, "aborted");
await new Promise((r) => setTimeout(r, 200));
assert.equal((await fetch(`${base}/v1/rooms/${old.code}`)).status, 404, "an older host's room closes with its socket");

// The relays. Admin routes are ours alone.
const ADMIN = { authorization: "Bearer admintest", "content-type": "application/json" };
const adminCall = (method, path, body) =>
  fetch(`${base}/v1/admin/relays${path}`, { method, headers: ADMIN, body: body && JSON.stringify(body) });
assert.equal((await fetch(`${base}/v1/admin/relays`)).status, 401);
assert.equal((await fetch(`${base}/v1/admin/relays`, { headers: { authorization: "Bearer nope" } })).status, 401);
assert.equal((await adminCall("PUT", "/eu", { host: "x.example", region: "x", lat: 0, lon: 0 })).status, 409);
assert.equal((await adminCall("PUT", "/bad id", {})).status, 400);

// Two community relays: one in Mumbai holding its secret, one in Dubai given the wrong one.
const southAsiaSecret = "south-asia-secret-0123456789";
const eu = await startFakeRelay("localtest", EU);
const southAsia = await startFakeRelay(southAsiaSecret);
const gulf = await startFakeRelay("not-the-secret-it-was-issued");
const putSouthAsia = await (await adminCall("PUT", "/south-asia", {
  host: "127.0.0.1", region: "South Asia", lat: 19.1, lon: 72.9, port: southAsia.port, secret: southAsiaSecret,
})).json();
assert.equal(putSouthAsia.relay.enabled, false, "a new relay waits to be enabled");
assert.equal(putSouthAsia.secret, undefined, "a secret we gave is not echoed");
const putGulf = await (await adminCall("PUT", "/gulf", { host: "127.0.0.1", region: "Gulf", lat: 25.2, lon: 55.3, port: gulf.port })).json();
assert.match(putGulf.secret, /^[0-9a-f]{64}$/, "a made secret is shown once");
assert.ok(!JSON.stringify(await (await adminCall("GET", "")).json()).includes(southAsiaSecret), "secrets never listed");

const KARACHI = "24.9,67.0";
const DELHI = "28.6,77.2";
const turnHosts = (ice) => ice.filter((s) => s.username).map((s) => Number(s.urls[0].split(":")[2].split("?")[0]));
const credentialOk = (server, secret) =>
  server.credential === createHmac("sha1", secret).update(server.username).digest("base64");

// A room made, hosted and closed again, so it doesn't hold one of this address's few rooms.
async function relaysFor(hostGeo, joinerGeo) {
  const created = await (await post(`/v1/rooms?testGeo=${hostGeo}`, room)).json();
  const h = await open(`${wsBase}/v1/rooms/${created.code}/ws?role=host&token=${created.hostToken}`);
  await h.next();
  const j = await open(`${wsBase}/v1/rooms/${created.code}/ws?role=join&testGeo=${joinerGeo}`);
  const w = await j.next();
  const hostJoin = await h.next();
  const s = await (await fetch(`${base}/v1/stats`)).json();
  j.close();
  h.close(1000, "the host left");
  await h.closedWith;
  await new Promise((r) => setTimeout(r, 200));
  return { host: created.ice, joiner: w.ice, hostJoin: hostJoin.ice, stats: s };
}

// Disabled, and then enabled but not yet probed: not handed out; the EU relay alone.
let got = await relaysFor(KARACHI, DELHI);
assert.deepEqual(turnHosts(got.host), [EU]);
await adminCall("PUT", "/south-asia", { enabled: true });
await adminCall("PUT", "/gulf", { enabled: true });
got = await relaysFor(KARACHI, DELHI);
assert.deepEqual(turnHosts(got.host), [EU]);

// Probed: South Asia passes, the Gulf relay's wrong secret fails its login.
let probed = (await (await adminCall("POST", "/probe")).json()).probed;
assert.equal(probed.find((p) => p.id === "south-asia").healthy, true, JSON.stringify(probed));
assert.equal(probed.find((p) => p.id === "eu").healthy, true);
assert.equal(southAsia.logins, 1, "the probe logged in with the relay's own secret");
const gulfProbe = probed.find((p) => p.id === "gulf");
assert.equal(gulfProbe.healthy, false);
assert.match(gulfProbe.error, /secret/);

// Host in Pakistan, joiner in India: South Asia first, EU behind it, for both and for the host's login to the joiner.
got = await relaysFor(KARACHI, DELHI);
for (const ice of [got.host, got.joiner, got.hostJoin]) {
  assert.deepEqual(turnHosts(ice), [southAsia.port, EU]);
  assert.ok(ice[0].urls[0] === `stun:127.0.0.1:${southAsia.port}`, "STUN on the best relay");
  assert.ok(credentialOk(ice[1], southAsiaSecret), "South Asia's login from South Asia's secret");
  assert.ok(credentialOk(ice[2], "localtest"), "EU's login from EU's secret");
  assert.ok(!credentialOk(ice[2], southAsiaSecret));
}

// The stats count rooms by relay, and say nothing else.
assert.equal(got.stats.relays["south-asia"].rooms, 1);
assert.equal(got.stats.relays["south-asia"].healthy, true);
assert.ok(!JSON.stringify(got.stats.relays).includes("127.0.0.1"), "never a relay's address");

// A joiner in Frankfurt with a host in Karachi: EU is the shorter way round, and both share it.
got = await relaysFor(KARACHI, "50.1,8.7");
assert.deepEqual(turnHosts(got.joiner), [EU, southAsia.port]);

// South Asia goes down: one failed probe keeps it, two drop it, and EU leads alone.
await southAsia.close();
probed = (await (await adminCall("POST", "/probe")).json()).probed;
assert.equal(probed.find((p) => p.id === "south-asia").healthy, true);
probed = (await (await adminCall("POST", "/probe")).json()).probed;
assert.equal(probed.find((p) => p.id === "south-asia").healthy, false);
got = await relaysFor(KARACHI, DELHI);
assert.deepEqual(turnHosts(got.host), [EU]);
assert.deepEqual(turnHosts(got.joiner), [EU]);

await adminCall("DELETE", "/south-asia");
await adminCall("DELETE", "/gulf");
await gulf.close();
await eu.close();
assert.equal((await (await adminCall("GET", "")).json()).relays.length, 1);

// One address can't hold more than a few rooms.
const codes = [];
let status = 200;
for (let i = 0; i < 6 && status === 200; i++) {
  const response = await post("/v1/rooms", room);
  status = response.status;
  if (status === 200) codes.push((await response.json()).code);
}

assert.equal(status, 429);
assert.equal(codes.length, 4);

console.log(`rooms ok (${made.code}); TURN login ${turn.username}`);
console.log(`TURN_CHECK ${turn.username} ${turn.credential}`);
