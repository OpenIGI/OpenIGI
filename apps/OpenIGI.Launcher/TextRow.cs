using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher;

/// <summary>A settings row whose control is a text field: the caption on the left, the field on the right.</summary>
/// <remarks>
/// The row itself takes no focus; the field does, so a pad or Tab lands straight in it and typing (or Ctrl+V)
/// goes where the caret is. The row lights up with the field, like every other settings row.
/// </remarks>
internal sealed class TextRow : Control
{
    private readonly TextBox _box;

    public TextRow(UiTheme theme, string caption, string text, string placeholder = "")
        : base(theme)
    {
        Caption = caption;
        _box = new TextBox(theme) { Text = text, Placeholder = placeholder, MaxLength = 1024 };
        _box.TextChanged += value => TextChanged?.Invoke(value);
        AddChild(_box);
    }

    /// <summary>Raised whenever the field's text changes.</summary>
    public event Action<string>? TextChanged;

    public string Caption { get; set; }

    public string Text
    {
        get => _box.Text;
        set => _box.Text = value;
    }

    /// <summary>Gets or sets the field's width, in design units.</summary>
    public float FieldWidth { get; set; } = 420f;

    protected override bool IsFocusable => false;

    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        _box.Measure(context, new UiVec2(context.Px(FieldWidth), available.Y));
        return UiRowStyle.Measure(this, context, available);
    }

    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float height = context.Px(36f);
        float width = context.Px(FieldWidth);
        float right = contentRect.Right - context.Px(UiRowStyle.Inset);
        _box.Arrange(new UiRect(right - width, contentRect.Center.Y - (height * 0.5f), width, height));
    }

    protected override void OnDraw(UiPainter painter) =>
        UiRowStyle.DrawFrame(painter, this, Caption, UiRowStyle.Highlight(_box));
}
