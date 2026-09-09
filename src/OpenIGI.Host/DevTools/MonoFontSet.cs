using OpenIGI.Rendering;
using OpenIGI.UI.Text;

namespace OpenIGI.Desktop;

/// <summary>
/// The developer tools' fixed-pitch faces, rasterised once per pixel size actually drawn.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a set rather than one font.</b> A coverage atlas is a bitmap: drawn at the size it was
/// rasterised at, each texel lands on one pixel and the text is as crisp as the rasteriser made it.
/// Drawn at any other size it is resampled through a linear filter, and minification in particular
/// turns stems grey and closes counters — a 32-pixel atlas drawn at 13 is being shrunk to 40%, which
/// is enough to make a capital L read as an I and an E as an F.
/// </para>
/// <para>
/// So each size the interface asks for gets its own atlas at exactly that height, and every draw is
/// one-to-one. The cost is bounded and small: the tools use three or four sizes, each is a few
/// hundred kilobytes, and <see cref="FontAtlasCache"/> keys on the pixel height so every one of them
/// is rasterised once ever and read from disk afterwards.
/// </para>
/// <para>
/// Sizes are rounded to whole pixels before being asked for, both because a fractional atlas height
/// would defeat the caching and because half-pixel text is the other half of why small type looks
/// soft.
/// </para>
/// </remarks>
public sealed class MonoFontSet : IDisposable
{
    /// <summary>The smallest atlas built, below which text is unreadable anyway.</summary>
    private const int MinimumSize = 8;

    /// <summary>The largest atlas built, which covers <c>con_scale 3</c> at the largest text size.</summary>
    private const int MaximumSize = 72;

    private readonly IGraphicsDevice _device;
    private readonly string _fileName;
    private readonly Dictionary<int, UiFont> _bySize = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="MonoFontSet"/> class.</summary>
    /// <param name="device">The device atlases are uploaded to.</param>
    /// <param name="fileName">The font file in the shipped fonts directory.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public MonoFontSet(IGraphicsDevice device, string fileName)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        _device = device;
        _fileName = fileName;
    }

    /// <summary>Gets how many distinct sizes have been rasterised.</summary>
    public int Count => _bySize.Count;

    /// <summary>Rounds a wanted size to one an atlas can be built for.</summary>
    /// <param name="sizePx">The wanted size in pixels.</param>
    /// <returns>The size that will actually be used.</returns>
    public static int Round(float sizePx) =>
        Math.Clamp((int)MathF.Round(sizePx), MinimumSize, MaximumSize);

    /// <summary>Gets the face rasterised at a size, building it if this is the first request.</summary>
    /// <param name="sizePx">The wanted size in pixels; rounded by <see cref="Round"/>.</param>
    /// <returns>A font whose base height is the returned size, so it draws one-to-one.</returns>
    /// <exception cref="ObjectDisposedException">The set has been disposed.</exception>
    public UiFont At(float sizePx)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int size = Round(sizePx);
        if (_bySize.TryGetValue(size, out UiFont? existing))
        {
            return existing;
        }

        UiFont built = UiFontLibrary.Load(_device, _fileName, size);
        _bySize[size] = built;
        return built;
    }

    /// <summary>Releases every rasterised size.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (UiFont font in _bySize.Values)
        {
            font.Dispose();
        }

        _bySize.Clear();
    }
}
