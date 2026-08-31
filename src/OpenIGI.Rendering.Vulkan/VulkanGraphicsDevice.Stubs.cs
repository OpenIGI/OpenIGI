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
    public void SetAlphaHash(float seed)
    {
        ThrowIfDisposed();
        _alphaHashSeed = seed;
    }

    private float _alphaHashSeed;

    /// <inheritdoc/>
    public void SetTextureDetail(TextureHandle diffuse, TextureHandle detail) => ThrowIfDisposed();

    /// <inheritdoc/>
    public void SetMaterialDetail(float strength) => ThrowIfDisposed();

    /// <inheritdoc/>
    /// <remarks>The up-facing floor sheen is not modelled by the Vulkan lighting yet; recorded and ignored.</remarks>
    public void SetFloorGloss(float gloss) => ThrowIfDisposed();

    /// <inheritdoc/>
    /// <remarks>
    /// Taken up by every texture created from here on — the app sets it before a level uploads anything,
    /// which is why no existing sampler is rebuilt. Mip generation is already unconditional on this
    /// backend, so only the anisotropy is recorded.
    /// </remarks>
    public void SetTextureQuality(int maxAnisotropy, bool generateMissingMipmaps)
    {
        ThrowIfDisposed();
        _maxAnisotropy = Math.Max(1, maxAnisotropy);
    }

    private float _maxAnisotropy = 1f;

    /// <summary>How far to bias every mipped sampler's level-of-detail choice, negative for sharper.</summary>
    /// <remarks>
    /// <para>
    /// The reference's automatic view mip bias, and it exists for two reasons this renderer needs BOTH of.
    /// </para>
    /// <para>
    /// <b>The resolution term (<c>log2(scale)</c>).</b> A scene rendered below full resolution has larger
    /// screen-space texture derivatives, so the hardware picks a mip that much coarser. For a cut-out —
    /// chain-link, foliage, railings — that coarser level averages the wire's 0-or-255 alpha down below
    /// the alpha test, and the thin geometry is discarded outright: the fence turns to whatever is behind
    /// it. Biasing the level back by <c>log2(scale)</c> samples where full resolution would, which keeps
    /// the cut-out alive. This is the resolution half of the reference's formula and — unlike the temporal
    /// half — it must apply whether or not a temporal filter runs, because the discard happens regardless.
    /// </para>
    /// <para>
    /// <b>The temporal term (−0.3).</b> A temporal filter sees a different sub-pixel offset each frame and
    /// averages them, recovering detail the level-of-detail rule assumes lost, so it can afford one level
    /// sharper. Applied only where such a filter runs; without one it would sample above the rate and
    /// alias, which is the artefact mipmaps exist to remove.
    /// </para>
    /// <para>
    /// The reference's whole formula is
    /// <c>clamp(max(-max(-log2(resolutionFraction), 0) + offset, min), -4, 4)</c>, offset −0.3, floor −2.
    /// </para>
    /// <para>
    /// Read once, at texture creation, and baked into the sampler — which the descriptor sets then hold
    /// for the life of the level, so it cannot be re-derived per frame without re-writing every one of
    /// them. A level therefore loads correct for whatever <c>r_scale</c> is set at the time (which is the
    /// shipping case: the preset carries the scale, so the world loads with the right bias). Changing
    /// <c>r_scale</c> live does not re-bias textures already resident — reload the level, or bake the scale
    /// into the preset, for the cut-outs to sample correctly at the new resolution.
    /// </para>
    /// </remarks>
    private static float TextureMipLodBias()
    {
        // Clamped to 1 at the top so supersampling (scale > 1) never biases coarser -- it already samples
        // below its own mip rate, which is the whole point of it.
        float scale = Math.Clamp(RenderCVars.RenderScale.Value, 0.25f, 1f);
        float resolution = MathF.Log2(scale);
        float temporal = RenderCVars.TemporalFeedback.Value < 1f ? -0.3f : 0f;
        return Math.Clamp(MathF.Max(resolution + temporal, -2f), -4f, 4f);
    }

    // ---- Deferred effects fed into the lighting (no-op until compute lands) ------------------------

    // SetScreenIndirect and SetVolumetricFog are implemented in VulkanGraphicsDevice.Deferred.cs — the
    // former feeds the near-field GI atlas into the lighting, the latter captures the camera position.

    /// <inheritdoc/>
    /// <remarks>
    /// The screen-space reflection pass reads these each frame: the marching steps, how strongly the
    /// reflection is mixed, the roughness past which a surface stops reflecting sharply, and the depth
    /// buffer's assumed thickness. The pass itself is in <see cref="VulkanGraphicsDevice"/>'s deferred
    /// half.
    /// </remarks>
    public void SetReflections(in ReflectionSettings settings)
    {
        ThrowIfDisposed();
        _reflectionSettings = settings;
    }

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
    /// <remarks>
    /// Routed into the lighting pass, which substitutes the requested channel for the shaded scene.
    /// The resolve leaves the mask views untouched by exposure and the tone map, so a visibility of
    /// one reads as white rather than as whatever the curve makes of it.
    /// </remarks>
    public void SetDebugView(DebugView view)
    {
        ThrowIfDisposed();
        _debugView = view;
    }

    private DebugView _debugView;

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

    /// <summary>The stage this frame is currently timing, or null when none is open.</summary>
    internal RenderStage? OpenStageTiming
        => _frameOpen ? _stageProfiler?.OpenStage(_frameLoop.CurrentFrame) : null;

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

    /// <inheritdoc/>
    public GpuFrameTiming GpuMeasuredTiming() => _stageProfiler?.MeasuredTiming() ?? default;
}
