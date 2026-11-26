using OpenIGI.Core.Graphics;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>
/// A row that opens and closes the rows under it: a caption in the accent on the left and a chevron on the right that
/// points down while they are hidden and up while they are shown.
/// </summary>
/// <remarks>
/// Accept or a click flips it; right opens and left closes, as a toggle's do, so a pad never flips it the wrong way by
/// pressing twice. The rows it governs are the owner's to show and hide, from <see cref="ExpandedChanged"/>.
/// </remarks>
public class ExpanderRow : Control
{
    private readonly Tween _turn = new(0f, duration: 0.18f, Ease.Snappy);
    private bool _expanded;

    /// <summary>Initializes a new instance of the <see cref="ExpanderRow"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="caption">The caption.</param>
    /// <param name="expanded">Whether it starts open.</param>
    public ExpanderRow(UiTheme theme, string caption, bool expanded = false)
        : base(theme)
    {
        Caption = caption ?? string.Empty;
        _expanded = expanded;
        _turn.SnapTo(expanded ? 1f : 0f);
    }

    /// <summary>Raised when the row is opened or closed.</summary>
    public event Action<bool>? ExpandedChanged;

    /// <summary>Gets or sets the caption.</summary>
    public string Caption { get; set; }

    /// <summary>Gets or sets a value indicating whether the rows under it are shown.</summary>
    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value)
            {
                return;
            }

            _expanded = value;
            _turn.Retarget(value ? 1f : 0f);
            ExpandedChanged?.Invoke(value);
        }
    }

    /// <inheritdoc/>
    public override bool HandleNavigate(NavDirection direction)
    {
        switch (direction)
        {
            case NavDirection.Left when IsExpanded:
                IsExpanded = false;
                return true;
            case NavDirection.Right when !IsExpanded:
                IsExpanded = true;
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    protected override void OnActivate() => IsExpanded = !IsExpanded;

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        _turn.Update(elapsedSeconds);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        UiRowStyle.Measure(this, context, available);

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float highlight = UiRowStyle.Highlight(this);
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float size = context.Px(12f);
        var center = new UiVec2(Bounds.Right - context.Px(Theme.RowInset) - size, Bounds.Center.Y);

        // The frame with no caption of its own: the caption here is set in the accent, as a link rather than a setting.
        UiRowStyle.DrawFrame(painter, this, string.Empty, highlight);
        float sizePx = context.Px(Theme.BodyTextSize);
        Bgra32 ink = Interpolate.Lerp(Theme.Accent, Theme.AccentHover, highlight);
        float y = Bounds.Y + ((Bounds.Height - Theme.Body.LineHeight(sizePx)) * 0.5f);
        painter.DrawText(Theme.BodyStrong, Caption, new UiVec2(Bounds.X + context.Px(Theme.RowInset), y), sizePx, UiPalette.Fade(ink, appear));
        painter.DrawChevron(center, size, _turn.Value >= 0.5f ? UiDirection.Up : UiDirection.Down, context.Px(2f), UiPalette.Fade(ink, appear));
    }
}
