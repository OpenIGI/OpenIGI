using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Pipelines;

/// <summary>
/// The per-frame vertex and index storage the 2D path appends into, one ring per frame in flight.
/// </summary>
/// <remarks>
/// <para>
/// Two-dimensional drawing is immediate: each call brings its own short vertex and index spans rather
/// than a mesh uploaded once. Allocating a buffer per call would exhaust the driver's allocation budget
/// in a frame, so instead each frame in flight owns one host-visible, persistently mapped vertex buffer
/// and one index buffer, and every draw appends to the frame's own pair at a running offset. A draw
/// records the offset it wrote at, so the buffers are written once and bound many times.
/// </para>
/// <para>
/// The rings are reset at the top of each frame. Because a frame in flight has its own pair, the CPU
/// filling frame N+1 never treads on the buffers the GPU is still reading for frame N — the same reason
/// the command buffers and fences are per frame in flight.
/// </para>
/// </remarks>
internal sealed unsafe class Sprite2DGeometry : IDisposable
{
    // Sized for a heavy HUD-and-menu frame with headroom. A 2D frame does not approach this; a draw
    // that would overflow it fails loudly rather than corrupting a neighbour's geometry.
    private const int VertexCapacity = 128 * 1024;
    private const int IndexCapacity = 256 * 1024;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly VulkanAllocator _allocator;
    private readonly Ring[] _rings;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="Sprite2DGeometry"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the buffers live on.</param>
    /// <param name="allocator">The allocator backing the buffers.</param>
    /// <param name="framesInFlight">How many rings to keep — one per frame in flight.</param>
    public Sprite2DGeometry(Vk vk, VulkanDevice device, VulkanAllocator allocator, int framesInFlight)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);
        _vk = vk;
        _device = device.Device;
        _allocator = allocator;

        _rings = new Ring[framesInFlight];
        for (int i = 0; i < framesInFlight; i++)
        {
            _rings[i] = new Ring(vk, device, allocator);
        }
    }

    /// <summary>Resets a frame's ring to empty, ready to be appended into.</summary>
    /// <param name="frameIndex">The frame-in-flight index.</param>
    public void Reset(int frameIndex) => _rings[frameIndex].Reset();

    /// <summary>Gets the vertex buffer of a frame's ring, to bind before drawing.</summary>
    /// <param name="frameIndex">The frame-in-flight index.</param>
    /// <returns>The vertex buffer handle.</returns>
    public Buffer VertexBuffer(int frameIndex) => _rings[frameIndex].VertexBuffer;

    /// <summary>Gets the index buffer of a frame's ring, to bind before drawing.</summary>
    /// <param name="frameIndex">The frame-in-flight index.</param>
    /// <returns>The index buffer handle.</returns>
    public Buffer IndexBuffer(int frameIndex) => _rings[frameIndex].IndexBuffer;

    /// <summary>Appends a draw's vertices and indices into a frame's ring.</summary>
    /// <param name="frameIndex">The frame-in-flight index.</param>
    /// <param name="vertices">The vertices to append.</param>
    /// <param name="indices">The indices to append, addressing <paramref name="vertices"/> from zero.</param>
    /// <param name="vertexOffset">Receives the vertex offset to pass to the draw.</param>
    /// <param name="firstIndex">Receives the first index to pass to the draw.</param>
    /// <exception cref="GraphicsDeviceException">The ring cannot hold the appended geometry.</exception>
    public void Append(
        int frameIndex,
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices,
        out int vertexOffset,
        out uint firstIndex)
    {
        _rings[frameIndex].Append(vertices, indices, out vertexOffset, out firstIndex);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Ring ring in _rings)
        {
            ring.Dispose();
        }
    }

    private sealed class Ring : IDisposable
    {
        private readonly Vk _vk;
        private readonly Device _device;
        private readonly VulkanAllocator _allocator;
        private readonly Buffer _vertexBuffer;
        private readonly Buffer _indexBuffer;
        private readonly DeviceMemory _vertexMemory;
        private readonly DeviceMemory _indexMemory;
        private readonly Vertex2D* _vertices;
        private readonly ushort* _indices;
        private int _vertexCount;
        private int _indexCount;
        private bool _disposed;

        public Ring(Vk vk, VulkanDevice device, VulkanAllocator allocator)
        {
            _vk = vk;
            _device = device.Device;
            _allocator = allocator;

            _vertexBuffer = CreateMapped(
                (ulong)(VertexCapacity * Vertex2D.Size),
                BufferUsageFlags.VertexBufferBit,
                out _vertexMemory,
                out void* vertexMap);
            _indexBuffer = CreateMapped(
                (ulong)(IndexCapacity * sizeof(ushort)),
                BufferUsageFlags.IndexBufferBit,
                out _indexMemory,
                out void* indexMap);
            _vertices = (Vertex2D*)vertexMap;
            _indices = (ushort*)indexMap;
        }

        public Buffer VertexBuffer => _vertexBuffer;

        public Buffer IndexBuffer => _indexBuffer;

        public void Reset()
        {
            _vertexCount = 0;
            _indexCount = 0;
        }

        public void Append(
            ReadOnlySpan<Vertex2D> vertices,
            ReadOnlySpan<ushort> indices,
            out int vertexOffset,
            out uint firstIndex)
        {
            if (_vertexCount + vertices.Length > VertexCapacity || _indexCount + indices.Length > IndexCapacity)
            {
                throw new GraphicsDeviceException(
                    "The 2D geometry ring is full; a single frame drew more than it is sized for.");
            }

            vertexOffset = _vertexCount;
            firstIndex = (uint)_indexCount;

            vertices.CopyTo(new Span<Vertex2D>(_vertices + _vertexCount, vertices.Length));
            indices.CopyTo(new Span<ushort>(_indices + _indexCount, indices.Length));

            _vertexCount += vertices.Length;
            _indexCount += indices.Length;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _vk.UnmapMemory(_device, _vertexMemory);
            _vk.UnmapMemory(_device, _indexMemory);
            _vk.DestroyBuffer(_device, _vertexBuffer, null);
            _vk.DestroyBuffer(_device, _indexBuffer, null);
            _allocator.Free(_vertexMemory);
            _allocator.Free(_indexMemory);
        }

        private Buffer CreateMapped(ulong size, BufferUsageFlags usage, out DeviceMemory memory, out void* mapped)
        {
            BufferCreateInfo createInfo = new()
            {
                SType = StructureType.BufferCreateInfo,
                Size = size,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
            };

            Buffer buffer;
            VkCheck.Ok(_vk.CreateBuffer(_device, &createInfo, null, &buffer), "vkCreateBuffer");

            _vk.GetBufferMemoryRequirements(_device, buffer, out MemoryRequirements requirements);
            memory = _allocator.Allocate(
                requirements,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            VkCheck.Ok(_vk.BindBufferMemory(_device, buffer, memory, 0), "vkBindBufferMemory");

            void* map = null;
            VkCheck.Ok(_vk.MapMemory(_device, memory, 0, size, 0, ref map), "vkMapMemory");
            mapped = map;
            return buffer;
        }
    }
}
