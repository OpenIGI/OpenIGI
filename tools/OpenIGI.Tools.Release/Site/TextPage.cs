namespace OpenIGI.Tools.Release;

/// <summary>
/// The site's plain reading pages (<c>/privacy/</c>, <c>/code-signing/</c>): one column of text in the landing page's
/// type and colours, with a footer back to the game. Each page supplies only its title, summary and body.
/// </summary>
internal static class TextPage
{
    /// <summary>A whole page around <paramref name="body"/>, the HTML that goes under the page's heading.</summary>
    /// <param name="title">The heading, also the tab's title before " · OpenIGI".</param>
    /// <param name="description">The one-line summary search engines and link previews show.</param>
    /// <param name="body">The page's sections.</param>
    public static string Html(string title, string description, string body) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
          <meta name="color-scheme" content="dark">
          <meta name="theme-color" content="#0b0c0b">
          <title>{{title}} · OpenIGI</title>
          <meta name="description" content="{{description}}">
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
            code { font-family: var(--mono); font-size: 14px; overflow-wrap: anywhere; }
            footer { margin-top: 40px; padding-top: 16px; border-top: 1px solid var(--line); }
          </style>
        </head>
        <body>
          <main>
            <span class="mono">OpenIGI</span>
            <h1>{{title}}</h1>
        {{body}}
            <footer><a href="/">Play OpenIGI</a> · <a href="/leaderboard/">Leaderboard</a> · <a href="/privacy/">Privacy</a> · <a href="/code-signing/">Code signing</a></footer>
          </main>
        </body>
        </html>
        """;
}
