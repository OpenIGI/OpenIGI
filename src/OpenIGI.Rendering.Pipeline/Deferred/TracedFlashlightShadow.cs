using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>What one frame's flashlight shadow is traced against, and from where.</summary>
/// <param name="World">The traced world, or null to march the depth buffer instead.</param>
/// <param name="CutoutMasks">The cut-out masks a candidate hit tests its alpha against, with a world.</param>
/// <param name="RecordAddress">Where the hit records live, which name each hit's mask.</param>
/// <param name="CameraPosition">Where the camera is, in absolute world space.</param>
/// <param name="ViewToWorld">This frame's view-to-world rotation.</param>
/// <param name="Unprojection">How a depth sample becomes a view position.</param>
/// <param name="Light">The flashlight, in view space.</param>
/// <param name="FrameIndex">A number that changes every frame, so the point on the lens moves.</param>
public readonly record struct FlashlightShadowFrame(
    IRhiAccelerationStructure? World,
    IRhiTextureView? CutoutMasks,
    ulong RecordAddress,
    Vector3f CameraPosition,
    Matrix3f ViewToWorld,
    DepthUnprojection Unprojection,
    FlashlightLight Light,
    long FrameIndex);

/// <summary>
/// The flashlight's shadow: one ray a pixel to a point on the lens, then spread over its penumbra.
/// </summary>
/// <remarks>
/// <para>
/// Two dispatches and their parameter blocks (<see cref="FlashlightShadowParameters"/>). FlashlightShadow
/// traces against the world where the adapter can and marches the depth buffer where it cannot, and stores
/// whether each ray got there and the penumbra its occluder implies; FlashlightShadowFilter spreads that over
/// the penumbra by depth. The shading reads the result at its own pixel.
/// </para>
/// <para>
/// <b>Two images</b> at the render resolution, both written whole by every dispatch that runs, so neither is
/// ever cleared. No history: the light moves with the view.
/// </para>
/// </remarks>
public sealed class TracedFlashlightShadow : IDisposable
{
    private readonly IRhiDevice _device;
    private readonly RetiredResources _retired;

    // Made on first use, each by the path that needs it: the traced module exists only as SPIR-V, so on
    // WebGPU making it would fail, and a Vulkan device that always traces never needs the other.
    private ComputePass? _trace;
    private ComputePass? _march;
    private readonly ComputePass _filter;
    private readonly ComputeParameters _traceParameters;
    private readonly ComputeParameters _filterParameters;

    private IRhiTexture? _raw;
    private IRhiTexture? _mask;
    private int _width;
    private int _height;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="TracedFlashlightShadow"/> class.</summary>
    /// <param name="device">The device the images live on.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    /// <param name="retired">Where images a resize replaces go until the frames reading them finish.</param>
    public TracedFlashlightShadow(IRhiDevice device, int framesInFlight, RetiredResources retired)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(retired);

        _device = device;
        _retired = retired;
        _filter = new ComputePass(device, ShaderModuleId.FlashlightShadowFilter, "flashlightshadow.filter");
        _traceParameters = new ComputeParameters(
            device, FlashlightShadowParameters.TraceFloats * sizeof(float), framesInFlight, "flashlightshadow.trace");
        _filterParameters = new ComputeParameters(
            device, FlashlightShadowParameters.FilterFloats * sizeof(float), framesInFlight, "flashlightshadow.filter");
    }

    /// <summary>Gets the mask the shading reads, or null before one is made.</summary>
    public IRhiTextureView? Mask => _mask?.View;

    /// <summary>Sizes the images for a render resolution, rebuilding them if it moved.</summary>
    /// <param name="width">The render width in pixels.</param>
    /// <param name="height">The render height in pixels.</param>
    /// <remarks>Images a change replaces are retired rather than freed: the frames still in flight name them.</remarks>
    public void Ensure(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_mask is not null && width == _width && height == _height)
        {
            return;
        }

        Release();
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);

        const RhiTextureUsage usage = RhiTextureUsage.Storage | RhiTextureUsage.Sampled;
        _raw = Create(RhiTextureFormat.Rg16Float, usage, "flashlightshadow.raw");
        _mask = Create(RhiTextureFormat.Rg16Float, usage, "flashlightshadow.mask");
    }

    /// <summary>Traces and filters one frame's flashlight shadow.</summary>
    /// <param name="pass">The compute pass to record into.</param>
    /// <param name="frameInFlight">Which set of per-frame resources this frame may write.</param>
    /// <param name="depth">The scene depth, at full resolution.</param>
    /// <param name="normal">The G-buffer normal, at full resolution.</param>
    /// <param name="frame">What to trace against, and the light.</param>
    /// <returns><see langword="true"/> when a mask was made for the shading to read.</returns>
    public bool Record(
        IRhiComputePassEncoder pass,
        int frameInFlight,
        IRhiTextureView depth,
        IRhiTextureView normal,
        in FlashlightShadowFrame frame)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(depth);
        ArgumentNullException.ThrowIfNull(normal);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_raw is not { } raw || _mask is not { } mask)
        {
            return false;
        }

        Span<float> trace = stackalloc float[FlashlightShadowParameters.TraceFloats];
        FlashlightShadowParameters.WriteTrace(
            trace,
            _width,
            _height,
            frame.ViewToWorld,
            frame.CameraPosition,
            frame.Unprojection,
            frame.Light,
            frame.FrameIndex,
            frame.RecordAddress);

        ComputePass shadow;
        if (frame.World is { } world && frame.CutoutMasks is { } masks)
        {
            shadow = _trace ??= new ComputePass(_device, ShaderModuleId.FlashlightShadow, "flashlightshadow.trace");
            shadow.Bind("uScene", world);
            shadow.Bind("uMasks", masks);
        }
        else
        {
            shadow = _march ??= new ComputePass(
                _device, ShaderModuleId.FlashlightShadow, "flashlightshadow.march", new ShaderVariant("software"));
        }

        shadow.Bind("uDepth", depth);
        shadow.Bind("uNormal", normal);
        shadow.Bind("uRaw", raw.View);
        shadow.Bind("p", _traceParameters.Write<float>(frameInFlight, trace));
        shadow.Dispatch(pass, _width, _height);

        Span<float> filter = stackalloc float[FlashlightShadowParameters.FilterFloats];
        FlashlightShadowParameters.WriteFilter(filter, _width, _height, frame.Unprojection, frame.Light.LensRadius);

        _filter.Bind("uRaw", raw.View);
        _filter.Bind("uDepth", depth);
        _filter.Bind("uMask", mask.View);
        _filter.Bind("p", _filterParameters.Write<float>(frameInFlight, filter));
        _filter.Dispatch(pass, _width, _height);

        return true;
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
        _filterParameters.Dispose();
        _traceParameters.Dispose();
        _filter.Dispose();
        _trace?.Dispose();
        _march?.Dispose();
    }

    private IRhiTexture Create(RhiTextureFormat format, RhiTextureUsage usage, string label) =>
        _device.CreateTexture(new RhiTextureDescriptor(_width, _height, format, usage, Label: label));

    private void Release()
    {
        foreach (IRhiTexture? owned in (IRhiTexture?[])[_raw, _mask])
        {
            if (owned is not null)
            {
                _retired.Retire(owned);
            }
        }

        _raw = null;
        _mask = null;
    }
}
