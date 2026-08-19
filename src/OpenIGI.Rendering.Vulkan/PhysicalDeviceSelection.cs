using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>The adapter the backend chose, and everything decided while choosing it.</summary>
/// <remarks>
/// Selection reads a great deal about a physical device — its queue families, its limits, whether it
/// can present, whether it carries the ray-tracing extensions — and every one of those facts is needed
/// again when the logical device is created or when capabilities are reported. Gathering them into one
/// value means the physical device is inspected once rather than re-queried at each later decision.
/// </remarks>
internal sealed class PhysicalDeviceSelection
{
    /// <summary>Initialises a new instance of the <see cref="PhysicalDeviceSelection"/> class.</summary>
    /// <param name="device">The chosen physical device.</param>
    /// <param name="queues">Its queue families.</param>
    /// <param name="properties">Its properties, already read.</param>
    /// <param name="name">Its human-readable name.</param>
    /// <param name="supportsRayTracing">Whether the hardware-RT extension set is present.</param>
    /// <param name="deviceExtensions">The device extensions the logical device should enable.</param>
    public PhysicalDeviceSelection(
        PhysicalDevice device,
        QueueFamilyIndices queues,
        PhysicalDeviceProperties properties,
        string name,
        bool supportsRayTracing,
        IReadOnlyList<string> deviceExtensions)
    {
        Device = device;
        Queues = queues;
        Properties = properties;
        Name = name;
        SupportsRayTracing = supportsRayTracing;
        DeviceExtensions = deviceExtensions;
    }

    /// <summary>Gets the chosen physical device.</summary>
    public PhysicalDevice Device { get; }

    /// <summary>Gets the queue families to draw queues from.</summary>
    public QueueFamilyIndices Queues { get; }

    /// <summary>Gets the physical device's properties.</summary>
    public PhysicalDeviceProperties Properties { get; }

    /// <summary>Gets the adapter's human-readable name.</summary>
    public string Name { get; }

    /// <summary>Gets a value indicating whether the hardware ray-tracing extension set is present.</summary>
    /// <remarks>
    /// The acceleration-structure, ray-tracing-pipeline and deferred-host-operations extensions
    /// together. Recorded here so the software-traced GI path can be swapped for a hardware-traced one
    /// without re-inspecting the adapter — the seam the whole exercise is aimed at.
    /// </remarks>
    public bool SupportsRayTracing { get; }

    /// <summary>Gets the device extensions to enable on the logical device.</summary>
    public IReadOnlyList<string> DeviceExtensions { get; }
}
