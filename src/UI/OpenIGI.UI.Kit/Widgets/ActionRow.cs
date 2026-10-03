using OpenIGI.Core.Graphics;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>
/// A settings row whose control is an action: a caption on the left and, on the right, an optional value
/// in a monospaced field — a folder, a launch argument — beside a ghost button that acts on it.
/// </summary>
/// <remarks>
/// The launcher's Library folder / CHANGE and Repair installation / VERIFY NOW rows. The whole row is one
/// focusable control and activating it anywhere presses the button, so a pad needs no second stop.
/// </remarks>
public class ActionRow : Control
{
    private string _action;

    /// <summary>Initializes a new instance of the <see cref="ActionRow"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="caption">The row's caption.</param>
    /// <param name="action">The button's caption, shown in capitals.</param>
    public ActionRow(UiTheme theme, string caption, string action)
        : base(theme)
    {
        Caption = caption ?? string.Empty;
        _action = (action ?? string.Empty).ToUpperInvariant();
    }

    /// <summary>Gets or sets the row's caption.</summary>
    public string Caption { get; set; }

    /// <summary>Gets or sets the button's caption.</summary>
    public string Action
    {
        get => _action;
        set => _action = (value ?? string.Empty).ToUpperInvariant();
    }

    /// <summary>Gets or sets the value shown in the field, or an empty string for a row that is only a button.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Gets or sets the field's width, in design units.</summary>
    public float FieldWidth { get; set; } = 260f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        UiRowStyle.Measure(this, context, available);

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float highlight = UiRowStyle.Highlight(this);
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float height = context.Px(36f);
        float top = Bounds.Center.Y - (height * 0.5f);
        float right = Bounds.Right - context.Px(UiRowStyle.Inset);

        float labelSize = context.Px(16f);
        float buttonWidth = UiPainter.MeasureLabel(Theme.Heading, _action, labelSize, 0.16f) + context.Px(28f);
        var button = new UiRect(right - buttonWidth, top, buttonWidth, height);
        UiRect? field = Value.Length == 0 ? null : FieldRect(context, button, top, height);
        UiRowStyle.DrawFrame(painter, this, Caption, highlight, field?.X ?? button.X);
        Bgra32 border = Interpolate.Lerp(UiPalette.White(0.18f), Theme.Accent, highlight);
        Bgra32 ink = Interpolate.Lerp(Theme.Text, Theme.Accent, highlight);
        painter.StrokeRect(button, context.Px(1f), UiPalette.Fade(border, appear));
        painter.DrawLabel(Theme.Heading, _action, new UiVec2(button.X + context.Px(14f), UiRowStyle.CapsCenterY(Theme.Heading, labelSize, button)), labelSize, UiPalette.Fade(ink, appear), 0.16f);

        if (field is not { } box)
        {
            return;
        }

        painter.FillRect(box, UiPalette.White(0.04f * appear));
        painter.StrokeRect(box, context.Px(1f), UiPalette.White(0.1f * appear));
        float valueSize = context.Px(15f);
        painter.PushClip(box.Deflate(new Thickness(context.Px(12f), 0f)));
        painter.DrawText(Theme.Mono, Value, new UiVec2(box.X + context.Px(12f), UiRowStyle.CenterY(Theme.Mono, valueSize, box)), valueSize, UiPalette.Fade(UiPalette.TextBody, appear));
        painter.PopClip();
    }

    // The value's field, left of the button: its design width, or less on a narrow row, which keeps at
    // least half of the room beside the button for the caption.
    private UiRect FieldRect(LayoutContext context, UiRect button, float top, float height)
    {
        float room = button.X - context.Px(8f) - (Bounds.X + context.Px(UiRowStyle.Inset));
        float width = MathF.Max(0f, MathF.Min(context.Px(FieldWidth), room * 0.5f));
        return new UiRect(button.X - context.Px(8f) - width, top, width, height);
    }
}
