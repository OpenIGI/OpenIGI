using System.Reflection;

namespace OpenIGI.Tools.Release;

/// <summary>
/// What every page the site build writes stands in: the bar along the top and the foot, drawn by the site's one stylesheet
/// (<c>/landing/site.css</c>), and that stylesheet and the standings' drawing (<c>/landing/board.js</c>) themselves. The two
/// files are the browser build's own (apps/OpenIGI.Web/wwwroot/landing), carried here so a page the site build writes never
/// waits for the next browser build to be styled. The home page, which is the browser build's, draws the same bar itself.
/// </summary>
internal static class SiteChrome
{
    /// <summary>The bar's link to the leaderboard, for the page it marks as the one shown.</summary>
    public const string Leaderboard = "leaderboard";

    /// <summary>The shared files, by the path they are served at, each with the name it is embedded under.</summary>
    public static IReadOnlyList<(string Path, string Resource)> Files { get; } =
    [
        ("landing/site.css", "OpenIGI.Site.site.css"),
        ("landing/board.js", "OpenIGI.Site.board.js"),
    ];

    /// <summary>The foot of a page.</summary>
    public const string Footer = """
          <footer class="site-footer">
            <div class="wrap">
              <span>An independent reimplementation, not affiliated with the game's publishers.</span>
              <span><a href="/">Home</a> · <a href="/leaderboard/">Leaderboard</a> · <a href="/privacy/">Privacy</a> · <a href="/code-signing/">Code signing</a></span>
            </div>
          </footer>
        """;

    /// <summary>The bar along the top of a page.</summary>
    /// <param name="current">Which of its links is the page shown (<see cref="Leaderboard"/>), or <see langword="null"/> for none.</param>
    /// <returns>Its HTML.</returns>
    public static string Nav(string? current) => $$"""
          <nav class="nav" aria-label="Site">
            <div class="wrap">
              <a class="brand" href="/" aria-label="OpenIGI home">
                <svg viewBox="0 0 32 32" aria-hidden="true"><circle cx="16" cy="16" r="10" fill="none" stroke="currentColor" stroke-width="2.4"/><circle cx="16" cy="16" r="2.8" fill="currentColor"/><path d="M16 1v7M16 24v7M1 16h7M24 16h7" stroke="currentColor" stroke-width="2.4"/></svg>
                <span>OpenIGI</span>
              </a>
              <span class="nav-links">
                <a href="/#play-now">Play</a>
                <a href="/leaderboard/"{{(current == Leaderboard ? " aria-current=\"page\"" : string.Empty)}}>Leaderboard</a>
                <a class="wide" href="/#desktop">Download</a>
                <a class="wide" href="/#about">About</a>
              </span>
              <span class="nav-right">
                <a class="social" href="https://discord.gg/35E72ascqg" target="_blank" rel="noopener" aria-label="OpenIGI on Discord"><svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M20.3 4.4A19.6 19.6 0 0 0 15.4 3l-.6 1.3a18.2 18.2 0 0 0-5.6 0L8.6 3a19.6 19.6 0 0 0-4.9 1.5C.6 9.1-.3 13.7.1 18.2a19.8 19.8 0 0 0 6 3l1.3-2.1a12.8 12.8 0 0 1-2-1l.5-.4a14.1 14.1 0 0 0 12.2 0l.5.4a12.8 12.8 0 0 1-2 1l1.3 2.1a19.7 19.7 0 0 0 6-3c.5-5.2-.9-9.7-3.7-13.8ZM8.3 15.5c-1.2 0-2.2-1.1-2.2-2.4s1-2.4 2.2-2.4 2.2 1.1 2.2 2.4-1 2.4-2.2 2.4Zm7.4 0c-1.2 0-2.2-1.1-2.2-2.4s1-2.4 2.2-2.4 2.2 1.1 2.2 2.4-1 2.4-2.2 2.4Z"/></svg></a>
                <a class="social" href="https://github.com/OpenIGI/OpenIGI" target="_blank" rel="noopener" aria-label="OpenIGI on GitHub"><svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M12 .5a11.5 11.5 0 0 0-3.6 22.4c.6.1.8-.3.8-.6v-2c-3.2.7-3.9-1.5-3.9-1.5-.5-1.3-1.3-1.7-1.3-1.7-1-.7.1-.7.1-.7 1.2.1 1.8 1.2 1.8 1.2 1 1.8 2.8 1.3 3.4 1 .1-.8.4-1.3.8-1.6-2.6-.3-5.3-1.3-5.3-5.7 0-1.3.5-2.3 1.2-3.1-.1-.3-.5-1.5.1-3.1 0 0 1-.3 3.2 1.2a11 11 0 0 1 5.8 0c2.2-1.5 3.2-1.2 3.2-1.2.6 1.6.2 2.8.1 3.1.8.8 1.2 1.8 1.2 3.1 0 4.4-2.7 5.4-5.3 5.7.4.4.8 1.1.8 2.2v3.2c0 .3.2.7.8.6A11.5 11.5 0 0 0 12 .5Z"/></svg></a>
              </span>
            </div>
          </nav>
        """;

    /// <summary>A shared file's text, as embedded.</summary>
    /// <param name="resource">The name it is embedded under.</param>
    /// <returns>Its text.</returns>
    public static string Read(string resource)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The site build carries no {resource}.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
