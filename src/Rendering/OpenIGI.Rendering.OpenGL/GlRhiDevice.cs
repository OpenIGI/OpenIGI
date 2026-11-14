using System.Runtime.InteropServices;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Rhi.Push;
using OpenIGI.Shaders;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>The RHI over OpenGL 4.3 core.</summary>
/// <remarks>
/// <para>
/// WebGPU's model maps onto OpenGL closely: bind groups become units through each pipeline's plan, push data a
/// uniform ring bound by range as on WebGPU (the same <see cref="RhiPushRing"/>), and there are no barriers to
/// place except after shader writes, which the executor does. The clip space is set to the one every other backend
/// has -- depth [0, 1], origin top-left -- so the shaders and the frame above are the same.
/// </para>
/// <para>
/// The frame draws into a texture of its own, and presenting is a blit to the window's framebuffer, flipped: the
/// window's rows run bottom-up whatever the clip control says, and a texture's are the RHI's top-down.
/// </para>
/// </remarks>
public sealed partial class GlRhiDevice : IRhiDevice
{
    private const string Category = "OpenGL";

    private readonly IGlSurface _surface;
    private readonly IOpenIgiLogger _logger;
    private readonly List<GlFence> _fences = [];
    private readonly Dictionary<int, RhiInstancePushRing> _instancePushes = [];
    private readonly DebugProc? _debugProc;
    private readonly System.Collections.Concurrent.ConcurrentBag<GlCommandList> _commandLists = [];
    private RhiPushRing? _pushes;
    private GlBindGroupLayout? _pushLayout;
    private GlBindGroupLayout? _instancePushLayout;
    private bool _disposed;

    // How long the OpenGL thread, idle with someone waiting, waits on a fence before looking for work again.
    private const ulong FenceWatchNanoseconds = 250_000;

    private int _fenceWaiters;

    private GlRhiDevice(IGlSurface surface, GL gl, GlCapabilities caps, IOpenIgiLogger logger, bool validate)
    {
        _surface = surface;
        _logger = logger;
        Gl = gl;
        Caps = caps;
        Validating = validate && caps.Debug;
        Marking = Validating || RhiPassMarkers.Requested;
        Thread = new GlThread();

        gl.GetInteger(GLEnum.MaxCombinedTextureImageUnits, out int textureUnits);
        gl.GetInteger(GLEnum.MaxImageUnits, out int imageUnits);
        gl.GetInteger(GLEnum.MaxUniformBufferBindings, out int uniformUnits);
        gl.GetInteger(GLEnum.MaxShaderStorageBufferBindings, out int storageUnits);

        // The last unit is kept for making and filling textures, so doing either never disturbs a unit a shader reads.
        EditUnit = (uint)Math.Max(1, textureUnits - 1);
        State = new GlState(gl, (int)EditUnit, Math.Max(1, imageUnits), Math.Max(uniformUnits, storageUnits));
        Framebuffers = new GlFramebuffers(gl);
        Executor = new GlExecutor(this);
        PushGroup = RhiPushBlocks.For(ShaderTarget.Glsl).Group;

        // The clip space every other backend has: depth [0, 1], and the first row of a target at the top.
        gl.ClipControl(GLEnum.UpperLeft, GLEnum.ZeroToOne);

        // An sRGB target is written encoded, as it is everywhere else, and a cube is filtered across its seams.
        gl.Enable(EnableCap.FramebufferSrgb);
        gl.Enable(EnableCap.TextureCubeMapSeamless);

        if (Validating)
        {
            _debugProc = OnDebugMessage;
            gl.Enable(EnableCap.DebugOutput);
            gl.Enable(EnableCap.DebugOutputSynchronous);
            unsafe
            {
                gl.DebugMessageCallback(_debugProc, null);
            }
        }
    }

    /// <summary>Gets whether the debug output is on, which also names objects and labels groups.</summary>
    public bool Validating { get; }

    /// <inheritdoc/>
    public string AdapterName => Caps.Renderer;

    /// <inheritdoc/>
    public string ApiName => "OpenGL";

    /// <inheritdoc/>
    public double TimestampPeriodNanoseconds => 1.0;

    /// <inheritdoc/>
    public string StreamlineUnavailableReason => "NVIDIA's features need Vulkan or Direct3D 12; this is OpenGL";

    /// <inheritdoc/>
    public RhiFeatures Features => Caps.Features;

    /// <inheritdoc/>
    public ShaderTarget ShaderTarget => ShaderTarget.Glsl;

    /// <inheritdoc/>
    public RhiLimits Limits => Caps.Limits;

    /// <inheritdoc/>
    /// <remarks>
    /// The frame's own. A window's frame is unsigned-normalised, the display's bytes as they are; an offscreen frame is
    /// whatever it was attached as, and a pipeline told otherwise would encode what an sRGB target encodes again.
    /// </remarks>
    public RhiTextureFormat SurfaceFormat => _offscreen && _frame is { } frame ? frame.Format : RhiTextureFormat.Rgba8Unorm;

    /// <inheritdoc/>
    public IRhiAccelerationStructures? AccelerationStructures => null;

    internal GL Gl { get; }

    internal GlCapabilities Caps { get; }

    internal GlThread Thread { get; }

    internal GlState State { get; }

    internal GlFramebuffers Framebuffers { get; }

    internal GlExecutor Executor { get; }

    internal uint EditUnit { get; }

    internal int PushGroup { get; }

    /// <summary>Gets the ring push data is written into, made on first use.</summary>
    internal RhiPushRing PushRing => _pushes ??= new RhiPushRing(
        this,
        CreatePushGroup,
        RhiPushBlocks.For(ShaderTarget.Glsl).LargestBlock,
        alignment: Math.Max(256, Caps.UniformOffsetAlignment));

    /// <summary>Makes the device over a context the window system made and has current on this thread.</summary>
    /// <param name="surface">The context's window-system side.</param>
    /// <param name="width">The frame's width in pixels.</param>
    /// <param name="height">The frame's height in pixels.</param>
    /// <param name="vsync">Whether a present waits for the display.</param>
    /// <param name="logger">Where the debug output and the start-up report go.</param>
    /// <param name="validate">Whether to turn on the driver's debug output.</param>
    /// <returns>The device.</returns>
    /// <exception cref="OpenGLUnavailableException">The context is short of what the backend needs.</exception>
    public static GlRhiDevice Create(
        IGlSurface surface,
        int width,
        int height,
        bool vsync,
        IOpenIgiLogger? logger = null,
        bool validate = false)
    {
        ArgumentNullException.ThrowIfNull(surface);
        IOpenIgiLogger log = logger ?? NullLogger.Instance;

        surface.MakeCurrent();
        GL gl = GL.GetApi(surface.GetProcAddress);
        GlCapabilities caps = GlCapabilities.Read(gl);

        GlRhiDevice device = new(surface, gl, caps, log, validate);
        log.Information(
            Category,
            $"OpenGL {caps.Version / 10}.{caps.Version % 10} on {caps.Renderer} ({caps.Vendor}); "
            + $"query buffers {(caps.QueryBuffer ? "yes" : "no")}, BC {(caps.S3tc ? "yes" : "no")}"
            + (device.Validating ? ", debug output on." : "."));

        device.ConfigureSurface(width, height, vsync);

        // From here the context is the OpenGL thread's: this thread lets it go, and that one makes it current.
        surface.ReleaseCurrent();
        try
        {
            device.Thread.Start(surface.MakeCurrent, surface.ReleaseCurrent, device.WatchFences);
        }
        catch
        {
            surface.MakeCurrent();
            device.Dispose();
            throw;
        }

        return device;
    }

    /// <summary>Gets whether passes, debug groups and objects are named to the driver, for a frame debugger to show.</summary>
    /// <remarks>On with validation, or alone where <see cref="RhiPassMarkers.Requested"/>.</remarks>
    public bool Marking { get; }

    /// <summary>Gets whether validation was asked for in the environment: <c>OPENIGI_GL_VALIDATION=1</c>.</summary>
    public static bool ValidationFromEnvironment =>
        Environment.GetEnvironmentVariable("OPENIGI_GL_VALIDATION")?.Trim().ToUpperInvariant() is "1" or "TRUE" or "ON";

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Behind every frame still queued, on the thread that owns the context, which then lets it go and stops. A
        // thread already gone took the context with it, and there is nothing left to free.
        Thread.TryInvoke(() =>
        {
            _pushes?.Dispose();
            foreach (RhiInstancePushRing ring in _instancePushes.Values)
            {
                ring.Dispose();
            }

            _instancePushes.Clear();
            Executor.Dispose();
            ReleaseSurface();
            Gl.Finish();
        });

        Thread.Dispose();
        _framesQueued.Dispose();
    }

    /// <summary>Gets the ring per-instance push blocks of one size are packed into, made on first use.</summary>
    internal RhiInstancePushRing InstancePushRing(int blockBytes)
    {
        if (!_instancePushes.TryGetValue(blockBytes, out RhiInstancePushRing? ring))
        {
            ring = new RhiInstancePushRing(this, CreateInstancePushGroup, blockBytes);
            _instancePushes.Add(blockBytes, ring);
        }

        return ring;
    }

    /// <summary>A command list to record into, reused from an earlier submission where there is one.</summary>
    internal GlCommandList RentCommands() => _commandLists.TryTake(out GlCommandList? list) ? list : new GlCommandList();

    /// <summary>Gives a list back once it has been replayed or will never be.</summary>
    internal void ReturnCommands(GlCommandList list)
    {
        list.Reset();
        _commandLists.Add(list);
    }

    /// <summary>Names an object for the debug output, where it is on.</summary>
    internal void Label(GLEnum identifier, uint name, string label)
    {
        if (Marking && !string.IsNullOrEmpty(label))
        {
            Gl.ObjectLabel(identifier, name, (uint)label.Length, label);
        }
    }

    /// <summary>Binds a texture on the edit unit, to make or fill it.</summary>
    internal void BindForEdit(GLEnum target, uint name)
    {
        Gl.ActiveTexture(GLEnum.Texture0 + (int)EditUnit);
        Gl.BindTexture(target, name);
        State.ForgetActiveUnit();
    }

    /// <summary>Keeps a fence to poll as frames end, for a caller on another thread watching it.</summary>
    internal void Track(GlFence fence) => _fences.Add(fence);

    /// <summary>Says a thread is waiting on a fence until the returned scope ends, so this thread watches closely.</summary>
    internal FenceWaiter WaitingOnFences()
    {
        Interlocked.Increment(ref _fenceWaiters);
        return new FenceWaiter(this);
    }

    // Settles the fences that have signalled. They signal in the order they were made, so the first one still out
    // means every later one is too, and is not asked about.
    private void PollFences()
    {
        int settled = 0;
        while (settled < _fences.Count && _fences[settled].Poll(0))
        {
            settled++;
        }

        _fences.RemoveRange(0, settled);
    }

    // What the OpenGL thread does with nothing to run. With someone waiting, it waits a moment on the oldest fence and
    // asks to be back at once; otherwise it looks, and rests.
    private bool WatchFences()
    {
        if (_fences.Count == 0)
        {
            return false;
        }

        if (Volatile.Read(ref _fenceWaiters) > 0)
        {
            _fences[0].Poll(FenceWatchNanoseconds);
            PollFences();
            return true;
        }

        PollFences();
        return false;
    }

    /// <summary>The span in which a thread says it waits on a fence.</summary>
    internal readonly struct FenceWaiter(GlRhiDevice device) : IDisposable
    {
        public void Dispose() => Interlocked.Decrement(ref device._fenceWaiters);
    }

    private IRhiBindGroup CreatePushGroup(IRhiBuffer buffer, int sliceBytes) => CreateBindGroup(new RhiBindGroupDescriptor(
        _pushLayout ??= new GlBindGroupLayout(this, new RhiBindGroupLayoutDescriptor(
            [new RhiBindGroupLayoutEntry(0, RhiBindingType.UniformBuffer, RhiShaderStages.All, HasDynamicOffset: true)],
            "push")),
        [RhiBindGroupEntry.Of(0, buffer, 0, (ulong)sliceBytes)],
        "push"));

    private IRhiBindGroup CreateInstancePushGroup(IRhiBuffer buffer) => CreateBindGroup(new RhiBindGroupDescriptor(
        _instancePushLayout ??= new GlBindGroupLayout(this, new RhiBindGroupLayoutDescriptor(
            [new RhiBindGroupLayoutEntry(0, RhiBindingType.ReadOnlyStorageBuffer, RhiShaderStages.Vertex | RhiShaderStages.Fragment)],
            "push.instances")),
        [RhiBindGroupEntry.Of(0, buffer, 0, buffer.SizeInBytes)],
        "push.instances"));

    private void OnDebugMessage(GLEnum source, GLEnum type, int id, GLEnum severity, int length, nint message, nint userParam)
    {
        // Notifications are the driver describing what it did (buffer placement, shader recompiles), not faults.
        if (severity == GLEnum.DebugSeverityNotification)
        {
            return;
        }

        string text = Marshal.PtrToStringUTF8(message, length) ?? string.Empty;
        if (type == GLEnum.DebugTypeError || severity == GLEnum.DebugSeverityHigh)
        {
            _logger.Error(Category, $"{type} {id}: {text}");
        }
        else
        {
            _logger.Warning(Category, $"{type} {id}: {text}");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
