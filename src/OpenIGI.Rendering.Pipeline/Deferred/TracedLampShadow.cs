using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Temporal;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>What one frame's lamp trace is traced against, and from where.</summary>
/// <param name="World">The traced world.</param>
/// <param name="CameraPosition">Where the camera is, in absolute world space.</param>
/// <param name="Lamps">The shading's lamp block (<see cref="DeferredLampBlock"/>), byte for byte.</param>
/// <param name="FrameIndex">Which frame this is, so the reservoir, the ray and the filter's disk move.</param>
/// <param name="Filter">Whether the denoiser filters; off is for measuring the raw trace against it.</param>
public readonly record struct TracedLampFrame(
    IRhiAccelerationStructure World,
    Vector3f CameraPosition,
    ReadOnlyMemory<float> Lamps,
    int FrameIndex,
    bool Filter);

/// <summary>
/// The lamps' shadows asked of the geometry: one ray per half-resolution pixel at the lamp its reservoir
/// picks, accumulated over frames and filtered, for the shading to read.
/// </summary>
/// <remarks>
/// <para>
/// The same three dispatches and parameter blocks as the Vulkan renderer's passes
/// (<see cref="LampShadowParameters"/>): LampShadow traces, LampDenoiseTemporal accumulates onto a
/// reprojected, clamped history, LampDenoiseSpatial filters what is left. The shading upsamples the
/// filtered result by depth.
/// </para>
/// <para>
/// <b>Four images</b>, all at half resolution: the raw trace, two accumulations that ping-pong, and the
/// filtered result. All are cleared to an unshadowed room when made (visibility one, nothing else), as the
/// Vulkan renderer's are, so a first frame reads light rather than a frame of black.
/// </para>
/// </remarks>
public sealed class TracedLampShadow : IDisposable
{
    private static readonly RhiClearColor Unshadowed = new(1f, 0f, 0f, 0f);

    private readonly IRhiDevice _device;
    private readonly RetiredResources _retired;
    private readonly ComputePass _trace;
    private readonly ComputePass _temporal;
    private readonly ComputePass _spatial;
    private readonly ComputeParameters _traceParameters;
    private readonly ComputeParameters _temporalParameters;
    private readonly ComputeParameters _spatialParameters;
    private readonly IRhiSampler _sampler;

    private readonly IRhiTexture?[] _accumulated = new IRhiTexture?[2];
    private IRhiTexture? _raw;
    private IRhiTexture? _filtered;
    private int _width;
    private int _height;
    private int _current;
    private bool _hasHistory;
    private bool _clearPending;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="TracedLampShadow"/> class.</summary>
    /// <param name="device">The device the images live on, which must trace rays.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    /// <param name="retired">Where images a resize replaces go until the frames reading them finish.</param>
    public TracedLampShadow(IRhiDevice device, int framesInFlight, RetiredResources retired)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(retired);

        _device = device;
        _retired = retired;
        _trace = new ComputePass(device, ShaderModuleId.LampShadow, "lampshadow.trace");
        _temporal = new ComputePass(device, ShaderModuleId.LampDenoiseTemporal, "lampshadow.temporal");
        _spatial = new ComputePass(device, ShaderModuleId.LampDenoiseSpatial, "lampshadow.spatial");
        _traceParameters = new ComputeParameters(
            device, LampShadowParameters.TraceFloats * sizeof(float), framesInFlight, "lampshadow.trace");
        _temporalParameters = new ComputeParameters(
            device, LampShadowParameters.TemporalFloats * sizeof(float), framesInFlight, "lampshadow.temporal");
        _spatialParameters = new ComputeParameters(
            device, LampShadowParameters.SpatialFloats * sizeof(float), framesInFlight, "lampshadow.spatial");
        _sampler = device.CreateSampler(new RhiSamplerDescriptor(Label: "lampshadow"));
    }

    /// <summary>Gets the filtered visibility the shading reads, or null before one is made.</summary>
    public IRhiTextureView? Filtered => _filtered?.View;

    /// <summary>Gets the half-resolution width the lamps are traced at, for a frame of a given width.</summary>
    /// <param name="width">The render width.</param>
    /// <returns>Half of it, at least one.</returns>
    public static int HalfOf(int width) => Math.Max(1, width / 2);

    /// <summary>Forgets the accumulated history, for a camera that has moved somewhere unrelated.</summary>
    public void Invalidate() => _hasHistory = false;

    /// <summary>Sizes the images for a render resolution, rebuilding them if it moved.</summary>
    /// <param name="renderWidth">The render width in pixels; the images are half of it.</param>
    /// <param name="renderHeight">The render height in pixels.</param>
    public void Ensure(int renderWidth, int renderHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int width = HalfOf(renderWidth);
        int height = HalfOf(renderHeight);

        if (_filtered is not null && width == _width && height == _height)
        {
            return;
        }

        Release();
        _width = width;
        _height = height;

        const RhiTextureUsage usage = RhiTextureUsage.Storage | RhiTextureUsage.Sampled | RhiTextureUsage.RenderTarget;

        _raw = Create(RhiTextureFormat.Rg16Float, usage, "lampshadow.raw");
        _filtered = Create(RhiTextureFormat.Rgba16Float, usage, "lampshadow.filtered");

        for (int i = 0; i < _accumulated.Length; i++)
        {
            _accumulated[i] = Create(RhiTextureFormat.Rgba16Float, usage, $"lampshadow.accumulated.{i}");
        }

        _clearPending = true;
    }

    /// <summary>Clears images made since the last frame to an unshadowed room.</summary>
    /// <param name="encoder">The frame's encoder, outside any pass.</param>
    public void ClearNew(IRhiCommandEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(encoder);

        if (!_clearPending)
        {
            return;
        }

        foreach (IRhiTexture? image in (IRhiTexture?[])[_raw, _filtered, .. _accumulated])
        {
            if (image is null)
            {
                continue;
            }

            using IRhiRenderPassEncoder pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor(
                [new RhiColorAttachment(image.View, RhiLoadOp.Clear, RhiStoreOp.Store, Unshadowed)],
                Label: "lampshadow.clear"));
            pass.EndPass();
        }

        _clearPending = false;
    }

    /// <summary>Traces, accumulates and filters one frame's lamp visibility.</summary>
    /// <param name="pass">The compute pass to record into.</param>
    /// <param name="frameInFlight">Which set of per-frame resources this frame may write.</param>
    /// <param name="depth">The scene depth, at full resolution.</param>
    /// <param name="normal">The G-buffer normal, at full resolution.</param>
    /// <param name="frame">What to trace against, and the lamps.</param>
    /// <param name="state">This frame and the last at FULL resolution; halved here, jitter included.</param>
    public void Record(
        IRhiComputePassEncoder pass,
        int frameInFlight,
        IRhiTextureView depth,
        IRhiTextureView normal,
        in TracedLampFrame frame,
        in TemporalFrameState state)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(depth);
        ArgumentNullException.ThrowIfNull(normal);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_raw is not { } raw || _filtered is not { } filtered || _accumulated[0] is null || _accumulated[1] is null)
        {
            return;
        }

        IRhiTexture current = _accumulated[_current]!;

        // The previous frame's accumulation, or this frame's own, cleared, where there is none.
        IRhiTexture history = _hasHistory ? _accumulated[_current ^ 1]! : current;

        Span<float> trace = stackalloc float[LampShadowParameters.TraceFloats];
        LampShadowParameters.WriteTrace(
            trace,
            _width,
            _height,
            state.ViewToWorld,
            frame.CameraPosition,
            state.Unprojection,
            frame.FrameIndex,
            frame.Lamps.Span);

        _trace.Bind("uDepth", depth, _sampler);
        _trace.Bind("uNormal", normal, _sampler);
        _trace.Bind("uScene", frame.World);
        _trace.Bind("uVisibility", raw.View);
        _trace.Bind("p", _traceParameters.Write<float>(frameInFlight, trace));
        _trace.Dispatch(pass, _width, _height);

        // Half a pixel of full-resolution jitter is a quarter of one here, and the buffers are half the
        // frame, so the offsets halve with them or the depth looked up belongs to the wrong sample.
        TemporalFrameState half = state with
        {
            Width = _width,
            Height = _height,
            JitterX = state.JitterX * 0.5f,
            JitterY = state.JitterY * 0.5f,
            PreviousJitterX = state.PreviousJitterX * 0.5f,
            PreviousJitterY = state.PreviousJitterY * 0.5f,
        };

        // Both halves run with the filter off -- the accumulation carries the frame count and the filter the
        // depth the shading's upsample reads -- and only the filtering itself stops.
        Span<float> temporal = stackalloc float[LampShadowParameters.TemporalFloats];
        LampShadowParameters.WriteTemporal(temporal, half, _hasHistory, frame.Filter);

        _temporal.Bind("uRaw", raw.View, _sampler);
        _temporal.Bind("uDepth", depth, _sampler);
        _temporal.Bind("uHistory", history.View, _sampler);
        _temporal.Bind("uOutput", current.View);
        _temporal.Bind("p", _temporalParameters.Write<float>(frameInFlight, temporal));
        _temporal.Dispatch(pass, _width, _height);

        Span<float> spatial = stackalloc float[LampShadowParameters.SpatialFloats];
        LampShadowParameters.WriteSpatial(
            spatial,
            _width,
            _height,
            state.ViewToWorld,
            state.Unprojection,
            half.JitterX,
            half.JitterY,
            frame.FrameIndex,
            frame.Filter);

        _spatial.Bind("uAccumulated", current.View, _sampler);
        _spatial.Bind("uDepth", depth, _sampler);
        _spatial.Bind("uNormal", normal, _sampler);
        _spatial.Bind("uOutput", filtered.View);
        _spatial.Bind("p", _spatialParameters.Write<float>(frameInFlight, spatial));
        _spatial.Dispatch(pass, _width, _height);

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
        _filtered?.Dispose();

        foreach (IRhiTexture? image in _accumulated)
        {
            image?.Dispose();
        }

        _sampler.Dispose();
        _spatialParameters.Dispose();
        _temporalParameters.Dispose();
        _traceParameters.Dispose();
        _spatial.Dispose();
        _temporal.Dispose();
        _trace.Dispose();
    }

    private IRhiTexture Create(RhiTextureFormat format, RhiTextureUsage usage, string label) =>
        _device.CreateTexture(new RhiTextureDescriptor(_width, _height, format, usage, Label: label));

    private void Release()
    {
        foreach (IRhiTexture? owned in (IRhiTexture?[])[_raw, _filtered, .. _accumulated])
        {
            if (owned is not null)
            {
                _retired.Retire(owned);
            }
        }

        _raw = null;
        _filtered = null;
        Array.Clear(_accumulated);
        _hasHistory = false;
    }
}
