using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>
/// A focusable row built from a leading badge, a title, and a trailing tag — the shape a mission entry,
/// a save slot or a profile takes in a list.
/// </summary>
/// <remarks>
/// Where <see cref="ListView"/> is one control owning many rows of plain text, a card <em>is</em> one row:
/// it takes focus itself, so a list of them is navigated and hit-tested by the ordinary focus model, each
/// row can be styled differently — a locked mission reads muted, the current one keeps an accent — and the
/// list is an ordinary <see cref="Layout.StackPanel"/> inside a <see cref="ScrollView"/> rather than a
/// widget with its own scrolling. That is what lets the campaign list scroll, hover and click with no
/// special cases.
/// </remarks>
public class ListCard : Control
{
    private readonly Spring _slide = new(0f, smoothTime: 0.14f);

    /// <summary>Initializes a new instance of the <see cref="ListCard"/> class.</summary>
    /// <param name="theme">The theme to draw from.</param>
    /// <param name="badge">The small leading marker — a mission number, a slot index.</param>
    /// <param name="title">The row's main text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public ListCard(UiTheme theme, string badge, string title)
        : base(theme)
    {
        Badge = badge ?? string.Empty;
        Title = title ?? string.Empty;
    }

    /// <summary>Gets or sets the small leading marker.</summary>
    public string Badge { get; set; }

    /// <summary>Gets or sets the row's main text.</summary>
    public string Title { get; set; }

    /// <summary>Gets or sets the short trailing tag, or an empty string for none.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Gets or sets the trailing tag's colour.</summary>
    public Bgra32 TagColor { get; set; } = Bgra32.FromRgb(150, 162, 172);

    /// <summary>Gets or sets a value indicating whether the row is the current one in its list.</summary>
    public bool Selected { get; set; }

    /// <summary>Gets or sets the row's height in design units.</summary>
    public float RowHeight { get; set; } = 54f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(available.X, context.Px(RowHeight));

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);

        // The row eases a little to the right as it becomes the current one, so the selection reads as a
        // physical movement down the list rather than a colour that jumps.
        _slide.Target = MathF.Max(Visual.Focus, Selected ? 1f : 0f);
        _slide.Update(elapsedSeconds);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float emphasis = MathF.Max(_slide.Value, IsPointerOver ? 0.45f : 0f);
        float appear = Visual.Appear;
        float pad = painter.Context.Px(16f);
        float slide = painter.Context.Px(8f) * _slide.Value;

        if (emphasis > 0.001f)
        {
            // A dark scrim plus an accent edge, not an accent wash. A row sits over whatever the level
            // behind the menu happens to be, and a translucent amber fill under amber text disappears the
            // moment that happens to be a sunlit hillside; darkening under the text works over anything.
            painter.FillRect(
                Bounds,
                Theme.Panel.WithAlpha((byte)(190 * emphasis * appear)),
                BlendMode.AlphaBlend);
            painter.FillRect(
                new UiRect(Bounds.X, Bounds.Y, painter.Context.Px(3f), Bounds.Height),
                Theme.Accent.WithAlpha((byte)(255 * emphasis * appear)),
                BlendMode.AlphaBlend);
        }

        float badgeSize = painter.Context.Px(Theme.BodyTextSize * 0.95f);
        float titleSize = painter.Context.Px(Theme.BodyTextSize * 1.1f);
        float tagSize = painter.Context.Px(Theme.BodyTextSize * 0.72f);

        Bgra32 titleColor = IsEnabled
            ? Interpolate.Lerp(Theme.Text, Theme.Accent, emphasis)
            : Theme.TextDisabled;
        Bgra32 badgeColor = IsEnabled
            ? Interpolate.Lerp(Theme.TextMuted, Theme.Accent, emphasis)
            : Theme.TextDisabled;

        titleColor = titleColor.WithAlpha((byte)(titleColor.A * appear));
        badgeColor = badgeColor.WithAlpha((byte)(badgeColor.A * appear));

        float badgeWidth = Theme.Heading.MeasureWidth(Badge, badgeSize);
        float x = Bounds.X + pad + slide;
        painter.DrawText(
            Theme.Heading,
            Badge,
            new UiVec2(x, Bounds.Y + ((Bounds.Height - Theme.Heading.LineHeight(badgeSize)) * 0.5f)),
            badgeSize,
            badgeColor);

        painter.DrawText(
            Theme.Body,
            Title,
            new UiVec2(x + badgeWidth + painter.Context.Px(18f), Bounds.Y + ((Bounds.Height - Theme.Body.LineHeight(titleSize)) * 0.5f)),
            titleSize,
            titleColor);

        if (Tag.Length > 0)
        {
            float tagWidth = Theme.Body.MeasureWidth(Tag, tagSize);
            Bgra32 tag = TagColor.WithAlpha((byte)(TagColor.A * appear));
            painter.DrawText(
                Theme.Body,
                Tag,
                new UiVec2(Bounds.Right - pad - tagWidth, Bounds.Y + ((Bounds.Height - Theme.Body.LineHeight(tagSize)) * 0.5f)),
                tagSize,
                tag);
        }
    }
}
