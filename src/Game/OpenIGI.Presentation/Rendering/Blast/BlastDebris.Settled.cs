using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering.Blast;

/// <summary>
/// The pieces that have come to rest, folded into a few meshes drawn in one go each: a building brought down leaves hundreds
/// of slabs lying still for the rest of the round, and a draw apiece for every one of them, every frame, was most of what the
/// blast cost once it had burned out.
/// </summary>
public sealed partial class BlastDebris
{
    // How many still pieces wait before they are folded, unless nothing is left moving: one fold for the bulk of a collapse,
    // not one for every piece as it stops.
    private const int FoldAtPieces = 48;

    private readonly List<SettledBatch> _settled = [];

    // Folds the pieces lying still for good into the settled meshes, once enough of them have stopped or nothing still moves.
    private void FoldSettled()
    {
        int still = 0;
        bool anyMoving = false;
        foreach (DebrisPiece piece in _pieces)
        {
            if (IsSettled(piece))
            {
                still++;
            }
            else
            {
                anyMoving = true;
            }
        }

        if (still == 0 || (anyMoving && still < FoldAtPieces))
        {
            return;
        }

        // The broken faces' texture is made at the first draw; a fast-forward folds before there has been one.
        EnsureTextures();
        var folding = new List<DebrisPiece>(still);
        _pieces.RemoveAll(piece =>
        {
            if (!IsSettled(piece))
            {
                return false;
            }

            folding.Add(piece);
            return true;
        });

        // Anchored at the first piece, so the vertices stay small numbers in floats; the rest within a blast's reach of it.
        Vector3d anchor = folding[0].Body.Position;
        var byTexture = new Dictionary<TextureHandle, (List<Vertex3D> Vertices, List<ushort> Indices)>();
        foreach (DebrisPiece piece in folding)
        {
            Matrix3f toWorld = piece.Body.Orientation.Transposed();
            Vector3d offsetUnits = piece.Body.Position - anchor;
            Vector3f offset = new((float)(offsetUnits.X / UnitsPerMetre), (float)(offsetUnits.Y / UnitsPerMetre), (float)(offsetUnits.Z / UnitsPerMetre));
            foreach (PieceDraw draw in piece.Draws)
            {
                TextureHandle texture = draw.Texture == TextureHandle.None ? _concrete : draw.Texture;
                if (!byTexture.TryGetValue(texture, out var lists) || lists.Vertices.Count + draw.Count > ushort.MaxValue)
                {
                    if (lists.Vertices is { Count: > 0 })
                    {
                        AddSettled(lists.Vertices, lists.Indices, texture, anchor);
                    }

                    lists = ([], []);
                    byTexture[texture] = lists;
                }

                // Each triangle's corners copied out as they lie, turned and placed into the anchor's frame.
                for (int i = draw.Offset; i < draw.Offset + draw.Count; i++)
                {
                    Vertex3D v = piece.Vertices[piece.Indices[i]];
                    lists.Indices.Add((ushort)lists.Vertices.Count);
                    lists.Vertices.Add(new Vertex3D(
                        toWorld.Transform(v.Position) + offset,
                        toWorld.Transform(v.Normal),
                        v.DiffuseU,
                        v.DiffuseV,
                        v.LightmapU,
                        v.LightmapV,
                        v.Colour,
                        v.SurfaceNormal));
                }
            }

            device.DestroyMesh(piece.Mesh);
        }

        foreach ((TextureHandle texture, var lists) in byTexture)
        {
            if (lists.Vertices.Count > 0)
            {
                AddSettled(lists.Vertices, lists.Indices, texture, anchor);
            }
        }
    }

    private void AddSettled(List<Vertex3D> vertices, List<ushort> indices, TextureHandle texture, Vector3d anchorUnits)
    {
        MeshHandle mesh = device.CreateMesh([.. vertices], [.. indices]);
        if (mesh.IsValid)
        {
            _settled.Add(new SettledBatch(mesh, indices.Count, texture, anchorUnits / WorldUnits.PerMetre));
        }
    }

    // Still for good -- at rest, or a stump that never lets go -- and no longer glowing.
    private static bool IsSettled(DebrisPiece piece) =>
        (piece.Body.IsAtRest || piece.Body.HeldTicks < 0) && (!piece.Hot || piece.AgeTicks >= HotTicks);

    private void DrawSettled(in Camera3D camera)
    {
        foreach (SettledBatch batch in _settled)
        {
            device.DrawMesh(batch.Mesh, 0, batch.Count, camera.CreateModelView(batch.AnchorMetres), batch.Texture, TextureHandle.None, BlendMode.Opaque);
        }
    }

    private void ClearSettled()
    {
        foreach (SettledBatch batch in _settled)
        {
            device.DestroyMesh(batch.Mesh);
        }

        _settled.Clear();
    }

    private readonly record struct SettledBatch(MeshHandle Mesh, int Count, TextureHandle Texture, Vector3d AnchorMetres);
}
