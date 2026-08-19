using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>Chooses the adapter the backend runs on and works out how it will be used.</summary>
/// <remarks>
/// <para>
/// The rule is the one every renderer uses: prefer a discrete GPU, fall back to whatever can present.
/// A candidate is only eligible if it can do the two things the backend cannot start without — render
/// to and present on the surface, and carry a swapchain — so the choice is made among devices already
/// known to work rather than found to be unusable three calls later.
/// </para>
/// <para>
/// Ray-tracing support is noted rather than required. The whole point of the backend is that the
/// software-traced GI can later become hardware-traced; recording, at selection, whether the adapter
/// carries the extensions for that is what lets the switch be a capability check rather than a second
/// enumeration.
/// </para>
/// </remarks>
internal sealed unsafe class PhysicalDeviceSelector
{
    // The one extension no candidate can be without: presenting a rendered image needs a swapchain.
    private static readonly string[] RequiredExtensions = [KhrSwapchain.ExtensionName];

    // The hardware ray-tracing set. Acceleration structures describe the scene, the ray-tracing
    // pipeline traces through them, and deferred host operations is how the driver builds those
    // structures across threads. All three, or the adapter is treated as raster-only.
    private static readonly string[] RayTracingExtensions =
    [
        KhrAccelerationStructure.ExtensionName,
        KhrRayTracingPipeline.ExtensionName,
        KhrDeferredHostOperations.ExtensionName,
    ];

    private readonly Vk _vk;
    private readonly Instance _instance;
    private readonly KhrSurface _khrSurface;
    private readonly SurfaceKHR _surface;
    private readonly IIgiLogger _logger;

    /// <summary>Initialises a new instance of the <see cref="PhysicalDeviceSelector"/> class.</summary>
    /// <param name="instance">The instance and its surface to select against.</param>
    /// <param name="logger">Where to report the choice made.</param>
    public PhysicalDeviceSelector(VulkanInstance instance, IIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(instance);
        _vk = instance.Vk;
        _instance = instance.Instance;
        _khrSurface = instance.KhrSurface;
        _surface = instance.Surface;
        _logger = logger;
    }

    /// <summary>Selects an adapter and works out how it will be used.</summary>
    /// <returns>The chosen adapter and the facts gathered while choosing it.</returns>
    /// <exception cref="GraphicsDeviceException">No adapter can render to and present on the surface.</exception>
    public PhysicalDeviceSelection Select()
    {
        uint count = 0;
        VkCheck.Ok(_vk.EnumeratePhysicalDevices(_instance, &count, null), "vkEnumeratePhysicalDevices");
        if (count == 0)
        {
            throw new GraphicsDeviceException("No Vulkan physical devices are present.");
        }

        PhysicalDevice[] devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* first = devices)
        {
            VkCheck.Ok(_vk.EnumeratePhysicalDevices(_instance, &count, first), "vkEnumeratePhysicalDevices");
        }

        PhysicalDeviceSelection? best = null;
        foreach (PhysicalDevice device in devices)
        {
            PhysicalDeviceSelection? candidate = Evaluate(device);
            if (candidate is null)
            {
                continue;
            }

            // A discrete GPU always wins; among equals the first found is kept, which on a single-GPU
            // machine is the only one anyway.
            if (best is null || IsPreferredOver(candidate, best))
            {
                best = candidate;
            }
        }

        if (best is null)
        {
            throw new GraphicsDeviceException(
                "No Vulkan adapter can both present to the window surface and support a swapchain.");
        }

        _logger.Information(
            "Vulkan",
            $"Selected adapter '{best.Name}' (ray tracing {(best.SupportsRayTracing ? "available" : "unavailable")}).");
        return best;
    }

    private static bool IsPreferredOver(PhysicalDeviceSelection candidate, PhysicalDeviceSelection incumbent)
    {
        bool candidateDiscrete = candidate.Properties.DeviceType == PhysicalDeviceType.DiscreteGpu;
        bool incumbentDiscrete = incumbent.Properties.DeviceType == PhysicalDeviceType.DiscreteGpu;
        return candidateDiscrete && !incumbentDiscrete;
    }

    private PhysicalDeviceSelection? Evaluate(PhysicalDevice device)
    {
        if (!TryFindQueueFamilies(device, out QueueFamilyIndices queues))
        {
            return null;
        }

        HashSet<string> available = ReadDeviceExtensions(device);
        foreach (string required in RequiredExtensions)
        {
            if (!available.Contains(required))
            {
                return null;
            }
        }

        List<string> enabled = [.. RequiredExtensions];

        // Robustness2 makes every out-of-bounds access — including index-buffer reads, which plain
        // robustBufferAccess does not cover — return zero rather than lose the device. Enabled whenever
        // present, as a safety net across the draw paths.
        if (available.Contains("VK_EXT_robustness2"))
        {
            enabled.Add("VK_EXT_robustness2");
        }

        bool rayTracing = true;
        foreach (string rt in RayTracingExtensions)
        {
            if (!available.Contains(rt))
            {
                rayTracing = false;
            }
        }

        if (rayTracing)
        {
            enabled.AddRange(RayTracingExtensions);

            // The ray-tracing pipeline reaches its geometry through buffer device addresses, so the two
            // travel together. It is core in 1.2 but still enumerated as an extension the device must
            // enable, so it is added alongside the set that needs it.
            const string bufferDeviceAddress = "VK_KHR_buffer_device_address";
            if (available.Contains(bufferDeviceAddress))
            {
                enabled.Add(bufferDeviceAddress);
            }
        }

        _vk.GetPhysicalDeviceProperties(device, out PhysicalDeviceProperties properties);
        string name = VkString.Read(properties.DeviceName);
        return new PhysicalDeviceSelection(device, queues, properties, name, rayTracing, enabled);
    }

    private bool TryFindQueueFamilies(PhysicalDevice device, out QueueFamilyIndices indices)
    {
        indices = default;

        uint count = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(device, &count, null);
        if (count == 0)
        {
            return false;
        }

        QueueFamilyProperties[] families = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* first = families)
        {
            _vk.GetPhysicalDeviceQueueFamilyProperties(device, &count, first);
        }

        int graphics = -1;
        int present = -1;
        int compute = -1;
        int unified = -1;

        for (uint i = 0; i < count; i++)
        {
            bool canGraphics = families[i].QueueFlags.HasFlag(QueueFlags.GraphicsBit);
            bool canCompute = families[i].QueueFlags.HasFlag(QueueFlags.ComputeBit);
            bool canPresent = PresentSupported(device, i);

            if (canGraphics && graphics < 0)
            {
                graphics = (int)i;
            }

            if (canCompute && compute < 0)
            {
                compute = (int)i;
            }

            if (canPresent && present < 0)
            {
                present = (int)i;
            }

            if (canGraphics && canPresent && unified < 0)
            {
                unified = (int)i;
            }
        }

        if (graphics < 0 || present < 0 || compute < 0)
        {
            return false;
        }

        // Prefer the family that does both graphics and present, so the common path needs one queue and
        // no cross-family image sharing on the swapchain.
        if (unified >= 0)
        {
            graphics = unified;
            present = unified;
        }

        indices = new QueueFamilyIndices((uint)graphics, (uint)present, (uint)compute);
        return true;
    }

    private bool PresentSupported(PhysicalDevice device, uint queueFamily)
    {
        Bool32 supported = false;
        VkCheck.Ok(
            _khrSurface.GetPhysicalDeviceSurfaceSupport(device, queueFamily, _surface, &supported),
            "vkGetPhysicalDeviceSurfaceSupportKHR");
        return supported;
    }

    private HashSet<string> ReadDeviceExtensions(PhysicalDevice device)
    {
        uint count = 0;
        if (_vk.EnumerateDeviceExtensionProperties(device, (byte*)null, &count, null) != Result.Success || count == 0)
        {
            return [];
        }

        ExtensionProperties[] properties = new ExtensionProperties[count];
        HashSet<string> names = new((int)count, StringComparer.Ordinal);
        fixed (ExtensionProperties* first = properties)
        {
            if (_vk.EnumerateDeviceExtensionProperties(device, (byte*)null, &count, first) != Result.Success)
            {
                return [];
            }

            for (uint i = 0; i < count; i++)
            {
                names.Add(VkString.Read(first[i].ExtensionName));
            }
        }

        return names;
    }
}
