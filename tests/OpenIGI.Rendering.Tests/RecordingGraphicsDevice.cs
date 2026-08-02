using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Rendering.Tests;

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

    /// <inheritdoc/>
    public void SetLighting(in SceneLighting lighting) => Lightings.Add(lighting);

    /// <summary>The shadow settings each frame was given, in order.</summary>
    public List<ShadowSettings> Shadows { get; } = [];

    /// <summary>The reflection settings each frame was given, in order.</summary>
    public List<ReflectionSettings> Reflections { get; } = [];

    /// <inheritdoc/>
    public void SetReflections(in ReflectionSettings settings) => Reflections.Add(settings);

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

    public void DestroyMesh(MeshHandle mesh) => Meshes.Remove(mesh.Id);

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
