using System.Globalization;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Formats.Textures;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// Turns any of the games' picture files into one picture to look at, for the content browser's preview and thumbnails
/// alike: textures, JPEG, TGA and bitmaps as they are, and sprites, pictures and fonts laid out as a sheet.
/// </summary>
internal static class ContentPictures
{
    /// <summary>The extensions shown as pictures.</summary>
    public static readonly HashSet<string> Extensions = new(StringComparer.Ordinal)
    {
        ".tex", ".tga", ".jpg", ".jpeg", ".spr", ".pic", ".fnt", ".bmp", ".png",
    };

    // What the sprite loader reads: the game's Sprite_Load takes all three.
    private static readonly HashSet<string> Sprites = new(StringComparer.Ordinal) { ".spr", ".pic", ".fnt" };

    /// <summary>Decodes a picture file.</summary>
    /// <param name="extension">The file's extension, lower case with its dot.</param>
    /// <param name="bytes">The file's bytes.</param>
    /// <param name="picture">Receives the picture.</param>
    /// <param name="facts">Receives what it is, for the preview's line of facts (<c>font, 96 glyphs</c>).</param>
    /// <returns>Whether it was a picture this reads.</returns>
    /// <exception cref="TextureFormatException">The bytes claim a format but are not valid in it.</exception>
    /// <exception cref="BitmapFormatException">A bitmap this reader cannot read.</exception>
    public static bool TryDecode(string extension, ReadOnlyMemory<byte> bytes, out TextureImage? picture, out string facts)
    {
        ReadOnlySpan<byte> span = bytes.Span;
        facts = string.Empty;
        picture = null;
        if (Sprites.Contains(extension) || (IlffContainer.IsIlff(span) && IlffContainer.Read(bytes).FormType == IlffTags.Font))
        {
            SpriteAsset sprite = SpriteLoader.Load(bytes, extension);
            picture = SpriteSheet.Compose(sprite);
            facts = sprite.IsFont
                ? string.Create(CultureInfo.InvariantCulture, $"font, {SpriteSheet.GlyphCount(sprite)} glyphs, {sprite.DefaultHeight} px high")
                : string.Create(CultureInfo.InvariantCulture, $"sprite, {sprite.Frames.Count} frame(s), {sprite.Textures.Count} texture(s)");
            return picture is not null;
        }

        if (span.Length > 2 && span[0] == (byte)'B' && span[1] == (byte)'M')
        {
            BitmapImage bitmap = BitmapImage.Read(span);
            picture = TextureImage.FromPixels(bitmap.Width, bitmap.Height, [.. bitmap.Pixels]);
            facts = "bitmap";
            return true;
        }

        return ImageSniffer.TryDecode(span, out picture) && picture is not null;
    }
}
