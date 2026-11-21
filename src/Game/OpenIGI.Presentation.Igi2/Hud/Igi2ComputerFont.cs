using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Presentation.Igi2.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi2.Hud;

/// <summary>
/// I.G.I. 2's font for the remastered computer: the status display's own, <c>font1.fnt</c>, read on its first use.
/// </summary>
/// <param name="device">The device the font is uploaded to.</param>
/// <param name="files">The installation's data.</param>
/// <param name="logger">Where a font that cannot be read is told.</param>
internal sealed class Igi2ComputerFont(IGraphicsDevice device, VirtualFileSystem files, IOpenIgiLogger logger) : IComputerFontSource
{
    /// <summary>The status display's font, as <see cref="Igi2HudRenderer"/> loads it.</summary>
    public const string FontResource = "font1.fnt";

    private Igi2SpriteResources? _assets;
    private BitmapTextRenderer? _font;
    private bool _opened;

    /// <inheritdoc/>
    public BitmapTextRenderer? Font
    {
        get
        {
            if (_opened)
            {
                return _font;
            }

            _opened = true;
            try
            {
                _assets = new Igi2SpriteResources(device, files, logger);
                if (_assets.Get(FontResource) is { IsFont: true } font)
                {
                    _font = new BitmapTextRenderer(font);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                logger.Warning("Computer", $"Could not read the computer's font: {ex.Message}");
            }

            return _font;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _assets?.Dispose();
}
