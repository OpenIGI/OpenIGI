using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// A selectable summary card for one player profile: the initial in a square, the name over its difficulty
/// and current mission, and — on the right — ACTIVE for the one in use and a segment per mission unlocked.
/// </summary>
public sealed class ProfileCard : Control
{
    private string _name = string.Empty;
    private string _initial = "?";
    private string _meta = string.Empty;
    private string _upperName = string.Empty;
    private string? _metaDifficulty;
    private string? _metaMission;

    /// <summary>Initializes a new instance of the <see cref="ProfileCard"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="name">The profile's name.</param>
    public ProfileCard(UiTheme theme, string name)
        : base(theme) => Name = name;

    /// <summary>Gets or sets the profile's name.</summary>
    public string Name
    {
        get => _name;
        set
        {
            _name = value ?? string.Empty;
            _upperName = _name.ToUpperInvariant();
            _initial = _name.Length > 0 ? _name[..1].ToUpperInvariant() : "?";
        }
    }

    /// <summary>Gets or sets the profile's difficulty, as a phrase such as "Hard difficulty".</summary>
    public string Difficulty { get; set; } = string.Empty;

    /// <summary>Gets or sets the profile's current mission.</summary>
    public string Mission { get; set; } = string.Empty;

    /// <summary>Gets or sets the profile's campaign progress as a phrase.</summary>
    public string Progress { get; set; } = string.Empty;

    /// <summary>Gets or sets how many missions the profile has unlocked.</summary>
    public int Unlocked { get; set; }

    /// <summary>Gets or sets how many missions the campaign has.</summary>
    public int Total { get; set; }

    /// <summary>Gets or sets a value indicating whether this is the profile in use.</summary>
    public bool Active { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(available.X, context.Px(ShellSizes.IsCompact(Theme) ? 64f : 80f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);

        // The active one washed with the accent and barred down its leading edge; the one in focus ringed.
        painter.FillRect(Bounds, UiPalette.Fade(Bgra32.FromRgba(12, 14, 15, 214), appear));
        if (Active)
        {
            painter.FillRect(Bounds, UiPalette.AccentAt(0.13f * appear));
            painter.FillRect(new UiRect(Bounds.X, Bounds.Y, context.Px(4f), Bounds.Height), UiPalette.Fade(Theme.Accent, appear));
        }

        Bgra32 border = Active ? UiPalette.AccentAt(0.45f) : Interpolate.Lerp(UiPalette.White(0.12f), UiPalette.White(0.42f), hot);
        painter.StrokeRect(Bounds, context.Px(1f), UiPalette.Fade(border, appear));
        if (Visual.Focus > 0.001f)
        {
            painter.StrokeRect(Bounds, context.Px(2f), UiPalette.Fade(Theme.AccentHover, Visual.Focus * appear));
        }

        // The initial, in a square outlined like the card.
        float square = context.Px(48f);
        var box = new UiRect(Bounds.X + context.Px(20f), Bounds.Center.Y - (square * 0.5f), square, square);
        painter.StrokeRect(box, context.Px(1f), UiPalette.Fade(Active ? Theme.Accent : Theme.Border, appear));
        float initialSize = context.Px(22f);
        float initialWidth = Theme.Display.MeasureWidth(_initial, initialSize);
        painter.DrawText(Theme.Display, _initial, new UiVec2(box.Center.X - (initialWidth * 0.5f), UiRowStyle.CapsCenterY(Theme.Display, initialSize, box)), initialSize, UiPalette.Fade(Active ? Theme.Accent : Theme.TextMuted, appear));

        // The right-hand column first, so the name knows how much room it has.
        float right = Bounds.Right - context.Px(20f);
        float segmentsWidth = KitDraw.SegmentsWidth(context, Total, 14f, 3f);
        float column = segmentsWidth;
        if (Active)
        {
            float pillWidth = UiPainter.MeasureLabel(Theme.Mono, "ACTIVE", context.Px(10f), 0.22f) + context.Px(16f);
            column = MathF.Max(column, pillWidth);
        }

        float nameSize = context.Px(24f);
        float metaSize = context.Px(15f);
        float textX = box.Right + context.Px(18f);
        float block = (nameSize * 0.7f) + context.Px(8f) + Theme.Body.LineHeight(metaSize);
        float top = Bounds.Center.Y - (block * 0.5f);
        painter.PushClip(UiRect.FromEdges(textX, Bounds.Y, right - column - context.Px(18f), Bounds.Bottom));
        var nameBox = new UiRect(textX, top, 0f, nameSize * 0.7f);
        painter.DrawLabel(Theme.Heading, _upperName, new UiVec2(textX, UiRowStyle.CapsCenterY(Theme.Heading, nameSize, nameBox)), nameSize, UiPalette.Fade(Interpolate.Lerp(Theme.Text, Theme.TextBright, hot), appear), 0.06f);
        if (!ReferenceEquals(_metaDifficulty, Difficulty) || !ReferenceEquals(_metaMission, Mission))
        {
            _metaDifficulty = Difficulty;
            _metaMission = Mission;
            _meta = $"{Difficulty} · {Mission}";
        }

        painter.DrawText(Theme.Body, _meta, new UiVec2(textX, top + (nameSize * 0.7f) + context.Px(8f)), metaSize, UiPalette.Fade(Theme.TextSecondary, appear));
        painter.PopClip();

        float rightBlock = (Active ? Theme.Mono.LineHeight(context.Px(10f)) + context.Px(6f + 8f) : 0f) + context.Px(3f);
        float rightTop = Bounds.Center.Y - (rightBlock * 0.5f);
        if (Active)
        {
            UiRect pill = KitDraw.Pill(painter, Theme, "ACTIVE", new UiVec2(right, rightTop), Theme.Accent, Theme.OnAccent);
            rightTop = pill.Bottom + context.Px(8f);
        }

        KitDraw.Segments(painter, new UiVec2(right - segmentsWidth, rightTop), Unlocked, Total, 14f, 3f, 3f, UiPalette.Fade(Theme.Accent, appear));
    }
}
