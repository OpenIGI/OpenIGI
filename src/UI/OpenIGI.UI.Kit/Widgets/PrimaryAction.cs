using OpenIGI.Core.Graphics;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>
/// The screen's one primary action — PLAY, RESUME, UPDATE: a big amber block with a corner cut off, the
/// verb large over a small monospaced line, and an arrow in a box at the far end.
/// </summary>
/// <remarks>
/// <para>
/// While <see cref="Progress"/> is set the block becomes a progress bar: the amber drops to a translucent
/// wash, a solid fill grows across it, and the verb is replaced by the percentage — the launcher's
/// downloading state, and any long action the game wants to show in place.
/// </para>
/// <para>
/// Focus and hover lighten the amber, which is the design's only hover state for it; a disabled action
/// greys out entirely, because a dimmed amber still reads as pressable.
/// </para>
/// </remarks>
public class PrimaryAction : Control
{
    private readonly Spring _progress = new(0f, smoothTime: 0.12f);
    private string _title;

    /// <summary>Initializes a new instance of the <see cref="PrimaryAction"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="title">The verb, shown in capitals.</param>
    public PrimaryAction(UiTheme theme, string title)
        : base(theme)
    {
        _title = (title ?? string.Empty).ToUpperInvariant();
    }

    /// <summary>Gets or sets the verb.</summary>
    public string Title
    {
        get => _title;
        set => _title = (value ?? string.Empty).ToUpperInvariant();
    }

    /// <summary>Gets or sets the small line under the verb.</summary>
    public string Subtitle { get; set; } = string.Empty;

    /// <summary>Gets or sets the size of the line under the verb, in design units.</summary>
    public float SubtitleSize { get; set; } = 10f;

    /// <summary>Gets or sets the verb's size, in design units.</summary>
    public float TitleSize { get; set; } = 40f;

    /// <summary>Gets or sets the length cut from the bottom-right corner, in design units.</summary>
    public float CutCorner { get; set; } = 22f;

    /// <summary>Gets or sets the progress shown in place of the verb, 0 to 1, or <see langword="null"/> when idle.</summary>
    public float? Progress { get; set; }

    /// <summary>Gets or sets the glyph in the box at the end: an arrow when idle.</summary>
    public PrimaryActionIcon Icon { get; set; } = PrimaryActionIcon.Arrow;

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        if (Progress is { } p)
        {
            _progress.Target = Math.Clamp(p, 0f, 1f);
        }

        _progress.Update(elapsedSeconds);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(context.Px(380f), context.Px(96f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
        float cut = context.Px(CutCorner);
        UiRect bounds = Bounds;
        bool busy = Progress is not null;

        if (busy)
        {
            painter.FillCutRect(bounds, cut, UiCorners.BottomRight, UiPalette.AccentAt(0.35f * appear));
            painter.PushClip(new UiRect(bounds.X, bounds.Y, bounds.Width * _progress.Value, bounds.Height));
            painter.FillCutRect(bounds, cut, UiCorners.BottomRight, UiPalette.Fade(Theme.Accent, appear));
            painter.PopClip();
        }
        else
        {
            Bgra32 fill = !IsEnabled ? UiPalette.White(0.12f) : Interpolate.Lerp(Theme.Accent, Theme.AccentHover, hot);
            painter.FillCutRect(bounds, cut, UiCorners.BottomRight, UiPalette.Fade(fill, appear));
        }

        Bgra32 ink = UiPalette.Fade(IsEnabled ? Theme.OnAccent : Theme.TextDisabled, appear);
        float titleSize = context.Px(TitleSize);
        float subSize = context.Px(SubtitleSize);
        float gap = context.Px(6f);
        float capHeight = titleSize * 0.9f;
        bool hasSub = Subtitle.Length > 0;
        float block = capHeight + (hasSub ? gap + Theme.Mono.LineHeight(subSize) : 0f);
        float x = bounds.X + context.Px(34f);
        float top = bounds.Center.Y - (block * 0.5f);

        string title = busy ? Percent(_progress.Value) : _title;
        var titleBox = new UiRect(x, top, bounds.Width, capHeight);
        painter.DrawLabel(Theme.Display, title, new UiVec2(x, UiRowStyle.CapsCenterY(Theme.Display, titleSize, titleBox)), titleSize, ink, 0.2f);
        if (hasSub)
        {
            painter.DrawLabel(Theme.Mono, Subtitle, new UiVec2(x, top + capHeight + gap), subSize, UiPalette.Fade(ink, 0.7f), 0.22f);
        }

        // The arrow box: a dark hairline square holding the glyph, set against the right padding.
        float boxSize = context.Px(48f);
        var box = new UiRect(bounds.Right - context.Px(30f) - boxSize, bounds.Center.Y - (boxSize * 0.5f), boxSize, boxSize);
        painter.StrokeRect(box, context.Px(1.5f), UiPalette.Fade(UiPalette.Ink.WithAlpha(204), appear));
        DrawIcon(painter, box, busy ? PrimaryActionIcon.Pause : Icon, ink);
    }

    private static string Percent(float value) => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{MathF.Round(value * 100f):0}%");

    private static void DrawIcon(UiPainter painter, UiRect box, PrimaryActionIcon icon, Bgra32 ink)
    {
        LayoutContext context = painter.Context;
        UiVec2 c = box.Center;
        float s = context.Px(10f);
        float stroke = context.Px(2.4f);
        switch (icon)
        {
            case PrimaryActionIcon.Pause:
                painter.FillRect(new UiRect(c.X - (s * 0.55f), c.Y - (s * 0.7f), s * 0.35f, s * 1.4f), ink);
                painter.FillRect(new UiRect(c.X + (s * 0.2f), c.Y - (s * 0.7f), s * 0.35f, s * 1.4f), ink);
                break;
            case PrimaryActionIcon.Play:
            {
                Span<UiVec2> play = [new(c.X - (s * 0.5f), c.Y - (s * 0.7f)), new(c.X + (s * 0.7f), c.Y), new(c.X - (s * 0.5f), c.Y + (s * 0.7f))];
                painter.FillPolygon(play, ink);
                break;
            }

            case PrimaryActionIcon.Download:
                painter.StrokeLine(new UiVec2(c.X, c.Y - s), new UiVec2(c.X, c.Y + (s * 0.4f)), stroke, ink);
                painter.DrawChevron(new UiVec2(c.X, c.Y + (s * 0.25f)), s * 1.2f, UiDirection.Down, stroke, ink);
                break;
            default:
                painter.StrokeLine(new UiVec2(c.X - s, c.Y), new UiVec2(c.X + (s * 0.9f), c.Y), stroke, ink);
                painter.DrawChevron(new UiVec2(c.X + (s * 0.55f), c.Y), s * 1.3f, UiDirection.Right, stroke, ink);
                break;
        }
    }
}

/// <summary>The glyph in a <see cref="PrimaryAction"/>'s box.</summary>
public enum PrimaryActionIcon
{
    /// <summary>An arrow pointing right: go.</summary>
    Arrow,

    /// <summary>A play triangle: launch.</summary>
    Play,

    /// <summary>A downward arrow: download or update.</summary>
    Download,

    /// <summary>Two bars: pause, shown automatically while busy.</summary>
    Pause,
}
