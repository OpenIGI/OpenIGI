using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Vertical lines against mission 1's ground, <c>Terrain_GetNextZHandler</c> (<c>igi2 0x572E80</c>).</summary>
public sealed class RetailIgi2TerrainColliderTests
{
    private static readonly Lazy<Igi2TerrainSurface> Mission1 = new(static () => Igi2Level.Find("location1", "level1").Terrain());

    /// <summary>A line down through the ground strikes it where the ground query stands a body, with the triangle's normal and the material's level slot.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_line_down_through_the_ground_strikes_it()
    {
        Igi2TerrainSurface surface = Mission1.Value;
        Igi2TerrainCollider collider = new(surface, "terrain");
        int struck = 0;
        foreach (Vector3d at in Samples(surface))
        {
            Igi2LineTrace trace = Vertical(at, 4096.0, -4096.0);
            collider.NextZ(trace);

            Igi2Intersection hit = trace.Intersections[0];
            Assert.True(hit.IsHit);
            Assert.Equal("terrain", hit.HitObject);

            Vector3d origin = surface.Origin;
            Assert.True(Igi2TerrainVertex.TrySample(surface.Tiles, ((Fpu)at.X - origin.X).F, ((Fpu)at.Y - origin.Y).F, out Igi2TerrainSample sample));
            double height = ((Fpu)sample.RelativeZ + origin.Z).Value;
            Assert.Equal(new Vector3d(at.X, at.Y, height), hit.Position);
            Assert.Equal(((Fpu)(at.Z + 4096.0) - height).Value, hit.Distance);
            Assert.Equal(sample.Normal, hit.Normal);
            Assert.Equal(sample.Normal, hit.LocalNormal);
            Assert.Equal(collider.LevelMaterial(sample.Material), hit.EnterMaterial);
            Assert.Null(hit.LeaveMaterial);
            Assert.InRange(hit.Light, 0f, 2f);

            Assert.True(new Igi2Ground(surface).TryGetGround(at.X, at.Y, at.Z + 4096.0, 8192.0, out TerrainGround ground));
            Assert.Equal(ground.Height, hit.Position.Z);
            struck++;
        }

        Assert.True(struck > 20, $"only {struck} lines were cast.");
    }

    /// <summary>A line that stays above the ground, or one that already holds a nearer hit, is left alone.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_line_that_misses_or_already_holds_a_nearer_hit_is_left_alone()
    {
        Igi2TerrainSurface surface = Mission1.Value;
        Igi2TerrainCollider collider = new(surface, "terrain");
        Vector3d at = Samples(surface).First();

        Igi2LineTrace above = Vertical(at, 8192.0, 4096.0);
        collider.NextZ(above);
        Assert.False(above.Intersections[0].IsHit);

        Igi2LineTrace held = Vertical(at, 4096.0, -4096.0);
        Igi2Intersection nearer = held.Intersections[0];
        nearer.IsHit = true;
        nearer.Distance = 1.0;
        nearer.HitObject = "crate";
        collider.NextZ(held);
        Assert.Equal("crate", nearer.HitObject);
    }

    /// <summary>The hangar's discard box leaves no ground under its middle for a line to strike.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_hangar_has_no_ground_to_strike()
    {
        Igi2TerrainSurface surface = Mission1.Value;
        Igi2DiscardBox box = Assert.Single(surface.DiscardBoxes);
        Vector3d middle = new(
            surface.Origin.X + ((box.MinX + (double)box.MaxX) * 0.5),
            surface.Origin.Y + ((box.MinY + (double)box.MaxY) * 0.5),
            0.0);

        Igi2LineTrace trace = new(1);
        trace.Starts[0] = new Vector3d(middle.X, middle.Y, 4096.0 * 1000.0);
        trace.Ends[0] = new Vector3d(middle.X, middle.Y, -4096.0 * 1000.0);
        new Igi2TerrainCollider(surface, "terrain").NextZ(trace);

        Assert.False(trace.Intersections[0].IsHit);
    }

    /// <summary>
    /// Where three corners of a cell share a material, the odd corner's owns the cell beyond the diagonal a half cell
    /// from it, and the rest of the cell is the three's.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Three_agreeing_corners_split_the_cell_on_a_diagonal()
    {
        Igi2TerrainSurface surface = Mission1.Value;
        int index = surface.Tiles.Count - 1;
        Igi2TerrainTile tile = surface.Tiles[index];
        Assert.NotNull(tile.Materials);

        int cells = 0;
        for (int y = 1; y < tile.Height - 2 && cells < 10; y++)
        {
            for (int x = 1; x < tile.Width - 2 && cells < 10; x++)
            {
                byte m00 = tile.Materials.GetMaterial(x, y), m10 = tile.Materials.GetMaterial(x + 1, y);
                byte m01 = tile.Materials.GetMaterial(x, y + 1), m11 = tile.Materials.GetMaterial(x + 1, y + 1);
                if (m00 != m10 || m00 != m01 || m00 == m11 || !Inside(surface, tile, index, x, y))
                {
                    continue;
                }

                // u + v = 1.4 is on the three's side of the 1.5 diagonal; 1.6 is past it.
                Assert.Equal(m00, MaterialAt(surface, tile, x, y, 0.7));
                Assert.Equal(m11, MaterialAt(surface, tile, x, y, 0.8));
                cells++;
            }
        }

        Assert.True(cells > 0, "no cell of the fine tile has three agreeing corners.");
    }

    private static bool Inside(Igi2TerrainSurface surface, Igi2TerrainTile tile, int index, int x, int y) =>
        Sample(surface, tile, x, y, 0.5).Tile == index;

    private static byte MaterialAt(Igi2TerrainSurface surface, Igi2TerrainTile tile, int x, int y, double fraction) =>
        Sample(surface, tile, x, y, fraction).Material;

    private static Igi2TerrainSample Sample(Igi2TerrainSurface surface, Igi2TerrainTile tile, int x, int y, double fraction)
    {
        float relativeX = (float)(tile.SampleX(x) + (fraction * tile.TexelWidth) - surface.Origin.X);
        float relativeY = (float)(tile.SampleY(y) + (fraction * tile.TexelHeight) - surface.Origin.Y);
        Assert.True(Igi2TerrainVertex.TrySample(surface.Tiles, relativeX, relativeY, out Igi2TerrainSample sample));
        return sample;
    }

    /// <summary>Sample positions of the fine tile that have ground: not discarded.</summary>
    private static IEnumerable<Vector3d> Samples(Igi2TerrainSurface surface)
    {
        Igi2TerrainTile tile = surface.Tiles[^1];
        for (int y = 3; y < tile.Height - 3; y += 31)
        {
            for (int x = 3; x < tile.Width - 3; x += 29)
            {
                Vector3d at = tile.PositionOf(x, y);
                if (!surface.IsDiscarded(at.X, at.Y))
                {
                    yield return at;
                }
            }
        }
    }

    private static Igi2LineTrace Vertical(Vector3d at, double above, double below)
    {
        Igi2LineTrace trace = new(1);
        trace.Starts[0] = new Vector3d(at.X, at.Y, at.Z + above);
        trace.Ends[0] = new Vector3d(at.X, at.Y, at.Z + below);
        return trace;
    }
}
