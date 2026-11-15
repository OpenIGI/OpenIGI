using System.Globalization;
using System.Text.Json;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Host.Infrastructure;

namespace OpenIGI.Host;

/// <content>
/// <c>mp_mapshot</c>: the loaded level seen from straight above, for laying out its multiplayer points by hand. The ground is
/// a picture -- every pixel a ray cast down onto the terrain, shaded by its height and slope -- and everything built on it is
/// written as its collision triangles, for a plan to be drawn from at any floor; a JSON file says which world box the picture
/// covers, and raw files hold the heights under each pixel, roofs and all and the bare ground.
/// </content>
public sealed partial class GameHost
{
    // The largest side the picture is made, the finest it is drawn, and how far it reaches past the level's outermost objects.
    private const int MapShotMaxPixels = 2048;
    private const double MapShotFinestMetres = 0.5;
    private const double MapShotMarginMetres = 120;

    // A map asked for on the command line before the level was up, written once it has settled, and the frame the level came
    // up on.
    private string? _mapShotPending;
    private long _mapShotSceneFrame = -1;

    private void RegisterMpMapShot(ConsoleSystem console)
    {
        console.Commands.Add(
            "mp_mapshot",
            "Writes the loaded level seen from above for laying out multiplayer maps: the ground <path>.bmp, the world box "
                + "<path>.json, the heights <path>.heights and <path>.ground, and the built triangles <path>.plan.",
            context =>
            {
                string path = context.Count > 0 ? context[0].Trim() : string.Empty;
                if (path.Length == 0)
                {
                    context.Error("Name the file to write, without its extension.");
                    return;
                }

                // Asked for before a level is up (on the command line): written once it has settled, and the run then ends.
                if (_scene is null)
                {
                    _mapShotPending = path;
                    return;
                }

                if (WriteMpMapShot(path) is { } problem)
                {
                    context.Error(problem);
                }
            },
            "<path>");
    }

    // Writes a map asked for on the command line once its level has been up a few seconds, and ends the run.
    private void AdvanceMapShot(FrameLoopState state)
    {
        if (_mapShotPending is not { } path || _scene is null || EnteringMission)
        {
            _mapShotSceneFrame = -1;
            return;
        }

        if (_mapShotSceneFrame < 0)
        {
            _mapShotSceneFrame = _loop.Clock.FrameCount;
        }

        if (_loop.Clock.FrameCount - _mapShotSceneFrame >= 90)
        {
            _mapShotPending = null;
            if (WriteMpMapShot(path) is { } problem)
            {
                _logger.Warning("Desktop", $"No overhead map: {problem}");
            }

            state.Running = false;
        }
    }

    /// <summary>The loaded level from straight above: its picture, the heights under it, and the world box it covers.</summary>
    /// <param name="Width">The picture's width in pixels.</param>
    /// <param name="Height">The picture's height in pixels.</param>
    /// <param name="Pixels">The picture, row by row from the north edge.</param>
    /// <param name="Heights">The surface's height under each pixel, roofs included, in world units; NaN where nothing is.</param>
    /// <param name="Ground">The bare ground's height under each pixel, in world units.</param>
    /// <param name="MinX">The west edge, in world units.</param>
    /// <param name="MaxY">The north edge, in world units.</param>
    /// <param name="UnitsPerPixel">How much of the world a pixel covers.</param>
    /// <param name="Top">The highest point the casts started from.</param>
    /// <param name="Bottom">The lowest they reached.</param>
    private sealed record Overhead(int Width, int Height, Bgra32[] Pixels, double[] Heights, double[] Ground, double MinX, double MaxY, double UnitsPerPixel, double Top, double Bottom)
    {
        public double MaxX => MinX + (Width * UnitsPerPixel);

        public double MinY => MaxY - (Height * UnitsPerPixel);

        public double Low { get; init; }

        public double High { get; init; }
    }

    /// <summary>The loaded level from above, for the editor's map; null when no level is up.</summary>
    /// <param name="maxPixels">The most pixels a side.</param>
    /// <returns>The map.</returns>
    internal Editor.OverheadMap? EditorOverheadMap(int maxPixels)
    {
        System.Diagnostics.Stopwatch took = System.Diagnostics.Stopwatch.StartNew();
        if (MakeOverhead(maxPixels, out _, roofs: false) is not { } overhead)
        {
            return null;
        }

        _logger.Information("Editor", string.Create(CultureInfo.InvariantCulture, $"The map took {took.ElapsedMilliseconds} ms."));
        double perMetre = WorldUnits.PerMetre;
        return new Editor.OverheadMap(overhead.Width, overhead.Height, overhead.Pixels, overhead.MinX / perMetre, overhead.MaxY / perMetre, overhead.UnitsPerPixel / perMetre)
        {
            HeightsMetres = [.. overhead.Heights.Select(h => (float)(h / perMetre))],
        };
    }

    // The level seen from above at no more than a number of pixels a side; null and the problem when there is no level.
    // The roofs are cast against everything built, which is most of the time a map takes; without them the ground is cast alone.
    private Overhead? MakeOverhead(int maxPixels, out string? problem, bool roofs = true)
    {
        if (_mission.EffectWalls is not { } walls)
        {
            problem = "No level is loaded.";
            return null;
        }

        if (OverheadBox(out problem) is not { } box)
        {
            return null;
        }

        (double minX, double maxX, double minY, double maxY, double top, double bottom) = box;

        double perPixel = Math.Max(MapShotFinestMetres * WorldUnits.PerMetre, Math.Max(maxX - minX, maxY - minY) / maxPixels);
        int width = Math.Max(1, (int)Math.Ceiling((maxX - minX) / perPixel));
        int height = Math.Max(1, (int)Math.Ceiling((maxY - minY) / perPixel));

        // Each pixel's height, cast from above its middle, onto everything and onto the bare ground; north (the greater Y) at
        // the top.
        WorldRayCast? land = _mission.Collision.Terrain is { } terrain ? WorldTrace.AsRayCast(TerrainRayTrace.Caster(terrain)) : null;
        double[] heights = new double[width * height];
        double[] ground = new double[width * height];
        float[] bare = new float[width * height];
        void Row(int row)
        {
            double y = maxY - ((row + 0.5) * perPixel);
            for (int column = 0; column < width; column++)
            {
                double x = minX + ((column + 0.5) * perPixel);
                int i = (row * width) + column;
                heights[i] = roofs && walls(new Vector3d(x, y, top), new Vector3d(x, y, bottom), out WorldRayHit hit) ? hit.Position.Z : double.NaN;
                ground[i] = land is not null && land(new Vector3d(x, y, top), new Vector3d(x, y, bottom), out WorldRayHit under)
                    ? under.Position.Z
                    : heights[i];
                if (!roofs)
                {
                    heights[i] = ground[i];
                }

                bare[i] = -1f;
            }
        }

        // The terrain cast reads the terrain and keeps nothing (TerrainRayTrace.TryTrace), so rows of it run side by side; the
        // walls' cast goes through the scene's collision, which is only ever asked from one thread, so it keeps to this one.
        if (roofs)
        {
            for (int row = 0; row < height; row++)
            {
                Row(row);
            }
        }
        else
        {
            Parallel.For(0, height, Row);
        }

        double[] known = [.. ground.Where(static h => !double.IsNaN(h))];
        double low = known.Length > 0 ? Percentile(known, 0.02) : 0;
        double high = known.Length > 0 ? Percentile(known, 0.98) : 1;
        Bgra32[] pixels = new Bgra32[width * height];
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                int i = (row * width) + column;
                pixels[i] = Shade(ground, bare, width, height, row, column, perPixel, low, high);
            }
        }

        return new Overhead(width, height, pixels, heights, ground, minX, maxY, perPixel, top, bottom) { Low = low, High = high };
    }

    // The picture and its box; a problem, or null once written.
    private string? WriteMpMapShot(string path)
    {
        if (MakeOverhead(MapShotMaxPixels, out string? made) is not { } overhead)
        {
            return made;
        }

        (int width, int height, Bgra32[] pixels, double[] heights, double[] ground, double minX, double maxY, double perPixel, double top, double bottom) = overhead;
        (double maxX, double minY, double low, double high) = (overhead.MaxX, overhead.MinY, overhead.Low, overhead.High);
        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        Directory.CreateDirectory(directory);
        BitmapWriter.Write($"{path}.bmp", width, height, pixels);

        // The surface's height under every pixel, in world units, as little-endian floats row by row (NaN where nothing was
        // hit): what a layout's point is set on, or under when it is indoors.
        WriteHeights($"{path}.heights", heights);
        WriteHeights($"{path}.ground", ground);

        // Everything built in the box, as its collision triangles in world units: a count, then nine little-endian floats a
        // triangle (three corners of x, y and z). The plan is drawn from these, cut at whatever floor is being laid out.
        List<CollisionTriangle> triangles = [];
        _mission.Solids?.CollectTriangles(new Vector3d(minX, minY, bottom), new Vector3d(maxX, maxY, top), triangles);
        using (BinaryWriter raw = new(File.Create($"{path}.plan")))
        {
            raw.Write(triangles.Count);
            foreach (CollisionTriangle t in triangles)
            {
                WriteCorner(raw, t.A);
                WriteCorner(raw, t.B);
                WriteCorner(raw, t.C);
            }
        }
        if (WriteMapMesh(path, BuildOverheadScene(minX, maxX, minY, maxY, bottom, top, int.MaxValue)) is { } meshProblem)
        {
            return meshProblem;
        }
        using (FileStream file = File.Create($"{path}.json"))
        using (Utf8JsonWriter json = new(file, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("mission", _session.Mission?.Id.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            json.WriteNumber("unitsPerMetre", WorldUnits.PerMetre);
            json.WriteNumber("minX", minX);
            json.WriteNumber("maxX", maxX);
            json.WriteNumber("minY", minY);
            json.WriteNumber("maxY", maxY);
            json.WriteNumber("width", width);
            json.WriteNumber("height", height);
            json.WriteNumber("unitsPerPixel", perPixel);
            json.WriteString("north", "+Y is up the picture, +X to its right");
            json.WriteNumber("groundLow", low);
            json.WriteNumber("groundHigh", high);
            if (TitlePlayer is { } player)
            {
                Vector3d start = player.Position;
                json.WriteStartArray("playerStart");
                json.WriteNumberValue(start.X);
                json.WriteNumberValue(start.Y);
                json.WriteNumberValue(start.Z);
                json.WriteEndArray();
            }

            json.WriteEndObject();
        }

        _logger.Information("Desktop", $"Wrote the overhead map {path}.bmp ({width}x{height}, {perPixel / WorldUnits.PerMetre:0.##} m a pixel).");
        return null;
    }

    // A pixel's colour: the ground green, darker low and paler high and gently hill-shaded; a built thing a light stone grey,
    // shaded by its slope and edged dark where it meets the ground or a lower part of itself; nothing hit, black.
    private static Bgra32 Shade(double[] heights, float[] upness, int width, int height, int row, int column, double perPixel, double low, double high)
    {
        int i = (row * width) + column;
        double h = heights[i];
        if (double.IsNaN(h))
        {
            return Bgra32.FromRgb(0, 0, 0);
        }

        double Height(int r, int c)
        {
            r = Math.Clamp(r, 0, height - 1);
            c = Math.Clamp(c, 0, width - 1);
            double v = heights[(r * width) + c];
            return double.IsNaN(v) ? h : v;
        }

        bool Built(int r, int c) => upness[(Math.Clamp(r, 0, height - 1) * width) + Math.Clamp(c, 0, width - 1)] > 0f;

        static byte Channel(double v) => (byte)Math.Clamp(v, 0, 255);
        double dx = (Height(row, column + 1) - Height(row, column - 1)) / (2 * perPixel);
        double dy = (Height(row - 1, column) - Height(row + 1, column)) / (2 * perPixel);
        bool built = upness[i] > 0f;
        if (built)
        {
            // An edge: a neighbour that is ground, or a drop of more than a metre and a half.
            double step = 1.5 * WorldUnits.PerMetre;
            bool edge = !Built(row, column - 1) || !Built(row, column + 1) || !Built(row - 1, column) || !Built(row + 1, column)
                || Math.Abs(Height(row, column + 1) - h) > step || Math.Abs(Height(row + 1, column) - h) > step;
            if (edge)
            {
                return Bgra32.FromRgb(28, 26, 24);
            }

            double lift = Math.Clamp((h - low) / Math.Max(1, high - low), 0, 1) * 30;
            double lit = Math.Clamp(0.85 + ((-dx + dy) * 0.25), 0.6, 1.1) * upness[i];
            return Bgra32.FromRgb(Channel((175 + lift) * lit), Channel((168 + lift) * lit), Channel((152 + lift) * lit));
        }

        double t = Math.Clamp((h - low) / Math.Max(1, high - low), 0, 1);
        double light = Math.Clamp(0.9 + ((-dx + dy) * 0.18), 0.65, 1.1);
        return Bgra32.FromRgb(Channel((44 + (t * 120)) * light), Channel((70 + (t * 110)) * light), Channel((40 + (t * 80)) * light));
    }

    private static void WriteCorner(BinaryWriter raw, Vector3d corner)
    {
        raw.Write((float)corner.X);
        raw.Write((float)corner.Y);
        raw.Write((float)corner.Z);
    }

    private static void WriteHeights(string path, double[] heights)
    {
        using BinaryWriter raw = new(File.Create(path));
        foreach (double h in heights)
        {
            raw.Write((float)h);
        }
    }

    private static double Percentile(IEnumerable<double> values, double fraction)
    {
        double[] sorted = [.. values.Order()];
        return sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Round(fraction * (sorted.Length - 1)), 0, sorted.Length - 1)];
    }
}
