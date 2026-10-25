using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Rendering;

/// <summary>The tactical computer's map layer: the world-aligned grid and every marker on it.</summary>
/// <remarks>
/// <see cref="TacticalComputerRenderer"/>'s map half, drawn under the chrome. It holds no state of its
/// own -- the centre, the clock and the faces belong to the renderer and are passed in each frame -- only
/// the lookup that turns a highlight's resource name into the words on its label.
/// </remarks>
internal sealed class TacticalMapOverlay
{
    private readonly Func<string, string> _computerString;

    /// <summary>Initializes a new instance of the <see cref="TacticalMapOverlay"/> class.</summary>
    /// <param name="computerString">Resolves a computer string resource to its text.</param>
    public TacticalMapOverlay(Func<string, string> computerString)
    {
        _computerString = computerString;
    }

    /// <summary>Draws the grid and every marker over the overhead view.</summary>
    /// <param name="painter">The painter, already inside the interface's fade.</param>
    /// <param name="context">The back buffer's layout context.</param>
    /// <param name="chrome">This frame's layout, which labels keep clear of.</param>
    /// <param name="map">The viewport the overhead view fills.</param>
    /// <param name="fieldOfView">The vertical field of view the map is read at.</param>
    /// <param name="runtime">The mission's objectives.</param>
    /// <param name="player">Where the player is, in engine units.</param>
    /// <param name="guards">The hostile contacts the map is allowed to show.</param>
    /// <param name="highlights">The authored points of interest currently visible.</param>
    /// <param name="centreUnits">The ground position at the middle of the map.</param>
    /// <param name="seconds">The interface's running clock, for the player marker's breath.</param>
    /// <param name="label">The face the point-of-interest labels are set in.</param>
    public void Draw(
        UiPainter painter,
        LayoutContext context,
        in TacticalComputerChrome chrome,
        Viewport map,
        float fieldOfView,
        IComputerObjectives runtime,
        Vector3d player,
        IReadOnlyList<Vector3d> guards,
        IEnumerable<ComputerMapHighlight> highlights,
        Vector3d centreUnits,
        float seconds,
        UiFont label)
    {
        DrawGrid(painter, context, map, fieldOfView, centreUnits);

        // Points of interest first, so a hostile standing on one is drawn over its label rather than
        // under it. Labels are suppressed once they would collide, nearest-authored winning, because a
        // map whose annotations overlap is less readable than one that is missing a few.
        List<(float X, float Y)> taken = [];
        float labelGuardX = context.Px(150f);
        float labelGuardY = context.Px(26f);
        foreach (ComputerMapHighlight highlight in highlights)
        {
            if (!TacticalMapProjection.TryProject(highlight.Position, centreUnits, fieldOfView, map, out float hx, out float hy))
            {
                continue;
            }

            UiVectors.DrawDiamond(painter, new UiVec2(hx, hy), context.Px(6f), TacticalComputerRenderer.Signal.WithAlpha(210));
            UiVectors.DrawDiamond(painter, new UiVec2(hx, hy), context.Px(2.5f), UiPalette.TextBright);

            bool clear = taken.All(point =>
                MathF.Abs(point.X - hx) > labelGuardX || MathF.Abs(point.Y - hy) > labelGuardY);
            string title = _computerString(highlight.TitleResource);
            if (clear && !string.IsNullOrWhiteSpace(title) && !chrome.OverInterface(hx, hy))
            {
                DrawLabelChip(painter, context, label, title, new UiVec2(hx + context.Px(12f), hy - context.Px(9f)));
                taken.Add((hx, hy));
            }
        }

        foreach (MissionObjective objective in runtime.Objectives)
        {
            if (objective.Position is not { } position || objective.State == MissionObjectiveState.Failed)
            {
                continue;
            }

            if (TacticalMapProjection.TryProject(position, centreUnits, fieldOfView, map, out float ox, out float oy))
            {
                DrawObjectiveMarker(
                    painter,
                    context,
                    new UiVec2(ox, oy),
                    objective.State == MissionObjectiveState.Complete);
            }
        }

        foreach (Vector3d guard in guards)
        {
            if (TacticalMapProjection.TryProject(guard, centreUnits, fieldOfView, map, out float gx, out float gy))
            {
                DrawHostileMarker(painter, context, new UiVec2(gx, gy));
            }
        }

        if (TacticalMapProjection.TryProject(player, centreUnits, fieldOfView, map, out float px, out float py))
        {
            DrawPlayerMarker(painter, context, new UiVec2(px, py), seconds);
        }
    }

    /// <summary>Draws the world-aligned reference grid the map is read against.</summary>
    /// <remarks>
    /// Aligned to the world rather than to the screen, so it pans and zooms with the terrain and is a
    /// measure of it instead of a texture over it. The spacing steps through a 1-2-5 sequence — the one
    /// every chart and every map in the world uses — so a division is always a number a person can hold,
    /// and every fifth line is drawn brighter to give the eye something to count in.
    /// </remarks>
    private static void DrawGrid(UiPainter painter, LayoutContext context, Viewport map, float fieldOfView, Vector3d centreUnits)
    {
        double halfHeightMetres = TacticalMapProjection.AltitudeMetres * Math.Tan(fieldOfView * 0.5);
        double metresPerPixel = 2.0 * halfHeightMetres / Math.Max(1, map.Height);
        double spacing = TacticalMapProjection.NiceStep(metresPerPixel * context.Px(120f));
        double spacingUnits = spacing * TacticalMapProjection.UnitsPerMetre;
        if (spacingUnits <= 0.0)
        {
            return;
        }

        float thin = MathF.Max(1f, context.Px(1f));
        Bgra32 minor = UiPalette.White(0.05f);
        Bgra32 major = UiPalette.White(0.11f);

        (double unitsPerPixelX, double unitsPerPixelY) = TacticalMapProjection.UnitsPerPixel(fieldOfView, map);
        double leftUnits = centreUnits.X - (unitsPerPixelX * map.Width * 0.5);
        double topUnits = centreUnits.Y + (unitsPerPixelY * map.Height * 0.5);

        // NiceStep is chosen so a division is about 120 design units wide, which bounds the count at a
        // couple of dozen per axis; the cap is only there so a degenerate viewport or a zoom that has run
        // to a denormal cannot turn a grid into a hang.
        const int MaxLines = 256;

        long firstColumn = (long)Math.Floor(leftUnits / spacingUnits);
        for (int drawn = 0; drawn < MaxLines; drawn++)
        {
            long i = firstColumn + drawn;
            float x = map.X + (float)(((i * spacingUnits) - leftUnits) / unitsPerPixelX);
            if (x > map.X + map.Width)
            {
                break;
            }

            if (x >= map.X)
            {
                painter.FillRect(new UiRect(x, map.Y, thin, map.Height), (i % 5) == 0 ? major : minor);
            }
        }

        long firstRow = (long)Math.Floor(topUnits / spacingUnits);
        for (int drawn = 0; drawn < MaxLines; drawn++)
        {
            long i = firstRow - drawn;
            float y = map.Y + (float)((topUnits - (i * spacingUnits)) / unitsPerPixelY);
            if (y > map.Y + map.Height)
            {
                break;
            }

            if (y >= map.Y)
            {
                painter.FillRect(new UiRect(map.X, y, map.Width, thin), (i % 5) == 0 ? major : minor);
            }
        }
    }

    private static void DrawPlayerMarker(UiPainter painter, LayoutContext context, UiVec2 at, float seconds)
    {
        // A slow breath rather than a blink. The player's own marker is the one thing on the map that has
        // to be findable without being looked for, and a pulse does that where a brighter static dot only
        // competes with everything else that is also bright.
        float pulse = 0.5f + (0.5f * MathF.Sin(seconds * 2.4f));
        float radius = context.Px(9f);

        UiVectors.StrokeRing(painter, at, radius + (context.Px(7f) * pulse), context.Px(1.5f),
            TacticalComputerRenderer.Accent.WithAlpha((byte)(90 * (1f - pulse))));
        UiVectors.StrokeRing(painter, at, radius, context.Px(2f), TacticalComputerRenderer.Accent);
        UiVectors.FillDisc(painter, at, context.Px(4f), UiPalette.AccentLight);

        // Four brackets rather than a full ring, so the marker reads as a fix on a position instead of as
        // one more circle among the objective markers.
        float arm = context.Px(16f);
        float gap = context.Px(11f);
        float weight = MathF.Max(1f, context.Px(1.5f));
        painter.FillRect(new UiRect(at.X - arm, at.Y - (weight * 0.5f), arm - gap, weight), TacticalComputerRenderer.Accent.WithAlpha(190));
        painter.FillRect(new UiRect(at.X + gap, at.Y - (weight * 0.5f), arm - gap, weight), TacticalComputerRenderer.Accent.WithAlpha(190));
        painter.FillRect(new UiRect(at.X - (weight * 0.5f), at.Y - arm, weight, arm - gap), TacticalComputerRenderer.Accent.WithAlpha(190));
        painter.FillRect(new UiRect(at.X - (weight * 0.5f), at.Y + gap, weight, arm - gap), TacticalComputerRenderer.Accent.WithAlpha(190));
    }

    private static void DrawHostileMarker(UiPainter painter, LayoutContext context, UiVec2 at)
    {
        UiVectors.DrawDiamond(painter, at, context.Px(8f), Bgra32.FromRgba(40, 10, 8, 220));
        UiVectors.DrawDiamond(painter, at, context.Px(6f), TacticalComputerRenderer.Hostile);
        UiVectors.DrawDiamond(painter, at, context.Px(2f), UiPalette.Danger);
    }

    private static void DrawObjectiveMarker(UiPainter painter, LayoutContext context, UiVec2 at, bool complete)
    {
        Bgra32 colour = complete ? TacticalComputerRenderer.Complete : TacticalComputerRenderer.Accent;
        float half = context.Px(9f);
        float tick = context.Px(5f);
        float weight = MathF.Max(1f, context.Px(1.8f));

        // Corner ticks: a target rather than a box. Four short pairs of rectangles, which is both cheaper
        // and more legible at small sizes than an outlined square.
        foreach ((float sx, float sy) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
        {
            float cornerX = at.X + (half * sx);
            float cornerY = at.Y + (half * sy);
            painter.FillRect(
                new UiRect(sx < 0f ? cornerX : cornerX - tick, cornerY - (weight * 0.5f), tick, weight), colour);
            painter.FillRect(
                new UiRect(cornerX - (weight * 0.5f), sy < 0f ? cornerY : cornerY - tick, weight, tick), colour);
        }

        if (complete)
        {
            UiVectors.FillDisc(painter, at, context.Px(3.5f), colour, 10);
        }
        else
        {
            UiVectors.StrokeRing(painter, at, context.Px(3.5f), weight, colour, 10);
        }
    }

    private static void DrawLabelChip(UiPainter painter, LayoutContext context, UiFont font, string text, UiVec2 at)
    {
        float size = context.Px(14f);
        float tracking = context.Px(1.2f);
        float width = UiVectors.MeasureTracked(font, text, size, tracking);
        float padX = context.Px(8f);
        float padY = context.Px(5f);
        float height = font.LineHeight(size) + (padY * 2f);

        var chip = new UiRect(at.X, at.Y - (height * 0.5f), width + (padX * 2f) + context.Px(3f), height);
        painter.FillRect(chip, UiPalette.Ink.WithAlpha(190));
        painter.FillRect(new UiRect(chip.X, chip.Y, context.Px(2f), chip.Height), TacticalComputerRenderer.Signal.WithAlpha(200));
        UiVectors.DrawTracked(painter, font, text, new UiVec2(chip.X + padX + context.Px(3f), chip.Y + padY), size,
            UiPalette.Text, tracking);
    }

    /// <summary>Draws the bar that says how far across the map a given distance is.</summary>
    /// <remarks>
    /// A ratio tells the player how far in they are; a bar tells them how far away something is, which is
    /// the question a map is actually asked. Its length is chosen from the 1-2-5 sequence so the number
    /// under it is always round, and the bar is whatever width that number turns out to be rather than a
    /// fixed width with an awkward number under it.
    /// </remarks>
    public static void DrawScaleBar(
        UiPainter painter,
        LayoutContext context,
        UiFont label,
        UiRect bar,
        Viewport map,
        float fieldOfView)
    {
        double halfHeightMetres = TacticalMapProjection.AltitudeMetres * Math.Tan(fieldOfView * 0.5);
        double metresPerPixel = 2.0 * halfHeightMetres / Math.Max(1, map.Height);
        double metres = TacticalMapProjection.NiceStep(metresPerPixel * context.Px(170f));
        float width = (float)(metres / Math.Max(metresPerPixel, 1e-9));
        if (!float.IsFinite(width) || width <= 0f)
        {
            return;
        }

        float right = bar.Right - context.Px(28f);
        float left = right - width;
        float mid = bar.Y + (bar.Height * 0.5f);
        float weight = MathF.Max(1f, context.Px(1.5f));
        float tick = context.Px(6f);

        painter.FillRect(new UiRect(left, mid + context.Px(3f), width, weight), TacticalComputerRenderer.Signal.WithAlpha(200));
        painter.FillRect(new UiRect(left, mid + context.Px(3f) - tick, weight, tick + weight), TacticalComputerRenderer.Signal.WithAlpha(200));
        painter.FillRect(new UiRect(right - weight, mid + context.Px(3f) - tick, weight, tick + weight), TacticalComputerRenderer.Signal.WithAlpha(200));

        string text = metres >= 1000.0 ? $"{metres / 1000.0:0.#} KM" : $"{metres:0} M";
        float size = context.Px(12f);
        float tracking = context.Px(1.6f);
        UiVectors.DrawTracked(painter, label, text,
            new UiVec2(right - UiVectors.MeasureTracked(label, text, size, tracking), mid - context.Px(14f)),
            size, TacticalComputerRenderer.TextMuted, tracking);
    }
}
