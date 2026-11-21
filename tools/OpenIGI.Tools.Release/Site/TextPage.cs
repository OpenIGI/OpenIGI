namespace OpenIGI.Tools.Release;

/// <summary>
/// The site's plain reading pages (<c>/privacy/</c>, <c>/code-signing/</c>): one column of text in the site's look
/// (<see cref="SiteChrome"/>), under its bar and over its foot. Each page supplies only its title, summary and body.
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
          <link rel="stylesheet" href="/landing/site.css">
          <style>
            main { width: min(760px, 100%); margin: 0 auto; padding: clamp(40px, 6vw, 72px) clamp(16px, 3vw, 32px) 64px; }
            main .mono { color: var(--accent); }
            h1 { margin: 10px 0 24px; font-size: clamp(40px, 6vw, 64px); line-height: 0.95; font-weight: 700; font-stretch: 78%; letter-spacing: -0.015em; }
            h2 { margin: 36px 0 10px; font-size: 21px; font-stretch: 90%; }
            p, li { font-size: 16px; line-height: 1.65; color: var(--soft); }
            ul { padding-left: 20px; }
            main a { color: var(--accent); }
            code { font-family: var(--mono); font-size: 14px; overflow-wrap: anywhere; }
          </style>
        </head>
        <body>
        {{SiteChrome.Nav(null)}}
          <main>
            <span class="mono">OpenIGI</span>
            <h1>{{title}}</h1>
        {{body}}
          </main>
        {{SiteChrome.Footer}}
        </body>
        </html>
        """;
}
