using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>A panel's edge in a colour: the accent along the top of the side panel.</summary>
public sealed class ColourEdge : UiElement
{
    /// <summary>Gets or sets the colour.</summary>
    public Bgra32 Colour { get; set; } = UiPalette.Accent;

    /// <summary>Gets or sets how thick it is, in design units.</summary>
    public float Thickness { get; set; } = 2f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(Thickness));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter) => painter.FillRect(Bounds, Colour);
}
