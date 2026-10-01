using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>A segmented control: a row of tabs in a glass tray, with an amber block that slides to the selected one.</summary>
/// <remarks>
/// One focusable control for the whole row: left and right move between tabs while it holds focus, and
/// the accent block springs across to the selection rather than jumping. Each tab additionally owns a
/// rectangle of the strip, resolved at layout time, so a pointer can hover and click <em>a</em> tab rather
/// than the strip as a whole — without that, clicking Graphics or Audio only ever re-activated whichever
/// tab was already current. The screen listens to <see cref="SelectionChanged"/> and swaps the panel
/// beneath. It is the spine of the settings screen's Graphics / Display / Audio / Controls / Gameplay
/// categories.
/// </remarks>
public class TabStrip : Control
{
    private readonly List<string> _tabs;
    private readonly Spring _indicator = new(0f, smoothTime: 0.08f);
    private readonly Spring _indicatorWidth = new(0f, smoothTime: 0.08f);
    private readonly Dictionary<string, string> _upper = new(StringComparer.Ordinal);
    private int _selected;
    private int _hovered = -1;

    // Per-tab geometry resolved during Arrange, so hit testing does not depend on a draw having happened.
    private UiRect[] _tabRects = [];
    private float[] _tabCenters = [];
    private float[] _tabWidths = [];

    /// <summary>Initializes a new instance of the <see cref="TabStrip"/> class.</summary>
    /// <param name="theme">The theme to draw from.</param>
    /// <param name="tabs">The tab captions.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public TabStrip(UiTheme theme, IEnumerable<string> tabs)
        : base(theme)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        _tabs = [.. tabs];
    }

    /// <summary>Raised when the selected tab changes, with the new index.</summary>
    public event Action<int>? SelectionChanged;

    /// <summary>Gets or sets the gap between segments, and between the segments and the tray, in design units.</summary>
    public float Spacing { get; set; } = 4f;

    /// <summary>Gets or sets the horizontal padding either side of each segment's caption, in design units.</summary>
    public float TabPadding { get; set; } = 28f;

    /// <summary>Gets or sets a segment's height, in design units.</summary>
    public float SegmentHeight { get; set; } = 44f;

    /// <summary>Gets or sets the captions' size, in design units; zero for the theme's label size.</summary>
    public float TextSize { get; set; }

    /// <summary>Gets the tab captions.</summary>
    public IReadOnlyList<string> Tabs => _tabs;

    /// <summary>Gets or sets the selected tab index.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (_tabs.Count == 0)
            {
                return;
            }

            int clamped = Math.Clamp(value, 0, _tabs.Count - 1);
            if (clamped == _selected)
            {
                return;
            }

            _selected = clamped;
            SelectionChanged?.Invoke(_selected);
        }
    }

    /// <summary>Gets the rectangle a tab occupies, valid after the strip has been arranged.</summary>
    /// <param name="index">The tab index.</param>
    /// <returns>The tab's hit rectangle, or <see cref="UiRect.Empty"/> when the index is not a tab.</returns>
    public UiRect RectOf(int index) =>
        index >= 0 && index < _tabRects.Length ? _tabRects[index] : UiRect.Empty;

    /// <summary>Gets the index of the tab under a point, or -1 when the point is over none.</summary>
    /// <param name="point">The point in pixels.</param>
    /// <returns>The tab index, or -1.</returns>
    public int IndexAt(UiVec2 point)
    {
        for (int i = 0; i < _tabRects.Length; i++)
        {
            if (_tabRects[i].Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    /// <inheritdoc/>
    public override bool HandleNavigate(NavDirection direction)
    {
        switch (direction)
        {
            case NavDirection.Left when _selected > 0:
                SelectedIndex = _selected - 1;
                return true;
            case NavDirection.Right when _selected < _tabs.Count - 1:
                SelectedIndex = _selected + 1;
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    public override void ActivateAt(UiVec2 point)
    {
        int index = IndexAt(point);
        if (index >= 0)
        {
            SelectedIndex = index;
        }

        Activate();
    }

    /// <inheritdoc/>
    public override void PointerMoved(UiVec2 point) => _hovered = IndexAt(point);

    /// <inheritdoc/>
    protected override void OnPointerOverChanged(bool over)
    {
        if (!over)
        {
            _hovered = -1;
        }
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        if (_selected < _tabCenters.Length)
        {
            // The first frame there is a layout, the block starts where it belongs instead of sliding in.
            if (_indicatorWidth.Value <= 0f)
            {
                _indicator.SnapTo(_tabRects[_selected].X);
                _indicatorWidth.SnapTo(_tabRects[_selected].Width);
            }

            _indicator.Target = _tabRects[_selected].X;
            _indicatorWidth.Target = _tabRects[_selected].Width;
        }

        _indicator.Update(elapsedSeconds);
        _indicatorWidth.Update(elapsedSeconds);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float sizePx = context.Px(CaptionSize);
        float gap = context.Px(Spacing);
        float pad = context.Px(TabPadding);
        float width = gap;
        foreach (string tab in _tabs)
        {
            width += UiPainter.MeasureLabel(Theme.Heading, Upper(tab), sizePx, Theme.LabelTracking) + (pad * 2f) + gap;
        }

        return new UiVec2(width, context.Px(SegmentHeight) + (gap * 2f));
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float sizePx = context.Px(CaptionSize);
        float gap = context.Px(Spacing);
        float pad = context.Px(TabPadding);
        float height = context.Px(SegmentHeight);

        _tabRects = new UiRect[_tabs.Count];
        _tabCenters = new float[_tabs.Count];
        _tabWidths = new float[_tabs.Count];

        float x = contentRect.X + gap;
        for (int i = 0; i < _tabs.Count; i++)
        {
            float w = UiPainter.MeasureLabel(Theme.Heading, Upper(_tabs[i]), sizePx, Theme.LabelTracking);
            _tabWidths[i] = w;
            _tabRects[i] = new UiRect(x, contentRect.Y + gap, w + (pad * 2f), height);
            _tabCenters[i] = _tabRects[i].Center.X;
            x += _tabRects[i].Width + gap;
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        if (_tabs.Count == 0 || _tabRects.Length != _tabs.Count)
        {
            return;
        }

        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float focus = Visual.Focus;

        // The tray: dark glass with a hairline, the segments set inside it.
        painter.FillRect(Bounds, UiPalette.Fade(UiPalette.Ink.WithAlpha(153), appear));
        painter.StrokeRect(Bounds, context.Px(1f), UiPalette.Fade(Interpolate.Lerp(Theme.Border, UiPalette.AccentAt(0.45f), focus), appear));

        // The sliding block; before its first update it is drawn where the selection is.
        UiRect selected = _tabRects[_selected];
        if (_indicatorWidth.Value <= 0f)
        {
            _indicator.SnapTo(selected.X);
            _indicatorWidth.SnapTo(selected.Width);
        }

        painter.FillRect(
            new UiRect(_indicator.Value, selected.Y, _indicatorWidth.Value, selected.Height),
            UiPalette.Fade(_hovered == _selected ? Theme.AccentHover : Theme.Accent, appear));

        float sizePx = context.Px(CaptionSize);
        for (int i = 0; i < _tabs.Count; i++)
        {
            UiRect rect = _tabRects[i];

            // How much of the block is under this segment decides its caption's colour, so the dark text
            // follows the amber across rather than flipping when the selection lands.
            float overlap = MathF.Max(0f, MathF.Min(rect.Right, _indicator.Value + _indicatorWidth.Value) - MathF.Max(rect.X, _indicator.Value));
            float covered = rect.Width <= 0f ? 0f : Math.Clamp(overlap / rect.Width, 0f, 1f);
            Bgra32 rest = _hovered == i ? Theme.TextBright : UiPalette.TextTertiary;
            Bgra32 color = Interpolate.Lerp(rest, Theme.OnAccent, covered);

            string caption = Upper(_tabs[i]);
            painter.DrawLabel(
                Theme.Heading,
                caption,
                new UiVec2(rect.X + ((rect.Width - _tabWidths[i]) * 0.5f), UiRowStyle.CapsCenterY(Theme.Heading, sizePx, rect)),
                sizePx,
                UiPalette.Fade(color, appear),
                Theme.LabelTracking);
        }
    }

    private float CaptionSize => TextSize > 0f ? TextSize : Theme.LabelTextSize;

    private string Upper(string caption)
    {
        if (!_upper.TryGetValue(caption, out string? upper))
        {
            upper = caption.ToUpperInvariant();
            _upper[caption] = upper;
        }

        return upper;
    }
}
