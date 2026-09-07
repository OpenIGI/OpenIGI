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
    private readonly VulkanBuffer _indexBuffer;

    // An animated mesh's poses live in a ring rather than a pair. A pose write must never land on
    // memory a frame still in flight is reading: the GPU is up to MaxFramesInFlight frames behind the
    // CPU, and a body's two streams stay bound for every frame between two ticks. OpenGL got this for
    // free -- it re-specified the buffer with glBufferData, which orphans the old storage and lets the
    // driver retire it once the GPU is done -- and the port dropped that, memcpying the new pose over
    // vertices an unretired frame was still fetching. The ring is the explicit form of the same idea:
    // a slot is written only once every frame that bound it has retired, so the previous pose a frame
    // reads stays the pose it was recorded with. Null for a static or storage mesh, whose vertices are
    // either immutable or appended under their own versioning.
    private readonly VulkanBuffer[]? _poseRing;
    private readonly ulong[]? _poseSlotLastBoundFrame;
    private int _frontSlot;
    private int _previousSlot;
    private bool _disposed;

    /// <summary>How many pose buffers an animated mesh rings through.</summary>
    /// <remarks>
    /// Two more than the frames in flight. A frame binds one (current, previous) pair, so the frames
    /// that have not retired hold at most three distinct slots between them; a fourth is always free
    /// for the next tick to write. Overridable for the A/B that proves the hazard: at two the ring
    /// aliases exactly as the old pair did.
    /// </remarks>
    internal static readonly int PoseRingLength = ResolvePoseRingLength();

    private static int ResolvePoseRingLength()
    {
        string? configured = Environment.GetEnvironmentVariable("OPENIGI_VK_POSE_RING");
        return int.TryParse(configured, out int length) && length >= 2 && length <= 8
            ? length
            : VulkanFrameLoop.MaxFramesInFlight + 2;
    }

    /// <summary>Counts pose writes that had to land on a slot a frame in flight could still read.</summary>
    /// <remarks>
    /// Zero on a correct run. It rises only when the ring cannot offer a retired slot, which needs the
    /// frame rate to fall far enough that several ticks pass inside one unretired frame.
    /// </remarks>
    internal static long PoseWriteHazards;

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
        if (animated)
        {
            // Slot zero is the mesh's own vertex buffer, so everything that addresses a mesh by that
            // buffer keeps working; the rest of the ring is allocated beside it.
            _poseRing = new VulkanBuffer[PoseRingLength];
            _poseRing[0] = _vertexBuffer;
            for (int slot = 1; slot < _poseRing.Length; slot++)
            {
                _poseRing[slot] = CreateBuffer(
                    vk,
                    device,
                    allocator,
                    (ulong)(vertexCapacity * Vertex3D.Size),
                    BufferUsageFlags.VertexBufferBit | rayTracing,
                    hostVisible: true);
            }

            _poseSlotLastBoundFrame = new ulong[_poseRing.Length];
            _frontSlot = 0;
            _previousSlot = _poseRing.Length - 1;
        }
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
    public Buffer PreviousVertexBuffer => PreviousPoseVertexBuffer.Handle;

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
        // Every slot starts at the rest pose, so the first ticks blend between two real poses rather
        // than out of uninitialised memory.
        foreach (VulkanBuffer slot in mesh._poseRing!)
        {
            slot.Write(vertices);
        }
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
    /// <param name="frameCounter">The device's current frame counter, which decides the ring slot.</param>
    /// <exception cref="GraphicsDeviceException">The mesh is not animated or the count differs.</exception>
    public void UpdateVertices(ReadOnlySpan<Vertex3D> vertices, ulong frameCounter)
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

        // Into a slot no unretired frame can still be fetching, then forward the ring: what was drawn
        // as the current pose becomes the previous-pose stream without a copy, which is what gives the
        // velocity raster the real old vertices for skinned motion. The slot the ring leaves behind is
        // not reused until the frames that bound it have retired -- the whole point of the ring, and
        // what the OpenGL path got from orphaning its buffer on every re-specification.
        int chosen = ChoosePoseSlot(frameCounter);
        _poseRing![chosen].Write(vertices);
        _previousSlot = _frontSlot;
        _frontSlot = chosen;
    }

    /// <summary>Picks the ring slot this pose may be written into.</summary>
    /// <param name="frameCounter">The device's current frame counter.</param>
    /// <returns>A slot that is neither live pose and that no unretired frame bound.</returns>
    /// <remarks>
    /// The current pose is never a candidate: the flip is about to make it the previous stream. Of the
    /// rest, a slot is free once every frame that bound it has retired, which the device expresses
    /// everywhere else as being <see cref="VulkanFrameLoop.MaxFramesInFlight"/> frames behind. If the
    /// frame rate has fallen far enough that several ticks pass inside one unretired frame, no slot is
    /// free; the least recently bound one is taken and the write is counted, because tearing one pose
    /// is a better failure than refusing to animate.
    /// </remarks>
    private int ChoosePoseSlot(ulong frameCounter)
    {
        VulkanBuffer[] ring = _poseRing!;
        ulong[] lastBound = _poseSlotLastBoundFrame!;
        int oldest = -1;
        for (int step = 1; step <= ring.Length; step++)
        {
            int slot = (_frontSlot + step) % ring.Length;
            if (slot == _frontSlot)
            {
                continue;
            }

            if (lastBound[slot] + VulkanFrameLoop.MaxFramesInFlight <= frameCounter)
            {
                return slot;
            }

            if (oldest < 0 || lastBound[slot] < lastBound[oldest])
            {
                oldest = slot;
            }
        }

        Interlocked.Increment(ref PoseWriteHazards);
        return oldest;
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
        if (_poseRing is not null)
        {
            // Slot zero is _vertexBuffer, disposed below with every other mesh's.
            for (int slot = 1; slot < _poseRing.Length; slot++)
            {
                _poseRing[slot].Dispose();
            }
        }

        _vertexBuffer.Dispose();
    }

    private VulkanBuffer CurrentVertexBuffer => _poseRing is null ? _vertexBuffer : _poseRing[_frontSlot];

    private VulkanBuffer PreviousPoseVertexBuffer =>
        _poseRing is null ? _vertexBuffer : _poseRing[_previousSlot];

    /// <summary>Records that the frame being built has bound this mesh's two pose streams.</summary>
    /// <param name="frameCounter">The device's frame counter for the frame doing the binding.</param>
    /// <remarks>
    /// What keeps a pose write off memory the GPU is still reading. Stamped on both live slots, since a
    /// frame fetches from both, and read back by <see cref="UpdateVertices"/> to tell a retired slot
    /// from one an unretired frame still refers to.
    /// </remarks>
    public void MarkPoseSlotsBound(ulong frameCounter)
    {
        if (_poseSlotLastBoundFrame is null)
        {
            return;
        }

        _poseSlotLastBoundFrame[_frontSlot] = frameCounter;
        _poseSlotLastBoundFrame[_previousSlot] = frameCounter;
    }

    private void StageInto<T>(Buffer destination, ulong destinationOffset, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        if (data.IsEmpty)
        {
            return;
        }

        ulong bytes = (ulong)(data.Length * Unsafe.SizeOf<T>());

        // Handed to the batch rather than disposed here; see VulkanCommands.SingleTimeBatched. The batch
        // is flushed both before a frame opens and before it is submitted, so a page staged mid-frame
        // still reaches the queue ahead of the draw that reads it.
        VulkanBuffer staging = new(
            _vk,
            _device,
            _allocator,
            bytes,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        staging.Write(data);

        Buffer source = staging.Handle;
        _commands.SingleTimeBatched(
            cmd =>
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
        },
            keepAlive: staging,
            stagingBytes: bytes);
    }
}
