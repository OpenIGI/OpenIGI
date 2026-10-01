using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Drawing;

/// <summary>A uniform scale followed by a translation: <c>p' = p × Scale + Offset</c>.</summary>
/// <remarks>
/// The only transform the painter applies, because it is the only one that keeps an axis-aligned
/// rectangle axis-aligned — so clipping, text and every fast rectangle path stay exact under it. It is
/// what an animation needs to grow a backdrop from 110% to 100% or slide a block of text in; anything
/// that has to rotate (a chevron, a diamond) is built as a polygon instead.
/// </remarks>
/// <param name="Scale">The uniform scale factor.</param>
/// <param name="Offset">The translation applied after scaling.</param>
public readonly record struct UiTransform(float Scale, UiVec2 Offset)
{
    /// <summary>Gets the identity transform.</summary>
    public static UiTransform Identity => new(1f, default);

    /// <summary>Gets a value indicating whether this transform changes nothing.</summary>
    public bool IsIdentity => Scale == 1f && Offset.X == 0f && Offset.Y == 0f;

    /// <summary>Creates a translation.</summary>
    /// <param name="dx">The horizontal offset.</param>
    /// <param name="dy">The vertical offset.</param>
    /// <returns>The transform.</returns>
    public static UiTransform Translate(float dx, float dy) => new(1f, new UiVec2(dx, dy));

    /// <summary>Creates a uniform scale about a fixed point.</summary>
    /// <param name="scale">The scale factor.</param>
    /// <param name="pivot">The point that stays where it is.</param>
    /// <returns>The transform.</returns>
    public static UiTransform ScaleAbout(float scale, UiVec2 pivot) =>
        new(scale, new UiVec2(pivot.X - (pivot.X * scale), pivot.Y - (pivot.Y * scale)));

    /// <summary>Maps a point through the transform.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The transformed point.</returns>
    public UiVec2 Apply(UiVec2 point) => new((point.X * Scale) + Offset.X, (point.Y * Scale) + Offset.Y);

    /// <summary>Maps a rectangle through the transform.</summary>
    /// <param name="rect">The rectangle.</param>
    /// <returns>The transformed rectangle.</returns>
    public UiRect Apply(UiRect rect) =>
        new((rect.X * Scale) + Offset.X, (rect.Y * Scale) + Offset.Y, rect.Width * Scale, rect.Height * Scale);

    /// <summary>Returns the transform that applies <paramref name="inner"/> first and then this one.</summary>
    /// <param name="inner">The transform applied first.</param>
    /// <returns>The composed transform.</returns>
    public UiTransform Then(UiTransform inner) =>
        new(Scale * inner.Scale, new UiVec2((inner.Offset.X * Scale) + Offset.X, (inner.Offset.Y * Scale) + Offset.Y));
}
