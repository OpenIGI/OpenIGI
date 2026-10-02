using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Capabilities;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Queues;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using System.Reflection;

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
    private readonly Pipelines.VulkanPipelineCache _pipelineCache;
    private readonly bool _deviceFaultEnabled;
    private nint _cmdSetCheckpoint;
    private nint _getQueueCheckpointData;
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
        SamplerMipLodBias = Capabilities.SamplerMipLodBias;

        Device = CreateDevice(selection);
        LoadDiagnosticCheckpoints();

        VulkanQueuePlan plan = selection.Queues;
        Graphics = QueueAt(plan.Graphics);
        Present = plan.GraphicsAndPresentUnified ? Graphics : QueueAt(plan.Present);

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

        // After the device and before anything builds a pipeline against it. Every pipeline in the
        // backend is created through this, which is what lets the driver hand back what it compiled on
        // a previous run instead of compiling it again on the frame that first needs it.
        _pipelineCache = new Pipelines.VulkanPipelineCache(_vk, Device, selection.Properties, logger);

        logger.Information("Vulkan", $"Created logical device on '{selection.Name}'.");
        LogMemoryBudget("at start-up");
    }

    /// <summary>Gets the cache every pipeline creation is built through.</summary>
    /// <remarks>
    /// One per device, reached from the device rather than threaded through every pipeline class's
    /// constructor -- they all already take a <see cref="VulkanDevice"/>, and a driver's compiled
    /// output belongs to the device it was compiled for.
    /// </remarks>
    public PipelineCache PipelineCache => _pipelineCache.Handle;

    /// <summary>Gets the chosen adapter and its selection facts.</summary>
    public PhysicalDeviceSelection Selection { get; }

    /// <summary>Gets which optional features the device was created with.</summary>
    public VulkanDeviceCapabilities Capabilities { get; }

    /// <summary>Gets the physical device the logical device runs on.</summary>
    public PhysicalDevice PhysicalDevice => Selection.Device;

    /// <summary>Gets the logical device.</summary>
    public Device Device { get; }

    /// <summary>Gets the queue every submission goes to.</summary>
    public Queue Graphics { get; }

    /// <summary>Gets the queue images are presented from: the graphics queue, where its family can present.</summary>
    public Queue Present { get; }

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

    /// <summary>Gets a value indicating whether a sampler may bias which mip level it selects.</summary>
    /// <remarks>
    /// True on every conformant adapter. False only on a portable one whose underlying API has no such
    /// control, in which case a sampler asking for a bias would be rejected outright — so the texture
    /// path reads this and asks for none.
    /// </remarks>
    public bool SamplerMipLodBias { get; }

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
    /// <summary>Writes how much of each device-local heap is spoken for.</summary>
    /// <param name="when">What moment this reading describes, for the message.</param>
    /// <remarks>
    /// Read through VK_EXT_memory_budget, which reports the driver's own view rather than the sum of what
    /// the port asked for -- so it counts what other applications and the desktop are holding too. Logged
    /// at start-up and again on a device loss, because those two readings together answer a question a
    /// stack trace cannot: whether the device died because it ran out of memory or for some other reason.
    /// A card with a third of the memory the port was developed against is the case this exists for.
    /// </remarks>
    public unsafe void LogMemoryBudget(string when)
    {
        if (!_logger.IsEnabled(LogLevel.Information)
            || !Capabilities.EnabledExtensions.Contains(VulkanExtensionNames.MemoryBudget))
        {
            return;
        }

        PhysicalDeviceMemoryBudgetPropertiesEXT budget = new()
        {
            SType = StructureType.PhysicalDeviceMemoryBudgetPropertiesExt,
        };

        PhysicalDeviceMemoryProperties2 properties = new()
        {
            SType = StructureType.PhysicalDeviceMemoryProperties2,
            PNext = &budget,
        };

        _vk.GetPhysicalDeviceMemoryProperties2(Selection.Device, &properties);

        List<string> heaps = [];
        for (uint i = 0; i < properties.MemoryProperties.MemoryHeapCount; i++)
        {
            if ((properties.MemoryProperties.MemoryHeaps[(int)i].Flags & MemoryHeapFlags.DeviceLocalBit) == 0)
            {
                continue;
            }

            double used = budget.HeapUsage[(int)i] / (1024.0 * 1024.0);
            double allowed = budget.HeapBudget[(int)i] / (1024.0 * 1024.0);
            double size = properties.MemoryProperties.MemoryHeaps[(int)i].Size / (1024.0 * 1024.0);
            heaps.Add(FormattableString.Invariant(
                $"heap {i}: {used:F0} MiB used of {allowed:F0} MiB available ({size:F0} MiB total)"));
        }

        if (heaps.Count > 0)
        {
            _logger.Information("Vulkan", $"Device memory {when} -- {string.Join("; ", heaps)}.");
        }
    }

    /// <summary>Describes every memory heap's use and budget, the host's as well as the device's.</summary>
    /// <returns>One line per heap, or a note that the driver does not report budgets.</returns>
    /// <remarks>
    /// The host heap is the one a memory report is usually about: it is where upload staging and every
    /// other host-visible block lives, and what it holds counts against the process.
    /// </remarks>
    public unsafe string DescribeMemoryBudget()
    {
        if (!Capabilities.EnabledExtensions.Contains(VulkanExtensionNames.MemoryBudget))
        {
            return "  (the driver does not report memory budgets)";
        }

        PhysicalDeviceMemoryBudgetPropertiesEXT budget = new()
        {
            SType = StructureType.PhysicalDeviceMemoryBudgetPropertiesExt,
        };

        PhysicalDeviceMemoryProperties2 properties = new()
        {
            SType = StructureType.PhysicalDeviceMemoryProperties2,
            PNext = &budget,
        };

        _vk.GetPhysicalDeviceMemoryProperties2(Selection.Device, &properties);

        var text = new System.Text.StringBuilder();
        for (uint i = 0; i < properties.MemoryProperties.MemoryHeapCount; i++)
        {
            bool deviceLocal = (properties.MemoryProperties.MemoryHeaps[(int)i].Flags & MemoryHeapFlags.DeviceLocalBit) != 0;
            double used = budget.HeapUsage[(int)i] / (1024.0 * 1024.0);
            double allowed = budget.HeapBudget[(int)i] / (1024.0 * 1024.0);
            double size = properties.MemoryProperties.MemoryHeaps[(int)i].Size / (1024.0 * 1024.0);
            text.Append(FormattableString.Invariant(
                $"  heap {i} ({(deviceLocal ? "device" : "host")}): {used:F0} MiB used of {allowed:F0} MiB budget ({size:F0} MiB total)\n"));
        }

        return text.ToString().TrimEnd('\n');
    }

    /// <summary>The NVIDIA diagnostic-checkpoints extension, taken wherever it is offered.</summary>
    public const string DiagnosticCheckpointsExtension = "VK_NV_device_diagnostic_checkpoints";

    /// <summary>Records a marker the queue can be asked for if the device is lost.</summary>
    /// <param name="commandBuffer">The command buffer being recorded.</param>
    /// <param name="marker">One of <see cref="VulkanCheckpointMarkers"/>.</param>
    /// <remarks>
    /// Nothing at all on an adapter without the extension; with it, a few bytes of command stream. Legal
    /// inside and outside a render pass, on every queue type.
    /// </remarks>
    public unsafe void Checkpoint(CommandBuffer commandBuffer, nint marker)
    {
        if (_cmdSetCheckpoint != 0)
        {
            ((delegate* unmanaged<CommandBuffer, void*, void>)_cmdSetCheckpoint)(commandBuffer, (void*)marker);
        }
    }

    private unsafe void LoadDiagnosticCheckpoints()
    {
        if (!Capabilities.EnabledExtensions.Contains(DiagnosticCheckpointsExtension))
        {
            return;
        }

        _cmdSetCheckpoint = (nint)_vk.GetDeviceProcAddr(Device, "vkCmdSetCheckpointNV").Handle;
        _getQueueCheckpointData = (nint)_vk.GetDeviceProcAddr(Device, "vkGetQueueCheckpointDataNV").Handle;
        if (_cmdSetCheckpoint == 0 || _getQueueCheckpointData == 0)
        {
            _cmdSetCheckpoint = 0;
            _getQueueCheckpointData = 0;
        }
    }

    /// <summary>Asks the graphics queue which markers it last passed, and logs them oldest first.</summary>
    /// <remarks>
    /// The one report that names a pass. The fault address says what the GPU touched; this says which
    /// stage of which kind of command buffer it was running when it did, which is what turns a remote
    /// log into something that can be bisected.
    /// </remarks>
    private unsafe void ReportCheckpoints()
    {
        if (_getQueueCheckpointData == 0)
        {
            return;
        }

        var read = (delegate* unmanaged<Queue, uint*, VkCheckpointDataNV*, void>)_getQueueCheckpointData;
        uint count = 0;
        read(Graphics, &count, null);
        if (count == 0)
        {
            _logger.Error("Vulkan", "The graphics queue reports no diagnostic checkpoints.");
            return;
        }

        count = Math.Min(count, 64u);
        VkCheckpointDataNV* data = stackalloc VkCheckpointDataNV[(int)count];
        for (int i = 0; i < (int)count; i++)
        {
            data[i] = new VkCheckpointDataNV { SType = StructureType.CheckpointDataNV };
        }

        read(Graphics, &count, data);
        _logger.Error("Vulkan", $"Last {count} diagnostic checkpoint(s) the graphics queue passed:");
        for (int i = 0; i < (int)count; i++)
        {
            VkCheckpointDataNV c = data[i];
            _logger.Error(
                "Vulkan",
                $"  checkpoint: {VulkanCheckpointMarkers.Describe((nint)c.PCheckpointMarker)} (stage {c.Stage}).");
        }
    }

    public unsafe void ReportDeviceFault()
    {
        LogMemoryBudget("when the device was lost");
        ReportCheckpoints();

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

        // Before the device is destroyed: saving the cache reads it back out of the driver.
        _pipelineCache.Dispose();

        TimelineSemaphores?.Dispose();
        Synchronization2?.Dispose();
        Swapchain.Dispose();
        _vk.DestroyDevice(Device, null);
    }

    private Queue QueueAt(VulkanQueueSlot slot)
    {
        _vk.GetDeviceQueue(Device, slot.Family, slot.Index, out Queue queue);
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

        // Said BEFORE the call, in full, because of how this one fails. Handing vkCreateDevice a pNext
        // structure whose extension is not among the enabled names is undefined, and a driver that does
        // not recognise it faults rather than returning an error -- so the process dies inside the driver
        // with no VkResult to report and no chance to log afterwards. Written out first, the log names the
        // adapter, the exact extension list and every structure in the chain, which is the whole of what
        // the call was given and enough to see the mismatch without the machine that has it.
        LogDeviceCreation(selection, chain, features);

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

    /// <summary>Writes everything vkCreateDevice is about to be given.</summary>
    /// <param name="selection">The adapter and the extensions it will be created with.</param>
    /// <param name="chain">The feature chain, walked for the structure types and the flags it carries.</param>
    /// <param name="features">The core features, which travel outside the chain.</param>
    private void LogDeviceCreation(PhysicalDeviceSelection selection, VulkanStructChain chain, in PhysicalDeviceFeatures features)
    {
        if (!_logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        Version32 api = (Version32)selection.ApiVersion;
        Version32 driver = (Version32)selection.Properties.DriverVersion;
        _logger.Information(
            "Vulkan",
            $"Creating device on '{selection.Name}' (vendor 0x{selection.Properties.VendorID:X4}, "
            + $"device 0x{selection.Properties.DeviceID:X4}, type {selection.Properties.DeviceType}, "
            + $"Vulkan {api.Major}.{api.Minor}.{api.Patch}, driver {driver.Major}.{driver.Minor}.{driver.Patch}).");

        _logger.Information(
            "Vulkan",
            $"Enabling {selection.DeviceExtensions.Count} device extensions: "
            + string.Join(", ", selection.DeviceExtensions));

        // Every Vulkan extension structure opens with a 32-bit type and then, a pointer-slot along, its
        // own pNext, so the chain can be read back without knowing what was put in it.
        List<string> linked = [];
        for (void* link = chain.Head; link is not null; link = ((void**)link)[1])
        {
            linked.Add((*(StructureType*)link).ToString());
        }

        _logger.Information("Vulkan", $"Feature chain ({linked.Count}): {string.Join(", ", linked)}.");

        // And every flag set in each, because VK_ERROR_FEATURE_NOT_PRESENT names no feature: it says one
        // of the bits was not there, and the list below is the whole set of bits it could have been. The
        // core structure travels through pEnabledFeatures rather than the chain, so it is written first.
        _logger.Information("Vulkan", $"Requested core features: {RequestedFlags(features)}.");
        for (void* link = chain.Head; link is not null; link = ((void**)link)[1])
        {
            string? flags = *(StructureType*)link switch
            {
                StructureType.PhysicalDeviceVulkan11Features => RequestedFlags(*(PhysicalDeviceVulkan11Features*)link),
                StructureType.PhysicalDeviceVulkan12Features => RequestedFlags(*(PhysicalDeviceVulkan12Features*)link),
                StructureType.PhysicalDeviceVulkan13Features => RequestedFlags(*(PhysicalDeviceVulkan13Features*)link),
                _ => null,
            };

            if (flags is not null)
            {
                _logger.Information("Vulkan", $"Requested {*(StructureType*)link}: {flags}.");
            }
        }
    }

    /// <summary>Names every <see cref="Bool32"/> field of a feature structure that is set.</summary>
    /// <typeparam name="T">The Vulkan feature structure type.</typeparam>
    /// <param name="value">The structure as it will be handed to the driver.</param>
    /// <returns>The set field names, comma-separated, or a note that none is set.</returns>
    private static string RequestedFlags<T>(in T value)
        where T : unmanaged
    {
        // Reflection over a blittable struct, once per process, on the way into a call that may not
        // return: the cost is nothing and the field names are the ones the specification uses.
        object boxed = value;
        List<string> set = [];
        foreach (FieldInfo field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType == typeof(Bool32) && (Bool32)field.GetValue(boxed)!)
            {
                set.Add(field.Name);
            }
        }

        return set.Count == 0 ? "none" : string.Join(", ", set);
    }
}
