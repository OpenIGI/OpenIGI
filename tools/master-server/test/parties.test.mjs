// Parties on their own, in Node: `node test/parties.test.mjs`. Made, joined, moved by the leader only, followed by the
// members; a member who stops asking is gone, the next oldest leads, and an empty party is forgotten.
import assert from "node:assert/strict";
import { Party, cleanTarget, handleParties } from "../src/parties.js";

// A Durable Object's state as far as a party uses it.
function fakeState() {
  const store = new Map();
  let ready = Promise.resolve();
  const storage = {
    async get(key) {
      return structuredClone(store.get(key));
    },
    async put(key, value) {
      store.set(key, structuredClone(value));
    },
    async deleteAll() {
      store.clear();
    },
    async setAlarm() {},
  };
  return { store, storage, blockConcurrencyWhile: (f) => (ready = f()), ready: () => ready };
}

const post = (path, body) => new Request(`https://party/ABCDEF/${path}`, { method: "POST", body: JSON.stringify(body) });
const get = (who) => new Request(`https://party/ABCDEF/state?member=${who.member}&token=${who.token}`);
const read = async (response) => ({ status: response.status, ...(await response.json()) });

const state = fakeState();
const party = new Party(state, {});
await state.ready();

// Made: its maker leads it.
const leader = await read(await party.fetch(post("create", { name: "Ann" })));
assert.equal(leader.status, 200);
assert.equal(leader.leader, leader.member);
assert.equal((await read(await party.fetch(post("create", { name: "Eve" })))).status, 409);

// Joined; names are cleaned, and a member is never told another's token.
const friend = await read(await party.fetch(post("join", { name: " Bob\u0007 " })));
assert.deepEqual(friend.members.map((m) => m.name), ["Ann", "Bob"]);
assert.ok(friend.members.every((m) => m.token === undefined));

// Only the leader moves the party; a member reads where it went.
assert.equal((await party.fetch(post("target", { member: friend.member, token: friend.token, target: "ZZZZZZ" }))).status, 403);
assert.equal((await party.fetch(post("target", { member: leader.member, token: "wrong", target: "ZZZZZZ" }))).status, 403);
const moved = await read(await party.fetch(post("target", { member: leader.member, token: leader.token, target: "udp:203.0.113.5:26001" })));
assert.equal(moved.target, "udp:203.0.113.5:26001");
const seen = await read(await party.fetch(get(friend)));
assert.equal(seen.target, "udp:203.0.113.5:26001");
assert.equal(seen.version, 1);

// The same place again is not a move.
await party.fetch(post("target", { member: leader.member, token: leader.token, target: "udp:203.0.113.5:26001" }));
assert.equal((await read(await party.fetch(get(friend)))).version, 1);

// Rooms and server keys only.
assert.equal(cleanTarget("abcdef"), "ABCDEF");
assert.equal(cleanTarget("udp:[::1]:26001"), "udp:[::1]:26001");
assert.equal(cleanTarget("http://evil"), undefined);
assert.equal(cleanTarget(null), null);

// The leader goes quiet while Bob keeps asking: after a while the leader is gone, and Bob leads.
const realNow = Date.now;
try {
  Date.now = () => realNow() + 20_000;
  assert.equal((await party.fetch(get(friend))).status, 200);
  Date.now = () => realNow() + 31_000;
  const after = await read(await party.fetch(get(friend)));
  assert.equal(after.status, 200);
  assert.deepEqual(after.members.map((m) => m.name), ["Bob"]);
  assert.equal(after.leader, friend.member);
  assert.equal((await party.fetch(get(leader))).status, 403);

  // The last one out: the party is forgotten.
  assert.equal((await party.fetch(post("leave", { member: friend.member, token: friend.token }))).status, 200);
  assert.equal(state.store.size, 0);
  assert.equal((await party.fetch(get(friend))).status, 404);
} finally {
  Date.now = realNow;
}

// Full at eight.
const full = new Party(fakeState(), {});
await full.fetch(post("create", { name: "1" }));
for (let i = 2; i <= 8; i++) {
  assert.equal((await full.fetch(post("join", { name: String(i) }))).status, 200);
}

assert.equal((await full.fetch(post("join", { name: "9" }))).status, 403);
// Through the routes, as the Worker serves them: each code its own object.
const objects = new Map();
const env = {
  PARTIES: {
    idFromName: (name) => name,
    get: (name) => {
      if (!objects.has(name)) {
        const s = fakeState();
        objects.set(name, new Party(s, {}));
      }

      return { fetch: (input, init) => objects.get(name).fetch(new Request(input, init)) };
    },
  },
};
const route = async (method, path, body) => {
  const url = new URL(`https://master${path}`);
  const response = await handleParties(new Request(url, { method, body: body ? JSON.stringify(body) : undefined }), env, url);
  return { status: response.status, ...(await response.json()) };
};
const made = await route("POST", "/v1/parties", { name: "Lead" });
assert.equal(made.status, 200);
assert.match(made.code, /^[A-Z2-9]{6}$/);
const came = await route("POST", `/v1/parties/${made.code.toLowerCase()}/join`, { name: "Mate" });
assert.equal(came.status, 200);
await route("POST", `/v1/parties/${made.code}/target`, { member: made.member, token: made.token, target: "QWERTY" });
const looked = await route("GET", `/v1/parties/${made.code}?member=${came.member}&token=${came.token}`);
assert.equal(looked.target, "QWERTY");
assert.equal((await route("GET", "/v1/parties/nope")).status, 400);
assert.equal((await route("DELETE", `/v1/parties/${made.code}`)).status, 404);
console.log("parties: ok");
