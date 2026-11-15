// Community maps on their own, in Node: `node test/maps.test.mjs`. Published signed and checked, listed, downloaded as they
// were sent, a new version only from the key that first published, rated, reported until hidden, taken down; the game's own
// files neither carried nor named falsely. And a request the game signs (MapIdentity.cs) checks out here.
import assert from "node:assert/strict";
import { webcrypto as crypto } from "node:crypto";
import { Maps, checkManifest, readPackage } from "../src/maps.js";

// A Durable Object's key-value storage, as far as the maps use it.
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

const sha = async (bytes) => [...new Uint8Array(await crypto.subtle.digest("SHA-256", bytes))].map((b) => b.toString(16).padStart(2, "0")).join("");
const base64 = (bytes) => Buffer.from(bytes).toString("base64");
const text = (s) => new TextEncoder().encode(s);
const folder = "missions/location0/level15";

// A package as MapPackage.Write makes one.
async function makePackage(id, files, extra = {}) {
  const listed = [];
  for (const f of files) listed.push({ path: f.path, size: f.bytes.length, sha256: await sha(f.bytes), ...(f.origin ? { origin: f.origin } : {}) });
  listed.sort((a, b) => (a.path < b.path ? -1 : 1));
  const manifest = { schema: 1, id, title: "Night Raid", summary: "The trainyard by night.", game: "igi1", author: "Tester", missionId: 15, missionDirectory: folder, files: listed, ...extra };
  const head = text(JSON.stringify(manifest));
  const parts = [text("OIGIMAP1"), new Uint8Array(new Uint32Array([head.length]).buffer), head];
  for (const f of listed) if (!f.origin) parts.push(files.find((x) => x.path === f.path).bytes);
  const total = parts.reduce((n, p) => n + p.length, 0);
  const out = new Uint8Array(total);
  let at = 0;
  for (const p of parts) { out.set(p, at); at += p.length; }
  return out;
}

async function newKey() {
  const pair = await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"]);
  return { pair, spki: base64(new Uint8Array(await crypto.subtle.exportKey("spki", pair.publicKey))) };
}

async function signed(key, method, path, body, time = Math.floor(Date.now() / 1000)) {
  const payload = text(`${method}\n${path}\n${time}\n${await sha(body)}`);
  const signature = base64(new Uint8Array(await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, key.pair.privateKey, payload)));
  return new Request(`https://master${path}`, {
    method,
    body,
    headers: { "x-openigi-key": key.spki, "x-openigi-signature": signature, "x-openigi-time": String(time) },
  });
}

const read = async (response) => ({ status: response.status, body: response.headers.get("content-type")?.includes("json") ? await response.json() : new Uint8Array(await response.arrayBuffer()) });

const env = { ADMIN_TOKEN: "admin-secret" };
const maps = new Maps({ storage: fakeStorage() }, env);
const ann = await newKey();
const eve = await newKey();

const script = text("a new mission.qvm");
const objects = text("objects the author edited");
const terrain = text("the game's own terrain, unchanged");
const files = [
  { path: `${folder}/mission.qvm`, bytes: script },
  { path: `${folder}/objects.qsc`, bytes: objects },
  { path: `${folder}/terrain/terrain.dat`, bytes: terrain, origin: "missions/location0/level1/terrain/terrain.dat" },
];

// The game's own files, loaded by us; then a map names them but never carries them.
const admin = (path, body, method = "POST") => new Request(`https://master${path}`, { method, body, headers: { authorization: "Bearer admin-secret" } });
assert.equal((await maps.fetch(new Request("https://master/v1/maps/admin/game-files", { method: "POST", body: "[]" }))).status, 404);
assert.equal((await read(await maps.fetch(admin("/v1/maps/admin/game-files", JSON.stringify([await sha(terrain)]))))).body.hashes, 1);

// Published: signed, checked, up at once.
const pkg = await makePackage("night-raid", files);
assert.equal((await maps.fetch(new Request("https://master/v1/maps", { method: "POST", body: pkg }))).status, 401);
const first = await read(await maps.fetch(await signed(ann, "POST", "/v1/maps", pkg)));
assert.equal(first.status, 201, JSON.stringify(first.body));
assert.equal(first.body.version, 1);
assert.equal(first.body.sha256, await sha(pkg));

// A signature over another body, or made long ago, does not do.
const stale = await signed(ann, "POST", "/v1/maps", pkg, Math.floor(Date.now() / 1000) - 3600);
assert.equal((await maps.fetch(stale)).status, 401);
const forged = await signed(ann, "POST", "/v1/maps", text("something else"));
assert.equal((await maps.fetch(new Request("https://master/v1/maps", { method: "POST", body: pkg, headers: forged.headers }))).status, 401);

// Listed, and downloaded byte for byte.
const listed = await read(await maps.fetch(new Request("https://master/v1/maps?game=igi1")));
assert.deepEqual(listed.body.map((m) => m.id), ["night-raid"]);
assert.equal((await read(await maps.fetch(new Request("https://master/v1/maps?game=igi2")))).body.length, 0);
const downloaded = await read(await maps.fetch(new Request("https://master/v1/maps/night-raid/package")));
assert.deepEqual(downloaded.body, pkg);
assert.equal((await read(await maps.fetch(new Request("https://master/v1/maps/night-raid")))).body.downloads, 1);

// A big map goes in chunks and comes back whole.
const big = new Uint8Array(1_000_000).map((_, i) => (i * 31) & 255);
const bigPkg = await makePackage("big-map", [...files, { path: `${folder}/textures/big.tex`, bytes: big }]);
assert.equal((await maps.fetch(await signed(ann, "POST", "/v1/maps", bigPkg))).status, 201);
assert.deepEqual((await read(await maps.fetch(new Request("https://master/v1/maps/big-map/package")))).body, bigPkg);

// A new version from its author; nobody else's.
const pkg2 = await makePackage("night-raid", [...files, { path: `${folder}/briefing.txt`, bytes: text("go quietly") }]);
assert.equal((await read(await maps.fetch(await signed(eve, "POST", "/v1/maps", pkg2)))).status, 403);
const second = await read(await maps.fetch(await signed(ann, "POST", "/v1/maps", pkg2)));
assert.equal(second.body.version, 2);
assert.deepEqual((await read(await maps.fetch(new Request("https://master/v1/maps/night-raid/package")))).body, pkg2);

// The rules: the game's own file carried, a file named as the game's that isn't, code, a file outside its folder.
const carried = await makePackage("carried", files.map((f) => ({ ...f, origin: undefined })));
const refused = await read(await maps.fetch(await signed(ann, "POST", "/v1/maps", carried)));
assert.equal(refused.status, 422);
assert.ok(refused.body.problems.some((p) => p.includes("game's own files")));
const falsely = await makePackage("falsely", [...files, { path: `${folder}/x.dat`, bytes: text("mine"), origin: "missions/location0/level1/x.dat" }]);
assert.ok((await read(await maps.fetch(await signed(ann, "POST", "/v1/maps", falsely)))).body.problems.some((p) => p.includes("which it is not")));
assert.ok(checkManifest({ schema: 1, id: "x", title: "x", author: "x", game: "igi1", missionId: 1, missionDirectory: folder, files: [{ path: `${folder}/evil.dll`, size: 1, sha256: "a".repeat(64) }] }).some((p) => p.includes("evil.dll")));
assert.ok(checkManifest({ schema: 1, id: "x", title: "x", author: "x", game: "igi1", missionId: 1, missionDirectory: folder, files: [{ path: "missions/location0/level1/a.dat", size: 1, sha256: "a".repeat(64) }] }).some((p) => p.includes("inside")));
const tampered = pkg.slice();
tampered[tampered.length - 1] ^= 0xff;
assert.ok((await readPackage(tampered)).problems.some((p) => p.includes("not the file")));

// Rated: once a key, never by its author.
const rate = (key, stars) => signed(key, "POST", "/v1/maps/night-raid/rate", text(JSON.stringify({ stars })));
assert.equal((await maps.fetch(await rate(ann, 5))).status, 403);
assert.equal((await read(await maps.fetch(await rate(eve, 4)))).body.rating, 4);
assert.equal((await read(await maps.fetch(await rate(eve, 2)))).body.ratings, 1);

// Reported by five keys: hidden until we look; put back, it is listed again.
for (let i = 0; i < 5; i++) {
  await maps.fetch(await signed(await newKey(), "POST", "/v1/maps/night-raid/report", text(JSON.stringify({ reason: "broken" }))));
}
assert.ok(!(await read(await maps.fetch(new Request("https://master/v1/maps")))).body.some((m) => m.id === "night-raid"));
assert.equal((await maps.fetch(new Request("https://master/v1/maps/night-raid/package"))).status, 404);
assert.equal((await maps.fetch(admin("/v1/maps/admin/restore?id=night-raid", ""))).status, 200);
assert.ok((await read(await maps.fetch(new Request("https://master/v1/maps")))).body.some((m) => m.id === "night-raid"));

// Taken down by its author only; then gone for good.
assert.equal((await maps.fetch(await signed(eve, "DELETE", "/v1/maps/big-map", new Uint8Array()))).status, 403);
assert.equal((await maps.fetch(await signed(ann, "DELETE", "/v1/maps/big-map", new Uint8Array()))).status, 200);
assert.equal((await maps.fetch(new Request("https://master/v1/maps/big-map"))).status, 404);
assert.equal((await maps.fetch(await signed(ann, "POST", "/v1/maps", bigPkg))).status, 403);
assert.ok(![...maps.storage.store.keys()].some((k) => k.startsWith("chunk:big-map:")));

console.log("maps: ok");
