// Match results on their own, in Node: `node test/matches.test.mjs`. A listed server's signed result is counted; any other
// server's is refused; a malformed one is refused; totals add up per season and all time; the board ranks by each metric
// (K/D only from five matches), gathers a linked account's keys under its name, and a record reads by key or account
// with the medals its totals have earned.
import assert from "node:assert/strict";
import { webcrypto as crypto } from "node:crypto";
import { Stats, checkResult, officialName, scoreOf, seasonOf } from "../src/stats.js";

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

async function serverKey() {
  const pair = await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"]);
  const raw = new Uint8Array(await crypto.subtle.exportKey("raw", pair.publicKey));
  return { pair, wire: base64(raw) };
}

async function signed(key, body) {
  const time = Math.floor(Date.now() / 1000);
  const bytes = text(JSON.stringify(body));
  const payload = text(`POST\n/v1/matches\n${time}\n${await sha(bytes)}`);
  const signature = base64(new Uint8Array(await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, key.pair.privateKey, payload)));
  return new Request("https://master/v1/matches", {
    method: "POST",
    body: bytes,
    headers: { "x-openigi-key": key.wire, "x-openigi-signature": signature, "x-openigi-time": String(time) },
  });
}

// Two keys of one Discord account ("Ana"), and two players with none.
const ANA_DESK = "aaaaaaaaaaaaaaaa";
const ANA_WEB = "aaaaaaaaaaaaaaab";
const BO = "bbbbbbbbbbbbbbbb";
const CY = "cccccccccccccccc";
const ana = { id: "111111111111111111", name: "Ana", avatar: "a", since: 1 };
const accounts = {
  idFromName: () => "all",
  get: () => ({
    async fetch(url) {
      const asked = new URL(url);
      if (asked.pathname === "/v1/players") {
        const ids = asked.searchParams.get("ids").split(",");
        return Response.json(Object.fromEntries(ids.filter((id) => id === ANA_DESK || id === ANA_WEB).map((id) => [id, ana])));
      }

      if (asked.pathname === `/internal/keys/${ana.id}`) return Response.json({ keys: [ANA_DESK, ANA_WEB] });
      return new Response("{}", { status: 404 });
    },
  }),
};

let now = Date.UTC(2026, 10, 17, 12);
const stats = new Stats({ storage: fakeStorage() }, { ACCOUNTS: accounts }, () => now);
const ask = (request) => stats.fetch(request);
const get = async (path) => (await ask(new Request(`https://master${path}`))).json();

const official = await serverKey();
const community = await serverKey();

// Registering the official server (the Worker checks the admin token before this object is asked).
const added = await ask(new Request("https://master/v1/admin/stats-servers", { method: "POST", body: JSON.stringify({ key: official.wire, name: "Official OpenIGI EU" }) }));
assert.equal(added.status, 201);
assert.equal((await get("/v1/admin/stats-servers")).length, 1);

const line = (player, name, team, kills, deaths, won, objectives = 0) => ({ player, name, team, kills, deaths, won, objectives });
const tdm = (players) => ({ title: "igi1", mode: "tdm", map: "1", seconds: 600, winner: 0, players });

// Another server's result doesn't count.
assert.equal((await ask(await signed(community, tdm([line(BO, "Bo", 0, 50, 0, true), line(CY, "Cy", 1, 0, 50, false)])))).status, 403);

// A listed server's does.
const first = await ask(await signed(official, tdm([line(ANA_DESK, "Ana", 0, 10, 2, true, 1), line(BO, "Bo", 1, 4, 6, false), line(null, "Guest", 1, 1, 3, false)])));
assert.equal(first.status, 201);
assert.equal((await first.json()).counted, 2);
assert.equal((await ask(await signed(official, { title: "igi1", mode: "tdm", map: "1", seconds: 600, players: [line(BO, "Bo", 0, 1, 1, true)] }))).status, 400);

// The same player from the browser, a few matches more; Cy plays five so K/D can rank them.
await ask(await signed(official, tdm([line(ANA_WEB, "Ana", 0, 6, 1, true), line(CY, "Cy", 1, 2, 6, false)])));
for (let i = 0; i < 5; i++) {
  await ask(await signed(official, tdm([line(CY, "Cy", 0, 3, 1, true), line(BO, "Bo", 1, 1, 3, false)])));
}

const latest = await get("/v1/matches");
assert.equal(latest.length, 7);
assert.equal(latest[latest.length - 1].players[0].score, scoreOf({ kills: 10, objectives: 1, won: true }));
assert.equal(latest[0].server, "Official OpenIGI EU");

// The season board by kills: Ana's two keys count as one, under her name.
const byKills = await get("/v1/leaderboard?period=season&metric=kills");
assert.equal(byKills.season, "2026-11");
assert.deepEqual(byKills.rows.map((row) => [row.rank, row.name, row.kills, row.matches]), [
  [1, "Cy", 17, 6],
  [2, "Ana", 16, 2],
  [3, "Bo", 9, 6],
]);
assert.equal(byKills.rows[1].account.id, ana.id);

// K/D ranks only those with five matches or more.
const byKd = await get("/v1/leaderboard?period=season&metric=kd");
assert.deepEqual(byKd.rows.map((row) => row.name), ["Cy", "Bo"]);
assert.equal((await ask(new Request("https://master/v1/leaderboard?metric=luck"))).status, 400);

// A record by account gathers its keys; by key, that key alone.
const anaRecord = await get(`/v1/players/${ana.id}/stats`);
assert.equal(anaRecord.allTime.kills, 16);
assert.equal(anaRecord.allTime.wins, 2);
assert.equal(anaRecord.allTime.kd, 5.33);
assert.equal(anaRecord.latest.length, 2);
assert.equal(anaRecord.latest[0].you[0].player, ANA_WEB);
assert.equal((await get(`/v1/players/${ANA_DESK}/stats`)).allTime.kills, 10);
assert.equal((await ask(new Request("https://master/v1/players/nobody/stats"))).status, 404);

// Next month is a new season; all time keeps everything.
now = Date.UTC(2026, 11, 2);
await ask(await signed(official, tdm([line(BO, "Bo", 0, 9, 0, true), line(CY, "Cy", 1, 0, 9, false)])));
const december = await get("/v1/leaderboard?metric=kills");
assert.deepEqual(december.rows.map((row) => [row.name, row.kills]), [["Bo", 9], ["Cy", 0]]);
const november = await get("/v1/leaderboard?period=2026-11&metric=kills");
assert.equal(november.rows[0].name, "Cy");
const allTime = await get("/v1/leaderboard?period=all&metric=kills");
assert.equal(allTime.rows.find((row) => row.name === "Bo").kills, 18);
assert.equal(seasonOf(Date.UTC(2027, 0, 1)), "2027-01");

// Medals follow from all-time totals: Ana has a kill and a win; Bo's 9-0 win is a Ghost, kept across seasons.
assert.deepEqual(anaRecord.medals.map((medal) => medal.id), ["first-kill", "first-win"]);
assert.ok((await get(`/v1/players/${BO}/stats`)).medals.some((medal) => medal.id === "ghost" && medal.name === "Ghost"));
assert.ok(!(await get(`/v1/players/${CY}/stats`)).medals.some((medal) => medal.id === "ghost"));

// A malformed result is refused before anything is counted.
assert.throws(() => checkResult({ title: "igi9", mode: "tdm", map: "1", seconds: 1, players: [] }));
assert.throws(() => checkResult({ title: "igi1", mode: "tdm", map: "1", seconds: 1, players: [line(BO, "Bo", 0, -1, 0, true), line(CY, "Cy", 1, 0, 0, false)] }));

// A deleted account's keys are forgotten: off every board, no record, their lines in kept results made anonymous.
const forgot = await ask(new Request("https://master/internal/forget", { method: "POST", body: JSON.stringify({ keys: [BO] }) }));
assert.equal((await forgot.json()).forgotten, 1);
assert.ok(!(await get("/v1/leaderboard?period=all&metric=kills")).rows.some((row) => row.name === "Bo"));
assert.equal((await get(`/v1/players/${BO}/stats`)).allTime.matches, 0);
const kept = await get("/v1/matches");
assert.ok(kept.every((match) => match.players.every((p) => p.player !== BO && p.name !== "Bo")));
assert.ok(kept.some((match) => match.players.some((p) => p.player === CY)));

// A heartbeat signed by the registered server's key names it official; another key's, or an unsigned one, doesn't.
const env = { STATS: { idFromName: () => "all", get: () => ({ fetch: (url) => stats.fetch(new Request(url)) }) } };
const beat = { port: 26000, name: "Official EU" };
const official1 = async (request) => officialName(request, new URL(request.url), new Uint8Array(await request.clone().arrayBuffer()), env);
assert.equal(await official1(await signed(official, beat, "/v1/servers/heartbeat")), "Official OpenIGI EU");
assert.equal(await official1(await signed(community, beat, "/v1/servers/heartbeat")), null);
assert.equal(await official1(new Request("https://master/v1/servers/heartbeat", { method: "POST", body: "{}" })), null);
// Signed for another path: not good for this one.
const elsewhere = await signed(official, beat, "/v1/matches");
assert.equal(await officialName(elsewhere, new URL("https://master/v1/servers/heartbeat"), new Uint8Array(await elsewhere.arrayBuffer()), env), null);

// Removing a server: its next result is refused.
const keyId = (await get("/v1/admin/stats-servers"))[0].keyId;
await ask(new Request(`https://master/v1/admin/stats-servers/${keyId}`, { method: "DELETE" }));
assert.equal((await ask(await signed(official, tdm([line(BO, "Bo", 0, 1, 0, true), line(CY, "Cy", 1, 0, 1, false)])))).status, 403);

console.log("matches: ok");
