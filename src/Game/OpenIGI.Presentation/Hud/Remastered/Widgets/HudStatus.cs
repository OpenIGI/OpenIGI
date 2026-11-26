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
        context.Report(HudElement.Vitals, panel);
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
            DrawVisibility(context, new UiRect(x, gaugeTop + context.Px(18f), meterWidth, gaugeHeight - context.Px(18f)), visibility, model.VisibilityAlert, model.IsSeen);
            x += meterWidth + context.Px(8f);
            if (model.IsSeen)
            {
                // Seen: an open eye in the header's corner, level with the HP label.
                float half = context.Px(8f);
                float blink = 0.75f + (0.25f * context.Reactive.AlertPulse);
                UiVec2 eye = new(panel.Right - pad - half, top + context.Px(6f));
                HudPictograms.Eye(context.Painter, eye, half, MathF.Max(1f, context.Px(1.5f)), HudDrawContext.Fade(palette.Danger, blink));
            }
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

        // A planted bomb's share of the bar: the segments it would take flash, the whole lit bar where it would kill.
        (float riskFrom, float riskTo) = Hud.HudBlastRisk.Span(health);
        float riskPulse = Hud.HudBlastRisk.Pulse(context.Seconds, Hud.HudBlastRisk.IsLethal(health));
        for (int i = 0; i < Segments; i++)
        {
            float from = (float)i / Segments;
            float y = rect.Bottom - ((i + 1) * segment) - (i * gap);
            UiRect cell = new(rect.X, y, rect.Width, segment);
            float middle = from + (0.5f / Segments);
            Bgra32 colour = riskTo > riskFrom && middle >= riskFrom && from < riskTo ? HudDrawContext.Fade(palette.Danger, riskPulse)
                : from < health ? HudDrawContext.Fade(lit, breathe)
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
    private static void DrawVisibility(HudDrawContext context, UiRect rect, float visibility, float alert, bool seen)
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

        // Seen: not the colour alone -- the meter's label turns to the word, and an eye opens in the header.
        string label = seen ? "SEEN" : "VIS";
        Bgra32 ink = seen ? HudDrawContext.Fade(palette.Danger, 0.75f + (0.25f * pulse)) : palette.Primary;
        context.Painter.DrawLabel(context.Fonts.Value, label, new UiVec2(rect.X - context.Px(4f), rect.Y - context.Px(17f)), context.Px(12f), ink, 0.08f);
    }
}
