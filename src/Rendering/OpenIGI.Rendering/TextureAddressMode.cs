namespace OpenIGI.Rendering;

/// <summary>How texture coordinates outside the unit square are resolved.</summary>
public enum TextureAddressMode
{
    /// <summary>Coordinates wrap. The default for world surfaces.</summary>
    Repeat,

    /// <summary>Coordinates clamp to the edge texel. The default for sprites and glyph atlases, where wrapping bleeds neighbouring art.</summary>
    Clamp,
}
