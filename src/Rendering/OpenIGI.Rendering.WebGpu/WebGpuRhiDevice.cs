using OpenIGI.Core.Threading;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu.Native;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>
/// The RHI, answered through wgpu-native.
/// </summary>
/// <remarks>
/// <para>
/// Almost every member here is a translation followed by one call: the interface was shaped from
/// WebGPU's model precisely so that this layer has no cleverness in it, and anywhere it starts to grow
/// some is a sign the interface drifted.
/// </para>
/// <para>
/// What it does own is the widening of narrow storage formats (see <see cref="WebGpuFormatPolicy"/>)
/// and the report of when that happened, because nothing above a backend should be deciding what a
/// format is allocated as.
/// </para>
/// </remarks>
public sealed unsafe partial class WebGpuRhiDevice : IRhiDevice
{
    private readonly WebGpuContext _context;
    private readonly bool _ownsContext;
    private readonly List<string> _widened = [];
    private readonly WebGpuSurface? _surface;
    private readonly WebGpuSurfaceSupport _support;
    private readonly IWorkScheduler _finishing;
    private WebGpuPushRing? _pushes;

    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="WebGpuRhiDevice"/> class.</summary>
    /// <param name="context">The instance, adapter, device and queue to work through.</param>
    /// <param name="ownsContext">Whether disposing this disposes the context too.</param>
    /// <param name="surface">The surface to present to, or null for a headless device.</param>
    /// <param name="finishing">
    /// Where encoders are finished (<see cref="WebGpuCommandEncoder.Finish"/>); inline where none is given.
    /// </param>
    public WebGpuRhiDevice(
        WebGpuContext context,
        bool ownsContext = true,
        WebGpuSurface? surface = null,
        IWorkScheduler? finishing = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _ownsContext = ownsContext;
        _surface = surface;
        _support = surface?.Describe(context.Adapter) ?? default;
        _finishing = finishing ?? InlineWorkScheduler.Instance;
    }

    /// <summary>Gets where this device's encoders are finished.</summary>
    internal IWorkScheduler Finishing => _finishing;

    /// <summary>Creates a device over a context of its own.</summary>
    /// <param name="surface">The surface the adapter must present to, or the null handle for headless.</param>
    /// <param name="lowPower">Whether to prefer the integrated adapter.</param>
    /// <returns>The device.</returns>
    public static WebGpuRhiDevice Create(WGPUSurface surface = default, bool lowPower = false) =>
        new(WebGpuContext.Create(surface, lowPower));

    /// <summary>Creates a device that presents to a window.</summary>
    /// <param name="handles">The window's native handles.</param>
    /// <param name="width">The width to configure the surface at.</param>
    /// <param name="height">The height to configure it at.</param>
    /// <param name="vsync">Whether to wait for the display.</param>
    /// <param name="lowPower">Whether to prefer the integrated adapter.</param>
    /// <param name="finishing">
    /// Where encoders are finished (<see cref="WebGpuCommandEncoder.Finish"/>); inline where none is given.
    /// </param>
    /// <returns>The device, with its surface configured.</returns>
    /// <remarks>
    /// The surface is made before the adapter is asked for, because the adapter has to be one that can
    /// present to it -- on a machine with two, asking the other way round can pick the one the window is
    /// not on.
    /// </remarks>
    public static WebGpuRhiDevice CreateForWindow(
        in WebGpuWindowHandles handles,
        int width,
        int height,
        bool vsync = true,
        bool lowPower = false,
        IWorkScheduler? finishing = null)
    {
        WgpuLibrary.Register();

        WGPUInstance instance = Wgpu.wgpuCreateInstance(null);
        if (!instance.IsValid)
        {
            throw new InvalidOperationException("wgpu-native would not create an instance.");
        }

        WebGpuSurface surface = WebGpuSurface.Create(instance, handles, "openigi");

        try
        {
            WebGpuContext context = WebGpuContext.CreateOn(instance, surface.Handle, lowPower);
            WebGpuRhiDevice device = new(context, ownsContext: true, surface, finishing);
            device.ConfigureSurface(width, height, vsync);
            return device;
        }
        catch
        {
            surface.Dispose();
            Wgpu.wgpuInstanceRelease(instance);
            throw;
        }
    }

    /// <inheritdoc/>
    public string AdapterName => _context.Report.Name;

    /// <inheritdoc/>
    public string ApiName => "WebGPU";

    /// <inheritdoc/>
    /// <remarks>One: a resolved WebGPU timestamp is in nanoseconds already.</remarks>
    public double TimestampPeriodNanoseconds => 1.0;

    /// <inheritdoc/>
    public string StreamlineUnavailableReason => "NVIDIA's features run on Vulkan only";

    /// <inheritdoc/>
    public RhiFeatures Features => _context.Report.Features;

    /// <inheritdoc/>
    public ShaderTarget ShaderTarget => ShaderTarget.Wgsl;

    /// <inheritdoc/>
    public RhiLimits Limits => _context.Report.Limits;

    /// <inheritdoc/>
    /// <remarks>
    /// What the canvas or the offscreen target was configured as. A browser hands out an sRGB image
    /// where this backend's desktop counterpart hands out an unsigned-normalised one, which is exactly
    /// why the shared renderer asks rather than assuming.
    /// </remarks>
    public RhiTextureFormat SurfaceFormat => _offscreen.Count > 0
        ? _offscreen[0].Format
        : _surface?.Format ?? RhiTextureFormat.Bgra8Unorm;

    /// <inheritdoc/>
    /// <remarks>
    /// Null for now. wgpu-native does offer ray query as an extension of its own, so this is a gap to
    /// fill rather than a thing the backend cannot do — see the adapter report, which already says
    /// whether the feature is there.
    /// </remarks>
    public IRhiAccelerationStructures? AccelerationStructures => null;

    /// <summary>Gets the context this works through.</summary>
    public WebGpuContext Context => _context;

    /// <summary>Gets the ring push data is written into, made on first use.</summary>
    /// <remarks>
    /// WebGPU has no push constants, so a push is a slice of a uniform buffer bound by dynamic offset.
    /// See <see cref="WebGpuPushRing"/> for what that costs and when it has to be reset.
    /// </remarks>
    public WebGpuPushRing PushRing => _pushes ??= new WebGpuPushRing(this);

    /// <summary>Gets every texture whose format had to be widened, as sentences.</summary>
    /// <remarks>
    /// Read at start-up and logged. A texture silently costing four times what it was asked for is the
    /// kind of thing that is only ever noticed by somebody measuring something else.
    /// </remarks>
    public IReadOnlyList<string> WidenedFormats => _widened;

    /// <inheritdoc/>
    public void Submit(ReadOnlySpan<IRhiCommandBuffer> commands)
    {
        ThrowIfDisposed();

        if (commands.Length == 0)
        {
            return;
        }

        // The pushes this submission's draws read, uploaded ahead of it in one write a chunk.
        _pushes?.Flush();

        WGPUCommandBuffer[] handles = new WGPUCommandBuffer[commands.Length];
        for (int i = 0; i < commands.Length; i++)
        {
            handles[i] = commands[i] switch
            {
                WebGpuFinishingCommandBuffer finishing => finishing.Finished().Handle,
                IRhiCommandBuffer finished => ((WebGpuCommandBuffer)finished).Handle,
            };
        }

        fixed (WGPUCommandBuffer* first = handles)
        {
            Wgpu.wgpuQueueSubmit(_context.Queue, (nuint)handles.Length, first);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Nothing is held here: every submit goes to the queue as it is made, so there is nothing to send
    /// early, and the wait belongs to the readback that follows -- which polls the queue until it drains,
    /// and on a browser, which will not block, reports what it has instead.
    /// </remarks>
    public void Flush() => ThrowIfDisposed();

    /// <inheritdoc/>
    public IRhiCommandEncoder CreateCommandEncoder(string label = "")
    {
        ThrowIfDisposed();
        using WebGpuString name = new(label);

        WGPUCommandEncoderDescriptor native = new() { label = name.View };
        WGPUCommandEncoder handle = Wgpu.wgpuDeviceCreateCommandEncoder(_context.Device, &native);

        return handle.IsValid
            ? new WebGpuCommandEncoder(this, handle, label)
            : throw new InvalidOperationException($"The command encoder '{label}' could not be created.");
    }

    /// <inheritdoc/>
    public IRhiReadback ReadBuffer(IRhiBuffer buffer, ulong offset = 0, ulong sizeInBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfDisposed();

        ulong size = sizeInBytes > 0 ? sizeInBytes : buffer.SizeInBytes - offset;
        return new WebGpuReadback(_context, (WebGpuBuffer)buffer, offset, size);
    }

    /// <inheritdoc/>
    public void WaitIdle()
    {
        ThrowIfDisposed();
        _context.Poll();
        _context.ProcessEvents();
    }

    /// <inheritdoc/>
    public IRhiWorkMarker MarkSubmittedWork()
    {
        ThrowIfDisposed();
        return new WebGpuWorkMarker(_context);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _frameTexture?.Dispose();
        _drawnFrame?.Dispose();
        foreach (WebGpuTexture replaced in _replacedFrames)
        {
            replaced.Dispose();
        }

        ReleaseOffscreen();
        _pushes?.Dispose();
        _surface?.Dispose();

        if (_ownsContext)
        {
            _context.Dispose();
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
