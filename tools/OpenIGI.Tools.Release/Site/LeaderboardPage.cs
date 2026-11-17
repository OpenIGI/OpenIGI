namespace OpenIGI.Tools.Release;

/// <summary>
/// <c>/leaderboard/</c>: the best players on the official servers this season or all time, by score, kills, wins, K/D or
/// matches, read from the master as the page opens; <c>/leaderboard/?player=ID</c> (and <c>/u/ID</c>, which the 404 page
/// sends here) is one player's record by their Discord user id or a key's: this season, their medals and their latest
/// matches. Static like the rest of the site; everything shown is set as text, never as markup.
/// </summary>
internal static class LeaderboardPage
{
    /// <summary>The page.</summary>
    public const string Html = """
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
          <style>
            @font-face { font-family: "Archivo"; src: url("/landing/archivo.woff2") format("woff2"); font-weight: 400 800; font-stretch: 62% 125%; font-display: swap; }
            @font-face { font-family: "JetBrains Mono"; src: url("/landing/jetbrains-mono.woff2") format("woff2"); font-weight: 400 500; font-display: swap; }
            :root { --bg: #0b0c0b; --panel: rgba(11, 12, 11, 0.72); --text: #ecebe6; --soft: #c9c7c0; --muted: #b4b2ab; --faint: #8a8882;
                    --line: rgba(236, 235, 230, 0.14); --accent: #e8a33d;
                    --sans: "Archivo", system-ui, sans-serif; --mono: "JetBrains Mono", ui-monospace, Consolas, monospace; }
            * { box-sizing: border-box; }
            html, body { margin: 0; min-height: 100%; }
            body {
              background:
                linear-gradient(180deg, rgba(11, 12, 11, 0.55) 0%, rgba(11, 12, 11, 0.92) 55%),
                url("/landing/bg.webp") center top / cover no-repeat fixed,
                var(--bg);
              color: var(--text); font-family: var(--sans); -webkit-font-smoothing: antialiased;
              padding: clamp(16px, 4vw, 48px);
            }
            main { max-width: 880px; margin: 0 auto; display: flex; flex-direction: column; gap: 20px; }
            .mono { font-family: var(--mono); font-size: 11.5px; letter-spacing: 0.12em; text-transform: uppercase; color: var(--muted); }
            h1 { margin: 0; font-size: clamp(38px, 6vw, 64px); line-height: 0.98; font-weight: 700; font-stretch: 80%; letter-spacing: -0.015em; }
            p { margin: 0; font-size: 16px; line-height: 1.55; color: var(--soft); }
            nav { display: flex; flex-wrap: wrap; gap: 8px; }
            nav button, a.button {
              min-height: 40px; padding: 0 16px; border-radius: 9px; border: 1px solid var(--line); background: var(--panel); color: var(--soft);
              font: 600 13px var(--sans); letter-spacing: 0.08em; text-transform: uppercase; cursor: pointer; text-decoration: none;
              display: inline-flex; align-items: center;
            }
            nav button[aria-pressed="true"] { background: var(--accent); color: var(--bg); border-color: var(--accent); }
            a.button.primary { background: var(--text); color: var(--bg); border-color: var(--text); }
            nav button:focus-visible, a.button:focus-visible { outline: 2px solid var(--text); outline-offset: 3px; }
            .panel { background: var(--panel); border: 1px solid var(--line); border-radius: 12px; overflow: hidden; }
            table { width: 100%; border-collapse: collapse; font-size: 15px; }
            th, td { padding: 12px 14px; text-align: right; white-space: nowrap; }
            th { font: 500 11px var(--mono); letter-spacing: 0.12em; text-transform: uppercase; color: var(--faint); border-bottom: 1px solid var(--line); }
            th:nth-child(2), td:nth-child(2) { text-align: left; width: 100%; }
            th:first-child, td:first-child { text-align: left; color: var(--faint); font-family: var(--mono); }
            tr + tr td { border-top: 1px solid rgba(236, 235, 230, 0.06); }
            td.key { color: var(--accent); font-weight: 700; }
            .who { display: inline-flex; align-items: center; gap: 10px; color: var(--text); text-decoration: none; font-weight: 600; }
            .who img { width: 26px; height: 26px; border-radius: 50%; }
            .medal1 { color: #f2c94c !important; } .medal2 { color: #d0d4d9 !important; } .medal3 { color: #d39b6a !important; }
            .empty { padding: 28px; color: var(--muted); }
            .stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(120px, 1fr)); gap: 1px; background: var(--line); }
            .stat { background: var(--bg); padding: 18px; }
            .stat b { display: block; font-size: 30px; font-stretch: 80%; }
            .profile { display: flex; align-items: center; gap: 16px; }
            .profile img { width: 64px; height: 64px; border-radius: 50%; border: 2px solid var(--accent); }
            .medals { display: flex; flex-wrap: wrap; gap: 8px; }
            .medal { border: 1px solid var(--accent); border-radius: 999px; padding: 6px 12px; font: 600 13px var(--sans); color: var(--accent); background: rgba(232, 163, 61, 0.08); }
            .medal.none { border-color: var(--line); color: var(--muted); background: none; }
            h2 { margin: 24px 0 10px; font: 500 11.5px var(--mono); letter-spacing: 0.12em; text-transform: uppercase; color: var(--muted); }
            td.won { color: var(--accent); }
            .narrow-hide { }
            @media (max-width: 620px) { .narrow-hide { display: none; } th, td { padding: 10px 8px; } }
            [hidden] { display: none !important; }
          </style>
        </head>
        <body>
          <main>
            <span class="mono" id="eyebrow">Official servers</span>
            <h1 id="title">Leaderboard</h1>
            <p id="lead">Matches on the official servers count. Sign in with Discord on the game's Profile page to be on the board.</p>

            <section id="board">
              <nav aria-label="Period" id="periods">
                <button data-period="season" aria-pressed="true">This season</button>
                <button data-period="all" aria-pressed="false">All time</button>
              </nav>
              <nav aria-label="Ranked by" id="metrics" style="margin-top: 8px">
                <button data-metric="score" aria-pressed="true">Score</button>
                <button data-metric="kills" aria-pressed="false">Kills</button>
                <button data-metric="wins" aria-pressed="false">Wins</button>
                <button data-metric="kd" aria-pressed="false">K/D</button>
                <button data-metric="matches" aria-pressed="false">Matches</button>
              </nav>
              <div class="panel" style="margin-top: 14px">
                <table>
                  <thead><tr><th>#</th><th>Player</th><th>Score</th><th class="narrow-hide">Kills</th><th class="narrow-hide">Wins</th><th>K/D</th><th class="narrow-hide">Matches</th></tr></thead>
                  <tbody id="rows"><tr><td colspan="7" class="empty">Loading…</td></tr></tbody>
                </table>
              </div>
            </section>

            <section id="record" hidden>
              <div class="profile"><img id="avatar" alt="" hidden><div><span class="mono" id="season"></span><h1 id="name" style="font-size: 44px"></h1></div></div>
              <div class="panel stats" id="figures" style="margin-top: 14px"></div>
              <p id="alltime" style="margin-top: 12px"></p>
              <h2>Medals</h2>
              <div class="medals" id="medals"></div>
              <h2>Latest matches</h2>
              <div class="panel">
                <table>
                  <thead><tr><th>When</th><th>Match</th><th>Kills</th><th>Deaths</th><th>Result</th></tr></thead>
                  <tbody id="matches"></tbody>
                </table>
              </div>
            </section>

            <div style="display: flex; flex-wrap: wrap; gap: 12px">
              <a class="button primary" href="/">Play free in the browser</a>
              <a class="button" href="/leaderboard/" id="back" hidden>The whole board</a>
              <a class="button" href="/privacy/">Privacy</a>
            </div>
          </main>
          <script>
            const master = "https://master.openigi.com";
            const $ = (id) => document.getElementById(id);
            const params = new URLSearchParams(location.search);
            const number = (n) => (typeof n === "number" ? n.toLocaleString("en") : "—");
            const seasonName = (s) => /^\d{4}-\d{2}$/.test(s)
              ? new Date(Date.UTC(+s.slice(0, 4), +s.slice(5) - 1, 1)).toLocaleString("en", { month: "long", year: "numeric", timeZone: "UTC" })
              : s;
            const cell = (text, className) => { const td = document.createElement("td"); td.textContent = text; if (className) td.className = className; return td; };
            let period = "season", metric = "score";

            async function ask(path) {
              const answer = await fetch(master + path);
              if (!answer.ok) throw new Error(String(answer.status));
              return answer.json();
            }

            async function showBoard() {
              const rows = $("rows");
              try {
                const board = await ask(`/v1/leaderboard?period=${period}&metric=${metric}`);
                $("eyebrow").textContent = period === "all" ? "Official servers · all time" : `Official servers · season ${seasonName(board.season)}`;
                rows.replaceChildren();
                if (board.rows.length === 0) {
                  const tr = document.createElement("tr");
                  tr.append(cell("Nobody yet: play a match on an official server to be the first.", "empty"));
                  tr.firstChild.colSpan = 7;
                  rows.append(tr);
                  return;
                }
                for (const row of board.rows) {
                  const tr = document.createElement("tr");
                  tr.append(cell(String(row.rank), row.rank <= 3 ? `medal${row.rank}` : ""));
                  const who = document.createElement("td");
                  const link = document.createElement("a");
                  link.className = "who";
                  link.href = `/leaderboard/?player=${encodeURIComponent(row.account ? row.account.id : row.player)}`;
                  if (row.account?.avatar) { const img = document.createElement("img"); img.src = row.account.avatar; img.alt = ""; link.append(img); }
                  link.append(document.createTextNode(row.name));
                  who.append(link);
                  tr.append(who);
                  for (const [key, text, narrow] of [["score", number(row.score)], ["kills", number(row.kills), true], ["wins", number(row.wins), true], ["kd", row.kd.toFixed(2)], ["matches", number(row.matches), true]]) {
                    tr.append(cell(text, [key === metric ? "key" : "", narrow ? "narrow-hide" : ""].join(" ").trim()));
                  }
                  rows.append(tr);
                }
              } catch {
                rows.replaceChildren();
                const tr = document.createElement("tr");
                tr.append(cell("The leaderboard can't be reached right now. Try again in a moment.", "empty"));
                tr.firstChild.colSpan = 7;
                rows.append(tr);
              }
            }

            async function showRecord(player) {
              $("board").hidden = true;
              $("record").hidden = false;
              $("back").hidden = false;
              $("title").hidden = true;
              $("lead").hidden = true;
              try {
                const [record, account] = await Promise.all([ask(`/v1/players/${encodeURIComponent(player)}/stats`), ask(`/v1/players/${encodeURIComponent(player)}`).catch(() => null)]);
                const name = account?.name ?? record.latest?.[0]?.you?.[0]?.name ?? "Operative";
                document.title = `${name} · OpenIGI`;
                $("name").textContent = name;
                $("season").textContent = `Season ${seasonName(record.season)}`;
                if (account?.avatar) { $("avatar").src = account.avatar; $("avatar").hidden = false; }
                const s = record.seasonTotals;
                $("figures").replaceChildren(...[["Matches", number(s.matches)], ["Wins", number(s.wins)], ["Kills", number(s.kills)], ["Deaths", number(s.deaths)], ["K/D", s.kd.toFixed(2)], ["Score", number(s.score)]].map(([label, value]) => {
                  const box = document.createElement("div");
                  box.className = "stat";
                  const b = document.createElement("b");
                  b.textContent = value;
                  const span = document.createElement("span");
                  span.className = "mono";
                  span.textContent = label;
                  box.append(b, span);
                  return box;
                }));
                const a = record.allTime;
                $("alltime").textContent = `All time: ${number(a.matches)} matches, ${number(a.wins)} wins, ${number(a.kills)} kills, ${a.kd.toFixed(2)} K/D.`;
                const medals = (record.medals ?? []).map((medal) => {
                  const chip = document.createElement("span");
                  chip.className = "medal";
                  chip.textContent = medal.name;
                  chip.title = medal.about;
                  return chip;
                });
                if (medals.length === 0) {
                  const none = document.createElement("span");
                  none.className = "medal none";
                  none.textContent = "None yet: a kill on an official server is the first.";
                  medals.push(none);
                }
                $("medals").replaceChildren(...medals);
                const modes = { tdm: "Team deathmatch", ffa: "Free for all", sd: "Search & destroy", elim: "Elimination" };
                const matches = (record.latest ?? []).map((match) => {
                  const you = match.you?.[0];
                  const tr = document.createElement("tr");
                  tr.append(
                    cell(new Date(match.at).toLocaleDateString("en", { day: "numeric", month: "short" })),
                    cell(`${modes[match.mode] ?? match.mode.toUpperCase()} · map ${match.map}`),
                    cell(number(you?.kills)),
                    cell(number(you?.deaths)),
                    cell(you?.won ? "Won" : "Lost", you?.won ? "won" : ""));
                  return tr;
                });
                if (matches.length === 0) {
                  const tr = document.createElement("tr");
                  tr.append(cell("No matches yet.", "empty"));
                  tr.firstChild.colSpan = 5;
                  matches.push(tr);
                }
                $("matches").replaceChildren(...matches);
              } catch {
                $("name").textContent = "No record";
                $("alltime").textContent = "This player has no matches on the official servers yet, or the stats can't be reached right now.";
              }
            }

            for (const button of document.querySelectorAll("#periods button")) {
              button.addEventListener("click", () => {
                period = button.dataset.period;
                for (const other of document.querySelectorAll("#periods button")) other.setAttribute("aria-pressed", String(other === button));
                showBoard();
              });
            }
            for (const button of document.querySelectorAll("#metrics button")) {
              button.addEventListener("click", () => {
                metric = button.dataset.metric;
                for (const other of document.querySelectorAll("#metrics button")) other.setAttribute("aria-pressed", String(other === button));
                showBoard();
              });
            }

            const player = params.get("player");
            if (player && /^([0-9a-f]{16}|\d{5,25})$/.test(player)) showRecord(player); else showBoard();
          </script>
        </body>
        </html>
        """;
}
