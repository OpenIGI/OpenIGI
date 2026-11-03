using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>The renderer's side of editing: dropping what it drew from ground an editor changed.</summary>
public sealed partial class TerrainRenderer
{
    // The heightmap moved the ground over a rectangle: the chunks it displaces there, and the smooth surface sampled
    // from it, are dropped so that they are built again from the new heights.
    private void OnGroundChanged(double minX, double minY, double maxX, double maxY)
    {
        _batcher.Evict(entry =>
        {
            TerrainChunk chunk = entry.Chunk;
            double extent = chunk.Extent;
            return chunk.LodExponent >= TerrainTraversal.HeightmapExponent
                && chunk.Position.X + extent >= minX && chunk.Position.X - extent <= maxX
                && chunk.Position.Y + extent >= minY && chunk.Position.Y - extent <= maxY;
        });
        _smooth?.Forget(minX, minY, maxX, maxY);
    }

    // The octree was built again under a square: every chunk over it, at any depth, and the smooth surface sampled from it.
    private void OnRebuilt(double minX, double minY, double maxX, double maxY)
    {
        OnPainted(minX, minY, maxX, maxY);
        _smooth?.Forget(minX, minY, maxX, maxY);
    }

    // Paint changed which material covers a rectangle: every chunk over it, at any distance, is built again with the new
    // coverage, as the materials show at every level of detail.
    private void OnPainted(double minX, double minY, double maxX, double maxY) =>
        _batcher.Evict(entry =>
        {
            TerrainChunk chunk = entry.Chunk;
            double extent = chunk.Extent;
            return chunk.Position.X + extent >= minX && chunk.Position.X - extent <= maxX
                && chunk.Position.Y + extent >= minY && chunk.Position.Y - extent <= maxY;
        });
}
