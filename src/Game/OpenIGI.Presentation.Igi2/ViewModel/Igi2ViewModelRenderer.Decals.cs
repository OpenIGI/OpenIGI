using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.Game.Igi2;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Presentation.Igi2.World;

namespace OpenIGI.Presentation.Igi2.ViewModel;

/// <content>Marks lying on the ground: a body's shadow and its pool of blood (<c>Shadow_MagicObjEnterHandler</c>).</content>
public sealed partial class Igi2ViewModelRenderer
{
    private static readonly ushort[] DecalIndices = [0, 1, 2, 0, 2, 3];
    private readonly List<MeshHandle> _decalMeshes = [];
    private readonly Vertex3D[] _decalVertices = new Vertex3D[4];

    /// <inheritdoc/>
    /// <remarks>
    /// A square in the ground's own plane (<c>Real32x9_FromReal32x3</c> of its normal), its half-width either way of the point and
    /// a hover above it, blended over the world and tested against its depth, written into none.
    /// </remarks>
    public void DrawGroundDecals(IReadOnlyList<TitleGroundDecal> decals, in Camera3D camera, in Matrix4f projection)
    {
        ArgumentNullException.ThrowIfNull(decals);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_textures is null || decals.Count == 0)
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
            TextureHandle texture = _textures.Resolve(decal.Texture);
            if (texture == TextureHandle.None)
            {
                continue;
            }

            Vector3f n = decal.Normal;
            Vector3f across = MathF.Abs(n.X) < 0.9f ? Vector3f.Cross(n, new Vector3f(1f, 0f, 0f)).NormalizedOrSelf() : Vector3f.Cross(n, new Vector3f(0f, 1f, 0f)).NormalizedOrSelf();
            Vector3f along = Vector3f.Cross(n, across);
            float s = decal.HalfSize * metres, h = decal.Hover * metres;
            Vector3f up = n * h, a = across * s, b = along * s;
            _decalVertices[0] = new(up - a - b, n, 0f, 0f, colour: decal.Colour);
            _decalVertices[1] = new(up + a - b, n, 1f, 0f, colour: decal.Colour);
            _decalVertices[2] = new(up + a + b, n, 1f, 1f, colour: decal.Colour);
            _decalVertices[3] = new(up - a + b, n, 0f, 1f, colour: decal.Colour);
            while (_decalMeshes.Count <= i)
            {
                _decalMeshes.Add(_device.CreateMeshStorage(4, 6));
            }

            MeshHandle mesh = _decalMeshes[i];
            _device.UploadMeshRange(mesh, 0, _decalVertices, 0, DecalIndices);
            _device.DrawMesh(mesh, 0, 6, camera.CreateModelView(WorldScene.ToMetres(decal.Position), Matrix3f.Identity), texture,
                TextureHandle.None, BlendMode.AlphaBlend);
        }

        _device.SetModelAmbient(ambient);
        _device.SetDepthState(DepthState.TestAndWrite);
    }

}
