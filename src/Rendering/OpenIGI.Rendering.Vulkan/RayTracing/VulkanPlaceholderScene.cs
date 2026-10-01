using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Queues;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.RayTracing;

/// <summary>A scene with nothing in it, for the frames before the world has one.</summary>
/// <remarks>
/// <para>
/// A shader that traces rays names an acceleration structure, and a descriptor set has no way to say
/// "there is not one yet" — every binding it declares must be written with something legal before the
/// dispatch. The world's own structure is not ready until the level has been drawn once and its geometry
/// collected, which leaves a handful of frames at the start of every level with nothing to bind.
/// </para>
/// <para>
/// So there is always this: one triangle, far enough away and small enough that no ray of any length used
/// here can reach it. Binding it is the same as binding an empty world — every trace misses — but it is a
/// real structure, which is what the descriptor needs. The alternative is a second shader for the frames
/// before the world exists, which is a great deal of machinery for two seconds of level load.
/// </para>
/// </remarks>
internal sealed class VulkanPlaceholderScene : IDisposable
{
    // Far outside any level, and degenerate besides. Nothing traces this far, and if anything did the
    // triangle has no area to hit.
    private const float Away = 1e9f;

    private readonly VulkanBuffer _vertices;
    private readonly VulkanBuffer _indices;
    private readonly VulkanRayTracingScene _scene;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanPlaceholderScene"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="api">The ray-tracing entry points.</param>
    /// <param name="device">The device the structure lives on.</param>
    /// <param name="allocator">The allocator backing its buffers.</param>
    /// <param name="commands">The pool the one-off build is recorded through.</param>
    /// <param name="queue">The queue that build is submitted to.</param>
    public VulkanPlaceholderScene(
        Vk vk,
        VulkanRayTracingApi api,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        ISubmissionClock queue)
    {
        const BufferUsageFlags usage = BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr
            | BufferUsageFlags.ShaderDeviceAddressBit
            | BufferUsageFlags.StorageBufferBit;

        _vertices = new VulkanBuffer(
            vk, device, allocator, (ulong)(3 * 3 * sizeof(float)), usage,
            allocator.UploadMemoryProperties(), persistentlyMapped: true);
        _vertices.Write<float>([Away, Away, Away, Away, Away, Away, Away, Away, Away]);

        _indices = new VulkanBuffer(
            vk, device, allocator, sizeof(uint) * 3, usage,
            allocator.UploadMemoryProperties(), persistentlyMapped: true);
        _indices.Write<ushort>([0, 1, 2]);

        _scene = new VulkanRayTracingScene(vk, api, device, allocator, instanceCapacity: 1);

        VulkanBottomLevelStructure geometry = _scene.AddGeometry(
        [
            new VulkanTriangleGeometry(
                _vertices.DeviceAddress,
                VertexStride: 3 * sizeof(float),
                VertexCount: 3,
                _indices.DeviceAddress,
                PrimitiveCount: 1),
        ]);

        _scene.SetInstances(
        [
            VulkanRayTracingInstance.Create(
                Matrix4f.Identity,
                customIndex: 0,
                VulkanRayTracingInstance.VisibleToAll,
                hitGroupOffset: 0,
                GeometryInstanceFlagsKHR.TriangleFacingCullDisableBitKhr,
                geometry.DeviceAddress),
        ]);

        commands.SingleTime(cmd => _scene.RecordUpdate(cmd, queue));
    }

    /// <summary>Gets the structure to bind, or null if the build did not produce one.</summary>
    public VulkanAccelerationStructure? TopLevel => _scene.TopLevel;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scene.Dispose();
        _indices.Dispose();
        _vertices.Dispose();
    }
}
