// The community maps alone, served from Node for a local try: `node --import ./test/cf-stub.mjs test/serve-maps.mjs [port]`,
// then point the game, the editor or the launcher at it with OPENIGI_MAPS_URL=http://localhost:<port>. Kept in memory; the
// admin token is "local".
import { createServer } from "node:http";
import { Maps } from "../src/maps.js";

const store = new Map();
const storage = {
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

const maps = new Maps({ storage }, { ADMIN_TOKEN: "local" });
const port = Number(process.argv[2] ?? 8791);
createServer(async (incoming, outgoing) => {
  const chunks = [];
  for await (const chunk of incoming) chunks.push(chunk);
  const body = chunks.length > 0 ? Buffer.concat(chunks) : undefined;
  const request = new Request(`http://localhost:${port}${incoming.url}`, {
    method: incoming.method,
    headers: incoming.headers,
    body: incoming.method === "GET" || incoming.method === "HEAD" ? undefined : body ?? new Uint8Array(),
  });
  const response = await maps.fetch(request);
  outgoing.writeHead(response.status, Object.fromEntries(response.headers));
  outgoing.end(Buffer.from(await response.arrayBuffer()));
}).listen(port, () => console.log(`maps on http://localhost:${port}`));
