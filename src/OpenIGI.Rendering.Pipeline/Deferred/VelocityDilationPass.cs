using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Temporal;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>
/// Dilates each pixel's motion onto the closest surface around it.
/// </summary>
/// <remarks>
/// <para>
/// A silhouette pixel sits on the boundary between a near surface and whatever is behind it, and the
/// temporal filter reprojects it with whatever single motion vector that pixel holds. Where that vector
/// belongs to the background, the edge is dragged along with it and the foreground's outline smears.
/// Taking the motion of the closest surface in the neighbourhood instead makes the edge reproject with
/// the thing that owns it.
/// </para>
/// <para>
/// <b>SPIR-V only.</b> The closest-occluder scatter is an atomic minimum on a storage image, and WGSL
/// has atomics on storage-buffer and workgroup variables alone — never on textures. A browser form has
/// to scatter into a storage buffer instead, which changes what the host allocates rather than what the
/// shader says; until that exists this pass stands down there, and a thin occluder drags a trail the
/// way it did before the scatter existed.
/// </para>
/// </remarks>
public sealed class VelocityDilationPass : IDisposable
{
    private readonly ComputePass _pass;
    private readonly ComputeParameters _parameters;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VelocityDilationPass"/> class.</summary>
    /// <param name="device">The device the pass runs on.</param>
    /// <param name="framesInFlight">How many parameter buffers to keep.</param>
    public VelocityDilationPass(IRhiDevice device, int framesInFlight)
    {
        ArgumentNullException.ThrowIfNull(device);

        _pass = new ComputePass(device, ShaderModuleId.UpscaleDilateVelocity, "upscale.dilate-velocity");

        _parameters = new ComputeParameters(
            device,
            TemporalParameters.ReprojectionFloats * sizeof(float),
            framesInFlight,
            "upscale.dilate-velocity");
    }

    /// <summary>Records the dilation for one frame.</summary>
    /// <param name="pass">The compute pass to record into.</param>
    /// <param name="targets">The frame's targets.</param>
    /// <param name="state">The frame and the one before it.</param>
    /// <param name="frameInFlight">Which parameter buffer this frame writes.</param>
    public void Record(
        IRhiComputePassEncoder pass,
        SceneTargetSet targets,
        in TemporalFrameState state,
        int frameInFlight)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(targets);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Span<float> block = stackalloc float[TemporalParameters.ReprojectionFloats];
        TemporalParameters.WriteReprojection(block, state);

        _pass.Bind("uDepth", targets.View(SceneTargetId.Depth));
        _pass.Bind("uVelocity", targets.View(SceneTargetId.Velocity));
        _pass.Bind("uDilatedVelocity", targets.View(SceneTargetId.DilatedVelocity));
        _pass.Bind("uClosestDepth", targets.View(SceneTargetId.ClosestDepth));
        _pass.Bind("uOccluder", targets.View(SceneTargetId.ClosestOccluder));
        _pass.Bind("p", _parameters.Write<float>(frameInFlight, block));

        _pass.Dispatch(pass, targets.Width, targets.Height);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _parameters.Dispose();
        _pass.Dispose();
    }
}
