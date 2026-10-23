using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Widgets;

/// <summary>
/// The status cluster at the lower left: the health gauge, the visibility meter and the stance, as both games stack them
/// there, on one pane of glass.
/// </summary>
/// <remarks>
/// The health gauge is a column of segments filled from the bottom, as both games fill their power bar; the health lost lately
/// stays lit behind it in the danger colour and falls away (<see cref="HudReactiveState.HealthTrail"/>), and under a quarter the
/// gauge breathes. The visibility meter beside it is a thinner column in the title's own ramp that pulses while the player can
/// be seen. The stance is the title's own figure, where it has one.
/// </remarks>
public static class HudStatus
{
    private const int Segments = 12;

    /// <summary>Draws the cluster, standing on its anchor's corner.</summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="model">The frame's model.</param>
    public static void Draw(HudDrawContext context, HudModel model)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        if ((model.Show & HudParts.Status) == 0 || context.Place(HudElement.Vitals) is not { } anchor)
        {
            return;
        }

        HudPalette palette = context.Palette;
        float gaugeWidth = context.Px(12f);
        float gaugeHeight = context.Px(86f);
        float meterWidth = context.Px(6f);
        bool stance = model.Stance != HudStance.None && context.Icons.TryMeasure(StanceIcon(model.Stance), out _, out _);
        float stanceWidth = stance ? context.Px(34f) : 0f;
        float pad = context.Px(10f);
        float labelHeight = context.Px(16f);

        float width = pad + gaugeWidth + (model.Visibility is null ? 0f : context.Px(8f) + meterWidth) + (stance ? context.Px(8f) + stanceWidth : 0f) + pad;
        float height = pad + labelHeight + gaugeHeight + context.Px(24f) + pad;
        UiRect panel = new(anchor.X, anchor.Y - height, width, height);
        HudPanel.Draw(context, panel, context.Reactive.Appear);

        float x = panel.X + pad;
        float top = panel.Y + pad;
        context.Painter.DrawLabel(context.Fonts.Value, "HP", new UiVec2(x, top - context.Px(2f)), context.Px(14f), palette.Primary, 0.1f);
        float gaugeTop = top + labelHeight;
        DrawHealth(context, new UiRect(x, gaugeTop, gaugeWidth, gaugeHeight));

        // The figure under the gauge: the health, in whole percent.
        string percent = ((int)MathF.Round(context.Reactive.Health * 100f)).ToString(CultureInfo.InvariantCulture);
        context.Painter.DrawLabel(context.Fonts.Value, percent, new UiVec2(x - context.Px(1f), gaugeTop + gaugeHeight + context.Px(2f)), context.Px(19f), palette.Health(context.Reactive.Health), 0.02f);

        x += gaugeWidth + context.Px(8f);
        if (model.Visibility is { } visibility)
        {
            DrawVisibility(context, new UiRect(x, gaugeTop + context.Px(18f), meterWidth, gaugeHeight - context.Px(18f)), visibility, model.VisibilityAlert);
            x += meterWidth + context.Px(8f);
        }

        if (stance)
        {
            HudIcon icon = StanceIcon(model.Stance);
            context.Icons.TryMeasure(icon, out float w, out float h);
            float s = MathF.Min(stanceWidth / MathF.Max(1f, w), gaugeHeight / MathF.Max(1f, h));
            context.DrawIcon(icon, x + ((stanceWidth - (w * s)) * 0.5f), gaugeTop + gaugeHeight - (h * s), s, palette.Primary);
        }
    }

    /// <summary>The title's figure for a stance.</summary>
    /// <param name="stance">The stance.</param>
    /// <returns>Its picture's name.</returns>
    public static HudIcon StanceIcon(HudStance stance) => new(stance switch
    {
        HudStance.Running => "stance_running.spr",
        HudStance.Crouching => "stance_crouching.spr",
        HudStance.Prone => "stance_lying.spr",
        HudStance.Flying => "stance_flying.spr",
        _ => "stance_standing.spr",
    });

    // The gauge: a column of segments lit from the bottom, the lost health trailing behind in the danger colour.
    private static void DrawHealth(HudDrawContext context, UiRect rect)
    {
        HudPalette palette = context.Palette;
        float health = context.Reactive.Health;
        float trail = context.Reactive.HealthTrail;
        float gap = MathF.Max(1f, context.Px(2f));
        float segment = (rect.Height - (gap * (Segments - 1))) / Segments;
        Bgra32 lit = palette.Health(health);
        float breathe = health < 0.25f ? 0.65f + (0.35f * MathF.Sin(context.Seconds * 6f)) : 1f;
        for (int i = 0; i < Segments; i++)
        {
            float from = (float)i / Segments;
            float y = rect.Bottom - ((i + 1) * segment) - (i * gap);
            UiRect cell = new(rect.X, y, rect.Width, segment);
            Bgra32 colour = from < health ? HudDrawContext.Fade(lit, breathe)
                : from < trail ? HudDrawContext.Fade(palette.Danger, 0.85f)
                : HudDrawContext.Fade(palette.Dim, 0.55f);
            context.Painter.FillRect(cell, colour);
        }

        // A bright cap on the top lit segment.
        int top = Math.Clamp((int)MathF.Ceiling(health * Segments) - 1, 0, Segments - 1);
        if (health > 0f)
        {
            float y = rect.Bottom - ((top + 1) * segment) - (top * gap);
            context.Painter.FillRect(new UiRect(rect.X - context.Px(2f), y, rect.Width + context.Px(4f), MathF.Max(1f, context.Px(1.5f))), HudDrawContext.Fade(palette.Highlight, breathe));
        }
    }

    // The meter: a thin column in the title's ramp, with the warning level marked, pulsing once over it.
    private static void DrawVisibility(HudDrawContext context, UiRect rect, float visibility, float alert)
    {
        HudPalette palette = context.Palette;
        float v = Math.Clamp(visibility, 0f, 1f);
        context.Painter.FillRect(rect, HudDrawContext.Fade(palette.Dim, 0.45f));
        float filled = rect.Height * v;
        Bgra32 colour = palette.Visibility(v);
        context.Painter.FillRect(new UiRect(rect.X, rect.Bottom - filled, rect.Width, filled), colour);

        float mark = rect.Bottom - (rect.Height * alert);
        context.Painter.FillRect(new UiRect(rect.X - context.Px(3f), mark, rect.Width + context.Px(6f), MathF.Max(1f, context.Px(1f))), HudDrawContext.Fade(palette.Danger, 0.8f));

        float pulse = context.Reactive.AlertPulse;
        if (pulse > 0f)
        {
            float grow = context.Px(6f) * (1f - pulse);
            context.Painter.StrokeRect(
                new UiRect(rect.X - grow, rect.Y - grow, rect.Width + (grow * 2f), rect.Height + (grow * 2f)),
                MathF.Max(1f, context.Px(1.5f)),
                HudDrawContext.Fade(palette.Danger, pulse));
        }

        context.Painter.DrawLabel(context.Fonts.Value, "VIS", new UiVec2(rect.X - context.Px(4f), rect.Y - context.Px(17f)), context.Px(12f), palette.Primary, 0.08f);

        // Seen: not the colour alone -- an open eye and the word, so it reads whatever the eye makes of red.
        if (v >= alert)
        {
            float blink = 0.75f + (0.25f * pulse);
            UiVec2 eye = new(rect.X + (rect.Width * 0.5f), rect.Y - context.Px(30f));
            DrawEye(context, eye, context.Px(9f), HudDrawContext.Fade(palette.Danger, blink));
            context.Painter.DrawLabel(context.Fonts.Value, "SEEN", new UiVec2(eye.X + context.Px(12f), eye.Y - context.Px(8f)), context.Px(14f), HudDrawContext.Fade(palette.Danger, blink), 0.08f);
        }
    }

    // An open eye: an almond outline round a pupil.
    private static void DrawEye(HudDrawContext context, UiVec2 centre, float half, Bgra32 colour)
    {
        Span<UiVec2> almond = stackalloc UiVec2[12];
        for (int i = 0; i < 6; i++)
        {
            float t = (float)i / 5;
            float x = centre.X - half + (2f * half * t);
            float lift = MathF.Sin(t * MathF.PI) * half * 0.55f;
            almond[i] = new UiVec2(x, centre.Y - lift);
            almond[11 - i] = new UiVec2(x, centre.Y + lift);
        }

        context.Painter.StrokePolygon(almond, MathF.Max(1.5f, context.Px(1.5f)), colour);
        float r = half * 0.32f;
        context.Painter.FillRect(new UiRect(centre.X - r, centre.Y - r, r * 2f, r * 2f), colour);
    }
}
