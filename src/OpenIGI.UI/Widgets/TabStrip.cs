using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>A horizontal row of tabs with an underline that slides to the selected one.</summary>
/// <remarks>
/// One focusable control for the whole row: left and right move between tabs while it holds focus, and
/// the accent underline springs across to the selection rather than jumping. Each tab additionally owns a
/// rectangle of the strip, resolved at layout time, so a pointer can hover and click <em>a</em> tab rather
/// than the strip as a whole — without that, clicking Graphics or Audio only ever re-activated whichever
/// tab was already current. The screen listens to <see cref="SelectionChanged"/> and swaps the panel
/// beneath. It is the spine of the settings screen's Graphics / Display / Audio / Controls / Gameplay
/// categories.
/// </remarks>
public class TabStrip : Control
{
    private readonly List<string> _tabs;
    private readonly Spring _indicator = new(0f, smoothTime: 0.12f);
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

    /// <summary>Gets or sets the gap between tabs, in design units.</summary>
    public float Spacing { get; set; } = 28f;

    /// <summary>Gets or sets the padding added around each tab's caption to make its hit target, in design units.</summary>
    public float TabPadding { get; set; } = 14f;

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

        return new UiVec2(width + context.Px(TabPadding), Theme.Body.LineHeight(sizePx) + context.Px(18f));
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float sizePx = context.Px(Theme.HeadingTextSize * 0.5f);
        float spacing = context.Px(Spacing);
        float pad = context.Px(TabPadding);

        _tabRects = new UiRect[_tabs.Count];
        _tabCenters = new float[_tabs.Count];
        _tabWidths = new float[_tabs.Count];

        float x = contentRect.X + (pad * 0.5f);
        for (int i = 0; i < _tabs.Count; i++)
        {
            float w = Theme.Body.MeasureWidth(_tabs[i], sizePx);
            _tabWidths[i] = w;
            _tabCenters[i] = x + (w * 0.5f);

            // The hit target reaches half the padding either side of the caption and the strip's full
            // height, so the gap between two tabs belongs to the nearer of them rather than to nothing.
            _tabRects[i] = new UiRect(x - (pad * 0.5f), contentRect.Y, w + pad, contentRect.Height);
            x += w + spacing;
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        if (_tabs.Count == 0 || _tabRects.Length != _tabs.Count)
        {
            return;
        }

        float sizePx = painter.Context.Px(Theme.HeadingTextSize * 0.5f);
        float focus = Visual.Focus;
        float underlineY = Bounds.Bottom - painter.Context.Px(5f);

        // The rail the indicator runs along, so the row still reads as a set of tabs before anything moves.
        painter.FillRect(
            new UiRect(Bounds.X, underlineY, Bounds.Width, painter.Context.Px(1f)),
            Theme.Border.WithAlpha(70),
            BlendMode.AlphaBlend);

        // The pointer answers in the CAPTION, not in a block behind it: a filled rectangle under a tab is
        // as loud as the selection itself and turns a quiet row of words into a row of buttons. Lifting the
        // hovered caption out of the muted colour says the same thing and says it in the tab's own terms —
        // muted for the rest, bright for the one under the pointer, accent for the one you are on.
        for (int i = 0; i < _tabs.Count; i++)
        {
            bool isSelected = i == _selected;
            Bgra32 color = isSelected
                ? Interpolate.Lerp(Theme.Text, Theme.Accent, MathF.Max(0.4f, focus))
                : _hovered == i ? Theme.Text
                : Theme.TextMuted;

            float textY = Bounds.Y + ((Bounds.Height - painter.Context.Px(6f) - Theme.Body.LineHeight(sizePx)) * 0.5f);
            painter.DrawText(Theme.Body, _tabs[i], new UiVec2(_tabRects[i].X + (painter.Context.Px(TabPadding) * 0.5f), textY), sizePx, color);
        }

        // The sliding underline. Snap it into place the first frame before it has a target.
        if (_indicator.Value <= 0f && _selected < _tabCenters.Length)
        {
            _indicator.SnapTo(_tabCenters[_selected]);
        }

        float indicatorWidth = _tabWidths[_selected];
        painter.FillRect(
            new UiRect(_indicator.Value - (indicatorWidth * 0.5f), underlineY, indicatorWidth, painter.Context.Px(3f)),
            Theme.Accent,
            BlendMode.AlphaBlend);
    }
}
