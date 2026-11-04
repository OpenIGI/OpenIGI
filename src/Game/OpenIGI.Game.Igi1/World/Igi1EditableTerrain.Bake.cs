using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>Saving shaped ground: the shape baked into the octree at every level of detail, the heightmap keeping only the rest.</summary>
/// <remarks>
/// <para>
/// The original adds a heightmap only to the near levels of detail (<c>0x510010</c>: nothing below depth 14, faded in at
/// 14, whole past it), so a shape kept in the heightmap alone vanishes as the camera draws back, exactly as the retail
/// heightmaps' own bumps do. Saving therefore moves the octree's own vertices, at every depth, by how far the ground under
/// each one was shaped, rounded to that depth's step, and writes the heightmap back with only what the finest vertices
/// could not take: the rounding, and wherever a vertex reached the top or bottom of its cube. The ground the game stands
/// on is the one that was shaped, and the distant levels of detail show it too.
/// </para>
/// <para>
/// The editor keeps drawing from what it loaded until the mission is loaded again; it is the files that change.
/// </para>
/// </remarks>
public sealed partial class Igi1EditableTerrain
{
    /// <summary>The octree's path relative to the mission's folder.</summary>
    public const string OctreeMissionPath = "terrain/terrain.ctr";

    /// <summary>The terrain lightmaps' path relative to the mission's folder.</summary>
    public const string LightmapMissionPath = "terrain/terrain.lmp";

    /// <summary>The terrain geometry's path relative to the mission's folder.</summary>
    public const string GeometryMissionPath = "terrain/terrain.cmd";

    // How far above the expected ground a drop for the rewritten surface starts, and how far it looks, in metres.
    private const double DropLeadMetres = 16.0;
    private const double DropRangeMetres = 64.0;

    /// <inheritdoc/>
    public IReadOnlyList<(string MissionPath, byte[] Bytes)> ChangedFiles(Func<string, ReadOnlyMemory<byte>?> read)
    {
        List<(string MissionPath, byte[] Bytes)> files = [.. ChangedGround()];

        // Mountains are in the octree already; a bake of the heightmap carries them, else the octree goes on its own.
        if (ChangedMountains() is { } mountains && !files.Any(static f => f.MissionPath == OctreeMissionPath))
        {
            files.Add((OctreeMissionPath, mountains.Ctr));
            files.Add((GeometryMissionPath, mountains.Cmd));
        }

        if (ChangedMasks() is { } masks)
        {
            files.Add(masks);
        }

        return files;
    }

    // The heightmap, and the octree and lightmaps it was baked into, when the ground was shaped since the last save.
    private List<(string MissionPath, byte[] Bytes)> ChangedGround()
    {
        if (_surface.Terrain.Heightmap is not { } map || map.Revision == _savedRevision)
        {
            return [];
        }

        List<Grid> moved = [.. _grids.Where(static g => g.HasMoved)];
        if (moved.Count == 0 || Bake(moved) is not { } baked)
        {
            return [(HeightmapMissionPath, map.Write())];
        }

        List<(string MissionPath, byte[] Bytes)> files =
            [(HeightmapMissionPath, baked.Heightmap), (OctreeMissionPath, baked.Ctr), (GeometryMissionPath, baked.Cmd)];
        if (_surface.Terrain.Lightmaps is { } lightmaps && Relight(moved) is { Count: > 0 } pages)
        {
            files.Add((LightmapMissionPath, lightmaps.Write(pages)));
        }

        return files;
    }

    // The octree with the shaping moved into its vertices, and the heightmap with what is left over.
    private (byte[] Heightmap, byte[] Ctr, byte[] Cmd)? Bake(List<Grid> moved)
    {
        TerrainData terrain = _surface.Terrain;
        (double MinX, double MinY, double MaxX, double MaxY)[] squares = [.. moved.Select(static g => g.Square)];
        (byte[] Ctr, byte[] Cmd)? rewritten = TerrainOctreeRewriter.Rewrite(
            terrain.Octree.Data,
            terrain.Commands.Data,
            place => squares.Any(s => place.CentreX + place.Extent >= s.MinX && place.CentreX - place.Extent <= s.MaxX
                && place.CentreY + place.Extent >= s.MinY && place.CentreY - place.Extent <= s.MaxY),
            (place, block) => Reshape(moved, place, block));
        if (rewritten is not { } files)
        {
            return null;
        }

        // The rewritten surface alone, with no heightmap bound to it, measured under every moved sample: whatever it did not
        // rise or sink by stays in the sample.
        TerrainData reshaped = new(
            TerrainOctree.Read(files.Ctr),
            TerrainCommands.Read(files.Cmd),
            terrain.Visibility,
            heightmap: null,
            lightmaps: null,
            tiles: null,
            terrain.Materials,
            terrain.TileMaps,
            []);
        TerrainHeightmap heightmap = TerrainHeightmap.Read(terrain.Heightmap!.Write());
        foreach (Grid grid in moved)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    (double ux, double uy) = grid.SampleUnits(x, y);
                    if (!grid.TryMoved(ux, uy, out double shaped) || shaped == 0.0)
                    {
                        continue;
                    }

                    double before = grid.BaseAt(x, y);
                    double top = (before + shaped + DropLeadMetres) * WorldScene.UnitsPerMetre;
                    if (!reshaped.TryDrop(ux, uy, top, DropRangeMetres * WorldScene.UnitsPerMetre, upward: false, out TerrainGround ground))
                    {
                        continue;
                    }

                    double rose = (ground.Height / WorldScene.UnitsPerMetre) - before;
                    double left = grid.OffsetAt(x, y) - rose;
                    heightmap.SetCorner(grid.Id, x, y, (int)Math.Round(left / MetresPerStep) + (int)TerrainHeightGrid.Bias);
                }
            }
        }

        return (heightmap.Write(), files.Ctr, files.Cmd);
    }

    // A node's block with every vertex moved by how far the ground under it was shaped, or null when none moves a step.
    private static byte[]? Reshape(List<Grid> moved, TerrainNodePlace place, TerrainCommandBlock block)
    {
        if (block.IsEmpty)
        {
            return null;
        }

        double unit = place.Unit;
        int[] heights = new int[block.VertexCount];
        bool changed = false;
        for (int i = 0; i < heights.Length; i++)
        {
            TerrainPackedVertex vertex = block.Vertex(i);
            heights[i] = vertex.Z;
            (int localX, int localY) = TerrainOrientation.Transform(place.Orientation, vertex.X, vertex.Y);
            double x = place.CentreX + (localX * unit);
            double y = place.CentreY + (localY * unit);
            foreach (Grid grid in moved)
            {
                if (!grid.TryMoved(x, y, out double metres))
                {
                    continue;
                }

                int steps = (int)Math.Round(metres * WorldScene.UnitsPerMetre / unit);
                int z = Math.Clamp(vertex.Z + steps, -TerrainPackedVertex.UnitsPerExtent, TerrainPackedVertex.UnitsPerExtent);
                if (z != vertex.Z)
                {
                    heights[i] = z;
                    changed = true;
                }

                break;
            }
        }

        return changed ? block.WithHeights(heights) : null;
    }
}
