using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi2.ViewModel;

/// <content>Security-camera indicator sprites in the world's depth buffer.</content>
public sealed partial class Igi2ViewModelRenderer
{
    private Igi2SpriteResources? _spriteResources;
    private readonly List<MeshHandle> _spriteMeshes = [];
    private readonly Vertex3D[] _spriteVertices = new Vertex3D[4];
    private static readonly ushort[] SpriteIndices = [0, 1, 2, 0, 2, 3];

    /// <inheritdoc/>
    public void DrawWorldSprites(IReadOnlyList<TitleWorldSprite> sprites, in Camera3D camera, in Matrix4f projection)
    {
        ArgumentNullException.ThrowIfNull(sprites);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_spriteResources is null || sprites.Count == 0) { return; }
        Vector3f ambient = _device.ModelAmbient;
        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);
        _device.SetVertexSun(Vector3f.Zero);
        _device.SetModelAmbient(Vector3f.One);
        _device.ClearPoseInterpolation();
        // scamera.c SCamera_SCameraLensEnterHandler: additive billboard, real scene depth, no depth write.
        for (int i = 0; i < sprites.Count; i++)
        {
            TitleWorldSprite light = sprites[i];
            if (_spriteResources.Get(light.Sprite) is not { } resource) { continue; }
            SpriteFrame frame = resource.Asset.Frames[0];
            float half = light.HalfSize / (float)WorldScene.UnitsPerMetre;
            _spriteVertices[0] = new(new(-half, half, 0f), default, frame.U0, frame.V0, colour: light.Colour);
            _spriteVertices[1] = new(new(half, half, 0f), default, frame.U1, frame.V0, colour: light.Colour);
            _spriteVertices[2] = new(new(half, -half, 0f), default, frame.U1, frame.V1, colour: light.Colour);
            _spriteVertices[3] = new(new(-half, -half, 0f), default, frame.U0, frame.V1, colour: light.Colour);
            while (_spriteMeshes.Count <= i) { _spriteMeshes.Add(_device.CreateMeshStorage(4, 6)); }
            MeshHandle mesh = _spriteMeshes[i];
            _device.UploadMeshRange(mesh, 0, _spriteVertices, 0, SpriteIndices);
            Matrix4f transform = camera.CreateModelView(WorldScene.ToMetres(light.Position), camera.Orientation.Transposed());
            _device.DrawMesh(mesh, 0, 6, transform, resource.Textures[frame.TextureIndex], TextureHandle.None, BlendMode.Additive);
        }
        _device.SetModelAmbient(ambient);
        _device.SetDepthState(DepthState.TestAndWrite);
    }
}
