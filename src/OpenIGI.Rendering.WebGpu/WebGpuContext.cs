using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu.Native;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>
/// An instance, an adapter, a device and its queue: what everything else here is built on.
/// </summary>
/// <remarks>
/// <para>
/// Separated from the RHI device on purpose. Acquiring these is a sequence of asynchronous requests
/// that can fail for reasons worth reporting separately — no adapter at all, an adapter that refuses a
/// device, a driver that does not answer — and keeping it apart means the first thing a new machine
/// runs is also the smallest thing that can be run.
/// </para>
/// <para>
/// Released in the reverse of the order they were acquired. WebGPU reference-counts every object and
/// releasing a device while its queue is still held leaks both, which surfaces as a validation message
/// at process exit rather than anywhere near the cause.
/// </para>
/// </remarks>
public sealed unsafe class WebGpuContext : IDisposable
{
    private WGPUInstance _instance;
    private WGPUAdapter _adapter;
    private WGPUDevice _device;
    private WGPUQueue _queue;
    private bool _disposed;

    private WebGpuContext(
        WGPUInstance instance,
        WGPUAdapter adapter,
        WGPUDevice device,
        WGPUQueue queue,
        WebGpuAdapterReport report)
    {
        _instance = instance;
        _adapter = adapter;
        _device = device;
        _queue = queue;
        Report = report;
    }

    /// <summary>Gets what the adapter said about itself.</summary>
    public WebGpuAdapterReport Report { get; }

    /// <summary>Gets the instance, which owns the event loop the callbacks are pumped through.</summary>
    public WGPUInstance Instance => _instance;

    /// <summary>Gets the adapter the device was made from.</summary>
    public WGPUAdapter Adapter => _adapter;

    /// <summary>Gets the device.</summary>
    public WGPUDevice Device => _device;

    /// <summary>Gets the device's queue, which every submission and every host write goes through.</summary>
    public WGPUQueue Queue => _queue;

    /// <summary>Gets the version of wgpu-native that actually loaded.</summary>
    /// <remarks>
    /// Read from the library rather than from the bindings, so the two can be compared: they are
    /// generated from a pinned header, and a library from a different release does not fail at load but
    /// in the middle of a frame at a structure whose layout has moved.
    /// </remarks>
    public static uint LoadedVersion
    {
        get
        {
            WgpuLibrary.Register();
            return Wgpu.wgpuGetVersion();
        }
    }

    /// <summary>Acquires everything, in order.</summary>
    /// <param name="surface">The surface the adapter must be able to present to, or the null handle.</param>
    /// <param name="lowPower">Whether to prefer the integrated adapter.</param>
    /// <param name="label">A name for the device.</param>
    /// <returns>The context.</returns>
    /// <exception cref="InvalidOperationException">Something along the way refused.</exception>
    public static WebGpuContext Create(
        WGPUSurface surface = default,
        bool lowPower = false,
        string label = "openigi")
    {
        WgpuLibrary.Register();

        WGPUInstance instance = Wgpu.wgpuCreateInstance(null);
        if (!instance.IsValid)
        {
            throw new InvalidOperationException(
                "wgpu-native would not create an instance. The library loaded but reported no instance, "
                + "which usually means no usable graphics backend at all.");
        }

        WGPUAdapter adapter = default;
        WGPUDevice device = default;

        try
        {
            adapter = WebGpuAdapter.Request(instance, surface, lowPower);
            WebGpuAdapterReport report = WebGpuAdapter.Describe(adapter);

            device = WebGpuAdapter.RequestDevice(
                instance,
                adapter,
                WebGpuAdapter.FeaturesToRequest(adapter),
                label);

            WGPUQueue queue = Wgpu.wgpuDeviceGetQueue(device);
            if (!queue.IsValid)
            {
                throw new InvalidOperationException("The device reported no queue.");
            }

            // What the device ended up with, which is not what the adapter offered: a device is given
            // only the features that were asked for, and a limit it was not asked for falls back to the
            // guaranteed minimum. Reading it back is the only way to know what a pass may actually use.
            return new WebGpuContext(instance, adapter, device, queue, report with
            {
                Features = report.Features,
                Limits = DeviceLimits(device, report.Limits),
            });
        }
        catch
        {
            if (device.IsValid)
            {
                Wgpu.wgpuDeviceRelease(device);
            }

            if (adapter.IsValid)
            {
                Wgpu.wgpuAdapterRelease(adapter);
            }

            Wgpu.wgpuInstanceRelease(instance);
            throw;
        }
    }

    /// <summary>Drives the instance's event loop once.</summary>
    /// <remarks>
    /// Called between frames, and while waiting on a readback. Never WaitAny: wgpu-native panics on it,
    /// which arrives as a process abort with a Rust backtrace and no managed exception at all.
    /// </remarks>
    public void ProcessEvents()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Wgpu.wgpuInstanceProcessEvents(_instance);
    }

    /// <summary>Waits until the device has finished everything submitted to it.</summary>
    /// <returns><see langword="true"/> when the queue emptied.</returns>
    public bool Poll(bool wait = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Wgpu.wgpuDevicePoll(_device, wait ? 1u : 0u, null) != 0;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_queue.IsValid)
        {
            Wgpu.wgpuQueueRelease(_queue);
            _queue = default;
        }

        if (_device.IsValid)
        {
            Wgpu.wgpuDeviceRelease(_device);
            _device = default;
        }

        if (_adapter.IsValid)
        {
            Wgpu.wgpuAdapterRelease(_adapter);
            _adapter = default;
        }

        if (_instance.IsValid)
        {
            Wgpu.wgpuInstanceRelease(_instance);
            _instance = default;
        }
    }

    private static RhiLimits DeviceLimits(WGPUDevice device, RhiLimits fallback)
    {
        WGPULimits limits = new();
        return Wgpu.wgpuDeviceGetLimits(device, &limits) == WGPUStatus.Success
            ? WebGpuCapabilities.Limits(limits)
            : fallback;
    }
}
