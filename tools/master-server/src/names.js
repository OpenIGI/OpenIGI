// Names shown in public -- on the leaderboard, a player's record, the latest matches -- with the hateful ones masked. A name is
// whatever its player typed (or their Discord name), and the board is on the website's front page, so a slur there is ours.
// Masked when read, never when kept: a list made longer masks the names already kept, and nothing is lost if it is wrong.

export const MASKED = "Operative";

// Read the way a player dodges a filter: digits and symbols for letters.
const LEET = { 0: "o", 1: "i", 3: "e", 4: "a", 5: "s", 7: "t", 8: "b", 9: "g", "@": "a", $: "s", "!": "i", "|": "i", "+": "t" };

// Slurs that are slurs wherever they stand in a name, however it is spaced or dotted.
const ANYWHERE = [
  "nigger", "nigga", "niggr", "nigg", "faggot", "fagot", "retard", "kike", "chink", "tranny", "trannie", "hitler", "heilhit",
  "siegheil", "kkk", "whitepower", "gasthejews", "jewkiller", "wetback", "towelhead",
];

// Short ones that are slurs only as a word of their own: inside a longer word they are innocent (a spice, a raccoon, Montenegro).
const WORDS = ["fag", "fags", "spic", "spick", "coon", "coons", "gook", "dyke", "nig", "nigs", "paki", "beaner", "jap", "negro"];

// A slur as a pattern that also takes its letters drawn out ("niiigggaaa"), never fewer of each than it has: "nigg" is
// n+i+g{2,}, so it finds "niiiggg" and not "knight".
const pattern = (slur) => slur.replace(/(.)\1*/g, (run) => `${run[0]}{${run.length},}`);
const ANYWHERE_PATTERNS = ANYWHERE.map((slur) => new RegExp(pattern(slur)));
const WORD_PATTERNS = WORDS.map((slur) => new RegExp(`^${pattern(slur)}$`));

// A name as a filter reads it: lower case, accents off, the stand-in characters read as the letters they stand for.
function letters(name) {
  return [...name.toLowerCase().normalize("NFKD").replace(/[̀-ͯ]/g, "")].map((c) => LEET[c] ?? c).join("");
}

/** Whether a name holds a slur. */
export function isHateful(name) {
  if (typeof name !== "string" || name.length === 0) return false;
  const read = letters(name);
  const joined = read.replace(/[^a-z]/g, "");
  if (ANYWHERE_PATTERNS.some((slur) => slur.test(joined))) return true;
  return read.split(/[^a-z]+/).some((word) => word && WORD_PATTERNS.some((slur) => slur.test(word)));
}

/** A name as it may be shown: itself, or "Operative" where it holds a slur. */
export function cleanName(name) {
  return isHateful(name) ? MASKED : name;
}
