namespace OpenIGI.Formats.Sprites;

/// <summary>
/// One frame of a sprite, or one glyph of a font: a rectangle of a texture plus the metrics needed to
/// place it.
/// </summary>
/// <param name="TextureIndex">Which of the sprite's textures this frame samples.</param>
/// <param name="Width">The frame width in pixels.</param>
/// <param name="Height">The frame height in pixels.</param>
/// <param name="OffsetX">The horizontal placement offset in pixels. For a glyph, the left bearing.</param>
/// <param name="OffsetY">The vertical placement offset in pixels. For a glyph, the distance below the line top.</param>
/// <param name="Advance">How far the cursor moves after drawing this frame, in pixels.</param>
/// <param name="Flags">The frame's flag word, meaning not yet established.</param>
/// <param name="U0">The left texture coordinate.</param>
/// <param name="V0">The top texture coordinate.</param>
/// <param name="U1">The right texture coordinate.</param>
/// <param name="V1">The bottom texture coordinate.</param>
/// <remarks>
/// This is the original's 36-byte frame record, minus the texture pointer that a managed
/// <see cref="SpriteAsset"/> replaces with an index. Field order and meanings come from
/// <c>Sprite_SetFrame</c> (<c>0x4B6540</c>); see the original and §6.1.
/// </remarks>
public readonly record struct SpriteFrame(
    int TextureIndex,
    int Width,
    int Height,
    int OffsetX,
    int OffsetY,
    int Advance,
    uint Flags,
    float U0,
    float V0,
    float U1,
    float V1)
{
    /// <summary>Gets a value indicating whether the frame covers any pixels.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
}
