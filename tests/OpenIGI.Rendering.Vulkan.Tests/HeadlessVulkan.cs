using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering.Vulkan;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Rhi;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Xunit;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// A Vulkan device with no window, for the tests that need a real one.
/// </summary>
/// <remarks>
/// The surface seam already allows this: it says a test or a headless host may implement it however it
/// needs to, and what this needs is nothing at all. The instance still enables the surface extension —
/// the backend looks for it — and hands back the null surface, which a device that never presents never
/// touches.
/// </remarks>
internal sealed class HeadlessVulkan : IDisposable
{
    private readonly VulkanInstance _instance;
    private readonly VulkanDevice _device;
    private readonly VulkanAllocator _allocator;
    private readonly VulkanCommands _commands;
    private bool _disposed;

    private HeadlessVulkan(
        VulkanInstance instance,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands)
    {
        _instance = instance;
        _device = device;
        _allocator = allocator;
        _commands = commands;

        Rhi = new VulkanRhiDevice(instance.Vk, device, allocator, commands, NullLogger.Instance);
    }

    /// <summary>Gets the RHI device over it.</summary>
    public VulkanRhiDevice Rhi { get; }

    /// <summary>Gets what the adapter calls itself.</summary>
    public string AdapterName => _device.Selection.Name;

    /// <summary>Opens one, or says why it could not.</summary>
    /// <param name="reason">Why it could not, when it could not.</param>
    /// <returns>The device, or null.</returns>
    public static HeadlessVulkan? TryCreate(out string? reason)
    {
        try
        {
            VulkanInstance instance = new(new NoSurface(), enableValidation: false, NullLogger.Instance);

            try
            {
                PhysicalDeviceSelection selection =
                    new PhysicalDeviceSelector(instance, NullLogger.Instance).Select();

                VulkanDevice device = new(instance, selection, NullLogger.Instance);
                VulkanAllocator allocator = new(device);
                VulkanCommands commands = new(instance.Vk, device);

                reason = null;
                return new HeadlessVulkan(instance, device, allocator, commands);
            }
            catch
            {
                instance.Dispose();
                throw;
            }
        }
        catch (Exception error)
        {
            reason = $"No headless Vulkan device here: {error.Message}";
            return null;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Rhi.Dispose();
        _commands.Dispose();
        _allocator.Dispose();
        _device.Dispose();
        _instance.Dispose();
    }

    /// <summary>A surface provider for a device that never presents.</summary>
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

/// <summary>A fact that runs only where a headless Vulkan device can be had.</summary>
public sealed class VulkanFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> Probe = new(
        () =>
        {
            using HeadlessVulkan? device = HeadlessVulkan.TryCreate(out string? reason);
            return device is null ? reason : null;
        },
        isThreadSafe: true);

    /// <summary>Initialises a new instance of the <see cref="VulkanFactAttribute"/> class.</summary>
    public VulkanFactAttribute() => Skip = Probe.Value;
}
