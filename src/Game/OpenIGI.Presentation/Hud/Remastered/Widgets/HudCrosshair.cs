using OpenIGI.Core.Graphics;
using OpenIGI.Game.Player;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Widgets;

/// <summary>
/// The remastered crosshair: a few short white ticks that sit where the weapon's rounds may land and close in as its aim
/// steadies -- three for most guns, left, right and below, with no centre point while aiming; a circle for a shotgun; three with
/// four small diagonals near the centre for a scoped rifle fired from the hip; a faint point for a knife. A hit shows a small cross
/// that blinks off; a kill a larger red one that holds, punching out again if another kill lands while it shows. <b>Not
/// retail.</b>
/// </summary>
/// <remarks>
/// The ticks are offset from the centre by the projected spread itself (the cone's half-angle as the view sees it), so a tight
/// weapon brings them almost together and a wild one throws them wide. The drawing is this port's own.
/// </remarks>
public static class HudCrosshair
{
    /// <summary>A tick's length, in design pixels.</summary>
    public const float TickLength = 4f;

    /// <summary>The least a tick stands from the centre, in design pixels, so a perfectly steady aim still reads as three ticks.</summary>
    public const float LeastOffset = 2.5f;

    /// <summary>The most the ticks open, in design pixels, so a weapon at its wildest still frames the aim.</summary>
    public const float MostOffset = 40f;

    /// <summary>
    /// How far the ticks open for a weapon's spread: half the projected cone, so they frame the aim closely rather than marking
    /// where its widest round may land.
    /// </summary>
    public const float SpreadShare = 0.5f;

    /// <summary>A hit or kill cross at full scale: from this far out of the centre, in design pixels.</summary>
    public const float MarkInner = 5f;

    /// <summary>A hit or kill cross at full scale: to this far out, in design pixels.</summary>
    public const float MarkOuter = 12f;

    private static readonly Bgra32 White = Bgra32.FromRgba(255, 255, 255, 235);
    private static readonly Bgra32 KillInk = Bgra32.FromRgba(232, 48, 40, 255);
    private static readonly Bgra32 Edge = Bgra32.FromRgba(0, 0, 0, 90);

    // The three ticks' directions: left, right and below (the screen's y runs down).
    private static readonly (float X, float Y)[] Ticks = [(-1f, 0f), (1f, 0f), (0f, 1f)];

    // The hip-fired scoped rifle's diagonals, a share of the ticks' offset in each axis.
    private static readonly (float X, float Y)[] Diagonals = [(-0.3f, 0.3f), (0.3f, 0.3f), (0.3f, -0.3f), (-0.3f, -0.3f)];

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
        float width = MathF.Max(1.1f, 1.25f * u);
        float offset = Math.Clamp(SpreadPixels(state.Spread, frame) * SpreadShare, LeastOffset * u, MostOffset * u);
        switch (shape)
        {
            case CrosshairShape.Shotgun:
                Ring(painter, c, offset, MathF.Max(1f, width * 0.8f), White);
                break;
            case CrosshairShape.Melee:
                Point(painter, c, width * 0.75f, HudDrawContext.Fade(White, 0.5f));
                break;
            case CrosshairShape.Sniper:
                ThreeTicks(painter, c, offset, TickLength * u, width, White);
                foreach ((float x, float y) in Diagonals)
                {
                    // A short tick on the diagonal, running outward from its place.
                    UiVec2 at = new(c.X + (x * offset), c.Y + (y * offset));
                    float reach = TickLength * u * 0.5f * 0.70710678f;
                    Stroke(painter, at, new UiVec2(at.X + (MathF.Sign(x) * reach), at.Y + (MathF.Sign(y) * reach)), width, White);
                }

                break;
            default:
                ThreeTicks(painter, c, offset, TickLength * u, width, White);
                break;
        }

        if (HudCVars.HitMarkers.Value)
        {
            Marks(painter, c, u, width, state);
        }
    }

    // A kill's red cross, held and punched on a repeat; else a hit's small white one, blinking off.
    private static void Marks(UiPainter painter, UiVec2 c, float u, float width, HudCrosshairState state)
    {
        if (state.KillShowing)
        {
            float scale = state.KillScale;
            XMark(painter, c, MarkInner * u * scale, MarkOuter * u * scale, width * 1.3f, KillInk);
        }
        else if (state.HitShowing)
        {
            XMark(painter, c, MarkInner * u * HudCrosshairState.HitScale, MarkOuter * u * HudCrosshairState.HitScale, width, White);
        }
    }

    // Left, right and below, each running out from the offset.
    private static void ThreeTicks(UiPainter painter, UiVec2 c, float offset, float length, float width, Bgra32 ink)
    {
        foreach ((float x, float y) in Ticks)
        {
            Stroke(painter, new UiVec2(c.X + (x * offset), c.Y + (y * offset)), new UiVec2(c.X + (x * (offset + length)), c.Y + (y * (offset + length))), width, ink);
        }
    }

    // Four strokes on the diagonals, from one radius to another.
    private static void XMark(UiPainter painter, UiVec2 c, float inner, float outer, float width, Bgra32 ink)
    {
        const float Diagonal = 0.70710678f;
        for (int corner = 0; corner < 4; corner++)
        {
            float x = (corner & 1) == 0 ? Diagonal : -Diagonal;
            float y = corner < 2 ? Diagonal : -Diagonal;
            Stroke(painter, new UiVec2(c.X + (x * inner), c.Y + (y * inner)), new UiVec2(c.X + (x * outer), c.Y + (y * outer)), width, ink);
        }
    }

    // A stroke over its faint dark edge.
    private static void Stroke(UiPainter painter, UiVec2 from, UiVec2 to, float width, Bgra32 ink)
    {
        painter.StrokeSoft(from, to, width + 1.5f, HudDrawContext.Fade(Edge, ink.A / 255f));
        painter.StrokeSoft(from, to, width, ink);
    }

    // A circle over its faint dark edge.
    private static void Ring(UiPainter painter, UiVec2 c, float radius, float width, Bgra32 ink)
    {
        float half = width * 0.5f;
        painter.FillArc(c, MathF.Max(0f, radius - half - 0.75f), radius + half + 0.75f, 0f, MathF.Tau, HudDrawContext.Fade(Edge, ink.A / 255f));
        painter.FillArc(c, MathF.Max(0f, radius - half), radius + half, 0f, MathF.Tau, ink);
    }

    // A small soft square, the knife's point.
    private static void Point(UiPainter painter, UiVec2 c, float half, Bgra32 ink)
    {
        painter.StrokeSoft(new UiVec2(c.X - half - 0.75f, c.Y), new UiVec2(c.X + half + 0.75f, c.Y), (half * 2f) + 1.5f, HudDrawContext.Fade(Edge, ink.A / 255f));
        painter.StrokeSoft(new UiVec2(c.X - half, c.Y), new UiVec2(c.X + half, c.Y), half * 2f, ink);
    }
}
