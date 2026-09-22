using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Capabilities;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Queues;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>Chooses the adapter the backend runs on and works out how it will be used.</summary>
/// <remarks>
/// <para>
/// The rule is the one every renderer uses: prefer a discrete GPU, fall back to whatever can present. A
/// candidate is only eligible if it can do the two things the backend cannot start without — render to
/// and present on the surface, and carry a swapchain — so the choice is made among devices already known
/// to work rather than found to be unusable three calls later.
/// </para>
/// <para>
/// Everything else is noted rather than required. Whether the adapter can trace rays in hardware, hold
/// its descriptors in a buffer, or run compute on an engine of its own all decide which path a pass
/// takes, not whether the backend starts; recording all of it at selection is what lets those be
/// capability checks later rather than a second enumeration.
/// </para>
/// </remarks>
internal sealed unsafe class PhysicalDeviceSelector
{
    // The one extension no candidate can be without: presenting a rendered image needs a swapchain.
    private static readonly string[] RequiredExtensions = [KhrSwapchain.ExtensionName];

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

        Report(best);
        return best;
    }

    private static bool IsPreferredOver(PhysicalDeviceSelection candidate, PhysicalDeviceSelection incumbent)
    {
        bool candidateDiscrete = candidate.Properties.DeviceType == PhysicalDeviceType.DiscreteGpu;
        bool incumbentDiscrete = incumbent.Properties.DeviceType == PhysicalDeviceType.DiscreteGpu;
        return candidateDiscrete && !incumbentDiscrete;
    }

    private void Report(PhysicalDeviceSelection selection)
    {

        VulkanDeviceCapabilities capabilities = selection.Capabilities;
        Version32 api = (Version32)selection.ApiVersion;

        _logger.Information(
            "Vulkan",
            $"Selected adapter '{selection.Name}' (Vulkan {api.Major}.{api.Minor}.{api.Patch}).");

        VulkanQueuePlan queues = selection.Queues;
        string compute = queues.HasDedicatedCompute
            ? $"family {queues.AsyncCompute.Family}"
            : "shared with graphics";
        string transfer = queues.HasDedicatedTransfer
            ? $"family {queues.Transfer.Family}"
            : "shared";
        _logger.Information(
            "Vulkan",
            $"Queues: graphics family {queues.Graphics.Family}, async compute {compute}, transfer {transfer}.");

        string tracing = capabilities.HardwareRayTracing
            ? capabilities.RayTracingPipeline ? "ray query and ray-tracing pipeline" : "ray query only"
            : "unavailable";
        _logger.Information(
            "Vulkan",
            $"Hardware ray tracing: {tracing}. Bindless descriptor buffer: " +
            $"{(capabilities.DescriptorBuffer ? "available" : "unavailable")}. " +
            $"Synchronisation 2: {(capabilities.Synchronization2 ? "available" : "unavailable")}. " +
            $"Timeline semaphores: {(capabilities.TimelineSemaphore ? "available" : "unavailable")}.");

        // Said once, near the top, because it reframes everything under it: on a portable adapter an
        // absent capability is the translation layer's limit rather than the hardware's, and no amount of
        // driver updating will add it.
        if (capabilities.PortabilitySubset)
        {
            _logger.Information(
                "Vulkan",
                "Adapter implements the Vulkan portability subset (Vulkan translated to another API, "
                + $"which on macOS is Metal through MoltenVK). Sampler mip LOD bias: "
                + $"{(capabilities.SamplerMipLodBias ? "available" : "unavailable, textures sample unbiased")}.");
        }

        // Named individually only when one is missing, because on every adapter the port has run on so
        // far the list is empty and a line saying so would be noise. When it is not empty it is the first
        // thing worth knowing: these are the features device creation used to demand outright, so an
        // adapter listing any of them here is one that could not have started the port at all before.
        List<string> absent = [];
        if (!capabilities.ShaderInt64)
        {
            absent.Add("64-bit integers in shaders");
        }

        if (!capabilities.FragmentStoresAndAtomics)
        {
            absent.Add("fragment-stage stores and atomics");
        }

        if (!capabilities.VertexPipelineStoresAndAtomics)
        {
            absent.Add("vertex-stage stores and atomics");
        }

        if (!capabilities.StorageImageWriteWithoutFormat)
        {
            absent.Add("format-less storage image writes");
        }

        if (!capabilities.IndependentBlend)
        {
            absent.Add("per-attachment blend state");
        }

        if (!capabilities.ArrayDynamicIndexing)
        {
            absent.Add("dynamically indexed descriptor arrays");
        }

        if (absent.Count > 0)
        {
            _logger.Warning(
                "Vulkan",
                $"Adapter does not offer: {string.Join(", ", absent)}. These are not enabled, and no pass "
                + "depends on one today; a pass added later that does must check the capability first.");
        }
    }

    /// <summary>Extension names to treat as absent, from the environment.</summary>
    /// <remarks>
    /// <para>
    /// A comma-separated list of extension names, or <c>optional</c> for every extension that is not
    /// required. It exists for the failure that cannot be reproduced: device creation faulting inside a
    /// driver on an adapter nobody working on the port owns. Bisecting it otherwise means a build, a
    /// download and a report for every candidate, where this asks the person in front of the machine to
    /// run the same executable again with one variable set.
    /// </para>
    /// <para>
    /// Applied here, to the available set, rather than at either of the places that read it. Everything
    /// downstream -- which capabilities are reported, which extensions are enabled, which structures are
    /// linked -- is derived from this one set, so removing a name here removes it from all of them at
    /// once and they cannot be made to disagree by using it.
    /// </para>
    /// </remarks>
    internal const string DisableEnvironmentVariable = "OPENIGI_VK_DISABLE";

    private PhysicalDeviceSelection? Evaluate(PhysicalDevice device)
    {
        HashSet<string> available = WithdrawDisabled(ReadDeviceExtensions(device));

        // What the adapter offers, before anything decides what to do with it. The enabled list and the
        // feature chain are both derived from this, so a report that carries all three says which of the
        // two derivations went wrong -- and it is the only one of the three that cannot be worked out
        // from the source, because it is the driver's answer rather than the port's decision.
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _vk.GetPhysicalDeviceProperties(device, out PhysicalDeviceProperties reported);
            Version32 reportedApi = (Version32)reported.ApiVersion;
            _logger.Information(
                "Vulkan",
                $"'{VkString.Read(reported.DeviceName)}' (Vulkan {reportedApi.Major}.{reportedApi.Minor}."
                + $"{reportedApi.Patch}) offers {available.Count} device extensions: "
                + string.Join(", ", available.Order(StringComparer.Ordinal)));
        }

        foreach (string required in RequiredExtensions)
        {
            if (!available.Contains(required))
            {
                return null;
            }
        }

        _vk.GetPhysicalDeviceProperties(device, out PhysicalDeviceProperties properties);

        if (!TryPlanQueues(device, out VulkanQueuePlan? queues))
        {
            return null;
        }

        VulkanDeviceCapabilities capabilities =
            VulkanCapabilityProbe.Probe(_vk, device, available, properties.ApiVersion);

        string name = VkString.Read(properties.DeviceName);
        return new PhysicalDeviceSelection(device, queues, properties, name, capabilities);
    }

    /// <summary>Removes whatever <see cref="DisableEnvironmentVariable"/> names from the available set.</summary>
    /// <param name="available">Everything the adapter reports.</param>
    /// <returns>The set with the named extensions withdrawn.</returns>
    private HashSet<string> WithdrawDisabled(HashSet<string> available)
    {
        string? request = Environment.GetEnvironmentVariable(DisableEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(request))
        {
            return available;
        }

        // The required ones are never withdrawn: without them the adapter is rejected outright, and an
        // adapter rejected for a reason the person asked for is a confusing way to answer.
        HashSet<string> withdrawn = request.Trim().Equals("optional", StringComparison.OrdinalIgnoreCase)
            ? [.. available.Except(RequiredExtensions)]
            : [.. request.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(name => available.Contains(name) && !RequiredExtensions.Contains(name))];

        if (withdrawn.Count == 0)
        {
            return available;
        }

        _logger.Warning(
            "Vulkan",
            $"{DisableEnvironmentVariable} is withdrawing {withdrawn.Count} extension(s) the adapter "
            + $"does offer: {string.Join(", ", withdrawn.Order(StringComparer.Ordinal))}. "
            + "The device is created as though this adapter did not have them.");

        return [.. available.Except(withdrawn)];
    }

    /// <summary>Works out which queue of which family each kind of work will be submitted to.</summary>
    /// <remarks>
    /// <para>
    /// What matters is independence from the graphics engine, so a family only counts as asynchronous
    /// compute if it is a <em>different</em> family from the graphics one — a second queue drawn from the
    /// graphics family is still the same hardware and the work still serialises. A transfer family is
    /// stricter still: it must carry neither graphics nor compute, which is what identifies a real copy
    /// engine rather than a general family that happens to accept transfers.
    /// </para>
    /// <para>
    /// Not finding either is normal and not a failure. The slot falls back to the graphics queue, work
    /// runs in order, and every caller is written the same way regardless.
    /// </para>
    /// </remarks>
    private bool TryPlanQueues(PhysicalDevice device, [NotNullWhen(true)] out VulkanQueuePlan? plan)
    {
        plan = null;

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
        int unified = -1;

        for (uint i = 0; i < count; i++)
        {
            bool canGraphics = families[i].QueueFlags.HasFlag(QueueFlags.GraphicsBit);
            bool canPresent = PresentSupported(device, i);

            if (canGraphics && graphics < 0)
            {
                graphics = (int)i;
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

        if (graphics < 0 || present < 0)
        {
            return false;
        }

        // Prefer the family that does both, so the common path needs one queue and no cross-family image
        // sharing on the swapchain.
        if (unified >= 0)
        {
            graphics = unified;
            present = unified;
        }

        int asyncCompute = graphics;
        for (uint i = 0; i < count; i++)
        {
            if (i != (uint)graphics && families[i].QueueFlags.HasFlag(QueueFlags.ComputeBit))
            {
                asyncCompute = (int)i;
                break;
            }
        }

        int transfer = graphics;
        for (uint i = 0; i < count; i++)
        {
            QueueFlags flags = families[i].QueueFlags;
            bool dedicated = flags.HasFlag(QueueFlags.TransferBit)
                && !flags.HasFlag(QueueFlags.GraphicsBit)
                && !flags.HasFlag(QueueFlags.ComputeBit);
            if (dedicated && i != (uint)graphics && i != (uint)asyncCompute)
            {
                transfer = (int)i;
                break;
            }
        }

        // One queue per family is what the backend uses: a second queue from the same family shares the
        // hardware, so it buys ordering flexibility rather than parallelism, and it is not needed here.
        Dictionary<uint, uint> queueCounts = new()
        {
            [(uint)graphics] = 1,
            [(uint)present] = 1,
            [(uint)asyncCompute] = 1,
            [(uint)transfer] = 1,
        };

        plan = new VulkanQueuePlan(
            new VulkanQueueSlot((uint)graphics, 0),
            new VulkanQueueSlot((uint)present, 0),
            new VulkanQueueSlot((uint)asyncCompute, 0),
            new VulkanQueueSlot((uint)transfer, 0),
            queueCounts);
        return true;
    }

    private bool PresentSupported(PhysicalDevice device, uint queueFamily)
    {
        // A device with no surface presents nothing, and asking the driver whether a queue can present
        // to a surface that does not exist is undefined: it does not answer false, it takes the process
        // down. A headless device -- a test, a compute-only tool -- says every queue presents, which
        // leaves the choice to the other criteria and makes the present queue the graphics one.
        if (_surface.Handle == 0)
        {
            return true;
        }

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
