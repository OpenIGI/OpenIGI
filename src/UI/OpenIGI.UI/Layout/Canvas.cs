namespace OpenIGI.UI.Layout;

/// <summary>
/// Where an anchored child sits within its parent, as a normalised sub-rectangle inset by a margin.
/// </summary>
/// <remarks>
/// <see cref="Min"/> and <see cref="Max"/> are fractions of the parent, so <c>(0,0)–(1,1)</c> is the
/// whole parent, <c>(0,0)–(1,0.5)</c> the top half, and <c>(1,1)–(1,1)</c> the bottom-right corner. The
/// child is arranged into that region inset by <see cref="Margin"/>; its own width, height and alignment
/// then place it inside — so a full-bleed background stretches to <c>(0,0)–(1,1)</c>, while a fixed-size
/// logo anchored to the same region but aligned centre/centre sits in the middle at any aspect ratio.
/// </remarks>
/// <param name="Min">The top-left corner as a fraction of the parent.</param>
/// <param name="Max">The bottom-right corner as a fraction of the parent.</param>
/// <param name="Margin">An inset applied to the anchored region, in design units.</param>
public readonly record struct Anchor(UiVec2 Min, UiVec2 Max, Thickness Margin = default)
{
    /// <summary>Gets the anchor that fills the whole parent.</summary>
    public static Anchor Fill => new(UiVec2.Zero, UiVec2.One);

    /// <summary>Creates an anchor filling the parent inset on every side.</summary>
    /// <param name="inset">The inset in design units.</param>
    /// <returns>The anchor.</returns>
    public static Anchor Filled(float inset) => new(UiVec2.Zero, UiVec2.One, new Thickness(inset));

    /// <summary>Creates a point anchor at a fraction of the parent, for a child sized by itself.</summary>
    /// <param name="x">The horizontal fraction.</param>
    /// <param name="y">The vertical fraction.</param>
    /// <returns>The anchor.</returns>
    public static Anchor Point(float x, float y) => new(new UiVec2(x, y), new UiVec2(x, y));
}

/// <summary>
/// Places each child by an <see cref="Anchor"/> — a normalised region of the canvas the child is
/// arranged into and positioned within by its own size and alignment.
/// </summary>
/// <remarks>
/// The absolute-positioning container, and the one that makes a screen correct across aspect ratios:
/// anchors resolve against the canvas's real pixel bounds, so a background anchored to fill covers a
/// 32:9 monitor while a centred menu column anchored to the middle stays centred. Children with no
/// registered anchor fill the canvas.
/// </remarks>
public class Canvas : UiElement
{
    private readonly Dictionary<UiElement, Anchor> _anchors = [];

    /// <summary>Adds a child at an anchor.</summary>
    /// <param name="child">The child to add.</param>
    /// <param name="anchor">Where the child is anchored; defaults to filling the canvas.</param>
    /// <returns>The child, so calls can be chained.</returns>
    public UiElement Add(UiElement child, Anchor? anchor = null)
    {
        AddChild(child);
        _anchors[child] = anchor ?? Anchor.Fill;
        return child;
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        // A canvas takes the space offered; children are positioned relative to it rather than sizing it. Each is measured
        // against the room its anchor gives it, so text that wraps wraps to the width it is placed in: a page beside a
        // phone's rail is the screen less the rail, not the whole screen.
        foreach (UiElement child in Children)
        {
            if (child.Visibility != Visibility.Collapsed)
            {
                Anchor anchor = _anchors.TryGetValue(child, out Anchor a) ? a : Anchor.Fill;
                child.Measure(context, Room(context, anchor, available));
            }
        }

        return available;
    }

    // The size an anchor spans of the space offered. An anchor that spans nothing along an axis (a point, which the child
    // sizes for itself) and an axis offered without bound both measure against what is offered, as before.
    private static UiVec2 Room(LayoutContext context, Anchor anchor, UiVec2 available)
    {
        Thickness margin = context.Px(anchor.Margin);
        return new UiVec2(
            Span(available.X, anchor.Min.X, anchor.Max.X, margin.Left + margin.Right),
            Span(available.Y, anchor.Min.Y, anchor.Max.Y, margin.Top + margin.Bottom));

        static float Span(float offered, float min, float max, float margins)
        {
            if (float.IsInfinity(offered) || max <= min)
            {
                return offered;
            }

            float span = ((max - min) * offered) - margins;
            return span > 0f ? span : offered;
        }
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        foreach (UiElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            Anchor anchor = _anchors.TryGetValue(child, out Anchor a) ? a : Anchor.Fill;
            Thickness margin = context.Px(anchor.Margin);

            float x0 = contentRect.X + (anchor.Min.X * contentRect.Width) + margin.Left;
            float x1 = contentRect.X + (anchor.Max.X * contentRect.Width) - margin.Right;
            float y0 = contentRect.Y + (anchor.Min.Y * contentRect.Height) + margin.Top;
            float y1 = contentRect.Y + (anchor.Max.Y * contentRect.Height) - margin.Bottom;

            child.Arrange(UiRect.FromEdges(x0, y0, MathF.Max(x0, x1), MathF.Max(y0, y1)));
        }
    }
}
