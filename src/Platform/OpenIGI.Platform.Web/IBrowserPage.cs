namespace OpenIGI.Platform.Web;

/// <summary>The page a browser game runs in, as its window sees it.</summary>
/// <remarks>
/// <para>
/// The window's whole view of the browser. What the page reports arrives as events the page queued since
/// the last frame -- a browser delivers input to its own handlers, between frames, never to a poll -- and
/// what the window asks of it are a few requests the page may or may not grant: a pointer lock and
/// fullscreen are both refused outside a click.
/// </para>
/// <para>
/// An interface so the window's reading of those events runs and is tested off a browser; the one
/// implementation that talks to a page is <see cref="BrowserPage"/>.
/// </para>
/// </remarks>
public interface IBrowserPage
{
    /// <summary>Gets the size the canvas is shown at, in device pixels.</summary>
    (int Width, int Height) CanvasSize { get; }

    /// <summary>Gets whether the page has the keyboard.</summary>
    bool HasFocus { get; }

    /// <summary>Gets whether the page is shown fullscreen.</summary>
    bool IsFullscreen { get; }

    /// <summary>Takes the events the page has queued since it was last asked.</summary>
    /// <returns>The events, oldest first, in the form <see cref="WebInputEvent.Parse"/> reads.</returns>
    string[] TakeEvents();

    /// <summary>Sets the page's title.</summary>
    /// <param name="title">The title.</param>
    void SetTitle(string title);

    /// <summary>Says how the pointer should be over the canvas.</summary>
    /// <param name="visible">Whether it is drawn.</param>
    /// <param name="locked">
    /// Whether it is locked to the canvas, reporting motion rather than a position. The page asks for the
    /// lock now and again on the next click when the browser refuses it.
    /// </param>
    void SetPointer(bool visible, bool locked);

    /// <summary>Asks for the canvas to be shown fullscreen, or not.</summary>
    /// <param name="fullscreen">Whether it should be.</param>
    void RequestFullscreen(bool fullscreen);

    /// <summary>Puts text on the system clipboard, where the browser allows it.</summary>
    /// <param name="text">The text.</param>
    void WriteClipboard(string text);

    /// <summary>Says whether the page should report the device's turning, as <c>gyro</c> events.</summary>
    /// <param name="wanted">Whether it should. Where the browser asks the player's leave, it is asked on the next tap.</param>
    void SetGyro(bool wanted)
    {
    }
}
