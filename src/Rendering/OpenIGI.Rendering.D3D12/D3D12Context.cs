using System.Runtime.InteropServices;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering.D3D12.Interop;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using Feature = Silk.NET.Direct3D12.Feature;

namespace OpenIGI.Rendering.D3D12;

/// <summary>How much checking the device is created with.</summary>
public enum D3D12Validation
{
    /// <summary>None: what a player runs.</summary>
    None,

    /// <summary>The debug layer, its messages written to the log.</summary>
    DebugLayer,

    /// <summary>The debug layer with GPU-based validation: slow, and the one that finds a bad descriptor.</summary>
    GpuBased,
}

/// <summary>Thrown when this machine cannot run the Direct3D 12 backend at the baseline it needs.</summary>
/// <remarks>The application catches it and runs on Vulkan instead, saying why.</remarks>
public sealed class D3D12UnavailableException : Exception
{
    /// <summary>Initialises a new instance of the <see cref="D3D12UnavailableException"/> class.</summary>
    public D3D12UnavailableException()
    {
    }

    /// <summary>Initialises a new instance of the <see cref="D3D12UnavailableException"/> class.</summary>
    /// <param name="message">Why.</param>
    public D3D12UnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises a new instance of the <see cref="D3D12UnavailableException"/> class.</summary>
    /// <param name="message">Why.</param>
    /// <param name="inner">What failed.</param>
    public D3D12UnavailableException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>What the adapter offers, as the backend reads it once at creation.</summary>
/// <param name="ShaderModel">The highest shader model, as <c>0x66</c> for 6.6.</param>
/// <param name="RaytracingTier">The DXR tier, as <c>11</c> for 1.1; nought where there is none.</param>
/// <param name="GpuUploadHeap">Whether the GPU upload heap -- device-local memory the CPU writes -- is offered.</param>
/// <param name="TimestampFrequency">The direct queue's timestamp ticks per second.</param>
internal readonly record struct D3D12Capabilities(
    int ShaderModel,
    int RaytracingTier,
    bool GpuUploadHeap,
    ulong TimestampFrequency);

/// <summary>
/// The device and everything it is created from: the Agility runtime, the debug layer, the adapter, the
/// direct queue and its fence.
/// </summary>
/// <remarks>
/// <para>
/// The runtime is the Agility SDK's, loaded from <c>&lt;app&gt;/D3D12/</c> through
/// <c>ID3D12SDKConfiguration1::CreateDeviceFactory</c>. That is the way in for a process that cannot export
/// <c>D3D12SDKVersion</c>, which a .NET apphost cannot, and it is what gives every machine the same
/// enhanced barriers, shader model and DRED rather than whatever its Windows update carries.
/// </para>
/// <para>
/// The baseline is checked here and refused with <see cref="D3D12UnavailableException"/>: shader model 6.6
/// (a traced hit reaches its mesh through the descriptor heap), resource binding tier 3, enhanced barriers,
/// and typed UAV loads of the additional formats. A machine short of any of them runs on Vulkan.
/// </para>
/// </remarks>
internal sealed unsafe class D3D12Context : IDisposable
{
    /// <summary>The Agility SDK version the runtime in <c>&lt;app&gt;/D3D12/</c> is.</summary>
    public const uint SdkVersion = 619;

    private const string LogCategory = "D3D12";

    // The info queue hands messages to a plain function pointer; one device at a time owns the sink.
    private static IOpenIgiLogger? _messageSink;

    private readonly IOpenIgiLogger _logger;
    private ID3D12DeviceFactory* _factory;
    private IDXGIFactory6* _dxgi;
    private IDXGIAdapter1* _adapter;
    private ID3D12Device10* _device;
    private ID3D12CommandQueue* _queue;
    private ID3D12Fence* _fence;
    private ID3D12InfoQueue1* _infoQueue;
    private uint _callbackCookie;
    private nint _fenceEvent;
    private ulong _signalled;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="D3D12Context"/> class.</summary>
    /// <param name="logger">Where the device reports.</param>
    /// <param name="validation">How much checking to create it with.</param>
    /// <exception cref="D3D12UnavailableException">This machine cannot run the backend.</exception>
    public D3D12Context(IOpenIgiLogger logger, D3D12Validation validation)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        if (!OperatingSystem.IsWindows())
        {
            throw new D3D12UnavailableException("Direct3D 12 runs on Windows only.");
        }

        try
        {
            CreateFactory(validation);
            CreateAdapterAndDevice(validation);
            CreateQueue();

            // The first call to find the device removed says why, whichever call that is.
            D3D12Check.DeviceLost = () =>
            {
                if (_device is not null)
                {
                    D3D12DeviceRemoved.Report(_device, _logger, _device->GetDeviceRemovedReason());
                }
            };

            if (validation != D3D12Validation.None)
            {
                AttachInfoQueue();
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Gets the device.</summary>
    public ID3D12Device10* Device => _device;

    /// <summary>Gets the direct queue every submission goes to.</summary>
    public ID3D12CommandQueue* Queue => _queue;

    /// <summary>Gets the DXGI factory a swapchain is created from.</summary>
    public IDXGIFactory6* Dxgi => _dxgi;

    /// <summary>Gets the adapter's name.</summary>
    public string AdapterName { get; private set; } = string.Empty;

    /// <summary>Gets the adapter's locally unique identifier, which Streamline names it by.</summary>
    public Luid AdapterLuid { get; private set; }

    /// <summary>Gets what the adapter offers.</summary>
    public D3D12Capabilities Capabilities { get; private set; }

    /// <summary>Gets the newest fence value the queue has been asked to signal.</summary>
    public ulong Signalled => _signalled;

    /// <summary>Gets the newest fence value the GPU has reached.</summary>
    public ulong Completed => _fence->GetCompletedValue();

    /// <summary>Gets the fence every submission signals.</summary>
    public ID3D12Fence* Fence => _fence;

    /// <summary>Asks the queue to signal the next fence value once everything before it has run.</summary>
    /// <returns>The value it will reach.</returns>
    public ulong Signal()
    {
        _signalled++;
        D3D12Check.Ok(_queue->Signal(_fence, _signalled), "ID3D12CommandQueue::Signal");
        return _signalled;
    }

    /// <summary>Blocks until the GPU has reached a fence value.</summary>
    /// <param name="value">The value.</param>
    /// <param name="timeout">How long to wait, or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <returns><see langword="true"/> when it was reached.</returns>
    public bool WaitFor(ulong value, TimeSpan timeout)
    {
        // The event is shared by every wait, and waking it proves nothing about THIS value: a wait that timed
        // out leaves the event armed for its own value, and when the GPU reaches that later the event is left
        // signalled for whoever waits next. Trusting the wake, WaitIdle returned on that stale signal with the
        // GPU still a frame behind, and a swapchain rebuilt just after freed buffers the GPU was drawing into
        // -- a page fault, the device hung, and frame generation's plugin unable to make its queues. So the
        // fence itself is the answer, re-armed and re-read until it says so.
        long deadline = timeout == Timeout.InfiniteTimeSpan
            ? long.MaxValue
            : Environment.TickCount64 + (long)Math.Max(0, timeout.TotalMilliseconds);

        // An endless wait is waited out in slices, so a GPU that stopped without removing the device -- a
        // hang the OS has not yet called -- is reported rather than silently freezing the game.
        const uint Slice = 5000;
        long started = Environment.TickCount64;
        bool warned = false;
        while (_fence->GetCompletedValue() < value)
        {
            long left = deadline - Environment.TickCount64;
            if (left <= 0)
            {
                CheckRemoved();
                return false;
            }

            D3D12Check.Ok(_fence->SetEventOnCompletion(value, (void*)_fenceEvent), "ID3D12Fence::SetEventOnCompletion");
            _ = Kernel32.WaitForSingleObject(_fenceEvent, (uint)Math.Min(left, Slice));
            CheckRemoved();

            if (!warned && timeout == Timeout.InfiniteTimeSpan && Environment.TickCount64 - started >= 2 * Slice)
            {
                warned = true;
                _logger.Warning(LogCategory, $"The GPU has not reached fence value {value} in {(Environment.TickCount64 - started) / 1000} s (it is at {_fence->GetCompletedValue()}); it may be hung.");
            }
        }

        return true;
    }

    /// <summary>Replaces the DXGI factory with a proxy of it, so the swapchain made from it can be hooked.</summary>
    /// <param name="upgrade">What turns the factory into its proxy, or returns it unchanged.</param>
    public void UpgradeDxgi(Func<nint, nint> upgrade)
    {
        ArgumentNullException.ThrowIfNull(upgrade);
        _dxgi = (IDXGIFactory6*)upgrade((nint)_dxgi);
    }

    /// <summary>How much device-local memory the OS will let this process have, and how much it uses.</summary>
    /// <returns>The budget and the current usage, in bytes; noughts where the adapter will not say.</returns>
    public (ulong Budget, ulong Usage) LocalMemory()
    {
        IDXGIAdapter3* adapter = null;
        Guid iid = IDXGIAdapter3.Guid;
        if (_adapter is null || ((IUnknown*)_adapter)->QueryInterface(&iid, (void**)&adapter) < 0 || adapter is null)
        {
            return (0, 0);
        }

        try
        {
            QueryVideoMemoryInfo info = default;
            return adapter->QueryVideoMemoryInfo(0, MemorySegmentGroup.Local, &info) < 0
                ? (0, 0)
                : (info.Budget, info.CurrentUsage);
        }
        finally
        {
            adapter->Release();
        }
    }

    /// <summary>Blocks until everything submitted has run.</summary>
    public void WaitIdle() => WaitFor(Signal(), Timeout.InfiniteTimeSpan);

    /// <summary>Throws, with what DRED recorded, if the device has been removed.</summary>
    /// <exception cref="GraphicsDeviceException">It has.</exception>
    public void CheckRemoved()
    {
        int reason = _device->GetDeviceRemovedReason();
        if (reason >= 0)
        {
            return;
        }

        D3D12Check.DeviceLost = null;
        D3D12DeviceRemoved.Report(_device, _logger, reason);
        throw new GraphicsDeviceException($"The Direct3D 12 device was removed (0x{reason:X8}); the log holds what DRED recorded.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_queue is not null && _fence is not null)
        {
            try
            {
                WaitIdle();
            }
            catch (GraphicsDeviceException)
            {
                // A removed device has nothing left to wait for.
            }
        }

        if (_infoQueue is not null && _callbackCookie != 0)
        {
            _infoQueue->UnregisterMessageCallback(_callbackCookie);
            _messageSink = null;
        }

        D3D12Check.Release(ref _infoQueue);
        D3D12Check.Release(ref _fence);
        D3D12Check.Release(ref _queue);
        D3D12Check.Release(ref _device);
        D3D12Check.Release(ref _adapter);
        D3D12Check.Release(ref _dxgi);
        D3D12Check.Release(ref _factory);

        if (_fenceEvent != 0)
        {
            _ = Kernel32.CloseHandle(_fenceEvent);
            _fenceEvent = 0;
        }
    }

    private void CreateFactory(D3D12Validation validation)
    {
        string runtime = Path.Combine(AppContext.BaseDirectory, "D3D12");
        if (!File.Exists(Path.Combine(runtime, "D3D12Core.dll")))
        {
            throw new D3D12UnavailableException($"The Agility runtime is not at {runtime}.");
        }

        Silk.NET.Direct3D12.D3D12 api = Silk.NET.Direct3D12.D3D12.GetApi();
        ID3D12SDKConfiguration1* configuration = null;
        Guid clsid = D3D12Classes.SdkConfiguration;
        Guid iid = ID3D12SDKConfiguration1.Guid;
        if (api.GetInterface(&clsid, &iid, (void**)&configuration) < 0 || configuration is null)
        {
            throw new D3D12UnavailableException("This Windows has no Direct3D 12 SDK configuration, so the Agility runtime cannot load.");
        }

        try
        {
            byte* path = (byte*)SilkMarshal.StringToPtr(runtime + Path.DirectorySeparatorChar, NativeStringEncoding.UTF8);
            try
            {
                ID3D12DeviceFactory* factory = null;
                Guid factoryIid = ID3D12DeviceFactory.Guid;
                int created = configuration->CreateDeviceFactory(SdkVersion, path, &factoryIid, (void**)&factory);
                if (created < 0)
                {
                    throw new D3D12UnavailableException($"The Agility runtime {SdkVersion} would not load from {runtime} (0x{created:X8}).");
                }

                _factory = factory;
            }
            finally
            {
                SilkMarshal.Free((nint)path);
            }
        }
        finally
        {
            ((IUnknown*)configuration)->Release();
        }

        // Breadcrumbs on request -- OPENIGI_D3D12_DRED=1 -- or with validation: the runtime records a marker
        // around every command, which costs GPU time in every frame to describe a device loss most runs never
        // have. Without them a loss still reports its reason.
        bool dredWanted = validation != D3D12Validation.None
            || Environment.GetEnvironmentVariable("OPENIGI_D3D12_DRED") is "1" or "true" or "on";
        ID3D12DeviceRemovedExtendedDataSettings1* dred = null;
        Guid dredClsid = D3D12Classes.DeviceRemovedExtendedData;
        Guid dredIid = ID3D12DeviceRemovedExtendedDataSettings1.Guid;
        if (dredWanted && _factory->GetConfigurationInterface(&dredClsid, &dredIid, (void**)&dred) >= 0 && dred is not null)
        {
            dred->SetAutoBreadcrumbsEnablement(DredEnablement.ForcedOn);
            dred->SetBreadcrumbContextEnablement(DredEnablement.ForcedOn);
            dred->SetPageFaultEnablement(DredEnablement.ForcedOn);
            ((IUnknown*)dred)->Release();
        }

        if (validation != D3D12Validation.None)
        {
            ID3D12Debug1* debug = null;
            Guid debugClsid = D3D12Classes.Debug;
            Guid debugIid = ID3D12Debug1.Guid;
            if (_factory->GetConfigurationInterface(&debugClsid, &debugIid, (void**)&debug) >= 0 && debug is not null)
            {
                debug->EnableDebugLayer();
                debug->SetEnableGPUBasedValidation(validation == D3D12Validation.GpuBased);
                ((IUnknown*)debug)->Release();
            }
            else
            {
                _logger.Warning(LogCategory, "The debug layer was asked for and is not installed beside the runtime.");
            }
        }

        DXGI dxgi = DXGI.GetApi(null);
        IDXGIFactory6* factory6 = null;
        Guid dxgiIid = IDXGIFactory6.Guid;
        D3D12Check.Ok(
            dxgi.CreateDXGIFactory2(validation != D3D12Validation.None ? 0x01u : 0u, &dxgiIid, (void**)&factory6),
            "CreateDXGIFactory2");
        _dxgi = factory6;
    }

    private void CreateAdapterAndDevice(D3D12Validation validation)
    {
        List<string> refused = [];

        for (uint index = 0; ; index++)
        {
            IDXGIAdapter1* adapter = null;
            Guid iid = IDXGIAdapter1.Guid;
            if (_dxgi->EnumAdapterByGpuPreference(index, GpuPreference.HighPerformance, &iid, (void**)&adapter) < 0)
            {
                break;
            }

            AdapterDesc1 description;
            adapter->GetDesc1(&description);
            string name = new((char*)description.Description);

            // The software rasteriser is an adapter too, and would "succeed" at a frame a second.
            if ((description.Flags & (uint)AdapterFlag.Software) != 0)
            {
                ((IUnknown*)adapter)->Release();
                continue;
            }

            ID3D12Device10* device = null;
            Guid deviceIid = ID3D12Device10.Guid;
            int created = _factory->CreateDevice((IUnknown*)adapter, D3DFeatureLevel.Level120, &deviceIid, (void**)&device);
            if (created < 0 || device is null)
            {
                refused.Add($"{name}: no device (0x{created:X8})");
                ((IUnknown*)adapter)->Release();
                continue;
            }

            string? shortfall = Shortfall(device, out D3D12Capabilities capabilities);
            if (shortfall is not null)
            {
                refused.Add($"{name}: {shortfall}");
                ((IUnknown*)device)->Release();
                ((IUnknown*)adapter)->Release();
                continue;
            }

            _adapter = adapter;
            _device = device;
            AdapterName = name;
            AdapterLuid = description.AdapterLuid;
            Capabilities = capabilities;
            _logger.Information(
                LogCategory,
                $"{name}: shader model {capabilities.ShaderModel >> 4}.{capabilities.ShaderModel & 0xF}, "
                + $"raytracing {(capabilities.RaytracingTier == 0 ? "none" : $"tier {capabilities.RaytracingTier / 10}.{capabilities.RaytracingTier % 10}")}, "
                + $"GPU upload heap {(capabilities.GpuUploadHeap ? "yes" : "no")}, Agility {SdkVersion}"
                + (validation == D3D12Validation.None ? "." : $", validation {validation}."));
            return;
        }

        throw new D3D12UnavailableException(
            refused.Count == 0
                ? "No hardware adapter offers Direct3D 12."
                : "No adapter meets the Direct3D 12 baseline: " + string.Join("; ", refused) + ".");
    }

    private static string? Shortfall(ID3D12Device10* device, out D3D12Capabilities capabilities)
    {
        capabilities = default;

        FeatureDataShaderModel model = new() { HighestShaderModel = D3DShaderModel.ShaderModel67 };
        if (device->CheckFeatureSupport(Feature.ShaderModel, &model, (uint)sizeof(FeatureDataShaderModel)) < 0)
        {
            model.HighestShaderModel = D3DShaderModel.ShaderModel51;
        }

        FeatureDataD3D12Options options = default;
        device->CheckFeatureSupport(Feature.D3D12Options, &options, (uint)sizeof(FeatureDataD3D12Options));
        FeatureDataD3D12Options5 options5 = default;
        device->CheckFeatureSupport(Feature.D3D12Options5, &options5, (uint)sizeof(FeatureDataD3D12Options5));
        FeatureDataD3D12Options12 options12 = default;
        device->CheckFeatureSupport(Feature.D3D12Options12, &options12, (uint)sizeof(FeatureDataD3D12Options12));
        FeatureDataD3D12Options16 options16 = default;
        device->CheckFeatureSupport(Feature.D3D12Options16, &options16, (uint)sizeof(FeatureDataD3D12Options16));

        int shaderModel = (int)model.HighestShaderModel;
        if (shaderModel < 0x66)
        {
            return $"shader model {shaderModel >> 4}.{shaderModel & 0xF}, and 6.6 is needed";
        }

        if (options.ResourceBindingTier < ResourceBindingTier.Tier3)
        {
            return "resource binding below tier 3";
        }

        if (!options12.EnhancedBarriersSupported)
        {
            return "no enhanced barriers";
        }

        if (!options.TypedUAVLoadAdditionalFormats)
        {
            return "no typed UAV loads of the additional formats";
        }

        capabilities = new D3D12Capabilities(
            shaderModel,
            (int)options5.RaytracingTier,
            options16.GPUUploadHeapSupported,
            0);
        return null;
    }

    private void CreateQueue()
    {
        CommandQueueDesc description = new() { Type = CommandListType.Direct, Priority = (int)CommandQueuePriority.High };
        ID3D12CommandQueue* queue = null;
        Guid iid = ID3D12CommandQueue.Guid;
        D3D12Check.Ok(_device->CreateCommandQueue(&description, &iid, (void**)&queue), "ID3D12Device::CreateCommandQueue");
        _queue = queue;

        ulong frequency = 0;
        D3D12Check.Ok(_queue->GetTimestampFrequency(&frequency), "ID3D12CommandQueue::GetTimestampFrequency");
        Capabilities = Capabilities with { TimestampFrequency = frequency };

        ID3D12Fence* fence = null;
        Guid fenceIid = ID3D12Fence.Guid;
        D3D12Check.Ok(_device->CreateFence(0, FenceFlags.None, &fenceIid, (void**)&fence), "ID3D12Device::CreateFence");
        _fence = fence;
        _fenceEvent = Kernel32.CreateEventW(0, 0, 0, 0);
    }

    private void AttachInfoQueue()
    {
        ID3D12InfoQueue1* queue = null;
        Guid iid = ID3D12InfoQueue1.Guid;
        if (_device->QueryInterface(&iid, (void**)&queue) < 0 || queue is null)
        {
            return;
        }

        _infoQueue = queue;
        _messageSink = _logger;
        uint cookie = 0;
        PfnMessageFunc callback = new(&OnMessage);
        if (_infoQueue->RegisterMessageCallback(callback, MessageCallbackFlags.FlagNone, null, &cookie) >= 0)
        {
            _callbackCookie = cookie;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void OnMessage(MessageCategory category, MessageSeverity severity, MessageID id, byte* description, void* context)
    {
        // Called from the runtime's own threads: nothing escapes into native code.
        try
        {
            IOpenIgiLogger? logger = _messageSink;
            string text = $"{severity} {id}: {Marshal.PtrToStringAnsi((nint)description)}";
            switch (severity)
            {
                case MessageSeverity.Corruption or MessageSeverity.Error:
                    logger?.Error(LogCategory, text);
                    break;
                case MessageSeverity.Warning:
                    logger?.Warning(LogCategory, text);
                    break;
                default:
                    logger?.Debug(LogCategory, text);
                    break;
            }
        }
        catch (Exception)
        {
            // Nowhere to report a logging failure to.
        }
    }
}
