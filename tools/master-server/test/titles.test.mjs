// Each game's stats on their own, in Node: `node test/titles.test.mjs`. A match counts on its own game's board and record
// only; a request that doesn't name a game is Project I.G.I.'s; and the totals kept before the games were apart are made again
// by game from the kept results, once, with the old shared ones gone.
import assert from "node:assert/strict";
import { webcrypto as crypto } from "node:crypto";
import { Stats } from "../src/stats.js";

function fakeStorage(seed = {}) {
  const store = new Map(Object.entries(seed).map(([k, v]) => [k, structuredClone(v)]));
  return {
    store,
    async get(key) {
      if (Array.isArray(key)) return new Map(key.filter((k) => store.has(k)).map((k) => [k, structuredClone(store.get(k))]));
      return structuredClone(store.get(key));
    },
    async put(key, value) {
      if (typeof key === "object") {
        assert.ok(Object.keys(key).length <= 128, "a put takes at most 128 keys");
        for (const [k, v] of Object.entries(key)) store.set(k, structuredClone(v));
      } else store.set(key, structuredClone(value));
    },
    async delete(key) {
      const keys = Array.isArray(key) ? key : [key];
      assert.ok(keys.length <= 128, "a delete takes at most 128 keys");
      for (const k of keys) store.delete(k);
    },
    async list({ prefix }) {
      return new Map([...store].filter(([k]) => k.startsWith(prefix)).map(([k, v]) => [k, structuredClone(v)]));
    },
  };
}

const base64 = (bytes) => Buffer.from(bytes).toString("base64");
const sha = async (bytes) => [...new Uint8Array(await crypto.subtle.digest("SHA-256", bytes))].map((b) => b.toString(16).padStart(2, "0")).join("");
const pair = await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"]);
const wire = base64(new Uint8Array(await crypto.subtle.exportKey("raw", pair.publicKey)));

async function signed(body) {
  const time = Math.floor(Date.now() / 1000);
  const bytes = new TextEncoder().encode(JSON.stringify(body));
  const payload = new TextEncoder().encode(`POST\n/v1/matches\n${time}\n${await sha(bytes)}`);
  const signature = base64(new Uint8Array(await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, pair.privateKey, payload)));
  return new Request("https://master/v1/matches", { method: "POST", body: bytes, headers: { "x-openigi-key": wire, "x-openigi-signature": signature, "x-openigi-time": String(time) } });
}

const BO = "bbbbbbbbbbbbbbbb";
const CY = "cccccccccccccccc";
const line = (player, name, team, kills, deaths, won) => ({ player, name, team, kills, deaths, won, objectives: 0, assists: 0 });
const match = (title, players) => ({ title, mode: "tdm", map: "1", seconds: 600, winner: 0, players });
const now = Date.UTC(2026, 10, 20);

// Two games, apart.
{
  const stats = new Stats({ storage: fakeStorage() }, {}, () => now);
  const get = async (path) => (await stats.fetch(new Request(`https://master${path}`))).json();
  await stats.fetch(new Request("https://master/v1/admin/stats-servers", { method: "POST", body: JSON.stringify({ key: wire, name: "Official" }) }));
  assert.equal((await stats.fetch(await signed(match("igi1", [line(BO, "Bo", 0, 7, 1, true), line(CY, "Cy", 1, 1, 7, false)])))).status, 201);
  assert.equal((await stats.fetch(await signed(match("igi2", [line(BO, "Bo", 0, 2, 3, false), line(CY, "Cy", 1, 9, 0, true)])))).status, 201);

  const igi1 = await get("/v1/leaderboard?title=igi1&metric=kills");
  const igi2 = await get("/v1/leaderboard?title=igi2&metric=kills");
  assert.equal(igi1.title, "igi1");
  assert.deepEqual(igi1.rows.map((row) => [row.name, row.kills, row.matches]), [["Bo", 7, 1], ["Cy", 1, 1]]);
  assert.deepEqual(igi2.rows.map((row) => [row.name, row.kills, row.matches]), [["Cy", 9, 1], ["Bo", 2, 1]]);

  // A board or record that doesn't name its game is Project I.G.I.'s, as an older build asks.
  assert.deepEqual((await get("/v1/leaderboard?metric=kills")).rows, igi1.rows);
  assert.equal((await get(`/v1/players/${BO}/stats`)).allTime.kills, 7);
  assert.equal((await get(`/v1/players/${BO}/stats?title=igi2`)).allTime.kills, 2);
  assert.ok((await get(`/v1/players/${BO}/stats?title=igi2`)).latest.every((kept) => kept.title === "igi2"));
  assert.equal((await stats.fetch(new Request("https://master/v1/leaderboard?title=igi9"))).status, 400);
}

// The totals kept before the games were apart, made again by game from the results.
{
  const at = Date.UTC(2026, 10, 10);
  const kept = (id, title, players) => ({ id, at, server: "Official", title, mode: "tdm", map: "1", seconds: 600, winner: 0, players: players.map((p) => ({ ...p, seconds: 600, score: p.kills * 10 + (p.won ? 50 : 0) })) });
  const seed = {
    "match:1": kept("1", "igi1", [line(BO, "Bo", 0, 4, 1, true), line(CY, "Cy", 1, 1, 4, false)]),
    "match:2": kept("2", "igi2", [line(BO, "Bo", 0, 0, 5, false), line(CY, "Cy", 1, 5, 0, true)]),
    [`tot:all:${BO}`]: { name: "Bo", matches: 2, wins: 1, kills: 4, deaths: 6, objectives: 0, seconds: 1200, score: 90 },
    [`tot:2026-11:${BO}`]: { name: "Bo", matches: 2, wins: 1, kills: 4, deaths: 6, objectives: 0, seconds: 1200, score: 90 },
    [`last:${BO}`]: ["match:2", "match:1"],
  };
  const storage = fakeStorage(seed);
  const stats = new Stats({ storage }, {}, () => now);
  const get = async (path) => (await stats.fetch(new Request(`https://master${path}`))).json();

  assert.equal((await get(`/v1/players/${BO}/stats?title=igi1`)).allTime.kills, 4);
  assert.equal((await get(`/v1/players/${BO}/stats?title=igi2`)).allTime.deaths, 5);
  assert.equal((await get(`/v1/players/${CY}/stats?title=igi2`)).allTime.wins, 1);
  assert.ok(!storage.store.has(`tot:all:${BO}`) && !storage.store.has(`last:${BO}`), "the shared totals are gone");
  assert.equal(storage.store.get("schema"), 3);
  assert.deepEqual(storage.store.get("seasons:igi1"), ["2026-11"]);
  assert.deepEqual(storage.store.get(`last:igi1:${BO}`), ["match:1"]);
}

// A player's own place wherever they stand, how many are ranked, and the seasons a game has had.
{
  const stats = new Stats({ storage: fakeStorage() }, {}, () => now);
  const get = async (path) => (await stats.fetch(new Request(`https://master${path}`))).json();
  await stats.fetch(new Request("https://master/v1/admin/stats-servers", { method: "POST", body: JSON.stringify({ key: wire, name: "Official" }) }));
  await stats.fetch(await signed(match("igi1", [line(BO, "Bo", 0, 7, 1, true), line(CY, "Cy", 1, 1, 7, false)])));

  const board = await get(`/v1/leaderboard?title=igi1&metric=kills&you=${CY}`);
  assert.equal(board.players, 2);
  assert.deepEqual([board.you.rank, board.you.name], [2, "Cy"]);
  assert.deepEqual(board.seasons, ["2026-11"]);
  assert.equal((await get("/v1/leaderboard?title=igi1&you=dddddddddddddddd")).you, null);
  assert.equal((await get("/v1/leaderboard?title=igi1")).you, null);
  // A game without a match yet still has this season to show.
  assert.deepEqual((await get("/v1/leaderboard?title=igi2")).seasons, ["2026-11"]);
}

console.log("titles: ok");
