using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Capabilities;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Queues;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// The logical device: the queues the backend submits to, the device-level extensions it enabled, and
/// the memory properties every allocation is measured against.
/// </summary>
/// <remarks>
/// A logical device is the handle every resource is created against and every command is submitted
/// through. It is created once and lives for the backend's whole life — unlike the swapchain, which is
/// rebuilt on resize — so the queues, the enabled features and the memory properties are read here once
/// and shared out.
/// </remarks>
internal sealed unsafe class VulkanDevice : IDisposable
{
    private readonly Vk _vk;
    private readonly Instance _instance;
    private readonly IIgiLogger _logger;
    private readonly bool _deviceFaultEnabled;
    private readonly List<VulkanQueue> _queues = [];
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
        _instance = instance.Instance;
        _logger = logger;
        _deviceFaultEnabled = selection.Capabilities.EnabledExtensions.Contains("VK_EXT_device_fault");
        Selection = selection;
        Capabilities = selection.Capabilities;

        _vk.GetPhysicalDeviceMemoryProperties(selection.Device, out PhysicalDeviceMemoryProperties memory);
        MemoryProperties = memory;
        SamplerAnisotropy = Capabilities.SamplerAnisotropy;

        Device = CreateDevice(selection);

        VulkanQueuePlan plan = selection.Queues;
        GraphicsQueue = CreateQueue(plan.Graphics, VulkanQueueKind.Graphics);
        PresentQueue = plan.Present == plan.Graphics
            ? GraphicsQueue
            : CreateQueue(plan.Present, VulkanQueueKind.Graphics);
        AsyncComputeQueue = plan.HasDedicatedCompute
            ? CreateQueue(plan.AsyncCompute, VulkanQueueKind.AsyncCompute)
            : GraphicsQueue;
        TransferQueue = plan.HasDedicatedTransfer
            ? CreateQueue(plan.Transfer, VulkanQueueKind.Transfer)
            : GraphicsQueue;

        if (!_vk.TryGetDeviceExtension(instance.Instance, Device, out KhrSwapchain swapchain))
        {
            throw new GraphicsDeviceException("VK_KHR_swapchain is not available on the created device.");
        }

        Swapchain = swapchain;

        if (Capabilities.Synchronization2
            && _vk.TryGetDeviceExtension(instance.Instance, Device, out KhrSynchronization2 sync2))
        {
            Synchronization2 = sync2;
        }

        if (Capabilities.TimelineSemaphore
            && _vk.TryGetDeviceExtension(instance.Instance, Device, out KhrTimelineSemaphore timeline))
        {
            TimelineSemaphores = timeline;
        }

        logger.Information("Vulkan", $"Created logical device on '{selection.Name}'.");
    }

    /// <summary>Gets the chosen adapter and its selection facts.</summary>
    public PhysicalDeviceSelection Selection { get; }

    /// <summary>Gets which optional features the device was created with.</summary>
    public VulkanDeviceCapabilities Capabilities { get; }

    /// <summary>Gets the physical device the logical device runs on.</summary>
    public PhysicalDevice PhysicalDevice => Selection.Device;

    /// <summary>Gets the logical device.</summary>
    public Device Device { get; }

    /// <summary>Gets the queue drawing is submitted to.</summary>
    public VulkanQueue GraphicsQueue { get; }

    /// <summary>Gets the queue images are presented from.</summary>
    public VulkanQueue PresentQueue { get; }

    /// <summary>
    /// Gets the queue long-running compute is submitted to — a dedicated engine where the adapter has
    /// one, and the graphics queue where it does not.
    /// </summary>
    public VulkanQueue AsyncComputeQueue { get; }

    /// <summary>Gets the queue bulk uploads are submitted to.</summary>
    public VulkanQueue TransferQueue { get; }

    /// <summary>Gets every distinct queue the device created, for teardown and for polling completion.</summary>
    public IReadOnlyList<VulkanQueue> Queues => _queues;

    /// <summary>Gets the raw graphics queue handle.</summary>
    /// <remarks>For the paths that still submit directly rather than through a payload.</remarks>
    public Queue Graphics => GraphicsQueue.Handle;

    /// <summary>Gets the raw present queue handle.</summary>
    public Queue Present => PresentQueue.Handle;

    /// <summary>Gets the raw asynchronous-compute queue handle.</summary>
    public Queue Compute => AsyncComputeQueue.Handle;

    /// <summary>Gets the swapchain extension loaded for the device.</summary>
    public KhrSwapchain Swapchain { get; }

    /// <summary>Gets the synchronisation-2 entry points, or <see langword="null"/> when unavailable.</summary>
    public KhrSynchronization2? Synchronization2 { get; }

    /// <summary>Gets the timeline-semaphore entry points, or <see langword="null"/> when unavailable.</summary>
    public KhrTimelineSemaphore? TimelineSemaphores { get; }

    /// <summary>Gets the device's memory heaps and types, for choosing where an allocation lives.</summary>
    public PhysicalDeviceMemoryProperties MemoryProperties { get; }

    /// <summary>Gets a value indicating whether anisotropic sampling was enabled.</summary>
    public bool SamplerAnisotropy { get; }

    /// <summary>Waits until the device has finished every outstanding submission.</summary>
    /// <remarks>
    /// A coarse barrier, for teardown and swapchain rebuild rather than per frame: it is the only safe
    /// point at which resources a command buffer might still reference can be destroyed. Per-frame
    /// completion is tracked through each queue's timeline instead.
    /// </remarks>
    /// <summary>Logs whatever the driver recorded about a device loss, when the fault extension is on.</summary>
    /// <remarks>
    /// Called the instant a submit or wait returns <c>ErrorDeviceLost</c>, before the exception unwinds.
    /// A hang leaves no address behind and reports only a vendor code or nothing; a real fault reports the
    /// address it read, wrote or executed at, and whether it was invalid -- which is what tells a timeout
    /// apart from an out-of-bounds and points at the resource behind it.
    /// </remarks>
    public unsafe void ReportDeviceFault()
    {
        if (!_deviceFaultEnabled)
        {
            _logger.Warning("Vulkan", "Device lost; VK_EXT_device_fault is not enabled, so no fault detail is available.");
            return;
        }

        if (!_vk.TryGetDeviceExtension(_instance, Device, out ExtDeviceFault ext))
        {
            return;
        }

        // Pre-size the arrays to a capacity and read in one call, rather than the counts-then-info two
        // step -- the null-info counts call marshalled badly and faulted the process before it could say
        // anything at all.
        const int capacity = 256;
        DeviceFaultAddressInfoEXT* addresses = stackalloc DeviceFaultAddressInfoEXT[capacity];
        DeviceFaultVendorInfoEXT* vendors = stackalloc DeviceFaultVendorInfoEXT[capacity];

        DeviceFaultCountsEXT counts = new()
        {
            SType = StructureType.DeviceFaultCountsExt,
            AddressInfoCount = capacity,
            VendorInfoCount = capacity,
        };

        DeviceFaultInfoEXT info = new()
        {
            SType = StructureType.DeviceFaultInfoExt,
            PAddressInfos = addresses,
            PVendorInfos = vendors,
        };

        Result read = ext.GetDeviceFaultInfo(Device, &counts, &info);
        if (read != Result.Success && read != Result.Incomplete)
        {
            _logger.Warning("Vulkan", $"vkGetDeviceFaultInfoEXT returned {read}.");
            return;
        }

        string description = new((sbyte*)info.Description);
        _logger.Error("Vulkan", $"DEVICE FAULT: '{description}' ({counts.AddressInfoCount} address(es), {counts.VendorInfoCount} vendor code(s)).");

        for (int i = 0; i < (int)Math.Min(counts.AddressInfoCount, (uint)capacity); i++)
        {
            DeviceFaultAddressInfoEXT a = addresses[i];
            _logger.Error(
                "Vulkan",
                $"  fault address[{i}]: type={a.AddressType} at 0x{a.ReportedAddress:X} (precision mask 0x{a.AddressPrecision:X}).");
        }

        for (int i = 0; i < (int)Math.Min(counts.VendorInfoCount, (uint)capacity); i++)
        {
            DeviceFaultVendorInfoEXT v = vendors[i];
            _logger.Error(
                "Vulkan",
                $"  vendor fault[{i}]: code=0x{v.VendorFaultCode:X} data=0x{v.VendorFaultData:X}.");
        }
    }

    public void WaitIdle() => _vk.DeviceWaitIdle(Device);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (VulkanQueue queue in _queues)
        {
            queue.Dispose();
        }

        _queues.Clear();
        TimelineSemaphores?.Dispose();
        Synchronization2?.Dispose();
        Swapchain.Dispose();
        _vk.DestroyDevice(Device, null);
    }

    private VulkanQueue CreateQueue(VulkanQueueSlot slot, VulkanQueueKind kind)
    {
        VulkanQueue queue = new(_vk, this, slot, kind);
        _queues.Add(queue);
        return queue;
    }

    private Device CreateDevice(PhysicalDeviceSelection selection)
    {
        // One create-info per distinct family, so a unified adapter asks for a single queue and a split
        // one asks for exactly the families it actually uses.
        IReadOnlyDictionary<uint, uint> queueCounts = selection.Queues.QueueCounts;

        float priority = 1.0f;
        DeviceQueueCreateInfo[] queueInfos = new DeviceQueueCreateInfo[queueCounts.Count];
        int index = 0;
        foreach ((uint family, uint count) in queueCounts)
        {
            queueInfos[index++] = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = family,
                QueueCount = count,
                PQueuePriorities = &priority,
            };
        }

        // The chain must outlive the create call, so it is built here and disposed only afterwards; every
        // structure in it is one the probe confirmed this adapter implements.
        using VulkanStructChain chain = new();
        PhysicalDeviceFeatures features =
            VulkanCapabilityProbe.BuildEnabledFeatures(selection.Capabilities, chain, selection.ApiVersion);

        nint extensionNames = SilkMarshal.StringArrayToPtr(selection.DeviceExtensions);
        try
        {
            fixed (DeviceQueueCreateInfo* queues = queueInfos)
            {
                DeviceCreateInfo createInfo = new()
                {
                    SType = StructureType.DeviceCreateInfo,
                    PNext = chain.Head,
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
}
