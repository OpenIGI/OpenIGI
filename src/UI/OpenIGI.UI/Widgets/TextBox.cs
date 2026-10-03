using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.UI.Widgets;

/// <summary>A single-line text field driven by the platform's decoded character queue.</summary>
/// <remarks>
/// The shell owns raw keyboard dispatch, while this control owns the editable value and its presentation.
/// Keeping that boundary lets a form use the same field with keyboard, pointer and gamepad focus without
/// making a page responsible for clipping, drawing or validating individual characters.
/// </remarks>
public sealed class TextBox : Control
{
    private string _text = string.Empty;

    /// <summary>Initializes a text field.</summary>
    public TextBox(UiTheme theme)
        : base(theme)
    {
    }

    /// <summary>Raised whenever <see cref="Text"/> changes.</summary>
    public event Action<string>? TextChanged;

    /// <summary>Gets or sets the editable text.</summary>
    public string Text
    {
        get => _text;
        set
        {
            string trimmed = value ?? string.Empty;
            if (trimmed.Length > MaxLength)
            {
                trimmed = trimmed[..MaxLength];
            }

            if (_text == trimmed)
            {
                return;
            }

            _text = trimmed;
            TextChanged?.Invoke(_text);
        }
    }

    /// <summary>Gets or sets the faint prompt shown while the field is empty.</summary>
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>Gets or sets the maximum accepted character count.</summary>
    public int MaxLength { get; set; } = 256;

    /// <summary>Appends one printable platform-decoded character.</summary>
    /// <param name="character">The character to append.</param>
    /// <returns><see langword="true"/> when the text changed.</returns>
    public bool Append(char character)
    {
        if (char.IsControl(character) || _text.Length >= MaxLength)
        {
            return false;
        }

        Text += character;
        return true;
    }

    /// <summary>Deletes the final character.</summary>
    /// <returns><see langword="true"/> when the text changed.</returns>
    public bool Backspace()
    {
        if (_text.Length == 0)
        {
            return false;
        }

        Text = _text[..^1];
        return true;
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float size = context.Px(Theme.BodyTextSize);
        return new UiVec2(available.X, Theme.Body.LineHeight(size) + context.Px(24f));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float focus = MathF.Max(Visual.Focus, IsPointerOver ? 0.35f : 0f);
        float size = painter.Context.Px(Theme.BodyTextSize);
        float pad = painter.Context.Px(14f);
        Bgra32 border = Interpolate.Lerp(Theme.Border, Theme.Accent, focus);
        Bgra32 text = IsEnabled ? Theme.Text : Theme.TextDisabled;
        string drawn = _text.Length == 0 ? Placeholder : _text;
        Bgra32 color = _text.Length == 0 ? Theme.TextMuted : text;

        painter.FillRect(Bounds, Theme.Panel.WithAlpha((byte)(175 + (45 * focus))), UiBlend.AlphaBlend);
        painter.StrokeRect(Bounds, painter.Context.Px(1f), border, UiBlend.AlphaBlend);

        // The text stays inside the field. Not being edited, a long value is cut short with an ellipsis; being
        // edited, it slides left so its end, where the caret is, stays in view.
        var inner = new UiRect(Bounds.X + pad, Bounds.Y, MathF.Max(0f, Bounds.Width - (pad * 2f)), Bounds.Height);
        bool editing = Visual.Focus > 0.45f && IsEnabled;
        float caretWidth = painter.Context.Px(2f);
        float textWidth = Theme.Body.MeasureWidth(drawn, size);
        float x = editing && _text.Length > 0 && textWidth + caretWidth > inner.Width ? inner.Right - textWidth - caretWidth : inner.X;
        string shown = editing ? drawn : TextFit.ToWidth(Theme.Body, drawn, size, inner.Width);

        painter.PushClip(inner);
        painter.DrawText(Theme.Body, shown, new UiVec2(x, UiRowStyle.CenterY(Theme.Body, size, Bounds)), size, color);
        if (editing)
        {
            float caretHeight = Theme.Body.LineHeight(size) * 0.78f;
            float caretX = x + (_text.Length == 0 ? 0f : textWidth) + caretWidth;
            painter.FillRect(
                new UiRect(caretX, Bounds.Y + ((Bounds.Height - caretHeight) * 0.5f), caretWidth, caretHeight),
                Theme.Accent,
                UiBlend.AlphaBlend);
        }

        painter.PopClip();
    }
}
