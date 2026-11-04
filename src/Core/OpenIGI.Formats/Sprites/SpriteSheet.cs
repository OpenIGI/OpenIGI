using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Textures;

namespace OpenIGI.Formats.Sprites;

/// <summary>
/// Lays a sprite's frames out as one picture, for looking at it: every frame of a sprite side by side, or a font's sample
/// line over a grid of all its glyphs.
/// </summary>
/// <remarks>
/// A frame is drawn at its own size with its texture coordinates spread across it, as the game draws a sprite's quad, so
/// what is shown is what the game draws.
/// </remarks>
public static class SpriteSheet
{
    /// <summary>The line a font is shown writing.</summary>
    public const string SampleText = "The quick brown fox jumps over the lazy dog 0123456789";

    private const int Gap = 2;
    private const int GlyphColumns = 16;
    private const int MaximumEdge = 2048;
    private const int MaximumFrames = 400;

    /// <summary>Lays a sprite or font out as one picture.</summary>
    /// <param name="sprite">The sprite.</param>
    /// <returns>The picture, or null when no frame covers any pixels.</returns>
    public static TextureImage? Compose(SpriteAsset sprite)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        return sprite.IsFont ? ComposeFont(sprite) : sprite.TileLayout is { } tiles ? ComposeTiles(sprite, tiles) : ComposeFrames(sprite);
    }

    /// <summary>Gets how many glyphs a font has: the characters its map gives a frame.</summary>
    /// <param name="sprite">The font.</param>
    /// <returns>The count, or zero for a sprite that is not a font.</returns>
    public static int GlyphCount(SpriteAsset sprite)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        int count = 0;
        foreach (byte cell in sprite.CharacterMap)
        {
            if (cell > 0 && cell <= sprite.Frames.Count)
            {
                count++;
            }
        }

        return count;
    }

    // A big picture stored as tiles, put back together as the game's TileGrid_Load lays it out: one tile size, cell by cell.
    private static TextureImage? ComposeTiles(SpriteAsset sprite, SpriteTileLayout tiles)
    {
        int tileWidth = Math.Max(1, sprite.DefaultWidth);
        int tileHeight = Math.Max(1, sprite.DefaultHeight);
        Canvas canvas = new(Math.Min(MaximumEdge, tiles.Columns * tileWidth), Math.Min(MaximumEdge, tiles.Rows * tileHeight));
        bool any = false;
        for (int row = 0; row < tiles.Rows; row++)
        {
            for (int column = 0; column < tiles.Columns; column++)
            {
                int frame = tiles.GetFrameIndex(column, row);
                if (frame >= 0 && frame < sprite.Frames.Count && Size(sprite, sprite.Frames[frame]) is { Width: > 0, Height: > 0 })
                {
                    canvas.Blit(sprite, sprite.Frames[frame], column * tileWidth, row * tileHeight);
                    any = true;
                }
            }
        }

        return any ? canvas.ToImage() : null;
    }

    // Every frame in a grid as near square as it goes, each in a cell as big as the biggest.
    private static TextureImage? ComposeFrames(SpriteAsset sprite)
    {
        List<SpriteFrame> frames = [.. sprite.Frames.Where(f => Size(sprite, f) is { Width: > 0, Height: > 0 }).Take(MaximumFrames)];
        if (frames.Count == 0)
        {
            return null;
        }

        int cellWidth = frames.Max(f => Size(sprite, f).Width);
        int cellHeight = frames.Max(f => Size(sprite, f).Height);
        int columns = Math.Max(1, Math.Min(frames.Count, (int)Math.Ceiling(Math.Sqrt(frames.Count))));
        columns = Math.Max(1, Math.Min(columns, MaximumEdge / (cellWidth + Gap)));
        int rows = (frames.Count + columns - 1) / columns;
        Canvas canvas = new(Math.Min(MaximumEdge, (columns * (cellWidth + Gap)) - Gap), Math.Min(MaximumEdge, (rows * (cellHeight + Gap)) - Gap));
        for (int i = 0; i < frames.Count; i++)
        {
            canvas.Blit(sprite, frames[i], (i % columns) * (cellWidth + Gap), (i / columns) * (cellHeight + Gap));
        }

        return canvas.ToImage();
    }

    // The sample line, then every glyph from the space up, sixteen to a row.
    private static TextureImage? ComposeFont(SpriteAsset sprite)
    {
        List<(char Character, SpriteFrame Frame)> glyphs = [];
        for (int c = 32; c < 256; c++)
        {
            int cell = sprite.CharacterMap[c];
            if (cell > 0 && cell <= sprite.Frames.Count && Size(sprite, sprite.Frames[cell - 1]) is { Width: > 0, Height: > 0 })
            {
                glyphs.Add(((char)c, sprite.Frames[cell - 1]));
            }
        }

        if (glyphs.Count == 0)
        {
            return null;
        }

        int cellWidth = glyphs.Max(g => Size(sprite, g.Frame).Width) + (Gap * 2);
        int lineHeight = Math.Max(sprite.DefaultHeight, glyphs.Max(g => g.Frame.OffsetY + Size(sprite, g.Frame).Height));
        int cellHeight = glyphs.Max(g => Size(sprite, g.Frame).Height) + (Gap * 2);
        int sampleWidth = SampleText.Sum(c => Advance(sprite, c));
        int width = Math.Min(MaximumEdge, Math.Max(sampleWidth, GlyphColumns * cellWidth));
        int rows = (glyphs.Count + GlyphColumns - 1) / GlyphColumns;
        int gridTop = lineHeight + (Gap * 6);
        Canvas canvas = new(width, Math.Min(MaximumEdge, gridTop + (rows * cellHeight)));

        // The line as the game writes it: each glyph at the pen plus its bearing, the pen moved on by its advance.
        int pen = 0;
        foreach (char c in SampleText)
        {
            SpriteFrame glyph = sprite.GetGlyph(c);
            if (c != ' ' && Size(sprite, glyph) is { Width: > 0 })
            {
                canvas.Blit(sprite, glyph, pen + glyph.OffsetX, glyph.OffsetY);
            }

            pen += Advance(sprite, c);
        }

        for (int i = 0; i < glyphs.Count; i++)
        {
            canvas.Blit(sprite, glyphs[i].Frame, ((i % GlyphColumns) * cellWidth) + Gap, gridTop + ((i / GlyphColumns) * cellHeight) + Gap);
        }

        return canvas.ToImage();
    }

    private static int Advance(SpriteAsset sprite, char c)
    {
        SpriteFrame glyph = sprite.GetGlyph(c);
        return Math.Max(1, glyph.Advance > 0 ? glyph.Advance : sprite.DefaultAdvance > 0 ? sprite.DefaultAdvance : Size(sprite, glyph).Width);
    }

    // The size a frame is drawn at: its own, as the game draws its quad, or what its texture coordinates span when it has none.
    private static (int Width, int Height) Size(SpriteAsset sprite, SpriteFrame frame)
    {
        if (frame.TextureIndex < 0 || frame.TextureIndex >= sprite.Textures.Count)
        {
            return (0, 0);
        }

        if (frame.Width > 0 && frame.Height > 0)
        {
            return (Math.Min(frame.Width, MaximumEdge), Math.Min(frame.Height, MaximumEdge));
        }

        TextureImage texture = sprite.Textures[frame.TextureIndex];
        return ((int)MathF.Round(MathF.Abs(frame.U1 - frame.U0) * texture.Width), (int)MathF.Round(MathF.Abs(frame.V1 - frame.V0) * texture.Height));
    }

    private sealed class Canvas(int width, int height)
    {
        private readonly Bgra32[] _pixels = new Bgra32[Math.Max(1, width) * Math.Max(1, height)];

        // A frame copied at a point, sampled from its texture point by point (a flipped frame's coordinates run backwards).
        public void Blit(SpriteAsset sprite, SpriteFrame frame, int left, int top)
        {
            (int w, int h) = Size(sprite, frame);
            TextureImage texture = sprite.Textures[frame.TextureIndex];
            ReadOnlySpan<Bgra32> source = texture.Pixels;
            bool mapped = frame.U1 != frame.U0 && frame.V1 != frame.V0;
            for (int y = 0; y < h; y++)
            {
                int ty = top + y;
                if (ty < 0 || ty >= height)
                {
                    continue;
                }

                float v = mapped ? frame.V0 + ((frame.V1 - frame.V0) * ((y + 0.5f) / h)) : (y + 0.5f) / texture.Height;
                int sy = Math.Clamp((int)(v * texture.Height), 0, texture.Height - 1);
                for (int x = 0; x < w; x++)
                {
                    int tx = left + x;
                    if (tx < 0 || tx >= width)
                    {
                        continue;
                    }

                    float u = mapped ? frame.U0 + ((frame.U1 - frame.U0) * ((x + 0.5f) / w)) : (x + 0.5f) / texture.Width;
                    int sx = Math.Clamp((int)(u * texture.Width), 0, texture.Width - 1);
                    Bgra32 pixel = source[(sy * texture.Width) + sx];
                    if (pixel.A > 0)
                    {
                        _pixels[(ty * width) + tx] = pixel;
                    }
                }
            }
        }

        public TextureImage ToImage() => TextureImage.FromPixels(Math.Max(1, width), Math.Max(1, height), _pixels);
    }
}
