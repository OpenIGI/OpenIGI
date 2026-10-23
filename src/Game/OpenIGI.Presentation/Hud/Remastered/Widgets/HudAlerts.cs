using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Widgets;

/// <summary>What the display says where the eye already is: who is noticing the player, and the magazine at the aim.</summary>
/// <remarks>
/// <para>
/// A threat is an arc on an ellipse round the middle of the screen, towards the soldier noticing: amber and short while they
/// only wonder, red, wider and pulsing as they come to see. Someone behind marks the bottom of the ellipse.
/// </para>
/// <para>
/// The magazine is a short arc of pips beside the aiming mark, where a shooter looks: it shows while the rounds change -- firing,
/// reloading -- and stays while they are low or spent, a reload sweeping it full.
/// </para>
/// </remarks>
public static class HudAlerts
{
    /// <summary>The most pips the arc at the aim shows; a larger magazine is shown as a share of them.</summary>
    public const int MaximumArcPips = 30;

    /// <summary>Draws the threats round the middle of the screen.</summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="model">The frame's model.</param>
    public static void DrawThreats(HudDrawContext context, HudModel model)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        if (model.Threats.Count == 0 || model.Show == HudParts.None)
        {
            return;
        }

        HudPalette palette = context.Palette;
        float cx = context.Width * 0.5f;
        float cy = context.Height * 0.5f;
        float rx = MathF.Min(context.Width * 0.36f, context.Px(330f));
        float ry = MathF.Min(context.Height * 0.36f, context.Px(250f));
        float thickness = MathF.Max(2f, context.Px(4f));
        foreach (HudThreat threat in model.Threats)
        {
            float a = Math.Clamp(threat.Awareness, 0f, 1f);
            if (a <= 0.02f)
            {
                continue;
            }

            float red = Math.Clamp((a - 0.4f) / 0.5f, 0f, 1f);
            float pulse = a > 0.75f ? 0.7f + (0.3f * MathF.Sin(context.Seconds * 10f)) : 1f;
            Bgra32 colour = Mix(palette.Accent, palette.Danger, red);
            colour = HudDrawContext.Fade(colour, (0.35f + (0.65f * a)) * pulse);

            // The arc: a few short quads along the ellipse, spanning more the more they have noticed.
            float span = (0.12f + (0.22f * a)) * 0.5f;
            const int Pieces = 6;
            for (int i = 0; i < Pieces; i++)
            {
                float b0 = threat.Bearing - span + (2f * span * i / Pieces);
                float b1 = threat.Bearing - span + (2f * span * (i + 1) / Pieces);
                UiVec2 o0 = OnEllipse(cx, cy, rx, ry, b0);
                UiVec2 o1 = OnEllipse(cx, cy, rx, ry, b1);
                UiVec2 i0 = OnEllipse(cx, cy, rx - thickness, ry - thickness, b0);
                UiVec2 i1 = OnEllipse(cx, cy, rx - thickness, ry - thickness, b1);
                context.Painter.FillQuad(i0, o0, o1, i1, colour);
            }

            // A pointed tick outward at its middle, so the arc reads as a direction.
            UiVec2 tip = OnEllipse(cx, cy, rx + context.Px(9f) * (0.5f + a), ry + context.Px(9f) * (0.5f + a), threat.Bearing);
            UiVec2 left = OnEllipse(cx, cy, rx, ry, threat.Bearing - (span * 0.35f));
            UiVec2 right = OnEllipse(cx, cy, rx, ry, threat.Bearing + (span * 0.35f));
            context.Painter.FillTriangle(left, tip, right, colour);
        }
    }

    /// <summary>Draws the magazine beside the aiming mark, while its rounds change or are low.</summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="model">The frame's model.</param>
    /// <param name="spread">How far the aiming mark stands open, in design pixels.</param>
    public static void DrawAmmoArc(HudDrawContext context, HudModel model, float spread)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        if ((model.Show & HudParts.Crosshair) == 0 || model.Weapon is not { Magazine: { } loaded, Capacity: > 0 } weapon)
        {
            return;
        }

        // Shown as the rounds change and for a while after, and for as long as they are low, spent or being reloaded.
        float show = context.Reactive.Presence(HudParts.Weapon, HudDensity.Minimal, model);
        if (show <= 0.01f)
        {
            return;
        }

        HudPalette palette = context.Palette;
        bool reloading = weapon.State == HudWeaponState.Reloading;
        bool low = HudReactiveState.LowRounds(weapon) || loaded == 0;
        int pips = Math.Min(weapon.Capacity, MaximumArcPips);
        int lit = weapon.Capacity <= MaximumArcPips ? loaded : (int)MathF.Ceiling((float)loaded * pips / weapon.Capacity);
        if (reloading)
        {
            // A reload sweeps the arc full and round again.
            lit = (int)((context.Seconds * 2.2f % 1f) * pips);
        }

        float cx = context.Width * 0.5f;
        float cy = context.Height * 0.5f;
        float radius = context.Px(spread + 22f);
        float size = MathF.Max(2f, context.Px(3f));
        const float From = -0.95f;
        const float To = 0.95f;
        Bgra32 on = reloading ? palette.Accent : low ? palette.Danger : palette.Primary;
        for (int i = 0; i < pips; i++)
        {
            // From the bottom of the arc up, the last round at the top, on the right of the mark.
            float t = pips == 1 ? 0.5f : (float)i / (pips - 1);
            float angle = To + ((From - To) * t);
            float x = cx + (MathF.Cos(angle) * radius);
            float y = cy + (MathF.Sin(angle) * radius);
            bool isLit = i < lit;
            Bgra32 colour = isLit ? HudDrawContext.Fade(on, show) : HudDrawContext.Fade(palette.Dim, 0.5f * show);
            context.Painter.FillRect(new UiRect(x - (size * 0.5f), y - (size * 0.5f), size, size), colour);
        }
    }

    /// <summary>A point on an ellipse round a centre, at a bearing clockwise from straight up.</summary>
    /// <param name="cx">The centre across.</param>
    /// <param name="cy">The centre down.</param>
    /// <param name="rx">The radius across.</param>
    /// <param name="ry">The radius down.</param>
    /// <param name="bearing">The bearing, in radians.</param>
    /// <returns>The point.</returns>
    public static UiVec2 OnEllipse(float cx, float cy, float rx, float ry, float bearing) =>
        new(cx + (MathF.Sin(bearing) * rx), cy - (MathF.Cos(bearing) * ry));

    private static Bgra32 Mix(Bgra32 a, Bgra32 b, float t) => Bgra32.FromRgba(
        (byte)(a.R + ((b.R - a.R) * t)),
        (byte)(a.G + ((b.G - a.G) * t)),
        (byte)(a.B + ((b.B - a.B) * t)),
        (byte)(a.A + ((b.A - a.A) * t)));
}
