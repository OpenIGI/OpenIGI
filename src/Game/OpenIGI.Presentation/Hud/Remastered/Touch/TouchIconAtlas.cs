using System.Reflection;
using System.Text;
using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Rhi;

namespace OpenIGI.Presentation.Hud.Remastered.Touch;

/// <summary>
/// The touch controls' icons: one texture of square tiles, each a button's picture as coverage alone, drawn a quad apiece in
/// whatever colour the button is in.
/// </summary>
/// <remarks>
/// <para>
/// The pictures are SVGs (<c>assets/touch-icons</c>) baked into the tiles ahead of time by <c>tools/OpenIGI.Tools.IconAtlas</c>
/// and embedded in this assembly, so the game reads no SVG and tessellates no path: a button's picture costs one textured
/// quad, on a phone as on a desk.
/// </para>
/// <para>
/// Two textures are kept, the tiles as baked and the same at half the size, and a picture is drawn from the one nearer the
/// size it is drawn at: a small corner button minified from the large tiles would shimmer as it moved.
/// </para>
/// </remarks>
public sealed class TouchIconAtlas : IDisposable
{
    private const string Resource = "OpenIGI.Presentation.touch-icons.bin";

    private readonly RhiImageFactory _images;
    private readonly Dictionary<TouchButton, int> _tiles = [];
    private UiImage _full;
    private UiImage _half;
    private int _tile;
    private int _columns;
    private int _rows;
    private bool _loaded;

    /// <summary>Initializes a new instance of the <see cref="TouchIconAtlas"/> class.</summary>
    /// <param name="device">The device the tiles are uploaded to, at the first draw.</param>
    public TouchIconAtlas(IGraphicsDevice device)
    {
        _images = new RhiImageFactory(device ?? throw new ArgumentNullException(nameof(device)));
    }

    /// <summary>Draws a button's picture, centred, if the atlas has one for it.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="button">The button.</param>
    /// <param name="centre">The picture's centre, in pixels.</param>
    /// <param name="side">The side of the square it is drawn in, in pixels.</param>
    /// <param name="ink">Its colour.</param>
    /// <returns>Whether it was drawn; a button without a picture is left to its caller.</returns>
    public bool TryDraw(UiPainter painter, TouchButton button, UiVec2 centre, float side, Bgra32 ink)
    {
        ArgumentNullException.ThrowIfNull(painter);
        Load();
        if (!_tiles.TryGetValue(button, out int index) || !_full.IsValid)
        {
            return false;
        }

        // A texel in from each edge, so the tile beside this one never bleeds into it.
        bool small = side <= _tile * 0.6f && _half.IsValid;
        float texel = small ? 2f : 1f;
        float width = _columns * _tile, height = _rows * _tile;
        float x = (index % _columns) * _tile, y = (index / _columns) * _tile;
        UiRect source = new((x + texel) / width, (y + texel) / height, (_tile - (texel * 2f)) / width, (_tile - (texel * 2f)) / height);
        painter.DrawSprite(small ? _half : _full, new UiRect(centre.X - (side * 0.5f), centre.Y - (side * 0.5f), side, side), source, ink);
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _images.DestroyImage(_full);
        _images.DestroyImage(_half);
        _full = default;
        _half = default;
        _tiles.Clear();
        _loaded = false;
    }

    // Reads the baked tiles once and uploads them: "OITI", the tile's size, the count, each button's name, then each tile's
    // coverage, a byte a pixel.
    private void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource);
        if (stream is null)
        {
            return;
        }

        using BinaryReader reader = new(stream, Encoding.UTF8);
        if (!reader.ReadBytes(4).AsSpan().SequenceEqual("OITI"u8))
        {
            return;
        }

        _tile = reader.ReadUInt16();
        int count = reader.ReadUInt16();
        for (int i = 0; i < count; i++)
        {
            // A name this build has no button for is a tile nothing draws.
            if (Enum.TryParse(reader.ReadString(), out TouchButton button))
            {
                _tiles[button] = i;
            }
        }

        _columns = (int)MathF.Ceiling(MathF.Sqrt(count));
        _rows = (count + _columns - 1) / _columns;
        int width = _columns * _tile, height = _rows * _tile;
        byte[] coverage = reader.ReadBytes(count * _tile * _tile);
        if (coverage.Length != count * _tile * _tile)
        {
            _tiles.Clear();
            return;
        }

        // The pixels are made in methods of their own. The browser build runs this one in its interpreter, and a call
        // from there to a compiled method that returns a colour trapped with a signature mismatch: the game stopped
        // the first time a touch screen drew its buttons. Nothing here may call anything that returns a Bgra32.
        Bgra32[] pixels = Tiles(coverage, count, _tile, _columns, width, height);
        _full = _images.CreateImage(width, height, pixels);
        _half = _images.CreateImage(width / 2, height / 2, Halved(pixels, width, height));
    }

    // Each tile's coverage as white at that alpha, the tiles laid out in a grid.
    private static Bgra32[] Tiles(byte[] coverage, int count, int tile, int columns, int width, int height)
    {
        Bgra32[] pixels = new Bgra32[width * height];
        for (int i = 0; i < count; i++)
        {
            int left = (i % columns) * tile, top = (i / columns) * tile;
            for (int y = 0; y < tile; y++)
            {
                for (int x = 0; x < tile; x++)
                {
                    pixels[((top + y) * width) + left + x] = Bgra32.FromRgba(255, 255, 255, coverage[(((i * tile) + y) * tile) + x]);
                }
            }
        }

        return pixels;
    }

    // The same at half the size, each pixel the mean of the four it stands for.
    private static Bgra32[] Halved(Bgra32[] pixels, int width, int height)
    {
        int halfWidth = width / 2, halfHeight = height / 2;
        Bgra32[] half = new Bgra32[halfWidth * halfHeight];
        for (int y = 0; y < halfHeight; y++)
        {
            for (int x = 0; x < halfWidth; x++)
            {
                int at = (y * 2 * width) + (x * 2);
                int sum = pixels[at].A + pixels[at + 1].A + pixels[at + width].A + pixels[at + width + 1].A;
                half[(y * halfWidth) + x] = Bgra32.FromRgba(255, 255, 255, (byte)((sum + 2) / 4));
            }
        }

        return half;
    }
}
