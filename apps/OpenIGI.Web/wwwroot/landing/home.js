// The home page's live sections, apart from the game's own script (main.js): who is playing now, from the master's counts,
// and the season's top operatives -- Discord-linked players only, under their Discord names -- from its leaderboard. A
// section the master can't fill is left out rather than shown empty.
import { METRICS, ask, daysLeft, el, number, podiumCard, rankHead, rankRow, seasonName } from "./board.js";

const $ = (id) => document.getElementById(id);

// The players, servers and rooms up now: totals only, never a room's name.
async function live() {
  const section = $("live");
  try {
    const stats = await ask("/v1/stats");
    const rooms = stats.rooms ?? {};
    const players = (stats.servers?.players ?? 0) + (rooms.public?.players ?? 0) + (rooms.private?.players ?? 0);
    const figures = [
      [players, players === 1 ? "Player online" : "Players online"],
      [stats.servers?.open ?? 0, "Dedicated servers"],
      [(rooms.public?.open ?? 0) + (rooms.private?.open ?? 0), "Rooms open"],
      [Object.values(stats.relays ?? {}).filter((relay) => relay.healthy).length, "Relays up"],
    ];
    $("live-figures").replaceChildren(...figures.map(([value, label]) => {
      const box = el("div", "figure");
      box.append(el("b", null, number(value)), el("span", null, label));
      return box;
    }));
    section.hidden = false;
  } catch {
    section.hidden = true;
  }
}

// The season's best, by score: a podium of three and the next two, linked players only.
async function top() {
  const section = $("top");
  try {
    const board = await ask("/v1/leaderboard?period=season&metric=score");
    $("top-season").textContent = `Season · ${seasonName(board.season)} · ${daysLeft()} days left`;
    const linked = board.rows.filter((row) => row.account).slice(0, 5).map((row, index) => ({ ...row, rank: index + 1 }));
    const label = METRICS.find(([id]) => id === "score")[1];
    const podium = $("top-podium");
    const ranks = $("top-ranks");
    if (linked.length === 0) {
      podium.replaceChildren(el("div", "empty", "Nobody on the board yet. Sign in with Discord on the game's Profile page and play a match on an official server to be the first."));
      ranks.replaceChildren();
    } else {
      podium.replaceChildren(...linked.slice(0, 3).map((row) => podiumCard(row, "score", label)));
      ranks.replaceChildren(...(linked.length > 3 ? [rankHead(label), ...linked.slice(3).map((row) => rankRow(row, "score"))] : []));
    }

    section.hidden = false;
  } catch {
    section.hidden = true;
  }
}

live();
top();

// The counts again every minute while the page is looked at; never once the game has taken the page over.
setInterval(() => { if (!document.hidden && !$("landing")?.hidden) live(); }, 60000);
