using System.Runtime.CompilerServices;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Meshes;

/// <summary>A mesh's vertex and index buffers, in one of the three shapes the seam asks for.</summary>
/// <remarks>
/// <para>
/// A <em>static</em> mesh is immutable: its vertices and indices are staged into device-local memory
/// once and never change, which is what most of the world is. A <em>storage</em> mesh is allocated to a
/// capacity and filled by appending ranges — the terrain's growing pages. An <em>animated</em> mesh's
/// vertices are rewritten each pose.
/// </para>
/// <para>
/// Device-local memory is where the GPU reads fastest, so a static mesh lives there and is staged once
/// through a host-visible buffer at load. A storage or animated mesh is written every frame, so its
/// rewritten buffers are host-visible and updated by a plain copy — never a mid-frame staging submit,
/// which is the wrong shape for Vulkan and lost the device. That trades some GPU read speed for
/// correctness and no per-frame stall; a device-local pool fed by a deferred upload queue is the later
/// refinement.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanMesh : IDisposable
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanAllocator _allocator;
    private readonly VulkanCommands _commands;
    private readonly VulkanBuffer _vertexBuffer;
    private readonly VulkanBuffer? _secondVertexBuffer;
    private readonly VulkanBuffer _indexBuffer;
    private bool _frontIsFirst = true;
    private bool _disposed;

    private VulkanMesh(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        int vertexCapacity,
        int indexCapacity,
        bool animated,
        bool storage)
    {
        _vk = vk;
        _device = device;
        _allocator = allocator;
        _commands = commands;
        VertexCapacity = vertexCapacity;
        IndexCapacity = indexCapacity;
        Animated = animated;
        Storage = storage;

        // A storage mesh is appended to every frame and an animated mesh's vertices are rewritten every
        // pose, so those buffers are host-visible and written by a plain copy — never a mid-frame staging
        // submit, which is the wrong shape for Vulkan. A static mesh stays device-local and is staged
        // once at load, where a blocking copy is fine.
        bool vertexHostVisible = animated || storage;

        // Where the adapter can trace, a mesh's geometry is also a build input for an acceleration
        // structure — read by the build through the buffer's device address rather than through a
        // binding. Both usages have to be asked for at creation, so they are added to every mesh rather
        // than to the ones that happen to be traced: which meshes those are is not known here, and a
        // buffer cannot gain a usage later.
        BufferUsageFlags rayTracing = device.Capabilities.AccelerationStructure
            ? BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr
                | BufferUsageFlags.ShaderDeviceAddressBit
                | BufferUsageFlags.StorageBufferBit
            : BufferUsageFlags.None;

        _vertexBuffer = CreateBuffer(
            vk,
            device,
            allocator,
            (ulong)(vertexCapacity * Vertex3D.Size),
            BufferUsageFlags.VertexBufferBit | rayTracing,
            vertexHostVisible);
        _secondVertexBuffer = animated
            ? CreateBuffer(
                vk,
                device,
                allocator,
                (ulong)(vertexCapacity * Vertex3D.Size),
                BufferUsageFlags.VertexBufferBit | rayTracing,
                hostVisible: true)
            : null;
        _indexBuffer = CreateBuffer(
            vk,
            device,
            allocator,
            (ulong)(indexCapacity * sizeof(ushort)),
            BufferUsageFlags.IndexBufferBit | rayTracing,
            storage);
    }

    /// <summary>Gets a value indicating whether the mesh is an appendable storage mesh.</summary>
    public bool Storage { get; }

    private static VulkanBuffer CreateBuffer(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        ulong size,
        BufferUsageFlags usage,
        bool hostVisible)
    {
        return new VulkanBuffer(
            vk,
            device,
            allocator,
            size,
            hostVisible ? usage : usage | BufferUsageFlags.TransferDstBit,
            hostVisible
                ? MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit
                : MemoryPropertyFlags.DeviceLocalBit);
    }

    /// <summary>Gets the vertex buffer handle, to bind before drawing.</summary>
    public Buffer VertexBuffer => CurrentVertexBuffer.Handle;

    /// <summary>Gets the preceding pose's vertex buffer, or the current buffer for a static mesh.</summary>
    public Buffer PreviousVertexBuffer => (_secondVertexBuffer is null ? _vertexBuffer : PreviousPoseVertexBuffer).Handle;

    /// <summary>Gets the index buffer handle, to bind before drawing.</summary>
    public Buffer IndexBuffer => _indexBuffer.Handle;

    /// <summary>Gets the vertex buffer's device address, for an acceleration-structure build.</summary>
    /// <remarks>
    /// A build reads geometry straight out of memory rather than through a descriptor, so what it needs
    /// is the address rather than the handle. Only meaningful on a device that can trace, since only then
    /// were the buffers created addressable.
    /// </remarks>
    public ulong VertexBufferAddress => CurrentVertexBuffer.DeviceAddress;

    /// <summary>Gets the index buffer's device address, for an acceleration-structure build.</summary>
    public ulong IndexBufferAddress => _indexBuffer.DeviceAddress;

    /// <summary>Gets how many vertices the mesh can hold.</summary>
    public int VertexCapacity { get; }

    /// <summary>Gets how many indices the mesh can hold.</summary>
    public int IndexCapacity { get; }

    /// <summary>Gets the number of indices that are drawable — the highest slot written plus one.</summary>
    public int DrawableIndexCount { get; private set; }

    /// <summary>Gets a value indicating whether the mesh's vertices can be rewritten each pose.</summary>
    public bool Animated { get; }

    /// <summary>Gets a counter that advances on every range written into a storage mesh.</summary>
    /// <remarks>
    /// What the ray-traced world compares to know that a caster mesh has been rewritten and its
    /// structure has to be built again.
    /// </remarks>
    public int ContentVersion { get; private set; }

    /// <summary>Creates an immutable mesh and stages its geometry.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the mesh lives on.</param>
    /// <param name="allocator">The allocator backing the buffers.</param>
    /// <param name="commands">The pool the staging copies come from.</param>
    /// <param name="vertices">The vertices.</param>
    /// <param name="indices">The indices.</param>
    /// <returns>The created mesh.</returns>
    public static VulkanMesh CreateStatic(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        ReadOnlySpan<Vertex3D> vertices,
        ReadOnlySpan<ushort> indices)
    {
        VulkanMesh mesh = new(vk, device, allocator, commands, Math.Max(1, vertices.Length), Math.Max(1, indices.Length), animated: false, storage: false);
        mesh.StageInto(mesh._vertexBuffer.Handle, 0, vertices);
        mesh.StageInto(mesh._indexBuffer.Handle, 0, indices);
        mesh.DrawableIndexCount = indices.Length;
        return mesh;
    }

    /// <summary>Creates a mesh whose vertices are rewritten each pose, staging the initial pose.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the mesh lives on.</param>
    /// <param name="allocator">The allocator backing the buffers.</param>
    /// <param name="commands">The pool the index staging copy comes from.</param>
    /// <param name="vertices">The rest pose.</param>
    /// <param name="indices">The triangle list, fixed for the mesh's life.</param>
    /// <returns>The created mesh.</returns>
    public static VulkanMesh CreateAnimated(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        ReadOnlySpan<Vertex3D> vertices,
        ReadOnlySpan<ushort> indices)
    {
        VulkanMesh mesh = new(vk, device, allocator, commands, Math.Max(1, vertices.Length), Math.Max(1, indices.Length), animated: true, storage: false);
        mesh._vertexBuffer.Write(vertices);
        mesh._secondVertexBuffer!.Write(vertices);
        mesh.StageInto(mesh._indexBuffer.Handle, 0, indices);
        mesh.DrawableIndexCount = indices.Length;
        return mesh;
    }

    /// <summary>Creates an empty mesh of fixed capacity, to be filled by <see cref="UploadRange"/>.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the mesh lives on.</param>
    /// <param name="allocator">The allocator backing the buffers.</param>
    /// <param name="commands">The pool the staging copies come from.</param>
    /// <param name="vertexCapacity">How many vertices the mesh can ever hold.</param>
    /// <param name="indexCapacity">How many indices the mesh can ever hold.</param>
    /// <returns>The created mesh, drawing nothing until a range is uploaded.</returns>
    public static VulkanMesh CreateStorage(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        int vertexCapacity,
        int indexCapacity)
    {
        return new VulkanMesh(vk, device, allocator, commands, vertexCapacity, indexCapacity, animated: false, storage: true);
    }

    /// <summary>Replaces an animated mesh's vertices.</summary>
    /// <param name="vertices">The new pose. Must be as many vertices as the mesh was created with.</param>
    /// <exception cref="GraphicsDeviceException">The mesh is not animated or the count differs.</exception>
    public void UpdateVertices(ReadOnlySpan<Vertex3D> vertices)
    {
        if (!Animated)
        {
            throw new GraphicsDeviceException("Only an animated mesh can have its vertices replaced.");
        }

        if (vertices.Length != VertexCapacity)
        {
            throw new GraphicsDeviceException(
                $"An animated mesh takes exactly {VertexCapacity} vertices but got {vertices.Length}.");
        }

        // Write the buffer holding the stale pose, then flip it to the front. The buffer that was drawn
        // before the flip becomes the previous-pose stream without copying it, matching the OpenGL path
        // and giving the velocity raster the actual old vertices for skinned motion.
        VulkanBuffer stale = _frontIsFirst ? _secondVertexBuffer! : _vertexBuffer;
        stale.Write(vertices);
        _frontIsFirst = !_frontIsFirst;
    }

    /// <summary>Writes a range of vertices and indices into a storage mesh.</summary>
    /// <param name="vertexOffset">The first vertex slot written.</param>
    /// <param name="vertices">The vertices to write; may be empty.</param>
    /// <param name="indexOffset">The first index slot written.</param>
    /// <param name="indices">The indices to write; may be empty.</param>
    /// <exception cref="GraphicsDeviceException">A range runs past its capacity.</exception>
    public void UploadRange(
        int vertexOffset,
        ReadOnlySpan<Vertex3D> vertices,
        int indexOffset,
        ReadOnlySpan<ushort> indices)
    {
        if (vertexOffset < 0 || vertexOffset + vertices.Length > VertexCapacity)
        {
            throw new GraphicsDeviceException("A vertex range runs past the storage mesh's capacity.");
        }

        if (indexOffset < 0 || indexOffset + indices.Length > IndexCapacity)
        {
            throw new GraphicsDeviceException("An index range runs past the storage mesh's capacity.");
        }

        // A storage mesh's buffers are host-visible, so a range is written directly with no staging
        // buffer and no mid-frame submit — the whole reason it is drawn without losing the device.
        _vertexBuffer.WriteAt((ulong)(vertexOffset * Vertex3D.Size), vertices);
        _indexBuffer.WriteAt((ulong)(indexOffset * sizeof(ushort)), indices);
        DrawableIndexCount = Math.Max(DrawableIndexCount, indexOffset + indices.Length);
        ContentVersion++;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _indexBuffer.Dispose();
        _secondVertexBuffer?.Dispose();
        _vertexBuffer.Dispose();
    }

    private VulkanBuffer CurrentVertexBuffer => _frontIsFirst ? _vertexBuffer : _secondVertexBuffer!;

    private VulkanBuffer PreviousPoseVertexBuffer => _frontIsFirst ? _secondVertexBuffer! : _vertexBuffer;

    private void StageInto<T>(Buffer destination, ulong destinationOffset, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        if (data.IsEmpty)
        {
            return;
        }

        ulong bytes = (ulong)(data.Length * Unsafe.SizeOf<T>());
        using VulkanBuffer staging = new(
            _vk,
            _device,
            _allocator,
            bytes,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        staging.Write(data);

        Buffer source = staging.Handle;
        _commands.SingleTime(cmd =>
        {
            BufferCopy region = new()
            {
                SrcOffset = 0,
                DstOffset = destinationOffset,
                Size = bytes,
            };
            _vk.CmdCopyBuffer(cmd, source, destination, 1, &region);

            // Make the copied data available to, and visible for, the vertex and index fetches that
            // read it. Without this the transfer write is not ordered against a later frame's draw on
            // the same queue — harmless for a mesh staged at load, but a storage mesh is uploaded and
            // drawn within the same frame, and an unsynchronised index read is undefined.
            MemoryBarrier barrier = new()
            {
                SType = StructureType.MemoryBarrier,
                SrcAccessMask = AccessFlags.TransferWriteBit,
                DstAccessMask = AccessFlags.VertexAttributeReadBit | AccessFlags.IndexReadBit,
            };
            _vk.CmdPipelineBarrier(
                cmd,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.VertexInputBit,
                0,
                1,
                &barrier,
                0,
                null,
                0,
                null);
        });
    }
}
