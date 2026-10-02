using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Mesh3D;
using OpenIGI.Rendering.Pipeline.Meshes;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.Textures;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>Everything the direct path draws: textures, meshes, sprites and text.</summary>
public sealed partial class GraphicsDevice : ISdfTextDevice
{
    private readonly TextureTable _textures;
    private readonly Dictionary<uint, RhiMesh> _meshTable = [];

    // Each diffuse's derived detail map, by texture id; see SetTextureDetail.
    private readonly Dictionary<uint, uint> _textureDetail = [];

    // The other direction: which diffuse textures each detail map is paired with, so destroying a map finds
    // its pairings without walking every one. See SetTextureDetail.
    private readonly Dictionary<uint, HashSet<uint>> _detailUsers = [];

    private uint _nextMesh = 1;
    private Matrix4f _projection = Matrix4f.Identity;
    private DepthState _depthState = DepthState.Disabled;
    private CullMode _cullMode = CullMode.None;
    private SceneFog _fog = SceneFog.None;
    private FogTarget _fogTarget;
    private SceneLighting _lighting = SceneLighting.Baked;
    private Vector3f _modelAmbient = Vector3f.One;
    private Vector3f _vertexSun;
    private DetailFade _detailFade = DetailFade.None;
    private float _lightmapScale = 1f;
    private float _alphaHashSeed;
    private float _poseBlend;
    private byte _alphaReference = Mesh3DConstants.DefaultAlphaReference;
    private Vector3f _sceneSunAmbient;
    private Vector3f _sceneSunColour;
    private bool _firstPerson;

    /// <inheritdoc/>
    public SceneFog Fog => _fog;

    /// <inheritdoc/>
    public Vector3f ModelAmbient => _modelAmbient;

    /// <inheritdoc/>
    public TextureHandle CreateTexture(in TextureDescription description, ReadOnlySpan<Bgra32> pixels)
    {
        ThrowIfDisposed();

        TextureHandle texture = _textures.Create(description, pixels);

        // A data texture -- a detail map's normal, roughness and occlusion -- is never what a ray sees, so
        // it takes no layer of the traced albedo and no cut-out mask. Its alpha carries numbers, and read
        // as coverage it could even classify the map as a cut-out.
        if (!description.HoldsData)
        {
            ObserveTexture(texture, description, pixels);
        }

        return texture;
    }

    /// <inheritdoc/>
    public TextureHandle CreateCompressedTexture(
        in TextureDescription description,
        BlockFormat format,
        ReadOnlySpan<byte> chain,
        ReadOnlySpan<Bgra32> proxy,
        int proxyWidth,
        int proxyHeight)
    {
        ThrowIfDisposed();

        if (!Capabilities.SupportsBlockCompression)
        {
            throw new NotSupportedException("This device cannot sample block-compressed textures.");
        }

        TextureHandle texture = _textures.CreateCompressed(description, format, chain);

        // The traced world reads a texture's colour and cut-out shape off its pixels as it is made; a
        // compressed one hands it the proxy, which is all a ray needs of it.
        if (!description.HoldsData && proxyWidth > 0 && proxyHeight > 0 && proxy.Length >= proxyWidth * proxyHeight)
        {
            ObserveTexture(
                texture,
                new TextureDescription(proxyWidth, proxyHeight, 1, description.Filter, description.AddressMode),
                proxy[..(proxyWidth * proxyHeight)]);
        }

        return texture;
    }

    /// <inheritdoc/>
    public void UpdateTexture(TextureHandle texture, int mipLevel, ReadOnlySpan<Bgra32> pixels)
    {
        ThrowIfDisposed();
        _textures.Update(texture, mipLevel, pixels);
    }

    /// <inheritdoc/>
    public void DestroyTexture(TextureHandle texture)
    {
        ThrowIfDisposed();

        if (_textures.Destroy(texture))
        {
            // A pairing either side of it goes too: a diffuse whose map is gone is given the neutral one.
            // Through the reverse index -- scanning every pairing for each texture destroyed made a
            // level's release quadratic, on the render thread, every time the menu swapped its backdrop.
            UnpairDetail(texture.Id);

            if (_detailUsers.Remove(texture.Id, out HashSet<uint>? diffuses))
            {
                foreach (uint diffuse in diffuses)
                {
                    _textureDetail.Remove(diffuse);
                }
            }

            // Every group that named it goes with it, or the next frame binds a view that is gone.
            _sprites.ForgetTexture(texture.Id);
            _materials.Forget(texture.Id);
            ForgetStorageTexture(texture);
            TracedWorld?.ForgetTexture(texture.Id);

            // The radiance cache goes with any of its volumes, or the next lighting pass samples one that
            // is gone -- the cache is rebuilt with the surface cache, a preset switch among the reasons.
            if (_realtimeProbe is { } field
                && (field.Red == texture || field.Green == texture || field.Blue == texture || field.Distance == texture))
            {
                _realtimeProbe = null;
            }
        }
    }

    /// <summary>Pairs a diffuse texture with a detail map, keeping both directions of the pairing.</summary>
    private void PairDetail(uint diffuse, uint detail)
    {
        UnpairDetail(diffuse);
        _textureDetail[diffuse] = detail;

        if (!_detailUsers.TryGetValue(detail, out HashSet<uint>? users))
        {
            users = [];
            _detailUsers[detail] = users;
        }

        users.Add(diffuse);
    }

    /// <summary>Drops a diffuse texture's pairing, if it has one, from both directions.</summary>
    private void UnpairDetail(uint diffuse)
    {
        if (_textureDetail.Remove(diffuse, out uint detail)
            && _detailUsers.TryGetValue(detail, out HashSet<uint>? users))
        {
            users.Remove(diffuse);
            if (users.Count == 0)
            {
                _detailUsers.Remove(detail);
            }
        }
    }

    /// <inheritdoc/>
    public void Set2DProjection(int width, int height) => _projection2D = (width, height);

    /// <inheritdoc/>
    /// <remarks>See <see cref="Drawing2D"/> for why nothing is drawn while a scene is being captured.</remarks>
    public void DrawTriangles2D(
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices,
        TextureHandle texture,
        BlendMode blend)
    {
        ThrowIfDisposed();

        if (Drawing2D)
        {
            Apply2DProjection();
            _sprites.DrawTriangles(Pass(), vertices, indices, texture, blend);
        }
    }

    /// <inheritdoc/>
    /// <remarks>See <see cref="Drawing2D"/> for why nothing is drawn while a scene is being captured.</remarks>
    public void DrawText2D(
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices,
        TextureHandle atlas,
        in SdfTextParams parameters,
        BlendMode blend)
    {
        ThrowIfDisposed();

        if (Drawing2D)
        {
            Apply2DProjection();
            _sprites.DrawText(Pass(), vertices, indices, atlas, parameters, blend);
        }
    }

    /// <inheritdoc/>
    /// <remarks>See <see cref="Drawing2D"/> for why nothing is drawn while a scene is being captured.</remarks>
    public void DrawLoadingBackdrop(TextureHandle texture, in LoadingBackdropSettings settings)
    {
        ThrowIfDisposed();

        if (Drawing2D)
        {
            _sprites.DrawLoadingBackdrop(Pass(), texture, settings);
        }
    }

    /// <summary>Gets whether a 2D draw goes to the frame now, which it does whenever no scene is being captured.</summary>
    /// <remarks>
    /// Two-dimensional drawing is the interface, and it lands on the frame over the resolved scene. While a
    /// capture is open, the frame is not what is being drawn: opening the frame's pass would close the
    /// scene's and every draw after it would be lost, and whatever did land on the frame would be painted
    /// over by the resolve, which covers all of it. So the three 2D draws decline alike until the capture
    /// has been resolved, where the frame order puts them anyway.
    /// </remarks>
    private bool Drawing2D => !Capturing;

    /// <inheritdoc/>
    public MeshHandle CreateMesh(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices) =>
        Keep(RhiMesh.CreateStatic(_device, vertices, indices, $"mesh-{_nextMesh}"));

    /// <inheritdoc/>
    public MeshHandle CreateAnimatedMesh(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices) =>
        Keep(RhiMesh.CreateAnimated(_device, vertices, indices, $"mesh-{_nextMesh}"));

    /// <inheritdoc/>
    public MeshHandle CreateMeshStorage(int vertexCapacity, int indexCapacity) =>
        Keep(RhiMesh.CreateStorage(_device, vertexCapacity, indexCapacity, $"mesh-{_nextMesh}"));

    /// <inheritdoc/>
    public void UpdateAnimatedMesh(MeshHandle mesh, ReadOnlySpan<Vertex3D> vertices)
    {
        ThrowIfDisposed();
        Resolve(mesh).UpdateVertices(vertices, _frameCounter);
    }

    /// <inheritdoc/>
    public void UploadMeshRange(
        MeshHandle mesh,
        int vertexOffset,
        ReadOnlySpan<Vertex3D> vertices,
        int indexOffset,
        ReadOnlySpan<ushort> indices)
    {
        ThrowIfDisposed();
        Resolve(mesh).UploadRange(vertexOffset, vertices, indexOffset, indices);
    }

    /// <inheritdoc/>
    public void DestroyMesh(MeshHandle mesh)
    {
        ThrowIfDisposed();

        if (_meshTable.Remove(mesh.Id, out RhiMesh? resource))
        {
            TracedWorld?.Forget(mesh.Id);
            _retired.Retire(resource);
        }
    }

    /// <inheritdoc/>
    public void SetProjection(in Matrix4f projection) => _projection = projection;

    /// <inheritdoc/>
    public void SetDepthState(DepthState state) => _depthState = state;

    /// <inheritdoc/>
    public void SetCullMode(CullMode mode) => _cullMode = mode;

    /// <inheritdoc/>
    public void SetFog(in SceneFog fog)
    {
        _fog = fog;
        _pipelines.SetFog(fog.Colour, fog.DensityPerMetre);
        _forwardPipelines?.SetFog(fog.Colour, fog.DensityPerMetre);
    }

    /// <inheritdoc/>
    public void SetFogTarget(FogTarget target) => _fogTarget = target;

    /// <inheritdoc/>
    public void SetModelAmbient(Vector3f ambient) => _modelAmbient = ambient;

    /// <inheritdoc/>
    public void SetSceneSun(Vector3f ambient, Vector3f colour)
    {
        _sceneSunAmbient = ambient;
        _sceneSunColour = colour;
        _pipelines.SetSun(ambient, colour);
        _forwardPipelines?.SetSun(ambient, colour);
    }

    /// <inheritdoc/>
    public void SetVertexSun(Vector3f towardsSun) => _vertexSun = towardsSun;

    /// <inheritdoc/>
    public void SetDetailFade(float scale, float offset, float slope, bool fadeAlpha) =>
        _detailFade = new DetailFade(scale, offset, slope, fadeAlpha ? 2f : 1f);

    /// <inheritdoc/>
    public void ClearDetailFade() => _detailFade = DetailFade.None;

    /// <inheritdoc/>
    public void SetLightmapScale(float scale) => _lightmapScale = scale;

    /// <inheritdoc/>
    public void SetAlphaReference(byte reference) => _alphaReference = reference;

    /// <inheritdoc/>
    public void SetFirstPersonGeometry(bool firstPerson)
    {
        // Leaving the layer puts the scene's draws back on the full depth range; entering it moves them
        // into the sliver. Only the scene's passes have a sliver: the direct path clears its depth instead,
        // which is what the interface describes.
        if (_firstPerson != firstPerson && Capturing)
        {
            SetForegroundLayer(firstPerson);
        }

        _firstPerson = firstPerson;
    }

    /// <inheritdoc/>
    public void SetLighting(in SceneLighting lighting) => _lighting = lighting;

    /// <inheritdoc/>
    public void DrawMesh(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend)
    {
        ThrowIfDisposed();

        if (indexCount <= 0)
        {
            return;
        }

        MeshDrawCall call = Call(mesh, indexOffset, indexCount, diffuse, lightmap, blend);

        // A cascade being drawn takes every draw: the world seen from the light, depth alone.
        if (DrawingShadow)
        {
            DrawShadow(call, [], modelView, blend);
            return;
        }

        if (CapturingCards)
        {
            DrawCard(call, _projection * modelView);
            return;
        }

        if (Capturing)
        {
            // While a capture is open a draw belongs to the scene's pass, never to the frame: sent there
            // instead, the view model and the muzzle flash would land on the back buffer for the resolve
            // to paint over.
            if (CurrentPass is ScenePass.Geometry)
            {
                ObserveForTracing(mesh, modelView, diffuse, indexOffset, indexCount);
                DrawGeometry(call, modelView, blend);
                NoteObjectMotion(mesh, call.Mesh, indexOffset, indexCount, modelView, blend);
            }
            else if (CurrentPass is ScenePass.GlassLayer)
            {
                DrawIntoGlassLayer(call, [], modelView);
            }
            else if (CurrentPass is ScenePass.Forward)
            {
                // The first-person rig's opaque, depth-writing draws belong in the traced world too, where
                // only a reflection finds them.
                if (_firstPerson && blend == BlendMode.Opaque && _depthState == DepthState.TestAndWrite)
                {
                    ObserveForTracing(mesh, modelView, diffuse, indexOffset, indexCount, firstPerson: true);
                }

                DrawForward(call, [], modelView, blend, lightmap.IsValid);
                NoteObjectMotion(mesh, call.Mesh, indexOffset, indexCount, modelView, blend);
            }

            return;
        }

        Span<float> push = stackalloc float[ForwardPushBlock.Floats];
        WriteForward(push, modelView, blend, lightmap.IsValid, exposure: 1f);
        (bool reflective, IRhiBindGroup environment) = EnvironmentFor();

        _meshes.Draw(
            Pass(),
            _pipelines.ForState(_depthState, _cullMode, blend, _alphaReference, reflective),
            call,
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(push),
            _frameCounter,
            environment);
    }

    /// <inheritdoc/>
    public void DrawMeshRanges(
        MeshHandle mesh,
        ReadOnlySpan<MeshDrawRange> ranges,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend)
    {
        ThrowIfDisposed();

        if (ranges.IsEmpty)
        {
            return;
        }

        if (DrawingShadow)
        {
            DrawShadow(Call(mesh, 0, 0, diffuse, lightmap, blend), ranges, modelView, blend);
            return;
        }

        if (CapturingCards)
        {
            // A capture draws the whole model into its page: the ranges are a frame's material batching,
            // and a page holds one material by construction.
            DrawCard(Call(mesh, 0, 0, diffuse, lightmap, blend), _projection * modelView);
            return;
        }

        if (Capturing)
        {
            if (CurrentPass is ScenePass.Geometry)
            {
                foreach (MeshDrawRange range in ranges)
                {
                    ObserveForTracing(mesh, modelView, diffuse, range.IndexOffset, range.IndexCount);
                }

                DrawGeometryRanges(mesh, ranges, modelView, diffuse, lightmap, blend);
            }
            else if (CurrentPass is ScenePass.GlassLayer)
            {
                DrawIntoGlassLayer(Call(mesh, 0, 0, diffuse, lightmap, blend), ranges, modelView);
            }
            else if (CurrentPass is ScenePass.Forward)
            {
                DrawForward(Call(mesh, 0, 0, diffuse, lightmap, blend), ranges, modelView, blend, lightmap.IsValid);
            }

            return;
        }

        Span<float> push = stackalloc float[ForwardPushBlock.Floats];
        WriteForward(push, modelView, blend, lightmap.IsValid, exposure: 1f);
        (bool reflective, IRhiBindGroup environment) = EnvironmentFor();

        _meshes.DrawRanges(
            Pass(),
            _pipelines.ForState(_depthState, _cullMode, blend, _alphaReference, reflective),
            Call(mesh, 0, 0, diffuse, lightmap, blend),
            ranges,
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(push),
            _frameCounter,
            environment);
    }

    private (int Width, int Height) _projection2D;

    private MeshDrawCall Call(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend) => new(
            Resolve(mesh),
            indexOffset,
            indexCount,
            diffuse,
            lightmap,
            DetailFor(diffuse),
            blend,
            _depthState,
            _cullMode,
            _alphaReference);

    /// <summary>The detail map a diffuse was paired with, or the neutral one where it has none.</summary>
    private TextureHandle DetailFor(TextureHandle diffuse) =>
        _textureDetail.TryGetValue(diffuse.Id, out uint detail)
            ? new TextureHandle(detail)
            : _textures.NeutralDetail;

    /// <summary>Fills the block a forward draw carries, from the scene as it stands.</summary>
    /// <param name="push">Room for the block.</param>
    /// <param name="modelView">The draw's transform into view space.</param>
    /// <param name="blend">How it is blended.</param>
    /// <param name="hasLightmap">Whether it carries a lightmap.</param>
    /// <param name="exposure">
    /// What the frame will be exposed by, which the lit rig divides itself by so that it leaves the tone
    /// map as it was lit. One on the direct path, which is never tone mapped.
    /// </param>
    private void WriteForward(Span<float> push, in Matrix4f modelView, BlendMode blend, bool hasLightmap, float exposure)
    {
        ForwardDrawState state = new(
            _projection,
            _lighting,
            _fog,
            _fogTarget,
            _detailFade,
            _modelAmbient,
            _vertexSun,
            _lightmapScale,
            _poseBlend,

            exposure,

            // Only the lit rig reads it, so the lamps are summed only for its draws. The flashlight's share
            // is the host's: the reflector's leak onto the hand holding it and the beam thrown back off
            // whatever it is pointed at, which only the host knows the distance to.
            LampLight: ForwardDrawMarker.IsStudioRig(ForwardDrawMarker.For(blend, _firstPerson, _lighting.Model))
                ? LampFill.AtEye(LocalLights) + (_flashlight.IsOn ? _flashlight.RigFill : Vector3f.Zero)
                : Vector3f.Zero,
            _firstPerson);

        ForwardPushBlock.Write(push, state, modelView, blend, hasLightmap);
    }

    /// <summary>The 2D projection the sprite path divides by, which follows the mode unless set.</summary>
    private void Apply2DProjection()
    {
        (int width, int height) = _projection2D;

        _sprites.SetProjection(
            width > 0 ? width : CurrentMode.Width,
            height > 0 ? height : CurrentMode.Height);
    }

    private MeshHandle Keep(RhiMesh mesh)
    {
        ThrowIfDisposed();

        uint id = _nextMesh++;
        _meshTable[id] = mesh;
        return new MeshHandle(id);
    }

    private RhiMesh Resolve(MeshHandle mesh) =>
        _meshTable.TryGetValue(mesh.Id, out RhiMesh? resource)
            ? resource
            : throw new ArgumentException($"There is no mesh {mesh.Id}.", nameof(mesh));

    private void DisposeMeshes()
    {
        foreach (RhiMesh mesh in _meshTable.Values)
        {
            mesh.Dispose();
        }

        _meshTable.Clear();
    }
}
