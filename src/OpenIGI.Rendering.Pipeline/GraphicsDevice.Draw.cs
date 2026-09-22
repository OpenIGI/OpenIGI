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
    private readonly Dictionary<uint, IRhiBuffer> _occlusionResults = [];

    private uint _nextMesh = 1;
    private uint _nextQuery = 1;
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
    private bool _firstPerson;

    /// <inheritdoc/>
    public SceneFog Fog => _fog;

    /// <inheritdoc/>
    public Vector3f ModelAmbient => _modelAmbient;

    /// <inheritdoc/>
    public TextureHandle CreateTexture(in TextureDescription description, ReadOnlySpan<Bgra32> pixels)
    {
        ThrowIfDisposed();
        return _textures.Create(description, pixels);
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
            // Every group that named it goes with it, or the next frame binds a view that is gone.
            _sprites.ForgetTexture(texture.Id);
            _materials.Forget(texture.Id);
        }
    }

    /// <inheritdoc/>
    public void Set2DProjection(int width, int height) => _projection2D = (width, height);

    /// <inheritdoc/>
    public void DrawTriangles2D(
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices,
        TextureHandle texture,
        BlendMode blend)
    {
        ThrowIfDisposed();
        Apply2DProjection();

        if (Capturing)
        {
            return;
        }

        _sprites.DrawTriangles(Pass(), vertices, indices, texture, blend);
    }

    /// <inheritdoc/>
    public void DrawText2D(
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices,
        TextureHandle atlas,
        in SdfTextParams parameters,
        BlendMode blend)
    {
        ThrowIfDisposed();
        Apply2DProjection();

        _sprites.DrawText(Pass(), vertices, indices, atlas, parameters, blend);
    }

    /// <inheritdoc/>
    public void DrawLoadingBackdrop(TextureHandle texture, in LoadingBackdropSettings settings)
    {
        ThrowIfDisposed();
        _sprites.DrawLoadingBackdrop(Pass(), texture, settings);
    }

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
    }

    /// <inheritdoc/>
    public void SetFogTarget(FogTarget target) => _fogTarget = target;

    /// <inheritdoc/>
    public void SetModelAmbient(Vector3f ambient) => _modelAmbient = ambient;

    /// <inheritdoc/>
    public void SetSceneSun(Vector3f ambient, Vector3f colour) => _pipelines.SetSun(ambient, colour);

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
    public void SetFirstPersonGeometry(bool firstPerson) => _firstPerson = firstPerson;

    /// <inheritdoc/>
    public void SetPoseInterpolation(float currentBlend, float previousBlend)
    {
        _poseBlend = currentBlend;
        _ = previousBlend;
    }

    /// <inheritdoc/>
    public void ClearPoseInterpolation() => _poseBlend = 0f;

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

        if (CapturingCards)
        {
            DrawCard(call, _projection * modelView);
            return;
        }

        if (Capturing)
        {
            // Only the geometry pass draws while a capture is open. The translucency and glazing passes
            // have not moved onto this path, and sending their draws to the frame instead would put the
            // view model and the muzzle flash on the back buffer for the resolve to paint over.
            if (CurrentPass is ScenePass.Geometry)
            {
                DrawGeometry(call, modelView, blend);
            }

            return;
        }

        Span<float> push = stackalloc float[ForwardPushBlock.Floats];
        WriteForward(push, modelView, blend, lightmap.IsValid);

        _meshes.Draw(
            Pass(),
            _pipelines.ForState(_depthState, _cullMode, blend, _alphaReference),
            call,
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(push),
            _frameCounter,
            _cubeGroup);
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
                DrawGeometryRanges(mesh, ranges, modelView, diffuse, lightmap, blend);
            }

            return;
        }

        Span<float> push = stackalloc float[ForwardPushBlock.Floats];
        WriteForward(push, modelView, blend, lightmap.IsValid);

        _meshes.DrawRanges(
            Pass(),
            _pipelines.ForState(_depthState, _cullMode, blend, _alphaReference),
            Call(mesh, 0, 0, diffuse, lightmap, blend),
            ranges,
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(push),
            _frameCounter,
            _cubeGroup);
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
            TextureHandle.None,
            blend,
            _depthState,
            _cullMode,
            _alphaReference);

    /// <summary>Fills the block a forward draw carries, from the scene as it stands.</summary>
    private void WriteForward(Span<float> push, in Matrix4f modelView, BlendMode blend, bool hasLightmap)
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

            // The direct path never tone-maps, so there is no adapted exposure to divide the rig by.
            Exposure: 1f,

            // Nor are there dynamic lamps on it: the lit rig is a remaster path.
            LampLight: Vector3f.Zero,
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

    private void DisposeQueries()
    {
        foreach (IRhiBuffer buffer in _occlusionResults.Values)
        {
            buffer.Dispose();
        }

        _occlusionResults.Clear();
    }
}
