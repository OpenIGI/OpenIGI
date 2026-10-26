using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>Marks lying on the ground, both games' bodies' shadows and I.G.I. 2's pools of blood (<c>Shadow_MagicObjEnterHandler</c>).</summary>
/// <param name="device">The device the squares are drawn with.</param>
public sealed class GroundDecalDrawer(IGraphicsDevice device) : IDisposable
{
    private static readonly ushort[] Indices = [0, 1, 2, 0, 2, 3];
    private readonly IGraphicsDevice _device = device ?? throw new ArgumentNullException(nameof(device));
    private readonly List<MeshHandle> _meshes = [];
    private readonly Vertex3D[] _vertices = new Vertex3D[4];

    /// <summary>Draws the marks over the world.</summary>
    /// <param name="decals">The marks.</param>
    /// <param name="texture">Resolves a mark's texture by its name, or none to leave the mark out.</param>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The world's projection.</param>
    /// <remarks>
    /// A square in the ground's own plane (<c>Real32x9_FromReal32x3</c> of its normal), its half-width either way of the point and
    /// a hover above it, blended over the world and tested against its depth, written into none.
    /// </remarks>
    public void Draw(IReadOnlyList<TitleGroundDecal> decals, Func<string, TextureHandle> texture, in Camera3D camera, in Matrix4f projection)
    {
        ArgumentNullException.ThrowIfNull(decals);
        ArgumentNullException.ThrowIfNull(texture);
        if (decals.Count == 0)
        {
            return;
        }

        Vector3f ambient = _device.ModelAmbient;
        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);
        _device.SetVertexSun(Vector3f.Zero);
        _device.SetModelAmbient(Vector3f.One);
        _device.ClearPoseInterpolation();
        const float metres = (float)(1.0 / WorldScene.UnitsPerMetre);
        for (int i = 0; i < decals.Count; i++)
        {
            TitleGroundDecal decal = decals[i];
            TextureHandle handle = texture(decal.Texture);
            if (handle == TextureHandle.None)
            {
                continue;
            }

            Vector3f n = decal.Normal;
            Vector3f across = MathF.Abs(n.X) < 0.9f ? Vector3f.Cross(n, new Vector3f(1f, 0f, 0f)).NormalizedOrSelf() : Vector3f.Cross(n, new Vector3f(0f, 1f, 0f)).NormalizedOrSelf();
            Vector3f along = Vector3f.Cross(n, across);
            float s = decal.HalfSize * metres, h = decal.Hover * metres;
            Vector3f up = n * h, a = across * s, b = along * s;
            _vertices[0] = new(up - a - b, n, 0f, 0f, colour: decal.Colour);
            _vertices[1] = new(up + a - b, n, 1f, 0f, colour: decal.Colour);
            _vertices[2] = new(up + a + b, n, 1f, 1f, colour: decal.Colour);
            _vertices[3] = new(up - a + b, n, 0f, 1f, colour: decal.Colour);
            while (_meshes.Count <= i)
            {
                _meshes.Add(_device.CreateMeshStorage(4, 6));
            }

            MeshHandle mesh = _meshes[i];
            _device.UploadMeshRange(mesh, 0, _vertices, 0, Indices);
            _device.DrawMesh(mesh, 0, 6, camera.CreateModelView(WorldScene.ToMetres(decal.Position), Matrix3f.Identity), handle,
                TextureHandle.None, BlendMode.AlphaBlend);
        }

        _device.SetModelAmbient(ambient);
        _device.SetDepthState(DepthState.TestAndWrite);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (MeshHandle mesh in _meshes)
        {
            _device.DestroyMesh(mesh);
        }

        _meshes.Clear();
    }
}
