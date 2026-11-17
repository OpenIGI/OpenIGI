// Relayed rooms on their own, in Node: `node --import ./test/cf-stub.mjs test/relay.test.mjs`. A Discord Activity's room has
// one code for its instance, whoever asks; a relayed room carries datagrams between the host and each joiner, the peer
// named on the host's side; a room that isn't relayed refuses them; a joiner past its byte budget is cut off quietly.
import assert from "node:assert/strict";
import { Room, handleRooms, instanceCode } from "../src/rooms.js";

// The same instance, the same code; another instance, another.
const a = await instanceCode("i-1234-gc-5678");
assert.equal(a, await instanceCode("i-1234-gc-5678"));
assert.notEqual(a, await instanceCode("i-1234-gc-5679"));
assert.match(a, /^[A-HJ-NP-Z2-9]{6}$/);

// Creating an instance's room: made under its code, or, when it is there already, said so with the code to join.
{
  let asked = null;
  let taken = false;
  const env = {
    ROOMS: {
      idFromName: (name) => name,
      get: (code) => ({
        async fetch(url, init) {
          asked = { code, url: String(url), body: init?.body ? JSON.parse(init.body) : null };
          return taken ? Response.json({ error: "code taken" }, { status: 409 }) : Response.json({ code });
        },
      }),
    },
  };
  const body = { title: "igi1", protocol: "p", name: "Ana's game", map: "1", mode: "coop", version: "0.7", maxPlayers: 4, instance: "i-1234-gc-5678" };
  const make = () => handleRooms(new Request("https://master/v1/rooms", { method: "POST", body: JSON.stringify(body) }), env, new URL("https://master/v1/rooms"), null);

  const made = await make();
  assert.equal(made.status, 200);
  assert.equal(asked.code, a);
  assert.equal(asked.body.relayed, true);
  assert.equal(asked.body.instance, "i-1234-gc-5678");

  taken = true;
  const there = await make();
  assert.equal(there.status, 409);
  assert.equal((await there.json()).room, a);
}

// Sockets, as the room holds them.
function socket(attachment) {
  return {
    sent: [],
    closed: null,
    deserializeAttachment: () => attachment,
    send(data) { this.sent.push(data instanceof Uint8Array ? [...data] : data); },
    close(code, reason) { this.closed = { code, reason }; },
  };
}

function roomWith(sockets) {
  const ctx = { getWebSockets: (tag) => sockets.filter((s) => s.tags.includes(tag)).map((s) => s.ws), storage: {} };
  return new Room(ctx, {});
}

const host = { ws: socket({ role: "host", relayed: true }), tags: ["host"] };
const one = { ws: socket({ role: "join", peer: 1, relayed: true }), tags: ["join", "peer:1"] };
const two = { ws: socket({ role: "join", peer: 2, relayed: true }), tags: ["join", "peer:2"] };
const room = roomWith([host, one, two]);
const bytes = (...values) => new Uint8Array(values).buffer;

// A joiner's datagram reaches the host with its peer in front.
await room.webSocketMessage(two.ws, bytes(9, 8, 7));
assert.deepEqual(host.ws.sent, [[2, 0, 9, 8, 7]]);

// The host's datagram reaches the peer it names, bare, and nobody else.
await room.webSocketMessage(host.ws, bytes(1, 0, 4, 5));
assert.deepEqual(one.ws.sent, [[4, 5]]);
assert.deepEqual(two.ws.sent, []);

// A room that isn't relayed carries none.
const plain = { ws: socket({ role: "join", peer: 1, relayed: false }), tags: ["join", "peer:1"] };
await roomWith([host, plain]).webSocketMessage(plain.ws, bytes(1, 2, 3));
assert.equal(plain.ws.closed.code, 1003);

// Past its budget, a joiner's datagrams are dropped, its socket kept.
host.ws.sent.length = 0;
const big = new Uint8Array(2048).buffer;
for (let i = 0; i < 1100; i++) await room.webSocketMessage(one.ws, big);
assert.ok(host.ws.sent.length < 1100 && host.ws.sent.length >= 1000);
assert.equal(one.ws.closed, null);

console.log("relay: ok");
