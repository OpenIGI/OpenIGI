using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>A horizontal row of tabs with an underline that slides to the selected one.</summary>
/// <remarks>
/// One focusable control for the whole row: left and right move between tabs while it holds focus, and
/// the accent underline springs across to the selection rather than jumping. The screen listens to
/// <see cref="SelectionChanged"/> and swaps the panel beneath. It is the spine of the settings screen's
/// Graphics / Display / Audio / Controls / Gameplay categories.
/// </remarks>
public class TabStrip : Control
{
    private readonly List<string> _tabs;
    private readonly Spring _indicator = new(0f, smoothTime: 0.12f);
    private int _selected;

    // Cached per-tab centres and widths from the last draw, so the indicator can be placed in pixels.
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

    /// <summary>Gets or sets the gap between tabs, in design units.</summary>
    public float Spacing { get; set; } = 28f;

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
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        if (_selected < _tabCenters.Length)
        {
            _indicator.Target = _tabCenters[_selected];
        }

        _indicator.Update(elapsedSeconds);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float sizePx = context.Px(Theme.HeadingTextSize * 0.5f);
        float spacing = context.Px(Spacing);
        float width = 0f;
        for (int i = 0; i < _tabs.Count; i++)
        {
            width += Theme.Body.MeasureWidth(_tabs[i], sizePx) + (i > 0 ? spacing : 0f);
        }

        return new UiVec2(width, Theme.Body.LineHeight(sizePx) + context.Px(14f));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float sizePx = painter.Context.Px(Theme.HeadingTextSize * 0.5f);
        float spacing = painter.Context.Px(Spacing);
        float focus = Visual.Focus;

        _tabCenters = new float[_tabs.Count];
        _tabWidths = new float[_tabs.Count];

        float x = Bounds.X;
        float textY = Bounds.Y + painter.Context.Px(2f);
        for (int i = 0; i < _tabs.Count; i++)
        {
            float w = Theme.Body.MeasureWidth(_tabs[i], sizePx);
            _tabWidths[i] = w;
            _tabCenters[i] = x + (w * 0.5f);

            bool isSelected = i == _selected;
            Bgra32 color = isSelected
                ? Interpolate.Lerp(Theme.Text, Theme.Accent, MathF.Max(0.4f, focus))
                : Theme.TextMuted;
            painter.DrawText(Theme.Body, _tabs[i], new UiVec2(x, textY), sizePx, color);
            x += w + spacing;
        }

        if (_tabs.Count == 0)
        {
            return;
        }

        // The sliding underline. Snap it into place the first frame before it has a target.
        if (_indicator.Value <= 0f && _selected < _tabCenters.Length)
        {
            _indicator.SnapTo(_tabCenters[_selected]);
        }

        float indicatorWidth = _tabWidths[_selected];
        float underlineY = Bounds.Bottom - painter.Context.Px(4f);
        painter.FillRect(
            new UiRect(_indicator.Value - (indicatorWidth * 0.5f), underlineY, indicatorWidth, painter.Context.Px(3f)),
            Theme.Accent,
            BlendMode.AlphaBlend);
    }
}
