// Single-player runs and their boards. A player submits a replay of a completed mission (signed by their installation's key,
// which must belong to a Discord-linked account); a checker we run plays it again in the game itself and sends its verdict
// (signed by its own registered key); only what the checker found goes on the boards -- the score the level came to there,
// never the file's claim -- and a race against a board's run shows the ghost the checker saw.
//
//   POST /v1/runs?title=&mission=&difficulty=&ruleset=&ticks=&rank=   a replay (.oigr, at most 1 MB), signed by the player's
//                                         key; the claims only decide whether it is worth keeping and checking (below)
//   GET  /v1/runs/board?title=&mission=&difficulty=&ruleset=&metric=&you=   the best of one board: mission a number, or all
//                                         (each player's total over every mission the board has); metric time (fewest ticks)
//                                         or rank (highest rank, then fewest ticks); with how many are ranked (players) and,
//                                         with you (an account's id), that player's own row wherever it stands
//   GET  /v1/runs/<id>                    a run: whose, of what, its status and, checked, its score and whether its replay is kept
//   GET  /v1/runs/<id>/replay             a checked run's replay with the checker's ghost, while it is kept
//   GET  /v1/players/<account>/runs?title=   a player's bests in one game, board by board
//   GET  /v1/verifier/next                the next run to check, its replay's bytes (x-openigi-run says which), or 204: signed
//                                         by a registered checker's key
//   POST /v1/verifier/runs/<id>           the checker's verdict: the verdict's JSON's length (four bytes, little-endian), the JSON
//                                         (OpenIGI.Server -verify's), and a verified run's replay as the checker wrote it again
//   POST /internal/forget { keys }        an account deleted (accounts.js): every run of its keys gone
//   /v1/admin/runs...                     (admin token, checked by the Worker) GET storage, GET ?status=, DELETE /<id>,
//                                         POST bar { account }; /v1/admin/run-verifiers: GET, POST { key, name }, DELETE /<keyId>
//
// Storage is bounded, as the server's disk is: only a run that betters the player's own best on its board and would place in
// the board's top KEPT is taken; a player keeps one run a board (their best time and their best rank, one run where it is
// both); a board's top KEPT on either metric keep their replays and the rows below keep only their numbers; an account keeps
// ACCOUNT_CAP replays at most; and the whole is held under REPLAY_BUDGET_BYTES, the lowest-placed replays let go first and a
// board's top PROTECTED never. A run waiting to be checked is let go after a day; a refused one at once.
//
// One Durable Object ("all"), in key-value storage:
//   run:<id>            { id, title, mission, difficulty, ruleset, key, account, name, submitted, status, reason, ticks, rank,
//                         score, size, chunks, kept, taken }
//   blob:<id>:<n>       a run's replay, in chunks (blobs.js)
//   board:<title>:<mission>:<difficulty>:<ruleset>   [{ account, name, time: { run, ticks, at }, best: { run, rank, ticks, at } }]
//   boards              every board's key
//   acct:<account>      { day, count, pending, barred }
//   pending             the runs waiting, oldest first
//   verifier:<keyId>    { name, added }
//   bytes               the bytes of every replay kept or waiting

import { dropBlob, getBlob, putBlob } from "./blobs.js";
import { cleanName } from "./names.js";
import { Refusal, json, signedBy } from "./signed.js";

export const TITLES = ["igi1", "igi2"];
export const RULESETS = ["classic", "modern"];
export const METRICS = ["time", "rank"];
export const KEPT = 20;
export const PROTECTED = 3;
export const ACCOUNT_CAP = 40;
export const MAX_TAPE = 1024 * 1024;
export const MAX_CHECKED = 4 * 1024 * 1024;
export const DAILY = 30;
export const PENDING_MS = 24 * 3600 * 1000;
export const LEASE_MS = 15 * 60 * 1000;
export const DEFAULT_BUDGET = 2 * 1024 * 1024 * 1024;
const BOARD_SIZE = 50;

const boardKey = (title, mission, difficulty, ruleset) => `board:${title}:${mission}:${difficulty}:${ruleset}`;
const dayOf = (time) => new Date(time).toISOString().slice(0, 10);

// The Worker's side: every runs request goes to the one object that keeps them.
export async function handleRuns(request, env) {
  if (!env.RUNS) {
    return json({ error: "Runs are not served here." }, 503);
  }

  return env.RUNS.get(env.RUNS.idFromName("all")).fetch(request);
}

// Whether a path is one of the runs'.
export function isRunsPath(pathname) {
  return (
    pathname === "/v1/runs" ||
    pathname.startsWith("/v1/runs/") ||
    pathname.startsWith("/v1/verifier/") ||
    /^\/v1\/players\/[^/]+\/runs$/.test(pathname)
  );
}

// Whether an admin path is one of the runs'.
export function isRunsAdminPath(pathname) {
  return pathname.startsWith("/v1/admin/runs") || pathname.startsWith("/v1/admin/run-verifiers");
}

// A board's rows by a metric, best first: the fewest ticks, or the highest rank and then the fewest ticks; the earlier on a tie.
export function ranked(entries, metric) {
  if (metric === "time") {
    return entries.filter((e) => e.time).sort((a, b) => a.time.ticks - b.time.ticks || a.time.at - b.time.at);
  }

  return entries.filter((e) => e.best).sort((a, b) => b.best.rank - a.best.rank || a.best.ticks - b.best.ticks || a.best.at - b.best.at);
}

// Whether a claim betters a row on a metric.
function betters(claim, row, metric) {
  if (!row) return true;
  return metric === "time" ? claim.ticks < row.ticks : claim.rank > row.rank || (claim.rank === row.rank && claim.ticks < row.ticks);
}

// The runs a board keeps the replays of, and those it never lets go: its top KEPT and top PROTECTED on either metric.
function placed(entries, count) {
  const runs = new Set();
  for (const metric of METRICS) {
    for (const row of ranked(entries, metric).slice(0, count)) runs.add(metric === "time" ? row.time.run : row.best.run);
  }

  return runs;
}

// A run's best place on its board, either metric, from one; Infinity off it.
function placeOf(entries, run) {
  let best = Infinity;
  for (const metric of METRICS) {
    const at = ranked(entries, metric).findIndex((row) => (metric === "time" ? row.time.run : row.best.run) === run);
    if (at >= 0) best = Math.min(best, at + 1);
  }

  return best;
}

// A submission's claims, checked for shape: they only decide whether it is worth checking.
export function claimsOf(url) {
  const title = url.searchParams.get("title");
  const ruleset = url.searchParams.get("ruleset");
  const number = (name, min, max) => {
    const value = Number(url.searchParams.get(name));
    if (!Number.isInteger(value) || value < min || value > max) throw new Refusal(400, `${name} is a whole number from ${min} to ${max}.`);
    return value;
  };

  if (!TITLES.includes(title)) throw new Refusal(400, `A title is one of ${TITLES.join(", ")}.`);
  if (!RULESETS.includes(ruleset)) throw new Refusal(400, `Only the ${RULESETS.join(" and ")} rules are ranked.`);
  return {
    title,
    ruleset,
    mission: number("mission", 1, 999),
    difficulty: number("difficulty", 0, 2),
    ticks: number("ticks", 1, 2 * 3600 * 30),
    rank: number("rank", -1, 99),
  };
}

// A checker's verdict and its replay, from the body it sends: the JSON's length, the JSON, the bytes.
export function verdictOf(body) {
  if (body.length < 4) throw new Refusal(400, "No verdict.");
  const length = body[0] | (body[1] << 8) | (body[2] << 16) | (body[3] << 24);
  if (length <= 0 || length > 64 * 1024 || 4 + length > body.length) throw new Refusal(400, "The verdict is not one.");
  let verdict;
  try {
    verdict = JSON.parse(new TextDecoder().decode(body.subarray(4, 4 + length)));
  } catch {
    throw new Refusal(400, "The verdict is not JSON.");
  }

  return { verdict, replay: body.subarray(4 + length) };
}

export class Runs {
  constructor(state, env, now = () => Date.now()) {
    this.state = state;
    this.env = env;
    this.storage = state.storage;
    this.now = now;
  }

  get budget() {
    const asked = Number(this.env.REPLAY_BUDGET_BYTES);
    return Number.isFinite(asked) && asked > 0 ? asked : DEFAULT_BUDGET;
  }

  async fetch(request) {
    const url = new URL(request.url);
    const parts = url.pathname.split("/").filter(Boolean);
    try {
      if (parts[0] === "internal" && parts[1] === "forget" && request.method === "POST") {
        return json({ forgotten: await this.forget((await request.json()).keys ?? []) });
      }

      if (parts[1] === "admin") return await this.admin(request, parts);
      if (parts[1] === "verifier" && parts[2] === "next" && request.method === "GET") return await this.next(request, url);
      if (parts[1] === "verifier" && parts[2] === "runs" && parts[3] && request.method === "POST") return await this.verdict(request, url, parts[3]);
      if (parts[1] === "players" && parts[3] === "runs") return json(await this.bestsOf(parts[2], url.searchParams.get("title") ?? TITLES[0]));
      if (parts[1] === "runs" && parts.length === 2 && request.method === "POST") return await this.submit(request, url);
      if (parts[1] === "runs" && parts[2] === "board") return json(await this.board(url));
      if (parts[1] === "runs" && parts[2] && parts[3] === "replay") return await this.replay(parts[2]);
      if (parts[1] === "runs" && parts[2] && parts.length === 3) return await this.one(parts[2]);
      return json({ error: "Not found." }, 404);
    } catch (error) {
      if (error instanceof Refusal) return json({ error: error.message }, error.status);
      throw error;
    }
  }

  // A run submitted: from a linked account, worth keeping by its claims, within the account's limits and the server's room.
  async submit(request, url) {
    const body = new Uint8Array(await request.arrayBuffer());
    if (body.length > MAX_TAPE) throw new Refusal(413, "A replay is at most 1 MB.");
    const key = await signedBy(request, url, body, this.now());
    const account = (await this.accountsOf([key.id]))[key.id];
    if (!account) throw new Refusal(403, "Sign in with Discord to put runs on the boards.");

    await this.sweep();
    const claims = claimsOf(url);
    const holder = (await this.storage.get(`acct:${account.id}`)) ?? {};
    const today = dayOf(this.now());
    if (holder.barred) throw new Refusal(403, "This account's runs are not taken.");
    if (holder.day === today && holder.count >= DAILY) throw new Refusal(429, `At most ${DAILY} runs a day.`);
    if (holder.pending && (await this.storage.get(`run:${holder.pending}`))?.status === "pending") {
      throw new Refusal(409, "A run of yours is being checked: send the next when it is done.");
    }

    // Only a run that betters the player's own best and would place in the board's kept top is worth the room.
    const entries = (await this.storage.get(boardKey(claims.title, claims.mission, claims.difficulty, claims.ruleset))) ?? [];
    const mine = entries.find((e) => e.account === account.id);
    const improves = betters(claims, mine?.time, "time") || betters(claims, mine?.best, "rank");
    if (!improves) throw new Refusal(409, "It is no better than your best on this board; it stays on your machine.");
    const places = METRICS.some((metric) => {
      const rows = ranked(entries.filter((e) => e.account !== account.id), metric);
      return rows.length < KEPT || betters(claims, metric === "time" ? rows[KEPT - 1].time : rows[KEPT - 1].best, metric);
    });
    if (!places) throw new Refusal(409, `It would not place in the top ${KEPT}; it stays on your machine.`);

    if (!(await this.makeRoom(body.length))) throw new Refusal(507, "The boards are full for now; it stays on your machine.");

    const id = crypto.randomUUID().replaceAll("-", "");
    const chunks = await putBlob(this.storage, `blob:${id}`, body);
    const run = {
      id,
      ...claims,
      key: key.id,
      account: account.id,
      name: cleanName(account.name),
      submitted: this.now(),
      status: "pending",
      reason: "",
      score: null,
      size: body.length,
      chunks,
      kept: true,
      taken: 0,
    };
    await this.storage.put({
      [`run:${id}`]: run,
      [`acct:${account.id}`]: { ...holder, day: today, count: holder.day === today ? holder.count + 1 : 1, pending: id },
      pending: [...((await this.storage.get("pending")) ?? []), id],
      bytes: ((await this.storage.get("bytes")) ?? 0) + body.length,
    });
    return json({ id, status: "pending" }, 202);
  }

  // The next run to check, to a registered checker: the oldest waiting that no checker has taken lately.
  async next(request, url) {
    await this.checker(request, url, new Uint8Array());
    await this.sweep();
    for (const id of (await this.storage.get("pending")) ?? []) {
      const run = await this.storage.get(`run:${id}`);
      if (run?.status !== "pending" || this.now() - (run.taken ?? 0) < LEASE_MS) continue;
      run.taken = this.now();
      await this.storage.put(`run:${id}`, run);
      return new Response(await getBlob(this.storage, `blob:${id}`, run.chunks, run.size), {
        headers: { "content-type": "application/octet-stream", "x-openigi-run": id },
      });
    }

    return new Response(null, { status: 204 });
  }

  // A checker's verdict on a run: refused, let go of at once; verified, on its board as the checker found it.
  async verdict(request, url, id) {
    const body = new Uint8Array(await request.arrayBuffer());
    await this.checker(request, url, body);
    const run = await this.storage.get(`run:${id}`);
    if (!run || run.status !== "pending") throw new Refusal(404, "No such run waiting.");
    const { verdict, replay } = verdictOf(body);

    // What the checker found must be the run submitted: of what it said, by whose key it was signed.
    const mismatch =
      verdict.verified !== true ? String(verdict.reason || "it was refused")
      : verdict.player !== run.key ? "it was recorded by another player"
      : verdict.title !== run.title || verdict.mission !== run.mission || verdict.difficulty !== run.difficulty || verdict.ruleset !== run.ruleset
        ? "it is not of the board it was sent for"
      : !verdict.score || !Number.isInteger(verdict.score.ticks) || verdict.score.ticks <= 0 ? "the checker found no score"
      : replay.length === 0 || replay.length > MAX_CHECKED ? "the checker sent no replay"
      : null;
    if (mismatch) {
      await this.refuse(run, mismatch);
      return json({ id, status: "rejected", reason: mismatch });
    }

    // The checker's replay, with its ghost, in place of the one submitted.
    await dropBlob(this.storage, `blob:${id}`, run.chunks);
    const chunks = await putBlob(this.storage, `blob:${id}`, replay);
    await this.storage.put("bytes", ((await this.storage.get("bytes")) ?? 0) - run.size + replay.length);
    Object.assign(run, {
      status: "verified",
      verified: this.now(),
      ticks: verdict.score.ticks,
      rank: Number.isInteger(verdict.rank) ? verdict.rank : -1,
      score: verdict.score,
      size: replay.length,
      chunks,
    });
    await this.storage.put(`run:${id}`, run);
    await this.release(run.account);
    await this.place(run);
    return json({ id, status: "verified" });
  }

  // A verified run put on its board: the player's row bettered where it betters it, the runs no longer anyone's best let go,
  // and the board's and the account's replays held to what they keep.
  async place(run) {
    const key = boardKey(run.title, run.mission, run.difficulty, run.ruleset);
    const entries = (await this.storage.get(key)) ?? [];
    let row = entries.find((e) => e.account === run.account);
    if (!row) {
      row = { account: run.account, name: run.name, time: null, best: null };
      entries.push(row);
    }

    row.name = run.name;
    const was = new Set([row.time?.run, row.best?.run].filter(Boolean));
    const claim = { ticks: run.ticks, rank: run.rank };
    if (betters(claim, row.time, "time")) row.time = { run: run.id, ticks: run.ticks, at: run.verified };
    if (betters(claim, row.best, "rank")) row.best = { run: run.id, rank: run.rank, ticks: run.ticks, at: run.verified };
    await this.storage.put(key, entries);
    const boards = (await this.storage.get("boards")) ?? [];
    if (!boards.includes(key)) await this.storage.put("boards", [...boards, key]);

    // A run the player bettered on every metric is no longer on the board at all.
    const still = new Set([row.time?.run, row.best?.run]);
    for (const old of was) {
      if (!still.has(old)) await this.forgetRun(old);
    }

    if (!still.has(run.id)) await this.forgetRun(run.id);
    await this.holdBoard(entries);
    await this.holdAccount(run.account);
  }

  // A board's replays: its top KEPT on either metric keep theirs, the rows below keep their numbers alone.
  async holdBoard(entries) {
    const kept = placed(entries, KEPT);
    for (const row of entries) {
      for (const id of new Set([row.time?.run, row.best?.run].filter(Boolean))) {
        if (!kept.has(id)) await this.dropReplay(id);
      }
    }
  }

  // An account's replays: at most ACCOUNT_CAP, its lowest-placed let go first.
  async holdAccount(account) {
    const mine = [];
    for (const key of (await this.storage.get("boards")) ?? []) {
      const entries = (await this.storage.get(key)) ?? [];
      const row = entries.find((e) => e.account === account);
      for (const id of new Set([row?.time?.run, row?.best?.run].filter(Boolean))) {
        const run = await this.storage.get(`run:${id}`);
        if (run?.kept) mine.push({ id, place: placeOf(entries, id) });
      }
    }

    mine.sort((a, b) => b.place - a.place);
    for (const { id } of mine.slice(0, Math.max(0, mine.length - ACCOUNT_CAP))) await this.dropReplay(id);
  }

  // Room for a new replay under the budget: the lowest-placed replays kept let go until it fits, never a board's top
  // PROTECTED. Whether it fits.
  async makeRoom(size) {
    let bytes = (await this.storage.get("bytes")) ?? 0;
    if (bytes + size <= this.budget) return true;
    const candidates = [];
    for (const key of (await this.storage.get("boards")) ?? []) {
      const entries = (await this.storage.get(key)) ?? [];
      const guarded = placed(entries, PROTECTED);
      for (const row of entries) {
        for (const id of new Set([row.time?.run, row.best?.run].filter(Boolean))) {
          if (!guarded.has(id)) candidates.push({ id, place: placeOf(entries, id) });
        }
      }
    }

    candidates.sort((a, b) => b.place - a.place);
    for (const { id } of candidates) {
      if (bytes + size <= this.budget) break;
      bytes -= await this.dropReplay(id);
    }

    return bytes + size <= this.budget;
  }

  // A run's replay let go of, its row kept. The bytes it freed.
  async dropReplay(id) {
    const run = await this.storage.get(`run:${id}`);
    if (!run?.kept) return 0;
    await dropBlob(this.storage, `blob:${id}`, run.chunks);
    run.kept = false;
    await this.storage.put({ [`run:${id}`]: run, bytes: ((await this.storage.get("bytes")) ?? 0) - run.size });
    return run.size;
  }

  // A run gone altogether: its replay and its record.
  async forgetRun(id) {
    await this.dropReplay(id);
    await this.storage.delete(`run:${id}`);
  }

  // A run refused: its replay let go of at once, the reason kept for its player to read.
  async refuse(run, reason) {
    await dropBlob(this.storage, `blob:${run.id}`, run.chunks);
    await this.storage.put({
      [`run:${run.id}`]: { ...run, status: "rejected", reason, kept: false },
      bytes: ((await this.storage.get("bytes")) ?? 0) - run.size,
    });
    await this.release(run.account);
  }

  // An account's run is checked: it may send the next.
  async release(account) {
    const holder = (await this.storage.get(`acct:${account}`)) ?? {};
    await this.storage.put(`acct:${account}`, { ...holder, pending: null });
  }

  // The runs waiting: those checked off the queue, and those a day old refused as never checked.
  async sweep() {
    const waiting = [];
    for (const id of (await this.storage.get("pending")) ?? []) {
      const run = await this.storage.get(`run:${id}`);
      if (run?.status !== "pending") continue;
      if (this.now() - run.submitted > PENDING_MS) {
        await this.refuse(run, "it was not checked within a day");
        continue;
      }

      waiting.push(id);
    }

    await this.storage.put("pending", waiting);
  }

  // One board's best: a mission's, or all its missions' together.
  async board(url) {
    const title = url.searchParams.get("title") ?? TITLES[0];
    const ruleset = url.searchParams.get("ruleset") ?? RULESETS[0];
    const metric = url.searchParams.get("metric") ?? METRICS[0];
    const difficulty = Number(url.searchParams.get("difficulty") ?? 1);
    const mission = url.searchParams.get("mission") ?? "all";
    const you = url.searchParams.get("you");
    if (!TITLES.includes(title) || !RULESETS.includes(ruleset) || !METRICS.includes(metric) || ![0, 1, 2].includes(difficulty)) {
      throw new Refusal(400, "Name a title, a ruleset, a metric and a difficulty the boards have.");
    }

    const rows = mission === "all" ? await this.overall(title, difficulty, ruleset, metric) : await this.missionRows(title, Number(mission), difficulty, ruleset, metric);
    const numbered = rows.map((row, i) => ({ place: i + 1, ...row }));
    return {
      title,
      mission,
      difficulty,
      ruleset,
      metric,
      players: numbered.length,
      rows: numbered.slice(0, BOARD_SIZE),
      you: you ? (numbered.find((row) => row.account === you) ?? null) : null,
    };
  }

  // A mission's rows by a metric: who, their ticks and rank, the run and whether its replay is kept to watch and race.
  async missionRows(title, mission, difficulty, ruleset, metric) {
    if (!Number.isInteger(mission) || mission < 1) throw new Refusal(400, "A mission is a number, or all.");
    const entries = (await this.storage.get(boardKey(title, mission, difficulty, ruleset))) ?? [];
    const rows = [];
    for (const row of ranked(entries, metric)) {
      const best = metric === "time" ? row.time : row.best;
      const run = await this.storage.get(`run:${best.run}`);
      rows.push({ account: row.account, name: row.name, ticks: best.ticks, rank: metric === "time" ? (run?.rank ?? -1) : best.rank, run: best.run, replay: run?.kept === true });
    }

    return rows;
  }

  // Every mission of a board together: by time, each player's ticks summed over every mission the board has, only those who
  // have run them all; by rank, their ranks' points summed (a rank's place, from one), then their ticks.
  async overall(title, difficulty, ruleset, metric) {
    const prefix = `board:${title}:`;
    const suffix = `:${difficulty}:${ruleset}`;
    const keys = ((await this.storage.get("boards")) ?? []).filter((k) => k.startsWith(prefix) && k.endsWith(suffix));
    const totals = new Map();
    for (const key of keys) {
      for (const row of (await this.storage.get(key)) ?? []) {
        const total = totals.get(row.account) ?? { account: row.account, name: row.name, ticks: 0, points: 0, missions: 0 };
        total.missions++;
        total.ticks += metric === "time" ? (row.time?.ticks ?? 0) : (row.best?.ticks ?? 0);
        total.points += (row.best?.rank ?? -1) + 1;
        totals.set(row.account, total);
      }
    }

    const all = [...totals.values()];
    if (metric === "time") {
      return all
        .filter((t) => t.missions === keys.length)
        .sort((a, b) => a.ticks - b.ticks)
        .map((t) => ({ account: t.account, name: t.name, ticks: t.ticks, missions: t.missions }));
    }

    return all.sort((a, b) => b.points - a.points || a.ticks - b.ticks).map((t) => ({ account: t.account, name: t.name, points: t.points, ticks: t.ticks, missions: t.missions }));
  }

  // One run, as anyone may see it.
  async one(id) {
    const run = await this.storage.get(`run:${id}`);
    if (!run) throw new Refusal(404, "No such run.");
    return json({
      id: run.id,
      title: run.title,
      mission: run.mission,
      difficulty: run.difficulty,
      ruleset: run.ruleset,
      account: run.account,
      name: run.name,
      status: run.status,
      reason: run.reason,
      ticks: run.status === "verified" ? run.ticks : null,
      rank: run.status === "verified" ? run.rank : null,
      score: run.status === "verified" ? run.score : null,
      replay: run.kept === true && run.status === "verified",
    });
  }

  // A checked run's replay, while it is kept.
  async replay(id) {
    const run = await this.storage.get(`run:${id}`);
    if (!run || run.status !== "verified" || !run.kept) throw new Refusal(404, "That replay is not kept.");
    return new Response(await getBlob(this.storage, `blob:${id}`, run.chunks, run.size), {
      headers: { "content-type": "application/octet-stream", "content-disposition": `attachment; filename="${id}.oigr"` },
    });
  }

  // A player's bests in one game, board by board.
  async bestsOf(account, title) {
    const out = [];
    for (const key of (await this.storage.get("boards")) ?? []) {
      const [, boardTitle, mission, difficulty, ruleset] = key.split(":");
      if (boardTitle !== title) continue;
      const row = ((await this.storage.get(key)) ?? []).find((e) => e.account === account);
      if (row) out.push({ mission: Number(mission), difficulty: Number(difficulty), ruleset, time: row.time, best: row.best });
    }

    return out;
  }

  // An account deleted: every run of its keys gone, from the boards and the storage.
  async forget(keys) {
    const gone = new Set(keys);
    const accounts = new Set();
    for (const [, run] of await this.storage.list({ prefix: "run:" })) {
      if (gone.has(run.key)) {
        accounts.add(run.account);
        await this.forgetRun(run.id);
      }
    }

    for (const account of accounts) await this.removeAccount(account);
    return accounts.size;
  }

  // An account's rows off every board, and its runs with them.
  async removeAccount(account) {
    for (const key of (await this.storage.get("boards")) ?? []) {
      const entries = (await this.storage.get(key)) ?? [];
      const row = entries.find((e) => e.account === account);
      if (!row) continue;
      for (const id of new Set([row.time?.run, row.best?.run].filter(Boolean))) await this.forgetRun(id);
      await this.storage.put(key, entries.filter((e) => e !== row));
    }
  }

  // The admin's routes (the Worker has checked the token).
  async admin(request, parts) {
    if (parts[2] === "run-verifiers") {
      if (request.method === "GET") return json([...(await this.storage.list({ prefix: "verifier:" }))].map(([k, v]) => ({ id: k.slice(9), ...v })));
      if (request.method === "POST") {
        const { key, name } = await request.json();
        const id = await this.keyIdOf(key);
        await this.storage.put(`verifier:${id}`, { name: String(name ?? "").slice(0, 64), added: this.now() });
        return json({ id }, 201);
      }

      if (request.method === "DELETE" && parts[3]) {
        await this.storage.delete(`verifier:${parts[3]}`);
        return json({ removed: parts[3] });
      }
    }

    if (parts[2] === "runs" && parts[3] === "storage") {
      let runs = 0;
      let kept = 0;
      for (const [, run] of await this.storage.list({ prefix: "run:" })) {
        runs++;
        if (run.kept) kept++;
      }

      return json({ bytes: (await this.storage.get("bytes")) ?? 0, budget: this.budget, runs, kept, pending: ((await this.storage.get("pending")) ?? []).length });
    }

    if (parts[2] === "runs" && parts[3] === "bar" && request.method === "POST") {
      const { account } = await request.json();
      const holder = (await this.storage.get(`acct:${account}`)) ?? {};
      await this.storage.put(`acct:${account}`, { ...holder, barred: true });
      await this.removeAccount(account);
      return json({ barred: account });
    }

    if (parts[2] === "runs" && parts[3] && request.method === "DELETE") {
      const run = await this.storage.get(`run:${parts[3]}`);
      if (!run) throw new Refusal(404, "No such run.");
      const key = boardKey(run.title, run.mission, run.difficulty, run.ruleset);
      const entries = (await this.storage.get(key)) ?? [];
      for (const row of entries) {
        if (row.time?.run === run.id) row.time = null;
        if (row.best?.run === run.id) row.best = null;
      }

      await this.storage.put(key, entries.filter((row) => row.time || row.best));
      await this.forgetRun(run.id);
      return json({ removed: run.id });
    }

    if (parts[2] === "runs" && request.method === "GET") {
      const status = new URL(request.url).searchParams.get("status");
      const out = [];
      for (const [, run] of await this.storage.list({ prefix: "run:" })) {
        if (!status || run.status === status) out.push(run);
      }

      return json(out.sort((a, b) => b.submitted - a.submitted).slice(0, 200));
    }

    return json({ error: "Not found." }, 404);
  }

  // A request from a registered checker, or a refusal.
  async checker(request, url, body) {
    const key = await signedBy(request, url, body, this.now());
    if (!(await this.storage.get(`verifier:${key.id}`))) throw new Refusal(403, "Not a checker of ours.");
    return key;
  }

  // A key's id as signed.js makes it, from the key as a request carries it.
  async keyIdOf(wire) {
    const bytes = Uint8Array.from(atob(String(wire ?? "")), (c) => c.charCodeAt(0));
    if (bytes.length === 0) throw new Refusal(400, "Give the checker's key.");
    const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", bytes));
    return [...digest].map((b) => b.toString(16).padStart(2, "0")).join("").slice(0, 16);
  }

  // The linked accounts of some keys, from the accounts' object: { <key>: account } for those linked.
  async accountsOf(keys) {
    if (!this.env.ACCOUNTS || keys.length === 0) return {};
    const answer = await this.env.ACCOUNTS.get(this.env.ACCOUNTS.idFromName("all")).fetch(`https://accounts/v1/players?ids=${keys.join(",")}`);
    return answer.ok ? await answer.json() : {};
  }
}
