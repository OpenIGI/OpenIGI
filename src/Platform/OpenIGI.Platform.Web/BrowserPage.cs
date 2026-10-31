using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace OpenIGI.Platform.Web;

/// <summary>The page, through the functions platform.js gives the runtime.</summary>
/// <remarks>
/// The page registers platform.js's imports under <see cref="ModuleName"/> before the game starts
/// (<c>runtime.setModuleImports</c>), so nothing here loads a module or waits for one.
/// </remarks>
[SupportedOSPlatform("browser")]
public sealed partial class BrowserPage : IBrowserPage
{
    /// <summary>The name the page registers platform.js's imports under.</summary>
    public const string ModuleName = "openigi-platform";

    /// <inheritdoc/>
    public (int Width, int Height) CanvasSize => (CanvasWidth(), CanvasHeight());

    /// <inheritdoc/>
    public bool HasFocus => PageHasFocus();

    /// <inheritdoc/>
    public bool IsFullscreen => PageIsFullscreen();

    /// <inheritdoc/>
    public string[] TakeEvents() => TakePageEvents();

    /// <inheritdoc/>
    public void SetTitle(string title) => SetPageTitle(title ?? string.Empty);

    /// <inheritdoc/>
    public void SetPointer(bool visible, bool locked) => SetPagePointer(visible, locked);

    /// <inheritdoc/>
    public void RequestFullscreen(bool fullscreen) => RequestPageFullscreen(fullscreen);

    /// <inheritdoc/>
    public void WriteClipboard(string text) => WritePageClipboard(text ?? string.Empty);

    /// <inheritdoc/>
    public void SetGyro(bool wanted) => SetPageGyro(wanted);

    [JSImport("canvasWidth", ModuleName)]
    private static partial int CanvasWidth();

    [JSImport("canvasHeight", ModuleName)]
    private static partial int CanvasHeight();

    [JSImport("hasFocus", ModuleName)]
    private static partial bool PageHasFocus();

    [JSImport("isFullscreen", ModuleName)]
    private static partial bool PageIsFullscreen();

    [JSImport("takeEvents", ModuleName)]
    private static partial string[] TakePageEvents();

    [JSImport("setTitle", ModuleName)]
    private static partial void SetPageTitle(string title);

    [JSImport("setPointer", ModuleName)]
    private static partial void SetPagePointer(bool visible, bool locked);

    [JSImport("requestFullscreen", ModuleName)]
    private static partial void RequestPageFullscreen(bool fullscreen);

    [JSImport("writeClipboard", ModuleName)]
    private static partial void WritePageClipboard(string text);

    [JSImport("setGyro", ModuleName)]
    private static partial void SetPageGyro(bool wanted);
}
