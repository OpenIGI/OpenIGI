using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Temporal;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>What one frame's traced sun shadow is traced against, and from where.</summary>
/// <param name="World">The traced world.</param>
/// <param name="CutoutMasks">The cut-out masks a candidate hit tests its alpha against.</param>
/// <param name="RecordAddress">Where the hit records live, which name each hit's mask.</param>
/// <param name="CameraPosition">Where the camera is, in absolute world space.</param>
/// <param name="SunDirection">The world direction towards the sun.</param>
/// <param name="SunAngleDegrees">How wide the sun's disc is.</param>
/// <param name="FrameIndex">A number that changes every frame, so the one ray lands somewhere new.</param>
/// <param name="AccumulationFrames">How many frames the temporal half averages; one or less skips it.</param>
public readonly record struct TracedSunShadowFrame(
    IRhiAccelerationStructure World,
    IRhiTextureView CutoutMasks,
    ulong RecordAddress,
    Vector3f CameraPosition,
    Vector3f SunDirection,
    float SunAngleDegrees,
    float FrameIndex,
    float AccumulationFrames);

/// <summary>
/// The sun's shadow asked of the geometry: one ray a pixel, spread over its penumbra, averaged over frames.
/// </summary>
/// <remarks>
/// <para>
/// Three dispatches and their parameter blocks (<see cref="SunShadowParameters"/>). SunShadow traces a ray
/// from each pixel towards a point on the sun's disc and stores whether it got there and how far the occluder
/// was; ShadowDenoise spreads each answer over the penumbra that distance implies; ShadowAccumulate
/// reprojects the previous frames' mean and averages this one into it. The shading reads the result in place
/// of the cascades.
/// </para>
/// <para>
/// <b>Four images.</b> The raw trace, the mask the shading reads, and two accumulations that ping-pong:
/// the one written this frame is read as history the next. The accumulations are cleared when made, so
/// a first frame reads zero frames of history -- the denoise then reconstructs at full width.
/// </para>
/// </remarks>
public sealed class TracedSunShadow : IDisposable
{
    private readonly IRhiDevice _device;
    private readonly RetiredResources _retired;
    private readonly ComputePass _trace;
    private readonly ComputePass _denoise;
    private readonly ComputePass _accumulate;
    private readonly ComputeParameters _traceParameters;
    private readonly ComputeParameters _denoiseParameters;
    private readonly ComputeParameters _accumulateParameters;
    private readonly IRhiSampler _sampler;

    private readonly IRhiTexture?[] _histories = new IRhiTexture?[2];
    private IRhiTexture? _raw;
    private IRhiTexture? _mask;
    private int _width;
    private int _height;
    private int _current;
    private bool _hasHistory;
    private bool _clearPending;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="TracedSunShadow"/> class.</summary>
    /// <param name="device">The device the images live on, which must trace rays.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    /// <param name="retired">Where images a resize replaces go until the frames reading them finish.</param>
    public TracedSunShadow(IRhiDevice device, int framesInFlight, RetiredResources retired)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(retired);

        _device = device;
        _retired = retired;
        _trace = new ComputePass(device, ShaderModuleId.SunShadow, "sunshadow.trace");
        _denoise = new ComputePass(device, ShaderModuleId.ShadowDenoise, "sunshadow.denoise");
        _accumulate = new ComputePass(device, ShaderModuleId.ShadowAccumulate, "sunshadow.accumulate");
        _traceParameters = new ComputeParameters(
            device, SunShadowParameters.TraceFloats * sizeof(float), framesInFlight, "sunshadow.trace");
        _denoiseParameters = new ComputeParameters(
            device, SunShadowParameters.DenoiseFloats * sizeof(float), framesInFlight, "sunshadow.denoise");
        _accumulateParameters = new ComputeParameters(
            device, SunShadowParameters.AccumulateFloats * sizeof(float), framesInFlight, "sunshadow.accumulate");
        _sampler = device.CreateSampler(new RhiSamplerDescriptor(Label: "sunshadow"));
    }

    /// <summary>Gets the mask the shading reads, or null before one is made.</summary>
    public IRhiTextureView? Mask => _mask?.View;

    /// <summary>Forgets the accumulated history, for a camera that has moved somewhere unrelated.</summary>
    public void Invalidate() => _hasHistory = false;

    /// <summary>Sizes the images for a render resolution, rebuilding them if it moved.</summary>
    /// <param name="width">The render width in pixels.</param>
    /// <param name="height">The render height in pixels.</param>
    /// <remarks>
    /// Images a change replaces are retired rather than freed: the frames still in flight name them.
    /// </remarks>
    public void Ensure(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_mask is not null && width == _width && height == _height)
        {
            return;
        }

        Release();
        _width = width;
        _height = height;

        _raw = Create(RhiTextureFormat.Rg16Float, RhiTextureUsage.Storage | RhiTextureUsage.Sampled, "sunshadow.raw");
        _mask = Create(RhiTextureFormat.R8Unorm, RhiTextureUsage.Storage | RhiTextureUsage.Sampled, "sunshadow.mask");

        for (int i = 0; i < _histories.Length; i++)
        {
            _histories[i] = Create(
                RhiTextureFormat.Rgba16Float,
                RhiTextureUsage.Storage | RhiTextureUsage.Sampled | RhiTextureUsage.RenderTarget,
                $"sunshadow.history.{i}");
        }

        _clearPending = true;
    }

    /// <summary>Clears accumulations made since the last frame, so the first read of one finds no history.</summary>
    /// <param name="encoder">The frame's encoder, outside any pass.</param>
    public void ClearNew(IRhiCommandEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(encoder);

        if (!_clearPending)
        {
            return;
        }

        foreach (IRhiTexture? history in _histories)
        {
            if (history is null)
            {
                continue;
            }

            using IRhiRenderPassEncoder pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor(
                [new RhiColorAttachment(history.View, RhiLoadOp.Clear, RhiStoreOp.Store, RhiClearColor.Transparent)],
                Label: "sunshadow.clear"));
            pass.EndPass();
        }

        _clearPending = false;
    }

    /// <summary>Traces, denoises and accumulates one frame's sun shadow.</summary>
    /// <param name="pass">The compute pass to record into.</param>
    /// <param name="frameInFlight">Which set of per-frame resources this frame may write.</param>
    /// <param name="depth">The scene depth.</param>
    /// <param name="normal">The G-buffer normal, to offset each ray off its surface.</param>
    /// <param name="frame">What to trace against, and from where.</param>
    /// <param name="state">This frame and the last, at the render size.</param>
    /// <param name="velocity">The frame's motion field, or <see langword="null"/> when none was made before the shading.</param>
    /// <remarks>
    /// The three dispatches go in one compute pass: the Vulkan backend places the barrier between them,
    /// and nothing but the Vulkan backend traces.
    /// </remarks>
    public void Record(
        IRhiComputePassEncoder pass,
        int frameInFlight,
        IRhiTextureView depth,
        IRhiTextureView normal,
        in TracedSunShadowFrame frame,
        in TemporalFrameState state,
        IRhiTextureView? velocity = null)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(depth);
        ArgumentNullException.ThrowIfNull(normal);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_raw is not { } raw || _mask is not { } mask || _histories[0] is null || _histories[1] is null)
        {
            return;
        }

        IRhiTexture current = _histories[_current]!;

        // The previous frame's accumulation, or, with none, this frame's own: freshly cleared, it holds no
        // frames, and the denoise reconstructs the penumbra at full width.
        IRhiTexture previous = _hasHistory ? _histories[_current ^ 1]! : current;

        Span<float> trace = stackalloc float[SunShadowParameters.TraceFloats];
        SunShadowParameters.WriteTrace(
            trace,
            _width,
            _height,
            state.ViewToWorld,
            frame.CameraPosition,
            frame.SunDirection,
            state.Unprojection,
            frame.SunAngleDegrees,
            frame.FrameIndex,
            frame.RecordAddress,
            state.JitterX,
            state.JitterY);

        _trace.Bind("uDepth", depth, _sampler);
        _trace.Bind("uNormal", normal, _sampler);
        _trace.Bind("uScene", frame.World);
        _trace.Bind("uShadow", raw.View);
        _trace.Bind("uMasks", frame.CutoutMasks, _sampler);
        _trace.Bind("p", _traceParameters.Write<float>(frameInFlight, trace));
        _trace.Dispatch(pass, _width, _height);

        Span<float> denoise = stackalloc float[SunShadowParameters.DenoiseFloats];
        SunShadowParameters.WriteDenoise(
            denoise, _width, _height, state.Unprojection, frame.SunAngleDegrees, frame.AccumulationFrames);

        _denoise.Bind("uRaw", raw.View, _sampler);
        _denoise.Bind("uDepth", depth, _sampler);
        _denoise.Bind("uHistory", previous.View, _sampler);
        _denoise.Bind("uShadow", mask.View);
        _denoise.Bind("p", _denoiseParameters.Write<float>(frameInFlight, denoise));
        _denoise.Dispatch(pass, _width, _height);

        // The temporal half, averaging the mask before anything turns it into light: a mask that differs
        // every frame reaches the temporal resolve downstream as a colour that changed, which it rejects
        // rather than averages.
        if (frame.AccumulationFrames <= 1f)
        {
            _hasHistory = false;
            return;
        }

        Span<float> accumulate = stackalloc float[SunShadowParameters.AccumulateFloats];
        SunShadowParameters.WriteAccumulate(
            accumulate, state, frame.AccumulationFrames, _hasHistory, velocityValid: velocity is not null);

        _accumulate.Bind("uDepth", depth);

        // Where a moving object's surface really came from. Without one the flag is off and the normals stand
        // in, never read, because the module declares the slot either way.
        _accumulate.Bind("uVelocity", velocity ?? normal);
        _accumulate.Bind("uPrevHistory", previous.View, _sampler);
        _accumulate.Bind("uShadow", mask.View);
        _accumulate.Bind("uHistory", current.View);
        _accumulate.Bind("p", _accumulateParameters.Write<float>(frameInFlight, accumulate));
        _accumulate.Dispatch(pass, _width, _height);

        _current ^= 1;
        _hasHistory = true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _raw?.Dispose();
        _mask?.Dispose();

        foreach (IRhiTexture? history in _histories)
        {
            history?.Dispose();
        }

        _sampler.Dispose();
        _accumulateParameters.Dispose();
        _denoiseParameters.Dispose();
        _traceParameters.Dispose();
        _accumulate.Dispose();
        _denoise.Dispose();
        _trace.Dispose();
    }

    private IRhiTexture Create(RhiTextureFormat format, RhiTextureUsage usage, string label) =>
        _device.CreateTexture(new RhiTextureDescriptor(_width, _height, format, usage, Label: label));

    private void Release()
    {
        foreach (IRhiTexture? owned in (IRhiTexture?[])[_raw, _mask, .. _histories])
        {
            if (owned is not null)
            {
                _retired.Retire(owned);
            }
        }

        _raw = null;
        _mask = null;
        Array.Clear(_histories);
        _hasHistory = false;
    }
}
