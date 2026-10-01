using OpenIGI.Core.Graphics;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>
/// The header's profile button: OPERATIVE over the active profile's name, right-aligned, beside a square
/// holding its initial.
/// </summary>
public class OperativeBadge : Control
{
    private string _name = string.Empty;
    private string _initial = string.Empty;

    /// <summary>Initializes a new instance of the <see cref="OperativeBadge"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="name">The active profile's name.</param>
    public OperativeBadge(UiTheme theme, string name)
        : base(theme)
    {
        Name = name;
    }

    /// <summary>Gets or sets the active profile's name.</summary>
    public string Name
    {
        get => _name;
        set
        {
            _name = (value ?? string.Empty).ToUpperInvariant();
            _initial = _name.Length > 0 ? _name[..1] : "?";
        }
    }

    /// <summary>Gets or sets the caption over the name.</summary>
    public string Caption { get; set; } = "OPERATIVE";

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float text = MathF.Max(
            UiPainter.MeasureLabel(Theme.Mono, Caption, context.Px(10f), 0.3f),
            UiPainter.MeasureLabel(Theme.Heading, _name, context.Px(21f), 0.16f));
        return new UiVec2(text + context.Px(14f + 36f), context.Px(36f));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
        float square = context.Px(36f);
        var box = new UiRect(Bounds.Right - square, Bounds.Center.Y - (square * 0.5f), square, square);

        painter.FillRect(box, UiPalette.AccentAt(0.08f * appear));
        painter.StrokeRect(box, context.Px(1f), UiPalette.Fade(Theme.Accent, appear));
        float initialSize = context.Px(18f);
        float initialWidth = Theme.Display.MeasureWidth(_initial, initialSize);
        painter.DrawText(Theme.Display, _initial, new UiVec2(box.Center.X - (initialWidth * 0.5f), UiRowStyle.CapsCenterY(Theme.Display, initialSize, box)), initialSize, UiPalette.Fade(Theme.Accent, appear));

        float right = box.X - context.Px(14f);
        float captionSize = context.Px(10f);
        float nameSize = context.Px(21f);
        float block = Theme.Mono.LineHeight(captionSize) + context.Px(4f) + nameSize;
        float top = Bounds.Center.Y - (block * 0.5f);
        float captionWidth = UiPainter.MeasureLabel(Theme.Mono, Caption, captionSize, 0.3f);
        painter.DrawLabel(Theme.Mono, Caption, new UiVec2(right - captionWidth, top), captionSize, UiPalette.Fade(Theme.TextMuted, appear), 0.3f);

        float nameWidth = UiPainter.MeasureLabel(Theme.Heading, _name, nameSize, 0.16f);
        var nameBox = new UiRect(right - nameWidth, top + Theme.Mono.LineHeight(captionSize) + context.Px(4f), nameWidth, nameSize);
        Bgra32 name = Interpolate.Lerp(Theme.Text, Theme.TextBright, hot);
        painter.DrawLabel(Theme.Heading, _name, new UiVec2(nameBox.X, UiRowStyle.CapsCenterY(Theme.Heading, nameSize, nameBox)), nameSize, UiPalette.Fade(name, appear), 0.16f);
    }
}
