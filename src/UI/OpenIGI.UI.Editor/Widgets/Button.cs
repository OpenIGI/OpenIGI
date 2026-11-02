using OpenIGI.Core.Graphics;
using OpenIGI.UI.Editor.Actions;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>How a button is drawn.</summary>
public enum ButtonStyle
{
    /// <summary>A filled button with a border: dialogs, forms.</summary>
    Standard,

    /// <summary>The accent-filled button for the one action a dialog is for.</summary>
    Primary,

    /// <summary>No fill until hovered: toolbars, panel headers.</summary>
    Flat,
}

/// <summary>A button with text, an icon or both; optionally a toggle; optionally bound to an action.</summary>
public sealed class Button : Pressable
{
    /// <summary>Initializes a new instance of the <see cref="Button"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="text">The text, or empty for an icon-only button.</param>
    /// <param name="icon">The icon.</param>
    public Button(EditorTheme theme, string text = "", KitIcon icon = KitIcon.None)
        : base(theme)
    {
        Text = text;
        Icon = icon;
    }

    /// <summary>Gets or sets the text.</summary>
    public string Text { get; set; }

    /// <summary>Gets or sets the icon.</summary>
    public KitIcon Icon { get; set; }

    /// <summary>Gets or sets the style.</summary>
    public ButtonStyle Style { get; set; } = ButtonStyle.Standard;

    /// <summary>Gets or sets whether the button is a toggle that stays down while <see cref="IsChecked"/>.</summary>
    public bool IsToggle { get; set; }

    /// <summary>Gets or sets whether a toggle is down.</summary>
    public bool IsChecked { get; set; }

    /// <summary>Gets or sets the action the button runs, which also decides its enabled and checked state each frame.</summary>
    public EditorAction? Action
    {
        get;
        set
        {
            field = value;
            if (value is not null)
            {
                Tooltip = value.Shortcut is { } shortcut ? $"{value.Title} ({shortcut})" : value.Title;
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnClick()
    {
        if (Action is { } action)
        {
            action.TryExecute();
        }
        else if (IsToggle)
        {
            IsChecked = !IsChecked;
        }
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        if (Action is { } action)
        {
            IsEnabled = action.IsEnabled;
            IsChecked = action.Checked;
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float pad = Theme.Px(Text.Length > 0 ? 10f : 4f);
        float width = pad * 2f;
        if (Icon != KitIcon.None)
        {
            width += Theme.IconSize;
        }

        if (Text.Length > 0)
        {
            width += Theme.Font.MeasureWidth(Text, Theme.TextSize) + (Icon != KitIcon.None ? Theme.Px(6f) : 0f);
        }

        return new UiVec2(MathF.Max(width, Theme.ControlHeight), Theme.ControlHeight);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        UiRect rect = Bounds;
        bool down = IsToggle && IsChecked;
        switch (Style)
        {
            case ButtonStyle.Primary:
                EditorDraw.Fill(painter, Theme, rect, IsEnabled ? Theme.Accent : Theme.Button);
                break;
            case ButtonStyle.Standard:
                EditorDraw.Frame(painter, Theme, rect, Theme.Button, Theme.Border);
                break;
        }

        if (down)
        {
            EditorDraw.Fill(painter, Theme, rect, Theme.Selection);
        }

        DrawFeedback(painter, rect);

        Bgra32 ink = !IsEnabled ? Theme.TextDisabled
            : Style == ButtonStyle.Primary ? Theme.OnAccent
            : down ? Theme.Accent
            : Theme.Text;
        float textWidth = Text.Length > 0 ? Theme.Font.MeasureWidth(Text, Theme.TextSize) : 0f;
        float iconWidth = Icon != KitIcon.None ? Theme.IconSize : 0f;
        float gap = Icon != KitIcon.None && Text.Length > 0 ? Theme.Px(6f) : 0f;
        float x = MathF.Round(rect.X + ((rect.Width - (iconWidth + gap + textWidth)) * 0.5f));

        if (Icon != KitIcon.None)
        {
            EditorDraw.Icon(painter, Theme, Icon, new UiRect(x, rect.Y, iconWidth, rect.Height), Text.Length > 0 || down ? ink : IsEnabled ? Theme.Icon : Theme.TextDisabled);
            x += iconWidth + gap;
        }

        if (Text.Length > 0)
        {
            EditorDraw.Label(painter, Theme, Text, new UiRect(x, rect.Y, rect.Right - x, rect.Height), ink);
        }
    }
}

/// <summary>A tick box with an optional label.</summary>
public sealed class CheckBox : Pressable
{
    /// <summary>Initializes a new instance of the <see cref="CheckBox"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="label">The label beside the box, or empty.</param>
    public CheckBox(EditorTheme theme, string label = "")
        : base(theme) => Label = label;

    /// <summary>Raised when the user ticks or unticks the box.</summary>
    public event Action<bool>? CheckedChanged;

    /// <summary>Gets or sets the label.</summary>
    public string Label { get; set; }

    /// <summary>Gets or sets whether the box is ticked. Setting it does not raise <see cref="CheckedChanged"/>.</summary>
    public bool IsChecked { get; set; }

    /// <inheritdoc/>
    public override bool Focusable => true;

    /// <inheritdoc/>
    protected override void OnClick()
    {
        IsChecked = !IsChecked;
        CheckedChanged?.Invoke(IsChecked);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float box = Theme.Px(14f);
        float width = box + (Label.Length > 0 ? Theme.Px(6f) + Theme.Font.MeasureWidth(Label, Theme.TextSize) : 0f);
        return new UiVec2(width, Theme.ControlHeight);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float size = Theme.Px(14f);
        UiRect box = new(Bounds.X, MathF.Round(Bounds.Y + ((Bounds.Height - size) * 0.5f)), size, size);
        Bgra32 border = IsFocused ? Theme.Accent : IsPointerOver ? Theme.BorderStrong : Theme.Border;
        if (IsChecked)
        {
            EditorDraw.Fill(painter, Theme, box, IsEnabled ? Theme.Accent : Theme.Button, Theme.Px(3f));
            KitIcons.Draw(painter, KitIcon.Check, box.Center, size * 0.8f, Theme.OnAccent);
        }
        else
        {
            EditorDraw.Frame(painter, Theme, box, Theme.Input, border, Theme.Px(3f));
        }

        if (Label.Length > 0)
        {
            float x = box.Right + Theme.Px(6f);
            EditorDraw.Label(painter, Theme, Label, new UiRect(x, Bounds.Y, Bounds.Right - x, Bounds.Height), IsEnabled ? Theme.Text : Theme.TextDisabled);
        }
    }
}
