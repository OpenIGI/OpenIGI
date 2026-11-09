using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Igi1.World;

/// <content>The ground handed to a picture taken from straight above: every chunk in a box at its finest detail.</content>
public sealed partial class TerrainRenderer
{
    // The fade a picture from above is weighted at: halfway, both texture scales at half strength.
    private const float OverheadFade = 0.5f;

    /// <inheritdoc/>
    /// <remarks>
    /// The chunks are those the ground would draw seen from the box's middle with no limit on detail, so every one is at the
    /// finest the octree holds; the box keeps those over it. Each chunk's material passes go as they are drawn -- the first
    /// opaque, the rest added -- weighted as the shader weights them with the fade held halfway, so a material's two texture
    /// scales are blended evenly and neither's tiling shows from above. The light page is left out: a plan wants the
    /// ground's own colours.
    /// </remarks>
    public bool ExportOverhead(Vector3d min, Vector3d max, TexAtlasFile? tiles, IOverheadSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (tiles is null || tiles.Tiles.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < tiles.Tiles.Count; i++)
        {
            TextureImage tile = tiles.Tiles[i];
            sink.GroundTexture(i, tile.Width, tile.Height, tile.GetLevel(0));
        }

        // Looking straight down from well above the box, the frustum just taking it in, and no limit on detail: the walk
        // keeps to the box and resolves every chunk in it to the finest the octree holds.
        double height = (max.Z - min.Z) + (2000 * WorldScene.UnitsPerMetre);
        Vector3d eye = new((min.X + max.X) * 0.5, (min.Y + max.Y) * 0.5, max.Z + height);
        float tangentX = (float)(((max.X - min.X) * 0.55) / (eye.Z - min.Z));
        float tangentY = (float)(((max.Y - min.Y) * 0.55) / (eye.Z - min.Z));
        TerrainView view = new(new Camera3D(eye, Camera3D.LookRotation(0f, -MathF.PI * 0.5f)), tangentX, tangentY, int.MaxValue / 4, TerrainView.DefaultFloorZ);
        List<TerrainChunk> chunks = [];
        TerrainTraversal.Collect(_terrain.Octree, view, chunks, _surface.Discards, 1 << 18);

        List<OverheadVertex> corners = [];
        foreach (TerrainChunk chunk in chunks)
        {
            double reach = chunk.Extent;
            if (chunk.Position.X - reach > max.X || chunk.Position.X + reach < min.X || chunk.Position.Y - reach > max.Y || chunk.Position.Y + reach < min.Y
                || !_terrain.TryGetBlock(chunk, out TerrainCommandBlock block))
            {
                continue;
            }

            TerrainChunkGeometry? geometry = TerrainChunkMesh.Build(chunk, block, _surface, 1f, _surface.Heights.Resolve(chunk));
            if (geometry is null)
            {
                continue;
            }

            foreach (TerrainChunkLayer layer in geometry.Layers)
            {
                corners.Clear();
                for (int i = layer.IndexOffset; i < layer.IndexOffset + layer.IndexCount; i++)
                {
                    Vertex3D v = geometry.Vertices[geometry.Indices[i]];

                    // The material's share by the vertex's selector (see TerrainChunkMesh), times the scale's share.
                    float share = v.Colour.A switch { >= 213 => 1f, >= 128 => 1f - OverheadFade, >= 43 => OverheadFade, _ => 0f };
                    float weight = share * (layer.IsDetail ? 1f - OverheadFade : OverheadFade);
                    corners.Add(new OverheadVertex(
                        chunk.Position.X + v.Position.X,
                        chunk.Position.Y + v.Position.Y,
                        chunk.Position.Z + v.Position.Z,
                        v.DiffuseU,
                        v.DiffuseV,
                        weight));
                }

                sink.GroundPass(layer.TileIndex, layer.IsBase, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(corners));
            }
        }

        return true;
    }
}
