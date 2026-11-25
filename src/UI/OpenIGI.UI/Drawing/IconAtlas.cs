using System.Text;
using OpenIGI.Core.Graphics;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Drawing;

/// <summary>Icons a page draws by name, tinted as it draws them; a name it has no icon for is left to the page.</summary>
public interface IUiIcons
{
    /// <summary>Draws an icon, where there is one by that name.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="name">The icon's name.</param>
    /// <param name="centre">Its middle, in pixels.</param>
    /// <param name="side">Its side, in pixels.</param>
    /// <param name="ink">Its colour.</param>
    /// <returns>Whether there was one to draw.</returns>
    bool TryDraw(UiPainter painter, string name, UiVec2 centre, float side, Bgra32 ink);
}

/// <summary>
/// A set of icons baked ahead of time from SVGs (<c>tools/OpenIGI.Tools.IconAtlas</c>): one texture of square tiles, each an
/// icon's coverage alone, drawn a quad apiece in whatever colour it is drawn in. The game reads no SVG and tessellates no
/// path: an icon costs one textured quad, sharp at the size it is drawn at, on any renderer.
/// </summary>
/// <remarks>
/// The tiles are kept at their baked size and halved again and again down to <see cref="SmallestTile"/>, each level a texture
/// of its own, and an icon is drawn from the smallest level still as large as it is drawn: a small icon minified from the
/// large tiles would shimmer and alias.
/// </remarks>
public sealed class IconAtlas : IUiIcons, IDisposable
{
    /// <summary>The smallest tile a level is halved down to, in pixels.</summary>
    public const int SmallestTile = 16;

    private readonly IUiImageFactory _images;
    private readonly Dictionary<string, int> _tiles;
    private readonly List<(UiImage Image, int Tile)> _levels;
    private readonly int _columns;
    private readonly int _rows;

    private IconAtlas(IUiImageFactory images, Dictionary<string, int> tiles, List<(UiImage, int)> levels, int columns, int rows)
    {
        _images = images;
        _tiles = tiles;
        _levels = levels;
        _columns = columns;
        _rows = rows;
    }

    /// <summary>Gets the names of the icons it holds.</summary>
    public IReadOnlyCollection<string> Names => _tiles.Keys;

    /// <summary>
    /// Reads a baked atlas and uploads it: "OITI", the tile's size, the count, each icon's name, then each tile's coverage, a
    /// byte a pixel, row by row.
    /// </summary>
    /// <param name="stream">The baked atlas.</param>
    /// <param name="images">What the textures are made with: the renderer's.</param>
    /// <returns>The atlas, or null where the stream is not one.</returns>
    public static IconAtlas? Read(Stream stream, IUiImageFactory images)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(images);
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
        if (!reader.ReadBytes(4).AsSpan().SequenceEqual("OITI"u8))
        {
            return null;
        }

        int tile = reader.ReadUInt16();
        int count = reader.ReadUInt16();
        Dictionary<string, int> tiles = new(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            tiles[reader.ReadString()] = i;
        }

        int columns = (int)MathF.Ceiling(MathF.Sqrt(count));
        int rows = (count + columns - 1) / columns;
        byte[] coverage = reader.ReadBytes(count * tile * tile);
        if (count == 0 || coverage.Length != count * tile * tile)
        {
            return null;
        }

        // The pixels are made in methods of their own. The browser build runs this in its interpreter, and a call from there
        // to a compiled method that returns a colour trapped with a signature mismatch: nothing here may call anything that
        // returns a Bgra32.
        int width = columns * tile, height = rows * tile;
        Bgra32[] pixels = Tiles(coverage, count, tile, columns, width, height);
        List<(UiImage, int)> levels = [(images.CreateImage(width, height, pixels), tile)];
        for (int size = tile; size / 2 >= SmallestTile && size % 2 == 0; size /= 2)
        {
            pixels = Halved(pixels, width, height);
            width /= 2;
            height /= 2;
            levels.Add((images.CreateImage(width, height, pixels), size / 2));
        }

        return new IconAtlas(images, tiles, levels, columns, rows);
    }

    /// <inheritdoc/>
    public bool TryDraw(UiPainter painter, string name, UiVec2 centre, float side, Bgra32 ink)
    {
        ArgumentNullException.ThrowIfNull(painter);
        if (!_tiles.TryGetValue(name, out int index))
        {
            return false;
        }

        // The smallest level still as large as the icon is drawn.
        (UiImage image, int tile) = _levels[0];
        foreach ((UiImage levelImage, int levelTile) in _levels)
        {
            if (levelTile >= side * 0.9f)
            {
                (image, tile) = (levelImage, levelTile);
            }
        }

        // A texel in from each edge, so the tile beside this one never bleeds into it.
        const float Texel = 1f;
        float width = _columns * tile, height = _rows * tile;
        float x = (index % _columns) * tile, y = (index / _columns) * tile;
        UiRect source = new((x + Texel) / width, (y + Texel) / height, (tile - (Texel * 2f)) / width, (tile - (Texel * 2f)) / height);
        painter.DrawSprite(image, new UiRect(centre.X - (side * 0.5f), centre.Y - (side * 0.5f), side, side), source, ink);
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach ((UiImage image, _) in _levels)
        {
            _images.DestroyImage(image);
        }

        _levels.Clear();
        _tiles.Clear();
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
