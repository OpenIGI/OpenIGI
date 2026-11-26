using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Untextured triangles drawn flat in the scene's ambient colour (<c>PolyListDynCubeObj_Direct3DDrawHandler</c>, flat shaded): both
/// sides, tested against the world's depth, written into none.
/// </summary>
/// <param name="device">The device the triangles are drawn with.</param>
public sealed class FlatPolygonDrawer(IGraphicsDevice device) : IDisposable
{
    private readonly IGraphicsDevice _device = device ?? throw new ArgumentNullException(nameof(device));
    private readonly List<(MeshHandle Mesh, int Capacity)> _meshes = [];
    private Vertex3D[] _vertices = [];
    private ushort[] _indices = [];

    /// <summary>Draws the triangles over the world.</summary>
    /// <param name="polygons">The triangle lists.</param>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The world's projection.</param>
    /// <remarks>
    /// A flat-shaded list takes the scene's ambient colour as its colour (each channel no brighter than one) and no lighting of its
    /// own, as the original's colour offset is the ambient and its scale nought.
    /// </remarks>
    public void Draw(IReadOnlyList<TitleWorldPolygons> polygons, in Camera3D camera, in Matrix4f projection)
    {
        ArgumentNullException.ThrowIfNull(polygons);
        if (polygons.Count == 0)
        {
            return;
        }

        Vector3f ambient = _device.ModelAmbient;
        Bgra32 colour = Bgra32.FromRgba(Channel(ambient.X), Channel(ambient.Y), Channel(ambient.Z), 255);
        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);
        _device.SetVertexSun(Vector3f.Zero);
        _device.SetModelAmbient(Vector3f.One);
        _device.ClearPoseInterpolation();
        const float metres = (float)(1.0 / WorldScene.UnitsPerMetre);
        for (int i = 0; i < polygons.Count; i++)
        {
            IReadOnlyList<Vector3f> corners = polygons[i].Corners;
            int count = corners.Count - (corners.Count % 3);
            if (count == 0 || count > ushort.MaxValue)
            {
                continue;
            }

            if (_vertices.Length < count)
            {
                _vertices = new Vertex3D[count];
                _indices = new ushort[count];
            }

            for (int c = 0; c < count; c += 3)
            {
                Vector3f a = corners[c] * metres, b = corners[c + 1] * metres, d = corners[c + 2] * metres;
                Vector3f normal = Vector3f.Cross(b - a, d - a).NormalizedOrSelf();
                _vertices[c] = new(a, normal, 0f, 0f, colour: colour);
                _vertices[c + 1] = new(b, normal, 0f, 0f, colour: colour);
                _vertices[c + 2] = new(d, normal, 0f, 0f, colour: colour);
                _indices[c] = (ushort)c;
                _indices[c + 1] = (ushort)(c + 1);
                _indices[c + 2] = (ushort)(c + 2);
            }

            MeshHandle mesh = Mesh(i, count);
            _device.UploadMeshRange(mesh, 0, _vertices.AsSpan(0, count), 0, _indices.AsSpan(0, count));
            _device.DrawMesh(mesh, 0, count, camera.CreateModelView(WorldScene.ToMetres(polygons[i].Origin), Matrix3f.Identity),
                TextureHandle.None, TextureHandle.None, BlendMode.Opaque);
        }

        _device.SetModelAmbient(ambient);
        _device.SetDepthState(DepthState.TestAndWrite);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach ((MeshHandle mesh, _) in _meshes)
        {
            _device.DestroyMesh(mesh);
        }

        _meshes.Clear();
    }

    private static byte Channel(float value) => (byte)(Math.Clamp(value, 0f, 1f) * 255f);

    // The i-th list's mesh, grown to hold at least the vertices asked for.
    private MeshHandle Mesh(int i, int count)
    {
        while (_meshes.Count <= i)
        {
            _meshes.Add((default, 0));
        }

        (MeshHandle mesh, int capacity) = _meshes[i];
        if (capacity < count)
        {
            if (capacity > 0)
            {
                _device.DestroyMesh(mesh);
            }

            mesh = _device.CreateMeshStorage(count, count);
            _meshes[i] = (mesh, count);
        }

        return mesh;
    }
}
