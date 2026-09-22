using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Memory;
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
    private bool _disposed;

    private VulkanRhi(
        VulkanInstance instance,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        VulkanRhiDevice rhi)
    {
        _instance = instance;
        _device = device;
        _allocator = allocator;
        _commands = commands;
        _rhi = rhi;
    }

    /// <summary>Gets the device everything draws through.</summary>
    public IRhiDevice Device => _rhi;

    /// <summary>Gets how many nanoseconds one tick of the device's timestamp counter is.</summary>
    /// <remarks>
    /// Read from the adapter rather than assumed. It differs by an order of magnitude between vendors,
    /// and a wrong one reports every stage's cost wrong by that factor while looking entirely plausible.
    /// </remarks>
    public double TimestampPeriodNanoseconds { get; private set; } = 1.0;

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
        IIgiLogger? logger = null,
        bool enableValidation = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        IIgiLogger log = logger ?? NullLogger.Instance;
        VulkanInstance instance = new(new NoSurface(), enableValidation, log);

        try
        {
            PhysicalDeviceSelection selection = new PhysicalDeviceSelector(instance, log).Select();

            VulkanDevice device = new(instance, selection, log);
            VulkanAllocator allocator = new(device);
            VulkanCommands commands = new(instance.Vk, device);
            VulkanRhiDevice rhi = new(instance.Vk, device, allocator, commands, log);

            rhi.AttachOffscreen(width, height, format);

            return new VulkanRhi(instance, device, allocator, commands, rhi)
            {
                TimestampPeriodNanoseconds = selection.Properties.Limits.TimestampPeriod,
            };
        }
        catch
        {
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
        _rhi.Dispose();
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
