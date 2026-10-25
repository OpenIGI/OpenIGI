using OpenIGI.Core.Graphics;
using OpenIGI.Game.Ai;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Widgets;

/// <summary>
/// One small line at the top of the screen saying how far the soldiers are onto the player, in the title's own terms:
/// SEARCHING, SUSPICIOUS, ALARM, DETECTED. Nothing is drawn over the soldiers themselves.
/// </summary>
/// <remarks>
/// The word sits between two short rules over a faint dark band, in the colour its status calls for: amber while they search,
/// pale while they wonder, the theme's danger for an alarm or a sighting. A new status comes up with a flash -- DETECTED with
/// a slice of it slipping now and then, ALARM with its mark blinking -- and one cleared lingers a moment, fading
/// (<see cref="HudReactiveState.BannerOpacity"/>).
/// </remarks>
public static class HudAlertBanner
{
    /// <summary>How far down the screen the banner stands, in design pixels.</summary>
    public const float Top = 54f;

    private static readonly Bgra32 Pale = Bgra32.FromRgba(236, 240, 245, 255);
    private static readonly Bgra32 Amber = Bgra32.FromRgba(255, 170, 60, 255);

    /// <summary>The word a status is told by, or empty for none.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The word.</returns>
    public static string Word(PlayerAlertStatus status) => status switch
    {
        PlayerAlertStatus.Searching => "SEARCHING",
        PlayerAlertStatus.Suspicious => "SUSPICIOUS",
        PlayerAlertStatus.Alarm => "ALARM",
        PlayerAlertStatus.Detected => "DETECTED",
        _ => string.Empty,
    };

    /// <summary>Draws the banner, while there is a status to tell or one is fading.</summary>
    /// <param name="context">The frame's drawing.</param>
    public static void Draw(HudDrawContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        HudReactiveState reactive = context.Reactive;
        float opacity = reactive.BannerOpacity * reactive.Appear;
        string word = Word(reactive.Banner);
        if (opacity <= 0.01f || word.Length == 0)
        {
            return;
        }

        PlayerAlertStatus status = reactive.Banner;
        bool danger = status is PlayerAlertStatus.Alarm or PlayerAlertStatus.Detected;
        Bgra32 colour = danger ? context.Palette.Danger : status == PlayerAlertStatus.Searching ? Amber : Pale;
        float flash = reactive.BannerFlash;
        float size = context.Px(15f) * (1f + (0.08f * flash));
        float width = UiPainter.MeasureLabel(context.Fonts.Name, word, size, 0.32f);
        float cx = context.Width * 0.5f, y = context.Px(Top);

        // A faint dark band behind it, fading out at its ends, so it reads over sky as well as wall.
        float band = width + context.Px(150f);
        UiRect back = new(cx - (band * 0.5f), y - context.Px(5f), band, size + context.Px(10f));
        context.Painter.FillGradient(
            back,
            [
                new UiGradientStop(0f, HudDrawContext.Fade(Bgra32.Black, 0f)),
                new UiGradientStop(0.5f, HudDrawContext.Fade(Bgra32.Black, 0.35f * opacity)),
                new UiGradientStop(1f, HudDrawContext.Fade(Bgra32.Black, 0f)),
            ],
            UiGradientDirection.Horizontal);

        // The rules either side, and a small diamond before the word -- blinking for an alarm.
        float rule = context.Px(36f), gap = context.Px(14f), mid = y + (size * 0.55f);
        float thin = MathF.Max(2f, context.Px(1.6f));
        Bgra32 ruleColour = HudDrawContext.Fade(colour, 0.6f * opacity);
        context.Painter.FillRect(new UiRect(cx - (width * 0.5f) - gap - rule, mid, rule, thin), ruleColour);
        context.Painter.FillRect(new UiRect(cx + (width * 0.5f) + gap, mid, rule, thin), ruleColour);
        float blink = status == PlayerAlertStatus.Alarm ? (MathF.Sin(context.Seconds * 9f) > 0f ? 1f : 0.25f) : 1f;
        context.Painter.FillDiamond(new UiVec2(cx - (width * 0.5f) - (gap * 0.5f), mid), context.Px(3.5f), HudDrawContext.Fade(colour, blink * opacity));

        UiVec2 at = new(cx - (width * 0.5f), y);
        Bgra32 text = HudDrawContext.Fade(Mix(colour, Bgra32.White, 0.5f * flash), opacity);
        if (status == PlayerAlertStatus.Detected)
        {
            // A slice of it slips now and then, a faint copy left behind.
            float tick = MathF.Floor(context.Seconds * 16f);
            float slip = (Hash(tick) > 0.75f ? Hash(tick + 2.3f) - 0.5f : 0f) * context.Px(6f);
            context.Painter.DrawLabel(context.Fonts.Name, word, new UiVec2(at.X + slip + context.Px(1.5f), at.Y), size, HudDrawContext.Fade(colour, 0.35f * opacity), 0.32f);
            at = new UiVec2(at.X - slip, at.Y);
        }

        context.Painter.DrawLabel(context.Fonts.Name, word, at, size, text, 0.32f);
    }

    private static Bgra32 Mix(Bgra32 a, Bgra32 b, float t) => Bgra32.FromRgba(
        (byte)(a.R + ((b.R - a.R) * t)),
        (byte)(a.G + ((b.G - a.G) * t)),
        (byte)(a.B + ((b.B - a.B) * t)),
        a.A);

    // A steady pseudo-random number in [0, 1) for a number, so a flicker repeats the same on each frame of its tick.
    private static float Hash(float n)
    {
        float x = MathF.Sin(n * 12.9898f) * 43758.5453f;
        return x - MathF.Floor(x);
    }
}
