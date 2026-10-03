using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher;

/// <summary>A settings row whose control is a text field: the caption on the left, the field on the right.</summary>
/// <remarks>
/// <para>
/// The row itself takes no focus; the field does, so a pad or Tab lands straight in it and typing (or Ctrl+V)
/// goes where the caret is. The row lights up with the field, like every other settings row.
/// </para>
/// <para>
/// A row for a folder carries a Browse button beside the field, which opens the system's folder picker. The
/// picker answers from another thread; the answer is put in the field on this row's next update, on the
/// interface's own thread, as if it had been typed.
/// </para>
/// </remarks>
internal sealed class TextRow : Control
{
    private const float Gap = 8f;

    private readonly TextBox _box;
    private readonly Button? _browse;
    private string? _picked;

    /// <summary>Initialises a new instance of the <see cref="TextRow"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="caption">The row's caption.</param>
    /// <param name="text">The field's text.</param>
    /// <param name="placeholder">What the field shows while empty.</param>
    /// <param name="browse">
    /// For a folder, opens the system's picker starting from the field's text and answers with the folder
    /// chosen, or <see langword="null"/> when the player cancelled; <see langword="null"/> for no Browse button.
    /// </param>
    public TextRow(UiTheme theme, string caption, string text, string placeholder = "", Func<string, Task<string?>>? browse = null)
        : base(theme)
    {
        Caption = caption;
        _box = new TextBox(theme) { Text = text, Placeholder = placeholder, MaxLength = 1024 };
        _box.TextChanged += value => TextChanged?.Invoke(value);
        AddChild(_box);

        if (browse is not null)
        {
            _browse = new Button(theme, "Browse") { Accent = ButtonAccent.Ghost, TextSize = 15f, Padding = new Thickness(14f, 6f) };
            _browse.Activated += control => browse(Text).ContinueWith(
                task => _picked = task.IsCompletedSuccessfully ? task.Result : null,
                TaskScheduler.Default);
            AddChild(_browse);
        }
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

    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        if (Interlocked.Exchange(ref _picked, null) is { Length: > 0 } folder)
        {
            Text = folder;
        }
    }

    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        _box.Measure(context, new UiVec2(context.Px(FieldWidth), available.Y));
        _browse?.Measure(context, available);
        return UiRowStyle.Measure(this, context, available);
    }

    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float height = context.Px(36f);
        float top = contentRect.Center.Y - (height * 0.5f);
        float right = contentRect.Right - context.Px(UiRowStyle.Inset);
        if (_browse is not null)
        {
            float button = _browse.DesiredSize.X;
            _browse.Arrange(new UiRect(right - button, top, button, height));
            right -= button + context.Px(Gap);
        }

        float width = context.Px(FieldWidth);
        _box.Arrange(new UiRect(right - width, top, width, height));
    }

    protected override void OnDraw(UiPainter painter) =>
        UiRowStyle.DrawFrame(
            painter,
            this,
            Caption,
            _browse is null ? UiRowStyle.Highlight(_box) : MathF.Max(UiRowStyle.Highlight(_box), UiRowStyle.Highlight(_browse)));
}
