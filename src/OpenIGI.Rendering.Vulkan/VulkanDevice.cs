using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// The logical device: the queues the backend submits to, the device-level extensions it enabled, and
/// the memory properties every allocation is measured against.
/// </summary>
/// <remarks>
/// A logical device is the handle every resource is created against and every command is submitted
/// through. It is created once and lives for the backend's whole life — unlike the swapchain, which is
/// rebuilt on resize — so the queues and the memory properties are read here once and shared out.
/// </remarks>
internal sealed unsafe class VulkanDevice : IDisposable
{
    private readonly Vk _vk;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanDevice"/> class.</summary>
    /// <param name="instance">The instance the device is created under.</param>
    /// <param name="selection">The chosen adapter and how it will be used.</param>
    /// <param name="logger">Where to report device creation.</param>
    /// <exception cref="GraphicsDeviceException">The logical device could not be created.</exception>
    public VulkanDevice(VulkanInstance instance, PhysicalDeviceSelection selection, IIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(selection);
        _vk = instance.Vk;
        Selection = selection;

        _vk.GetPhysicalDeviceMemoryProperties(selection.Device, out PhysicalDeviceMemoryProperties memory);
        MemoryProperties = memory;

        _vk.GetPhysicalDeviceFeatures(selection.Device, out PhysicalDeviceFeatures supported);
        SamplerAnisotropy = supported.SamplerAnisotropy;

        Device = CreateDevice(selection, supported);

        Graphics = GetQueue(selection.Queues.Graphics);
        Present = GetQueue(selection.Queues.Present);
        Compute = GetQueue(selection.Queues.Compute);

        if (!_vk.TryGetDeviceExtension(instance.Instance, Device, out KhrSwapchain swapchain))
        {
            throw new GraphicsDeviceException("VK_KHR_swapchain is not available on the created device.");
        }

        Swapchain = swapchain;
        logger.Information("Vulkan", $"Created logical device on '{selection.Name}'.");
    }

    /// <summary>Gets the chosen adapter and its selection facts.</summary>
    public PhysicalDeviceSelection Selection { get; }

    /// <summary>Gets the physical device the logical device runs on.</summary>
    public PhysicalDevice PhysicalDevice => Selection.Device;

    /// <summary>Gets the logical device.</summary>
    public Device Device { get; }

    /// <summary>Gets the graphics queue.</summary>
    public Queue Graphics { get; }

    /// <summary>Gets the present queue. Equal to <see cref="Graphics"/> on a unified adapter.</summary>
    public Queue Present { get; }

    /// <summary>Gets the compute queue. Equal to <see cref="Graphics"/> when no separate family exists.</summary>
    public Queue Compute { get; }

    /// <summary>Gets the swapchain extension loaded for the device.</summary>
    public KhrSwapchain Swapchain { get; }

    /// <summary>Gets the device's memory heaps and types, for choosing where an allocation lives.</summary>
    public PhysicalDeviceMemoryProperties MemoryProperties { get; }

    /// <summary>Gets a value indicating whether anisotropic sampling was enabled.</summary>
    public bool SamplerAnisotropy { get; }

    /// <summary>Waits until the device has finished every outstanding submission.</summary>
    /// <remarks>
    /// A coarse barrier, for teardown and swapchain rebuild rather than per frame: it is the only safe
    /// point at which resources a command buffer might still reference can be destroyed.
    /// </remarks>
    public void WaitIdle() => _vk.DeviceWaitIdle(Device);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Swapchain.Dispose();
        _vk.DestroyDevice(Device, null);
    }

    private Device CreateDevice(PhysicalDeviceSelection selection, PhysicalDeviceFeatures supported)
    {
        // One create-info per distinct family, so a unified adapter asks for a single queue and a split
        // one asks for exactly the families it actually uses.
        HashSet<uint> families =
        [
            selection.Queues.Graphics,
            selection.Queues.Present,
            selection.Queues.Compute,
        ];

        float priority = 1.0f;
        DeviceQueueCreateInfo[] queueInfos = new DeviceQueueCreateInfo[families.Count];
        int index = 0;
        foreach (uint family in families)
        {
            queueInfos[index++] = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = family,
                QueueCount = 1,
                PQueuePriorities = &priority,
            };
        }

        // Only the features the backend actually uses are turned on. Anisotropy is the one the texture
        // sampler wants, and only if the adapter offers it. Robust buffer access makes an out-of-bounds
        // read return zero rather than lose the device — a safety net while the draw paths are brought
        // up, so a bad index is a glitch to see and fix, not a GPU reset.
        PhysicalDeviceFeatures features = new()
        {
            SamplerAnisotropy = supported.SamplerAnisotropy,
            RobustBufferAccess = supported.RobustBufferAccess,
        };

        // When robustness2 is available, chain its features so out-of-bounds reads across every buffer
        // kind — index buffers included — return zero rather than lose the device.
        bool robustness2 = selection.DeviceExtensions.Contains("VK_EXT_robustness2");
        PhysicalDeviceRobustness2FeaturesEXT robustness2Features = new()
        {
            SType = StructureType.PhysicalDeviceRobustness2FeaturesExt,
            RobustBufferAccess2 = true,
            RobustImageAccess2 = true,
        };

        nint extensionNames = SilkMarshal.StringArrayToPtr(selection.DeviceExtensions);
        try
        {
            fixed (DeviceQueueCreateInfo* queues = queueInfos)
            {
                DeviceCreateInfo createInfo = new()
                {
                    SType = StructureType.DeviceCreateInfo,
                    PNext = robustness2 ? &robustness2Features : null,
                    QueueCreateInfoCount = (uint)queueInfos.Length,
                    PQueueCreateInfos = queues,
                    PEnabledFeatures = &features,
                    EnabledExtensionCount = (uint)selection.DeviceExtensions.Count,
                    PpEnabledExtensionNames = (byte**)extensionNames,
                };

                Device device;
                VkCheck.Ok(_vk.CreateDevice(selection.Device, &createInfo, null, &device), "vkCreateDevice");
                return device;
            }
        }
        finally
        {
            SilkMarshal.Free(extensionNames);
        }
    }

    private Queue GetQueue(uint family)
    {
        _vk.GetDeviceQueue(Device, family, 0, out Queue queue);
        return queue;
    }
}
