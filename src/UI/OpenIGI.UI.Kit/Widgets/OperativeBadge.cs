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

    /// <summary>Gets or sets the line under the name: the difficulty and how far the operative has got.</summary>
    public string Caption { get; set; } = string.Empty;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float text = MathF.Max(
            Theme.Mono.MeasureWidth(Caption, context.Px(CaptionSize)),
            UiPainter.MeasureLabel(Theme.Heading, _name, context.Px(NameSize), NameTracking));
        return new UiVec2(text + context.Px(Gap + Square), context.Px(Square));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
        float square = context.Px(Square);
        var box = new UiRect(Bounds.Right - square, Bounds.Center.Y - (square * 0.5f), square, square);

        painter.FillRect(box, UiPalette.InkAt(0.5f * appear));
        painter.StrokeRect(box, context.Px(1f), UiPalette.Fade(Theme.Accent, appear));
        float initialSize = context.Px(19f);
        float initialWidth = Theme.Display.MeasureWidth(_initial, initialSize);
        painter.DrawText(Theme.Display, _initial, new UiVec2(box.Center.X - (initialWidth * 0.5f), UiRowStyle.CapsCenterY(Theme.Display, initialSize, box)), initialSize, UiPalette.Fade(Theme.Accent, appear));

        float right = box.X - context.Px(Gap);
        float captionSize = context.Px(CaptionSize);
        float nameSize = context.Px(NameSize);
        bool hasCaption = Caption.Length > 0;
        float nameHeight = nameSize * 0.75f;
        float block = nameHeight + (hasCaption ? context.Px(5f) + Theme.Mono.LineHeight(captionSize) : 0f);
        float top = Bounds.Center.Y - (block * 0.5f);

        float nameWidth = UiPainter.MeasureLabel(Theme.Heading, _name, nameSize, NameTracking);
        var nameBox = new UiRect(right - nameWidth, top, nameWidth, nameHeight);
        Bgra32 name = Interpolate.Lerp(Theme.Text, Theme.TextBright, hot);
        painter.DrawLabel(Theme.Heading, _name, new UiVec2(nameBox.X, UiRowStyle.CapsCenterY(Theme.Heading, nameSize, nameBox)), nameSize, UiPalette.Fade(name, appear), NameTracking);
        if (hasCaption)
        {
            float captionWidth = Theme.Mono.MeasureWidth(Caption, captionSize);
            painter.DrawText(Theme.Mono, Caption, new UiVec2(right - captionWidth, nameBox.Bottom + context.Px(5f)), captionSize, UiPalette.Fade(Theme.TextSecondary, appear));
        }
    }

    // Design units.
    private const float Square = 44f;
    private const float Gap = 12f;
    private const float NameSize = 18f;
    private const float NameTracking = 0.12f;
    private const float CaptionSize = 13f;
}
