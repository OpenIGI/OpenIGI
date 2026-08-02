using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>A caption with a value chosen from a fixed list by cycling through it with left and right.</summary>
/// <remarks>
/// A cycling selector rather than a pop-up list, because that is the form a gamepad drives without a
/// pointer: left and right step through the options and wrap, and the current one is shown between two
/// chevrons. It is what a preset picker or a "Windowed / Borderless / Fullscreen" row is.
/// </remarks>
public class Dropdown : Control
{
    private readonly List<string> _options;
    private int _selected;

    /// <summary>Initializes a new instance of the <see cref="Dropdown"/> class.</summary>
    /// <param name="theme">The theme to draw from.</param>
    /// <param name="label">The caption.</param>
    /// <param name="options">The options to choose between.</param>
    /// <param name="selected">The initially selected index.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public Dropdown(UiTheme theme, string label, IEnumerable<string> options, int selected = 0)
        : base(theme)
    {
        ArgumentNullException.ThrowIfNull(options);
        Label = label ?? string.Empty;
        _options = [.. options];
        _selected = _options.Count > 0 ? Math.Clamp(selected, 0, _options.Count - 1) : 0;
    }

    /// <summary>Raised when the selection changes, with the new index.</summary>
    public event Action<int>? SelectionChanged;

    /// <summary>Gets or sets the caption.</summary>
    public string Label { get; set; }

    /// <summary>Gets the options.</summary>
    public IReadOnlyList<string> Options => _options;

    /// <summary>Gets or sets the selected index.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (_options.Count == 0)
            {
                return;
            }

            int clamped = Math.Clamp(value, 0, _options.Count - 1);
            if (clamped == _selected)
            {
                return;
            }

            _selected = clamped;
            SelectionChanged?.Invoke(_selected);
        }
    }

    /// <summary>Gets the selected option's text, or the empty string when there are none.</summary>
    public string SelectedOption => _options.Count > 0 ? _options[_selected] : string.Empty;

    /// <inheritdoc/>
    public override bool HandleNavigate(NavDirection direction)
    {
        if (_options.Count == 0)
        {
            return false;
        }

        switch (direction)
        {
            case NavDirection.Left:
                SelectedIndex = (_selected - 1 + _options.Count) % _options.Count;
                return true;
            case NavDirection.Right:
                SelectedIndex = (_selected + 1) % _options.Count;
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        if (_options.Count > 0)
        {
            SelectedIndex = (_selected + 1) % _options.Count;
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float sizePx = context.Px(Theme.BodyTextSize);
        float height = Theme.Body.LineHeight(sizePx) + context.Px(12f);
        return new UiVec2(available.X, height);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float focus = Visual.Focus;
        if (focus > 0.001f)
        {
            painter.FillRect(Bounds, Theme.FocusFill.WithAlpha((byte)(Theme.FocusFill.A * focus)), BlendMode.AlphaBlend);
        }

        float sizePx = painter.Context.Px(Theme.BodyTextSize);
        Bgra32 textColor = IsEnabled ? Interpolate.Lerp(Theme.Text, Theme.Accent, focus) : Theme.TextDisabled;
        float pad = painter.Context.Px(8f);
        float textY = Bounds.Y + ((Bounds.Height - Theme.Body.LineHeight(sizePx)) * 0.5f);

        painter.DrawText(Theme.Body, Label, new UiVec2(Bounds.X + pad, textY), sizePx, textColor);

        // The value sits at the right between chevrons, which only show when the control is focused.
        string value = SelectedOption;
        float valueWidth = Theme.Body.MeasureWidth(value, sizePx);
        float chevron = painter.Context.Px(18f);
        float right = Bounds.Right - pad;
        Bgra32 chevronColor = Theme.Accent.WithAlpha((byte)(255 * focus));

        painter.DrawText(Theme.Body, ">", new UiVec2(right - chevron, textY), sizePx, chevronColor);
        painter.DrawText(Theme.Body, value, new UiVec2(right - chevron - painter.Context.Px(6f) - valueWidth, textY), sizePx, textColor);
        painter.DrawText(Theme.Body, "<", new UiVec2(right - chevron - painter.Context.Px(12f) - valueWidth - chevron, textY), sizePx, chevronColor);
    }
}
