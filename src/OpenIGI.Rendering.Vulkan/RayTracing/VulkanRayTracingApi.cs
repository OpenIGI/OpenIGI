using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace OpenIGI.Rendering.Vulkan.RayTracing;

/// <summary>
/// The ray-tracing entry points and the handful of alignments everything built against them must obey.
/// </summary>
/// <remarks>
/// <para>
/// Acceleration structures live in ordinary buffers, are pointed at by device address, and are built by
/// commands from an extension that has to be loaded before any of it can be called. Loading it once and
/// carrying the alignments alongside means the build code asks "what alignment does scratch need" rather
/// than re-reading a properties structure at each build.
/// </para>
/// <para>
/// The alignments are not the raw device limits. A structure's own memory wants 256 bytes, which is what
/// every implementation is happy with and what keeps a structure off the same cache lines as its
/// neighbour; scratch memory takes whichever is larger of that and the adapter's own requirement, so one
/// number satisfies both rather than the build having to reason about which applies where.
/// </para>
/// </remarks>
internal sealed class VulkanRayTracingApi : IDisposable
{
    /// <summary>The alignment an acceleration structure's backing memory is placed at.</summary>
    public const ulong StructureAlignment = 256;

    /// <summary>The alignment the instance descriptor array must start at.</summary>
    public const ulong InstanceBufferAlignment = 16;

    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanRayTracingApi"/> class.</summary>
    /// <param name="instance">The instance the device was created under.</param>
    /// <param name="device">The device to load the entry points for.</param>
    /// <exception cref="GraphicsDeviceException">The device does not support hardware ray tracing.</exception>
    public VulkanRayTracingApi(VulkanInstance instance, VulkanDevice device)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);

        if (!device.Capabilities.AccelerationStructure)
        {
            throw new GraphicsDeviceException(
                "Hardware acceleration structures are not available on this device.");
        }

        Vk vk = instance.Vk;
        if (!vk.TryGetDeviceExtension(instance.Instance, device.Device, out KhrAccelerationStructure structures))
        {
            throw new GraphicsDeviceException("VK_KHR_acceleration_structure could not be loaded.");
        }

        Structures = structures;

        if (device.Capabilities.RayTracingPipeline
            && vk.TryGetDeviceExtension(instance.Instance, device.Device, out KhrRayTracingPipeline pipeline))
        {
            Pipeline = pipeline;
        }

        PhysicalDeviceAccelerationStructurePropertiesKHR limits = device.Capabilities.AccelerationStructureLimits;
        ScratchAlignment = Math.Max(StructureAlignment, limits.MinAccelerationStructureScratchOffsetAlignment);
        MaxInstanceCount = limits.MaxInstanceCount;
        MaxPrimitiveCount = limits.MaxPrimitiveCount;

        PhysicalDeviceRayTracingPipelinePropertiesKHR rt = device.Capabilities.RayTracingLimits;
        ShaderGroupHandleSize = rt.ShaderGroupHandleSize;
        ShaderGroupHandleAlignment = Math.Max(1, rt.ShaderGroupHandleAlignment);
        ShaderGroupBaseAlignment = Math.Max(1, rt.ShaderGroupBaseAlignment);
        MaxRayRecursionDepth = rt.MaxRayRecursionDepth;

        // A build reads its geometry through the acceleration structure's own traversal rather than
        // through a shader binding, so a hit can report the triangle it struck without the shader having
        // to reach back into the mesh's buffers. Where it is available every structure is built to allow
        // it, because the alternative is threading every mesh's vertex and index buffers through the hit
        // group's parameters.
        PositionFetch = device.Capabilities.RayTracingPositionFetch;
    }

    /// <summary>Gets the acceleration-structure entry points.</summary>
    public KhrAccelerationStructure Structures { get; }

    /// <summary>Gets the ray-tracing-pipeline entry points, or <see langword="null"/> when unavailable.</summary>
    /// <remarks>
    /// Only needed where a hit has to re-shade a full material. Everything else traces inline through a
    /// ray query in an ordinary compute shader and never touches a ray-tracing pipeline.
    /// </remarks>
    public KhrRayTracingPipeline? Pipeline { get; }

    /// <summary>Gets the alignment build scratch memory must start at.</summary>
    public ulong ScratchAlignment { get; }

    /// <summary>Gets how many instances one top-level structure may hold.</summary>
    public ulong MaxInstanceCount { get; }

    /// <summary>Gets how many primitives one bottom-level structure may hold.</summary>
    public ulong MaxPrimitiveCount { get; }

    /// <summary>Gets the size in bytes of one shader group handle.</summary>
    public uint ShaderGroupHandleSize { get; }

    /// <summary>Gets the alignment one shader binding table record starts at.</summary>
    public uint ShaderGroupHandleAlignment { get; }

    /// <summary>Gets the alignment a shader binding table region starts at.</summary>
    public uint ShaderGroupBaseAlignment { get; }

    /// <summary>Gets how deep a ray-tracing pipeline may recurse.</summary>
    public uint MaxRayRecursionDepth { get; }

    /// <summary>Gets a value indicating whether a hit can read its triangle's positions from the structure.</summary>
    public bool PositionFetch { get; }

    /// <summary>Rounds a size up to an alignment.</summary>
    /// <param name="value">The value to round.</param>
    /// <param name="alignment">The alignment, which must be a power of two.</param>
    /// <returns>The rounded value.</returns>
    public static ulong Align(ulong value, ulong alignment) => VulkanAlign.Up(value, alignment);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Pipeline?.Dispose();
        Structures.Dispose();
    }
}
