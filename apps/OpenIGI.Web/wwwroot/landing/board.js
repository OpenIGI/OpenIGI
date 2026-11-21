// The official servers' standings as openigi.com draws them, shared by the home page's top operatives and the leaderboard:
// the master's address, a player's picture, a podium card and a ranked row. Everything is set as text, never as markup: a
// name is whatever its player typed. The site build writes this same file beside its pages.

export const MASTER = "https://master.openigi.com";

/** Asks the master for JSON. */
export async function ask(path) {
  const answer = await fetch(MASTER + path);
  if (!answer.ok) throw new Error(String(answer.status));
  return answer.json();
}

/** A count as said: 12,480; a dash for none. */
export const number = (n) => (typeof n === "number" ? n.toLocaleString("en") : "—");

/** An element with a class and text. */
export function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined && text !== null) node.textContent = String(text);
  return node;
}

/** The metrics a board ranks by, as the master names them and as a page says them. */
export const METRICS = [
  ["score", "Score"], ["kills", "Kills"], ["assists", "Assists"], ["kd", "K/D"], ["wins", "Wins"], ["matches", "Matches"],
];

/** A row's figure for a metric, as said. */
export function metricText(row, metric) {
  return metric === "kd" ? (row.kd ?? 0).toFixed(2) : number(row[metric] ?? 0);
}

/** The share of matches won, nought to one. */
export const winRate = (row) => (row.matches > 0 ? row.wins / row.matches : 0);

/** Kills and assists a death. */
export const kda = (row) => ((row.kills ?? 0) + (row.assists ?? 0)) / Math.max(1, row.deaths ?? 0);

/** Where a player's record is: by their Discord account where it is linked, else by their key. */
export const recordLink = (row) => `/leaderboard/?player=${encodeURIComponent(row.account ? row.account.id : row.player)}`;

/** A player's picture: their Discord avatar, or their initial on a disc. */
export function avatar(name, url) {
  const disc = el("span", "avatar");
  if (url && /^https:\/\/cdn\.discordapp\.com\//.test(url)) {
    const img = document.createElement("img");
    img.src = url;
    img.alt = "";
    img.loading = "lazy";
    img.referrerPolicy = "no-referrer";
    img.addEventListener("error", () => img.replaceWith(document.createTextNode(initial(name))));
    disc.append(img);
  } else {
    disc.textContent = initial(name);
  }

  return disc;
}

const initial = (name) => ([...(name ?? "").trim()][0] ?? "?").toUpperCase();

/** K / D / A, the slashes quiet. */
export function kdaLine(row) {
  const line = el("span", "kda");
  line.append(number(row.kills ?? 0), el("i", null, " / "), number(row.deaths ?? 0), el("i", null, " / "), number(row.assists ?? 0));
  return line;
}

const PLACES = ["first", "second", "third"];

/** One of the top three, as a card on the podium. */
export function podiumCard(row, metric, label) {
  const card = el("a", `podium-card ${PLACES[row.rank - 1] ?? ""}`);
  card.href = recordLink(row);
  card.append(el("span", "place", `#${row.rank}`), avatar(row.name, row.account?.avatar), el("span", "who", row.name));
  const value = el("span", "value", metricText(row, metric));
  value.append(el("small", null, label));
  card.append(value);
  const line = el("span", "line");
  line.append(kdaLine(row), ` · ${Math.round(winRate(row) * 100)}% won`);
  card.append(line);
  return card;
}

/** A player's row on the ranked list. */
export function rankRow(row, metric) {
  const link = el("a", "rank-row");
  link.href = recordLink(row);
  const player = el("span", "player");
  player.append(avatar(row.name, row.account?.avatar), el("span", "name", row.name));
  if (row.account) player.append(el("span", "linked", "DISCORD"));
  const rate = el("span", "winrate");
  const bar = el("span", "bar");
  const fill = el("span");
  fill.style.width = `${Math.round(winRate(row) * 100)}%`;
  bar.append(fill);
  rate.append(bar, `${Math.round(winRate(row) * 100)}%`);
  link.append(el("span", "n", row.rank), player, el("span", "num metric", metricText(row, metric)), kdaLine(row), rate, el("span", "num matches", number(row.matches)));
  return link;
}

/** The ranked list's heads. */
export function rankHead(label) {
  const head = el("div", "rank-row head");
  head.append(el("span", null, "#"), el("span", null, "Player"), el("span", "num", label), el("span", null, "K / D / A"), el("span", null, "Win rate"), el("span", "num", "Matches"));
  return head;
}

/** The season a month names, as said: November 2026. */
export const seasonName = (s) => (/^\d{4}-\d{2}$/.test(s)
  ? new Date(Date.UTC(+s.slice(0, 4), +s.slice(5) - 1, 1)).toLocaleString("en", { month: "long", year: "numeric", timeZone: "UTC" })
  : s);

/** The days left in this season (this month, UTC), the day it is counted. */
export function daysLeft(now = new Date()) {
  const end = Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + 1, 1);
  return Math.max(1, Math.ceil((end - now.getTime()) / 86400000));
}
