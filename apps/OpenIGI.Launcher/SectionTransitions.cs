using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher;

/// <summary>The launcher's sections, one shown at a time; the one just shown slides in from the side its tab is on.</summary>
/// <remarks>
/// Only the incoming page moves. The outgoing one goes at once, so for the moment of the slide nothing can be
/// clicked or focused on a page that is leaving, and the two never draw over each other. The page is clipped to
/// the deck while it travels, so it never slides across the header or the footer.
/// </remarks>
internal sealed class SectionDeck : Panel
{
    /// <summary>How long the incoming page takes to settle.</summary>
    private const float EnterSeconds = 0.38f;

    /// <summary>How far, in design units, the incoming page travels.</summary>
    private const float Travel = 44f;

    private int _current = -1;
    private int _direction;
    private float _progress = 1f;
    private bool _pushed;

    /// <summary>Shows one page, sliding it in when another was showing.</summary>
    /// <param name="index">The page, by the order it was added.</param>
    public void Show(int index)
    {
        if (index != _current)
        {
            // From the right when moving right along the tabs, from the left when moving back.
            _direction = _current < 0 ? 0 : Math.Sign(index - _current);
            _progress = _current < 0 ? 1f : 0f;
            _current = index;
        }

        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Visibility = i == _current ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        if (_progress < 1f)
        {
            _progress = MathF.Min(1f, _progress + (elapsedSeconds / EnterSeconds));
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        base.OnDraw(painter);
        _pushed = _progress < 1f;
        if (!_pushed)
        {
            return;
        }

        float travel = 1f - Easing.Evaluate(Ease.Emphasized, _progress);
        painter.PushClip(Bounds);
        painter.PushOpacity(Easing.Evaluate(Ease.OutCubic, _progress));
        painter.PushTransform(UiTransform.Translate(_direction * painter.Context.Px(Travel) * travel, 0f));
    }

    /// <inheritdoc/>
    protected override void OnDrawOverlay(UiPainter painter)
    {
        if (_pushed)
        {
            painter.PopTransform();
            painter.PopOpacity();
            painter.PopClip();
            _pushed = false;
        }

        base.OnDrawOverlay(painter);
    }
}

/// <summary>The header's tabs, with one amber underline that glides to the selected tab instead of jumping.</summary>
/// <remarks>
/// The underline's leading edge runs ahead and its trailing edge follows, so it stretches across the gap and
/// draws itself back in as it lands, with a glow above it that is brightest while it is stretched furthest.
/// A switch made mid-glide starts from wherever the underline is.
/// </remarks>
internal sealed class SectionTabs : FlexPanel
{
    /// <summary>How long the trailing edge takes to arrive; the leading edge takes three quarters of it.</summary>
    private const float GlideSeconds = 0.36f;

    private readonly UiTheme _theme;
    private readonly List<HeaderTab> _tabs = [];
    private int _selected = -1;
    private float _progress = 1f;
    private (float Left, float Right)? _from;
    private (float Left, float Right)? _drawn;

    /// <summary>Initializes a new instance of the <see cref="SectionTabs"/> class.</summary>
    /// <param name="theme">The theme the underline takes its colour from.</param>
    public SectionTabs(UiTheme theme)
    {
        _theme = theme;
        Orientation = Orientation.Horizontal;
        Gap = 4f;
    }

    /// <summary>Adds a tab, which leaves its own underline to the strip.</summary>
    /// <param name="tab">The tab.</param>
    public void AddTab(HeaderTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        tab.ShowsUnderline = false;
        _tabs.Add(tab);
        Add(tab);
    }

    /// <summary>Moves the underline to a tab, and marks it the selected one.</summary>
    /// <param name="index">The tab.</param>
    public void Select(int index)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            _tabs[i].Selected = i == index;
        }

        if (index == _selected)
        {
            return;
        }

        // The first selection, and any before the strip has been drawn, puts the underline straight in place.
        _from = _selected < 0 ? null : _drawn;
        _progress = _from is null ? 1f : 0f;
        _selected = index;
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        if (_progress < 1f)
        {
            _progress = MathF.Min(1f, _progress + (elapsedSeconds / GlideSeconds));
        }
    }

    /// <inheritdoc/>
    protected override void OnDrawOverlay(UiPainter painter)
    {
        base.OnDrawOverlay(painter);
        if (_selected < 0 || _selected >= _tabs.Count || _tabs[_selected].Visibility != Visibility.Visible)
        {
            return;
        }

        LayoutContext context = painter.Context;
        HeaderTab tab = _tabs[_selected];
        float inset = context.Px(tab.Padding.Left);
        (float Left, float Right) target = (tab.Bounds.X + inset, tab.Bounds.Right - inset);

        (float Left, float Right) edges = target;
        float stretch = 0f;
        if (_progress < 1f && _from is { } from)
        {
            float lead = Easing.Evaluate(Ease.Emphasized, MathF.Min(1f, _progress / 0.75f));
            float trail = Easing.Evaluate(Ease.InOutCubic, _progress);
            bool rightward = target.Left > from.Left;
            edges = rightward
                ? (Lerp(from.Left, target.Left, trail), Lerp(from.Right, target.Right, lead))
                : (Lerp(from.Left, target.Left, lead), Lerp(from.Right, target.Right, trail));
            stretch = Math.Clamp(lead - trail, 0f, 1f);
        }

        _drawn = edges;
        float thickness = context.Px(2f);
        float bottom = tab.Bounds.Bottom;
        var bar = new UiRect(edges.Left, bottom - thickness, MathF.Max(0f, edges.Right - edges.Left), thickness);

        // The glow: a soft wash rising off the underline, there only while it moves.
        if (stretch > 0.001f)
        {
            float height = context.Px(14f);
            var glow = new UiRect(bar.X, bottom - thickness - height, bar.Width, height);
            Span<UiGradientStop> wash = [new(0f, UiPalette.Fade(_theme.Accent, 0f)), new(1f, UiPalette.Fade(_theme.Accent, 0.45f * MathF.Min(1f, stretch * 2.5f)))];
            painter.FillGradient(glow, wash, UiGradientDirection.Vertical);
        }

        painter.FillRect(bar, _theme.Accent);
    }

    private static float Lerp(float from, float to, float t) => from + ((to - from) * t);
}
