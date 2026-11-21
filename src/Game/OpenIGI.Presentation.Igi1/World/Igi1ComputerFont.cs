using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>
/// Project I.G.I.'s font for the remastered computer: the status display's own (<see cref="StatusScreenRenderer.FontResource"/>),
/// from the computer's archive, read on its first use.
/// </summary>
/// <param name="device">The device the font is uploaded to.</param>
/// <param name="files">The installation's data.</param>
/// <param name="logger">Where a font that cannot be read is told.</param>
internal sealed class Igi1ComputerFont(IGraphicsDevice device, VirtualFileSystem files, IOpenIgiLogger logger) : IComputerFontSource
{
    private readonly RetailUiResources _resources = new(device, new RetailUiResourceOptions { LogCategory = "Computer" }, logger);
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
                // The mission's own copy where it has one, as the status display reads it.
                string? archive = files.Exists(StatusScreenRenderer.LocalFontArchive) ? StatusScreenRenderer.LocalFontArchive
                    : files.Exists(StatusScreenRenderer.FontArchive) ? StatusScreenRenderer.FontArchive
                    : null;
                if (archive is not null)
                {
                    _resources.SetArchives(files.ReadArchive(archive));
                    _font = _resources.Font(StatusScreenRenderer.FontResource);
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
    public void Dispose() => _resources.Dispose();
}
