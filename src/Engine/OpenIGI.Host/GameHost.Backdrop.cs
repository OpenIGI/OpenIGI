using OpenIGI.Rendering;
using OpenIGI.UI.Rhi;

namespace OpenIGI.Host;

/// <summary>The menu's frosted glass: the device's blurred frame, restated in the interface's units.</summary>
public sealed partial class GameHost
{
    private BackdropBlur? _backdropBlur;
    private RhiBackdrop? _backdropForUi;
    private int _backdropWidth;
    private int _backdropHeight;

    /// <summary>Gets the interface's view of the blurred frame, made again only when the device's levels or the window change.</summary>
    /// <param name="blur">This frame's blur, as the device returned it.</param>
    /// <returns>The backdrop the shell's renderer samples, or <see langword="null"/> when there is none.</returns>
    private RhiBackdrop? BackdropFor(BackdropBlur blur)
    {
        if (!ReferenceEquals(blur, _backdropBlur) || _backdropWidth != SafeWindowWidth || _backdropHeight != SafeWindowHeight)
        {
            _backdropBlur = blur;
            _backdropWidth = SafeWindowWidth;
            _backdropHeight = SafeWindowHeight;
            _backdropForUi = RhiBackdrop.From(blur, SafeWindowWidth, SafeWindowHeight);
        }

        return _backdropForUi;
    }
}
