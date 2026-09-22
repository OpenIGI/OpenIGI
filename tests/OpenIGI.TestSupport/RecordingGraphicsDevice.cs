using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

using OpenIGI.Shaders;

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
    private uint _nextOcclusionQuery = 1;

    public List<DrawCall> Draws { get; } = [];

    public List<MeshDrawCall> MeshDraws { get; } = [];

    public Dictionary<uint, (Vertex3D[] Vertices, ushort[] Indices)> Meshes { get; } = [];

    public Matrix4f Projection { get; private set; } = Matrix4f.Identity;

    public DepthState DepthState { get; private set; } = DepthState.Disabled;

    public CullMode CullMode { get; private set; } = CullMode.None;

    public List<Bgra32> Clears { get; } = [];

    /// <summary>The number of foreground depth resets requested.</summary>
    public int DepthClearCount { get; private set; }

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

    /// <summary>Gets the lightmap scale the draws that follow carry.</summary>
    public float LightmapScale { get; private set; } = 1f;

    /// <summary>Gets the model-space direction towards the fixed-function sun the draws that follow carry, or zero.</summary>
    public Vector3f VertexSun { get; private set; }

    /// <summary>The fixed-function sun's colours, each time they were set.</summary>
    public List<(Vector3f Ambient, Vector3f Colour)> SceneSuns { get; } = [];

    /// <inheritdoc/>
    public void SetLightmapScale(float scale) => LightmapScale = scale;

    /// <inheritdoc/>
    public void SetSceneSun(Vector3f ambient, Vector3f colour) => SceneSuns.Add((ambient, colour));

    /// <inheritdoc/>
    public void SetVertexSun(Vector3f towardsSun) => VertexSun = towardsSun;

    /// <summary>Gets the colour the draws that follow fog towards.</summary>
    public FogTarget FogTarget { get; private set; }

    /// <inheritdoc/>
    public void SetFogTarget(FogTarget target) => FogTarget = target;

    /// <inheritdoc/>
    public void SetAlphaHash(float seed)
    {
    }

    /// <summary>Gets the cut-out threshold the draws being recorded were made at.</summary>
    public byte AlphaReference { get; private set; } = IGraphicsDevice.DefaultAlphaReference;

    /// <summary>Every cut-out threshold set, in order, so a run of draws can be checked as a run.</summary>
    public List<byte> AlphaReferences { get; } = [];

    /// <inheritdoc/>
    public void SetAlphaReference(byte reference)
    {
        AlphaReference = reference;
        AlphaReferences.Add(reference);
    }

    /// <summary>Gets a value indicating whether the draws being recorded were marked first-person.</summary>
    public bool FirstPersonGeometry { get; private set; }

    /// <inheritdoc/>
    public void SetFirstPersonGeometry(bool firstPerson) => FirstPersonGeometry = firstPerson;

    /// <summary>The detail map attached to each diffuse texture, in the order they were attached.</summary>
    public List<(TextureHandle Diffuse, TextureHandle Detail)> TextureDetails { get; } = [];

    /// <summary>How strongly the derived detail maps were last asked to apply.</summary>
    public float MaterialDetail { get; private set; }

    /// <inheritdoc/>
    public void SetTextureDetail(TextureHandle diffuse, TextureHandle detail) =>
        TextureDetails.Add((diffuse, detail));

    /// <inheritdoc/>
    public void SetMaterialDetail(float strength) => MaterialDetail = strength;

    /// <inheritdoc/>
    public void SetFloorGloss(float gloss)
    {
    }

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
    private readonly Dictionary<uint, int> _cubeEdges = [];
    private uint _nextVolumeId = 10_000;
    private uint _nextCubeId = 20_000;

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
    public void ReadCardAtlas(int attachment, Span<Bgra32> destination)
    {
    }

    /// <inheritdoc/>
    public TextureHandle CardAlbedo => new(90_001);

    /// <inheritdoc/>
    public TextureHandle CardNormal => new(90_002);

    /// <inheritdoc/>
    public TextureHandle CardMaterial => new(90_003);

    /// <inheritdoc/>
    public TextureHandle CardDepth => new(90_004);

    /// <inheritdoc/>
    public TextureHandle SceneDepth => new(90_005);

    /// <inheritdoc/>
    public TextureHandle SceneNormal => new(90_006);

    /// <inheritdoc/>
    public TextureHandle SceneColour => new(90_007);

    public bool TryGetSkyIrradiance(Span<float> coefficients) => false;

    /// <inheritdoc/>
    public void SetScreenIndirect(TextureHandle indirect, float strength, bool fullResolution = false)
    {
    }

    /// <summary>Gets the local lights the last frame submitted, in the order they were given.</summary>
    /// <remarks>
    /// Recorded rather than dropped: a lamp reaching the device at all is the thing that was silently
    /// missing before -- the fixtures were proposed, written to a file and handed to the surface cache,
    /// and the field the deferred pass read from was never assigned, so no punctual light was ever
    /// submitted by anything.
    /// </remarks>
    public List<DeferredLocalLight> LocalLights { get; } = [];

    /// <inheritdoc/>
    public void SetLocalLights(ReadOnlySpan<DeferredLocalLight> lights)
    {
        LocalLights.Clear();

        foreach (DeferredLocalLight light in lights)
        {
            LocalLights.Add(light);
        }
    }

    /// <summary>The volume textures allocated, as name, dimensions and format.</summary>
    /// <remarks>Not to be confused with <see cref="Volumes"/>, which is the probe field.</remarks>
    public List<(string Name, int Width, int Height, int Depth, VolumeFormat Format)> VolumeTextures { get; } = [];

    /// <inheritdoc/>
    public void ReadStorageTexture(TextureHandle texture, Span<Bgra32> destination)
    {
    }

    /// <inheritdoc/>
    public TextureHandle CreateStorageTexture(
        int width, int height, VolumeFormat format, string name)
    {
        VolumeTextures.Add((name, width, height, 1, format));
        return new TextureHandle(_nextVolumeId++);
    }

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

    /// <summary>Every cube map created, by name and edge length, in order.</summary>
    public List<(string Name, int Edge)> CubeTextures { get; } = [];

    /// <summary>Each cube face written, with the texels it was written from.</summary>
    /// <remarks>
    /// The pixels are copied rather than referenced. A caller that uploads six faces out of one reused
    /// buffer -- which is the obvious way to walk an archive -- would otherwise leave six entries all
    /// showing the last face, and a test asserting the faces differ would pass on a device that had
    /// written the same face six times.
    /// </remarks>
    public List<(TextureHandle Cube, int Face, Bgra32[] Pixels)> CubeFaceUploads { get; } = [];

    /// <summary>The cube reflective draws would sample, or the null handle.</summary>
    public TextureHandle EnvironmentCube { get; private set; }

    /// <summary>Every cube handed over or cleared, in order.</summary>
    public List<TextureHandle> EnvironmentCubes { get; } = [];

    /// <inheritdoc/>
    public TextureHandle CreateCubeTexture(int edge, string name)
    {
        if (edge <= 0)
        {
            throw new GraphicsDeviceException($"A cube's edge is {edge}, which is not a length.");
        }

        CubeTextures.Add((name, edge));
        _cubeEdges[_nextCubeId] = edge;
        return new TextureHandle(_nextCubeId++);
    }

    /// <inheritdoc/>
    public void UpdateCubeFace(TextureHandle cube, int face, ReadOnlySpan<Bgra32> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(face);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(face, IGraphicsDevice.CubeFaceCount);

        if (!_cubeEdges.TryGetValue(cube.Id, out int edge))
        {
            throw new GraphicsDeviceException($"{cube} is not a cube of this device.");
        }

        if (pixels.Length != edge * edge)
        {
            throw new GraphicsDeviceException(
                $"A face of a {edge}-texel cube holds {edge * edge} texels, not {pixels.Length}.");
        }

        CubeFaceUploads.Add((cube, face, pixels.ToArray()));
    }

    /// <inheritdoc/>
    public void SetEnvironmentCube(TextureHandle cube)
    {
        EnvironmentCube = cube;
        EnvironmentCubes.Add(cube);
    }

    /// <inheritdoc/>
    public ComputeShaderHandle CreateComputeShader(
        ShaderModuleId shaderModule,
        ShaderVariant variant,
        string name)
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

    /// <summary>The surface caches handed over, in order.</summary>
    public List<SurfaceCacheBinding> SurfaceCaches { get; } = [];

    /// <inheritdoc/>
    public void SetSurfaceCache(in SurfaceCacheBinding binding) => SurfaceCaches.Add(binding);

    /// <summary>The colour tables handed over, in order; a null entry is a clear.</summary>
    public List<ColourLookupTable?> ColourLookups { get; } = [];

    /// <inheritdoc/>
    public void SetColourLookup(ColourLookupTable? table) => ColourLookups.Add(table);

    /// <summary>Whether stage timing was last switched on or off.</summary>
    public bool StageTimingEnabled { get; private set; }

    /// <inheritdoc/>
    public void SetStageTiming(bool enabled) => StageTimingEnabled = enabled;

    public void BeginStageTiming(RenderStage stage) { }

    public void EndStageTiming() { }

    /// <summary>The timings this device reports; empty, since nothing here runs on a GPU.</summary>
    public List<RenderStageTiming> Timings { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<RenderStageTiming> StageTimings() => Timings;

    /// <inheritdoc/>
    public GpuFrameTiming GpuTiming() => default;

    public OcclusionQueryHandle CreateOcclusionQuery() => new(_nextOcclusionQuery++);

    public bool OcclusionVisible(OcclusionQueryHandle query) => true;

    public void IssueOcclusionQuery(OcclusionQueryHandle query, in Matrix4f modelView, in OcclusionBounds bounds)
    {
    }

    public void DestroyOcclusionQuery(OcclusionQueryHandle query)
    {
    }

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
    public void SetRealtimeProbeVolume(
        TextureHandle red,
        TextureHandle green,
        TextureHandle blue,
        TextureHandle distance,
        Vector3f origin,
        float spacing,
        int countX,
        int countY,
        int countZ)
    {
    }

    /// <inheritdoc/>
    public void ClearRealtimeProbeVolume()
    {
    }

    /// <inheritdoc/>
    public void SetIrradianceVolumeSettings(in IrradianceVolumeSettings settings) =>
        VolumeSettings.Add(settings);

    /// <summary>The occlusion settings each frame was given, in order.</summary>
    public List<AmbientOcclusionSettings> Occlusion { get; } = [];

    /// <inheritdoc/>
    public void SetAmbientOcclusion(in AmbientOcclusionSettings settings) => Occlusion.Add(settings);

    /// <summary>The previous poses reported, in order; a null entry is a clear.</summary>
    public List<Matrix4f?> PreviousPoses { get; } = [];

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
        Vector3d cameraPosition,
        Matrix3f viewToWorld) => Temporal.Add(settings);

    /// <summary>The cascade indices that were rendered, in order.</summary>
    public List<int> ShadowCascadesRendered { get; } = [];

    /// <summary>Set to make BeginShadowCascade refuse, as a backend with no shadow map would.</summary>
    public bool RefuseShadows { get; set; }

    /// <summary>The fog settings each frame was given, in order.</summary>
    public List<VolumetricFogSettings> Fogs { get; } = [];

    /// <summary>The wetness settings applied, in order, with the shelter count each carried.</summary>
    public List<WetnessSettings> Wetness { get; } = [];

    /// <summary>Every shelter map handed over, newest last.</summary>
    public List<(TextureHandle Map, ShelterMapTransform Transform)> ShelterMaps { get; } = [];

    /// <inheritdoc/>
    public void SetWetness(in WetnessSettings wetness) => Wetness.Add(wetness);

    /// <inheritdoc/>
    public void SetShelterMap(TextureHandle map, in ShelterMapTransform transform) =>
        ShelterMaps.Add((map, transform));

    /// <summary>What <see cref="GpuMeasuredTiming"/> reports. Zero unless a test sets it.</summary>
    public GpuFrameTiming MeasuredTiming { get; set; }

    /// <inheritdoc/>
    public GpuFrameTiming GpuMeasuredTiming() => MeasuredTiming;

    /// <inheritdoc/>
    /// <remarks>
    /// False unless a test says otherwise: this double rasterises nothing, so the cascades are the
    /// only sun shadow it could be said to have.
    /// </remarks>
    public bool SunShadowTraced { get; set; }

    /// <inheritdoc/>
    /// <remarks>One unless a test says otherwise: this double resolves nothing, so a colour written
    /// through it means what it says.</remarks>
    public float SceneExposure { get; set; } = 1f;

    /// <summary>The shadow casters submitted, in order.</summary>
    public List<(MeshHandle Mesh, Matrix4f ModelView, int IndexCount)> ShadowCasters { get; } = [];

    /// <inheritdoc/>
    public void SubmitShadowCaster(MeshHandle mesh, in Matrix4f modelView, int indexCount) =>
        ShadowCasters.Add((mesh, modelView, indexCount));

    /// <summary>The texture-quality settings applied, in order.</summary>
    public List<int> TextureQuality { get; } = [];

    /// <inheritdoc/>
    public void SetTextureQuality(int maxAnisotropy) => TextureQuality.Add(maxAnisotropy);

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

    /// <summary>Gets how many times the level's accumulated world has been released.</summary>
    public int WorldReleaseCount { get; private set; }

    /// <inheritdoc/>
    public void ReleaseWorld() => WorldReleaseCount++;

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

    public void SetMode(in DisplayMode mode) => CurrentMode = mode;

    public void Clear(Bgra32 color) => Clears.Add(color);

    public void ClearDepth() => DepthClearCount++;

    public void SwapBuffers() => SwapCount++;

    public void SetViewport(in Viewport viewport)
    {
    }

    public void BlitPixels(int x, int y, int width, int height, ReadOnlySpan<Bgra32> pixels)
    {
    }

    public void CaptureFrame(int width, int height, Span<Bgra32> destination) => destination.Clear();

    public void CopyFrameToTexture(TextureHandle texture, int width, int height)
    {
    }

    public void DrawLoadingBackdrop(TextureHandle texture, in LoadingBackdropSettings settings)
    {
    }

    /// <summary>Every 2D texture created, in order, with the description it was created under.</summary>
    /// <remarks>
    /// The description rather than only the pixels, because the address mode and the mip count are
    /// decisions a caller makes and gets silently wrong: <c>TextureDescription</c>'s two-argument
    /// constructor is 2D drawing's -- one level, clamped -- and taking it for a tiling world texture
    /// smears its edge texel across everything it is drawn on.
    /// </remarks>
    public List<TextureDescription> Textures { get; } = [];

    public TextureHandle CreateTexture(in TextureDescription description, ReadOnlySpan<Bgra32> pixels)
    {
        Textures.Add(description);
        return new(_nextTexture++);
    }

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

    /// <summary>The storage meshes' capacities, by mesh id.</summary>
    public Dictionary<uint, (int VertexCapacity, int IndexCapacity)> MeshStorageCapacities { get; } = [];

    /// <summary>How many ranges each storage mesh received.</summary>
    public Dictionary<uint, int> MeshRangeUploads { get; } = [];

    public MeshHandle CreateMeshStorage(int vertexCapacity, int indexCapacity)
    {
        if (vertexCapacity <= 0 || indexCapacity <= 0)
        {
            throw new ArgumentException(
                $"A storage mesh needs positive capacities, got {vertexCapacity} vertices and {indexCapacity} indices.",
                nameof(vertexCapacity));
        }

        if (vertexCapacity > ushort.MaxValue + 1)
        {
            throw new ArgumentException(
                $"A 16-bit index buffer addresses at most {ushort.MaxValue + 1} vertices, got {vertexCapacity}.",
                nameof(vertexCapacity));
        }

        MeshHandle handle = new(_nextMesh++);
        Meshes[handle.Id] = (new Vertex3D[vertexCapacity], new ushort[indexCapacity]);
        MeshStorageCapacities[handle.Id] = (vertexCapacity, indexCapacity);
        MeshRangeUploads[handle.Id] = 0;
        return handle;
    }

    public void UploadMeshRange(
        MeshHandle mesh,
        int vertexOffset,
        ReadOnlySpan<Vertex3D> vertices,
        int indexOffset,
        ReadOnlySpan<ushort> indices)
    {
        if (!Meshes.TryGetValue(mesh.Id, out (Vertex3D[] Vertices, ushort[] Indices) entry))
        {
            throw new ArgumentException($"{mesh} does not belong to this device.", nameof(mesh));
        }

        if (!MeshStorageCapacities.TryGetValue(mesh.Id, out (int VertexCapacity, int IndexCapacity) capacity))
        {
            throw new ArgumentException($"{mesh} was not created with {nameof(CreateMeshStorage)}.", nameof(mesh));
        }

        if (vertexOffset < 0 || vertexOffset + vertices.Length > capacity.VertexCapacity)
        {
            throw new ArgumentException(
                $"Vertices {vertexOffset}..{vertexOffset + vertices.Length} lie outside the mesh's capacity of {capacity.VertexCapacity}.",
                nameof(vertices));
        }

        if (indexOffset < 0 || indexOffset + indices.Length > capacity.IndexCapacity)
        {
            throw new ArgumentException(
                $"Indices {indexOffset}..{indexOffset + indices.Length} lie outside the mesh's capacity of {capacity.IndexCapacity}.",
                nameof(indices));
        }

        vertices.CopyTo(entry.Vertices.AsSpan(vertexOffset));
        indices.CopyTo(entry.Indices.AsSpan(indexOffset));
        MeshRangeUploads[mesh.Id]++;
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
        MeshStorageCapacities.Remove(mesh.Id);
        MeshRangeUploads.Remove(mesh.Id);
    }

    public void SetDepthState(DepthState state) => DepthState = state;

    public void SetCullMode(CullMode mode) => CullMode = mode;

    public void SetProjection(in Matrix4f projection) => Projection = projection;

    public void SetDetailFade(float scale, float offset, float slope, bool fadeAlpha) =>
        DetailFade = (scale, offset, slope, fadeAlpha);

    public void ClearDetailFade() => DetailFade = null;

    public (float Scale, float Offset, float Slope, bool FadeAlpha)? DetailFade { get; private set; }

    public void SetFog(in SceneFog fog) => Fog = fog;

    /// <summary>Gets the fog the last <see cref="SetFog"/> asked for.</summary>
    public SceneFog Fog { get; private set; } = SceneFog.None;

    public void SetModelAmbient(Vector3f ambient) => ModelAmbient = ambient;

    /// <summary>Gets the flat model light the last <see cref="SetModelAmbient"/> asked for.</summary>
    public Vector3f ModelAmbient { get; private set; } = Vector3f.One;

    public void DrawMesh(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend) =>
        MeshDraws.Add(new MeshDrawCall(
            mesh,
            indexOffset,
            indexCount,
            modelView,
            diffuse,
            lightmap,
            blend,
            DepthState,
            CullMode,
            Fog,
            ModelAmbient,
            LightmapScale,
            VertexSun,
            FogTarget));

    public void DrawMeshRanges(
        MeshHandle mesh,
        ReadOnlySpan<MeshDrawRange> ranges,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend)
    {
        MeshBatchDrawCount++;
        foreach (MeshDrawRange range in ranges)
        {
            DrawMesh(mesh, range.IndexOffset, range.IndexCount, modelView, diffuse, lightmap, blend);
        }
    }

    /// <summary>Gets how many silhouette draws have been submitted.</summary>
    public int OutlineDrawCount { get; private set; }

    /// <summary>Gets the last outline's colour.</summary>
    public Bgra32 LastOutlineColour { get; private set; }

    /// <summary>Gets whether the last outline ignored the depth buffer.</summary>
    public bool LastOutlineThroughWalls { get; private set; }

    /// <inheritdoc/>
    public void DrawOutline(
        MeshHandle mesh,
        ReadOnlySpan<MeshDrawRange> ranges,
        in Matrix4f modelView,
        Bgra32 colour,
        float radius,
        bool throughWalls)
    {
        OutlineDrawCount++;
        LastOutlineColour = colour;
        LastOutlineThroughWalls = throughWalls;
    }

    public int MeshBatchDrawCount { get; private set; }

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
        CullMode CullMode,
        SceneFog Fog = default,
        Vector3f ModelAmbient = default,
        float LightmapScale = 1f,
        Vector3f VertexSun = default,
        FogTarget FogTarget = FogTarget.Scene)
    {
        public int TriangleCount => IndexCount / 3;
    }
}
