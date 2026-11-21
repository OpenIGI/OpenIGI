// The sessions being played online as openigi.com lists them, shared by the servers page and the home page: dedicated servers
// and the rooms players opened to the public, each with the two ways in -- the browser build that plays its protocol, and the
// installed game through the launcher's openigi:// link. Everything is set as text, never as markup: a session's name is
// whatever its host typed. The site build writes this same file beside its pages.
import { GAMES, MASTER, el, number } from "./board.js";

/** The sessions on the list: every dedicated server and public room, both games. Kept a few seconds by the master. */
export async function loadSessions() {
  const answer = await fetch(`${MASTER}/v1/servers`);
  if (!answer.ok) throw new Error(String(answer.status));
  return (await answer.json()).servers ?? [];
}

/**
 * The browser builds the site serves, each with the protocol it plays (its build.json): the stable one at the root and the
 * beta at /beta/. A build too old to say is left out of the protocol check, and matched by version instead.
 */
export async function loadBuilds() {
  const read = async (path) => {
    try {
      const answer = await fetch(`${path}build.json`, { cache: "no-cache" });
      return answer.ok ? { path, net: (await answer.json()).net ?? null } : { path, net: null };
    } catch {
      return { path, net: null };
    }
  };

  return Promise.all([read("/"), read("/beta/")]);
}

const MODES = { coop: "Co-op", tdm: "Team deathmatch", ffa: "Free for all", sd: "Search & destroy", elim: "Elimination", objective: "Objective" };
const REGIONS = { eu: "Europe", na: "North America", sa: "South America", asia: "Asia", oce: "Oceania", af: "Africa", me: "Middle East" };

/** Whether a session is co-op (the campaign together) rather than a versus match. */
export const isCoop = (session) => session.mode === "coop";

/** The game a session plays, by title id. */
export const gameOfSession = (session) => (GAMES.some(([id]) => id === session.title) ? session.title : GAMES[0][0]);

/**
 * The browser build that can join a session, or null: the one whose protocol is the session's, the stable one first; for a
 * session or a build too old to say its protocol, the build its version names (a beta's, or the stable one's). A dedicated
 * server is joined from a browser through its room, so one without a room has none.
 */
export function webBuildFor(session, builds) {
  if (!session.room) return null;
  if (session.net) {
    const speaking = builds.find((build) => build.net === session.net);
    if (speaking) return speaking.path;
    if (builds.some((build) => build.net !== null)) return null;
  }

  return /-beta\./.test(session.version ?? "") ? "/beta/" : "/";
}

/** Where the browser build plays a session: its room, in its game. */
export const webLink = (session, path) =>
  `${path}?join=${encodeURIComponent(session.room)}&game=${encodeURIComponent(gameOfSession(session))}`;

/**
 * Where the launcher plays a session: a dedicated server by its address (straight to its port, no room between), a room by
 * its code. A server listed at an IPv6 address is joined through its room instead, as a link takes a host and a port alone.
 */
export function desktopLink(session) {
  const game = encodeURIComponent(gameOfSession(session));
  if (session.address && session.port && !String(session.address).includes(":")) {
    return `openigi://connect/${session.address}:${session.port}?game=${game}`;
  }

  return session.room ? `openigi://join/${encodeURIComponent(session.room)}?game=${game}` : null;
}

/** The order the list is shown in: official servers first, then the busiest, then by name. */
export function bySessionOrder(a, b) {
  return Number(b.official) - Number(a.official) || b.players - a.players || String(a.name).localeCompare(String(b.name));
}

/** A session as a card: what it is, who is on it, and the two ways in (or why one isn't offered). */
export function sessionCard(session, builds) {
  const card = el("article", `session${session.official ? " official" : ""}`);
  const game = GAMES.find(([id]) => id === gameOfSession(session))[1];
  const full = session.players >= session.maxPlayers;

  const head = el("div", "session-head");
  const tags = el("div", "session-tags");
  tags.append(el("span", `chip ${isCoop(session) ? "coop" : "versus"}`, isCoop(session) ? "Co-op" : "Versus"), el("span", "chip", game));
  if (session.official) tags.append(el("span", "chip official", "Official"));
  if (!session.address) tags.append(el("span", "chip", "Player's room"));
  if (session.passworded) tags.append(el("span", "chip locked", "Password"));
  head.append(tags, el("h3", null, session.name));
  const where = [MODES[session.mode] ?? String(session.mode ?? "").toUpperCase(), `Mission ${session.map}`];
  if (session.region) where.push(REGIONS[session.region] ?? session.region.toUpperCase());
  const version = (session.version ?? "").split("+")[0];
  if (version && !version.startsWith("0.0.0")) where.push(`v${version}`);
  head.append(el("p", "session-where", where.join(" · ")));

  const count = el("div", "session-count");
  const bar = el("span", "bar");
  const fill = el("span");
  fill.style.width = `${Math.round((session.players / Math.max(1, session.maxPlayers)) * 100)}%`;
  bar.append(fill);
  count.append(el("b", null, `${number(session.players)}/${number(session.maxPlayers)}`), el("span", null, "players"), bar);

  const ways = el("div", "session-ways");
  const web = webBuildFor(session, builds);
  const desktop = desktopLink(session);
  const why = session.passworded ? "Asks a password: join from the game's server list"
    : full ? "Full right now"
    : null;
  if (why) {
    ways.append(el("span", "session-note", why));
  } else {
    if (web) {
      const play = el("a", "button primary", "Play on web");
      play.href = webLink(session, web);
      ways.append(play);
    } else {
      ways.append(el("span", "button disabled", "Desktop only"));
    }

    if (desktop) {
      const open = el("a", "button", "Play on desktop");
      open.href = desktop;
      open.title = "Opens the OpenIGI Launcher, which starts the game into this session";
      ways.append(open);
    }
  }

  card.append(head, count, ways);
  return card;
}
