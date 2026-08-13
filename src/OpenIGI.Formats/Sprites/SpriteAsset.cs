using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Textures;

namespace OpenIGI.Formats.Sprites;

/// <summary>
/// A loaded sprite: one or more decoded textures, a frame table over them, and — for a font — the
/// character-to-glyph map.
/// </summary>
/// <remarks>
/// The original has one loader for sprites and fonts alike (<c>Sprite_Load</c>, <c>0x4B6720</c>) and
/// one object shape for both, so this type covers both too. A font is a sprite whose frames are glyphs
/// and which carries a <see cref="CharacterMap"/>; see the original.
/// </remarks>
public sealed class SpriteAsset
{
    private readonly TextureImage[] _textures;
    private readonly SpriteFrame[] _frames;
    private readonly byte[]? _characterMap;
    private readonly Bgra32[]? _palette;
    private readonly int _lineHeight;

    internal SpriteAsset(
        string name,
        SpriteKind kind,
        TextureImage[] textures,
        SpriteFrame[] frames,
        int defaultWidth,
        int defaultHeight,
        int defaultAdvance,
        uint flags,
        Bgra32[]? palette,
        byte[]? characterMap,
        SpriteTileLayout? tileLayout,
        int lineHeight = 0)
    {
        Name = name;
        Kind = kind;
        TileLayout = tileLayout;
        _textures = textures;
        _frames = frames;
        DefaultWidth = defaultWidth;
        DefaultHeight = defaultHeight;
        DefaultAdvance = defaultAdvance;
        _lineHeight = lineHeight;
        Flags = flags;
        _palette = palette;
        _characterMap = characterMap;

        // The original allocates frameCount + 1 records and fills the trailing one with the
        // sprite-wide defaults. Its texture coordinates stay zeroed, so it covers no texels: drawing
        // it advances the cursor and paints nothing. Fonts rely on that — see DefaultFrame.
        DefaultFrame = new SpriteFrame(
            TextureIndex: 0,
            Width: defaultWidth,
            Height: defaultHeight,
            OffsetX: 0,
            OffsetY: 0,
            Advance: defaultAdvance,
            Flags: flags,
            U0: 0.0f,
            V0: 0.0f,
            U1: 0.0f,
            V1: 0.0f);
    }

    /// <summary>Gets the resource name the sprite was loaded from, for diagnostics.</summary>
    public string Name { get; }

    /// <summary>Gets which of the original's container shapes this sprite came from.</summary>
    public SpriteKind Kind { get; }

    /// <summary>Gets the decoded textures the frames sample.</summary>
    public IReadOnlyList<TextureImage> Textures => _textures;

    /// <summary>Gets the frame table. For a font these are the glyphs, in glyph-index order.</summary>
    public IReadOnlyList<SpriteFrame> Frames => _frames;

    /// <summary>Gets the sprite-wide default width in pixels.</summary>
    /// <remarks>
    /// The original stores this in a trailing entry one past the last frame rather than in the sprite
    /// header; it is a default, not a frame.
    /// </remarks>
    public int DefaultWidth { get; }

    /// <summary>Gets the sprite-wide default height in pixels.</summary>
    public int DefaultHeight { get; }

    /// <summary>Gets the sprite-wide default advance in pixels. For a font, the fallback character advance.</summary>
    public int DefaultAdvance { get; }

    /// <summary>
    /// Gets the font's line height in pixels: the vertical step between lines of text. Read from the
    /// FNTH chunk's third dword (<c>+8</c>).
    /// </summary>
    /// <remarks>
    /// The retail game stores all six FNTH dwords in its 24-byte font object, and the reference
    /// decoder (<c>fnt_parser.cpp</c>) reads <c>+8</c> as the line height and uses it for line
    /// stepping. This is distinct from <see cref="DefaultHeight"/> (<c>+20</c>), which is the glyph
    /// face size: for <c>font1.fnt</c> that is 18 while the line height is 32. Non-font sprites have
    /// no line-height field, so this falls back to <see cref="DefaultHeight"/> to stay safe.
    /// </remarks>
    public int LineHeight => _lineHeight > 0 ? _lineHeight : DefaultHeight;

    /// <summary>Gets the sprite's flag word.</summary>
    public uint Flags { get; }

    /// <summary>
    /// Gets the trailing defaults record the original allocates one past the last frame.
    /// </summary>
    /// <remarks>
    /// It carries the sprite-wide width, height and advance with zeroed texture coordinates, so it
    /// draws nothing. Fonts use it as the missing-glyph cell: characters whose map entry is zero, and
    /// the trailing entries of a font whose map names more glyphs than it has, land here rather than
    /// out of bounds.
    /// </remarks>
    public SpriteFrame DefaultFrame { get; }

    /// <summary>Gets the palette, or an empty span when the sprite is not paletted.</summary>
    public ReadOnlySpan<Bgra32> Palette => _palette;

    /// <summary>Gets the 256-entry character-to-glyph map, or an empty span when this is not a font.</summary>
    /// <remarks>Entries are <b>one-based</b> glyph numbers; zero means "no glyph". See <see cref="GetGlyph"/>.</remarks>
    public ReadOnlySpan<byte> CharacterMap => _characterMap;

    /// <summary>
    /// Gets the tile grid that reassembles this sprite's frames into one picture, or
    /// <see langword="null"/> when the sprite is not tiled.
    /// </summary>
    public SpriteTileLayout? TileLayout { get; }

    /// <summary>Gets a value indicating whether this sprite carries a character map and can render text.</summary>
    public bool IsFont => _characterMap is not null;

    /// <summary>Resolves a character to its glyph.</summary>
    /// <param name="character">The character. Only the low byte is significant, as in the original.</param>
    /// <returns>The glyph frame, or <see cref="DefaultFrame"/> when the character has no glyph.</returns>
    /// <exception cref="InvalidOperationException">This sprite is not a font.</exception>
    /// <remarks>
    /// <para>
    /// The original stores the map as a 256-cell grid and looks the character up by its low byte, so
    /// codes above 255 fold rather than fail.
    /// </para>
    /// <para>
    /// <b>Stored values are one-based</b>, matching the tile grids in <see cref="SpriteTileLayout"/>
    /// which use the identical cell table: cell <c>n</c> means frame <c>n - 1</c>, and zero means "no
    /// glyph". Reading them as zero-based renders every string shifted by one character — which is
    /// exactly what it looks like on screen, and how this was caught.
    /// </para>
    /// </remarks>
    public SpriteFrame GetGlyph(char character)
    {
        if (_characterMap is null)
        {
            throw new InvalidOperationException($"Sprite '{Name}' is not a font and has no character map.");
        }

        int glyph = _characterMap[(byte)character] - 1;
        return glyph >= 0 && glyph < _frames.Length ? _frames[glyph] : DefaultFrame;
    }

    /// <summary>Gets the texture a frame samples.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns>The texture image.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The frame's texture index is out of range.</exception>
    public TextureImage GetTexture(in SpriteFrame frame)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frame.TextureIndex, nameof(frame));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frame.TextureIndex, _textures.Length, nameof(frame));
        return _textures[frame.TextureIndex];
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Kind} '{Name}' frames={_frames.Length} textures={_textures.Length}{(IsFont ? " font" : string.Empty)}";
}
