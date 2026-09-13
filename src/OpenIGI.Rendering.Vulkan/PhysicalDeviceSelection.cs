using OpenIGI.Rendering.Vulkan.Capabilities;
using OpenIGI.Rendering.Vulkan.Queues;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>The adapter the backend chose, and everything decided while choosing it.</summary>
/// <remarks>
/// Selection reads a great deal about a physical device — its queue families, its limits, which optional
/// features it actually implements — and every one of those facts is needed again when the logical
/// device is created or when a pass asks whether it may trace a ray. Gathering them into one value means
/// the physical device is inspected once rather than re-queried at each later decision.
/// </remarks>
internal sealed class PhysicalDeviceSelection
{
    /// <summary>Initialises a new instance of the <see cref="PhysicalDeviceSelection"/> class.</summary>
    /// <param name="device">The chosen physical device.</param>
    /// <param name="queues">The queues the backend will use.</param>
    /// <param name="properties">Its properties, already read.</param>
    /// <param name="name">Its human-readable name.</param>
    /// <param name="capabilities">Which optional features it implements, and their limits.</param>
    public PhysicalDeviceSelection(
        PhysicalDevice device,
        VulkanQueuePlan queues,
        PhysicalDeviceProperties properties,
        string name,
        VulkanDeviceCapabilities capabilities)
    {
        Device = device;
        Queues = queues;
        Properties = properties;
        Name = name;
        Capabilities = capabilities;
    }

    /// <summary>Gets the chosen physical device.</summary>
    public PhysicalDevice Device { get; }

    /// <summary>Gets the queues the backend will use.</summary>
    public VulkanQueuePlan Queues { get; }

    /// <summary>Gets the physical device's properties.</summary>
    public PhysicalDeviceProperties Properties { get; }

    /// <summary>Gets the adapter's human-readable name.</summary>
    public string Name { get; }

    /// <summary>Gets which optional features the adapter implements, and the limits that go with them.</summary>
    public VulkanDeviceCapabilities Capabilities { get; }

    /// <summary>Gets the Vulkan version the adapter supports.</summary>
    public uint ApiVersion => Properties.ApiVersion;

    /// <summary>Gets the device extensions to enable on the logical device.</summary>
    public IReadOnlyList<string> DeviceExtensions => Capabilities.EnabledExtensions;

    /// <summary>
    /// Gets a value indicating whether rays can be traced against hardware acceleration structures.
    /// </summary>
    /// <remarks>
    /// The seam the whole exercise is aimed at: the indirect-lighting passes trace either a software
    /// distance field or a hardware acceleration structure, and this is the one question that chooses
    /// between them.
    /// </remarks>
    public bool SupportsRayTracing => Capabilities.HardwareRayTracing;
}
