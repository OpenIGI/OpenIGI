using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// Where the header's underline stands, kept by the shell between its pages. Each page builds a header of its own, so the
/// underline of the page arrived at has to be told where the one of the page left stood, to glide from there.
/// </summary>
public sealed class SectionUnderline
{
    /// <summary>Gets or sets the underline as last drawn, in pixels, or <see langword="null"/> when no section is current.</summary>
    public UiRect? Rect { get; set; }

    /// <summary>Gets or sets the header that draws the underline now: the newest page's, so one leaving does not draw a second.</summary>
    public object? Owner { get; set; }
}

/// <summary>
/// The header's section tabs, with one underline for the row that glides from the section left to the one arrived at, the
/// way the block of a <see cref="TabStrip"/> does, instead of each tab drawing its own.
/// </summary>
internal sealed class SectionTabs : FlexPanel
{
    private readonly UiTheme _theme;
    private readonly SectionUnderline _shared;
    private readonly Spring _x = new(0f, smoothTime: 0.09f);
    private readonly Spring _width = new(0f, smoothTime: 0.09f);
    private HeaderTab? _current;
    private bool _placed;

    /// <summary>Initializes a new instance of the <see cref="SectionTabs"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="shared">Where the underline stands, shared by every page's header.</param>
    public SectionTabs(UiTheme theme, SectionUnderline shared)
    {
        _theme = theme;
        _shared = shared;
        _shared.Owner = this;
    }

    /// <summary>Adds a section's tab to the row.</summary>
    /// <param name="tab">The tab.</param>
    /// <param name="current">Whether it is the section the page is.</param>
    public void AddTab(HeaderTab tab, bool current)
    {
        ArgumentNullException.ThrowIfNull(tab);
        tab.ShowsUnderline = false;
        tab.Selected = current;
        if (current)
        {
            _current = tab;
        }

        Add(tab);
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        if (ReferenceEquals(_shared.Owner, this) && _current is null)
        {
            // No section is current -- the landing page -- so the next page's underline has nowhere to glide from.
            _shared.Rect = null;
        }

        if (Under() is not { } under)
        {
            return;
        }

        if (!_placed)
        {
            // From where the last page's stood; or, with none, opening out from the tab's middle.
            UiRect from = _shared.Rect ?? new UiRect(under.X + (under.Width / 2f), under.Y, 0f, under.Height);
            _x.SnapTo(from.X);
            _width.SnapTo(from.Width);
            _placed = true;
        }

        _x.Target = under.X;
        _width.Target = under.Width;
        _x.Update(elapsedSeconds);
        _width.Update(elapsedSeconds);

        // Kept as it moves, not as it is drawn: a page left before its first draw still says where its underline was.
        if (ReferenceEquals(_shared.Owner, this))
        {
            _shared.Rect = new UiRect(_x.Value, under.Y, _width.Value, under.Height);
        }
    }

    /// <inheritdoc/>
    protected override void OnDrawOverlay(UiPainter painter)
    {
        if (!_placed || !ReferenceEquals(_shared.Owner, this) || Under() is not { } under)
        {
            return;
        }

        var line = new UiRect(_x.Value, under.Y, _width.Value, under.Height);
        painter.FillRect(line, _theme.Accent);
    }

    // Where the underline belongs: along the foot of the current tab, inside its padding. Null with no current tab, or
    // before the row has been laid out.
    private UiRect? Under()
    {
        if (_current is not { } tab || tab.Bounds.IsEmpty || Context is not { } context)
        {
            return null;
        }

        float inset = context.Px(tab.Padding.Left);
        float thickness = context.Px(2f);
        return new UiRect(tab.Bounds.X + inset, tab.Bounds.Bottom - thickness, MathF.Max(0f, tab.Bounds.Width - (inset * 2f)), thickness);
    }
}
