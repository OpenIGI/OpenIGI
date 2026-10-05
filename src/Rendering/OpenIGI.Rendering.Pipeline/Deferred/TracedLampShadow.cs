using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Temporal;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>What one frame's lamp trace is traced against, and from where.</summary>
/// <param name="World">The traced world, or null to march the distance field instead.</param>
/// <param name="CameraPosition">Where the camera is, in absolute world space.</param>
/// <param name="Lamps">The shading's lamp block (<see cref="DeferredLampBlock"/>), byte for byte.</param>
/// <param name="FrameIndex">Which frame this is, so the reservoir, the ray and the filter's disk move.</param>
/// <param name="Filter">Whether the denoiser filters; off is for measuring the raw trace against it.</param>
/// <param name="CutoutMasks">The cut-out masks a candidate hit tests its alpha against.</param>
/// <param name="RecordAddress">Where the hit records live, which name each hit's mask.</param>
/// <param name="FieldClipmaps">The distance field's clipmaps, when <paramref name="World"/> is null.</param>
/// <param name="Field">The field's block (<see cref="TracedLampShadow.FieldFloats"/> floats), likewise.</param>
/// <param name="SunVisibility">The sun's traced visibility this frame, or <see langword="null"/> where it was not traced.</param>
/// <param name="SunDirection">The world direction towards the sun.</param>
/// <param name="LampMaps">The nearest lamps' shadow maps, one per layer, or <see langword="null"/> for none yet.</param>
/// <param name="LampMapSampler">The comparison sampler those maps are read through.</param>
/// <param name="LightingChanged">Whether a lamp's output changed a moment ago, which holds the history short.</param>
/// <param name="SunLuminance">The sun's luminance as the lighting pass applies it; nought with no traced sun.</param>
/// <param name="MinimumVisible">
/// The summed lamp light at a pixel below which its shadow could not show at this exposure; such a pixel is
/// left lit without a ray. Nought traces every pixel any lamp reaches.
/// </param>
public readonly record struct TracedLampFrame(
    IRhiAccelerationStructure? World,
    Vector3f CameraPosition,
    ReadOnlyMemory<float> Lamps,
    int FrameIndex,
    bool Filter,
    IRhiTextureView? CutoutMasks,
    ulong RecordAddress,
    IRhiTextureView? FieldClipmaps = null,
    ReadOnlyMemory<float> Field = default,
    float MinimumVisible = 0f,
    IRhiTextureView? SunVisibility = null,
    Vector3f SunDirection = default,
    float SunLuminance = 0f,
    IRhiTextureView? LampMaps = null,
    IRhiSampler? LampMapSampler = null,
    bool LightingChanged = false);

/// <summary>
/// The lamps' shadows asked of the geometry: one ray per half-resolution pixel at the lamp its reservoir
/// picks, accumulated over frames and filtered, for the shading to read.
/// </summary>
/// <remarks>
/// <para>
/// Three dispatches and their parameter blocks (<see cref="LampShadowParameters"/>): LampShadow traces,
/// LampDenoiseTemporal accumulates onto a reprojected, clamped history, LampDenoiseSpatial filters what is
/// left. The shading upsamples the filtered result by depth.
/// </para>
/// <para>
/// <b>Four images</b>, all at half resolution: the raw trace, two accumulations that ping-pong, and the
/// filtered result. All are cleared to an unshadowed room when made (visibility one, nothing else), so a
/// first frame reads light rather than a frame of black.
/// </para>
/// </remarks>
public sealed class TracedLampShadow : IDisposable
{
    private static readonly RhiClearColor Unshadowed = new(1f, 0f, 0f, 0f);

    /// <summary>Floats in the software trace's field block: four corners with their voxel sizes, four ranges, and four integers.</summary>
    public const int FieldFloats = 24 + (240 * 16) + (240 * 2) + DeferredLocalLight.Maximum;

    private readonly IRhiDevice _device;
    private readonly RetiredResources _retired;
    private readonly int _framesInFlight;

    // Made on first use, each by the path that needs it: the traced module exists only as SPIR-V, so on
    // WebGPU making it would fail, and a Vulkan device that always traces never needs the other.
    private ComputePass? _trace;
    private ComputePass? _march;
    private ComputeParameters? _fieldParameters;
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
        _framesInFlight = framesInFlight;
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
    public static int HalfOf(int width) => LampShadowParameters.TracedSize(width);

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
    /// <param name="velocity">The frame's motion field, or <see langword="null"/> when none was made before the shading.</param>
    /// <param name="nextPass">
    /// When given, each step -- the trace, the temporal and the spatial filter -- asks it for a pass of its
    /// own under the step's name, so a profile can tell them apart; <paramref name="pass"/> is then unused.
    /// </param>
    public void Record(
        IRhiComputePassEncoder? pass,
        int frameInFlight,
        IRhiTextureView depth,
        IRhiTextureView normal,
        in TracedLampFrame frame,
        in TemporalFrameState state,
        IRhiTextureView? velocity = null,
        Func<string, IRhiComputePassEncoder>? nextPass = null)
    {
        if (pass is null && nextPass is null)
        {
            throw new ArgumentNullException(nameof(pass));
        }

        ArgumentNullException.ThrowIfNull(depth);
        ArgumentNullException.ThrowIfNull(normal);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_raw is not { } raw || _filtered is not { } filtered || _accumulated[0] is null || _accumulated[1] is null)
        {
            return;
        }

        IRhiTexture current = _accumulated[_current]!;

        // The previous frame's accumulation -- always the OTHER image, even with no history to read (the
        // temporal block says so, and the pass ignores it): WebGPU refuses one image as both the sampled
        // history and the storage output of a single dispatch, which is what reading "this frame's own,
        // cleared" did on the first frame.
        IRhiTexture history = _accumulated[_current ^ 1]!;

        Span<float> trace = stackalloc float[LampShadowParameters.TraceFloats];
        LampShadowParameters.WriteTrace(
            trace,
            _width,
            _height,
            state.ViewToWorld,
            frame.CameraPosition,
            state.Unprojection,
            frame.FrameIndex,
            frame.Lamps.Span,
            frame.RecordAddress,
            frame.MinimumVisible,
            frame.SunDirection,
            frame.SunVisibility is null ? 0f : frame.SunLuminance);

        if (frame.World is { } world)
        {
            _trace ??= new ComputePass(_device, ShaderModuleId.LampShadow, "lampshadow.trace");
            _trace.Bind("uDepth", depth, _sampler);
            _trace.Bind("uMasks", frame.CutoutMasks!, _sampler);
            _trace.Bind("uNormal", normal, _sampler);
            _trace.Bind("uScene", world);
            _trace.Bind("uVisibility", raw.View);

            // The normals stand in where no sun was traced: the slot is declared either way, and the
            // luminance of nought keeps the shader from reading it.
            _trace.Bind("uSunVisibility", frame.SunVisibility ?? normal);
            _trace.Bind("p", _traceParameters.Write<float>(frameInFlight, trace));
            _trace.Dispatch(nextPass?.Invoke("lamp.shadow.trace") ?? pass!, _width, _height);
        }
        else if (frame.FieldClipmaps is { } clipmaps)
        {
            // The same pass with the ray answered by the distance field: see Lamp/Shadow.slang.
            _march ??= new ComputePass(
                _device, ShaderModuleId.LampShadow, "lampshadow.march", new ShaderVariant("software"));
            _fieldParameters ??= new ComputeParameters(
                _device, FieldFloats * sizeof(float), _framesInFlight, "lampshadow.field");
            _march.Bind("uDepth", depth, _sampler);
            _march.Bind("uNormal", normal, _sampler);
            _march.Bind("uClipmaps", clipmaps, _sampler);
            _march.Bind("uVisibility", raw.View);
            _march.Bind("uSunVisibility", frame.SunVisibility ?? normal);

            // The nearest lamps' own maps. Made whenever the march runs, so the slots are always bound; a
            // lamp is handed out as having none until a map has been drawn for it.
            if (frame.LampMaps is { } maps && frame.LampMapSampler is { } comparison)
            {
                _march.Bind("uLampMaps", maps);
                _march.Bind("uLampMapSampler", comparison);
            }
            _march.Bind("p", _traceParameters.Write<float>(frameInFlight, trace));
            _march.Bind("f", _fieldParameters.Write<float>(frameInFlight, frame.Field.Span));
            _march.Dispatch(nextPass?.Invoke("lamp.shadow.march") ?? pass!, _width, _height);
        }
        else
        {
            return;
        }

        // Half a pixel of full-resolution jitter is a quarter of one here, and the buffers are half the
        // frame, so the offsets halve with them or the depth looked up belongs to the wrong sample.
        TemporalFrameState half = state with
        {
            Width = _width,
            Height = _height,
            JitterX = state.JitterX * LampShadowParameters.JitterScale,
            JitterY = state.JitterY * LampShadowParameters.JitterScale,
            PreviousJitterX = state.PreviousJitterX * LampShadowParameters.JitterScale,
            PreviousJitterY = state.PreviousJitterY * LampShadowParameters.JitterScale,
        };

        // Both halves run with the filter off -- the accumulation carries the frame count and the filter the
        // depth the shading's upsample reads -- and only the filtering itself stops.
        Span<float> temporal = stackalloc float[LampShadowParameters.TemporalFloats];
        LampShadowParameters.WriteTemporal(
            temporal, half, _hasHistory, frame.Filter, velocityValid: velocity is not null, lightingChanged: frame.LightingChanged);

        _temporal.Bind("uRaw", raw.View, _sampler);

        // Where a moving object's surface came from; without a field the flag is off and the normals, never
        // read, fill the slot the module declares either way.
        _temporal.Bind("uVelocity", velocity ?? normal, _sampler);
        _temporal.Bind("uDepth", depth, _sampler);
        _temporal.Bind("uHistory", history.View, _sampler);
        _temporal.Bind("uOutput", current.View);
        _temporal.Bind("p", _temporalParameters.Write<float>(frameInFlight, temporal));
        _temporal.Dispatch(nextPass?.Invoke("lamp.shadow.temporal") ?? pass!, _width, _height);

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
        _spatial.Dispatch(nextPass?.Invoke("lamp.shadow.spatial") ?? pass!, _width, _height);

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
        _fieldParameters?.Dispose();
        _spatial.Dispose();
        _temporal.Dispose();
        _trace?.Dispose();
        _march?.Dispose();
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
