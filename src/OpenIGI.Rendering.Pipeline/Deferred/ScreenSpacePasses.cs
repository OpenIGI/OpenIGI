using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Temporal;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>
/// The two screen-space fields the deferred frame computes before it lights anything: where every
/// pixel moved to, and how much of the sky it can see.
/// </summary>
/// <remarks>
/// <para>
/// Both read the depth the geometry pass left and write a field the lighting and the temporal resolve
/// go on to read, and both reproject against the frame before this one — so they take the same state
/// and are built together.
/// </para>
/// <para>
/// The motion field is computed from depth rather than rasterised, because most of what moves on screen
/// moves because the camera did: a static world under a turning camera has a motion vector at every
/// pixel and not one triangle that could have written it. What the camera cannot explain — a soldier, a
/// door — is rasterised over the top afterwards.
/// </para>
/// </remarks>
public sealed class ScreenSpacePasses : IDisposable
{
    private readonly ComputePass _velocity;
    private readonly ComputePass _occlusion;
    private readonly ComputeParameters _velocityParameters;
    private readonly ComputeParameters _occlusionParameters;
    private readonly IRhiSampler _sampler;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ScreenSpacePasses"/> class.</summary>
    /// <param name="device">The device the passes run on.</param>
    /// <param name="framesInFlight">How many parameter buffers to keep of each.</param>
    public ScreenSpacePasses(IRhiDevice device, int framesInFlight)
    {
        ArgumentNullException.ThrowIfNull(device);

        _velocity = new ComputePass(device, ShaderModuleId.Velocity, "velocity");
        _occlusion = new ComputePass(device, ShaderModuleId.Ssao, "occlusion");

        _velocityParameters = new ComputeParameters(
            device,
            TemporalParameters.ReprojectionFloats * sizeof(float),
            framesInFlight,
            "velocity");

        _occlusionParameters = new ComputeParameters(
            device,
            AmbientOcclusionParameters.Floats * sizeof(float),
            framesInFlight,
            "occlusion");

        _sampler = device.CreateSampler(new RhiSamplerDescriptor(Label: "screen"));
    }

    /// <summary>Records the motion field.</summary>
    /// <param name="pass">The compute pass to record into.</param>
    /// <param name="targets">The frame's targets.</param>
    /// <param name="state">The frame and the one before it.</param>
    /// <param name="frameInFlight">Which parameter buffer this frame writes.</param>
    public void RecordVelocity(
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

        _velocity.Bind("uDepth", targets.View(SceneTargetId.Depth), _sampler);
        _velocity.Bind("uVelocity", targets.View(SceneTargetId.Velocity));
        _velocity.Bind("p", _velocityParameters.Write<float>(frameInFlight, block));

        _velocity.Dispatch(pass, targets.Width, targets.Height);
    }

    /// <summary>Records the ambient occlusion.</summary>
    /// <param name="pass">The compute pass to record into.</param>
    /// <param name="targets">The frame's targets.</param>
    /// <param name="state">The frame and the one before it.</param>
    /// <param name="settings">How far the gather reaches and how much it darkens.</param>
    /// <param name="frameIndex">Which frame this is, which advances the noise.</param>
    /// <param name="frameInFlight">Which parameter buffer this frame writes.</param>
    /// <param name="hasHistory">
    /// Whether the previous frame's occlusion is worth reading. False on the first frame after a load or
    /// a resize, where what is in that texture belongs to a different scene.
    /// </param>
    public void RecordOcclusion(
        IRhiComputePassEncoder pass,
        SceneTargetSet targets,
        in TemporalFrameState state,
        in AmbientOcclusionSettings settings,
        long frameIndex,
        int frameInFlight,
        bool hasHistory = true)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(targets);
        ObjectDisposedException.ThrowIf(_disposed, this);

        IRhiTexture occlusion = targets.Current(SceneTargetId.Occlusion);

        Span<float> block = stackalloc float[AmbientOcclusionParameters.Floats];

        AmbientOcclusionParameters.Write(
            block,
            state,
            settings,
            occlusion.Width,
            occlusion.Height,
            frameIndex,
            hasHistory);

        _occlusion.Bind("uDepth", targets.View(SceneTargetId.Depth), _sampler);
        _occlusion.Bind("uOcclusion", occlusion.View);
        _occlusion.Bind("uPreviousOcclusion", targets.PreviousView(SceneTargetId.Occlusion), _sampler);
        _occlusion.Bind("uPreviousDepth", targets.PreviousView(SceneTargetId.PreviousDepth), _sampler);
        _occlusion.Bind("p", _occlusionParameters.Write<float>(frameInFlight, block));

        // Over the occlusion target, which is half the frame in each axis.
        _occlusion.Dispatch(pass, occlusion.Width, occlusion.Height);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _sampler.Dispose();
        _occlusionParameters.Dispose();
        _velocityParameters.Dispose();
        _occlusion.Dispose();
        _velocity.Dispose();
    }
}

/// <summary>The block the ambient occlusion reads, laid out as the module declares it.</summary>
public static class AmbientOcclusionParameters
{
    /// <summary>How many floats the block holds.</summary>
    public const int Floats = 56;

    /// <summary>
    /// How long the noise pattern runs before it repeats.
    /// </summary>
    /// <remarks>
    /// Long enough that the blend window never sees the same pattern twice, and small enough that the
    /// hash's sine stays in a range it is well conditioned over. Both matter: a short cycle shows as a
    /// standing pattern in the shadow, and an unbounded one eventually loses all its precision.
    /// </remarks>
    public const int NoiseCycle = 1021;

    /// <summary>How far two depths may differ, relative to the nearer, and still be one surface.</summary>
    public const float RelativeDepthTolerance = 0.02f;

    /// <summary>How sharply the occlusion falls off with distance along the gather.</summary>
    public const float FalloffPower = 1.5f;

    /// <summary>Fills the block.</summary>
    /// <param name="block">Room for <see cref="Floats"/> floats.</param>
    /// <param name="state">The frame and the one before it.</param>
    /// <param name="settings">How far the gather reaches and how much it darkens.</param>
    /// <param name="occlusionWidth">The occlusion target's width, which is half the frame's.</param>
    /// <param name="occlusionHeight">Its height.</param>
    /// <param name="frameIndex">Which frame this is.</param>
    /// <param name="hasHistory">Whether the previous frame's occlusion is worth reading.</param>
    /// <exception cref="ArgumentException">The block is the wrong length.</exception>
    public static void Write(
        Span<float> block,
        in TemporalFrameState state,
        in AmbientOcclusionSettings settings,
        int occlusionWidth,
        int occlusionHeight,
        long frameIndex,
        bool hasHistory)
    {
        if (block.Length != Floats)
        {
            throw new ArgumentException($"The occlusion block is exactly {Floats} floats.", nameof(block));
        }

        block.Clear();

        TemporalParameters.WriteRotation(block, 0, state.ViewToWorld);
        TemporalParameters.WriteRotation(block, 16, state.PreviousViewToWorld.Transposed());

        // The gather's own size first, then the frame's: it walks the one and reads depth from the other.
        TemporalParameters.WriteSize(block[32..], occlusionWidth, occlusionHeight);
        TemporalParameters.WriteSize(block[36..], state.Width, state.Height);

        block[40] = state.Unprojection.XScale;
        block[41] = state.Unprojection.YScale;
        block[42] = state.Unprojection.Near;
        block[43] = state.Unprojection.Far;

        block[44] = settings.Radius;
        block[45] = settings.Intensity;
        block[46] = RelativeDepthTolerance;
        block[47] = FalloffPower;

        block[48] = state.CameraDelta.X;
        block[49] = state.CameraDelta.Y;
        block[50] = state.CameraDelta.Z;
        block[51] = frameIndex % NoiseCycle;

        block[52] = hasHistory ? 1f : 0f;
    }
}
