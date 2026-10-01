using OpenIGI.Core.Graphics;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>A page's heading: an accent eyebrow in monospace over a large condensed title.</summary>
/// <remarks>
/// <c>OPERATIONS</c> over <c>CAMPAIGN</c>, <c>CONFIGURATION</c> over <c>SETTINGS</c>: every page in the
/// design opens with one, so where a player is reads the same everywhere.
/// </remarks>
public class SectionTitle : UiElement
{
    private string _eyebrow;
    private string _title;

    /// <summary>Initializes a new instance of the <see cref="SectionTitle"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="eyebrow">The small caption above, shown in capitals.</param>
    /// <param name="title">The title, shown in capitals.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public SectionTitle(UiTheme theme, string eyebrow, string title)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme = theme;
        _eyebrow = (eyebrow ?? string.Empty).ToUpperInvariant();
        _title = (title ?? string.Empty).ToUpperInvariant();
    }

    /// <summary>Gets the theme.</summary>
    public UiTheme Theme { get; }

    /// <summary>Gets or sets the small caption above the title.</summary>
    public string Eyebrow
    {
        get => _eyebrow;
        set => _eyebrow = (value ?? string.Empty).ToUpperInvariant();
    }

    /// <summary>Gets or sets the title.</summary>
    public string Title
    {
        get => _title;
        set => _title = (value ?? string.Empty).ToUpperInvariant();
    }

    /// <summary>Gets or sets the title's size, in design units.</summary>
    public float TitleSize { get; set; } = 44f;

    /// <summary>Gets or sets the eyebrow's colour, or <see langword="null"/> for the accent.</summary>
    public Bgra32? EyebrowColor { get; set; }

    /// <summary>Gets or sets the title's colour, or <see langword="null"/> for the theme's text colour.</summary>
    public Bgra32? TitleColor { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float title = context.Px(TitleSize);
        float width = MathF.Max(
            UiPainter.MeasureLabel(Theme.Mono, _eyebrow, context.Px(12f), 0.3f),
            UiPainter.MeasureLabel(Theme.Display, _title, title, 0.06f));
        return new UiVec2(width, Theme.Mono.LineHeight(context.Px(12f)) + context.Px(6f) + title);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float eyebrow = Theme.Mono.LineHeight(context.Px(12f));
        KitDraw.Eyebrow(painter, Theme, _eyebrow, Bounds.TopLeft, color: EyebrowColor);

        // line-height 1: the title's box is exactly its size, and the capitals sit in it the way CSS sets them.
        float size = context.Px(TitleSize);
        var box = new UiRect(Bounds.X, Bounds.Y + eyebrow + context.Px(6f), Bounds.Width, size);
        painter.DrawLabel(Theme.Display, _title, new UiVec2(Bounds.X, UiRowStyle.CapsCenterY(Theme.Display, size, box)), size, TitleColor ?? Theme.Text, 0.06f);
    }
}
