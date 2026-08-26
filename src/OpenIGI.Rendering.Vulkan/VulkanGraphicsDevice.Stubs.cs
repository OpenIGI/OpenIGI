using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// The members beyond the foundation's direct path — meshes, the deferred pipeline, compute, GI,
/// shadows, occlusion and timing — as the honest placeholders they are until each is built.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here pretends to work. The capability-gated features report unavailable through
/// <see cref="VulkanGraphicsDevice.Capabilities"/>, so the engine takes the same fallbacks a bare
/// OpenGL 3.3 context would and never calls them; if one is called anyway it throws rather than
/// silently doing nothing. The state setters the direct path still calls are no-ops because the state
/// they set only feeds the deferred pipeline, which is off. The 3D draws are no-ops that warn once, so
/// the game runs and presents its 2D layers while the world pass is still being built.
/// </para>
/// <para>
/// Each group is one milestone of the port: meshes and the forward draw, then the offscreen scene and
/// the deferred lighting, then compute and the storage resources it needs, then the shadow, GI and
/// temporal passes, and finally the acceleration structures the hardware ray tracer traces.
/// </para>
/// </remarks>
public sealed partial class VulkanGraphicsDevice
{
    // ---- The forward draw's outline (still a placeholder) -----------------------------------------

    /// <inheritdoc/>
    public void DrawOutline(
        MeshHandle mesh,
        ReadOnlySpan<MeshDrawRange> ranges,
        in Matrix4f modelView,
        Bgra32 colour,
        float radius,
        bool throughWalls) => ThrowIfDisposed();

    // ---- Pipeline state the direct path still sets -------------------------------------------------

    /// <inheritdoc/>
    public void SetDetailFade(float scale, float offset, float slope, bool fadeAlpha)
    {
        ThrowIfDisposed();

        // The fourth component is what the vertex stage reads as "this draw is terrain", and whether it is
        // a detail layer that has to fade out with distance rather than a base one that does not.
        _detailFade = (scale, offset, slope, fadeAlpha ? 2f : 1f);
    }

    /// <inheritdoc/>
    public void ClearDetailFade()
    {
        ThrowIfDisposed();
        _detailFade = default;
    }

    /// <inheritdoc/>
    public void SetAlphaHash(float seed) => ThrowIfDisposed();

    /// <inheritdoc/>
    public void SetTextureDetail(TextureHandle diffuse, TextureHandle detail) => ThrowIfDisposed();

    /// <inheritdoc/>
    public void SetMaterialDetail(float strength) => ThrowIfDisposed();

    /// <inheritdoc/>
    /// <remarks>The up-facing floor sheen is not modelled by the Vulkan lighting yet; recorded and ignored.</remarks>
    public void SetFloorGloss(float gloss) => ThrowIfDisposed();

    /// <inheritdoc/>
    /// <remarks>Recorded for when textures are created; the sampler rebuild is layered on with the 3D path.</remarks>
    public void SetTextureQuality(int maxAnisotropy, bool generateMissingMipmaps) => ThrowIfDisposed();

    // ---- Deferred effects fed into the lighting (no-op until compute lands) ------------------------

    // SetScreenIndirect and SetVolumetricFog are implemented in VulkanGraphicsDevice.Deferred.cs — the
    // former feeds the near-field GI atlas into the lighting, the latter captures the camera position.

    /// <inheritdoc/>
    public void SetReflections(in ReflectionSettings settings) => ThrowIfDisposed();

    /// <inheritdoc/>
    public void SetColourLookup(ColourLookupTable? table) => ThrowIfDisposed();


    // ---- Compute, storage buffers, storage images and volumes are in VulkanGraphicsDevice.Compute.cs.

    // ---- Shadows, GI, temporal and pose state --------------
    // SetShadows / SetIrradianceVolume / SetIrradianceVolumeSettings / SetTemporalAntiAliasing are
    // implemented in VulkanGraphicsDevice.Deferred.cs — they feed the probe-lit deferred lighting.

    // BeginShadowCascade / EndShadowCascade are implemented in VulkanGraphicsDevice.Deferred.cs — they
    // render the sun's cascades into the shadow map the lighting then samples.

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
        int countZ) => ThrowIfDisposed();

    /// <inheritdoc/>
    public void ClearRealtimeProbeVolume() => ThrowIfDisposed();

    // SetAmbientOcclusion is implemented in VulkanGraphicsDevice.Deferred.cs — it drives the occlusion pass.

    /// <inheritdoc/>
    public void SetPreviousPose(in Matrix4f previousModelView) => ThrowIfDisposed();

    /// <inheritdoc/>
    public void ClearPreviousPose() => ThrowIfDisposed();

    /// <inheritdoc/>
    public void SetPoseInterpolation(float currentBlend, float previousBlend) => ThrowIfDisposed();

    /// <inheritdoc/>
    public void ClearPoseInterpolation() => ThrowIfDisposed();

    /// <inheritdoc/>
    public void SetDebugView(DebugView view) => ThrowIfDisposed();

    // ---- Occlusion queries -------------------------------------------------------------------------

    /// <inheritdoc/>
    public OcclusionQueryHandle CreateOcclusionQuery()
    {
        ThrowIfDisposed();
        return _occlusion.Create();
    }

    /// <inheritdoc/>
    public bool OcclusionVisible(OcclusionQueryHandle query)
    {
        ThrowIfDisposed();
        return _occlusion.Visible(query);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A box is drawn against the geometry pass's depth, so a query can only be issued while that pass is
    /// open. Outside it — the direct render path, or between the deferred passes — the object is left
    /// conservatively visible rather than probed.
    /// </remarks>
    public void IssueOcclusionQuery(OcclusionQueryHandle query, in Matrix4f modelView, in OcclusionBounds bounds)
    {
        ThrowIfDisposed();
        if (!_deferredPassActive || _deferredStage != DeferredStage.Geometry)
        {
            return;
        }

        Matrix4f modelViewProjection = _projection * modelView;
        _occlusion.Issue(_frameCommandBuffer, _frameLoop.CurrentFrame, query, modelViewProjection, bounds);

        // The box binds a pipeline of its own, so whatever the last mesh draw left bound is gone.
        InvalidateBoundState();
    }

    /// <inheritdoc/>
    public void DestroyOcclusionQuery(OcclusionQueryHandle query)
    {
        ThrowIfDisposed();
        _occlusion.Destroy(query);
    }

    /// <inheritdoc/>
    public void SetStageTiming(bool enabled)
    {
        ThrowIfDisposed();
        if (_stageProfiler is { } profiler)
        {
            profiler.Enabled = enabled;
        }
    }

    /// <inheritdoc/>
    public void BeginStageTiming(RenderStage stage)
    {
        ThrowIfDisposed();
        if (_frameOpen)
        {
            _stageProfiler?.Begin(stage, _frameCommandBuffer, _frameLoop.CurrentFrame);
        }
    }

    /// <inheritdoc/>
    public void EndStageTiming()
    {
        ThrowIfDisposed();
        if (_frameOpen)
        {
            _stageProfiler?.End(_frameCommandBuffer, _frameLoop.CurrentFrame);
        }
    }

    /// <summary>
    /// Times <paramref name="stage"/> on the GPU for as long as the returned scope is alive, the backend's
    /// own bracket for a deferred pass the game does not bracket itself.
    /// </summary>
    /// <remarks>
    /// The counterpart to the OpenGL backend's <c>Measure</c>. The deferred passes -- geometry, lighting,
    /// forward, the resolve -- run entirely inside the backend, so unless the backend brackets them nothing
    /// does and the whole GPU frame reads as the one stage the game happens to bracket around it (the GI
    /// gather). Ranges cannot nest, so a scope must be disposed before the next stage opens; the deferred
    /// passes are strictly sequential, so that holds. Same-stage ranges in one frame sum, so a shadow
    /// cascade drawn several times still reports one Shadows figure.
    /// </remarks>
    private StageScope Measure(RenderStage stage) => new(this, stage);

    private readonly struct StageScope : IDisposable
    {
        private readonly VulkanGraphicsDevice _device;

        public StageScope(VulkanGraphicsDevice device, RenderStage stage)
        {
            _device = device;
            device.BeginStageTiming(stage);
        }

        public void Dispose() => _device.EndStageTiming();
    }

    /// <inheritdoc/>
    public IReadOnlyList<RenderStageTiming> StageTimings() => _stageProfiler?.Timings() ?? [];

    /// <inheritdoc/>
    public GpuFrameTiming GpuTiming() => _stageProfiler?.TotalTiming() ?? default;
}
