using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>
/// A panel of frosted glass: the scene behind it blurred, a dark translucent tint over that, and a
/// hairline round the edge. The surface every grouped block in the design sits on.
/// </summary>
/// <remarks>
/// The blur is whatever the renderer can do — the game samples a blurred copy of its live 3D frame, the
/// launcher blurs its hero image — and with nothing to blur the panel is just its tint, which is what it
/// would look like over a flat backdrop anyway.
/// </remarks>
public class GlassPanel : Panel
{
    /// <summary>Initializes a new instance of the <see cref="GlassPanel"/> class.</summary>
    /// <param name="theme">The theme the default tint, blur and hairline come from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public GlassPanel(UiTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme = theme;
    }

    /// <summary>Gets the theme.</summary>
    public UiTheme Theme { get; }

    /// <summary>Gets or sets the tint, or <see langword="null"/> for the theme's panel colour.</summary>
    public Bgra32? Tint { get; set; }

    /// <summary>Gets or sets a second tint the panel fades to from top to bottom, or <see langword="null"/> for a flat tint.</summary>
    public Bgra32? TintBottom { get; set; }

    /// <summary>Gets or sets the blur in design units, or <see langword="null"/> for the theme's.</summary>
    public float? Blur { get; set; }

    /// <summary>Gets or sets the hairline, or <see langword="null"/> for the theme's border; transparent for none.</summary>
    public Bgra32? Border { get; set; }

    /// <summary>Gets or sets which edges carry the hairline.</summary>
    public UiEdges BorderEdges { get; set; } = UiEdges.All;

    /// <summary>Gets or sets the opacity the panel is drawn at, for one that fades in on its own.</summary>
    public float Opacity { get; set; } = 1f;

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        if (Opacity <= 0.002f)
        {
            return;
        }

        painter.PushOpacity(Opacity);
        Bgra32 tint = Tint ?? Theme.Panel;
        float blur = painter.Context.Px(Blur ?? Theme.GlassBlur);
        if (TintBottom is { } bottom)
        {
            painter.DrawBackdrop(Bounds, blur, Bgra32.Transparent);
            Span<UiGradientStop> stops = [new(0f, tint), new(1f, bottom)];
            painter.FillGradient(Bounds, stops, UiGradientDirection.Vertical);
        }
        else
        {
            painter.DrawBackdrop(Bounds, blur, tint);
        }

        DrawEdges(painter, Bounds, Border ?? Theme.Border, BorderEdges);
        painter.PopOpacity();
    }

    /// <summary>Draws a one-unit hairline along some of a rectangle's edges.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="rect">The rectangle.</param>
    /// <param name="color">The hairline colour.</param>
    /// <param name="edges">Which edges.</param>
    public static void DrawEdges(UiPainter painter, UiRect rect, Bgra32 color, UiEdges edges)
    {
        ArgumentNullException.ThrowIfNull(painter);
        if (color.A == 0 || edges == UiEdges.None)
        {
            return;
        }

        float t = painter.Context.Px(1f);
        if (edges.HasFlag(UiEdges.Top))
        {
            painter.FillRect(new UiRect(rect.X, rect.Y, rect.Width, t), color);
        }

        if (edges.HasFlag(UiEdges.Bottom))
        {
            painter.FillRect(new UiRect(rect.X, rect.Bottom - t, rect.Width, t), color);
        }

        if (edges.HasFlag(UiEdges.Left))
        {
            painter.FillRect(new UiRect(rect.X, rect.Y, t, rect.Height), color);
        }

        if (edges.HasFlag(UiEdges.Right))
        {
            painter.FillRect(new UiRect(rect.Right - t, rect.Y, t, rect.Height), color);
        }
    }
}

/// <summary>Which edges of a rectangle something is drawn along.</summary>
[Flags]
public enum UiEdges
{
    /// <summary>No edge.</summary>
    None = 0,

    /// <summary>The top edge.</summary>
    Top = 1,

    /// <summary>The right edge.</summary>
    Right = 2,

    /// <summary>The bottom edge.</summary>
    Bottom = 4,

    /// <summary>The left edge.</summary>
    Left = 8,

    /// <summary>Every edge.</summary>
    All = Top | Right | Bottom | Left,
}
