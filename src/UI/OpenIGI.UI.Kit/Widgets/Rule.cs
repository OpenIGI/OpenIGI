using OpenIGI.Core.Graphics;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>A one-unit hairline, vertical or horizontal — the divider between the header's profile and Quit.</summary>
public class Rule : UiElement
{
    /// <summary>Gets or sets which way the line runs.</summary>
    public Orientation Orientation { get; set; } = Orientation.Vertical;

    /// <summary>Gets or sets the line's length, in design units; zero to fill the space offered.</summary>
    public float Length { get; set; } = 32f;

    /// <summary>Gets or sets the line's colour.</summary>
    public Bgra32 Color { get; set; } = UiPalette.White(0.12f);

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float length = Length > 0f ? context.Px(Length) : Orientation == Orientation.Vertical ? available.Y : available.X;
        return Orientation == Orientation.Vertical ? new UiVec2(context.Px(1f), length) : new UiVec2(length, context.Px(1f));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float t = painter.Context.Px(1f);
        UiRect line = Orientation == Orientation.Vertical
            ? new UiRect(Bounds.Center.X - (t * 0.5f), Bounds.Y, t, Bounds.Height)
            : new UiRect(Bounds.X, Bounds.Center.Y - (t * 0.5f), Bounds.Width, t);
        painter.FillRect(line, Color);
    }
}
