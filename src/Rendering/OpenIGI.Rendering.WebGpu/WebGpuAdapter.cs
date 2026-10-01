using System.Runtime.InteropServices;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu.Native;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>Everything an adapter turned out to be able to do, and what it calls itself.</summary>
/// <param name="Name">A description of the adapter, for logs and the options screen.</param>
/// <param name="Backend">Which API wgpu-native chose underneath.</param>
/// <param name="Features">The optional things it offers.</param>
/// <param name="Limits">The sizes it will not exceed.</param>
/// <param name="ExtendedStorageFormats">Whether a narrow format may carry a storage binding.</param>
public readonly record struct WebGpuAdapterReport(
    string Name,
    WGPUBackendType Backend,
    RhiFeatures Features,
    RhiLimits Limits,
    bool ExtendedStorageFormats);

/// <summary>
/// Asking for an adapter and a device, both of which arrive by callback.
/// </summary>
/// <remarks>
/// <para>
/// WebGPU has no synchronous way to get either: the request returns a future and the answer comes back
/// through a function pointer. On the desktop there is nothing useful to do in between, so this drives
/// the instance's own event loop until the callback has fired.
/// </para>
/// <para>
/// <b>Driven with ProcessEvents rather than WaitAny.</b> WaitAny is the shape the specification
/// intends and wgpu-native panics on it, which arrives as a process abort with a Rust backtrace and no
/// managed exception at all. ProcessEvents is the path it actually supports, so every callback here is
/// registered as AllowProcessEvents and pumped in a loop.
/// </para>
/// <para>
/// The callbacks are static methods passed as function pointers, and the box they write into reaches
/// them as a handle rather than a captured reference: a delegate would have to be kept alive by the
/// managed side for exactly as long as the native side might call it, and a missed root is a crash
/// minutes later in unrelated code.
/// </para>
/// </remarks>
public static unsafe class WebGpuAdapter
{
    /// <summary>How long to pump the instance before giving up on a request.</summary>
    /// <remarks>
    /// Generous, because an adapter request can genuinely take a second on a cold driver, and finite
    /// because a request that never completes would otherwise hang the process with no diagnosis.
    /// </remarks>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>What the callback writes and the caller reads.</summary>
    private sealed class Answer
    {
        public nint Handle { get; set; }

        public int Status { get; set; }

        public string Message { get; set; } = string.Empty;

        public bool Completed { get; set; }
    }

    /// <summary>Asks for an adapter.</summary>
    /// <param name="instance">The instance to ask.</param>
    /// <param name="surface">The surface it must be able to present to, or the null handle for none.</param>
    /// <param name="lowPower">Whether to prefer the integrated adapter over the discrete one.</param>
    /// <returns>The adapter.</returns>
    /// <exception cref="InvalidOperationException">No adapter answered.</exception>
    public static WGPUAdapter Request(WGPUInstance instance, WGPUSurface surface, bool lowPower = false)
    {
        Answer answer = new();
        GCHandle handle = GCHandle.Alloc(answer);

        try
        {
            WGPURequestAdapterOptions options = new()
            {
                powerPreference = lowPower ? WGPUPowerPreference.LowPower : WGPUPowerPreference.HighPerformance,
                compatibleSurface = surface,
            };

            WGPURequestAdapterCallbackInfo info = new()
            {
                mode = WGPUCallbackMode.AllowProcessEvents,
                callback = &OnAdapter,
                userdata1 = (void*)GCHandle.ToIntPtr(handle),
            };

            Wgpu.wgpuInstanceRequestAdapter(instance, &options, info);
            Pump(instance, answer, "adapter");

            if (answer.Status != (int)WGPURequestAdapterStatus.Success || answer.Handle == 0)
            {
                throw new InvalidOperationException(
                    $"No WebGPU adapter: {(WGPURequestAdapterStatus)answer.Status}. {answer.Message}".TrimEnd());
            }

            return new WGPUAdapter(answer.Handle);
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>Asks an adapter for a device, requesting the features the renderer can use.</summary>
    /// <param name="instance">The instance, to pump while waiting.</param>
    /// <param name="adapter">The adapter to ask.</param>
    /// <param name="features">The features to request, each of which the adapter must have.</param>
    /// <param name="label">A name for the device.</param>
    /// <returns>The device.</returns>
    /// <exception cref="InvalidOperationException">No device answered.</exception>
    /// <remarks>
    /// The limits requested are the adapter's own, so nothing is left on the table: WebGPU otherwise
    /// gives a device the guaranteed minimums whatever the hardware can do, and a pass sized from
    /// reported limits would then fail to create against limits nobody asked for.
    /// </remarks>
    public static WGPUDevice RequestDevice(
        WGPUInstance instance,
        WGPUAdapter adapter,
        ReadOnlySpan<WGPUFeatureName> features,
        string label = "openigi")
    {
        Answer answer = new();
        GCHandle handle = GCHandle.Alloc(answer);
        using WebGpuString name = new(label);

        try
        {
            WGPULimits limits = new();
            if (Wgpu.wgpuAdapterGetLimits(adapter, &limits) != WGPUStatus.Success)
            {
                throw new InvalidOperationException("The adapter would not report its limits.");
            }

            fixed (WGPUFeatureName* required = features)
            {
                WGPUDeviceDescriptor descriptor = new()
                {
                    label = name.View,
                    requiredFeatureCount = (nuint)features.Length,
                    requiredFeatures = required,
                    requiredLimits = &limits,
                    deviceLostCallbackInfo = new WGPUDeviceLostCallbackInfo
                    {
                        mode = WGPUCallbackMode.AllowProcessEvents,
                    },
                };

                WGPURequestDeviceCallbackInfo info = new()
                {
                    mode = WGPUCallbackMode.AllowProcessEvents,
                    callback = &OnDevice,
                    userdata1 = (void*)GCHandle.ToIntPtr(handle),
                };

                Wgpu.wgpuAdapterRequestDevice(adapter, &descriptor, info);
            }

            Pump(instance, answer, "device");

            if (answer.Status != (int)WGPURequestDeviceStatus.Success || answer.Handle == 0)
            {
                throw new InvalidOperationException(
                    $"No WebGPU device: {(WGPURequestDeviceStatus)answer.Status}. {answer.Message}".TrimEnd());
            }

            return new WGPUDevice(answer.Handle);
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>Reads what an adapter says about itself.</summary>
    /// <param name="adapter">The adapter.</param>
    /// <returns>Its name, its backend, and what it can do.</returns>
    public static WebGpuAdapterReport Describe(WGPUAdapter adapter)
    {
        WGPUAdapterInfo info = new();
        string name = "WebGPU";
        WGPUBackendType backend = WGPUBackendType.Undefined;

        if (Wgpu.wgpuAdapterGetInfo(adapter, &info) == WGPUStatus.Success)
        {
            string device = WebGpuString.Read(info.device);
            string vendor = WebGpuString.Read(info.vendor);
            backend = info.backendType;

            // The two often repeat each other -- a 3080 reports vendor "NVIDIA" and device "NVIDIA
            // GeForce RTX 3080" -- and a name that says it twice reads as a bug in the log.
            name = string.IsNullOrEmpty(vendor)
                || device.StartsWith(vendor, StringComparison.OrdinalIgnoreCase)
                    ? device
                    : $"{vendor} {device}".Trim();

            name = string.IsNullOrEmpty(name) ? "WebGPU" : name;

            Wgpu.wgpuAdapterInfoFreeMembers(info);
        }

        WGPUSupportedFeatures supported = new();
        Wgpu.wgpuAdapterGetFeatures(adapter, &supported);

        ReadOnlySpan<WGPUFeatureName> features = new(supported.features, (int)supported.featureCount);
        bool rayQuery = Wgpu.wgpuAdapterHasFeature(adapter, (WGPUFeatureName)WGPUNativeFeature.RayQuery) != 0;

        WebGpuAdapterReport report = new(
            name,
            backend,
            WebGpuCapabilities.Features(features, rayQuery),
            ReadLimits(adapter),
            WebGpuCapabilities.HasExtendedStorageFormats(features));

        Wgpu.wgpuSupportedFeaturesFreeMembers(supported);
        return report;
    }

    /// <summary>The features to ask a device for, being those the adapter has and the renderer uses.</summary>
    /// <param name="adapter">The adapter.</param>
    /// <returns>The features to request.</returns>
    /// <remarks>
    /// Asked for one at a time rather than taken wholesale: requesting a feature the adapter does not
    /// have fails the whole device, and requesting one nothing uses only narrows which adapters work.
    /// </remarks>
    public static WGPUFeatureName[] FeaturesToRequest(WGPUAdapter adapter)
    {
        WGPUFeatureName[] wanted =
        [
            WGPUFeatureName.TimestampQuery,
            WGPUFeatureName.Float32Filterable,
            WGPUFeatureName.DepthClipControl,
            WGPUFeatureName.IndirectFirstInstance,
            WGPUFeatureName.TextureCompressionBC,
            WGPUFeatureName.TextureFormatsTier1,
            WGPUFeatureName.RG11B10UfloatRenderable,
        ];

        List<WGPUFeatureName> available = [];
        foreach (WGPUFeatureName feature in wanted)
        {
            if (Wgpu.wgpuAdapterHasFeature(adapter, feature) != 0)
            {
                available.Add(feature);
            }
        }

        return [.. available];
    }

    private static RhiLimits ReadLimits(WGPUAdapter adapter)
    {
        WGPULimits limits = new();
        return Wgpu.wgpuAdapterGetLimits(adapter, &limits) == WGPUStatus.Success
            ? WebGpuCapabilities.Limits(limits)
            : RhiLimits.WebGpuDefaults;
    }

    /// <summary>Drives the instance until a request's callback has fired.</summary>
    /// <exception cref="TimeoutException">It never fired.</exception>
    private static void Pump(WGPUInstance instance, Answer answer, string what)
    {
        long deadline = Environment.TickCount64 + (long)RequestTimeout.TotalMilliseconds;

        while (!answer.Completed)
        {
            Wgpu.wgpuInstanceProcessEvents(instance);

            if (answer.Completed)
            {
                return;
            }

            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException(
                    $"The WebGPU {what} request did not complete within {RequestTimeout.TotalSeconds:0} seconds.");
            }

            Thread.Sleep(0);
        }
    }

    /// <summary>Reads the box a callback's userdata points at.</summary>
    private static Answer? Unbox(void* userdata) =>
        userdata is null ? null : GCHandle.FromIntPtr((nint)userdata).Target as Answer;

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void OnAdapter(
        WGPURequestAdapterStatus status,
        WGPUAdapter adapter,
        WGPUStringView message,
        void* userdata1,
        void* userdata2)
    {
        if (Unbox(userdata1) is not { } answer)
        {
            return;
        }

        answer.Status = (int)status;
        answer.Handle = adapter.Handle;
        answer.Message = WebGpuString.Read(message);
        answer.Completed = true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void OnDevice(
        WGPURequestDeviceStatus status,
        WGPUDevice device,
        WGPUStringView message,
        void* userdata1,
        void* userdata2)
    {
        if (Unbox(userdata1) is not { } answer)
        {
            return;
        }

        answer.Status = (int)status;
        answer.Handle = device.Handle;
        answer.Message = WebGpuString.Read(message);
        answer.Completed = true;
    }
}
