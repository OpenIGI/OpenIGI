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
    // DrawOutline is implemented in VulkanGraphicsDevice.Outline.cs — the selection glow.

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
    public void SetLightmapModulates(bool modulate)
    {
        ThrowIfDisposed();
        _groundDetail = modulate;
    }

    /// <inheritdoc/>
    public void SetSceneSun(Vector3f ambient, Vector3f colour)
    {
        ThrowIfDisposed();

        // Both forward pipelines, for the reason SetSceneFog gives: an object lit on one and not the
        // other would change with the preset.
        _mesh3D.SetSun(ambient, colour);
        _deferred?.Forward.SetSun(ambient, colour);
    }

    /// <inheritdoc/>
    public void SetVertexSun(Vector3f towardsSun)
    {
        ThrowIfDisposed();
        _vertexSun = towardsSun;
    }

    // The model-space direction towards the fixed-function sun for the draws in flight, or zero.
    private Vector3f _vertexSun;

    /// <inheritdoc/>
    public void ClearDetailFade()
    {
        ThrowIfDisposed();
        _detailFade = default;
    }

    /// <inheritdoc/>
    public void SetFog(in SceneFog fog)
    {
        ThrowIfDisposed();
        _fog = fog;
    }

    /// <inheritdoc/>
    public SceneFog Fog => _fog;

    /// <inheritdoc/>
    public void SetModelAmbient(Vector3f ambient)
    {
        ThrowIfDisposed();
        _modelAmbient = ambient;
    }

    /// <inheritdoc/>
    public Vector3f ModelAmbient => _modelAmbient;

    /// <inheritdoc/>
    public void SetAlphaHash(float seed)
    {
        ThrowIfDisposed();
        _alphaHashSeed = seed;
    }

    private float _alphaHashSeed;

    /// <inheritdoc/>
    public void SetAlphaReference(byte reference)
    {
        ThrowIfDisposed();
        _alphaReference = reference;
    }

    // Part of the pipeline rather than of a draw, because there is no push-constant room left in either
    // mesh block -- both are exactly the 128 bytes Vulkan guarantees, and every float is spoken for. It
    // becomes a specialization constant instead, which is why the seam takes a byte: the variant space
    // is the 256 values D3DRS_ALPHAREF itself has, and a level uses two or three of them.
    private byte _alphaReference = IGraphicsDevice.DefaultAlphaReference;


    /// <inheritdoc/>
    /// <remarks>
    /// Recorded against the diffuse texture's own id, and looked up when a draw binds that diffuse. The
    /// pairing outlives any draw, so nothing between here and the G-buffer has to carry it: the descriptor
    /// set a draw is given is keyed by all three textures and built once.
    /// </remarks>
    public void SetTextureDetail(TextureHandle diffuse, TextureHandle detail)
    {
        ThrowIfDisposed();
        if (!diffuse.IsValid || !_textures.ContainsKey(diffuse.Id))
        {
            throw new GraphicsDeviceException($"{diffuse} does not belong to this device.");
        }

        if (detail.IsValid && _textures.ContainsKey(detail.Id))
        {
            _textureDetail[diffuse.Id] = detail.Id;
        }
        else
        {
            _textureDetail.Remove(diffuse.Id);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Per frame rather than per draw, so it travels in the geometry pass's own surface block rather than
    /// in the push constant every draw already fills to the last float. Zero folds the whole detail term
    /// away in the shader, which is what every preset below high asks for.
    /// </remarks>
    public void SetMaterialDetail(float strength)
    {
        ThrowIfDisposed();
        _materialDetail = MathF.Max(strength, 0f);
    }

    private float _materialDetail;

    /// <inheritdoc/>
    /// <remarks>
    /// Applied in the G-buffer rather than in the shading, because the roughness it smooths is read by
    /// more than the lighting: the screen-space reflections decide what reflects from the same channel,
    /// and a floor that is glossy only to the light would be the one surface the effect exists for and
    /// still not reflect.
    /// </remarks>
    public void SetFloorGloss(float gloss)
    {
        ThrowIfDisposed();
        _floorGloss = Math.Clamp(gloss, 0f, 1f);
    }

    private float _floorGloss = 1f;

    /// <inheritdoc/>
    /// <remarks>
    /// Taken up by every texture created from here on — the app sets it before a level uploads anything,
    /// which is why no existing sampler is rebuilt.
    /// </remarks>
    public void SetTextureQuality(int maxAnisotropy)
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
    /// <remarks>
    /// Stored and nothing more. The handles are resolved where they are bound — in the reflection pass,
    /// and only on a frame that actually traces — because a level between missions can hold handles whose
    /// resources have already been released, and resolving one here would throw on a frame that was never
    /// going to read it.
    /// </remarks>
    public void SetSurfaceCache(in SurfaceCacheBinding binding)
    {
        ThrowIfDisposed();
        _surfaceCache = binding;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Stored and nothing more, exactly as the surface cache above is and for the same reason: the
    /// handles in it may name resources a level change has already released, and resolving one on a
    /// frame that was never going to march would throw for nothing.
    /// </remarks>
    public void SetDistanceField(in DistanceFieldBinding binding)
    {
        ThrowIfDisposed();
        _distanceField = binding;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Uploaded here and read by the resolve, which applies it to the display-encoded picture — the only
    /// space a grade means anything in, because a colourist authored it looking at a picture rather than
    /// at radiance. A table is resident until another replaces it; the upload blocks, and it happens
    /// when a level or the console chooses a grade rather than inside a frame.
    /// </remarks>
    public void SetColourLookup(ColourLookupTable? table)
    {
        ThrowIfDisposed();

        // Retired rather than freed: a grade can be chosen while frames are in flight, and their resolve
        // sets name the cube being replaced.
        _colourLut.Set(table, QueueDeletion);
    }


    // ---- Compute, storage buffers, storage images and volumes are in VulkanGraphicsDevice.Compute.cs.

    // ---- Shadows, GI, temporal and pose state --------------
    // SetShadows / SetIrradianceVolume / SetIrradianceVolumeSettings / SetTemporalAntiAliasing are
    // implemented in VulkanGraphicsDevice.Deferred.cs — they feed the probe-lit deferred lighting.

    // BeginShadowCascade / EndShadowCascade are implemented in VulkanGraphicsDevice.Deferred.cs — they
    // render the sun's cascades into the shadow map the lighting then samples.

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The world-space radiance cache stands in for the baked field wherever it is offered: same layout,
    /// same sampler, same four volumes, so the lighting reads it without knowing which it has. What it
    /// buys is stability — the cache is held in world cells rather than on the moving screen, so it does
    /// not shimmer as the camera turns the way a screen-space gather does.
    /// </para>
    /// <para>
    /// It was recorded and ignored here, which was worse than not offering it: the cache was traced and
    /// updated every frame at three of the four presets, at full cost, and then the lighting sampled the
    /// baked field instead. The work was done and thrown away.
    /// </para>
    /// </remarks>
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
        ThrowIfDisposed();

        // All four or none: three channels and a distance are one field, and a lighting pass handed a
        // partial one would sample whatever the missing slot happened to hold.
        if (!red.IsValid || !green.IsValid || !blue.IsValid || !distance.IsValid
            || countX <= 0 || countY <= 0 || countZ <= 0 || spacing <= 0f)
        {
            ClearRealtimeProbeVolume();
            return;
        }

        _realtimeProbe = new RealtimeProbeField(
            red, green, blue, distance, origin, spacing, countX, countY, countZ);
    }

    /// <inheritdoc/>
    public void ClearRealtimeProbeVolume()
    {
        ThrowIfDisposed();
        _realtimeProbe = null;
    }

    /// <summary>The world-space radiance cache offered in place of the baked probe field.</summary>
    /// <param name="Red">The red channel's harmonics.</param>
    /// <param name="Green">The green channel's.</param>
    /// <param name="Blue">The blue channel's.</param>
    /// <param name="Distance">How far each probe saw, which is what the leak rejection reads.</param>
    /// <param name="Origin">The grid's world origin in metres.</param>
    /// <param name="Spacing">Metres between probes.</param>
    /// <param name="CountX">Probes along x.</param>
    /// <param name="CountY">Probes along y.</param>
    /// <param name="CountZ">Probes along z.</param>
    private readonly record struct RealtimeProbeField(
        TextureHandle Red,
        TextureHandle Green,
        TextureHandle Blue,
        TextureHandle Distance,
        Vector3f Origin,
        float Spacing,
        int CountX,
        int CountY,
        int CountZ);

    private RealtimeProbeField? _realtimeProbe;

    // SetAmbientOcclusion is implemented in VulkanGraphicsDevice.Deferred.cs — it drives the occlusion pass.

    // A caller used to be able to report an object's previous model-view. It was recorded and ignored:
    // the object-velocity stamp keeps each drawn occurrence's own transform from the previous frame and
    // pairs the two itself (see RecordObjectVelocity), so the caller's answer only repeated what the
    // pass had already measured. The pose half of the same motion cannot be inferred from a transform,
    // which is why SetPoseInterpolation remains and the rigid half does not.
    // SetPoseInterpolation / ClearPoseInterpolation are implemented in VulkanGraphicsDevice.Draw3D.cs —
    // they blend the two vertex streams every mesh pipeline is handed.

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
            _device.Checkpoint(_frameCommandBuffer, VulkanCheckpointMarkers.StageBegin(stage));
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
            RenderStage? open = _stageProfiler?.OpenStage(_frameLoop.CurrentFrame);
            _stageProfiler?.End(_frameCommandBuffer, _frameLoop.CurrentFrame);
            _device.Checkpoint(
                _frameCommandBuffer,
                open is { } stage ? VulkanCheckpointMarkers.StageEnd(stage) : VulkanCheckpointMarkers.StageEndUnknown);
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
