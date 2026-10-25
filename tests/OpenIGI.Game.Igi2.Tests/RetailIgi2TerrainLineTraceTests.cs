using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;
using OpenIGI.TestSupport;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Lines in any direction against mission 1's ground, <c>Terrain_TraceLinesHandler1</c> (<c>igi2 0x5730B0</c>).</summary>
public sealed class RetailIgi2TerrainLineTraceTests
{
    private static readonly Lazy<Igi2TerrainSurface> Mission1 = new(static () => Igi2Level.Find("location1", "level1").Terrain());

    /// <summary>A line straight down strikes the ground inside its cell's span of heights, at the distance it travelled.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_line_straight_down_strikes_the_cell_it_falls_through()
    {
        Igi2TerrainSurface surface = Mission1.Value;
        Igi2TerrainCollider collider = new(surface, "terrain");
        int struck = 0;
        foreach (Vector3d at in Points(surface))
        {
            WorldLineTrace trace = Line(new Vector3d(at.X, at.Y, at.Z + 8192.0), new Vector3d(at.X, at.Y, at.Z - 8192.0));
            collider.TraceLines(trace);

            WorldIntersection hit = trace.Intersections[0];
            Assert.True(hit.IsHit, $"the line down at {at} missed.");
            Assert.Same(collider, hit.HitObject);
            AssertOnGround(surface, hit);
            Assert.InRange(hit.Distance, Distance(trace.Starts[0], hit.Position) - 1.0, Distance(trace.Starts[0], hit.Position) + 1.0);
            Assert.True(hit.Normal.Z > 0f, "the ground faces down.");
            struck++;
        }

        Assert.True(struck > 20, $"only {struck} lines were cast.");
    }

    /// <summary>A slanted line through the ground stops on it, nearer its start than its end.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_slanted_line_stops_where_it_enters_the_ground()
    {
        Igi2TerrainSurface surface = Mission1.Value;
        Igi2TerrainCollider collider = new(surface, "terrain");
        int struck = 0;
        foreach (Vector3d at in Points(surface))
        {
            Vector3d start = new(at.X - 12000.0, at.Y + 7000.0, at.Z + 16384.0);
            Vector3d end = new(at.X + 3000.0, at.Y - 1750.0, at.Z - 4096.0);
            WorldLineTrace trace = Line(start, end);
            collider.TraceLines(trace);

            WorldIntersection hit = trace.Intersections[0];
            if (!hit.IsHit)
            {
                continue;
            }

            AssertOnGround(surface, hit);
            Assert.True(hit.Distance < Distance(start, end));
            struck++;
        }

        Assert.True(struck > 20, $"only {struck} slanted lines struck.");
    }

    /// <summary>The title-neutral cast meets the ground where the game's own line trace does.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_neutral_cast_meets_the_ground_the_line_trace_meets()
    {
        Igi2TerrainSurface surface = Mission1.Value;
        Igi2TerrainCollider collider = new(surface, "terrain");
        Igi2CollisionWorld world = new([], collider);
        Vector3d at = Points(surface).First();
        Vector3d start = new(at.X, at.Y, at.Z + 8192.0), end = new(at.X, at.Y, at.Z - 8192.0);
        WorldLineTrace trace = Line(start, end);
        collider.TraceLines(trace);

        Assert.True(world.Cast(start, end, out Game.World.WorldRayHit hit));
        Assert.Equal(trace.Intersections[0].Position, hit.Position);
        Assert.Equal(trace.Intersections[0].Distance, hit.Distance);
        Assert.False(world.Cast(start, new Vector3d(at.X, at.Y, at.Z + 4096.0), out _));
    }

    /// <summary>A line that stays well above the ground strikes nothing.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_line_above_the_ground_misses()
    {
        Igi2TerrainSurface surface = Mission1.Value;
        Igi2TerrainCollider collider = new(surface, "terrain");
        Vector3d at = Points(surface).First();
        WorldLineTrace trace = Line(new Vector3d(at.X - 8192.0, at.Y, at.Z + 409600.0), new Vector3d(at.X + 8192.0, at.Y, at.Z + 409600.0));

        collider.TraceLines(trace);

        Assert.False(trace.Intersections[0].IsHit);
    }

    /// <summary>The world's line trace reaches the ground when the terrain group is asked for.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_world_traces_lines_against_the_ground()
    {
        Igi2TerrainSurface surface = Mission1.Value;
        Igi2CollisionWorld world = new([], new Igi2TerrainCollider(surface, "terrain"));
        Vector3d at = Points(surface).First();
        WorldLineTrace trace = Line(new Vector3d(at.X, at.Y, at.Z + 4096.0), new Vector3d(at.X, at.Y, at.Z - 4096.0));

        world.TraceLines(trace, 0xFFFFFFFF);
        Assert.True(trace.Intersections[0].IsHit);

        world.TraceLines(trace, Igi2CollisionWorld.EveryTypeGroup);
        Assert.False(trace.Intersections[0].IsHit);
    }

    /// <summary>The hit lies over a cell whose corner heights bracket it.</summary>
    private static void AssertOnGround(Igi2TerrainSurface surface, WorldIntersection hit)
    {
        Vector3d origin = surface.Origin;
        Assert.True(Igi2TerrainVertex.TrySample(surface.Tiles, ((Fpu)hit.Position.X - origin.X).F, ((Fpu)hit.Position.Y - origin.Y).F, out Igi2TerrainSample sample));
        Igi2TerrainTile tile = surface.Tiles[sample.Tile];
        int x = Math.Clamp((int)Math.Floor(tile.ColumnAt(hit.Position.X)), 0, tile.Width - 2);
        int y = Math.Clamp((int)Math.Floor(tile.RowAt(hit.Position.Y)), 0, tile.Height - 2);
        float[] corners =
        [
            tile.Heights.GetHeight(x, y), tile.Heights.GetHeight(x + 1, y),
            tile.Heights.GetHeight(x, y + 1), tile.Heights.GetHeight(x + 1, y + 1),
        ];
        double low = corners.Min() + origin.Z - 1.0, high = corners.Max() + origin.Z + 1.0;
        Assert.InRange(hit.Position.Z, low, high);
        Assert.Equal(new Vector3f(((Fpu)hit.Position.X - origin.X).F, ((Fpu)hit.Position.Y - origin.Y).F, ((Fpu)hit.Position.Z - origin.Z).F), hit.LocalPosition);
        Assert.Equal(hit.Normal, hit.LocalNormal);
    }

    private static double Distance(Vector3d a, Vector3d b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    /// <summary>Ground points of the fine tile off its samples (a line through a sample's corner touches no triangle's inside), not discarded.</summary>
    private static IEnumerable<Vector3d> Points(Igi2TerrainSurface surface)
    {
        Igi2TerrainTile tile = surface.Tiles[^1];
        for (int y = 3; y < tile.Height - 3; y += 31)
        {
            for (int x = 3; x < tile.Width - 3; x += 29)
            {
                Vector3d corner = tile.PositionOf(x, y);
                double atX = corner.X + (0.37 * tile.TexelWidth), atY = corner.Y + (0.21 * tile.TexelHeight);
                if (!surface.IsDiscarded(atX, atY)
                    && Igi2TerrainVertex.TrySample(surface.Tiles, ((Fpu)atX - surface.Origin.X).F, ((Fpu)atY - surface.Origin.Y).F, out Igi2TerrainSample sample))
                {
                    yield return new Vector3d(atX, atY, sample.RelativeZ + surface.Origin.Z);
                }
            }
        }
    }

    private static WorldLineTrace Line(Vector3d start, Vector3d end)
    {
        WorldLineTrace trace = new(1);
        trace.Starts[0] = start;
        trace.Ends[0] = end;
        return trace;
    }
}
