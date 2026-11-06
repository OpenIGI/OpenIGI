using OpenIGI.Core.Graphics;
using OpenIGI.UI.Editor.Actions;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>A row of flat buttons, usually bound to actions, in groups split by separators.</summary>
public sealed class Toolbar : EditorElement
{
    private readonly List<UiElement?> _items = [];

    /// <summary>Initializes a new instance of the <see cref="Toolbar"/> class.</summary>
    /// <param name="theme">The theme.</param>
    public Toolbar(EditorTheme theme)
        : base(theme)
    {
    }

    /// <summary>Adds a button for an action.</summary>
    /// <param name="action">The action.</param>
    /// <param name="icon">The icon.</param>
    /// <param name="showText">Whether the button shows the action's title beside the icon.</param>
    /// <returns>The button.</returns>
    public Button Add(EditorAction action, KitIcon icon, bool showText = false)
    {
        ArgumentNullException.ThrowIfNull(action);
        Button button = new(Theme, showText ? action.Title : string.Empty, icon) { Style = ButtonStyle.Flat, Action = action, IsToggle = action.IsChecked is not null };
        Add(button);
        return button;
    }

    /// <summary>Adds any element.</summary>
    /// <param name="element">The element.</param>
    public void Add(UiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        _items.Add(element);
        AddChild(element);
    }

    /// <summary>Adds a separator between groups.</summary>
    public void AddSeparator() => _items.Add(null);

    /// <summary>Adds a gap that pushes everything after it to the right end.</summary>
    public void AddSpring() => _items.Add(Spring);

    private static readonly UiElement Spring = new Canvas();

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        foreach (UiElement? item in _items)
        {
            if (item is not null && !ReferenceEquals(item, Spring))
            {
                item.Measure(context, new UiVec2(float.PositiveInfinity, Theme.ControlHeight));
            }
        }

        return new UiVec2(float.IsFinite(available.X) ? available.X : 0f, Theme.ToolbarHeight);
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float gap = Theme.Px(2f);
        float fixedWidth = 0f;
        int springs = 0;
        foreach (UiElement? item in _items)
        {
            fixedWidth += item is null ? Theme.Px(13f) : ReferenceEquals(item, Spring) ? 0f : item.DesiredSize.X + gap;
            springs += ReferenceEquals(item, Spring) ? 1 : 0;
        }

        float spring = springs > 0 ? MathF.Max(0f, contentRect.Width - (Theme.Padding * 2f) - fixedWidth) / springs : 0f;
        float x = contentRect.X + Theme.Padding;
        float y = MathF.Round(contentRect.Y + ((contentRect.Height - Theme.ControlHeight) * 0.5f));
        foreach (UiElement? item in _items)
        {
            if (item is null)
            {
                x += Theme.Px(13f);
            }
            else if (ReferenceEquals(item, Spring))
            {
                x += spring;
            }
            else
            {
                item.Arrange(new UiRect(MathF.Round(x), y, item.DesiredSize.X, Theme.ControlHeight));
                x += item.DesiredSize.X + gap;
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        painter.FillRect(Bounds, Theme.Chrome);
        painter.FillRect(new UiRect(Bounds.X, Bounds.Bottom - Theme.Hairline, Bounds.Width, Theme.Hairline), Theme.Border);
        float x = Bounds.X + Theme.Padding;
        float gap = Theme.Px(2f);
        foreach (UiElement? item in _items)
        {
            if (item is null)
            {
                painter.FillRect(new UiRect(MathF.Round(x + Theme.Px(6f)), Bounds.Y + Theme.Px(9f), Theme.Hairline, Bounds.Height - Theme.Px(18f)), Theme.Border);
                x += Theme.Px(13f);
            }
            else if (!ReferenceEquals(item, Spring))
            {
                x += item.DesiredSize.X + gap;
            }
        }
    }
}

/// <summary>The strip along the bottom of the window: messages on the left, facts on the right.</summary>
public sealed class StatusBar : EditorElement
{
    /// <summary>Initializes a new instance of the <see cref="StatusBar"/> class.</summary>
    /// <param name="theme">The theme.</param>
    public StatusBar(EditorTheme theme)
        : base(theme)
    {
    }

    /// <summary>Gets or sets the message on the left.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Gets or sets the message's colour, or <see langword="null"/> for muted text.</summary>
    public Bgra32? MessageColor { get; set; }

    /// <summary>Gets the facts on the right, drawn right to left in order.</summary>
    public List<string> Segments { get; } = [];

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(float.IsFinite(available.X) ? available.X : 0f, Theme.StatusBarHeight);

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        painter.FillRect(Bounds, Theme.Chrome);
        painter.FillRect(new UiRect(Bounds.X, Bounds.Y, Bounds.Width, Theme.Hairline), Theme.Border);
        float right = Bounds.Right - Theme.Padding;
        foreach (string segment in Segments)
        {
            float width = Theme.SmallFont.MeasureWidth(segment, Theme.SmallTextSize);
            float y = MathF.Round(Bounds.Y + ((Bounds.Height - Theme.SmallFont.LineHeight(Theme.SmallTextSize)) * 0.5f));
            painter.DrawText(Theme.SmallFont, segment, new UiVec2(right - width, y), Theme.SmallTextSize, Theme.TextMuted);
            right -= width + Theme.Px(18f);
        }

        float left = Bounds.X + Theme.Padding;
        EditorDraw.Label(painter, Theme, Message, new UiRect(left, Bounds.Y, MathF.Max(0f, right - left), Bounds.Height), MessageColor ?? Theme.TextMuted);
    }
}
