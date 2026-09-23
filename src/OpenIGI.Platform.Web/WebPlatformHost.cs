namespace OpenIGI.Platform.Web;

/// <summary>The platform host for a game running in a browser page.</summary>
/// <remarks>
/// One window, the canvas, and no pacing of its own: the browser calls the page once per display
/// refresh, which is the pacing a desktop has to do for itself.
/// </remarks>
public sealed class WebPlatformHost : IPlatformHost
{
    private readonly IBrowserPage _page;

    /// <summary>Initialises a new instance of the <see cref="WebPlatformHost"/> class.</summary>
    /// <param name="page">The page the game runs in.</param>
    public WebPlatformHost(IBrowserPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _page = page;
    }

    /// <inheritdoc/>
    public string Name => "Web";

    /// <inheritdoc/>
    public IGameWindow CreateWindow(in WindowSettings settings) => new WebGameWindow(_page, settings);

    /// <inheritdoc/>
    public IFramePacer CreateFramePacer() => NoOpFramePacer.Instance;
}
