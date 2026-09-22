using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Temporal;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>
/// The temporal resolve: eight compute passes that turn this frame's shading and the last frame's
/// history into one steadier picture.
/// </summary>
/// <remarks>
/// <para>
/// One class rather than eight, because they are never run apart and the order is not a preference. The
/// motion is dilated so a silhouette reprojects with the surface that owns it; the history is decimated
/// where the reprojection missed; thin features relax their rejection so a wire is not rejected every
/// frame for being narrower than the kernel; the shading is compared against the guide; that rejection
/// is smoothed once rather than four times over; the spatial filter finds the edges; the history is
/// updated; and the result is resolved to the output.
/// </para>
/// <para>
/// Each pass binds what it needs by name from the target set, which is why the whole chain is this
/// short: the layouts are read from the modules and the group is rebuilt only when a target changes.
/// </para>
/// </remarks>
public sealed class TemporalResolve : IDisposable
{
    private readonly ComputePass _dilateVelocity;
    private readonly ComputePass _decimateHistory;
    private readonly ComputePass _thinGeometry;
    private readonly ComputePass _rejectShading;
    private readonly ComputePass _rejectionDilate;
    private readonly ComputePass _spatialAntiAliasing;
    private readonly ComputePass _updateHistory;
    private readonly ComputePass _resolveHistory;

    private readonly ComputeParameters _reprojection;
    private readonly ComputeParameters _thin;
    private readonly ComputeParameters _shading;
    private readonly ComputeParameters _dilate;
    private readonly ComputeParameters _spatial;
    private readonly ComputeParameters _update;
    private readonly ComputeParameters _resolve;
    private readonly IRhiSampler _sampler;

    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="TemporalResolve"/> class.</summary>
    /// <param name="device">The device the passes run on.</param>
    /// <param name="framesInFlight">How many parameter buffers to keep of each.</param>
    public TemporalResolve(IRhiDevice device, int framesInFlight)
    {
        ArgumentNullException.ThrowIfNull(device);

        _dilateVelocity = new ComputePass(device, ShaderModuleId.UpscaleDilateVelocity, "upscale.dilate-velocity");
        _decimateHistory = new ComputePass(device, ShaderModuleId.UpscaleDecimateHistory, "upscale.decimate");
        _thinGeometry = new ComputePass(device, ShaderModuleId.UpscaleThinGeometry, "upscale.thin");
        _rejectShading = new ComputePass(device, ShaderModuleId.UpscaleRejectShading, "upscale.reject", RejectShadingVariant);
        _rejectionDilate = new ComputePass(device, ShaderModuleId.UpscaleRejectionDilate, "upscale.reject-dilate");
        _spatialAntiAliasing = new ComputePass(device, ShaderModuleId.UpscaleSpatialAntiAliasing, "upscale.spatial");
        _updateHistory = new ComputePass(device, ShaderModuleId.UpscaleUpdateHistory, "upscale.update");
        _resolveHistory = new ComputePass(device, ShaderModuleId.UpscaleResolveHistory, "upscale.resolve");

        _reprojection = Block(device, TemporalParameters.ReprojectionFloats, framesInFlight, "upscale.reprojection");
        _thin = Block(device, TemporalParameters.ThinGeometryFloats, framesInFlight, "upscale.thin");
        _shading = Block(device, TemporalParameters.RejectShadingFloats, framesInFlight, "upscale.reject");
        _dilate = Block(device, TemporalParameters.SizeFloats, framesInFlight, "upscale.reject-dilate");
        _spatial = Block(device, TemporalParameters.SpatialAntiAliasingFloats, framesInFlight, "upscale.spatial");
        _update = Block(device, TemporalParameters.UpdateHistoryFloats, framesInFlight, "upscale.update");
        _resolve = Block(device, TemporalParameters.ResolveHistoryFloats, framesInFlight, "upscale.resolve");

        // One sampler for the whole chain. Every field these passes read is screen-space and read the
        // same way: bilinear, clamped, because a reprojection lands between texels and a coordinate off
        // the edge has no neighbour to wrap to.
        _sampler = device.CreateSampler(new RhiSamplerDescriptor(Label: "upscale"));
    }

    /// <summary>The variant of the shading rejection this build uses.</summary>
    /// <remarks>
    /// The eight-wide tile, which every adapter has the shared memory for. The sixteen-wide one carries
    /// its apron once over four times the area and is the faster of the two where it fits; choosing
    /// between them by what the adapter reports belongs with the rest of the preset work.
    /// </remarks>
    private static ShaderVariant RejectShadingVariant => new("group8");

    /// <summary>Records the whole chain for one frame.</summary>
    /// <param name="pass">The compute pass to record into.</param>
    /// <param name="targets">The frame's targets.</param>
    /// <param name="state">The frame and the one before it.</param>
    /// <param name="settings">What the resolve is asked to do.</param>
    /// <param name="frameInFlight">Which parameter buffers this frame writes.</param>
    /// <remarks>
    /// The order is the whole of it. Every pass reads what the one before it wrote, and the two that
    /// read the previous frame — the decimation and the update — read the other half of a history pair
    /// rather than what this frame has already put there.
    /// </remarks>
    public void Record(
        IRhiComputePassEncoder pass,
        SceneTargetSet targets,
        in TemporalFrameState state,
        in TemporalResolveSettings settings,
        int frameInFlight)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(targets);
        ObjectDisposedException.ThrowIf(_disposed, this);

        int width = targets.Width;
        int height = targets.Height;

        DilateVelocity(pass, targets, state, frameInFlight, width, height);
        DecimateHistory(pass, targets, state, frameInFlight, width, height);
        ThinGeometry(pass, targets, state, settings, frameInFlight, width, height);
        RejectShading(pass, targets, state, settings, frameInFlight, width, height);
        DilateRejection(pass, targets, state, frameInFlight, width, height);
        SpatialAntiAliasing(pass, targets, state, settings, frameInFlight, width, height);
        UpdateHistory(pass, targets, state, settings, frameInFlight, width, height);
        ResolveHistory(pass, targets, settings, frameInFlight, width, height);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IDisposable owned in (ReadOnlySpan<IDisposable>)
        [
            _dilateVelocity, _decimateHistory, _thinGeometry, _rejectShading,
            _rejectionDilate, _spatialAntiAliasing, _updateHistory, _resolveHistory,
            _reprojection, _thin, _shading, _dilate, _spatial, _update, _resolve,
            _sampler,
        ])
        {
            owned.Dispose();
        }
    }

    private static ComputeParameters Block(IRhiDevice device, int floats, int framesInFlight, string label) =>
        new(device, floats * sizeof(float), framesInFlight, label);

    private void DilateVelocity(
        IRhiComputePassEncoder pass,
        SceneTargetSet targets,
        in TemporalFrameState state,
        int frame,
        int width,
        int height)
    {
        Span<float> block = stackalloc float[TemporalParameters.ReprojectionFloats];
        TemporalParameters.WriteReprojection(block, state);

        _dilateVelocity.Bind("uDepth", targets.View(SceneTargetId.Depth));
        _dilateVelocity.Bind("uVelocity", targets.View(SceneTargetId.Velocity));
        _dilateVelocity.Bind("uDilatedVelocity", targets.View(SceneTargetId.DilatedVelocity));
        _dilateVelocity.Bind("uClosestDepth", targets.View(SceneTargetId.ClosestDepth));
        _dilateVelocity.Bind("uOccluder", targets.View(SceneTargetId.ClosestOccluder));
        _dilateVelocity.Bind("p", _reprojection.Write<float>(frame, block));

        _dilateVelocity.BindRemainingSamplers(_sampler);
        _dilateVelocity.Dispatch(pass, width, height);
    }

    private void DecimateHistory(
        IRhiComputePassEncoder pass,
        SceneTargetSet targets,
        in TemporalFrameState state,
        int frame,
        int width,
        int height)
    {
        Span<float> block = stackalloc float[TemporalParameters.ReprojectionFloats];
        TemporalParameters.WriteReprojection(block, state);

        _decimateHistory.Bind("uDepth", targets.View(SceneTargetId.Depth));
        _decimateHistory.Bind("uDilatedVelocity", targets.View(SceneTargetId.DilatedVelocity));

        // The other half of the pair: what the frame before this one wrote, not what this one just did.
        _decimateHistory.Bind("uPrevDepth", targets.PreviousView(SceneTargetId.PreviousDepth));
        _decimateHistory.Bind("uRejection", targets.View(SceneTargetId.HistoryRejection));
        _decimateHistory.Bind("uOccluder", targets.View(SceneTargetId.ClosestOccluder));
        _decimateHistory.Bind("p", _reprojection.Write<float>(frame, block));

        _decimateHistory.BindRemainingSamplers(_sampler);
        _decimateHistory.Dispatch(pass, width, height);
    }

    private void ThinGeometry(
        IRhiComputePassEncoder pass,
        SceneTargetSet targets,
        in TemporalFrameState state,
        in TemporalResolveSettings settings,
        int frame,
        int width,
        int height)
    {
        Span<float> block = stackalloc float[TemporalParameters.ThinGeometryFloats];
        TemporalParameters.WriteThinGeometry(block, state, settings, hasRejection: true, hasVelocity: true);

        _thinGeometry.Bind("uDepth", targets.View(SceneTargetId.Depth));
        _thinGeometry.Bind("uRejection", targets.View(SceneTargetId.HistoryRejection));
        _thinGeometry.Bind("uRelaxed", targets.View(SceneTargetId.ThinRelaxedRejection));
        _thinGeometry.Bind("uVelocity", targets.View(SceneTargetId.DilatedVelocity));
        _thinGeometry.Bind("p", _thin.Write<float>(frame, block));

        _thinGeometry.BindRemainingSamplers(_sampler);
        _thinGeometry.Dispatch(pass, width, height);
    }

    private void RejectShading(
        IRhiComputePassEncoder pass,
        SceneTargetSet targets,
        in TemporalFrameState state,
        in TemporalResolveSettings settings,
        int frame,
        int width,
        int height)
    {
        Span<float> block = stackalloc float[TemporalParameters.RejectShadingFloats];
        TemporalParameters.WriteRejectShading(block, state, settings, hasParallax: true);

        _rejectShading.Bind("uCurrent", targets.View(SceneTargetId.Lit));
        _rejectShading.Bind("uDilatedVelocity", targets.View(SceneTargetId.DilatedVelocity));
        _rejectShading.Bind("uPrevGuide", targets.PreviousView(SceneTargetId.Guide));
        _rejectShading.Bind("uParallaxRejection", targets.View(SceneTargetId.HistoryRejection));
        _rejectShading.Bind("uRejection", targets.View(SceneTargetId.ShadingRejection));
        _rejectShading.Bind("uGuide", targets.View(SceneTargetId.Guide));
        _rejectShading.Bind("uThinGeometry", targets.View(SceneTargetId.ThinRelaxedRejection));
        _rejectShading.Bind("p", _shading.Write<float>(frame, block));

        _rejectShading.BindRemainingSamplers(_sampler);
        _rejectShading.Dispatch(pass, width, height);
    }

    private void DilateRejection(
        IRhiComputePassEncoder pass,
        SceneTargetSet targets,
        in TemporalFrameState state,
        int frame,
        int width,
        int height)
    {
        Span<float> block = stackalloc float[TemporalParameters.SizeFloats];
        TemporalParameters.WriteSize(block, state.Width, state.Height);

        _rejectionDilate.Bind("uRejection", targets.View(SceneTargetId.ShadingRejection));
        _rejectionDilate.Bind("uBlend", targets.View(SceneTargetId.RejectionBlend));
        _rejectionDilate.Bind("p", _dilate.Write<float>(frame, block));

        _rejectionDilate.BindRemainingSamplers(_sampler);
        _rejectionDilate.Dispatch(pass, width, height);
    }

    private void SpatialAntiAliasing(
        IRhiComputePassEncoder pass,
        SceneTargetSet targets,
        in TemporalFrameState state,
        in TemporalResolveSettings settings,
        int frame,
        int width,
        int height)
    {
        Span<float> block = stackalloc float[TemporalParameters.SpatialAntiAliasingFloats];
        TemporalParameters.WriteSpatialAntiAliasing(block, state, settings, hasRejection: true);

        _spatialAntiAliasing.Bind("uInput", targets.View(SceneTargetId.Lit));
        _spatialAntiAliasing.Bind("uRejection", targets.View(SceneTargetId.ShadingRejection));
        _spatialAntiAliasing.Bind("uOffset", targets.View(SceneTargetId.SpatialAntiAliasing));
        _spatialAntiAliasing.Bind("p", _spatial.Write<float>(frame, block));

        _spatialAntiAliasing.BindRemainingSamplers(_sampler);
        _spatialAntiAliasing.Dispatch(pass, width, height);
    }

    private void UpdateHistory(
        IRhiComputePassEncoder pass,
        SceneTargetSet targets,
        in TemporalFrameState state,
        in TemporalResolveSettings settings,
        int frame,
        int width,
        int height)
    {
        Span<float> block = stackalloc float[TemporalParameters.UpdateHistoryFloats];

        TemporalParameters.WriteUpdateHistory(
            block,
            state,
            settings,
            width,
            height,
            TemporalHistoryInputs.All);

        _updateHistory.Bind("uCurrent", targets.View(SceneTargetId.Lit));
        _updateHistory.Bind("uVelocity", targets.View(SceneTargetId.DilatedVelocity));
        _updateHistory.Bind("uPrevHistoryColour", targets.PreviousView(SceneTargetId.ResolveHistoryColour));
        _updateHistory.Bind("uPrevHistoryMetadata", targets.PreviousView(SceneTargetId.ResolveHistoryMetadata));
        _updateHistory.Bind("uHistoryColour", targets.View(SceneTargetId.ResolveHistoryColour));
        _updateHistory.Bind("uHistoryMetadata", targets.View(SceneTargetId.ResolveHistoryMetadata));
        _updateHistory.Bind("uHistoryRejection", targets.View(SceneTargetId.ThinRelaxedRejection));
        _updateHistory.Bind("uSpatialAa", targets.View(SceneTargetId.SpatialAntiAliasing));
        _updateHistory.Bind("uShadingRejection", targets.View(SceneTargetId.ShadingRejection));
        _updateHistory.Bind("uRejectionBlend", targets.View(SceneTargetId.RejectionBlend));
        _updateHistory.Bind("uDepth", targets.View(SceneTargetId.Depth));
        _updateHistory.Bind("p", _update.Write<float>(frame, block));

        _updateHistory.BindRemainingSamplers(_sampler);

        // Over the HISTORY, which is larger than the frame in each axis.
        _updateHistory.Dispatch(
            pass,
            width * settings.EffectiveHistoryScale,
            height * settings.EffectiveHistoryScale);
    }

    private void ResolveHistory(
        IRhiComputePassEncoder pass,
        SceneTargetSet targets,
        in TemporalResolveSettings settings,
        int frame,
        int width,
        int height)
    {
        Span<float> block = stackalloc float[TemporalParameters.ResolveHistoryFloats];

        TemporalParameters.WriteResolveHistory(
            block,
            settings,
            width * settings.EffectiveHistoryScale,
            height * settings.EffectiveHistoryScale,
            width,
            height);

        _resolveHistory.Bind("uHistoryColour", targets.View(SceneTargetId.ResolveHistoryColour));
        _resolveHistory.Bind("uOutput", targets.View(SceneTargetId.ResolveOutput));
        _resolveHistory.Bind("uVelocity", targets.View(SceneTargetId.DilatedVelocity));
        _resolveHistory.Bind("uParallax", targets.View(SceneTargetId.HistoryRejection));
        _resolveHistory.Bind("uShading", targets.View(SceneTargetId.ShadingRejection));
        _resolveHistory.Bind("uHistoryMetadata", targets.View(SceneTargetId.ResolveHistoryMetadata));
        _resolveHistory.Bind("p", _resolve.Write<float>(frame, block));

        _resolveHistory.BindRemainingSamplers(_sampler);
        _resolveHistory.Dispatch(pass, width, height);
    }
}
