// Medals: what a player has done on the official servers, read from their all-time totals (stats.js). Nothing is kept for
// them; they follow from the record each time it is read, so a new medal counts every match already played.
//
// Each medal is { id, name, about, test(total) }; a record carries the ones earned as { id, name, about }.

export const MEDALS = [
  { id: "first-kill", name: "First blood", about: "A kill on an official server.", test: (t) => t.kills >= 1 },
  { id: "first-win", name: "First win", about: "A match won.", test: (t) => t.wins >= 1 },
  { id: "regular", name: "Regular", about: "25 matches played.", test: (t) => t.matches >= 25 },
  { id: "veteran", name: "Veteran", about: "100 matches played.", test: (t) => t.matches >= 100 },
  { id: "marksman", name: "Marksman", about: "100 kills.", test: (t) => t.kills >= 100 },
  { id: "hunter", name: "Hunter", about: "1,000 kills.", test: (t) => t.kills >= 1000 },
  { id: "victor", name: "Victor", about: "50 matches won.", test: (t) => t.wins >= 50 },
  { id: "specialist", name: "Specialist", about: "10 objectives: bombs planted or defused.", test: (t) => t.objectives >= 10 },
  { id: "ghost", name: "Ghost", about: "A match with five kills or more and no deaths.", test: (t) => (t.flawless ?? 0) >= 1 },
  { id: "long-shift", name: "Long shift", about: "Ten hours in matches.", test: (t) => t.seconds >= 10 * 3600 },
];

// The medals a total has earned, in the order above.
export function medalsOf(total) {
  return MEDALS.filter((medal) => medal.test(total)).map(({ id, name, about }) => ({ id, name, about }));
}

// Whether one player's line in a match is a flawless one (counted in the totals for the Ghost medal).
export function isFlawless(line) {
  return line.kills >= 5 && line.deaths === 0;
}
