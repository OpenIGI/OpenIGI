using System.Text.Json;
using OpenIGI.Core.Titles;

namespace OpenIGI.Tools.Release;

/// <summary>
/// <c>/leaderboard/</c>: the best players on the official servers in one game (<c>?game=igi1</c> or <c>igi2</c>; each game's
/// stats are its own) this season or all time, by score, kills, assists, K/D, wins or matches -- the top three on a podium, the rest ranked under them -- read from the master as the page opens;
/// <c>/leaderboard/?board=missions</c> is the single-player mission boards instead: a mission (or all of them), a difficulty and
/// the rules, by the fastest or the best rank, every run played again by the server before it counted, with Watch and Race on
/// each row whose replay is kept -- the browser build opened on the run (<c>?replay=</c> or <c>?race=</c>).
/// <c>/leaderboard/?player=ID</c> (and <c>/u/ID</c>, which the 404 page sends here) is one player's record by their Discord
/// user id or a key's: their figures this season and all time, their medals and their latest matches. Static like the rest
/// of the site, in its look (<c>/landing/site.css</c>) and drawn with the home page's own pieces (<c>/landing/board.js</c>);
/// everything shown is set as text, never as markup.
/// </summary>
internal static class LeaderboardPage
{
    // Each game's missions by name, in campaign order, for the mission boards' pick.
    private static readonly string MissionNames = JsonSerializer.Serialize(GameTitle.All.ToDictionary(static t => t.Id, static t => t.MissionNames));

    // The ranks' names, three a difficulty from Easy, as the games' own screens name them.
    private static readonly string RankNames = JsonSerializer.Serialize(GameTitle.RankNames);

    /// <summary>The page.</summary>
    public static string Html { get; } = $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
          <meta name="color-scheme" content="dark">
          <meta name="theme-color" content="#0b0c0b">
          <title>Leaderboard · OpenIGI</title>
          <meta name="description" content="The best players on OpenIGI's official multiplayer servers, this season and all time.">
          <link rel="icon" type="image/png" sizes="192x192" href="/icons/icon-192.png">
          <link rel="preload" href="/landing/archivo.woff2" as="font" type="font/woff2" crossorigin>
          <link rel="stylesheet" href="/landing/site.css">
          <style>
            body {
              min-height: 100vh;
              background:
                radial-gradient(70% 50% at 50% 0%, rgba(232, 163, 61, 0.1) 0%, rgba(232, 163, 61, 0) 70%),
                linear-gradient(180deg, rgba(11, 12, 11, 0.55) 0%, var(--bg) 520px),
                url("/landing/bg.webp") center top / 100% auto no-repeat,
                var(--bg);
            }
            main.wrap { padding-top: clamp(36px, 6vw, 72px); padding-bottom: 72px; }

            /* The season's banner: what this is, how long it has left, and the period. */
            .banner { display: flex; justify-content: space-between; align-items: flex-end; gap: 20px 32px; flex-wrap: wrap; margin-bottom: 30px; }
            .banner .mono { color: var(--accent); }
            .banner h1 { margin: 10px 0 0; font-size: clamp(44px, 7vw, 84px); line-height: 0.92; font-weight: 700; font-stretch: 76%; letter-spacing: -0.02em; }
            .banner p { margin: 12px 0 0; max-width: 560px; color: var(--soft); font-size: 16px; line-height: 1.55; }
            .clock { display: inline-flex; align-items: center; gap: 10px; margin-top: 16px; padding: 8px 14px; border: 1px solid var(--line); border-radius: 999px; background: var(--panel); color: var(--note); font: 12px/1 var(--mono); }
            .clock b { color: var(--text); font-weight: 500; }

            /* Which game and which period: the shared pills (site.css), one over the other. */
            .switches { display: flex; flex-direction: column; align-items: flex-end; gap: 10px; }
            @media (max-width: 720px) { .switches { align-items: flex-start; } }
            .record-switch { margin-bottom: 16px; }

            /* A season gone by: a pick of the months the game has had, under the period's pill. */
            .past { appearance: none; padding: 9px 34px 9px 16px; border: 1px solid var(--line-strong); border-radius: 999px; background: var(--panel) url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='10' height='6'%3E%3Cpath d='M0 0l5 6 5-6z' fill='%23a3a39a'/%3E%3C/svg%3E") right 14px center no-repeat; color: var(--muted); font: 600 13px/1 var(--sans); cursor: pointer; }
            .past:hover, .past.picked { color: var(--text); }
            .past.picked { border-color: var(--accent); }
            .past:focus-visible { outline: 2px solid var(--text); outline-offset: 3px; }

            /* The metric the board is ranked by: a row of tabs, the chosen one underlined in the accent. */
            .tabs { display: flex; gap: 4px; margin-bottom: 28px; overflow-x: auto; border-bottom: 1px solid var(--line); scrollbar-width: none; }
            .tabs::-webkit-scrollbar { display: none; }
            .tabs button { appearance: none; flex: none; padding: 12px 16px; border: none; border-bottom: 2px solid transparent; margin-bottom: -1px; background: none; color: var(--muted); font: 600 14px/1 var(--sans); letter-spacing: 0.02em; cursor: pointer; }
            .tabs button:hover { color: var(--text); }
            .tabs button[aria-selected="true"] { color: var(--text); border-bottom-color: var(--accent); }

            #ranks { margin-top: 18px; }
            .podium .skeleton { height: 230px; }
            .podium .skeleton:nth-child(2) { height: 270px; }

            /* A player's record: who, their figures, their medals and their matches. */
            .hero { position: relative; display: flex; align-items: center; gap: 24px; padding: 28px; overflow: hidden; border: 1px solid var(--line); border-radius: var(--radius); background: linear-gradient(120deg, rgba(232, 163, 61, 0.16), rgba(14, 16, 14, 0.85) 55%); }
            .hero .avatar { width: 104px; height: 104px; font-size: 40px; box-shadow: 0 0 0 3px var(--bg), 0 0 0 5px var(--accent); }
            .hero h1 { margin: 8px 0 0; font-size: clamp(34px, 5vw, 56px); line-height: 0.95; font-weight: 700; font-stretch: 78%; overflow-wrap: anywhere; }
            .hero .mono { color: var(--accent); }
            .tags { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 12px; }
            .tag { padding: 6px 12px; border: 1px solid var(--line-strong); border-radius: 999px; color: var(--note); font: 12px/1 var(--mono); }
            .tag.rank { border-color: var(--accent); color: var(--accent); }
            @media (max-width: 560px) { .hero { flex-direction: column; align-items: flex-start; padding: 22px; } .hero .avatar { width: 80px; height: 80px; font-size: 30px; } }

            .tiles { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 1px; margin-top: 16px; overflow: hidden; border: 1px solid var(--line); border-radius: var(--radius); background: var(--line); }
            .tiles .figure { padding: 22px; background: var(--panel-solid); }
            .tiles .figure.lit b { color: var(--accent); }
            .tiles .bar { margin-top: 10px; }
            @media (max-width: 640px) { .tiles { grid-template-columns: repeat(2, minmax(0, 1fr)); } }

            h2.part { margin: 40px 0 14px; color: var(--muted); font: 500 11.5px var(--mono); letter-spacing: 0.12em; text-transform: uppercase; }
            .medals { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 10px; }
            .medal { display: flex; align-items: center; gap: 12px; padding: 14px; border: 1px solid rgba(232, 163, 61, 0.35); border-radius: 12px; background: var(--accent-soft); }
            .medal .emblem { flex: none; display: grid; place-items: center; width: 38px; height: 38px; border-radius: 50%; background: radial-gradient(circle at 35% 30%, #ffe4a8, var(--accent) 60%, #8a5a17); color: #2a1a05; font: 800 15px/1 var(--sans); }
            .medal b { display: block; font-size: 15px; }
            .medal span { display: block; margin-top: 3px; color: var(--muted); font-size: 12.5px; line-height: 1.35; }

            .matches { display: flex; flex-direction: column; gap: 8px; }
            .match { display: grid; grid-template-columns: 4px minmax(0, 1fr) auto auto; align-items: center; gap: 16px; padding: 14px 18px 14px 0; overflow: hidden; border: 1px solid var(--line); border-radius: 12px; background: var(--panel); }
            .match .stripe { align-self: stretch; background: var(--faint); }
            .match.won .stripe { background: var(--ok); }
            .match.lost .stripe { background: var(--error); }
            .match .what b { display: block; font-size: 15px; }
            .match .what span { display: block; margin-top: 4px; color: var(--faint); font: 12px/1.3 var(--mono); }
            .match .result { font: 700 13px/1 var(--mono); letter-spacing: 0.1em; }
            .match.won .result { color: var(--ok); }
            .match.lost .result { color: var(--error); }
            @media (max-width: 560px) { .match { grid-template-columns: 4px minmax(0, 1fr) auto; } .match .kda { grid-column: 2; } }

            .actions { display: flex; flex-wrap: wrap; gap: 12px; margin-top: 40px; }

            /* The mission boards: the picks, the metric's tabs, and a row a run with its time, its rank and its replay. */
            .picks { display: flex; flex-wrap: wrap; gap: 10px; margin-bottom: 18px; }
            .runs { display: flex; flex-direction: column; gap: 6px; }
            .run { display: grid; grid-template-columns: 44px minmax(0, 1fr) 110px 120px auto; align-items: center; gap: 14px; padding: 10px 16px; border: 1px solid transparent; border-radius: 12px; background: rgba(14, 16, 14, 0.62); }
            .run .n { color: var(--faint); font: 600 14px/1 var(--mono); }
            .run .n.top { color: var(--accent); }
            .run .player { display: flex; align-items: center; gap: 12px; min-width: 0; }
            .run .player .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-weight: 600; }
            .run .figure { text-align: right; color: var(--accent); font: 700 17px/1 var(--sans); font-variant-numeric: tabular-nums; }
            .run .under { text-align: right; white-space: nowrap; color: var(--muted); font: 12px/1 var(--mono); letter-spacing: 0.06em; }
            .run .go { display: flex; gap: 8px; justify-content: flex-end; min-width: 168px; }
            .run .go a { padding: 8px 14px; border: 1px solid var(--line-strong); border-radius: 999px; color: var(--text); font: 600 12px/1 var(--sans); letter-spacing: 0.06em; text-decoration: none; text-transform: uppercase; }
            .run .go a:hover { border-color: var(--accent); color: var(--accent); }
            .run .go .kept { color: var(--faint); font: 11px/1 var(--mono); }
            .run.head { background: none; padding-top: 0; padding-bottom: 0; color: var(--faint); font: 500 10.5px/1 var(--mono); letter-spacing: 0.12em; text-transform: uppercase; }
            .run.head span { color: inherit; font: inherit; }
            .runs .more { margin-top: 10px; text-align: center; color: var(--faint); font: 11px/1 var(--mono); letter-spacing: 0.12em; }
            @media (max-width: 760px) {
              .run { grid-template-columns: 34px minmax(0, 1fr) auto; row-gap: 8px; }
              .run .under { display: none; }
              .run .go { grid-column: 2 / -1; justify-content: flex-start; min-width: 0; }
              .run.head { display: none; }
            }
          </style>
        </head>
        <body>
        {{SiteChrome.Nav(SiteChrome.Leaderboard)}}

          <main class="wrap">
            <section id="board">
              <div class="banner">
                <div>
                  <span class="mono" id="eyebrow">Official servers</span>
                  <h1>Leaderboard</h1>
                  <p>Every match on an official server counts: a kill 10, an assist 5, an objective 25, a win 50. Sign in with Discord on the game's Profile page to be on the board under your name.</p>
                  <span class="clock" id="clock" hidden></span>
                </div>
                <div class="switches">
                  <div class="segmented" role="group" aria-label="Boards" id="modes">
                    <button type="button" data-mode="matches" aria-pressed="true">Multiplayer</button>
                    <button type="button" data-mode="missions" aria-pressed="false">Single player</button>
                  </div>
                  <span id="board-game"></span>
                  <div class="segmented" role="group" aria-label="Period" id="periods">
                    <button type="button" data-period="season" aria-pressed="true">This season</button>
                    <button type="button" data-period="all" aria-pressed="false">All time</button>
                  </div>
                  <select class="past" id="past" aria-label="A season gone by" hidden></select>
                </div>
              </div>
              <div id="matches-board">
                <div class="tabs" role="tablist" aria-label="Ranked by" id="metrics"></div>
                <div class="podium" id="podium"><div class="skeleton"></div><div class="skeleton"></div><div class="skeleton"></div></div>
                <div class="ranks" id="ranks"></div>
              </div>
              <div id="missions-board" hidden>
                <div class="picks">
                  <select class="past picked" id="mission" aria-label="Mission"></select>
                  <select class="past picked" id="difficulty" aria-label="Difficulty">
                    <option value="0">Easy</option><option value="1">Normal</option><option value="2" selected>Hard</option>
                  </select>
                  <select class="past picked" id="ruleset" aria-label="Rules">
                    <option value="classic">Classic rules</option><option value="modern">Modern rules</option>
                  </select>
                </div>
                <div class="tabs" role="tablist" aria-label="Ranked by" id="run-metrics"></div>
                <div class="runs" id="runs"></div>
              </div>
            </section>

            <section id="record" hidden>
              <div class="record-switch" id="record-game"></div>
              <div class="hero">
                <span id="avatar"></span>
                <div>
                  <span class="mono" id="season"></span>
                  <h1 id="name"></h1>
                  <div class="tags" id="tags"></div>
                </div>
              </div>
              <div class="tiles" id="figures"></div>
              <h2 class="part">Medals</h2>
              <div class="medals" id="medals"></div>
              <h2 class="part">Latest matches</h2>
              <div class="matches" id="matches"></div>
            </section>

            <div class="actions">
              <a class="button primary" href="/#play-now">Play free in the browser <span aria-hidden="true">→</span></a>
              <a class="button" href="/leaderboard/" id="back" hidden>The whole board</a>
            </div>
          </main>

        {{SiteChrome.Footer}}

          <script type="module">
            import { GAMES, METRICS, ask, avatar, daysLeft, el, gameOf, gameSwitch, kda, kdaLine, number, podiumCard, rankHead, rankRow, seasonName, winRate } from "/landing/board.js";

            const $ = (id) => document.getElementById(id);
            const params = new URLSearchParams(location.search);
            let period = "season", metric = "score", game = gameOf(params.get("game"));
            const gameName = () => GAMES.find(([id]) => id === game)[1];

            // The game in the address, so a link or a reload keeps to it.
            function pickGame(id, show) {
              game = id;
              const address = new URL(location.href);
              address.searchParams.set("game", id);
              history.replaceState(null, "", address);
              show();
            }
            const label = () => METRICS.find(([id]) => id === metric)[1];

            // The metric tabs, from the shared list.
            $("metrics").replaceChildren(...METRICS.map(([id, text]) => {
              const tab = el("button", null, text);
              tab.type = "button";
              tab.role = "tab";
              tab.dataset.metric = id;
              tab.setAttribute("aria-selected", String(id === metric));
              tab.addEventListener("click", () => {
                metric = id;
                for (const other of $("metrics").children) other.setAttribute("aria-selected", String(other === tab));
                showBoard();
              });
              return tab;
            }));

            // The period: this season, all time, or a season gone by from the pick under them.
            function pickPeriod(id) {
              period = id;
              for (const other of document.querySelectorAll("#periods button")) other.setAttribute("aria-pressed", String(other.dataset.period === id));
              const past = $("past");
              past.classList.toggle("picked", /^\d{4}-\d{2}$/.test(id));
              if (!past.classList.contains("picked")) past.value = "";
              showBoard();
            }

            for (const button of document.querySelectorAll("#periods button")) {
              button.addEventListener("click", () => pickPeriod(button.dataset.period));
            }

            $("past").addEventListener("change", () => pickPeriod($("past").value || "season"));

            // The seasons gone by that the board names (newest first, this one leading), offered once there is one.
            function showPast(seasons) {
              const past = $("past");
              const gone = (seasons ?? []).slice(1);
              past.hidden = gone.length === 0;
              const chosen = /^\d{4}-\d{2}$/.test(period) ? period : "";
              const first = el("option", null, "Past seasons");
              first.value = "";
              past.replaceChildren(first, ...gone.map((s) => Object.assign(el("option", null, seasonName(s)), { value: s })));
              past.value = chosen;
            }

            async function showBoard() {
              const podium = $("podium");
              const ranks = $("ranks");
              try {
                const board = await ask(`/v1/leaderboard?title=${game}&period=${period}&metric=${metric}`);
                const season = period !== "all";
                const current = period === "season";
                showPast(board.seasons);
                $("eyebrow").textContent = season ? `${gameName()} · season ${seasonName(board.season)}` : `${gameName()} · all time`;
                const clock = $("clock");
                clock.hidden = !current;
                if (current) {
                  const left = daysLeft();
                  clock.replaceChildren("Season ends in ", el("b", null, `${left} ${left === 1 ? "day" : "days"}`));
                }

                if (board.rows.length === 0) {
                  podium.replaceChildren(el("div", "empty", `Nobody yet in ${gameName()}: play a match on an official server to be the first.`));
                  ranks.replaceChildren();
                  return;
                }

                podium.replaceChildren(...board.rows.slice(0, 3).map((row) => podiumCard(row, metric, label(), game)));
                const rest = board.rows.slice(3);
                ranks.replaceChildren(...(rest.length > 0 ? [rankHead(label()), ...rest.map((row) => rankRow(row, metric, game))] : []));
              } catch {
                podium.replaceChildren(el("div", "empty", "The leaderboard can't be reached right now. Try again in a moment."));
                ranks.replaceChildren();
              }
            }

            // A figure tile: a big number, its label, and a quieter line under it.
            function tile(labelText, value, sub, lit = false, share = null) {
              const box = el("div", `figure${lit ? " lit" : ""}`);
              box.append(el("b", null, value), el("span", null, labelText));
              if (share !== null) {
                const bar = el("span", "bar");
                const fill = el("span");
                fill.style.width = `${Math.round(share * 100)}%`;
                bar.append(fill);
                box.append(bar);
              }

              if (sub) box.append(el("small", null, sub));
              return box;
            }

            const hours = (seconds) => (seconds >= 3600 ? `${(seconds / 3600).toFixed(1)} h` : `${Math.round(seconds / 60)} min`);
            const percent = (share) => `${Math.round(share * 100)}%`;
            const modes = { tdm: "Team deathmatch", ffa: "Free for all", sd: "Search & destroy", elim: "Elimination", objective: "Objective" };

            async function showRecord(player) {
              $("board").hidden = true;
              $("record").hidden = false;
              $("back").hidden = false;
              $("back").href = `/leaderboard/?game=${game}`;
              try {
                const [record, account, board] = await Promise.all([
                  ask(`/v1/players/${encodeURIComponent(player)}/stats?title=${game}`),
                  ask(`/v1/players/${encodeURIComponent(player)}`).catch(() => null),
                  ask(`/v1/leaderboard?title=${game}&period=season&metric=score`).catch(() => null),
                ]);
                const name = account?.name ?? record.latest?.[0]?.you?.[0]?.name ?? "Operative";
                document.title = `${name} · OpenIGI`;
                $("name").textContent = name;
                $("season").textContent = `${gameName()} · season ${seasonName(record.season)}`;
                $("avatar").replaceWith(Object.assign(avatar(name, account?.avatar), { id: "avatar" }));

                const s = record.seasonTotals;
                const a = record.allTime;
                const tags = [];
                const placed = board?.rows.find((row) => row.player === player || row.account?.id === player);
                if (placed) tags.push(el("span", "tag rank", `#${placed.rank} this season`));
                if (account) tags.push(el("span", "tag", "Discord linked"));
                tags.push(el("span", "tag", `${hours(a.seconds ?? 0)} played`));
                $("tags").replaceChildren(...tags);

                $("figures").replaceChildren(
                  tile("Score", number(s.score), `${number(a.score)} all time`, true),
                  tile("Kills", number(s.kills), `${number(a.kills)} all time`),
                  tile("Deaths", number(s.deaths), `${number(a.deaths)} all time`),
                  tile("Assists", number(s.assists ?? 0), `${number(a.assists ?? 0)} all time`),
                  tile("K/D", (s.kd ?? 0).toFixed(2), `${(a.kd ?? 0).toFixed(2)} all time`),
                  tile("KDA", kda(s).toFixed(2), `${kda(a).toFixed(2)} all time`),
                  tile("Win rate", percent(winRate(s)), `${number(s.wins)} of ${number(s.matches)} won`, false, winRate(s)),
                  tile("Matches", number(s.matches), `${number(a.matches)} all time`));

                const medals = (record.medals ?? []).map((medal) => {
                  const chip = el("div", "medal");
                  const body = el("div");
                  body.append(el("b", null, medal.name), el("span", null, medal.about));
                  chip.append(el("span", "emblem", "★"), body);
                  return chip;
                });
                $("medals").replaceChildren(...(medals.length > 0 ? medals : [el("div", "empty", "None yet: a kill on an official server is the first.")]));

                const matches = (record.latest ?? []).map((match) => {
                  const you = match.you?.[0];
                  const card = el("div", `match ${you?.won ? "won" : "lost"}`);
                  const what = el("div", "what");
                  const when = new Date(match.at).toLocaleDateString("en", { day: "numeric", month: "short" });
                  what.append(el("b", null, `${modes[match.mode] ?? match.mode.toUpperCase()} · map ${match.map}`), el("span", null, `${when} · ${match.server ?? "Official"} · ${number(you?.score ?? 0)} points`));
                  card.append(el("span", "stripe"), what, kdaLine(you ?? {}), el("span", "result", you?.won ? "WON" : "LOST"));
                  return card;
                });
                $("matches").replaceChildren(...(matches.length > 0 ? matches : [el("div", "empty", `No matches in ${gameName()} yet.`)]));
              } catch {
                $("name").textContent = "Operative";
                $("figures").replaceChildren(el("div", "empty", "This player has no matches on the official servers yet, or the stats can't be reached right now."));
              }
            }

            // The single-player mission boards: every run on them was played again by the server before it counted.
            const MISSIONS = {{MissionNames}};
            const RANKS = {{RankNames}};
            const BOARD_TEXT = $("board").querySelector(".banner p").textContent;
            const RUNS_TEXT = "Every run is played again by the server from its inputs before it counts, so a time on the board is one the game really allows. A replay can't prove a person played it live, so every new first place and every reported run is looked at by a person too. Sign in with Discord on the game's Profile page to be on the board under your name.";
            const RUN_METRICS = [["time", "Fastest"], ["rank", "Best rank"]];
            let missions = params.get("board") === "missions", runMetric = "time";
            const time = (ticks) => {
              const seconds = Math.max(0, ticks ?? 0) / 30;
              const minutes = Math.floor(seconds / 60);
              return `${minutes}:${(seconds - minutes * 60).toFixed(1).padStart(4, "0")}`;
            };

            // The run tabs: the fastest or the best rank.
            $("run-metrics").replaceChildren(...RUN_METRICS.map(([id, text]) => {
              const tab = el("button", null, text);
              tab.type = "button";
              tab.role = "tab";
              tab.setAttribute("aria-selected", String(id === runMetric));
              tab.addEventListener("click", () => {
                runMetric = id;
                for (const other of $("run-metrics").children) other.setAttribute("aria-selected", String(other === tab));
                showRuns();
              });
              return tab;
            }));
            for (const pick of ["mission", "difficulty", "ruleset"]) $(pick).addEventListener("change", showRuns);

            // The game's missions in the pick, every one of them together first.
            function fillMissions() {
              const all = el("option", null, "All missions");
              all.value = "all";
              $("mission").replaceChildren(all, ...(MISSIONS[game] ?? []).map((name, i) => Object.assign(el("option", null, `${i + 1}. ${name}`), { value: String(i + 1) })));
            }

            // A board's row: its place, the player, their figure and, where the replay is kept, Watch and Race in the browser.
            function runRow(row, overall) {
              const line = el("div", "run");
              const place = el("span", `n${row.place <= 3 ? " top" : ""}`, `#${row.place}`);
              const who = el("span", "player");
              who.append(avatar(row.name, null), el("span", "name", row.name));
              const rank = (row.rank >= 0 && row.rank < 3 ? RANKS[Number($("difficulty").value) * 3 + row.rank] : null) ?? "No rank";
              const [figure, under] = overall
                ? (runMetric === "rank" ? [`${number(row.points)} pts`, time(row.ticks)] : [time(row.ticks), `${row.missions} missions`])
                : (runMetric === "rank" ? [rank, time(row.ticks)] : [time(row.ticks), rank]);
              const go = el("span", "go");
              if (!overall && row.replay && /^[0-9a-f]{32}$/.test(row.run ?? "")) {
                const open = (key, text) => Object.assign(el("a", null, text), { href: `/?game=${game}&play&${key}=${row.run}`, title: `${text} this run in the browser` });
                go.append(open("replay", "Watch"), open("race", "Race"));
              } else if (!overall) {
                go.append(el("span", "kept", "Replay not kept"));
              }

              line.append(place, who, el("span", "figure", figure), el("span", "under", under), go);
              return line;
            }

            async function showRuns() {
              const list = $("runs");
              const mission = $("mission").value || "all";
              const overall = mission === "all";
              const address = new URL(location.href);
              address.searchParams.set("mission", mission);
              address.searchParams.set("difficulty", $("difficulty").value);
              address.searchParams.set("rules", $("ruleset").value);
              history.replaceState(null, "", address);
              $("eyebrow").textContent = `${gameName()} · mission boards`;
              $("clock").hidden = true;
              try {
                const board = await ask(`/v1/runs/board?title=${game}&mission=${mission}&difficulty=${$("difficulty").value}&ruleset=${$("ruleset").value}&metric=${runMetric}`);
                if (board.rows.length === 0) {
                  list.replaceChildren(el("div", "empty", overall
                    ? `Nobody has run every mission of ${gameName()} on these rules yet.`
                    : "Nobody on this board yet: complete the mission signed in with Discord to be the first."));
                  return;
                }

                const head = el("div", "run head");
                head.append(el("span", null, "#"), el("span", null, "Player"), el("span", "figure", runMetric === "rank" ? (overall ? "Points" : "Rank") : "Time"), el("span", "under", ""), el("span", "go", ""));
                const more = board.players > board.rows.length ? [el("div", "more", `THE TOP ${board.rows.length} OF ${number(board.players)} RANKED`)] : [];
                list.replaceChildren(head, ...board.rows.map((row) => runRow(row, overall)), ...more);
              } catch {
                list.replaceChildren(el("div", "empty", "The mission boards can't be reached right now. Try again in a moment."));
              }
            }

            // The matches' boards or the missions', kept in the address.
            function pickMode(toMissions) {
              missions = toMissions;
              for (const other of document.querySelectorAll("#modes button")) other.setAttribute("aria-pressed", String((other.dataset.mode === "missions") === missions));
              $("matches-board").hidden = missions;
              $("missions-board").hidden = !missions;
              $("periods").hidden = missions;
              $("past").hidden = missions || $("past").options.length <= 1;
              $("board").querySelector(".banner p").textContent = missions ? RUNS_TEXT : BOARD_TEXT;
              const address = new URL(location.href);
              if (missions) address.searchParams.set("board", "missions");
              else for (const key of ["board", "mission", "difficulty", "rules"]) address.searchParams.delete(key);
              history.replaceState(null, "", address);
              if (missions) showRuns(); else showBoard();
            }

            for (const button of document.querySelectorAll("#modes button")) button.addEventListener("click", () => pickMode(button.dataset.mode === "missions"));

            const player = params.get("player");
            fillMissions();
            // A shared board's picks, where the address names ones the page offers.
            for (const [pick, key] of [["mission", "mission"], ["difficulty", "difficulty"], ["ruleset", "rules"]]) {
              if ([...$(pick).options].some((option) => option.value === params.get(key))) $(pick).value = params.get(key);
            }
            $("board-game").replaceWith(gameSwitch(game, (id) => pickGame(id, () => { fillMissions(); if (missions) showRuns(); else showBoard(); })));
            $("record-game").replaceChildren(gameSwitch(game, (id) => pickGame(id, () => showRecord(player))));
            if (player) showRecord(player); else pickMode(missions);
          </script>
        </body>
        </html>
        """;
}
