using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>
/// What the direct path is told and does not act on, and the handful of things it genuinely does.
/// </summary>
/// <remarks>
/// <para>
/// A setting that is stored and then ignored is the worst kind of dead control: the value persists, the
/// options screen shows it, and the picture never changes — so the person changing it concludes the
/// effect is broken rather than absent, and anyone comparing two renders is comparing configurations
/// that were never the same. The names are therefore reported through
/// <see cref="GraphicsCapabilities.UnimplementedSettings"/>, where the shell says so out loud at
/// start-up, and each disappears from the list on the day its pass lands.
/// </para>
/// <para>
/// The alternative — throwing — would be worse and not more honest: the game calls these
/// unconditionally, so a device that threw would run no frame at all rather than the frame it can draw.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    // The stage a host bracket has named for the frame's own passes, or none outside a bracket.
    private RenderStage? _stageBracket;

    /// <summary>The settings this device does not act on over the backend it was given.</summary>
    /// <param name="temporal">Whether the backend can run the temporal resolve.</param>
    /// <param name="gi">Whether it can run the game's GI stack.</param>
    /// <param name="traced">Whether it traces rays, which is what the traced sun shadow needs.</param>
    /// <param name="metered">Whether it can meter the scene for the eye adaptation.</param>
    /// <returns>The names.</returns>
    private static string[] UnimplementedOn(bool temporal, bool gi, bool traced, bool metered) =>
    [
        .. temporal ? Array.Empty<string>() : ["r_taa"],
        .. gi ? Array.Empty<string>() : ["r_screen_gi", "r_surface_cache"],
        .. traced ? Array.Empty<string>() : ["r_rt_shadows", "r_rt_lights", "r_rt_reflections"],
        .. metered ? Array.Empty<string>() : ["r_autoexposure"],
    ];

    /// <inheritdoc/>
    /// <remarks>
    /// The setting times what the eye has adapted to while a capture is open; one outside it, because the
    /// direct path never tone-maps: what it draws is what is shown.
    /// </remarks>
    public float SceneExposure => _capturing
        ? MathF.Max(ExposureCVars.EffectiveExposure * (_autoExposure?.Adapted ?? 1f), 1e-3f)
        : 1f;

    /// <inheritdoc/>
    public void SetTextureQuality(int maxAnisotropy) => _textures.MaxAnisotropy = maxAnisotropy;

    /// <inheritdoc/>
    public void SetPointMips(bool point) => _textures.PointMips = point;

    /// <inheritdoc/>
    public void SetStageTiming(bool enabled) => _profiler.Enabled = enabled;

    /// <inheritdoc/>
    public IReadOnlyList<RenderStageTiming> StageTimings() => _profiler.Ledger.Timings();

    /// <inheritdoc/>
    public GpuFrameTiming GpuTiming() => _profiler.Ledger.TotalTiming();

    /// <inheritdoc/>
    public IReadOnlyList<RenderPassTiming> PassTimings() => _profiler.Ledger.PassTimings();

    /// <inheritdoc/>
    public void SetComputeTimingLabel(string? label) => _computeTimingLabel = label;

    /// <inheritdoc/>
    public IReadOnlyList<RenderPassTiming> WorstFramePassTimings(double fraction, out int frames, out double averageMilliseconds) =>
        _profiler.Ledger.WorstFramePassTimings(fraction, out frames, out averageMilliseconds);

    /// <inheritdoc/>
    public GpuFrameTiming GpuMeasuredTiming() => _profiler.Ledger.MeasuredTiming();

    /// <inheritdoc/>
    public IGpuWorkMarker MarkRecordedWork()
    {
        ThrowIfDisposed();

        RecordedWorkMarker marker = new();
        if (_inFrame)
        {
            _unarmedMarkers.Add(marker);
        }
        else
        {
            marker.Arm(_device.MarkSubmittedWork());
        }

        return marker;
    }

    /// <summary>Arms every marker taken in the frame, once what it recorded has gone to the GPU.</summary>
    /// <remarks>
    /// After the RHI's own frame end or flush, not merely after this device's submit: a backend may hold a
    /// frame's command buffers until then, and a marker the RHI hands out covers only what it has sent.
    /// </remarks>
    private void ArmRecordedWorkMarkers()
    {
        if (_unarmedMarkers.Count == 0)
        {
            return;
        }

        IRhiWorkMarker submitted = _device.MarkSubmittedWork();
        foreach (RecordedWorkMarker marker in _unarmedMarkers)
        {
            marker.Arm(submitted);
        }

        _unarmedMarkers.Clear();
    }

    /// <summary>A marker on recorded work, which the RHI can only answer for once it has been sent.</summary>
    private sealed class RecordedWorkMarker : IGpuWorkMarker
    {
        private IRhiWorkMarker? _submitted;

        public bool IsComplete => _submitted?.IsComplete ?? false;

        public void Arm(IRhiWorkMarker submitted) => _submitted = submitted;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The RHI times a pass rather than an arbitrary range, so a bracket names the stage of the passes
    /// opened inside it that have no stage of their own: the frame's pass, which the interface, the debug
    /// drawing and the whole of the direct path draw into. Every scene pass and every lighting dispatch is
    /// timed as its own stage whatever bracket it falls in.
    /// </para>
    /// <para>
    /// A frame pass already open was timed for what came before the bracket, so while anything is
    /// measuring it is closed at the bracket's edge and the next draw opens one timed for this stage.
    /// Nothing is measuring otherwise, and then the break would buy nothing.
    /// </para>
    /// </remarks>
    public void BeginStageTiming(RenderStage stage) => SetStageBracket(stage);

    /// <inheritdoc/>
    public void EndStageTiming() => SetStageBracket(null);

    /// <summary>Moves into or out of a bracket, closing a frame pass timed for the other side of it.</summary>
    private void SetStageBracket(RenderStage? stage)
    {
        if (_stageBracket == stage)
        {
            return;
        }

        _stageBracket = stage;

        if (_profiler.Enabled)
        {
            ClosePass();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Read by the geometry pass, which writes it into the G-buffer's material, and by the card capture,
    /// which writes it into every page it draws.
    /// </remarks>
    public void SetMaterialOverride(in SurfaceMaterial material) => _material = material;

    /// <inheritdoc/>
    /// <remarks>
    /// Kept for the geometry pass, which dithers a cut-out's alpha against it so that a surface fading
    /// out of the world does so in a pattern the temporal resolve can average rather than in a hard step.
    /// </remarks>
    public void SetAlphaHash(float seed) => _alphaHashSeed = seed;

    /// <inheritdoc/>
    /// <remarks>
    /// Recorded against the diffuse texture's own id and looked up whenever a draw names that diffuse, so
    /// nothing between here and the G-buffer has to carry it: the material group a draw binds is keyed by
    /// all three textures and built once.
    /// </remarks>
    public void SetTextureDetail(TextureHandle diffuse, TextureHandle detail)
    {
        ThrowIfDisposed();

        if (_textures.Find(diffuse) is null)
        {
            throw new GraphicsDeviceException($"{diffuse} does not belong to this device.");
        }

        if (_textures.Find(detail) is not null)
        {
            PairDetail(diffuse.Id, detail.Id);
        }
        else
        {
            UnpairDetail(diffuse.Id);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Per frame rather than per draw, so it travels in the geometry pass's surface block rather than in
    /// the push block every draw already fills. Nought folds the whole detail term away in the shader,
    /// which is what every preset below high asks for. Written with the floor gloss as the next capture
    /// opens.
    /// </remarks>
    public void SetMaterialDetail(float strength)
    {
        ThrowIfDisposed();
        _materialDetail = MathF.Max(strength, 0f);
    }

    /// <inheritdoc/>
    public void SetAmbientOcclusion(in AmbientOcclusionSettings settings) => _occlusionSettings = settings;
}
