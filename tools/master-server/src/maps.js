// Community maps: published from the editor, checked here as they arrive, live at once, listed and downloaded by the game and
// the launcher. One Durable Object ("all") keeps them, each version's package in chunks in its storage.
//
// A map is one file (src/Core/OpenIGI.Releases/Maps/MapPackage.cs): "OIGIMAP1", the manifest's length (four bytes,
// little-endian), the manifest as JSON, then the bytes of every file the map carries. The game's own files it uses are named
// by path and hash, never carried; the player's game gives them back. The rules below are MapRules.cs's, kept alike.
//
// Who publishes, rates and reports is a key of one installation's (MapIdentity.cs): ECDSA P-256 over the method, path, time
// and the body's SHA-256, a line each. A map belongs to the key that first published it; a report from five keys hides it
// until we look; the admin token takes one down or puts it back, and loads the hashes of the game's own files.

import { Refusal, json, sha256Hex, signedBy } from "./signed.js";

const CHUNK = 120 * 1024;
const MAX_EMBEDDED = 96 * 1024 * 1024;
const MAX_PACKAGE = MAX_EMBEDDED + 4 * 1024 * 1024;
const MAX_FILES = 6000;
const MAX_MANIFEST = 4 * 1024 * 1024;
const HIDE_AT_REPORTS = 5;
const GAMES = ["igi1", "igi2"];
const EXTENSIONS = new Set([
  ".qvm", ".qsc", ".dat", ".res", ".tex", ".bit", ".cmd", ".ctr", ".hmp", ".lmp", ".mtp", ".olm", ".wav", ".mef", ".iff",
  ".spr", ".pic", ".tga", ".png", ".jpg", ".dds", ".json", ".txt", ".ogg", ".fnt", ".mtl", ".ilff", ".grp",
]);
const MAGIC = new TextEncoder().encode("OIGIMAP1");

// The Worker's side: every /v1/maps request goes to the one object that keeps them.
export async function handleMaps(request, env) {
  if (!env.MAPS) {
    return json({ error: "Maps are not served here." }, 503);
  }

  return env.MAPS.get(env.MAPS.idFromName("all")).fetch(request);
}

export class Maps {
  constructor(state, env) {
    this.state = state;
    this.env = env;
    this.storage = state.storage;
  }

  async fetch(request) {
    const url = new URL(request.url);
    const parts = url.pathname.split("/").filter(Boolean); // ["v1", "maps", id?, verb?]
    if (parts[0] !== "v1" || parts[1] !== "maps") {
      return json({ error: "Not found." }, 404);
    }

    try {
      if (parts[2] === "admin") {
        return await this.admin(request, url, parts[3]);
      }

      if (request.method === "GET" && parts.length === 2) {
        return json(await this.list(url.searchParams.get("game")));
      }

      if (request.method === "POST" && parts.length === 2) {
        return await this.publish(request, url);
      }

      const id = parts[2];
      const map = id ? await this.storage.get(`map:${id}`) : undefined;
      if (!map || (map.removed && parts[3] !== "restore")) {
        return json({ error: "No such map." }, 404);
      }

      if (request.method === "GET" && parts.length === 3) {
        return json(listing(map));
      }

      if (request.method === "GET" && parts[3] === "package") {
        return await this.download(map);
      }

      if (request.method === "POST" && parts[3] === "rate") {
        return await this.rate(request, url, map);
      }

      if (request.method === "POST" && parts[3] === "report") {
        return await this.report(request, url, map);
      }

      if (request.method === "DELETE" && parts.length === 3) {
        return await this.remove(request, url, map);
      }

      return json({ error: "Not found." }, 404);
    } catch (error) {
      if (error instanceof Refusal) {
        return json({ error: error.message, problems: error.problems }, error.status);
      }

      throw error;
    }
  }

  // Every map that is up, for a game or every game, the most downloaded first.
  async list(game) {
    const maps = await this.storage.list({ prefix: "map:" });
    return [...maps.values()]
      .filter((m) => !m.removed && !m.hidden && (!game || m.game === game))
      .sort((a, b) => b.downloads - a.downloads || b.published.localeCompare(a.published))
      .map(listing);
  }

  // A new map, or a new version of one: signed, checked, and up at once.
  async publish(request, url) {
    const body = new Uint8Array(await request.arrayBuffer());
    if (body.length > MAX_PACKAGE) {
      throw new Refusal(413, "The map is too big.");
    }

    const key = await signedBy(request, url, body);
    const { manifest, problems } = await readPackage(body, await this.gameFiles());
    if (problems.length > 0) {
      throw new Refusal(422, "The map breaks the rules.", problems);
    }

    const existing = await this.storage.get(`map:${manifest.id}`);
    if (existing && existing.owner !== key.id) {
      throw new Refusal(403, "Another author published a map with this id: choose another.");
    }

    if (existing && existing.removed) {
      throw new Refusal(403, "This map was taken down.");
    }

    const version = (existing?.version ?? 0) + 1;
    const chunks = Math.ceil(body.length / CHUNK);
    const entries = {};
    for (let i = 0; i < chunks; i++) {
      entries[`chunk:${manifest.id}:${version}:${i}`] = body.slice(i * CHUNK, (i + 1) * CHUNK);
      if (Object.keys(entries).length === 100) {
        await this.storage.put(entries);
        for (const k of Object.keys(entries)) delete entries[k];
      }
    }

    if (Object.keys(entries).length > 0) {
      await this.storage.put(entries);
    }

    const map = {
      id: manifest.id,
      version,
      title: manifest.title.trim(),
      summary: (manifest.summary ?? "").trim(),
      game: manifest.game,
      author: manifest.author.trim(),
      owner: key.id,
      published: new Date().toISOString(),
      size: body.length,
      sha256: await sha256Hex(body),
      chunks,
      downloads: existing?.downloads ?? 0,
      ratingSum: existing?.ratingSum ?? 0,
      ratings: existing?.ratings ?? 0,
      reports: existing?.reports ?? 0,
      hidden: existing?.hidden ?? false,
      remixOf: manifest.remixOf ?? null,
    };
    await this.storage.put(`map:${map.id}`, map);
    if (existing) {
      await this.dropChunks(map.id, existing.version, existing.chunks);
    }

    return json(listing(map), existing ? 200 : 201);
  }

  async download(map) {
    if (map.hidden) {
      throw new Refusal(404, "No such map.");
    }

    const keys = Array.from({ length: map.chunks }, (_, i) => `chunk:${map.id}:${map.version}:${i}`);
    const found = new Map();
    for (let i = 0; i < keys.length; i += 128) {
      for (const [k, v] of await this.storage.get(keys.slice(i, i + 128))) found.set(k, v);
    }

    const body = new Uint8Array(map.size);
    let at = 0;
    for (const k of keys) {
      const chunk = found.get(k);
      if (!chunk) {
        throw new Error(`Map ${map.id} is missing ${k}.`);
      }

      body.set(new Uint8Array(chunk), at);
      at += chunk.byteLength;
    }

    map.downloads++;
    await this.storage.put(`map:${map.id}`, map);
    return new Response(body, {
      headers: {
        "content-type": "application/octet-stream",
        "content-disposition": `attachment; filename="${map.id}.oigimap"`,
        "x-openigi-map-version": String(map.version),
        "x-openigi-map-sha256": map.sha256,
      },
    });
  }

  // One rating a key, one to five stars; a second from the same key replaces the first.
  async rate(request, url, map) {
    const body = new Uint8Array(await request.arrayBuffer());
    const key = await signedBy(request, url, body);
    const stars = Number(parseBody(body).stars);
    if (!Number.isInteger(stars) || stars < 1 || stars > 5) {
      throw new Refusal(400, "A rating is one to five stars.");
    }

    if (key.id === map.owner) {
      throw new Refusal(403, "Authors don't rate their own maps.");
    }

    const before = await this.storage.get(`rating:${map.id}:${key.id}`);
    map.ratingSum += stars - (before ?? 0);
    map.ratings += before ? 0 : 1;
    await this.storage.put({ [`rating:${map.id}:${key.id}`]: stars, [`map:${map.id}`]: map });
    return json(listing(map));
  }

  // One report a key; enough of them hide the map until it is looked at.
  async report(request, url, map) {
    const body = new Uint8Array(await request.arrayBuffer());
    const key = await signedBy(request, url, body);
    const reason = String(parseBody(body).reason ?? "").slice(0, 500);
    if (await this.storage.get(`report:${map.id}:${key.id}`)) {
      return json({ reported: true });
    }

    map.reports++;
    map.hidden = map.hidden || map.reports >= HIDE_AT_REPORTS;
    await this.storage.put({ [`report:${map.id}:${key.id}`]: { reason, at: new Date().toISOString() }, [`map:${map.id}`]: map });
    return json({ reported: true });
  }

  // Taken down by its author, or by us.
  async remove(request, url, map) {
    if (!(await isAdmin(request, this.env))) {
      const body = new Uint8Array(await request.arrayBuffer());
      const key = await signedBy(request, url, body);
      if (key.id !== map.owner) {
        throw new Refusal(403, "Only its author takes a map down.");
      }
    }

    map.removed = true;
    await this.storage.put(`map:${map.id}`, map);
    await this.dropChunks(map.id, map.version, map.chunks);
    return json({ removed: true });
  }

  // Ours alone: the game's own files' hashes (which no map may carry, and only which it may name), the reports, and a
  // hidden or taken down map put back.
  async admin(request, url, what) {
    if (!(await isAdmin(request, this.env))) {
      return json({ error: "Not found." }, 404);
    }

    if (request.method === "POST" && what === "game-files") {
      const hashes = await request.json();
      if (!Array.isArray(hashes) || !hashes.every((h) => typeof h === "string" && /^[0-9a-f]{64}$/.test(h))) {
        throw new Refusal(400, "A list of SHA-256 hashes, lowercase hex.");
      }

      const sorted = [...new Set(hashes)].sort();
      const entries = {};
      const pages = Math.ceil(sorted.length / 2000);
      for (let i = 0; i < pages; i++) entries[`gamefiles:${i}`] = sorted.slice(i * 2000, (i + 1) * 2000);
      await this.storage.delete([...(await this.storage.list({ prefix: "gamefiles:" })).keys()]);
      await this.storage.put({ ...entries, "gamefiles": pages });
      this.gameFileSet = undefined;
      return json({ hashes: sorted.length });
    }

    if (request.method === "GET" && what === "reports") {
      const maps = await this.storage.list({ prefix: "map:" });
      return json([...maps.values()].filter((m) => m.reports > 0 || m.hidden).map((m) => ({ ...listing(m), reports: m.reports, hidden: m.hidden, removed: !!m.removed })));
    }

    if (request.method === "POST" && what === "restore") {
      const id = url.searchParams.get("id");
      const map = await this.storage.get(`map:${id}`);
      if (!map || map.removed) {
        throw new Refusal(404, "No such map, or it was taken down for good.");
      }

      map.hidden = false;
      map.reports = 0;
      await this.storage.delete([...(await this.storage.list({ prefix: `report:${id}:` })).keys()]);
      await this.storage.put(`map:${id}`, map);
      return json(listing(map));
    }

    return json({ error: "Not found." }, 404);
  }

  // The game's own files' hashes, loaded once; empty until we load them, and then nothing is checked against them.
  async gameFiles() {
    if (!this.gameFileSet) {
      const pages = (await this.storage.get("gamefiles")) ?? 0;
      const set = new Set();
      for (let i = 0; i < pages; i++) for (const h of (await this.storage.get(`gamefiles:${i}`)) ?? []) set.add(h);
      this.gameFileSet = set;
    }

    return this.gameFileSet;
  }

  async dropChunks(id, version, chunks) {
    const keys = Array.from({ length: chunks }, (_, i) => `chunk:${id}:${version}:${i}`);
    for (let i = 0; i < keys.length; i += 128) await this.storage.delete(keys.slice(i, i + 128));
  }
}

// A map as the game and the launcher are told of it (MapListing.cs).
function listing(map) {
  return {
    id: map.id,
    version: map.version,
    title: map.title,
    summary: map.summary,
    game: map.game,
    author: map.author,
    published: map.published,
    size: map.size,
    sha256: map.sha256,
    downloads: map.downloads,
    rating: map.ratings > 0 ? Math.round((map.ratingSum / map.ratings) * 10) / 10 : 0,
    ratings: map.ratings,
    remixOf: map.remixOf ?? null,
  };
}

// Reads a package and checks it: the manifest against the rules, every carried file against its hash.
export async function readPackage(body, gameFiles = new Set()) {
  const problems = [];
  if (body.length < MAGIC.length + 4 || !MAGIC.every((b, i) => body[i] === b)) {
    return { manifest: null, problems: ["Not a map package."] };
  }

  const length = new DataView(body.buffer, body.byteOffset, body.byteLength).getUint32(MAGIC.length, true);
  let at = MAGIC.length + 4 + length;
  if (length === 0 || length > MAX_MANIFEST || at > body.length) {
    return { manifest: null, problems: ["The map's manifest is cut short or too long."] };
  }

  let manifest;
  try {
    manifest = JSON.parse(new TextDecoder().decode(body.subarray(MAGIC.length + 4, at)));
  } catch {
    return { manifest: null, problems: ["The map's manifest is not readable."] };
  }

  problems.push(...checkManifest(manifest, gameFiles.size > 0 ? (h) => gameFiles.has(h) : null));
  if (problems.length > 0) {
    return { manifest, problems };
  }

  for (const file of manifest.files) {
    if (file.origin) continue;
    if (at + file.size > body.length) {
      return { manifest, problems: [`The package ends inside ${file.path}.`] };
    }

    if ((await sha256Hex(body.subarray(at, at + file.size))) !== file.sha256) {
      problems.push(`${file.path} is not the file the manifest names.`);
    }

    at += file.size;
  }

  if (at !== body.length) {
    problems.push("The package runs on past its last file.");
  }

  return { manifest, problems };
}

// MapRules.Check, alike.
export function checkManifest(m, isGameFile) {
  const problems = [];
  const text = (v) => (typeof v === "string" ? v : "");
  if (m?.schema !== 1) problems.push("Format is not one this reads (1).");
  if (!validId(m?.id)) problems.push("The map's id is lowercase letters, digits and inner hyphens, at most 64.");
  if (!text(m?.title).trim() || text(m?.title).length > 64) problems.push("The map needs a name of at most 64 characters.");
  if (text(m?.summary).length > 500) problems.push("The summary is at most 500 characters.");
  if (!text(m?.author).trim() || text(m?.author).length > 40) problems.push("The map needs an author's name of at most 40 characters.");
  if (!GAMES.includes(m?.game)) problems.push(`The map is for a game this does not know: ${m?.game}.`);
  if (m?.remixOf != null && !validId(m.remixOf)) problems.push("The map it remixes is named by an id.");
  if (!Number.isInteger(m?.missionId)) problems.push("The map names its mission's id.");
  const folder = text(m?.missionDirectory).replace(/\/+$/, "");
  if (!safePath(folder) || !folder.startsWith("missions/")) problems.push("The mission's folder is a folder under missions/.");
  const files = Array.isArray(m?.files) ? m.files : [];
  if (files.length === 0 || files.length > MAX_FILES) problems.push(`A map has between one and ${MAX_FILES} files.`);
  const embedded = files.filter((f) => !f.origin).reduce((sum, f) => sum + (Number(f.size) || 0), 0);
  if (embedded > MAX_EMBEDDED) problems.push(`A map carries at most ${MAX_EMBEDDED / (1024 * 1024)} MB of its own.`);
  const seen = new Set();
  for (const f of files) {
    const path = text(f?.path);
    if (!safePath(path) || !path.startsWith(folder + "/")) problems.push(`${path}: every file is inside the mission's folder.`);
    else if (seen.has(path.toLowerCase())) problems.push(`${path}: named twice.`);
    seen.add(path.toLowerCase());
    if (!Number.isInteger(f?.size) || f.size < 0 || !/^[0-9a-f]{64}$/.test(text(f?.sha256))) problems.push(`${path}: its size or hash is not one.`);
    if (f?.origin != null) {
      if (!safePath(text(f.origin))) problems.push(`${path}: the game file it is, ${f.origin}, is not a path in the game.`);
      if (isGameFile && !isGameFile(f.sha256)) problems.push(`${path}: named as the game's own ${f.origin}, which it is not; the map has to carry it.`);
    } else {
      const dot = path.lastIndexOf(".");
      if (dot < 0 || !EXTENSIONS.has(path.slice(dot).toLowerCase())) problems.push(`${path}: a map carries the game's kinds of data only.`);
      if (isGameFile && isGameFile(f?.sha256)) problems.push(`${path}: one of the game's own files, which a map names rather than carries.`);
    }
  }

  if (!files.some((f) => f?.path === `${folder}/mission.qvm` && f.origin == null)) problems.push("The map carries its own mission.qvm, which defines it.");
  return problems;
}

function validId(id) {
  return typeof id === "string" && id.length > 0 && id.length <= 64 && /^[a-z0-9-]+$/.test(id) && !id.startsWith("-") && !id.endsWith("-");
}

function safePath(path) {
  return typeof path === "string" && path.length > 0 && path.length <= 240 && !path.startsWith("/")
    && /^[A-Za-z0-9/._ -]+$/.test(path) && path.split("/").every((p) => p.length > 0 && p !== "." && p !== "..");
}

async function isAdmin(request, env) {
  const given = (request.headers.get("authorization") ?? "").replace(/^Bearer /, "");
  if (!env.ADMIN_TOKEN || !given) return false;
  const digest = async (s) => new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(s)));
  const [a, b] = [await digest(given), await digest(env.ADMIN_TOKEN)];
  return crypto.subtle.timingSafeEqual ? crypto.subtle.timingSafeEqual(a, b) : a.every((v, i) => v === b[i]);
}

function parseBody(body) {
  try {
    return JSON.parse(new TextDecoder().decode(body)) ?? {};
  } catch {
    throw new Refusal(400, "The body is JSON.");
  }
}
