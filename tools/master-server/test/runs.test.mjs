// Runs and their boards on their own, in Node: `node --import ./test/cf-stub.mjs test/runs.test.mjs`. Only a linked account's
// run is taken, one at a time and thirty a day, and only when it betters the player's best and would place; only a registered
// checker's verdict puts it on a board, as the checker found it and only if it is the run sent; the boards rank by time and by
// rank, a mission's and all of them together; and the room is bounded: a player's old best let go when bettered, a run below
// the kept top keeping its numbers alone, forty replays an account, the budget kept by letting the lowest-placed go (a board's
// top three never), a run never checked let go after a day, and an account deleted taking its runs with it. What a person is
// to look at is queued: a new first place, a run that reached the boards sooner than it takes to play, a run reported.
import assert from "node:assert/strict";
import { webcrypto as crypto } from "node:crypto";
import { ACCOUNT_CAP, CLOCK_SLACK_MS, DAILY, KEPT, PENDING_MS, REPORTS_DAILY, Runs } from "../src/runs.js";

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
const hex = (bytes) => [...new Uint8Array(bytes)].map((b) => b.toString(16).padStart(2, "0")).join("");
const sha = async (bytes) => hex(await crypto.subtle.digest("SHA-256", bytes));

async function makeKey() {
  const pair = await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"]);
  const raw = new Uint8Array(await crypto.subtle.exportKey("raw", pair.publicKey));
  return { pair, wire: base64(raw), id: (await sha(raw)).slice(0, 16) };
}

let now = Date.UTC(2026, 10, 24, 12);

async function signed(key, method, pathAndQuery, body = new Uint8Array()) {
  const url = new URL(`https://master${pathAndQuery}`);
  const time = Math.floor(now / 1000);
  const payload = new TextEncoder().encode(`${method}\n${url.pathname}\n${time}\n${await sha(body)}`);
  const signature = base64(new Uint8Array(await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, key.pair.privateKey, payload)));
  return new Request(url, {
    method,
    body: method === "GET" ? undefined : body,
    headers: { "x-openigi-key": key.wire, "x-openigi-signature": signature, "x-openigi-time": String(time) },
  });
}

// Players: each key its own account, named; one key with no account.
const players = [];
for (let i = 0; i < 30; i++) {
  const key = await makeKey();
  players.push({ key, account: { id: String(100000 + i), name: `Player ${i}` } });
}

const stranger = await makeKey();
const accounts = {
  idFromName: () => "all",
  get: () => ({
    async fetch(url) {
      const ids = new URL(url).searchParams.get("ids")?.split(",") ?? [];
      return Response.json(Object.fromEntries(players.filter((p) => ids.includes(p.key.id)).map((p) => [p.key.id, p.account])));
    },
  }),
};

const storage = fakeStorage();
const env = { ACCOUNTS: accounts, REPLAY_BUDGET_BYTES: String(64 * 1024 * 1024) };
const runs = new Runs({ storage }, env, () => now);
const ask = (request) => runs.fetch(request);
const get = async (path) => (await ask(new Request(`https://master${path}`))).json();
const bytes = (size, fill = 7) => new Uint8Array(size).fill(fill);

const checker = await makeKey();
const added = await ask(new Request("https://master/v1/admin/run-verifiers", { method: "POST", body: JSON.stringify({ key: checker.wire, name: "sh-de" }) }));
assert.equal(added.status, 201);

const claims = (mission, ticks, rank, extra = "") => `/v1/runs?title=igi1&mission=${mission}&difficulty=2&ruleset=modern&ticks=${ticks}&rank=${rank}${extra}`;
const submit = async (player, mission, ticks, rank, size = 1000) => ask(await signed(player.key, "POST", claims(mission, ticks, rank), bytes(size)));

// The checker's verdict on the next run waiting: as given, or by default verified at the claimed numbers.
async function check(verdictFor = () => ({}), canonicalSize = 1500) {
  const next = await ask(await signed(checker, "GET", "/v1/verifier/next"));
  assert.equal(next.status, 200);
  const id = next.headers.get("x-openigi-run");
  const run = await get(`/v1/runs/${id}`);
  const stored = await storage.get(`run:${id}`);
  const verdict = {
    verified: true,
    reason: "",
    title: run.title,
    mission: run.mission,
    difficulty: run.difficulty,
    ruleset: run.ruleset,
    player: stored.key,
    score: { ticks: stored.ticks },
    rank: stored.rank,
    ...verdictFor(stored),
  };
  const json = new TextEncoder().encode(JSON.stringify(verdict));
  const body = new Uint8Array(4 + json.length + canonicalSize);
  new DataView(body.buffer).setUint32(0, json.length, true);
  body.set(json, 4);
  body.fill(9, 4 + json.length);
  const answer = await ask(await signed(checker, "POST", `/v1/verifier/runs/${id}`, body));
  return { id, ...(await answer.json()) };
}

// Unsigned, unlinked, or a malformed claim: refused.
assert.equal((await ask(new Request(`https://master${claims(1, 900, 2)}`, { method: "POST", body: bytes(10) }))).status, 401);
assert.equal((await ask(await signed(stranger, "POST", claims(1, 900, 2), bytes(10)))).status, 403);
assert.equal((await ask(await signed(players[0].key, "POST", claims(1, 900, 2).replace("modern", "anything"), bytes(10)))).status, 400);
assert.equal((await ask(await signed(players[0].key, "POST", claims(1, 900, 2), bytes(1024 * 1024 + 1)))).status, 413);

// A linked account's run: waiting, and one at a time.
const first = await submit(players[0], 1, 900, 2);
assert.equal(first.status, 202);
assert.equal((await submit(players[0], 2, 900, 2)).status, 409);

// Only a registered checker gets the runs waiting.
assert.equal((await ask(await signed(stranger, "GET", "/v1/verifier/next"))).status, 403);

// Checked: on the board at the checker's numbers, its replay the checker's.
const checked = await check((run) => ({ score: { ticks: 870 }, rank: 2 }));
assert.equal(checked.status, "verified");
let board = await get("/v1/runs/board?title=igi1&mission=1&difficulty=2&ruleset=modern&metric=time");
assert.deepEqual(board.rows.map((r) => [r.place, r.name, r.ticks, r.replay]), [[1, "Player 0", 870, true]]);
const replay = new Uint8Array(await (await ask(new Request(`https://master/v1/runs/${checked.id}/replay`))).arrayBuffer());
assert.equal(replay.length, 1500);
assert.equal(replay[0], 9);
assert.equal((await storage.get("bytes")), 1500);

// A run the checker found to be another player's, or of another board, is refused, and let go of at once.
assert.equal((await submit(players[1], 1, 950, 1)).status, 202);
const stolen = await check(() => ({ player: players[0].key.id }));
assert.equal(stolen.status, "rejected");
assert.match(stolen.reason, /another player/);
assert.equal((await storage.get("bytes")), 1500);
assert.equal((await get(`/v1/runs/${stolen.id}`)).status, "rejected");

// No better than the player's own best: not taken. Better: taken, and the old best let go once it is no best of theirs.
assert.equal((await submit(players[0], 1, 880, 1)).status, 409);
assert.equal((await submit(players[0], 1, 800, 2)).status, 202);
const better = await check();
board = await get("/v1/runs/board?title=igi1&mission=1&difficulty=2&ruleset=modern&metric=time");
assert.deepEqual(board.rows.map((r) => [r.ticks, r.run]), [[800, better.id]]);
assert.equal((await ask(new Request(`https://master/v1/runs/${checked.id}`))).status, 404);
assert.equal((await storage.get("bytes")), 1500);

// A faster run of a lower rank: the player's time and rank rows are two runs, both kept.
assert.equal((await submit(players[0], 1, 700, 1)).status, 202);
const faster = await check();
const byRank = await get("/v1/runs/board?title=igi1&mission=1&difficulty=2&ruleset=modern&metric=rank");
assert.deepEqual(byRank.rows.map((r) => [r.rank, r.run]), [[2, better.id]]);
board = await get("/v1/runs/board?title=igi1&mission=1&difficulty=2&ruleset=modern&metric=time");
assert.deepEqual(board.rows.map((r) => [r.ticks, r.run]), [[700, faster.id]]);
assert.equal((await storage.get("bytes")), 3000);

// A board's top KEPT keep their replays; one pushed below keeps its row and loses its replay. Each player brings mission 3 a
// run, slower and slower, then one more of the fastest comes.
for (let i = 1; i <= KEPT; i++) {
  now += 1000;
  assert.equal((await submit(players[i], 3, 1000 + i * 10, 0)).status, 202, `player ${i}`);
  await check();
}

board = await get("/v1/runs/board?title=igi1&mission=3&difficulty=2&ruleset=modern&metric=time");
assert.equal(board.players, KEPT);
assert.ok(board.rows.every((r) => r.replay));

// Would not place: slower than the twentieth by time, and no better rank.
assert.equal((await submit(players[KEPT + 1], 3, 5000, -1)).status, 409);

// Places, by time and by rank: in, and the old twentieth's replay -- twentieth no longer on either -- let go, its row kept.
assert.equal((await submit(players[KEPT + 1], 3, 1001, 1)).status, 202);
await check();
board = await get("/v1/runs/board?title=igi1&mission=3&difficulty=2&ruleset=modern&metric=time");
assert.equal(board.players, KEPT + 1);
assert.equal(board.rows[0].name, `Player ${KEPT + 1}`);
assert.equal(board.rows.at(-1).replay, false);
assert.ok(board.rows.slice(0, KEPT).every((r) => r.replay));

// The overall board: by time only those who ran every mission the board has; you found wherever you are.
const overall = await get(`/v1/runs/board?title=igi1&mission=all&difficulty=2&ruleset=modern&metric=time&you=${players[0].account.id}`);
assert.equal(overall.players, 0);
assert.equal(overall.you, null);
const overallRank = await get(`/v1/runs/board?title=igi1&mission=all&difficulty=2&ruleset=modern&metric=rank&you=${players[0].account.id}`);
assert.equal(overallRank.you.points, 3);

// Thirty a day; the next day thirty again.
const busy = players[29];
for (let i = 0; i < DAILY; i++) {
  now += 1000;
  assert.equal((await submit(busy, 100 + i, 900, 1, 100)).status, 202, `run ${i}`);
  await check(() => ({ verified: false, reason: "test" }));
}

assert.equal((await submit(busy, 200, 900, 1, 100)).status, 429);
now += 24 * 3600 * 1000;
assert.equal((await submit(busy, 200, 900, 1, 100)).status, 202);

// Never checked within a day: let go of, and the player may send again.
now += PENDING_MS + 1;
assert.equal((await submit(busy, 201, 900, 1, 100)).status, 202);
assert.equal((await get("/v1/admin/runs/storage")).pending, 1);
assert.equal((await ask(await signed(checker, "GET", "/v1/verifier/next"))).status, 200);

// Forty replays an account: its lowest-placed let go past them.
const many = players[28];
for (let i = 0; i < ACCOUNT_CAP + 2; i++) {
  // Thirty a day: a day passes between each thirty.
  now += i > 0 && i % DAILY === 0 ? 24 * 3600 * 1000 : 1000;
  assert.equal((await submit(many, 300 + i, 900, 1, 200)).status, 202, `run ${i}`);
  await check(() => ({}), 200);
}

let keptOfMany = 0;
for (const [, run] of await storage.list({ prefix: "run:" })) if (run.account === many.account.id && run.kept) keptOfMany++;
assert.equal(keptOfMany, ACCOUNT_CAP);

// The budget: a new run makes room by letting the lowest-placed go, never a board's top three; with only those left, refused.
const tight = new Runs({ storage: fakeStorage() }, { ACCOUNTS: accounts, REPLAY_BUDGET_BYTES: "10000" }, () => now);
const tightAsk = (request) => tight.fetch(request);
await tightAsk(new Request("https://master/v1/admin/run-verifiers", { method: "POST", body: JSON.stringify({ key: checker.wire, name: "sh-de" }) }));
async function tightRun(player, ticks) {
  const sent = await tightAsk(await signed(player.key, "POST", claims(9, ticks, 0), bytes(2000)));
  if (sent.status !== 202) return sent.status;
  const next = await tightAsk(await signed(checker, "GET", "/v1/verifier/next"));
  const id = next.headers.get("x-openigi-run");
  const json = new TextEncoder().encode(JSON.stringify({ verified: true, title: "igi1", mission: 9, difficulty: 2, ruleset: "modern", player: player.key.id, score: { ticks }, rank: 0 }));
  const body = new Uint8Array(4 + json.length + 2000);
  new DataView(body.buffer).setUint32(0, json.length, true);
  body.set(json, 4);
  await tightAsk(await signed(checker, "POST", `/v1/verifier/runs/${id}`, body));
  return 202;
}

for (let i = 0; i < 5; i++) assert.equal(await tightRun(players[i], 1000 + i), 202, `tight ${i}`);
const tightBoard = async () => (await (await tightAsk(new Request("https://master/v1/runs/board?title=igi1&mission=9&difficulty=2&ruleset=modern&metric=time"))).json()).rows;
assert.deepEqual((await tightBoard()).map((r) => r.replay), [true, true, true, true, true]);
assert.equal(await tightRun(players[5], 999), 202);
assert.deepEqual((await tightBoard()).map((r) => r.replay), [true, true, true, true, true, false]);
assert.equal(await tightRun(players[6], 998), 202);
assert.deepEqual((await tightBoard()).map((r) => r.replay), [true, true, true, true, true, false, false]);

// Room for no more without one of the top three's: refused, the run kept on the player's machine.
const full = new Runs({ storage: fakeStorage() }, { ACCOUNTS: accounts, REPLAY_BUDGET_BYTES: "5000" }, () => now);
await full.fetch(new Request("https://master/v1/admin/run-verifiers", { method: "POST", body: JSON.stringify({ key: checker.wire, name: "sh-de" }) }));
for (let i = 0; i < 2; i++) assert.equal((await full.fetch(await signed(players[i].key, "POST", claims(9, 1000 + i, 0), bytes(2000)))).status, 202);
assert.equal((await full.fetch(await signed(players[2].key, "POST", claims(9, 900, 0), bytes(2000)))).status, 507);

// The review: a fresh store, its runs checked with the player's clock given (recordedAt).
const watchedStore = fakeStorage();
const watched = new Runs({ storage: watchedStore }, env, () => now);
const watchedAsk = (request) => watched.fetch(request);
await watchedAsk(new Request("https://master/v1/admin/run-verifiers", { method: "POST", body: JSON.stringify({ key: checker.wire, name: "sh-de" }) }));
async function watchedRun(player, ticks, recordedAt) {
  assert.equal((await watchedAsk(await signed(player.key, "POST", claims(4, ticks, 1), bytes(800)))).status, 202);
  const next = await watchedAsk(await signed(checker, "GET", "/v1/verifier/next"));
  const id = next.headers.get("x-openigi-run");
  const verdict = { verified: true, title: "igi1", mission: 4, difficulty: 2, ruleset: "modern", player: player.key.id, score: { ticks }, rank: 1, recordedAt };
  const json = new TextEncoder().encode(JSON.stringify(verdict));
  const body = new Uint8Array(4 + json.length + 900);
  new DataView(body.buffer).setUint32(0, json.length, true);
  body.set(json, 4);
  assert.equal((await (await watchedAsk(await signed(checker, "POST", `/v1/verifier/runs/${id}`, body))).json()).status, "verified");
  return id;
}

const review = async () => (await watchedAsk(new Request("https://master/v1/admin/runs/review"))).json();

// Ten minutes of play begun ten minutes and more before it arrived: honest, but the board's first place, so looked at.
const honest = await watchedRun(players[10], 30 * 600, now - 11 * 60 * 1000);
assert.deepEqual((await review()).map((e) => [e.id, e.why]), [[honest, ["a new first place"]]]);

// A slower run in second place, its ten minutes begun a minute before it arrived (past the clock's slack): flagged.
const forged = await watchedRun(players[11], 30 * 610, now - 60 * 1000);
assert.ok(CLOCK_SLACK_MS < 9 * 60 * 1000);
assert.deepEqual((await review()).find((e) => e.id === forged)?.why, ["it reached the boards sooner after it began than it takes to play"]);

// Its clock a little off and nothing more: not flagged, and not first either.
const skewed = await watchedRun(players[12], 30 * 620, now - (30 * 620 / 30) * 1000 + CLOCK_SLACK_MS / 2);
assert.equal((await review()).some((e) => e.id === skewed), false);

// A report: signed by a linked account, not of its own run, once a run each, REPORTS_DAILY a day.
const report = async (player, id, reason) => watchedAsk(await signed(player.key, "POST", `/v1/runs/${id}/report`, new TextEncoder().encode(JSON.stringify({ reason }))));
assert.equal((await report({ key: stranger }, skewed, "fast")).status, 403);
assert.equal((await report(players[12], skewed, "mine")).status, 409);
assert.equal((await report(players[13], "0".repeat(32), "nothing")).status, 404);
assert.equal((await report(players[13], skewed, "walks\nthrough the fence")).status, 200);
assert.equal((await report(players[13], skewed, "again")).status, 200);
const reported = (await review()).find((e) => e.id === skewed);
assert.deepEqual([reported.why, reported.reporters], [["reported by Player 13: walks through the fence"], [players[13].account.id]]);
const holder = await watchedStore.get(`acct:${players[13].account.id}`);
await watchedStore.put(`acct:${players[13].account.id}`, { ...holder, reports: { ...holder.reports, count: REPORTS_DAILY } });
assert.equal((await report(players[13], honest, "one too many")).status, 429);

// Looked at: taken off the review, the run still on its board; a run removed takes its place in the review with it.
assert.equal((await (await watchedAsk(new Request(`https://master/v1/admin/runs/${honest}/cleared`, { method: "POST" }))).json()).cleared, honest);
assert.equal((await review()).some((e) => e.id === honest), false);
assert.equal((await (await watchedAsk(new Request("https://master/v1/runs/board?title=igi1&mission=4&difficulty=2&ruleset=modern&metric=time"))).json()).rows[0].run, honest);
await watchedAsk(new Request(`https://master/v1/admin/runs/${forged}`, { method: "DELETE" }));
assert.equal((await review()).some((e) => e.id === forged), false);

// A checker is handed only its own build's runs; one naming no build, any.
const builds = new Runs({ storage: fakeStorage() }, env, () => now);
await builds.fetch(new Request("https://master/v1/admin/run-verifiers", { method: "POST", body: JSON.stringify({ key: checker.wire, name: "sh-de" }) }));
assert.equal((await builds.fetch(await signed(players[20].key, "POST", claims(5, 900, 1, "&version=0.7.0"), bytes(500)))).status, 202);
assert.equal((await builds.fetch(await signed(players[20].key, "POST", claims(5, 900, 1, "&version=not a version"), bytes(500)))).status, 400);
assert.equal((await builds.fetch(await signed(checker, "GET", "/v1/verifier/next?version=0.6.9"))).status, 204);
assert.equal((await builds.fetch(await signed(checker, "GET", "/v1/verifier/next?version=0.7.0"))).status, 200);

// An account deleted: every run of its keys gone.
assert.equal((await (await ask(new Request("https://master/internal/forget", { method: "POST", body: JSON.stringify({ keys: [players[0].key.id] }) }))).json()).forgotten, 1);
board = await get("/v1/runs/board?title=igi1&mission=1&difficulty=2&ruleset=modern&metric=time");
assert.equal(board.players, 0);
assert.equal((await ask(new Request(`https://master/v1/runs/${faster.id}`))).status, 404);

console.log("runs: ok");
