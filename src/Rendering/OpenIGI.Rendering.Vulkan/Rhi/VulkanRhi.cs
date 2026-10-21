using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Streamline;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Streamline;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace OpenIGI.Rendering.Vulkan.Rhi;

/// <summary>
/// A Vulkan device behind the RHI, with everything it is built from held together.
/// </summary>
/// <remarks>
/// An instance, a physical device, an allocator and a command pool are four objects that have to be
/// created in order and destroyed in the opposite one, and nothing above the backend should have to
/// know that. This is the one public way in: ask for a device, use it, dispose it.
/// </remarks>
public sealed class VulkanRhi : IDisposable
{
    private readonly VulkanInstance _instance;
    private readonly VulkanDevice _device;
    private readonly VulkanAllocator _allocator;
    private readonly VulkanCommands _commands;
    private readonly VulkanRhiDevice _rhi;
    private readonly VulkanSwapchain? _swapchain;
    private readonly StreamlineRuntime? _streamlineRuntime;
    private readonly StreamlineSession? _streamline;
    private bool _disposed;

    /// <summary>The features every Streamline session asks for.</summary>
    /// <remarks>
    /// All of them at once, because what <c>slInit</c> is told to load decides what Streamline's own
    /// <c>vkCreateDevice</c> adds to the device — Reflex's <c>VK_NV_low_latency2</c>, frame generation's
    /// extra queues — and the device cannot be changed afterwards. Which of them then runs is the
    /// renderer's choice, frame by frame.
    /// </remarks>
    private static readonly uint[] StreamlineFeatures =
    [
        SlFeature.Dlss,
        SlFeature.DlssRr,
        SlFeature.DlssG,
        SlFeature.Reflex,
        SlFeature.Pcl,
    ];

    private VulkanRhi(
        VulkanInstance instance,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        VulkanRhiDevice rhi,
        VulkanSwapchain? swapchain = null,
        StreamlineRuntime? streamlineRuntime = null,
        StreamlineSession? streamline = null)
    {
        _instance = instance;
        _device = device;
        _allocator = allocator;
        _commands = commands;
        _rhi = rhi;
        _swapchain = swapchain;
        _streamlineRuntime = streamlineRuntime;
        _streamline = streamline;
    }

    /// <summary>Gets the device everything draws through.</summary>
    public IRhiDevice Device => _rhi;

    /// <summary>Gets how many nanoseconds one tick of the device's timestamp counter is.</summary>
    public double TimestampPeriodNanoseconds => _rhi.TimestampPeriodNanoseconds;

    /// <summary>Opens a device with no window, drawing into textures of its own.</summary>
    /// <param name="width">The frame's width in pixels.</param>
    /// <param name="height">Its height.</param>
    /// <param name="format">What the frame is drawn in.</param>
    /// <param name="logger">Where the backend reports what it found.</param>
    /// <param name="enableValidation">Whether to load the validation layers.</param>
    /// <returns>The device.</returns>
    /// <remarks>
    /// What a headless capture draws through, and what lets the whole renderer be run and read back on
    /// a machine with no display attached at all.
    /// </remarks>
    public static VulkanRhi CreateOffscreen(
        int width,
        int height,
        RhiTextureFormat format,
        IOpenIgiLogger? logger = null,
        bool enableValidation = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        IOpenIgiLogger log = logger ?? NullLogger.Instance;
        VulkanInstance instance = new(new NoSurface(), enableValidation, log);

        try
        {
            PhysicalDeviceSelection selection = new PhysicalDeviceSelector(instance, log).Select();

            VulkanDevice device = new(instance, selection, log);
            VulkanAllocator allocator = new(device);
            VulkanCommands commands = new(instance.Vk, device);
            VulkanRhiDevice rhi = new(instance.Vk, device, allocator, commands, log, instance);

            rhi.AttachOffscreen(width, height, format);

            return new VulkanRhi(instance, device, allocator, commands, rhi);
        }
        catch
        {
            instance.Dispose();
            throw;
        }
    }

    /// <summary>Opens a device that presents to a window's surface.</summary>
    /// <param name="surface">Where the window's surface comes from.</param>
    /// <param name="width">The surface's width in pixels.</param>
    /// <param name="height">Its height.</param>
    /// <param name="vsync">Whether to wait for the display.</param>
    /// <param name="logger">Where the backend reports what it found.</param>
    /// <param name="enableValidation">Whether to load the validation layers.</param>
    /// <param name="streamline">
    /// Where NVIDIA's Streamline runtime is, or <see langword="null"/> to run without it. When it starts,
    /// the instance and device are created through it and <see cref="IRhiDevice.Streamline"/> is set;
    /// when it does not, the device is exactly what it would have been and the log says why.
    /// </param>
    /// <returns>The device.</returns>
    public static VulkanRhi CreateForSurface(
        IVulkanSurfaceProvider surface,
        int width,
        int height,
        bool vsync,
        IOpenIgiLogger? logger = null,
        bool enableValidation = false,
        StreamlineOptions? streamline = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        IOpenIgiLogger log = logger ?? NullLogger.Instance;

        // Before the instance, which is the whole reason it is here: Streamline's own instance and device
        // creation add what its plugins need, and a device created any other way is one it cannot use.
        StreamlineRuntime? runtime = streamline is null
            ? null
            : StreamlineRuntime.TryCreate(streamline, StreamlineFeatures, log, SlRenderApi.Vulkan);

        VulkanInstance instance;
        try
        {
            instance = new(
                surface,
                enableValidation,
                log,
                runtime is null ? null : StreamlineVulkanLoader.CreateApi(runtime.InterposerPath));
        }
        catch
        {
            runtime?.Dispose();
            throw;
        }

        try
        {
            runtime?.LogFeatureRequirements();
            PhysicalDeviceSelection selection =
                new PhysicalDeviceSelector(instance, log, forStreamline: runtime is not null).Select();

            VulkanDevice device = new(instance, selection, log);
            VulkanAllocator allocator = new(device);
            VulkanCommands commands = new(instance.Vk, device);

            // Frame generation's plugin is unloaded before the first swapchain unless it was asked for:
            // while loaded, the host renders off-screen and pays a copy every frame even with it off.
            if (runtime is not null
                && streamline is { FrameGeneration: false }
                && runtime.IsFeatureSupported(SlFeature.DlssG, SlAdapterInfo.ForVulkan(selection.Device.Handle)) == SlResult.Ok)
            {
                _ = runtime.SetFeatureLoaded(SlFeature.DlssG, false);
            }

            // Unsigned-normalised. The renderer's resolve writes for either kind, but its interface
            // composites at the display encoding and blends there, as the original did; over an sRGB
            // swapchain it blended in linear space instead, and every dark panel of the menu came out grey.
            VulkanSwapchain swapchain = new(instance, device, width, height, vsync);
            VulkanRhiDevice rhi = new(instance.Vk, device, allocator, commands, log, instance);

            rhi.AttachSwapchain(swapchain);

            StreamlineSession? seam = null;
            if (runtime is not null)
            {
                VulkanStreamlineAdapter adapter = new(rhi, selection.Device);
                seam = new StreamlineSession(runtime, adapter, log);
                rhi.AttachStreamline(seam, adapter);
            }

            return new VulkanRhi(instance, device, allocator, commands, rhi, swapchain, runtime, seam);
        }
        catch
        {
            runtime?.Dispose();
            instance.Dispose();
            throw;
        }
    }

    /// <summary>Gets the texture a frame drawn offscreen landed in.</summary>
    /// <param name="index">The frame's index, as it was handed out.</param>
    /// <returns>The texture.</returns>
    public IRhiTexture OffscreenTarget(int index) => _rhi.OffscreenTarget(index);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // The opposite order to construction, which is the only order that works: the pool and the
        // allocator name the device, and the device names the instance.
        _rhi.DetachStreamline();
        _streamline?.Dispose();
        _rhi.Dispose();
        _swapchain?.Dispose();

        // Streamline goes after the swapchain, whose destruction it intercepts, and before the device:
        // its shutdown tears NGX down against that device, and doing it afterwards faults inside the
        // driver with nothing in the log.
        _streamlineRuntime?.Dispose();
        _commands.Dispose();
        _allocator.Dispose();
        _device.Dispose();
        _instance.Dispose();
    }

    /// <summary>An instance with no window behind it, for a device that only ever draws offscreen.</summary>
    private sealed class NoSurface : IVulkanSurfaceProvider
    {
        /// <inheritdoc/>
        /// <remarks>
        /// The surface extension is still asked for, because the backend loads it and would refuse a
        /// device without it. Nothing is created from it.
        /// </remarks>
        public IReadOnlyList<string> GetRequiredInstanceExtensions() => ["VK_KHR_surface"];

        /// <inheritdoc/>
        public SurfaceKHR CreateSurface(Vk vk, Instance instance, KhrSurface khrSurface) => default;
    }
}
