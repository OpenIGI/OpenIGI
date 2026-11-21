namespace OpenIGI.Tools.Release;

/// <summary>
/// <c>/servers/</c>: every session being played online -- the dedicated servers and the rooms players opened to the public, co-op
/// and versus, in either game -- each with the two ways in: the browser build that plays its protocol, and the installed game
/// through the launcher (<c>openigi://</c>). Read from the master as the page opens and every few seconds while it is looked
/// at, from the list it keeps in memory for everyone (<c>/landing/sessions.js</c> draws it); static like the rest of the site.
/// </summary>
internal static class ServersPage
{
    /// <summary>The page.</summary>
    public static string Html { get; } = $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
          <meta name="color-scheme" content="dark">
          <meta name="theme-color" content="#0b0c0b">
          <title>Play online · OpenIGI</title>
          <meta name="description" content="The OpenIGI servers and co-op rooms being played right now. Join one in your browser or with the desktop game.">
          <link rel="icon" type="image/png" sizes="192x192" href="/icons/icon-192.png">
          <link rel="preload" href="/landing/archivo.woff2" as="font" type="font/woff2" crossorigin>
          <link rel="stylesheet" href="/landing/site.css">
          <style>
            body {
              min-height: 100vh;
              background:
                radial-gradient(70% 50% at 50% 0%, rgba(127, 194, 140, 0.08) 0%, rgba(127, 194, 140, 0) 70%),
                linear-gradient(180deg, rgba(11, 12, 11, 0.55) 0%, var(--bg) 520px),
                url("/landing/bg.webp") center top / 100% auto no-repeat,
                var(--bg);
            }
            main.wrap { padding-top: clamp(36px, 6vw, 72px); padding-bottom: 72px; }
            .banner { display: flex; justify-content: space-between; align-items: flex-end; gap: 20px 32px; flex-wrap: wrap; margin-bottom: 26px; }
            .banner h1 { margin: 10px 0 0; font-size: clamp(44px, 7vw, 84px); line-height: 0.92; font-weight: 700; font-stretch: 76%; letter-spacing: -0.02em; }
            .banner p { margin: 12px 0 0; max-width: 580px; color: var(--soft); font-size: 16px; line-height: 1.55; }
            .live { display: inline-flex; align-items: center; gap: 8px; color: var(--ok); }
            .live::before { content: ""; width: 7px; height: 7px; border-radius: 50%; background: var(--ok); animation: beat 2s ease-in-out infinite; }
            @keyframes beat { 0%, 100% { box-shadow: 0 0 0 0 rgba(127, 194, 140, 0.45); } 50% { box-shadow: 0 0 0 6px rgba(127, 194, 140, 0); } }
            .totals { display: flex; gap: 28px; }
            .filters { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 12px; margin-bottom: 18px; }
            .launcher { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 10px 18px; margin-bottom: 22px; padding: 14px 18px; border: 1px solid var(--line); border-radius: 12px; background: var(--panel); color: var(--soft); font-size: 14px; line-height: 1.5; }
            .launcher a { color: var(--accent); }
            .sessions .skeleton { height: 220px; }
          </style>
        </head>
        <body>
        {{SiteChrome.Nav(SiteChrome.Servers)}}
          <main class="wrap">
            <div class="banner">
              <div>
                <span class="mono live" id="updated">Live</span>
                <h1>Play online</h1>
                <p>The dedicated servers and the rooms players have opened to everyone, right now. Pick one and play in your browser, or in the desktop game.</p>
              </div>
              <div class="totals" id="totals"></div>
            </div>

            <div class="filters">
              <div class="segmented" role="group" aria-label="Game" id="games">
                <button type="button" data-game="all" aria-pressed="true">Both games</button>
                <button type="button" data-game="igi1" aria-pressed="false">Project I.G.I.</button>
                <button type="button" data-game="igi2" aria-pressed="false">I.G.I. 2</button>
              </div>
              <div class="segmented" role="group" aria-label="Kind" id="kinds">
                <button type="button" data-kind="all" aria-pressed="true">All</button>
                <button type="button" data-kind="coop" aria-pressed="false">Co-op</button>
                <button type="button" data-kind="versus" aria-pressed="false">Versus</button>
              </div>
            </div>

            <div class="launcher">
              <span><b>Play on desktop</b> opens the OpenIGI Launcher, which starts the game straight into the session.</span>
              <span>No launcher yet? <a href="/download/win-x64/">Download for Windows</a> · <a href="/#desktop">macOS and Linux</a></span>
            </div>

            <div class="sessions" id="sessions"><div class="skeleton"></div><div class="skeleton"></div><div class="skeleton"></div></div>
          </main>
        {{SiteChrome.Footer}}
          <script type="module">
            import { el, number } from "/landing/board.js";
            import { bySessionOrder, gameOfSession, isCoop, loadBuilds, loadSessions, sessionCard } from "/landing/sessions.js";

            const $ = (id) => document.getElementById(id);
            const params = new URLSearchParams(location.search);
            let game = ["igi1", "igi2"].includes(params.get("game")) ? params.get("game") : "all";
            let kind = "all";
            let sessions = null;
            const builds = loadBuilds();

            function choose(group, attribute, value, pick) {
              for (const button of $(group).children) {
                button.setAttribute("aria-pressed", String(button.dataset[attribute] === value));
                button.addEventListener("click", () => {
                  for (const other of $(group).children) other.setAttribute("aria-pressed", String(other === button));
                  pick(button.dataset[attribute]);
                  draw();
                });
              }
            }

            choose("games", "game", game, (value) => { game = value; });
            choose("kinds", "kind", kind, (value) => { kind = value; });

            async function draw() {
              const list = $("sessions");
              if (sessions === null) {
                list.replaceChildren(el("div", "empty", "The list of servers can't be reached right now. It tries again by itself."));
                return;
              }

              const shown = sessions
                .filter((s) => game === "all" || gameOfSession(s) === game)
                .filter((s) => kind === "all" || (kind === "coop") === isCoop(s))
                .sort(bySessionOrder);
              const known = await builds;
              list.replaceChildren(...(shown.length > 0
                ? shown.map((s) => sessionCard(s, known))
                : [el("div", "empty", "Nothing being played here right now. Host a room from the game's Co-op page and tick Public to have it listed.")]));
            }

            async function refresh() {
              try {
                sessions = await loadSessions();
                const players = sessions.reduce((sum, s) => sum + (s.players ?? 0), 0);
                $("totals").replaceChildren(...[[sessions.length, "Sessions"], [players, "Playing"]].map(([value, label]) => {
                  const box = el("div", "figure");
                  box.append(el("b", null, number(value)), el("span", null, label));
                  return box;
                }));
                $("updated").textContent = `Live · ${new Date().toLocaleTimeString("en", { hour: "2-digit", minute: "2-digit", second: "2-digit" })}`;
              } catch {
                // The last list stands until the next try; none yet says it can't be reached.
              }

              draw();
            }

            // Every fifteen seconds while the page is looked at, and at once when it is looked at again: the master keeps the list
            // a few seconds for everyone, so a page open in a hundred tabs costs it next to nothing.
            refresh();
            setInterval(() => { if (!document.hidden) refresh(); }, 15000);
            document.addEventListener("visibilitychange", () => { if (!document.hidden) refresh(); });
          </script>
        </body>
        </html>
        """;
}
