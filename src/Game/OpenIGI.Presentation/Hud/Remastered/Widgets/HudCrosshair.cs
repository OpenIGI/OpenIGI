using OpenIGI.Core.Graphics;
using OpenIGI.Game.Player;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Widgets;

/// <summary>
/// The remastered crosshair: clean white strokes over a soft dark edge, so they read on any ground, shaped to the weapon in hand,
/// opened by how far its rounds stray and by each round's kick, turned red over an enemy; a white cross where a round hits and a
/// red one, popping and held, where it kills, larger for the head. <b>Not retail.</b>
/// </summary>
public static class HudCrosshair
{
    private static readonly Bgra32 White = Bgra32.FromRgba(255, 255, 255, 255);
    private static readonly Bgra32 Hostile = Bgra32.FromRgba(255, 70, 58, 255);
    private static readonly Bgra32 Edge = Bgra32.FromRgba(0, 0, 0, 115);

    /// <summary>The narrowest each crosshair stands open and how long its arms are, in design pixels.</summary>
    /// <param name="shape">The crosshair.</param>
    /// <returns>Its least gap and its arms' length.</returns>
    public static (float Gap, float Arm) Proportions(CrosshairShape shape) => shape switch
    {
        CrosshairShape.Pistol => (4f, 5f),
        CrosshairShape.Rifle => (5f, 7f),
        CrosshairShape.Smg => (8f, 6f),
        CrosshairShape.MachineGun => (7f, 9f),
        CrosshairShape.Shotgun => (12f, 0f),
        CrosshairShape.Sniper => (14f, 0f),
        _ => (0f, 0f),
    };

    /// <summary>How far from the centre a cone of rounds lands on the screen, in pixels.</summary>
    /// <param name="spreadRadians">The cone's half-angle.</param>
    /// <param name="frame">The frame, whose projector gives the view's width; without one, a quarter-turn view.</param>
    /// <returns>The radius.</returns>
    public static float SpreadPixels(float spreadRadians, in HudFrame frame)
    {
        float tanHalf = frame.Projector is { TanX: > 0f } view ? view.TanX : 1f;
        return MathF.Tan(Math.Clamp(spreadRadians, 0f, 1.2f)) / tanHalf * frame.Width * 0.5f;
    }

    /// <summary>Draws the crosshair, when the frame shows one and the weapon in hand has one.</summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="model">The frame's model.</param>
    /// <param name="frame">The frame.</param>
    public static void Draw(HudDrawContext context, HudModel model, in HudFrame frame)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        HudCrosshairState state = context.Reactive.Crosshair;
        CrosshairShape shape = model.Aim.Shape;
        if ((model.Show & HudParts.Crosshair) == 0 || shape == CrosshairShape.None || !HudCVars.Crosshair.Value)
        {
            return;
        }

        UiPainter painter = context.Painter;
        UiVec2 c = new(MathF.Round(context.Width * 0.5f) + 0.5f, MathF.Round(context.Height * 0.5f) + 0.5f);
        float u = MathF.Max(0.75f, context.Px(1f));
        float width = MathF.Max(1.25f, 1.6f * u);
        (float least, float arm) = Proportions(shape);
        float gap = MathF.Max(least * u, SpreadPixels(state.Spread, frame));
        Bgra32 ink = Interpolate.Lerp(White, Hostile, state.Enemy);
        switch (shape)
        {
            case CrosshairShape.Pistol:
            case CrosshairShape.Rifle:
                Arms(painter, c, gap, arm * u, width, ink);
                Pip(painter, c, 1.15f * u, ink);
                break;
            case CrosshairShape.Smg:
                Arms(painter, c, gap, arm * u, width, ink);
                for (int corner = 0; corner < 4; corner++)
                {
                    UiVec2 d = Diagonal(corner);
                    float r = (gap * 0.82f) + (2f * u);
                    Stroke(painter, Along(c, d, r), Along(c, d, r + (3f * u)), width, ink);
                }

                Pip(painter, c, 1.15f * u, ink);
                break;
            case CrosshairShape.MachineGun:
                Arms(painter, c, gap, arm * u, width * 1.3f, ink);
                float bracket = gap + (arm * u) + (4f * u);
                for (int corner = 0; corner < 4; corner++)
                {
                    float mid = (corner * MathF.PI * 0.5f) + (MathF.PI * 0.25f);
                    Ring(painter, c, bracket, width, mid - 0.32f, mid + 0.32f, ink);
                }

                Pip(painter, c, 1.3f * u, ink);
                break;
            case CrosshairShape.Shotgun:
                Ring(painter, c, gap, width, 0f, MathF.Tau, ink);
                Pip(painter, c, 1.25f * u, ink);
                break;
            case CrosshairShape.Sniper:
                Ring(painter, c, gap, width * 0.8f, 0f, MathF.Tau, HudDrawContext.Fade(ink, 0.35f));
                Pip(painter, c, 1.6f * u, ink);
                break;
            case CrosshairShape.Launcher:
                Stroke(painter, new UiVec2(c.X - (8f * u), c.Y - (3f * u)), c, width, ink);
                Stroke(painter, c, new UiVec2(c.X + (8f * u), c.Y - (3f * u)), width, ink);
                Stroke(painter, new UiVec2(c.X - (4f * u), c.Y + (9f * u)), new UiVec2(c.X + (4f * u), c.Y + (9f * u)), width, ink);
                Stroke(painter, new UiVec2(c.X - (2.5f * u), c.Y + (15f * u)), new UiVec2(c.X + (2.5f * u), c.Y + (15f * u)), width, ink);
                break;
            case CrosshairShape.Grenade:
                Ring(painter, c, 10f * u, width, MathF.PI * 1.15f, MathF.PI * 1.85f, ink);
                Pip(painter, c, 1.15f * u, ink);
                break;
            case CrosshairShape.Melee:
                Pip(painter, c, 1.5f * u, ink);
                break;
        }

        if (HudCVars.HitMarkers.Value)
        {
            Marks(painter, c, gap, u, width, state);
        }
    }

    // A hit's white cross, short and quick; a kill's red one, larger, popping out and held; the head's each larger again, the
    // kill's with a ring.
    private static void Marks(UiPainter painter, UiVec2 c, float gap, float u, float width, HudCrosshairState state)
    {
        float kill = state.Kill;
        if (kill > 0f)
        {
            float pop = 1f + (0.35f * MathF.Pow(Math.Clamp((kill - 0.8f) / 0.2f, 0f, 1f), 2f));
            float inner = ((state.KillHead ? 7f : 6f) * u * pop) + (gap * 0.35f);
            float outer = inner + ((state.KillHead ? 10f : 8f) * u * pop);
            Bgra32 red = HudDrawContext.Fade(Hostile, Math.Clamp(kill * 2.2f, 0f, 1f));
            XMark(painter, c, inner, outer, width * 1.45f, red);
            if (state.KillHead)
            {
                Ring(painter, c, outer + (5f * u), width, 0f, MathF.Tau, HudDrawContext.Fade(red, 0.75f));
            }

            return;
        }

        float hit = state.Hit;
        if (hit > 0f)
        {
            float grow = 1f + ((1f - hit) * 0.25f);
            float inner = ((state.HitHead ? 6f : 5f) * u) + (gap * 0.35f);
            float outer = inner + ((state.HitHead ? 8f : 6f) * u * grow);
            XMark(painter, c, inner, outer, width * (state.HitHead ? 1.35f : 1.1f), HudDrawContext.Fade(White, Math.Clamp(hit * 1.6f, 0f, 1f)));
        }
    }

    // Four arms out from the gap, up, down, left and right.
    private static void Arms(UiPainter painter, UiVec2 c, float gap, float arm, float width, Bgra32 ink)
    {
        Stroke(painter, new UiVec2(c.X - gap, c.Y), new UiVec2(c.X - gap - arm, c.Y), width, ink);
        Stroke(painter, new UiVec2(c.X + gap, c.Y), new UiVec2(c.X + gap + arm, c.Y), width, ink);
        Stroke(painter, new UiVec2(c.X, c.Y - gap), new UiVec2(c.X, c.Y - gap - arm), width, ink);
        Stroke(painter, new UiVec2(c.X, c.Y + gap), new UiVec2(c.X, c.Y + gap + arm), width, ink);
    }

    // Four strokes on the diagonals, from one radius to another.
    private static void XMark(UiPainter painter, UiVec2 c, float inner, float outer, float width, Bgra32 ink)
    {
        for (int corner = 0; corner < 4; corner++)
        {
            UiVec2 d = Diagonal(corner);
            Stroke(painter, Along(c, d, inner), Along(c, d, outer), width, ink);
        }
    }

    // A stroke over its soft dark edge.
    private static void Stroke(UiPainter painter, UiVec2 from, UiVec2 to, float width, Bgra32 ink)
    {
        painter.StrokeSoft(from, to, width + 1.6f, HudDrawContext.Fade(Edge, ink.A / 255f));
        painter.StrokeSoft(from, to, width, ink);
    }

    // A band of a ring over its soft dark edge.
    private static void Ring(UiPainter painter, UiVec2 c, float radius, float width, float from, float to, Bgra32 ink)
    {
        float half = width * 0.5f;
        painter.FillArc(c, MathF.Max(0f, radius - half - 0.8f), radius + half + 0.8f, from, to, HudDrawContext.Fade(Edge, ink.A / 255f));
        painter.FillArc(c, MathF.Max(0f, radius - half), radius + half, from, to, ink);
    }

    // A dot over its soft dark edge.
    private static void Pip(UiPainter painter, UiVec2 c, float radius, Bgra32 ink)
    {
        painter.FillDot(c, radius + 0.8f, HudDrawContext.Fade(Edge, ink.A / 255f));
        painter.FillDot(c, radius, ink);
    }

    private static UiVec2 Diagonal(int corner) => corner switch
    {
        0 => new UiVec2(0.70710678f, 0.70710678f),
        1 => new UiVec2(-0.70710678f, 0.70710678f),
        2 => new UiVec2(-0.70710678f, -0.70710678f),
        _ => new UiVec2(0.70710678f, -0.70710678f),
    };

    private static UiVec2 Along(UiVec2 c, UiVec2 d, float r) => new(c.X + (d.X * r), c.Y + (d.Y * r));
}
