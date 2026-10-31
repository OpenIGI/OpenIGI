// Room signaling against a running Worker: `npx wrangler dev`, then `node test/rooms.test.mjs [http://127.0.0.1:8787]`.
// Walks a room's life: made, hosted, listed, joined, signaled both ways, left, closed with its host.
import assert from "node:assert/strict";

const base = process.argv[2] ?? "http://127.0.0.1:8787";
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
const host = await open(`${wsBase}/v1/rooms/${made.code}/ws?role=host&token=${made.hostToken}`);
assert.deepEqual(await host.next(), { type: "hosting", code: made.code });
list = await (await fetch(`${base}/v1/servers?title=igi1`)).json();
const entry = list.servers.find((s) => s.room === made.code);
assert.ok(entry, "listed once hosted");
assert.equal(entry.address, undefined, "never the host's address");
assert.equal(entry.hostToken, undefined);

// Joined, case-insensitively, and signaled both ways.
const joiner = await open(`${wsBase}/v1/rooms/${made.code.toLowerCase()}/ws?role=join`);
const welcome = await joiner.next();
assert.equal(welcome.type, "welcome");
assert.equal(welcome.room.room, made.code);
assert.ok(welcome.ice.some((s) => s.username?.endsWith(`-${welcome.peer}`)));
assert.deepEqual(await host.next(), { type: "join", peer: welcome.peer });

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

// The host gone is the room gone.
const third = await open(`${wsBase}/v1/rooms/${made.code}/ws?role=join`);
await third.next();
host.close();
assert.equal(await third.closedWith, 4001);
await new Promise((r) => setTimeout(r, 200));
assert.equal((await fetch(`${base}/v1/rooms/${made.code}`)).status, 404);
list = await (await fetch(`${base}/v1/servers?title=igi1`)).json();
assert.ok(!list.servers.some((s) => s.room === made.code));

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
