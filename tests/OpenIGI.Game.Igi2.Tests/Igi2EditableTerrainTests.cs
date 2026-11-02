using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// I.G.I. 2's ground opened for editing on a shipped level: an edit reaches the ground the player stands on at once, the
/// quadtree's height bounds follow it, and saving writes a <c>heightmaps.res</c> that differs from the shipped one only
/// in what was changed.
/// </summary>
public sealed class Igi2EditableTerrainTests
{
    /// <summary>A sample raised through the editing view is where the ground query finds the ground.</summary>
    [RequiresTitleDataFact("igi2")]
    public void An_edit_reaches_the_ground_query()
    {
        Igi2TerrainSurface surface = Igi2Level.Find("location1", "level1").Terrain();
        Igi2EditableTerrain editing = surface.Editing;
        ITerrainHeightGrid grid = editing.Grids[^1];
        int x = grid.Width / 2;
        int y = grid.Height / 2;
        Assert.False(grid.IsChanged);

        double raised = grid.GetHeight(x, y) + 25.0;
        grid.SetHeight(x, y, raised);
        Assert.True(grid.IsChanged);
        Assert.Equal(raised, grid.GetHeight(x, y), 3);

        (double sx, double sy) = grid.SampleMetres(x, y);
        Assert.True(editing.TryGetHeight(sx, sy, out double found));
        Assert.Equal(raised, found, 3);

        Igi2Ground ground = new(surface);
        // Asked from just above where the ground should now be, as a body standing on it would.
        double probe = (raised + 0.5) * WorldScene.UnitsPerMetre;
        Assert.True(ground.TryGetGround(sx * WorldScene.UnitsPerMetre, sy * WorldScene.UnitsPerMetre, probe, 0.0, out TerrainGround standing));
        Assert.Equal(raised, standing.Height / WorldScene.UnitsPerMetre, 2);
    }

    /// <summary>Updating the height bounds over a changed region gives the bounds a full rebuild would.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Bounds_follow_an_edit()
    {
        Igi2TerrainSurface surface = Igi2Level.Find("location1", "level1").Terrain();
        Igi2TerrainTile tile = surface.Tiles[0];
        Igi2TerrainHeightPyramid pyramid = Igi2TerrainHeightPyramid.Build(tile);
        for (int y = 10; y <= 14; y++)
        {
            for (int x = 20; x <= 23; x++)
            {
                tile.Heights.SetHeight(x, y, tile.Heights.GetHeight(x, y) + 100_000f);
            }
        }

        pyramid.Update(tile, 20, 10, 23, 14);
        Igi2TerrainHeightPyramid rebuilt = Igi2TerrainHeightPyramid.Build(tile);
        for (int level = 0; level < pyramid.LevelCount; level++)
        {
            int blocks = Math.Max(1, (Math.Max(tile.Width, tile.Height) >> level) + 1);
            for (int by = 0; by < blocks; by++)
            {
                for (int bx = 0; bx < blocks; bx++)
                {
                    pyramid.GetBounds(level, bx, by, out float low, out float high);
                    rebuilt.GetBounds(level, bx, by, out float expectedLow, out float expectedHigh);
                    Assert.Equal(expectedLow, low);
                    Assert.Equal(expectedHigh, high);
                }
            }
        }
    }

    /// <summary>
    /// Saving rewrites the level's archive with the edited height map in it: read back, the edit is there, and every
    /// other entry is the shipped one byte for byte. With nothing changed there is nothing to save.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Saving_rewrites_only_what_changed()
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        byte[] shipped = File.ReadAllBytes(Path.Combine(level.Directory, "heightmaps", "heightmaps.res"));
        Igi2EditableTerrain editing = level.Terrain().Editing;
        ReadOnlyMemory<byte>? Read(string path) =>
            path.Equals("heightmaps/heightmaps.res", StringComparison.OrdinalIgnoreCase) ? shipped : null;

        Assert.Empty(editing.ChangedFiles(Read));

        ITerrainHeightGrid grid = editing.Grids[0];
        grid.SetHeight(3, 4, grid.GetHeight(3, 4) - 7.5);
        (string path, byte[] bytes) = Assert.Single(editing.ChangedFiles(Read));
        Assert.Equal("heightmaps/heightmaps.res", path);
        Assert.Equal(shipped.Length, bytes.Length);

        IresArchive before = IresArchive.Read(shipped);
        IresArchive after = IresArchive.Read(bytes);
        int differing = 0;
        for (int i = 0; i < before.Entries.Count; i++)
        {
            Assert.Equal(before.Entries[i].Name, after.Entries[i].Name);
            differing += before.Entries[i].Data.Span.SequenceEqual(after.Entries[i].Data.Span) ? 0 : 1;
        }

        Assert.Equal(1, differing);
        Assert.InRange(shipped.Zip(bytes).Count(static p => p.First != p.Second), 1, 4);

        editing.MarkSaved();
        Assert.False(grid.IsChanged);
        Assert.Empty(editing.ChangedFiles(Read));
    }
}
