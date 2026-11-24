// Match results and what they add up to. Only the servers we list (the official ones, and any we vouch for) report, each
// signing its results with its own key (signed.js); a listen host or a community server keeps its own scoreboard and
// counts nowhere here, so nobody's record can be made up on their own machine.
//
//   POST /v1/matches                      a server's result of a decided match, signed by its registered key
//   GET  /v1/matches                      the latest results, newest first
//   GET  /v1/leaderboard?title=&period=&metric=&you=  the best players of one game (title igi1 or igi2; igi1 when not said): period
//                                         season (this month, UTC), a month (2026-10) or all; metric score, kills, assists, wins,
//                                         kd or matches; with how many are ranked (players), the seasons the game has had
//                                         (seasons, newest first), and with you (an account's id or a key's) that player's own
//                                         row and place wherever they stand, top fifty or not (you, null when unranked)
//   GET  /v1/players/<id>/stats?title=    one player's record in one game (a key's id or an account's): this season, all
//                                         time, latest, and the medals earned (medals.js)
//   GET  /internal/servers/<keyId>        a registered server by its key's id ({ name }), or 404: what marks a server
//                                         official on the list (officialName); reached only from the Worker
//   POST /internal/forget { keys }          an account deleted (accounts.js): its keys' totals gone, and their name and id
//                                         taken out of every kept result (the line itself stays, so the others' add up);
//                                         reached only from the accounts' object, never from outside
//   /v1/admin/stats-servers               the servers whose matches count (admin token): GET lists, POST { key, name } adds,
//                                         DELETE /<keyId> removes
//
// A player is counted by the key they play under; a linked account (accounts.js) gathers its keys, so its record and its
// place on a board are its keys' together, under its Discord name. One Durable Object ("all"), in key-value storage:
//   server:<keyId>          { name, added }
//   match:<time>-<rand>     the result, as kept
//   recent                  the latest results' keys, newest first
//   tot:<title>:<period>:<player>   { name, matches, wins, kills, deaths, assists, objectives, seconds, score, flawless } in one
//                           game (assists counted from the servers that report them; a total kept before has none, read as nought)
//   last:<title>:<player>   the player's latest results in one game, their keys, newest first
//   seasons:<title>         the seasons (yyyy-MM) a game has had a match in, newest first
//   schema                  2 once the totals are kept by game, 3 once each game's seasons are listed (migrate)
//
// Each game is a board and a record of its own: Project I.G.I.'s and I.G.I. 2's matches never add up together.

import { isFlawless, medalsOf } from "./medals.js";
import { cleanName } from "./names.js";
import { Refusal, json, signedBy } from "./signed.js";

const RECENT = 50;
const PLAYER_RECENT = 10;
const MAX_PLAYERS = 64;
const MAX_SECONDS = 6 * 3600;
const MAX_COUNT = 10000;
const MIN_KD_MATCHES = 5;
const BOARD_SIZE = 50;
const BOARD_SECONDS = 30;
const TITLES = ["igi1", "igi2"];
const METRICS = ["score", "kills", "assists", "wins", "kd", "matches"];

// Storage takes at most this many keys in one put or delete.
const BATCH = 128;

// Where a player's totals and latest results in one game are kept.
const totKey = (title, period, player) => `tot:${title}:${period}:${player}`;
const lastKey = (title, player) => `last:${title}:${player}`;
const seasonsKey = (title) => `seasons:${title}`;

// A list of seasons with one more in it, newest first, each once.
const withSeason = (seasons, season) => [...new Set([season, ...(seasons ?? [])])].sort().reverse();

// The game a request asks about: igi1 when it doesn't say, as an older build doesn't; anything else is refused.
function titleAsked(url) {
  const title = url.searchParams.get("title") ?? TITLES[0];
  if (!TITLES.includes(title)) throw new Refusal(400, `A title is one of ${TITLES.join(", ")}.`);
  return title;
}

// The Worker's side: every stats request goes to the one object that keeps them.
export async function handleStats(request, env) {
  if (!env.STATS) {
    return json({ error: "Stats are not served here." }, 503);
  }

  return env.STATS.get(env.STATS.idFromName("all")).fetch(request);
}

// The name a heartbeat's server is registered under here, when the heartbeat is signed by a registered server's key:
// what the list marks it official by. A heartbeat signed by any other key, or badly, or not at all is listed as before,
// only not official.
export async function officialName(request, url, body, env) {
  if (!env.STATS || !request.headers.get("x-openigi-key")) return null;
  let key;
  try {
    key = await signedBy(request, url, body);
  } catch {
    return null;
  }

  const answer = await env.STATS.get(env.STATS.idFromName("all")).fetch(`https://stats/internal/servers/${key.id}`);
  return answer.ok ? ((await answer.json()).name ?? null) : null;
}

// Whether a path is one of the stats'.
export function isStatsPath(pathname) {
  return pathname === "/v1/matches" || pathname === "/v1/leaderboard" || /^\/v1\/players\/[^/]+\/stats$/.test(pathname);
}

// The season a time falls in: its month, UTC.
export function seasonOf(time) {
  const date = new Date(time);
  return `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, "0")}`;
}

// A result's points for a player: a kill 10, an assist 5, an objective 25, a win 50.
export function scoreOf(line) {
  return line.kills * 10 + (line.assists ?? 0) * 5 + line.objectives * 25 + (line.won ? 50 : 0);
}

export class Stats {
  constructor(state, env, now = () => Date.now()) {
    this.state = state;
    this.env = env;
    this.storage = state.storage;
    this.now = now;
    this.boards = new Map();
  }

  async fetch(request) {
    const url = new URL(request.url);
    const parts = url.pathname.split("/").filter(Boolean);
    try {
      await this.migrate();
      if (parts[0] === "internal" && parts[1] === "forget" && request.method === "POST") {
        return json({ forgotten: await this.forget((await request.json()).keys ?? []) });
      }

      if (parts[0] === "internal" && parts[1] === "servers" && request.method === "GET") {
        const server = await this.storage.get(`server:${parts[2]}`);
        return server ? json({ name: server.name }) : json({ error: "Not found." }, 404);
      }

      if (parts[1] === "admin") {
        return await this.admin(request, parts[3]);
      }

      if (parts[1] === "matches" && request.method === "POST") {
        return await this.report(request, url);
      }

      if (parts[1] === "matches" && request.method === "GET") {
        return json(await this.latest(await this.storage.get("recent") ?? []));
      }

      if (parts[1] === "leaderboard" && request.method === "GET") {
        return json(await this.board(url.searchParams.get("period") ?? "season", url.searchParams.get("metric") ?? "score", titleAsked(url), url.searchParams.get("you")));
      }

      if (parts[1] === "players" && parts[3] === "stats" && request.method === "GET") {
        return json(await this.record(decodeURIComponent(parts[2]), titleAsked(url)));
      }

      return json({ error: "Not found." }, 404);
    } catch (error) {
      if (error instanceof Refusal) {
        return json({ error: error.message }, error.status);
      }

      throw error;
    }
  }

  // A server's result: from a registered key, well formed, then kept and counted.
  async report(request, url) {
    const body = new Uint8Array(await request.arrayBuffer());
    const key = await signedBy(request, url, body);
    const server = await this.storage.get(`server:${key.id}`);
    if (!server) {
      throw new Refusal(403, "This server's matches don't count: it isn't one of the listed servers.");
    }

    const result = checkResult(parseJson(body));
    const at = this.now();
    // Ordered by time, then by arrival within the same millisecond, so the newest is always first.
    this.sequence = ((this.sequence ?? 0) + 1) % 1679616;
    const id = `${at}-${this.sequence.toString(36).padStart(4, "0")}${Math.random().toString(36).slice(2, 6)}`;
    const kept = { id, at, server: server.name, ...result };
    const recent = [`match:${id}`, ...(await this.storage.get("recent") ?? [])];
    const writes = { [`match:${id}`]: kept, recent: recent.slice(0, RECENT) };
    writes[seasonsKey(result.title)] = withSeason(await this.storage.get(seasonsKey(result.title)), seasonOf(at));

    const periods = [seasonOf(at), "all"];
    const counted = result.players.filter((line) => line.player);
    const title = result.title;
    const totals = await this.storage.get(counted.flatMap((line) => periods.map((period) => totKey(title, period, line.player))));
    const lasts = await this.storage.get(counted.map((line) => lastKey(title, line.player)));
    for (const line of counted) {
      for (const period of periods) {
        const name = totKey(title, period, line.player);
        writes[name] = add(totals.get(name), line);
      }

      writes[lastKey(title, line.player)] = [`match:${id}`, ...(lasts.get(lastKey(title, line.player)) ?? [])].slice(0, PLAYER_RECENT);
    }

    // A result off the latest list is kept still: a player's own latest may name it.
    await this.storage.put(writes);
    await this.tellRoles(counted.map((line) => line.player));

    this.boards.clear();
    return json({ id, counted: counted.length }, 201);
  }

  // The best players of one game in a period by a metric, accounts gathered from their keys; with one player's own place.
  async board(period, metric, title = TITLES[0], you = null) {
    if (!METRICS.includes(metric)) {
      throw new Refusal(400, `A metric is one of ${METRICS.join(", ")}.`);
    }

    // "all", a named month ("2026-10", a season gone by), or this month's season.
    const season = period === "all" ? "all" : /^\d{4}-\d{2}$/.test(period) ? period : seasonOf(this.now());
    const cacheKey = `${title}/${season}/${metric}`;
    let cached = this.boards.get(cacheKey);
    if (!cached || cached.until <= this.now()) {
      cached = { ...(await this.rank(title, season, metric)), until: this.now() + BOARD_SECONDS * 1000 };
      this.boards.set(cacheKey, cached);
    }

    const { ranked, owners, seasons } = cached;
    const wanted = typeof you === "string" && you.length > 0 ? owners.get(you) ?? you : null;
    const mine = wanted === null ? null : ranked.find((row) => row.player === wanted) ?? null;
    return {
      title,
      period: season === "all" ? "all" : "season",
      season,
      metric,
      rows: ranked.slice(0, BOARD_SIZE),
      players: ranked.length,
      seasons,
      you: mine,
    };
  }

  // Every ranked player of one game in a period by a metric, best first, each numbered; which row each key counts under;
  // and the game's seasons (this one always among them).
  async rank(title, season, metric) {
    const prefix = totKey(title, season, "");
    const totals = await this.storage.list({ prefix });
    const byPlayer = new Map([...totals].map(([name, total]) => [name.slice(prefix.length), total]));
    const accounts = await this.accountsOf([...byPlayer.keys()]);

    // A linked account's keys count together, under its name.
    const rows = new Map();
    const owners = new Map();
    for (const [player, total] of byPlayer) {
      const account = accounts[player];
      const who = account ? `account:${account.id}` : `key:${player}`;
      // Names leave here masked where they hold a slur (names.js): the board is on the website's front page.
      const shown = account ? { ...account, name: cleanName(account.name) } : null;
      const row = rows.get(who) ?? { player: account ? account.id : player, account: shown, name: cleanName(account?.name ?? total.name), ...zero() };
      rows.set(who, sum(row, total));
      owners.set(player, row.player);
    }

    const ranked = [...rows.values()]
      .map(finish)
      .filter((row) => metric !== "kd" || row.matches >= MIN_KD_MATCHES)
      .sort((a, b) => b[metric] - a[metric] || b.score - a.score || a.name.localeCompare(b.name))
      .map((row, index) => ({ rank: index + 1, ...row }));

    const seasons = withSeason(await this.storage.get(seasonsKey(title)), seasonOf(this.now()));
    return { ranked, owners, seasons };
  }

  // One player's record in one game, by a key's id or an account's: this season, all time, and the latest results they were in.
  async record(who, title = TITLES[0]) {
    const keys = await this.keysOf(who);
    const season = seasonOf(this.now());
    const totals = await this.storage.get(keys.flatMap((key) => [totKey(title, season, key), totKey(title, "all", key)]));
    const lasts = await this.storage.get(keys.map((key) => lastKey(title, key)));
    let seasonTotal = zero();
    let allTotal = zero();
    for (const key of keys) {
      seasonTotal = sum(seasonTotal, totals.get(totKey(title, season, key)) ?? zero());
      allTotal = sum(allTotal, totals.get(totKey(title, "all", key)) ?? zero());
    }

    const latest = [...new Set(keys.flatMap((key) => lasts.get(lastKey(title, key)) ?? []))]
      // Fixed-width time and arrival lead every key, so newest first is plain reverse order.
      .sort((a, b) => (a < b ? 1 : a > b ? -1 : 0))
      .slice(0, PLAYER_RECENT);
    const results = await this.latest(latest);
    return {
      player: who,
      title,
      season,
      seasonTotals: finish(seasonTotal),
      allTime: finish(allTotal),
      medals: medalsOf(allTotal),
      latest: results.map((match) => ({ ...match, you: match.players.filter((line) => keys.includes(line.player)) })),
    };
  }

  // The linked accounts among some players, their all-time totals told to Discord for its Linked Roles: the account's, both
  // games together, as a role is the player's and not a game's. Never in the way of a result: a failure here is let go.
  async tellRoles(players) {
    try {
      const accounts = await this.accountsOf(players);
      const byAccount = new Map();
      for (const account of Object.values(accounts)) byAccount.set(account.id, account);
      if (byAccount.size === 0 || !this.env.ACCOUNTS) return;
      const told = [];
      for (const id of byAccount.keys()) {
        let both = zero();
        for (const title of TITLES) both = sum(both, (await this.record(id, title)).allTime);
        told.push({ account: id, totals: finish(both) });
      }

      await this.env.ACCOUNTS.get(this.env.ACCOUNTS.idFromName("all")).fetch("https://accounts/internal/roles", {
        method: "POST",
        body: JSON.stringify({ players: told }),
      });
    } catch {
      // Discord is told next time.
    }
  }

  // Some keys' record forgotten: totals and latest lists gone, every kept result naming them made anonymous.
  async forget(keys) {
    const gone = new Set(keys.filter((key) => /^[0-9a-f]{16}$/.test(key)));
    if (gone.size === 0) return 0;
    const totals = await this.storage.list({ prefix: "tot:" });
    const deletes = [...totals.keys()].filter((name) => gone.has(name.slice(name.lastIndexOf(":") + 1)));
    deletes.push(...[...gone].flatMap((key) => [`last:${key}`, ...TITLES.map((title) => lastKey(title, key))]));
    const writes = {};
    for (const [name, match] of await this.storage.list({ prefix: "match:" })) {
      if (!match.players.some((line) => gone.has(line.player))) continue;
      writes[name] = { ...match, players: match.players.map((line) => (gone.has(line.player) ? { ...line, player: null, name: "Player" } : line)) };
    }

    for (let at = 0; at < deletes.length; at += BATCH) await this.storage.delete(deletes.slice(at, at + BATCH));
    await this.putAll(writes);
    this.boards.clear();
    return gone.size;
  }

  // Once: the totals and latest lists kept before the games were apart (tot:<period>:<player>, last:<player>) made again by
  // game from the results themselves, every one of which names its game, and the old ones gone. Nothing is lost: a total is
  // only ever the results added up.
  async migrate() {
    if (this.migrated) return;
    const schema = (await this.storage.get("schema")) ?? 0;
    if (schema < 2) await this.migrateByGame();
    if (schema < 3) await this.migrateSeasons();
    this.migrated = true;
  }

  // Schema 2: the totals and latest lists made again by game.
  async migrateByGame() {
    const matches = [...(await this.storage.list({ prefix: "match:" })).values()].sort((a, b) => a.at - b.at || (a.id < b.id ? -1 : 1));
    const writes = {};
    for (const match of matches) {
      const title = TITLES.includes(match.title) ? match.title : TITLES[0];
      for (const line of match.players.filter((counted) => counted.player)) {
        for (const period of [seasonOf(match.at), "all"]) {
          const name = totKey(title, period, line.player);
          writes[name] = add(writes[name], line);
        }

        const last = lastKey(title, line.player);
        writes[last] = [`match:${match.id}`, ...(writes[last] ?? [])].slice(0, PLAYER_RECENT);
      }
    }

    const old = [
      ...[...(await this.storage.list({ prefix: "tot:" })).keys()].filter((name) => name.split(":").length === 3),
      ...[...(await this.storage.list({ prefix: "last:" })).keys()].filter((name) => name.split(":").length === 2),
    ];
    await this.putAll(writes);
    for (let at = 0; at < old.length; at += BATCH) await this.storage.delete(old.slice(at, at + BATCH));
    await this.storage.put("schema", 2);
    this.boards.clear();
  }

  // Schema 3: each game's seasons listed, read once from the months its totals are kept under.
  async migrateSeasons() {
    const writes = {};
    for (const title of TITLES) {
      const prefix = `tot:${title}:`;
      const months = new Set();
      for (const name of (await this.storage.list({ prefix })).keys()) {
        const period = name.slice(prefix.length, name.indexOf(":", prefix.length));
        if (/^\d{4}-\d{2}$/.test(period)) months.add(period);
      }

      writes[seasonsKey(title)] = [...months].sort().reverse();
    }

    await this.storage.put(writes);
    await this.storage.put("schema", 3);
    this.boards.clear();
  }

  // Writes as many keys as there are, a batch at a time.
  async putAll(writes) {
    const entries = Object.entries(writes);
    for (let at = 0; at < entries.length; at += BATCH) await this.storage.put(Object.fromEntries(entries.slice(at, at + BATCH)));
  }

  // Kept results, their players' names masked where they hold a slur.
  async latest(names) {
    const matches = await this.storage.get(names);
    return names
      .map((name) => matches.get(name))
      .filter(Boolean)
      .map((match) => ({ ...match, players: match.players.map((line) => ({ ...line, name: cleanName(line.name) })) }));
  }

  // The servers whose matches count: listed, added by their key, removed by its id.
  async admin(request, keyId) {
    if (request.method === "GET") {
      const servers = await this.storage.list({ prefix: "server:" });
      return json([...servers].map(([name, server]) => ({ keyId: name.slice("server:".length), ...server })));
    }

    if (request.method === "POST") {
      const asked = await request.json().catch(() => ({}));
      const raw = typeof asked.key === "string" ? Uint8Array.from(atob(asked.key), (c) => c.charCodeAt(0)) : null;
      if (!raw || raw.length < 65 || typeof asked.name !== "string" || asked.name.trim().length === 0) {
        throw new Refusal(400, "Send { key: <the server's public key, base64>, name }.");
      }

      const id = [...new Uint8Array(await crypto.subtle.digest("SHA-256", raw))].map((b) => b.toString(16).padStart(2, "0")).join("").slice(0, 16);
      await this.storage.put(`server:${id}`, { name: asked.name.trim().slice(0, 64), added: this.now() });
      return json({ keyId: id }, 201);
    }

    if (request.method === "DELETE" && keyId) {
      await this.storage.delete(`server:${keyId}`);
      return json({ removed: true });
    }

    return json({ error: "Not found." }, 404);
  }

  // The linked accounts of some keys, from the accounts' object: { <key>: account } for those linked.
  async accountsOf(keys) {
    if (!this.env.ACCOUNTS || keys.length === 0) return {};
    const out = {};
    for (let at = 0; at < keys.length; at += 64) {
      const ids = keys.slice(at, at + 64).join(",");
      const answer = await this.env.ACCOUNTS.get(this.env.ACCOUNTS.idFromName("all")).fetch(`https://accounts/v1/players?ids=${ids}`);
      if (answer.ok) Object.assign(out, await answer.json());
    }

    return out;
  }

  // The keys a record is of: an account's linked keys, or the one key named.
  async keysOf(who) {
    if (/^[0-9a-f]{16}$/.test(who)) return [who];
    if (!/^\d{5,25}$/.test(who) || !this.env.ACCOUNTS) throw new Refusal(404, "No such player.");
    const answer = await this.env.ACCOUNTS.get(this.env.ACCOUNTS.idFromName("all")).fetch(`https://accounts/internal/keys/${who}`);
    if (!answer.ok) throw new Refusal(404, "No such player.");
    return (await answer.json()).keys ?? [];
  }
}

// A result as a server sends it, checked: what game, mode and map, how long, and each player's line.
export function checkResult(body) {
  const text = (value, max) => (typeof value === "string" ? value.replace(/[\u0000-\u001f\u007f]/g, "").trim().slice(0, max) : "");
  const count = (value) => (Number.isInteger(value) && value >= 0 && value <= MAX_COUNT ? value : null);
  if (!TITLES.includes(body.title)) throw new Refusal(400, "Which game?");
  const mode = text(body.mode, 16);
  const map = text(body.map, 32);
  if (!mode || !map) throw new Refusal(400, "A result names its mode and map.");
  if (!Number.isInteger(body.seconds) || body.seconds < 0 || body.seconds > MAX_SECONDS) throw new Refusal(400, "How long did it last?");
  if (!Array.isArray(body.players) || body.players.length < 2 || body.players.length > MAX_PLAYERS) {
    throw new Refusal(400, "A result counts two players at least.");
  }

  const players = body.players.map((line) => {
    const kills = count(line.kills);
    const deaths = count(line.deaths);
    // Assists come from the servers that count them; one that doesn't reports none.
    const assists = count(line.assists ?? 0);
    const objectives = count(line.objectives ?? 0);
    const seconds = Number.isInteger(line.seconds) && line.seconds >= 0 && line.seconds <= MAX_SECONDS ? line.seconds : body.seconds;
    if (kills === null || deaths === null || assists === null || objectives === null || !Number.isInteger(line.team)) {
      throw new Refusal(400, "A player's line is counts.");
    }

    const player = typeof line.player === "string" && /^[0-9a-f]{16}$/.test(line.player) ? line.player : null;
    const counted = { player, name: text(line.name, 32) || "Player", team: line.team, kills, deaths, assists, objectives, seconds, won: line.won === true };
    return { ...counted, score: scoreOf(counted) };
  });

  return { title: body.title, mode, map, seconds: body.seconds, winner: Number.isInteger(body.winner) ? body.winner : -1, players };
}

function zero() {
  return { matches: 0, wins: 0, kills: 0, deaths: 0, assists: 0, objectives: 0, seconds: 0, score: 0, flawless: 0 };
}

function add(total, line) {
  const base = total ?? { name: line.name, ...zero() };
  return {
    name: line.name,
    matches: base.matches + 1,
    wins: base.wins + (line.won ? 1 : 0),
    kills: base.kills + line.kills,
    deaths: base.deaths + line.deaths,
    assists: (base.assists ?? 0) + (line.assists ?? 0),
    objectives: base.objectives + line.objectives,
    seconds: base.seconds + line.seconds,
    score: base.score + line.score,
    flawless: (base.flawless ?? 0) + (isFlawless(line) ? 1 : 0),
  };
}

function sum(row, total) {
  const out = { ...row };
  for (const field of Object.keys(zero())) out[field] = (row[field] ?? 0) + (total[field] ?? 0);
  return out;
}

// The figures read from the counts: kills a death (deaths at least one), to two places.
function finish(row) {
  return { ...row, kd: Math.round((row.kills / Math.max(1, row.deaths)) * 100) / 100 };
}

function parseJson(body) {
  try {
    const value = JSON.parse(new TextDecoder().decode(body));
    if (value && typeof value === "object" && !Array.isArray(value)) return value;
  } catch {
    // Below.
  }

  throw new Refusal(400, "The body is JSON.");
}
