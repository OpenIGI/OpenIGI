using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The ground an I.G.I. 2 level puts under a body.</summary>
/// <remarks>
/// <c>Terrain_VertexFunction</c> (<c>igi2 0x5706F0</c>) is what these measure against: the tile choice,
/// the cell, the triangle, the height and the normal. The one thing data can prove about all of it at
/// once is that the level's own player lands on it, which is the last assertion here and the reason the
/// rest exists.
/// </remarks>
public sealed class RetailIgi2GroundTests
{
    /// <summary>A sample's own position reads back that sample's height.</summary>
    /// <remarks>
    /// The interpolation is exact at the grid points whichever of the cell's two triangles it lands on,
    /// so this pins the index arithmetic — a half-texel error in either direction moves the answer to a
    /// neighbouring sample and shows up here immediately.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_samples_own_position_reads_back_its_own_height()
    {
        int checks = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = Surface(level);
            if (surface.IsEmpty)
            {
                continue;
            }

            IGroundHeightField ground = new Igi2Ground(surface);

            // The finest tile, so nothing else supersedes the samples being asked about.
            Igi2TerrainTile tile = surface.Tiles[^1];

            for (int y = 1; y < tile.Height - 1; y += 37)
            {
                for (int x = 1; x < tile.Width - 1; x += 41)
                {
                    Vector3d at = tile.PositionOf(x, y);
                    // A sample a DiscardTask removed has no ground by design -- see
                    // RetailIgi2TerrainDiscardTests -- so it is not this test's business.
                    if (surface.IsDiscarded(at.X, at.Y))
                    {
                        continue;
                    }

                    Assert.True(
                        ground.TryGetGround(at.X, at.Y, at.Z, out TerrainGround found),
                        $"Tile {tile.Id} sample ({x}, {y}) has no ground under its own position.");

                    Assert.Equal(at.Z, found.Height, 3);
                    checks++;
                }
            }
        }

        Assert.True(checks > 0, "no level was measured.");
    }

    /// <summary>The normal is a unit vector, and the ground faces up.</summary>
    /// <remarks>
    /// The original builds it from the two slopes of the same triangle the height came from and
    /// normalises <c>(dzdx, dzdy, 1)</c> — so Z is positive by construction, and the length is what says
    /// the port normalised rather than merely divided by something.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_ground_faces_up_and_its_normal_is_unit_length()
    {
        int steep = 0;
        int checks = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = Surface(level);
            if (surface.IsEmpty)
            {
                continue;
            }

            IGroundHeightField ground = new Igi2Ground(surface);
            Igi2TerrainTile tile = surface.Tiles[^1];

            for (int y = 1; y < tile.Height - 1; y += 53)
            {
                for (int x = 1; x < tile.Width - 1; x += 47)
                {
                    Vector3d at = tile.PositionOf(x, y);
                    // A sample a DiscardTask removed has no ground by design -- see
                    // RetailIgi2TerrainDiscardTests -- so it is not this test's business.
                    if (surface.IsDiscarded(at.X, at.Y))
                    {
                        continue;
                    }

                    Assert.True(ground.TryGetGround(at.X, at.Y, at.Z, out TerrainGround found));

                    double length = Math.Sqrt(
                        (found.NormalX * found.NormalX)
                        + (found.NormalY * found.NormalY)
                        + (found.NormalZ * found.NormalZ));

                    Assert.Equal(1.0, length, 5);
                    Assert.True(found.NormalZ > 0.0, "the ground faces down.");

                    steep += found.NormalZ < 0.9 ? 1 : 0;
                    checks++;
                }
            }
        }

        // And the corpus is not flat, so the slope half of this is measuring something.
        Assert.True(checks > 0, "no level was measured.");
        Assert.True(steep > 0, "every sample measured came out level, so the slope is untested.");
    }

    /// <summary>A point a finer tile covers is answered by the finer tile.</summary>
    /// <remarks>
    /// The same backwards walk the drawn ground uses, and the reason it matters here: a coarse tile's
    /// height under a fine one is metres out, so a body standing on the wrong one either floats or sinks.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_point_a_finer_tile_covers_is_answered_by_the_finer_tile()
    {
        int compared = 0;
        int differed = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = Surface(level);
            if (surface.Tiles.Count < 2)
            {
                continue;
            }

            IGroundHeightField ground = new Igi2Ground(surface);
            Igi2TerrainTile coarse = surface.Tiles[0];
            Igi2TerrainTile fine = surface.Tiles[^1];

            for (int y = 2; y < fine.Height - 2; y += 61)
            {
                for (int x = 2; x < fine.Width - 2; x += 59)
                {
                    Vector3d at = fine.PositionOf(x, y);
                    if (!fine.CoversPoint(at.X, at.Y)
                        || !coarse.CoversPoint(at.X, at.Y)
                        || surface.IsDiscarded(at.X, at.Y))
                    {
                        continue;
                    }

                    // Asked from far enough above that the window cannot be what decides it.
                    Assert.True(ground.TryGetGround(at.X, at.Y, at.Z, out TerrainGround found));
                    Assert.Equal(at.Z, found.Height, 3);

                    compared++;
                    differed += Math.Abs(found.Height - CoarseHeightAt(coarse, at.X, at.Y)) > 4096.0 ? 1 : 0;
                }
            }
        }

        Assert.True(compared > 0, "no level nests one tile inside another, so this proves nothing.");
        Assert.True(
            differed > 0,
            $"all {compared} points read the same from both tiles, so the choice between them is untested.");
    }

    /// <summary>Nothing answers outside the probe's own window.</summary>
    /// <remarks>
    /// The contract <see cref="IGroundHeightField"/> states and <c>Terrain_GetNextZHandler</c>
    /// (<c>igi2 0x572E80</c>) enforces: the surface has to lie between the two ends of the probe. A
    /// query that answered from any distance would report a body standing on ground a hundred metres
    /// below it.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Nothing_answers_outside_the_probes_own_window()
    {
        Igi2Level level = Igi2Level.All().First(static l => !Surface(l).IsEmpty);
        Igi2TerrainSurface surface = Surface(level);
        IGroundHeightField ground = new Igi2Ground(surface);
        Igi2TerrainTile tile = surface.Tiles[^1];
        Vector3d at = tile.PositionOf(tile.Width / 2, tile.Height / 2);

        // Standing on it.
        Assert.True(ground.TryGetGround(at.X, at.Y, at.Z, out _));

        // A kilometre above it, which is a body in the air rather than one on the ground.
        Assert.False(ground.TryGetGround(at.X, at.Y, at.Z + (1000.0 * 4096.0), out _));

        // Under it, unless the query is told to look up.
        Assert.False(ground.TryGetGround(at.X, at.Y, at.Z - 4096.0, out _));
        Assert.True(ground.TryGetGround(at.X, at.Y, at.Z - 4096.0, 8192.0, out _));
    }

    /// <summary>The level's own player lands on the level's own ground.</summary>
    /// <remarks>
    /// <para>
    /// The whole point of the seam, measured over the corpus. Before it the player found nothing under
    /// the spawn and every mission opened in the debugging camera instead — and for
    /// <c>location1/level1</c> that also <em>failed</em> the mission two and a half seconds in, because
    /// its LevelFlow reads a player outside its <c>AreaActivate</c> volume as having left the area. That
    /// level is asserted by name for exactly that reason.
    /// </para>
    /// <para>
    /// Thirteen of the nineteen declared players stand on terrain and the other six legitimately do not:
    /// two levels ship no terrain at all, and four author the player on a floor or a gantry, three or
    /// four metres off the ground, which the placed collision meshes answer for and this cannot see. The
    /// count is pinned because it is a fact about the shipped levels — a sampling error would move it,
    /// and moving it is the point.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_levels_own_player_lands_on_the_levels_own_ground()
    {
        int spawns = 0;
        int standing = 0;
        bool firstMissionStands = false;

        foreach (Igi2Level level in Igi2Level.All())
        {
            if (!PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint spawn))
            {
                continue;
            }

            spawns++;

            Igi2TerrainSurface surface = Surface(level);
            if (surface.IsEmpty)
            {
                continue;
            }

            IGroundHeightField ground = new Igi2Ground(surface);

            // From head height, reaching the probe's own depth below the feet -- the window the body
            // itself queries with when the mission stands it up.
            if (!ground.TryGetGround(spawn.Position.X, spawn.Position.Y, spawn.Position.Z + 7372.8, out _))
            {
                continue;
            }

            standing++;
            firstMissionStands |= level.Directory.EndsWith("level1", StringComparison.OrdinalIgnoreCase)
                && level.Directory.Contains("location1", StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(19, spawns);
        Assert.Equal(13, standing);
        Assert.True(firstMissionStands, "the first campaign mission's player has no ground under his spawn.");
    }

    // What the coarse tile alone would have said, for the comparison above.
    private static double CoarseHeightAt(Igi2TerrainTile tile, double x, double y)
    {
        int column = Math.Clamp((int)tile.ColumnAt(x), 0, tile.Width - 1);
        int row = Math.Clamp((int)tile.RowAt(y), 0, tile.Height - 1);
        return tile.HeightAt(column, row);
    }

    private static Igi2TerrainSurface Surface(Igi2Level level)
    {
        string path = Path.Combine(level.Directory, "heightmaps", "heightmaps.res");
        IresArchive? archive = File.Exists(path) ? IresArchive.Read(File.ReadAllBytes(path)) : null;
        return Igi2TerrainSurface.Build(level.Root, archive);
    }
}
