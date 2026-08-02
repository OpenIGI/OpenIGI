using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.TestSupport;

/// <summary>
/// An <see cref="IGraphicsDevice"/> that records what it is asked to draw instead of drawing it.
/// </summary>
/// <remarks>
/// Everything above the backend — batching, sprite placement, text layout — is testable without a GL
/// context, and testing it here means those tests run in CI where no GPU exists.
/// </remarks>
public sealed class RecordingGraphicsDevice : IGraphicsDevice
{
    private uint _nextTexture = 1;
    private uint _nextMesh = 1;

    public event EventHandler? DeviceRestored;

    public List<DrawCall> Draws { get; } = [];

    public List<MeshDrawCall> MeshDraws { get; } = [];

    public Dictionary<uint, (Vertex3D[] Vertices, ushort[] Indices)> Meshes { get; } = [];

    public Matrix4f Projection { get; private set; } = Matrix4f.Identity;

    public DepthState DepthState { get; private set; } = DepthState.Disabled;

    public CullMode CullMode { get; private set; } = CullMode.None;

    public List<Bgra32> Clears { get; } = [];

    public int SwapCount { get; private set; }

    public GraphicsCapabilities Capabilities { get; } = new(2, 4096, true, "recording")
    {
        SupportsSceneCapture = true,
        SupportsFloatRenderTargets = true,
        MaxSampleCount = 4,
    };

    /// <summary>The scene targets that were asked for, in order.</summary>
    public List<SceneTargetDescription> SceneCaptures { get; } = [];

    /// <summary>The passes that were opened, in order.</summary>
    public List<ScenePass> Passes { get; } = [];

    /// <summary>The settings each resolve was asked for, in order.</summary>
    public List<PostProcessSettings> Resolves { get; } = [];

    /// <summary>Whether a scene capture is open right now.</summary>
    public bool IsCapturing { get; private set; }

    /// <inheritdoc/>
    public ScenePass CurrentPass { get; private set; } = ScenePass.None;

    /// <summary>Set to make <see cref="BeginSceneCapture"/> refuse, as a backend without targets would.</summary>
    public bool RefuseSceneCapture { get; set; }

    /// <inheritdoc/>
    public bool BeginSceneCapture(in SceneTargetDescription description)
    {
        if (IsCapturing)
        {
            throw new GraphicsDeviceException("A scene capture is already in progress.");
        }

        if (RefuseSceneCapture)
        {
            return false;
        }

        SceneCaptures.Add(description);
        IsCapturing = true;
        return true;
    }

    /// <summary>The material each draw was made under, most recent last.</summary>
    public List<SurfaceMaterial> Materials { get; } = [];

    /// <summary>The lighting each frame was set to, in order.</summary>
    public List<SceneLighting> Lightings { get; } = [];

    /// <inheritdoc/>
    public void SetMaterialOverride(in SurfaceMaterial material) => Materials.Add(material);

    /// <summary>The detail map attached to each diffuse texture, in the order they were attached.</summary>
    public List<(TextureHandle Diffuse, TextureHandle Detail)> TextureDetails { get; } = [];

    /// <summary>How strongly the derived detail maps were last asked to apply.</summary>
    public float MaterialDetail { get; private set; }

    /// <inheritdoc/>
    public void SetTextureDetail(TextureHandle diffuse, TextureHandle detail) =>
        TextureDetails.Add((diffuse, detail));

    /// <inheritdoc/>
    public void SetMaterialDetail(float strength) => MaterialDetail = strength;

    /// <summary>The compute programs asked for, by the name they were given.</summary>
    public List<string> ComputeShaders { get; } = [];

    /// <summary>Every dispatch made, as the program and its group counts.</summary>
    public List<(ComputeShaderHandle Shader, int X, int Y, int Z)> Dispatches { get; } = [];

    /// <summary>Every memory barrier issued, in order.</summary>
    public List<MemoryBarrierScope> Barriers { get; } = [];

    // The compute seam, recorded rather than performed. This device exists to check what the shell
    // ASKS a backend to do, and there is no context here to do any of it in -- so a handle is a
    // counter and a dispatch is an entry in a list. A test that wants to know whether a pass actually
    // computed the right answer needs a real device, which is what the capture tooling is for.
    private uint _nextComputeId = 1;
    private uint _nextBufferId = 1;

    // Volumes share the texture handle space with 2D textures, so their counter starts well clear of
    // anything CreateTexture hands out -- a test that confuses the two would otherwise pass.
    private uint _nextVolumeId = 10_000;

    /// <summary>The card atlases opened, as their dimensions.</summary>
    public List<(int Width, int Height)> CardCaptures { get; } = [];

    /// <summary>The pages drawn into, in order.</summary>
    public List<(int X, int Y, int Width, int Height)> CardPages { get; } = [];

    /// <summary>Whether a card capture is open right now.</summary>
    public bool IsCapturingCards { get; private set; }

    /// <inheritdoc/>
    public bool BeginCardCapture(int width, int height)
    {
        if (IsCapturingCards)
        {
            throw new GraphicsDeviceException("A card capture is already in progress.");
        }

        CardCaptures.Add((width, height));
        IsCapturingCards = true;
        CurrentPass = ScenePass.Card;
        return true;
    }

    /// <inheritdoc/>
    public void SetCardPage(int x, int y, int width, int height)
    {
        if (!IsCapturingCards)
        {
            throw new GraphicsDeviceException("No card capture is in progress.");
        }

        CardPages.Add((x, y, width, height));
    }

    /// <inheritdoc/>
    public void EndCardCapture()
    {
        if (!IsCapturingCards)
        {
            throw new GraphicsDeviceException("No card capture is in progress.");
        }

        IsCapturingCards = false;
        CurrentPass = ScenePass.None;
    }

    /// <inheritdoc/>
    public TextureHandle CardAlbedo => new(90_001);

    /// <inheritdoc/>
    public TextureHandle CardNormal => new(90_002);

    /// <inheritdoc/>
    public TextureHandle CardMaterial => new(90_003);

    /// <summary>The volume textures allocated, as name, dimensions and format.</summary>
    /// <remarks>Not to be confused with <see cref="Volumes"/>, which is the probe field.</remarks>
    public List<(string Name, int Width, int Height, int Depth, VolumeFormat Format)> VolumeTextures { get; } = [];

    /// <inheritdoc/>
    public TextureHandle CreateVolumeTexture(
        int width, int height, int depth, VolumeFormat format, string name)
    {
        VolumeTextures.Add((name, width, height, depth, format));
        return new TextureHandle(_nextVolumeId++);
    }

    /// <inheritdoc/>
    public void UpdateVolumeTexture<T>(TextureHandle texture, ReadOnlySpan<T> data)
        where T : unmanaged
    {
    }

    /// <inheritdoc/>
    public void ReadVolumeTexture<T>(TextureHandle texture, Span<T> destination)
        where T : unmanaged
    {
    }

    /// <inheritdoc/>
    public void SetVolumeTexture(int unit, TextureHandle texture)
    {
    }

    /// <inheritdoc/>
    public ComputeShaderHandle CreateComputeShader(string source, string name)
    {
        ComputeShaders.Add(name);
        return new ComputeShaderHandle(_nextComputeId++);
    }

    /// <inheritdoc/>
    public void DestroyComputeShader(ComputeShaderHandle shader)
    {
    }

    /// <inheritdoc/>
    public StorageBufferHandle CreateStorageBuffer(int sizeInBytes, string name) =>
        new(_nextBufferId++);

    /// <inheritdoc/>
    public void UpdateStorageBuffer<T>(StorageBufferHandle buffer, ReadOnlySpan<T> data)
        where T : unmanaged
    {
    }

    /// <inheritdoc/>
    public void ReadStorageBuffer<T>(StorageBufferHandle buffer, Span<T> destination)
        where T : unmanaged
    {
    }

    /// <inheritdoc/>
    public void DestroyStorageBuffer(StorageBufferHandle buffer)
    {
    }

    /// <inheritdoc/>
    public void BindStorageBuffer(int slot, StorageBufferHandle buffer)
    {
    }

    /// <inheritdoc/>
    public void BindImage(int slot, TextureHandle texture, ImageAccess access, int level = 0)
    {
    }

    /// <inheritdoc/>
    public void Dispatch(ComputeShaderHandle shader, int groupsX, int groupsY = 1, int groupsZ = 1) =>
        Dispatches.Add((shader, groupsX, groupsY, groupsZ));

    /// <inheritdoc/>
    public void DispatchIndirect(
        ComputeShaderHandle shader, StorageBufferHandle arguments, int offsetInBytes = 0) =>
        Dispatches.Add((shader, -1, -1, -1));

    /// <inheritdoc/>
    public void MemoryBarrier(MemoryBarrierScope scope) => Barriers.Add(scope);

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, float value)
    {
    }

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, int value)
    {
    }

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, in Vector3f value)
    {
    }

    /// <inheritdoc/>
    public void SetComputeUniform(
        ComputeShaderHandle shader, string name, float x, float y, float z, float w)
    {
    }

    /// <inheritdoc/>
    public void SetLighting(in SceneLighting lighting) => Lightings.Add(lighting);

    /// <summary>The shadow settings each frame was given, in order.</summary>
    public List<ShadowSettings> Shadows { get; } = [];

    /// <summary>The reflection settings each frame was given, in order.</summary>
    public List<ReflectionSettings> Reflections { get; } = [];

    /// <inheritdoc/>
    public void SetReflections(in ReflectionSettings settings) => Reflections.Add(settings);

    /// <summary>The colour tables handed over, in order; a null entry is a clear.</summary>
    public List<ColourLookupTable?> ColourLookups { get; } = [];

    /// <inheritdoc/>
    public void SetColourLookup(ColourLookupTable? table) => ColourLookups.Add(table);

    /// <summary>Whether stage timing was last switched on or off.</summary>
    public bool StageTimingEnabled { get; private set; }

    /// <inheritdoc/>
    public void SetStageTiming(bool enabled) => StageTimingEnabled = enabled;

    /// <summary>The timings this device reports; empty, since nothing here runs on a GPU.</summary>
    public List<RenderStageTiming> Timings { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<RenderStageTiming> StageTimings() => Timings;

    /// <summary>The debug views asked for, in order.</summary>
    public List<DebugView> DebugViews { get; } = [];

    /// <inheritdoc/>
    public void SetDebugView(DebugView view) => DebugViews.Add(view);

    /// <summary>The probe volumes handed over, in order; a null entry is a clear.</summary>
    public List<IrradianceVolume?> Volumes { get; } = [];

    /// <summary>The volume settings each frame was given, in order.</summary>
    public List<IrradianceVolumeSettings> VolumeSettings { get; } = [];

    /// <inheritdoc/>
    public void SetIrradianceVolume(IrradianceVolume? volume) => Volumes.Add(volume);

    /// <inheritdoc/>
    public void SetIrradianceVolumeSettings(in IrradianceVolumeSettings settings) =>
        VolumeSettings.Add(settings);

    /// <summary>The occlusion settings each frame was given, in order.</summary>
    public List<AmbientOcclusionSettings> Occlusion { get; } = [];

    /// <inheritdoc/>
    public void SetAmbientOcclusion(in AmbientOcclusionSettings settings) => Occlusion.Add(settings);

    /// <summary>The previous poses reported, in order; a null entry is a clear.</summary>
    public List<Matrix4f?> PreviousPoses { get; } = [];

    /// <inheritdoc/>
    public void SetPreviousPose(in Matrix4f previousModelView) => PreviousPoses.Add(previousModelView);

    /// <inheritdoc/>
    public void ClearPreviousPose() => PreviousPoses.Add(null);

    /// <summary>The pose blends reported, in order; a null entry is a clear.</summary>
    public List<(float Current, float Previous)?> PoseInterpolations { get; } = [];

    /// <inheritdoc/>
    public void SetPoseInterpolation(float currentBlend, float previousBlend) =>
        PoseInterpolations.Add((currentBlend, previousBlend));

    /// <inheritdoc/>
    public void ClearPoseInterpolation() => PoseInterpolations.Add(null);

    /// <summary>The temporal settings each frame was given, in order.</summary>
    public List<TemporalAaSettings> Temporal { get; } = [];

    /// <inheritdoc/>
    public void SetTemporalAntiAliasing(
        in TemporalAaSettings settings,
        in Matrix4f projection,
        Vector3f cameraPosition,
        Matrix3f viewToWorld) => Temporal.Add(settings);

    /// <summary>The cascade indices that were rendered, in order.</summary>
    public List<int> ShadowCascadesRendered { get; } = [];

    /// <summary>Set to make BeginShadowCascade refuse, as a backend with no shadow map would.</summary>
    public bool RefuseShadows { get; set; }

    /// <summary>The fog settings each frame was given, in order.</summary>
    public List<VolumetricFogSettings> Fogs { get; } = [];

    /// <summary>The texture-quality settings applied, in order.</summary>
    public List<(int Anisotropy, bool Mipmaps)> TextureQuality { get; } = [];

    /// <inheritdoc/>
    public void SetTextureQuality(int maxAnisotropy, bool generateMissingMipmaps) =>
        TextureQuality.Add((maxAnisotropy, generateMissingMipmaps));

    /// <inheritdoc/>
    public void SetVolumetricFog(
        in VolumetricFogSettings fog,
        Vector3f cameraPosition,
        ReadOnlySpan<LocalFogVolume> volumes) => Fogs.Add(fog);

    /// <inheritdoc/>
    public void SetShadows(
        in ShadowSettings settings,
        ReadOnlySpan<ShadowCascade> cascades,
        in DepthUnprojection unprojection,
        Matrix3f viewToWorld) => Shadows.Add(settings);

    /// <inheritdoc/>
    public bool BeginShadowCascade(int cascade)
    {
        if (RefuseShadows)
        {
            return false;
        }

        if (CurrentPass != ScenePass.None)
        {
            throw new GraphicsDeviceException($"The {CurrentPass} pass is still open.");
        }

        CurrentPass = ScenePass.Shadow;
        ShadowCascadesRendered.Add(cascade);
        return true;
    }

    /// <inheritdoc/>
    public void EndShadowCascade() => CurrentPass = ScenePass.None;

    /// <inheritdoc/>
    public void BeginPass(ScenePass pass)
    {
        if (!IsCapturing)
        {
            throw new GraphicsDeviceException($"Cannot begin the {pass} pass outside a scene capture.");
        }

        if (CurrentPass != ScenePass.None)
        {
            throw new GraphicsDeviceException($"The {CurrentPass} pass is still open.");
        }

        CurrentPass = pass;
        Passes.Add(pass);
    }

    /// <inheritdoc/>
    public void EndPass()
    {
        if (CurrentPass == ScenePass.None)
        {
            throw new GraphicsDeviceException("No pass is open.");
        }

        CurrentPass = ScenePass.None;
    }

    /// <inheritdoc/>
    public void EndSceneCapture()
    {
        if (!IsCapturing)
        {
            throw new GraphicsDeviceException("No scene capture is in progress.");
        }

        if (CurrentPass != ScenePass.None)
        {
            throw new GraphicsDeviceException($"The {CurrentPass} pass is still open.");
        }

        IsCapturing = false;
    }

    /// <inheritdoc/>
    public void ResolveScene(in PostProcessSettings post)
    {
        if (IsCapturing)
        {
            throw new GraphicsDeviceException("The scene capture has not been ended.");
        }

        if (SceneCaptures.Count == 0)
        {
            throw new GraphicsDeviceException("Nothing has been captured this frame.");
        }

        Resolves.Add(post);
    }

    public DisplayMode CurrentMode { get; private set; } = DisplayMode.Default;

    public bool VSyncEnabled { get; set; }

    public IReadOnlyList<DisplayMode> EnumerateDisplayModes() => [CurrentMode];

    public void SetMode(in DisplayMode mode) => CurrentMode = mode;

    public void UnsetMode()
    {
    }

    public void Restore() => DeviceRestored?.Invoke(this, EventArgs.Empty);

    public void Clear(Bgra32 color) => Clears.Add(color);

    public void SwapBuffers() => SwapCount++;

    public void SetViewport(in Viewport viewport)
    {
    }

    public void BlitPixels(int x, int y, int width, int height, ReadOnlySpan<Bgra32> pixels)
    {
    }

    public void CaptureFrame(int width, int height, Span<Bgra32> destination) => destination.Clear();

    public TextureHandle CreateTexture(in TextureDescription description, ReadOnlySpan<Bgra32> pixels) =>
        new(_nextTexture++);

    public void UpdateTexture(TextureHandle texture, int mipLevel, ReadOnlySpan<Bgra32> pixels)
    {
    }

    public void DestroyTexture(TextureHandle texture)
    {
    }

    public void SetTexture(int unit, TextureHandle texture)
    {
    }

    public void DrawTriangles2D(
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices,
        TextureHandle texture,
        BlendMode blend) =>
        Draws.Add(new DrawCall(vertices.ToArray(), indices.ToArray(), texture, blend));

    public void Set2DProjection(int width, int height)
    {
    }

    public MeshHandle CreateMesh(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices)
    {
        MeshHandle handle = new(_nextMesh++);
        Meshes[handle.Id] = (vertices.ToArray(), indices.ToArray());
        return handle;
    }

    /// <summary>The meshes created animated, with each one's previous-pose vertices.</summary>
    public Dictionary<uint, Vertex3D[]> PreviousPoseStreams { get; } = [];

    /// <summary>How many times each animated mesh was given a new pose.</summary>
    public Dictionary<uint, int> AnimatedMeshUpdates { get; } = [];

    public MeshHandle CreateAnimatedMesh(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices)
    {
        MeshHandle handle = CreateMesh(vertices, indices);
        PreviousPoseStreams[handle.Id] = vertices.ToArray();
        AnimatedMeshUpdates[handle.Id] = 0;
        return handle;
    }

    public void UpdateAnimatedMesh(MeshHandle mesh, ReadOnlySpan<Vertex3D> vertices)
    {
        if (!Meshes.TryGetValue(mesh.Id, out (Vertex3D[] Vertices, ushort[] Indices) entry))
        {
            throw new ArgumentException($"{mesh} does not belong to this device.", nameof(mesh));
        }

        if (!PreviousPoseStreams.ContainsKey(mesh.Id))
        {
            throw new ArgumentException($"{mesh} was not created with {nameof(CreateAnimatedMesh)}.", nameof(mesh));
        }

        if (vertices.Length != entry.Vertices.Length)
        {
            throw new ArgumentException(
                $"An animated mesh's vertex count is fixed: created with {entry.Vertices.Length}, got {vertices.Length}.",
                nameof(vertices));
        }

        // Exactly the backend's contract: what was current becomes the previous-pose stream.
        PreviousPoseStreams[mesh.Id] = entry.Vertices;
        Meshes[mesh.Id] = (vertices.ToArray(), entry.Indices);
        AnimatedMeshUpdates[mesh.Id]++;
    }

    public void DestroyMesh(MeshHandle mesh)
    {
        Meshes.Remove(mesh.Id);
        PreviousPoseStreams.Remove(mesh.Id);
        AnimatedMeshUpdates.Remove(mesh.Id);
    }

    public void SetDepthState(DepthState state) => DepthState = state;

    public void SetCullMode(CullMode mode) => CullMode = mode;

    public void SetProjection(in Matrix4f projection) => Projection = projection;

    public void SetDetailFade(float scale, float offset, float slope, bool fadeAlpha) =>
        DetailFade = (scale, offset, slope, fadeAlpha);

    public void ClearDetailFade() => DetailFade = null;

    public (float Scale, float Offset, float Slope, bool FadeAlpha)? DetailFade { get; private set; }

    public void DrawMesh(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend) =>
        MeshDraws.Add(new MeshDrawCall(mesh, indexOffset, indexCount, modelView, diffuse, lightmap, blend, DepthState, CullMode));

    public void Dispose()
    {
    }

    public sealed record DrawCall(Vertex2D[] Vertices, ushort[] Indices, TextureHandle Texture, BlendMode Blend)
    {
        public int QuadCount => Vertices.Length / 4;
    }

    public sealed record MeshDrawCall(
        MeshHandle Mesh,
        int IndexOffset,
        int IndexCount,
        Matrix4f ModelView,
        TextureHandle Diffuse,
        TextureHandle Lightmap,
        BlendMode Blend,
        DepthState DepthState,
        CullMode CullMode)
    {
        public int TriangleCount => IndexCount / 3;
    }
}
