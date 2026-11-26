// The self-hosted master's own cache, in Node: `node --import ./test/cf-stub.mjs test/memo.test.mjs`. However many ask at
// once, the list is asked once; a fresh answer is handed out until it goes stale; a failure is never kept.
import assert from "node:assert/strict";
import { forgetRemembered, remembered } from "../src/index.js";

let asked = 0;
let clock = 1000;
const now = () => clock;
const ask = async () => {
  asked++;
  await new Promise((resolve) => setTimeout(resolve, 5));
  return new Response(JSON.stringify({ servers: [asked] }), { headers: { "content-type": "application/json" } });
};

// Fifty at once: one fetch, the same answer to all, marked for the browser to keep as long.
const answers = await Promise.all(Array.from({ length: 50 }, () => remembered("https://m/v1/servers", 5, ask, now)));
assert.equal(asked, 1);
assert.ok((await Promise.all(answers.map((a) => a.text()))).every((body) => body === '{"servers":[1]}'));
assert.equal(answers[0].headers.get("cache-control"), "public, max-age=5");

// Fresh: kept. Stale: asked again.
clock += 4000;
await remembered("https://m/v1/servers", 5, ask, now);
assert.equal(asked, 1);
clock += 2000;
assert.equal(await (await remembered("https://m/v1/servers", 5, ask, now)).text(), '{"servers":[2]}');
assert.equal(asked, 2);

// A failure is passed on, never kept: the next asker asks again.
forgetRemembered();
let failing = 0;
const broken = async () => {
  failing++;
  return new Response('{"error":"busy"}', { status: 503 });
};
assert.equal((await remembered("https://m/v1/stats", 15, broken, now)).status, 503);
assert.equal((await remembered("https://m/v1/stats", 15, broken, now)).headers.get("cache-control"), "no-store");
assert.equal(failing, 2);

console.log("memo: ok");
