using OpenIGI.Core.Graphics;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Drawing;
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
        }
    }

    /// <summary>Gets or sets the line under the name: the difficulty and how far the operative has got.</summary>
    public string Caption { get; set; } = string.Empty;

    /// <summary>Gets or sets where the name is read from each frame, for a name that changes while the badge is up; none keeps <see cref="Name"/>.</summary>
    public Func<string>? NameSource { get; set; }

    /// <summary>Gets or sets the avatar drawn in the square in place of the initial, read each frame; <see langword="null"/> for the initial.</summary>
    public Func<UiPicture?>? Avatar { get; set; }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        if (NameSource?.Invoke() is { } name && !string.Equals(name, _name, StringComparison.OrdinalIgnoreCase))
        {
            Name = name;
        }
    }

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

        KitDraw.PlayerMark(painter, Theme, box, _name, Avatar?.Invoke(), signedIn: true, appear);

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
