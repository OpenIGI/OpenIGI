namespace OpenIGI.Tools.Release;

/// <summary>
/// The page an invite link opens: <c>/join/CODE</c> for a friend's room, <c>/party/CODE</c> for a friend's party. The site is
/// static, so it is the site's <c>404.html</c>, which the host serves for every path it has no file for; the page reads the
/// code from its own address and offers the two ways in -- the browser build at the site's root, or the installed game
/// through the launcher's <c>openigi://</c> link. Any other missing path gets a plain "not here".
/// </summary>
internal static class JoinPage
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
          <meta name="robots" content="noindex">
          <title>Join a friend · OpenIGI</title>
          <link rel="icon" type="image/png" sizes="192x192" href="/icons/icon-192.png">
          <style>
            @font-face { font-family: "Archivo"; src: url("/landing/archivo.woff2") format("woff2"); font-weight: 400 800; font-stretch: 62% 125%; font-display: swap; }
            @font-face { font-family: "JetBrains Mono"; src: url("/landing/jetbrains-mono.woff2") format("woff2"); font-weight: 400 500; font-display: swap; }
            :root { --bg: #0b0c0b; --text: #ecebe6; --soft: #c9c7c0; --muted: #b4b2ab; --faint: #8a8882; --line: rgba(236, 235, 230, 0.14);
                    --sans: "Archivo", system-ui, sans-serif; --mono: "JetBrains Mono", ui-monospace, Consolas, monospace; }
            * { box-sizing: border-box; }
            html, body { margin: 0; min-height: 100%; }
            body {
              background:
                radial-gradient(90% 80% at 0% 100%, rgba(11, 12, 11, 0.85) 0%, rgba(11, 12, 11, 0.5) 45%, rgba(11, 12, 11, 0.1) 80%),
                url("/landing/bg.webp") center / cover no-repeat fixed,
                var(--bg);
              color: var(--text); font-family: var(--sans); -webkit-font-smoothing: antialiased;
              display: grid; min-height: 100vh; padding: clamp(16px, 4vw, 48px); align-content: end;
            }
            main { max-width: 620px; display: flex; flex-direction: column; gap: 22px; }
            .mono { font-family: var(--mono); font-size: 11.5px; letter-spacing: 0.12em; text-transform: uppercase; color: var(--muted); }
            h1 { margin: 0; font-size: clamp(38px, 6vw, 68px); line-height: 0.98; font-weight: 700; font-stretch: 80%; letter-spacing: -0.015em; }
            p { margin: 0; font-size: 17px; line-height: 1.55; color: var(--soft); }
            .code { font-family: var(--mono); font-size: clamp(34px, 7vw, 54px); letter-spacing: 0.22em; font-weight: 500; }
            .ways { display: flex; flex-wrap: wrap; gap: 12px; }
            a.button, button {
              display: inline-flex; align-items: center; justify-content: center; min-height: 48px; padding: 0 22px;
              border-radius: 10px; border: 1px solid var(--line); background: rgba(11, 12, 11, 0.6); color: var(--text);
              font: 600 15px var(--sans); letter-spacing: 0.06em; text-transform: uppercase; text-decoration: none; cursor: pointer;
            }
            a.button.primary { background: var(--text); color: var(--bg); border-color: var(--text); }
            a.button:focus-visible, button:focus-visible { outline: 2px solid var(--text); outline-offset: 3px; }
            .note { font-size: 14px; color: var(--faint); }
            [hidden] { display: none !important; }
          </style>
        </head>
        <body>
          <main id="invite" hidden>
            <span class="mono" id="kind">A friend's room</span>
            <h1 id="title">You're invited to play Project I.G.I. together</h1>
            <div class="code" id="code" aria-label="Code"></div>
            <p id="lead"></p>
            <div class="ways">
              <a class="button primary" id="browser" href="/">Play in your browser</a>
              <a class="button" id="game" href="/">Open in the game</a>
              <button type="button" id="copy">Copy the code</button>
            </div>
            <p class="note" id="note">Open in the game needs the OpenIGI Launcher installed. You can also type the code on the game's Co-op page.</p>
          </main>
          <main id="missing" hidden>
            <span class="mono">OpenIGI</span>
            <h1>Nothing here</h1>
            <p>This page doesn't exist. An invite link looks like openigi.com/join/ABC234.</p>
            <div class="ways"><a class="button primary" href="/">Go to OpenIGI</a></div>
          </main>
          <script>
            (() => {
              // /join/CODE or /party/CODE: a code as the master server makes them, six of its letters and digits.
              const match = /^\/(join|party)\/([A-Za-z0-9]{6})\/?$/.exec(location.pathname);
              const code = match ? match[2].toUpperCase() : null;
              const valid = code !== null && /^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{6}$/.test(code);
              if (!valid) {
                document.getElementById('missing').hidden = false;
                return;
              }

              const party = match[1] === 'party';
              document.title = (party ? 'Join a party' : 'Join a friend') + ' · OpenIGI';
              document.getElementById('kind').textContent = party ? "A friend's party" : "A friend's room";
              document.getElementById('title').textContent = party ? 'Join the party' : "You're invited to play together";
              document.getElementById('code').textContent = code;
              document.getElementById('lead').textContent = party
                ? 'Wherever the party\'s leader plays, your game follows: into every room and server, without a code each time.'
                : 'Your friend is playing a mission in co-op. Join from the browser, or from the game if you have it installed.';
              document.getElementById('browser').href = '/?' + (party ? 'party=' : 'join=') + code;
              document.getElementById('game').href = 'openigi://' + (party ? 'party/' : 'join/') + code;
              const copy = document.getElementById('copy');
              copy.addEventListener('click', async () => {
                try {
                  await navigator.clipboard.writeText(code);
                  copy.textContent = 'Copied';
                } catch {
                  copy.textContent = code;
                }
              });
              document.getElementById('invite').hidden = false;
            })();
          </script>
        </body>
        </html>
        """;
}
