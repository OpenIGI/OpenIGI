using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The holes a level's <c>DiscardTask</c>s cut through its ground.</summary>
/// <remarks>
/// <c>DiscardTask_IsDiscardVertex</c> (<c>igi2ed 0x65A670</c>) and the AND in
/// <c>TerrainRender_DrawFace</c> (<c>0x66AAB0</c>) are what these measure against. The level that proves
/// it is <c>location1/level1</c>: it declares exactly one box, and the box is the vehicle hangar dug
/// into the hillside whose ramp the ground was otherwise drawn straight across.
/// </remarks>
public sealed class RetailIgi2TerrainDiscardTests
{
    /// <summary>Four of the shipped levels declare the type, and they declare fifteen boxes between them.</summary>
    /// <remarks>
    /// Counted from the data rather than assumed: a <c>DiscardTask</c> that failed to bind would load as
    /// a placeholder with no parameters and simply not appear here, which is the shape the bug had.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Fifteen_discard_boxes_are_authored_across_four_levels()
    {
        int levels = 0;
        int boxes = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = Surface(level);
            if (surface.Discards.Length == 0)
            {
                continue;
            }

            levels++;
            boxes += surface.Discards.Length;
        }

        Assert.Equal(4, levels);
        Assert.Equal(15, boxes);
    }

    /// <summary>The hangar box is the level's own numbers, halved and scaled the way the original scales them.</summary>
    /// <remarks>
    /// <c>location1/level1</c> authors the task at <c>(-1891183.4, 667326.9, 77883.28)</c> with a width
    /// of 56.5 and a height of 54. <c>DiscardTask_UpdateInternalDataHandler</c> (<c>0x65AEE0</c>) turns
    /// those into <c>±Width * 2048</c> and <c>±Height * 2048</c> — full spans in metres, not half ones,
    /// which is the one thing a transcription of this can get wrong without anything else noticing.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_hangar_box_spans_the_authored_width_and_height()
    {
        Igi2TerrainSurface surface = Surface(Hangar());
        Igi2TerrainDiscard box = Assert.Single(surface.Discards);

        Assert.Equal(-1891183.4 - (56.5 * 2048.0), box.MinX, 1);
        Assert.Equal(-1891183.4 + (56.5 * 2048.0), box.MaxX, 1);
        Assert.Equal(667326.9 - (54.0 * 2048.0), box.MinY, 1);
        Assert.Equal(667326.9 + (54.0 * 2048.0), box.MaxY, 1);

        // The Z is carried by the declaration and read by nobody: the test is flat, so the box covers
        // the ramp's floor and the hillside above it alike.
        Assert.True(surface.IsDiscarded(-1891183.4, 667326.9));
        Assert.False(surface.IsDiscarded(box.MinX - 1.0, 667326.9));
        Assert.False(surface.IsDiscarded(-1891183.4, box.MaxY + 1.0));
    }

    /// <summary>No triangle survives with all three corners inside the box, and the ground around it does.</summary>
    /// <remarks>
    /// Both halves matter. Dropping the triangles is the fix; keeping the ones that straddle the edge is
    /// what the original's AND buys, and a naive per-triangle centre test would cut a ragged hole one
    /// square larger than the box on every side.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_mesh_drops_every_face_wholly_inside_the_box_and_no_other()
    {
        Igi2TerrainSurface surface = Surface(Hangar());
        Igi2TerrainDiscard box = Assert.Single(surface.Discards);

        int cut = 0;
        int straddling = 0;

        foreach (Igi2TerrainTile tile in surface.Tiles)
        {
            Igi2TerrainMesh with = Igi2TerrainMesh.Build(tile, surface.Materials, surface.Discards);
            Igi2TerrainMesh without = Igi2TerrainMesh.Build(tile, surface.Materials);

            foreach (Igi2TerrainPass pass in with.Passes)
            {
                ReadOnlySpan<int> indices = pass.Indices.Span;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int inside = 0;
                    for (int corner = 0; corner < 3; corner++)
                    {
                        Vector3d at = with.Positions[indices[i + corner]];
                        inside += box.Contains(at.X, at.Y) ? 1 : 0;
                    }

                    Assert.True(inside < 3, $"tile {tile.Id} still draws a face wholly inside the hangar.");
                    straddling += inside > 0 ? 1 : 0;
                }
            }

            cut += without.TriangleCount - with.TriangleCount;
        }

        Assert.True(cut > 0, "the hangar removed no ground at all.");
        Assert.True(straddling > 0, "the hole has no edge: the ground does not reach the hangar's walls.");
    }

    /// <summary>A body over the hangar gets no terrain, and one just outside it still does.</summary>
    /// <remarks>
    /// <c>Terrain_GetNextZHandler</c> (<c>0x62E620</c>) refuses the surface before it is kept, so this is
    /// not a rendering-only hole: the floor a player walks on inside the hangar is the building's, which
    /// is the whole reason the ground is taken away.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_ground_refuses_a_point_inside_the_box()
    {
        Igi2TerrainSurface surface = Surface(Hangar());
        Igi2TerrainDiscard box = Assert.Single(surface.Discards);
        IGroundHeightField ground = new Igi2Ground(surface);

        double midX = (box.MinX + box.MaxX) * 0.5;
        double midY = (box.MinY + box.MaxY) * 0.5;

        Assert.False(
            ground.TryGetGround(midX, midY, 77883.28, out _),
            "the hangar's middle still has terrain under it.");

        // A metre outside the west edge, at the height the level authored the task at. The probe window
        // is generous downward, so this asks whether the box is the only thing refusing.
        Assert.True(
            ground.TryGetGround(box.MinX - 4096.0, midY, 77883.28, out _),
            "the ground beside the hangar went with it.");
    }

    /// <summary>A level with no discards is untouched by any of this.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_level_without_discards_builds_the_same_mesh_either_way()
    {
        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = Surface(level);
            if (surface.Discards.Length != 0 || surface.IsEmpty)
            {
                continue;
            }

            Igi2TerrainTile tile = surface.Tiles[0];
            Assert.Equal(
                Igi2TerrainMesh.Build(tile, surface.Materials).TriangleCount,
                Igi2TerrainMesh.Build(tile, surface.Materials, surface.Discards).TriangleCount);
            return;
        }

        Assert.Fail("no level without discards was measured.");
    }

    private static Igi2Level Hangar() =>
        Igi2Level.All().Single(level =>
            Path.GetFileName(level.Directory).Equals("level1", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(Path.GetDirectoryName(level.Directory)!)
                .Equals("location1", StringComparison.OrdinalIgnoreCase));

    private static Igi2TerrainSurface Surface(Igi2Level level)
    {
        string path = Path.Combine(level.Directory, "heightmaps", "heightmaps.res");
        IresArchive? archive = File.Exists(path) ? IresArchive.Read(File.ReadAllBytes(path)) : null;
        return Igi2TerrainSurface.Build(level.Root, archive);
    }
}
