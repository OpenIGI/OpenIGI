// The self-hosted master's config embeds every module the Worker is made of: a module left out stops workerd from starting
// (2026-11-17: medals.js was missing and the master was down until it was added). `node test/selfhost.test.mjs`.
import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";

const config = readFileSync(new URL("../selfhost/master.capnp", import.meta.url), "utf8");
const embedded = new Set([...config.matchAll(/name = "([^"]+\.js)", esModule = embed "\.\.\/src\/\1"/g)].map((m) => m[1]));
const modules = readdirSync(new URL("../src/", import.meta.url)).filter((name) => name.endsWith(".js"));
for (const name of modules) assert.ok(embedded.has(name), `selfhost/master.capnp doesn't embed src/${name}`);
console.log("selfhost: ok");
