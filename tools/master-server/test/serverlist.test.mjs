// The server list on its own, in Node: `node --import ./test/cf-stub.mjs test/serverlist.test.mjs`. A heartbeat lists a
// server; the list outlives a restart of its object; a room is claimed only from the address hosting it, and makes the
// server "verified".
import assert from "node:assert/strict";
import { ServerList } from "../src/index.js";

// A Durable Object's state as far as the list uses it: storage kept between objects, as a restart keeps it.
function fakeState(store = new Map()) {
  let ready = Promise.resolve();
  const storage = {
    async list({ prefix }) {
      return new Map([...store].filter(([k]) => k.startsWith(prefix)).map(([k, v]) => [k, structuredClone(v)]));
    },
    async put(key, value) {
      store.set(key, structuredClone(value));
    },
    async delete(keys) {
      for (const key of Array.isArray(keys) ? keys : [keys]) {
        store.delete(key);
      }
    },
  };
  return { store, storage, blockConcurrencyWhile: (f) => (ready = f()), ready: () => ready };
}

const heartbeat = (ip, body) =>
  new Request("https://list/v1/servers/heartbeat", {
    method: "POST",
    headers: { "content-type": "application/json", "CF-Connecting-IP": ip },
    body: JSON.stringify(body),
  });
const server = { port: 26000, title: "igi1", protocol: "p", name: "Test", map: "1", mode: "tdm", version: "1", maxPlayers: 16 };
const listed = async (list) => (await (await list.fetch(new Request("https://list/v1/servers"))).json()).servers;

// Listed, and written down.
const state = fakeState();
let list = new ServerList(state, {});
await state.ready();
assert.equal((await list.fetch(heartbeat("1.2.3.4", server))).status, 200);
assert.equal((await listed(list)).length, 1);
assert.equal(state.store.size, 1);

// Another heartbeat a moment later, only its player count moved: nothing worth writing.
const firstSave = [...state.store.values()][0].savedAt;
await list.fetch(heartbeat("1.2.3.4", { ...server, players: 3 }));
assert.equal([...state.store.values()][0].savedAt, firstSave);

// The object restarts: the server is still listed before its next heartbeat.
const restarted = fakeState(state.store);
list = new ServerList(restarted, {});
await restarted.ready();
const after = await listed(list);
assert.equal(after.length, 1);
assert.equal(after[0].name, "Test");
assert.equal(after[0].verified, false);
assert.equal(after[0].savedAt, undefined);

// A room hosted from 5.6.7.8 can't be claimed from 1.2.3.4; from its own address it verifies the server.
await list.fetch(
  new Request("https://list/rooms/update", {
    method: "POST",
    body: JSON.stringify({ view: { room: "ABCDEF", title: "igi1" }, ip: "5.6.7.8", public: false, hosted: true }),
  }),
);
assert.equal((await list.fetch(heartbeat("1.2.3.4", { ...server, room: "ABCDEF" }))).status, 403);
const claim = await (await list.fetch(heartbeat("5.6.7.8", { ...server, room: "ABCDEF" }))).json();
assert.equal(claim.verified, true);

// Shut down: off the list, and out of storage.
await list.fetch(
  new Request("https://list/v1/servers/shutdown", {
    method: "POST",
    headers: { "CF-Connecting-IP": "1.2.3.4" },
    body: JSON.stringify({ port: 26000 }),
  }),
);
assert.ok(![...restarted.store.keys()].some((k) => k.startsWith("server:1.2.3.4")));
console.log("server list: ok");
