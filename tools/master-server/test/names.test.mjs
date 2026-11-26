// Names in public, in Node: `node test/names.test.mjs`. A slur is masked however it is spelled out, spaced or drawn out;
// an innocent name that only looks like one in part is left alone.
import assert from "node:assert/strict";
import { MASKED, cleanName, isHateful } from "../src/names.js";

for (const name of ["Gigga Nigga", "n1gg4", "N.I.G.G.E.R", "niiiigggaaa", "xXfaggotXx", "Fag", "h1tler", "spic", "coon hunter", "K K K"]) {
  assert.ok(isHateful(name), `${name} should be masked`);
  assert.equal(cleanName(name), MASKED);
}

for (const name of ["Jones", "Knight", "Niger", "Montenegro", "Scunthorpe", "Raccoon", "Spicy", "Bigger", "Japan", "Anya", "Joe (2)", "", null]) {
  assert.ok(!isHateful(name), `${name} should be left alone`);
}

assert.equal(cleanName("Priboi"), "Priboi");
console.log("names: ok");
