using OpenIGI.Core.Graphics;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
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

    /// <summary>Gets or sets the line under the title: what the action will do, in a few words.</summary>
    public string Subtitle { get; set; } = string.Empty;

    /// <summary>Gets or sets the subtitle's size, in design units.</summary>
    public float SubtitleSize { get; set; } = 14f;

    /// <summary>Gets or sets the title's size, in design units.</summary>
    public float TitleSize { get; set; } = 28f;

    /// <summary>Gets or sets the size of the cut across the bottom-right corner, in design units.</summary>
    public float CutCorner { get; set; } = 16f;

    /// <summary>Gets or sets the padding either side of the caption and the arrow, in design units.</summary>
    public float SidePadding { get; set; } = 28f;

    /// <summary>Gets or sets the key that does this, shown on a cap before the arrow; empty for none.</summary>
    /// <remarks>A touch screen has no keys: the page leaves it empty there.</remarks>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether this is the page's second action: outlined, not filled.</summary>
    public bool Secondary { get; set; }

    /// <summary>Gets or sets how far a running action has got, 0..1; <see langword="null"/> when it is not running.</summary>
    public float? Progress { get; set; }

    /// <summary>Gets or sets the glyph at the right.</summary>
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
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        if (!Theme.Large)
        {
            return new UiVec2(context.Px(380f), context.Px(96f));
        }

        // As wide as its caption and what stands at its right, unless given a width.
        float title = UiPainter.MeasureLabel(Theme.Display, _title, context.Px(TitleSize), TitleTracking);
        float sub = Subtitle.Length > 0 ? UiPainter.MeasureLabel(Theme.Heading, Subtitle, context.Px(SubtitleSize), SubtitleTracking) : 0f;
        float right = Icon == PrimaryActionIcon.None && Key.Length == 0 ? 0f : context.Px(24f) + RightWidth(context);
        return new UiVec2(MathF.Max(title, sub) + right + context.Px(SidePadding * 2f), context.Px(88f));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        if (!Theme.Large)
        {
            DrawClassic(painter);
            return;
        }

        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
        float cut = context.Px(CutCorner);
        UiRect bounds = Bounds;
        bool busy = Progress is not null;
        Bgra32 ink;

        if (Secondary)
        {
            painter.FillRect(bounds, UiPalette.InkAt((0.35f + (0.2f * hot)) * appear));
            Bgra32 border = !IsEnabled ? UiPalette.White(0.14f) : Interpolate.Lerp(UiPalette.White(0.42f), Theme.Accent, hot);
            painter.StrokeRect(bounds, context.Px(1f), UiPalette.Fade(border, appear));
            ink = UiPalette.Fade(IsEnabled ? Theme.Text : Theme.TextDisabled, appear);
        }
        else if (busy)
        {
            painter.FillCutRect(bounds, cut, UiCorners.BottomRight, UiPalette.AccentAt(0.35f * appear));
            painter.PushClip(new UiRect(bounds.X, bounds.Y, bounds.Width * _progress.Value, bounds.Height));
            painter.FillCutRect(bounds, cut, UiCorners.BottomRight, UiPalette.Fade(Theme.Accent, appear));
            painter.PopClip();
            ink = UiPalette.Fade(Theme.OnAccent, appear);
        }
        else if (!IsEnabled)
        {
            // Not to be taken for the accent gone wrong: a plain dark plate with an outline, its caption still readable.
            painter.FillCutRect(bounds, cut, UiCorners.BottomRight, UiPalette.InkAt(0.55f * appear));
            painter.StrokeCutRect(bounds, cut, UiCorners.BottomRight, context.Px(1f), UiPalette.White(0.22f * appear));
            ink = UiPalette.Fade(Theme.TextMuted, appear);
        }
        else
        {
            painter.FillCutRect(bounds, cut, UiCorners.BottomRight, UiPalette.Fade(Interpolate.Lerp(Theme.Accent, Theme.AccentHover, hot), appear));
            ink = UiPalette.Fade(Theme.OnAccent, appear);
        }

        string title = busy ? Percent(_progress.Value) : _title;
        float pad = context.Px(SidePadding);
        float x = bounds.X + pad;
        float right = bounds.Right - pad;

        // The arrow, and before it the key that does the same.
        PrimaryActionIcon icon = busy ? PrimaryActionIcon.Pause : Icon;
        if (icon != PrimaryActionIcon.None)
        {
            float glyph = context.Px(IconSize);
            DrawIcon(painter, new UiRect(right - glyph, bounds.Center.Y - (glyph * 0.5f), glyph, glyph), icon, ink);
            right -= glyph + context.Px(12f);
        }

        if (Key.Length > 0 && IsEnabled && !busy)
        {
            UiVec2 cap = KitDraw.MeasureKeyCap(context, Theme, Key);
            KitDraw.KeyCap(painter, Theme, Key, new UiVec2(right - cap.X, bounds.Center.Y - (cap.Y * 0.5f)), Secondary ? KeyCapStyle.Footer : KeyCapStyle.OnAccent);
            right -= cap.X + context.Px(12f);
        }

        // The title has the room up to those; a longer one is set smaller to fit rather than running under them.
        float titleSize = context.Px(TitleSize);
        float room = right - x;
        float wide = UiPainter.MeasureLabel(Theme.Display, title, titleSize, TitleTracking);
        if (wide > room && room > 0f)
        {
            titleSize *= room / wide;
        }

        float subSize = context.Px(SubtitleSize);
        float capHeight = titleSize * 0.72f;
        bool hasSub = Subtitle.Length > 0;
        float gap = context.Px(8f);
        float block = capHeight + (hasSub ? gap + (subSize * 0.72f) : 0f);
        float top = bounds.Center.Y - (block * 0.5f);

        var titleBox = new UiRect(x, top, bounds.Width, capHeight);
        painter.DrawLabel(Theme.Display, title, new UiVec2(x, UiRowStyle.CapsCenterY(Theme.Display, titleSize, titleBox)), titleSize, ink, TitleTracking);
        if (hasSub)
        {
            var subBox = new UiRect(x, top + capHeight + gap, bounds.Width, subSize * 0.72f);
            string sub = TextFit.ToWidth(Theme.Heading, Subtitle, subSize, room);
            painter.DrawLabel(Theme.Heading, sub, new UiVec2(x, UiRowStyle.CapsCenterY(Theme.Heading, subSize, subBox)), subSize, UiPalette.Fade(ink, 0.72f), SubtitleTracking);
        }
    }

    // The look before the large one: a taller plate, the title at 40, the arrow in a box of its own.
    private void DrawClassic(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
        float cut = context.Px(ClassicCut);
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
        string title = busy ? Percent(_progress.Value) : _title;
        float x = bounds.X + context.Px(34f);

        // The arrow box: a dark hairline square holding the glyph, set against the right padding.
        float boxSize = context.Px(48f);
        var box = new UiRect(bounds.Right - context.Px(30f) - boxSize, bounds.Center.Y - (boxSize * 0.5f), boxSize, boxSize);

        // The title has the room up to the box; a longer one is set smaller to fit it rather than running under the arrow.
        float titleSize = context.Px(ClassicTitle);
        float room = box.X - context.Px(16f) - x;
        float wide = UiPainter.MeasureLabel(Theme.Display, title, titleSize, 0.2f);
        if (wide > room && room > 0f)
        {
            titleSize *= room / wide;
        }

        float subSize = context.Px(10f);
        float gap = context.Px(6f);
        float capHeight = titleSize * 0.9f;
        bool hasSub = Subtitle.Length > 0;
        float block = capHeight + (hasSub ? gap + Theme.Mono.LineHeight(subSize) : 0f);
        float top = bounds.Center.Y - (block * 0.5f);

        var titleBox = new UiRect(x, top, bounds.Width, capHeight);
        painter.DrawLabel(Theme.Display, title, new UiVec2(x, UiRowStyle.CapsCenterY(Theme.Display, titleSize, titleBox)), titleSize, ink, 0.2f);
        if (hasSub)
        {
            painter.DrawLabel(Theme.Mono, Subtitle, new UiVec2(x, top + capHeight + gap), subSize, UiPalette.Fade(ink, 0.7f), 0.22f);
        }

        painter.StrokeRect(box, context.Px(1.5f), UiPalette.Fade(UiPalette.Ink.WithAlpha(204), appear));
        DrawIcon(painter, new UiRect(box.Center.X - context.Px(12f), box.Center.Y - context.Px(12f), context.Px(24f), context.Px(24f)), busy ? PrimaryActionIcon.Pause : Icon, ink);
    }

    // The small look's title and cut, in design units; a page that set its own keeps them.
    private float ClassicTitle => TitleSize == 28f ? 40f : TitleSize;

    private float ClassicCut => CutCorner == 16f ? 22f : CutCorner;

    // Design units and ems.
    private const float TitleTracking = 0.16f;
    private const float SubtitleTracking = 0.12f;
    private const float IconSize = 24f;

    private float RightWidth(LayoutContext context)
    {
        float width = Icon == PrimaryActionIcon.None ? 0f : context.Px(IconSize);
        if (Key.Length > 0)
        {
            width += KitDraw.MeasureKeyCap(context, Theme, Key).X + context.Px(12f);
        }

        return width;
    }

    private static string Percent(float value) => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{MathF.Round(value * 100f):0}%");

    private static void DrawIcon(UiPainter painter, UiRect box, PrimaryActionIcon icon, Bgra32 ink)
    {
        LayoutContext context = painter.Context;
        UiVec2 c = box.Center;
        float s = box.Width * 0.42f;
        float stroke = MathF.Max(1f, box.Width * 0.09f);
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
    /// <summary>No glyph: the caption alone.</summary>
    None = -1,

    /// <summary>An arrow pointing right: go.</summary>
    Arrow,

    /// <summary>A play triangle: launch.</summary>
    Play,

    /// <summary>A downward arrow: download or update.</summary>
    Download,

    /// <summary>Two bars: pause, shown automatically while busy.</summary>
    Pause,
}
