namespace OpenIGI.Tools.Release;

/// <summary>
/// <c>/privacy/</c>: what OpenIGI keeps when a player signs in with Discord, plays on the official servers or plays inside
/// Discord, and how to have it removed. Kept short and plain; it must stay true to what the master server stores
/// (<c>tools/master-server/src/accounts.js</c> and <c>stats.js</c>).
/// </summary>
internal static class PrivacyPage
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
          <title>Privacy · OpenIGI</title>
          <meta name="description" content="What OpenIGI keeps when you sign in with Discord or play online, and how to remove it.">
          <link rel="icon" type="image/png" sizes="192x192" href="/icons/icon-192.png">
          <style>
            @font-face { font-family: "Archivo"; src: url("/landing/archivo.woff2") format("woff2"); font-weight: 400 800; font-stretch: 62% 125%; font-display: swap; }
            @font-face { font-family: "JetBrains Mono"; src: url("/landing/jetbrains-mono.woff2") format("woff2"); font-weight: 400 500; font-display: swap; }
            :root { --bg: #0b0c0b; --text: #ecebe6; --soft: #c9c7c0; --muted: #b4b2ab; --line: rgba(236, 235, 230, 0.14); --accent: #e8a33d;
                    --sans: "Archivo", system-ui, sans-serif; --mono: "JetBrains Mono", ui-monospace, Consolas, monospace; }
            * { box-sizing: border-box; }
            body { margin: 0; background: var(--bg); color: var(--text); font-family: var(--sans); -webkit-font-smoothing: antialiased; padding: clamp(16px, 4vw, 48px); }
            main { max-width: 720px; margin: 0 auto; }
            .mono { font-family: var(--mono); font-size: 11.5px; letter-spacing: 0.12em; text-transform: uppercase; color: var(--muted); }
            h1 { margin: 8px 0 20px; font-size: clamp(38px, 6vw, 56px); line-height: 1; font-weight: 700; font-stretch: 80%; }
            h2 { margin: 32px 0 8px; font-size: 20px; }
            p, li { font-size: 16px; line-height: 1.6; color: var(--soft); }
            ul { padding-left: 20px; }
            a { color: var(--accent); }
            footer { margin-top: 40px; padding-top: 16px; border-top: 1px solid var(--line); }
          </style>
        </head>
        <body>
          <main>
            <span class="mono">OpenIGI</span>
            <h1>Privacy</h1>
            <p>You can play OpenIGI without an account. Signing in with Discord is optional, and this page lists everything our
              servers keep when you do, or when you play online.</p>

            <h2>Playing without an account</h2>
            <p>Each installation makes its own key and keeps it on your computer, or in your browser for the web version. Game
              servers see that key and the name you play under. Nothing else about you is sent.</p>

            <h2>Signing in with Discord</h2>
            <p>Discord tells us your user id, your display name and your avatar. We never see your email or your password. We keep:</p>
            <ul>
              <li>your Discord user id, name and avatar, and when you signed in first;</li>
              <li>the keys of the installations you signed in from, each labelled "Desktop" or "Browser";</li>
              <li>a copy of your profile for cloud sync: how far you are in each game, your difficulty, your controls and your settings;</li>
              <li>Discord's token for updating your OpenIGI role connection (your linked role). It is stored encrypted.</li>
            </ul>

            <h2>Matches on the official servers</h2>
            <p>The official servers report each finished match: the mode and map, and each player's name, team, kills, deaths,
              objectives and result. Your totals, medals and place on the <a href="/leaderboard/">leaderboard</a> are worked out
              from these reports and are public. Community and friends' servers report nothing.</p>

            <h2>Discord features</h2>
            <p>Rich Presence talks to the Discord app on your own computer and sends nothing to us. Inside a Discord Activity,
              Discord gives us your user id and name so you're signed in, and the voice channel's activity id so everyone in it
              joins one game.</p>

            <h2>Removing your data</h2>
            <ul>
              <li>Sign out on the game's Profile page or in the launcher. Your installation stops being linked.</li>
              <li><b>Delete account</b> on the game's Profile page deletes your account, your synced profile and Discord's token.
                It also deletes your match record: you leave every leaderboard, and your name and id are removed from old match results.</li>
              <li>You can also remove OpenIGI under Discord's User Settings, Authorized Apps.</li>
            </ul>
            <p>Questions go to the <a href="https://discord.gg/35E72ascqg">OpenIGI Discord</a>.</p>

            <footer><a href="/">Play OpenIGI</a> · <a href="/leaderboard/">Leaderboard</a></footer>
          </main>
        </body>
        </html>
        """;
}
