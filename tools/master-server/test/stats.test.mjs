// The stats on their own, in Node: `node --import ./test/cf-stub.mjs test/stats.test.mjs`. Dedicated servers are counted
// and listed by name, their own rooms counted once (as the server), and nothing says where anything is.
import assert from "node:assert/strict";
import { stats } from "../src/index.js";

const servers = new Map([
  ["1.2.3.4:26001", { address: "1.2.3.4", port: 26001, wsPort: 0, title: "igi1", name: "Official EU", version: "0.6.7", mode: "sd", map: "1", players: 3, maxPlayers: 16, passworded: false, region: "eu", room: "SERVED" }],
  ["5.6.7.8:26001", { address: "5.6.7.8", port: 26001, wsPort: 0, title: "igi1", name: "Quiet one", version: "0.6.6", mode: "tdm", map: "2", players: 0, maxPlayers: 8, passworded: true, region: "na", room: null }],
]);
const room = (code, players, isPublic, relay = null) => [code, { view: { room: code, title: "igi1", players }, public: isPublic, hosted: true, relay }];
const rooms = new Map([room("SERVED", 3, true, "eu"), room("OPENAA", 2, true, "eu"), room("SECRET", 1, false)]);
const s = stats(servers, rooms, [{ id: "eu", region: "Europe", healthy: true, enabled: true }]);

assert.deepEqual(s.servers.open, 2);
assert.deepEqual(s.servers.playing, 1);
assert.deepEqual(s.servers.players, 3);
assert.deepEqual(s.servers.list.map((x) => [x.name, x.players, x.mode, x.map, x.version, x.region, x.passworded]), [
  ["Official EU", 3, "sd", "1", "0.6.7", "eu", false],
  ["Quiet one", 0, "tdm", "2", "0.6.6", "na", true],
]);

// The served room is the server, not a public room as well; its relay still carries it.
assert.equal(s.rooms.public.open, 1);
assert.equal(s.rooms.public.players, 2);
assert.equal(s.rooms.private.open, 1);
assert.deepEqual(s.byTitle.igi1, { rooms: 2, servers: 2, players: 6 });
assert.equal(s.relays.eu.rooms, 2);

const text = JSON.stringify(s);
for (const secret of ["1.2.3.4", "5.6.7.8", "26001", "SERVED", "OPENAA", "SECRET"]) {
  assert.ok(!text.includes(secret), `the stats say ${secret}`);
}

console.log("stats ok");
