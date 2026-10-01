namespace OpenIGI.Formats.Sprites;

/// <summary>
/// Which of the container shapes accepted by the original's <c>Sprite_Load</c> a sprite came from.
/// </summary>
/// <remarks>
/// The loader (<c>0x4B6720</c>) recognises an ILFF <c>FONT</c> container and three <c>"LOOP"</c>
/// versions, and fatals on versions 5 and 10 with <c>"The font '%s' must be packed."</c>. See
/// the original.
/// </remarks>
public enum SpriteKind
{
    /// <summary>An ILFF <c>FONT</c> container: <c>FNTH</c>, <c>ANMF</c>, <c>TRAN</c> and texture chunks.</summary>
    Font,

    /// <summary><c>"LOOP"</c> version 1: a single frame with an inline header.</summary>
    Loop1,

    /// <summary><c>"LOOP"</c> version 7: multiple frames sharing one format, 40-byte frame records.</summary>
    Loop7,

    /// <summary><c>"LOOP"</c> version 9: multiple frames with per-frame formats, 32-byte frame records.</summary>
    Loop9,
}
