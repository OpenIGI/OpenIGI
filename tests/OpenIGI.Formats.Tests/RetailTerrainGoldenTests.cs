using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Runs the terrain readers over every retail level's <c>terrain/</c> directory.
/// </summary>
/// <remarks>
/// <para>
/// The claims these check were measured over all fourteen levels, not sampled. The ones that matter most
/// are the two that separate live data from rubbish: every octree link and every <c>.cmd</c> offset
/// resolves in levels 1–13, and level 14's do not — 14400 of its 15202 nodes address nothing.
/// </para>
/// </remarks>
public sealed class RetailTerrainGoldenTests
{
    /// <summary>The level whose terrain files exist but are not live data.</summary>
    private const int DeadTerrainLevel = 14;

    private const int LevelCount = 14;

    [Fact]
    public void OrientationTablesAgreeWithEachOther()
    {
        // The slot table is data lifted from the image; the composition is a closed form of a table the
        // original builds at run time; the vertex transform is a nest of branches in a third function.
        // They are three statements of one group and this is what proves the reading of all three.
        for (int a = 0; a < TerrainOrientation.Count; a++)
        {
            for (int b = 0; b < TerrainOrientation.Count; b++)
            {
                int composed = TerrainOrientation.Compose(a, b);
                for (int octant = 0; octant < TerrainOrientation.Count; octant++)
                {
                    Assert.Equal(
                        TerrainOrientation.ChildSlot(b, TerrainOrientation.ChildSlot(a, octant)),
                        TerrainOrientation.ChildSlot(composed, octant));
                }
            }
        }
    }

    [Fact]
    public void EveryOrientationIsAPermutationThatFixesZ()
    {
        for (int orientation = 0; orientation < TerrainOrientation.Count; orientation++)
        {
            bool[] seen = new bool[TerrainOrientation.Count];
            for (int octant = 0; octant < TerrainOrientation.Count; octant++)
            {
                int slot = TerrainOrientation.ChildSlot(orientation, octant);
                Assert.False(seen[slot]);
                seen[slot] = true;

                // Bit 2 is the vertical axis and no orientation touches it — which is why the vertex
                // transform leaves Z alone as well.
                Assert.Equal(octant & 4, slot & 4);
            }
        }
    }

    [Fact]
    public void TheVertexTransformInvertsTheSlotTable()
    {
        // A slot maps world to node-local, because it answers "which child of this node holds that
        // octant". A vertex is stored node-local and has to come back out to world, so the vertex
        // transform has to be the inverse of the slot table — and it is, for all 64 pairs.
        for (int orientation = 0; orientation < TerrainOrientation.Count; orientation++)
        {
            for (int octant = 0; octant < TerrainOrientation.Count; octant++)
            {
                int slot = TerrainOrientation.ChildSlot(orientation, octant);
                (int x, int y) = TerrainOrientation.Transform(orientation, Sign(slot, 1), Sign(slot, 2));

                Assert.Equal(Sign(octant, 1), x);
                Assert.Equal(Sign(octant, 2), y);
            }
        }

        static int Sign(int octant, int bit) => (octant & bit) != 0 ? 1 : -1;
    }

    [RequiresGameDataFact]
    public void EveryLevelsOctreeAndGeometryAgree()
    {
        int checkedLevels = 0;
        int totalNodes = 0;
        int totalTriangles = 0;
        List<string> failures = [];

        for (int level = 1; level <= LevelCount; level++)
        {
            if (!TryReadTerrain(level, out string dir))
            {
                continue;
            }

            checkedLevels++;
            TerrainOctree octree = TerrainOctree.Read(File.ReadAllBytes(Path.Combine(dir, "terrain.ctr")));
            TerrainCommands commands = TerrainCommands.Read(File.ReadAllBytes(Path.Combine(dir, "terrain.cmd")));

            Assert.True(octree.Count > TerrainOctree.RootIndex, $"level {level} has no root");

            if (level == DeadTerrainLevel)
            {
                // 19662 links leave the file and 31954 orientation bytes are not 0..7. The tree is not a
                // tree; this is what a loader has to notice instead of descending it.
                Assert.NotEmpty(octree.Defects);
            }
            else
            {
                Assert.Empty(octree.Defects);
            }

            int resolved = 0;
            int triangles = 0;
            ReadOnlySpan<TerrainNode> nodes = octree.Nodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (!commands.TryGetBlock(nodes[i].CommandOffset, out TerrainCommandBlock block))
                {
                    continue;
                }

                resolved++;
                triangles += block.TriangleCount;

                // Indices are single bytes, so a block can never carry more vertices than they address.
                Assert.InRange(block.VertexCount, 0, 256);

                // Level 14's few blocks that parse are coincidences of a self-describing header rather
                // than geometry anyone authored, so its contents are not held to the corpus invariants.
                if (level == DeadTerrainLevel)
                {
                    continue;
                }

                for (int t = 0; t < block.TriangleCount; t++)
                {
                    TerrainTriangle triangle = block.Triangle(t);
                    Assert.InRange(triangle.I0, 0, block.VertexCount - 1);
                    Assert.InRange(triangle.I1, 0, block.VertexCount - 1);
                    Assert.InRange(triangle.I2, 0, block.VertexCount - 1);

                    // The flag byte is the command's top byte and only ever continues or restarts a run.
                    Assert.Contains(triangle.Packed >> 24, (uint[])[0xE0u, 0xF0u]);
                }

                for (int v = 0; v < block.VertexCount; v++)
                {
                    // A coordinate of ±24 spans the chunk's cube exactly; nothing overshoots it.
                    TerrainPackedVertex vertex = block.Vertex(v);
                    Assert.InRange(vertex.X, -24, 24);
                    Assert.InRange(vertex.Y, -24, 24);
                    Assert.InRange(vertex.Z, -24, 24);
                }
            }

            if (level == DeadTerrainLevel)
            {
                // Not an assertion about a bug — this level's files are simply not live data, and a
                // loader has to notice rather than follow the offsets.
                Assert.True(resolved < nodes.Length / 2, "level 14's terrain is supposed to be unusable");
            }
            else if (resolved != nodes.Length)
            {
                failures.Add($"level {level}: {nodes.Length - resolved} of {nodes.Length} nodes address no block");
            }

            totalNodes += nodes.Length;
            totalTriangles += triangles;
        }

        Assert.Empty(failures);
        Assert.Equal(LevelCount, checkedLevels);
        Assert.True(totalNodes > 100_000, $"only walked {totalNodes} nodes");
        Assert.True(totalTriangles > 500_000, $"only walked {totalTriangles} triangles");
    }

    [RequiresGameDataFact]
    public void EveryLevelsGridsAndLightmapsWalkExactly()
    {
        int withHeightmap = 0;
        int checkedLevels = 0;
        List<string> failures = [];

        for (int level = 1; level <= LevelCount; level++)
        {
            if (!TryReadTerrain(level, out string dir))
            {
                continue;
            }

            checkedLevels++;

            TerrainVisibility visibility = TerrainVisibility.Read(File.ReadAllBytes(Path.Combine(dir, "terrain.bit")));
            if (!visibility.IsExact)
            {
                failures.Add($"level {level}: terrain.bit consumed {visibility.Table.BytesConsumed} of {visibility.Table.Length}");
            }

            string heightmapPath = Path.Combine(dir, "terrain.hmp");
            if (File.Exists(heightmapPath))
            {
                withHeightmap++;
                TerrainHeightmap heightmap = TerrainHeightmap.Read(File.ReadAllBytes(heightmapPath));
                if (!heightmap.IsExact)
                {
                    failures.Add($"level {level}: terrain.hmp consumed {heightmap.Table.BytesConsumed} of {heightmap.Table.Length}");
                }

                // Record 0 is a grid like any other: sub_507410 indexes the table straight by a HeightMap
                // task's Bitmap ID, and levels 1 and 3 both name it. (The "none" id is the run-time
                // binding slot, which is a different number.)
                if (level is 1 or 3)
                {
                    Assert.True(heightmap.TryGetGrid(0, out _), $"level {level} has no grid 0");
                }

                for (int id = 0; id < TerrainGridTable.RecordCount; id++)
                {
                    if (heightmap.TryGetGrid(id, out TerrainHeightGrid grid))
                    {
                        // The samples sit on the cell corners, which is where the extra row and column go.
                        Assert.Equal((grid.Size + 1) * (grid.Size + 1), grid.Corners.Length);
                    }
                }
            }

            string lightmapPath = Path.Combine(dir, "terrain.lmp");
            if (!File.Exists(lightmapPath))
            {
                continue;
            }

            TerrainLightmaps lightmaps = TerrainLightmaps.Read(File.ReadAllBytes(lightmapPath));
            if (!lightmaps.IsExact)
            {
                failures.Add($"level {level}: terrain.lmp consumed {lightmaps.BytesConsumed} of {lightmaps.Length}");
            }

            foreach (TerrainLightmapPage page in lightmaps.Pages)
            {
                if (!page.IsEmpty)
                {
                    Assert.Contains(page.Size, (int[])[16, 32, 64, 128, 256]);
                    Assert.Equal(page.Size * page.Size, page.Texels.Length);
                }
            }
        }

        Assert.Empty(failures);
        Assert.Equal(LevelCount, checkedLevels);

        // Most levels ship no heightmap at all, which is the trap a loader has to survive. Six do:
        // 1, 3, 6, 8, 9 and 10. An earlier reading said seven, and counting the files says six.
        Assert.Equal(6, withHeightmap);
    }

    [RequiresGameDataFact]
    public void EveryLevelsGroundAtlasDecodes()
    {
        int checkedLevels = 0;
        int tiles = 0;
        List<string> counts = [];

        for (int level = 1; level <= LevelCount; level++)
        {
            string path = GameData.Resolve(
                Path.Combine("missions", "location0", $"level{level}", "terrain", "terrain.tex"));

            if (!File.Exists(path))
            {
                continue;
            }

            checkedLevels++;
            byte[] bytes = File.ReadAllBytes(path);

            // The ordinary texture loader refuses these on purpose: they are containers, not textures.
            Assert.True(TexAtlasFile.IsAtlas(bytes));
            Assert.False(TexFile.IsTexture(bytes));

            TexAtlasFile atlas = TexAtlasFile.Read(bytes);
            Assert.Equal(TexAtlasFile.Version9, atlas.Version);

            counts.Add($"{level}:{atlas.Tiles.Count}");
            Assert.Equal(128, atlas.TileWidth);
            Assert.Equal(128, atlas.TileHeight);

            foreach (TextureImage tile in atlas.Tiles)
            {
                Assert.Equal(128, tile.Width);
                Assert.Equal(128, tile.Height);

                // Ground is opaque: every texel of every retail tile sets the format's single alpha bit.
                // A tile that did not would be alpha-tested into a hole in the terrain.
                foreach (Bgra32 texel in tile.Pixels)
                {
                    Assert.Equal(255, texel.A);
                }

                tiles++;
            }
        }

        Assert.Equal(LevelCount, checkedLevels);
        Assert.Equal(228, tiles);

        // Twelve or eighteen, and which is which lines up exactly with the declared tile-map counts: the
        // four levels that declare seven tile maps are the four that ship twelve tiles.
        Assert.Equal(
            "1:18 2:18 3:18 4:18 5:18 6:18 7:12 8:12 9:18 10:18 11:12 12:12 13:18 14:18",
            string.Join(" ", counts));
    }

    private static bool TryReadTerrain(int level, out string directory)
    {
        directory = GameData.Resolve(Path.Combine("missions", "location0", $"level{level}", "terrain"));
        return File.Exists(Path.Combine(directory, "terrain.ctr"))
            && File.Exists(Path.Combine(directory, "terrain.cmd"))
            && File.Exists(Path.Combine(directory, "terrain.bit"));
    }
}
