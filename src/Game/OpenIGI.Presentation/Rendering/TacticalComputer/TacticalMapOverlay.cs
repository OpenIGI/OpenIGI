using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Rendering;

/// <summary>What the remastered computer draws a frame with: the painter, the text, the title's look and the layout.</summary>
/// <param name="Painter">The painter the shapes are drawn with.</param>
/// <param name="Text">The text.</param>
/// <param name="Theme">The title's look.</param>
/// <param name="Chrome">This frame's layout.</param>
/// <param name="Seconds">The display's clock, in seconds.</param>
internal sealed record ComputerCanvas(UiPainter Painter, ComputerText Text, HudTheme Theme, TacticalComputerChrome Chrome, float Seconds)
{
    /// <summary>Gets the title's colours.</summary>
    public HudPalette Palette => Theme.Palette;

    /// <summary>Gets the colour of quiet text: the title's own, stepped back, where its fill colour would be too dark to read.</summary>
    public Bgra32 Quiet => Fade(Theme.Palette.Primary, 0.62f);

    /// <summary>Gets the thickness of a thin line, in pixels.</summary>
    public float Hairline => MathF.Max(1f, MathF.Round(Theme.Motifs.Hairline * Chrome.Unit));

    /// <summary>A length in design pixels, on the screen.</summary>
    /// <param name="design">The length.</param>
    /// <returns>It in pixels.</returns>
    public float Px(float design) => Chrome.Px(design);

    /// <summary>A colour at an opacity.</summary>
    /// <param name="colour">The colour.</param>
    /// <param name="opacity">The opacity, nought to one, times the colour's own.</param>
    /// <returns>The colour.</returns>
    public static Bgra32 Fade(Bgra32 colour, float opacity) => HudDrawContext.Fade(colour, opacity);
}

/// <summary>The remastered computer's map layer: the world-aligned grid and every mark on it.</summary>
/// <remarks>
/// Drawn under the strips, in the title's colours: its own colour for the grid, the player and the level's places, the
/// colour of what is passing for the objectives, the colour of harm for the hostile. It holds no state of its own -- the
/// centre, the clock and the text belong to the renderer and are passed in each frame -- only the lookup that turns a
/// highlight's resource name into the words on its label.
/// </remarks>
internal sealed class TacticalMapOverlay
{
    private readonly Func<string, string> _computerString;
    private readonly List<(float X, float Y)> _taken = [];

    /// <summary>Initializes a new instance of the <see cref="TacticalMapOverlay"/> class.</summary>
    /// <param name="computerString">Resolves a computer string resource to its text.</param>
    public TacticalMapOverlay(Func<string, string> computerString) => _computerString = computerString;

    /// <summary>Draws the grid and every mark over the feed.</summary>
    /// <param name="canvas">The frame's drawing.</param>
    /// <param name="map">The viewport the overhead view fills.</param>
    /// <param name="fieldOfView">The vertical field of view the map is read at.</param>
    /// <param name="runtime">The mission's objectives.</param>
    /// <param name="player">Where the player is, in engine units.</param>
    /// <param name="heading">Which way the player faces: nought along +Y, turning towards +X.</param>
    /// <param name="guards">The hostile contacts the map is allowed to show.</param>
    /// <param name="highlights">The authored points of interest currently visible.</param>
    /// <param name="centreUnits">The ground position at the middle of the map.</param>
    public void Draw(
        ComputerCanvas canvas,
        Viewport map,
        float fieldOfView,
        IComputerObjectives runtime,
        Vector3d player,
        float heading,
        IReadOnlyList<Vector3d> guards,
        IEnumerable<ComputerMapHighlight> highlights,
        Vector3d centreUnits)
    {
        DrawGrid(canvas, map, fieldOfView, centreUnits);
        DrawFrame(canvas);

        // Points of interest first, so a hostile standing on one is drawn over its label rather than under it. Labels are
        // left out once they would collide, the first authored winning, because a map whose annotations overlap is less
        // readable than one that is missing a few.
        _taken.Clear();
        float guardX = canvas.Px(130f);
        float guardY = canvas.Text.LineHeight + canvas.Px(4f);
        foreach (ComputerMapHighlight highlight in highlights)
        {
            if (!Project(canvas, highlight.Position, centreUnits, fieldOfView, map, out UiVec2 at))
            {
                continue;
            }

            UiVectors.DrawDiamond(canvas.Painter, at, canvas.Px(5.5f), Bgra32.FromRgba(0, 0, 0, 170));
            UiVectors.DrawDiamond(canvas.Painter, at, canvas.Px(4f), canvas.Palette.Primary);

            string title = highlight.Title ?? _computerString(highlight.TitleResource);
            bool clear = true;
            foreach ((float x, float y) in _taken)
            {
                if (MathF.Abs(x - at.X) <= guardX && MathF.Abs(y - at.Y) <= guardY)
                {
                    clear = false;
                    break;
                }
            }

            if (clear && !string.IsNullOrWhiteSpace(title))
            {
                DrawLabel(canvas, canvas.Text.Capitals(title), at);
                _taken.Add((at.X, at.Y));
            }
        }

        for (int i = 0; i < runtime.Objectives.Count; i++)
        {
            MissionObjective objective = runtime.Objectives[i];
            if (objective.Position is { } position
                && objective.State != MissionObjectiveState.Failed
                && Project(canvas, position, centreUnits, fieldOfView, map, out UiVec2 at))
            {
                DrawObjective(canvas, at, i + 1, objective.State == MissionObjectiveState.Complete);
            }
        }

        foreach (Vector3d guard in guards)
        {
            if (Project(canvas, guard, centreUnits, fieldOfView, map, out UiVec2 at))
            {
                DrawHostile(canvas, at);
            }
        }

        if (Project(canvas, player, centreUnits, fieldOfView, map, out UiVec2 own))
        {
            DrawPlayer(canvas, own, heading);
        }
    }

    /// <summary>Draws the pointer over the map: an open reticle, and where on the ground it is and how far from the player.</summary>
    /// <param name="canvas">The frame's drawing.</param>
    /// <param name="at">The pointer, in pixels.</param>
    /// <param name="ground">The ground under it, in engine units.</param>
    /// <param name="player">Where the player is, in engine units.</param>
    public static void DrawPointer(ComputerCanvas canvas, UiVec2 at, Vector3d ground, Vector3d player)
    {
        float arm = canvas.Px(12f);
        float gap = canvas.Px(4f);
        float weight = MathF.Max(1f, MathF.Round(canvas.Px(1.4f)));
        Bgra32 ink = canvas.Palette.Highlight;
        Bgra32 under = Bgra32.FromRgba(0, 0, 0, 150);
        foreach ((float dx, float dy) in Arms)
        {
            float x = dx < 0f ? at.X - arm : dx > 0f ? at.X + gap : at.X - (weight * 0.5f);
            float y = dy < 0f ? at.Y - arm : dy > 0f ? at.Y + gap : at.Y - (weight * 0.5f);
            float w = dx == 0f ? weight : arm - gap;
            float h = dy == 0f ? weight : arm - gap;
            canvas.Painter.FillRect(new UiRect(x + 1f, y + 1f, w, h), under);
            canvas.Painter.FillRect(new UiRect(x, y, w, h), ink);
        }

        double dxm = (ground.X - player.X) / TacticalMapProjection.UnitsPerMetre;
        double dym = (ground.Y - player.Y) / TacticalMapProjection.UnitsPerMetre;
        string range = string.Create(CultureInfo.InvariantCulture, $"{GridReference(ground)}  {Math.Sqrt((dxm * dxm) + (dym * dym)):0}M");
        float width = canvas.Text.Measure(range);
        float x0 = at.X + arm + canvas.Px(4f);
        if (x0 + width > canvas.Chrome.Map.Right - canvas.Px(8f))
        {
            x0 = at.X - arm - canvas.Px(4f) - width;
        }

        float y0 = MathF.Min(at.Y + canvas.Px(8f), canvas.Chrome.Map.Bottom - canvas.Text.LineHeight - canvas.Px(4f));
        canvas.Text.DrawOnMap(range, x0, y0, canvas.Palette.Highlight);
    }

    /// <summary>A ground position as metres east and north of the level's origin.</summary>
    /// <param name="units">The position, in engine units.</param>
    /// <returns>The reference, as the status line and the pointer print it.</returns>
    public static string GridReference(Vector3d units)
    {
        long east = (long)Math.Round(units.X / TacticalMapProjection.UnitsPerMetre);
        long north = (long)Math.Round(units.Y / TacticalMapProjection.UnitsPerMetre);
        return string.Create(CultureInfo.InvariantCulture, $"E {east:0000} N {north:0000}");
    }

    /// <summary>Draws the bar that says how far across the map a given distance is, ending at a point.</summary>
    /// <remarks>
    /// Its length is chosen from the 1-2-5 sequence so the number beside it is always round, and the bar is whatever width
    /// that number turns out to be rather than a fixed width with an awkward number beside it.
    /// </remarks>
    /// <param name="canvas">The frame's drawing.</param>
    /// <param name="right">Where the bar ends, in pixels.</param>
    /// <param name="middle">The height of the line it stands on, in pixels.</param>
    /// <param name="map">The viewport the overhead view fills.</param>
    /// <param name="fieldOfView">The vertical field of view the map is read at.</param>
    /// <returns>The left edge of the bar and its figure, in pixels.</returns>
    public static float DrawScaleBar(ComputerCanvas canvas, float right, float middle, Viewport map, float fieldOfView)
    {
        double metresPerPixel = MetresPerPixel(map, fieldOfView);
        double metres = TacticalMapProjection.NiceStep(metresPerPixel * canvas.Px(110f));
        float width = (float)(metres / Math.Max(metresPerPixel, 1e-9));
        if (!float.IsFinite(width) || width <= 0f)
        {
            return right;
        }

        float left = MathF.Round(right - width);
        float weight = canvas.Hairline;
        float tick = canvas.Px(5f);
        float y = MathF.Round(middle + (tick * 0.5f));
        Bgra32 ink = canvas.Palette.Primary;
        canvas.Painter.FillRect(new UiRect(left, y, width, weight), ink);
        canvas.Painter.FillRect(new UiRect(left, y - tick, weight, tick), ink);
        canvas.Painter.FillRect(new UiRect(right - weight, y - tick, weight, tick), ink);

        string text = metres >= 1000.0
            ? string.Create(CultureInfo.InvariantCulture, $"{metres / 1000.0:0.#}KM")
            : string.Create(CultureInfo.InvariantCulture, $"{metres:0}M");
        return canvas.Text.DrawRight(text, left - canvas.Px(8f), middle - (canvas.Text.LineHeight * 0.5f), canvas.Palette.Primary);
    }

    /// <summary>How many metres of ground a map pixel spans.</summary>
    /// <param name="map">The viewport the overhead view fills.</param>
    /// <param name="fieldOfView">The vertical field of view the map is read at.</param>
    /// <returns>The metres.</returns>
    public static double MetresPerPixel(Viewport map, float fieldOfView) =>
        2.0 * TacticalMapProjection.AltitudeMetres * Math.Tan(fieldOfView * 0.5) / Math.Max(1, map.Height);

    private static readonly (float X, float Y)[] Arms = [(-1f, 0f), (1f, 0f), (0f, -1f), (0f, 1f)];

    private static readonly (float X, float Y)[] Corners = [(-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f)];

    // A ground position on the map, where it falls between the strips.
    private static bool Project(ComputerCanvas canvas, Vector3d units, Vector3d centreUnits, float fieldOfView, Viewport map, out UiVec2 at)
    {
        bool inside = TacticalMapProjection.TryProject(units, centreUnits, fieldOfView, map, out float x, out float y);
        at = new UiVec2(x, y);
        return inside && y >= canvas.Chrome.Map.Y && y <= canvas.Chrome.Map.Bottom;
    }

    /// <summary>Draws the world-aligned reference grid the map is read against, with its figures along the top and the left.</summary>
    /// <remarks>
    /// Aligned to the world rather than to the screen, so it pans and zooms with the terrain and is a measure of it instead
    /// of a texture over it. The spacing steps through a 1-2-5 sequence, so a division is always a number a person can
    /// hold, and every fifth line is drawn brighter and carries its metres.
    /// </remarks>
    private static void DrawGrid(ComputerCanvas canvas, Viewport map, float fieldOfView, Vector3d centreUnits)
    {
        double metresPerPixel = MetresPerPixel(map, fieldOfView);
        double spacing = TacticalMapProjection.NiceStep(metresPerPixel * canvas.Px(110f));
        double spacingUnits = spacing * TacticalMapProjection.UnitsPerMetre;
        if (spacingUnits <= 0.0)
        {
            return;
        }

        UiRect area = canvas.Chrome.Map;
        float thin = canvas.Hairline;
        Bgra32 minor = ComputerCanvas.Fade(canvas.Palette.Primary, 0.09f);
        Bgra32 major = ComputerCanvas.Fade(canvas.Palette.Primary, 0.22f);
        Bgra32 figure = ComputerCanvas.Fade(canvas.Palette.Primary, 0.7f);

        (double unitsPerPixelX, double unitsPerPixelY) = TacticalMapProjection.UnitsPerPixel(fieldOfView, map);
        double leftUnits = centreUnits.X - (unitsPerPixelX * map.Width * 0.5);
        double topUnits = centreUnits.Y + (unitsPerPixelY * map.Height * 0.5);

        // The step bounds the count at a couple of dozen an axis; the cap is only there so a degenerate viewport or a
        // zoom that has run to a denormal cannot turn a grid into a hang.
        const int MaxLines = 256;
        float pad = canvas.Px(4f);

        long firstColumn = (long)Math.Floor(leftUnits / spacingUnits);
        for (int drawn = 0; drawn < MaxLines; drawn++)
        {
            long i = firstColumn + drawn;
            float x = MathF.Round(map.X + (float)(((i * spacingUnits) - leftUnits) / unitsPerPixelX));
            if (x > area.Right)
            {
                break;
            }

            if (x < area.X)
            {
                continue;
            }

            bool fifth = (i % 5) == 0;
            canvas.Painter.FillRect(new UiRect(x, area.Y, thin, area.Height), fifth ? major : minor);
            if (fifth)
            {
                canvas.Text.DrawOnMap(
                    string.Create(CultureInfo.InvariantCulture, $"{i * spacing:0}"), x + pad, area.Y + pad, figure);
            }
        }

        long firstRow = (long)Math.Floor(topUnits / spacingUnits);
        for (int drawn = 0; drawn < MaxLines; drawn++)
        {
            long i = firstRow - drawn;
            float y = MathF.Round(map.Y + (float)((topUnits - (i * spacingUnits)) / unitsPerPixelY));
            if (y > area.Bottom)
            {
                break;
            }

            if (y < area.Y)
            {
                continue;
            }

            bool fifth = (i % 5) == 0;
            canvas.Painter.FillRect(new UiRect(area.X, y, area.Width, thin), fifth ? major : minor);
            if (fifth && y > area.Y + canvas.Text.LineHeight + (pad * 2f))
            {
                canvas.Text.DrawOnMap(
                    string.Create(CultureInfo.InvariantCulture, $"{i * spacing:0}"), area.X + pad, y + pad, figure);
            }
        }
    }

    // The map's own frame: brackets at its corners, the title's way, and a mark at the middle of each side.
    private static void DrawFrame(ComputerCanvas canvas)
    {
        float inset = canvas.Px(10f);
        UiRect area = canvas.Chrome.Map;
        var frame = new UiRect(area.X + inset, area.Y + inset, area.Width - (inset * 2f), area.Height - (inset * 2f));
        if (frame.Width <= 0f || frame.Height <= 0f)
        {
            return;
        }

        Bgra32 ink = ComputerCanvas.Fade(canvas.Palette.Primary, 0.75f);
        Hud.Remastered.Widgets.HudPanel.Brackets(canvas.Painter, canvas.Theme, canvas.Chrome.Unit, frame, canvas.Px(18f), ink);

        float tick = canvas.Px(8f);
        float weight = canvas.Hairline;
        float midX = MathF.Round(frame.X + (frame.Width * 0.5f));
        float midY = MathF.Round(frame.Y + (frame.Height * 0.5f));
        canvas.Painter.FillRect(new UiRect(midX, frame.Y, weight, tick), ink);
        canvas.Painter.FillRect(new UiRect(midX, frame.Bottom - tick, weight, tick), ink);
        canvas.Painter.FillRect(new UiRect(frame.X, midY, tick, weight), ink);
        canvas.Painter.FillRect(new UiRect(frame.Right - tick, midY, tick, weight), ink);
    }

    // The player: an arrow along the heading, and a ring that breathes out from it. The ring is a slow breath rather than
    // a blink: the one mark that has to be found without being looked for.
    private static void DrawPlayer(ComputerCanvas canvas, UiVec2 at, float heading)
    {
        float pulse = (canvas.Seconds * 0.55f) % 1f;
        Bgra32 ink = canvas.Palette.Highlight;
        UiVectors.StrokeRing(canvas.Painter, at, canvas.Px(9f) + (canvas.Px(16f) * pulse), MathF.Max(1f, canvas.Px(1.5f)),
            ComputerCanvas.Fade(canvas.Palette.Primary, 0.8f * (1f - pulse)));

        // Up the screen is north: +Y on the ground.
        var along = new UiVec2(MathF.Sin(heading), -MathF.Cos(heading));
        var across = new UiVec2(-along.Y, along.X);
        float length = canvas.Px(11f);
        float half = canvas.Px(7f);
        UiVec2 tip = at + (along * length);
        UiVec2 notch = at - (along * (length * 0.25f));
        UiVec2 left = at - (along * (length * 0.7f)) - (across * half);
        UiVec2 right = at - (along * (length * 0.7f)) + (across * half);

        Bgra32 under = Bgra32.FromRgba(0, 0, 0, 170);
        var drop = new UiVec2(canvas.Px(1.5f), canvas.Px(1.5f));
        canvas.Painter.FillTriangle(tip + drop, left + drop, notch + drop, under);
        canvas.Painter.FillTriangle(tip + drop, notch + drop, right + drop, under);
        canvas.Painter.FillTriangle(tip, left, notch, ink);
        canvas.Painter.FillTriangle(tip, notch, right, ink);
    }

    private static void DrawHostile(ComputerCanvas canvas, UiVec2 at)
    {
        // See-through, so a guard's mark does not hide the ground he stands on or the marks beside his.
        UiVectors.DrawDiamond(canvas.Painter, at, canvas.Px(6.5f), Bgra32.FromRgba(0, 0, 0, 70));
        UiVectors.DrawDiamond(canvas.Painter, at, canvas.Px(5f), ComputerCanvas.Fade(canvas.Palette.Danger, 0.55f));
    }

    // An objective: corner ticks about its place -- a target rather than a box -- and its number; done, it is quiet and
    // filled.
    private static void DrawObjective(ComputerCanvas canvas, UiVec2 at, int number, bool complete)
    {
        Bgra32 ink = complete ? canvas.Palette.Dim : canvas.Palette.Accent;
        float half = canvas.Px(9f);
        float tick = canvas.Px(5f);
        float weight = MathF.Max(1f, MathF.Round(canvas.Px(1.8f)));
        foreach ((float sx, float sy) in Corners)
        {
            float cornerX = at.X + (half * sx);
            float cornerY = at.Y + (half * sy);
            canvas.Painter.FillRect(new UiRect(sx < 0f ? cornerX : cornerX - tick, cornerY - (weight * 0.5f), tick, weight), ink);
            canvas.Painter.FillRect(new UiRect(cornerX - (weight * 0.5f), sy < 0f ? cornerY : cornerY - tick, weight, tick), ink);
        }

        if (complete)
        {
            UiVectors.FillDisc(canvas.Painter, at, canvas.Px(3f), ink, 10);
            return;
        }

        UiVectors.StrokeRing(canvas.Painter, at, canvas.Px(3f), weight, ink, 10);
        canvas.Text.DrawOnMap(
            string.Create(CultureInfo.InvariantCulture, $"{number:00}"),
            at.X + half + canvas.Px(5f),
            at.Y - (canvas.Text.LineHeight * 0.5f),
            ink);
    }

    // A place's name, led off its mark by a short line.
    private static void DrawLabel(ComputerCanvas canvas, string text, UiVec2 at)
    {
        float lead = canvas.Px(14f);
        float weight = canvas.Hairline;
        var elbow = new UiVec2(at.X + (lead * 0.6f), at.Y - (lead * 0.6f));
        canvas.Painter.StrokeLine(at, elbow, weight, ComputerCanvas.Fade(canvas.Palette.Primary, 0.8f));
        canvas.Painter.FillRect(new UiRect(elbow.X, MathF.Round(elbow.Y), lead * 0.5f, weight), ComputerCanvas.Fade(canvas.Palette.Primary, 0.8f));
        canvas.Text.DrawOnMap(text, elbow.X + (lead * 0.5f) + canvas.Px(4f), elbow.Y - (canvas.Text.LineHeight * 0.5f), canvas.Palette.Primary);
    }
}
