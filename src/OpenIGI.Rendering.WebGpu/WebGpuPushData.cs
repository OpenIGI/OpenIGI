using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu.Native;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>
/// Where the shader library puts push data when it has no push constants to put it in.
/// </summary>
/// <remarks>
/// <para>
/// WGSL has nothing like a push constant, so the shader compiler emits the push block as an ordinary
/// uniform in a group of its own. Which group is its choice, not this backend's, so it is read from the
/// compiled modules exactly as the storage formats are — and a library whose modules disagreed about
/// where it goes would be a library no single pipeline layout could serve.
/// </para>
/// <para>
/// Every module in the library today puts it at group 3, binding 0.
/// </para>
/// </remarks>
public static class WebGpuPushBlocks
{
    private static readonly Lazy<(int Group, int Binding)> Placement = new(Find, isThreadSafe: true);

    /// <summary>Gets the bind group the push block is bound as.</summary>
    public static int Group => Placement.Value.Group;

    /// <summary>Gets the binding within that group.</summary>
    public static int Binding => Placement.Value.Binding;

    /// <summary>Gets the largest push block any module declares, which sizes one slice of the ring.</summary>
    public static int LargestBlock { get; private set; }

    private static (int Group, int Binding) Find()
    {
        (int Group, int Binding)? placement = null;
        string? declaredBy = null;
        int largest = 0;

        foreach (ShaderModuleEntry module in ShaderCatalog.Manifest.Modules)
        {
            foreach (ShaderVariantEntry variant in module.Variants)
            {
                if (!variant.Compiles(ShaderTarget.Wgsl))
                {
                    continue;
                }

                ShaderPushBlock? push = ShaderCatalog
                    .GetReflection(module.Id, new ShaderVariant(variant.Name))
                    .PushBlock;

                if (push is null)
                {
                    continue;
                }

                largest = Math.Max(largest, push.Size);
                string where = $"{module.Id}.{variant.Name}";

                if (placement is { } already && already != (push.Group, push.Binding))
                {
                    throw new InvalidOperationException(
                        $"The shader library puts its push block at group {already.Group} binding "
                        + $"{already.Binding} in {declaredBy} and at group {push.Group} binding "
                        + $"{push.Binding} in {where}. One pipeline layout cannot serve both.");
                }

                placement = (push.Group, push.Binding);
                declaredBy = where;
            }
        }

        LargestBlock = largest;

        // A library with no push blocks at all still needs an answer, and the group after the ones a
        // pipeline uses is the one that cannot collide with anything.
        return placement ?? (3, 0);
    }
}

/// <summary>Where one push landed: the group to bind and the dynamic offset to bind it at.</summary>
/// <param name="Group">The bind group over the chunk of the ring the push was written to.</param>
/// <param name="Offset">Where in that chunk it landed.</param>
public readonly record struct WebGpuPushSlice(IRhiBindGroup Group, uint Offset);

/// <summary>
/// A ring of uniform memory that push data is written into and bound from.
/// </summary>
/// <remarks>
/// <para>
/// Uniform buffers written a slice at a time and bound by dynamic offset, because that is the only shape
/// WebGPU offers for what a push constant does. A slice is aligned to what the device requires of a
/// dynamic uniform offset, which is 256 bytes on every adapter this has met, so a block of 112 bytes
/// still costs 256 — the price of not having push constants.
/// </para>
/// <para>
/// The ring is written through the queue rather than through the encoder, which is what makes it
/// correct: a queue write is ordered against the submissions around it, so everything written while a
/// frame is being recorded lands before that frame runs. It is also why the ring has to hold a whole
/// frame's worth of pushes — nothing is reused until the frame that read it has finished.
/// </para>
/// <para>
/// So it grows rather than refusing: a frame that needs more than it holds gets another chunk, its own
/// buffer behind its own bind group, and keeps it. A frame's worth is not a number anything can know in
/// advance — capturing the surface cache draws the level into every card in one go, thousands of pushes
/// where an ordinary frame makes hundreds — and a Vulkan push constant has no such ceiling to hit.
/// </para>
/// </remarks>
public sealed class WebGpuPushRing : IDisposable
{
    private readonly WebGpuRhiDevice _device;
    private readonly List<Chunk> _chunks = [];
    private readonly byte[] _staging;
    private readonly int _slice;
    private int _next;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="WebGpuPushRing"/> class.</summary>
    /// <param name="device">The device the buffers live on.</param>
    /// <param name="slices">How many pushes one chunk holds.</param>
    /// <param name="alignment">What the device requires of a dynamic uniform offset.</param>
    public WebGpuPushRing(WebGpuRhiDevice device, int slices = 4096, int alignment = 256)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slices);

        _device = device;
        _slice = Math.Max(alignment, Align(Math.Max(WebGpuPushBlocks.LargestBlock, 1), alignment));
        _staging = new byte[_slice];

        Slices = slices;
        _chunks.Add(MakeChunk(0));
    }

    /// <summary>Gets the buffer of the ring's first chunk, where a frame's first pushes land.</summary>
    public IRhiBuffer Buffer => _chunks[0].Buffer;

    /// <summary>Gets how many bytes one slice occupies.</summary>
    public int SliceBytes => _slice;

    /// <summary>Gets how many slices one chunk holds.</summary>
    public int Slices { get; }

    /// <summary>Gets how many chunks the ring has grown to.</summary>
    public int Chunks => _chunks.Count;

    /// <summary>Writes one push block and says where it landed.</summary>
    /// <param name="data">The bytes, which must fit in one slice.</param>
    /// <returns>The group and dynamic offset to bind it at.</returns>
    /// <exception cref="InvalidOperationException">The block is larger than a slice.</exception>
    public WebGpuPushSlice Write(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (data.Length > _slice)
        {
            throw new InvalidOperationException(
                $"A push of {data.Length} bytes does not fit the ring's {_slice}-byte slice.");
        }

        int slice = _next++;
        int index = slice / Slices;

        if (index == _chunks.Count)
        {
            _chunks.Add(MakeChunk(index));
        }

        Chunk chunk = _chunks[index];
        uint offset = (uint)((slice % Slices) * _slice);

        // Written a whole slice at a time, zero-filled past the data: a uniform binding is sized to the
        // block, and leaving the tail at whatever the last frame put there would hand a shader values
        // nobody wrote.
        Array.Clear(_staging);
        data.CopyTo(_staging);
        _device.WriteBuffer<byte>(chunk.Buffer, offset, _staging);

        return new WebGpuPushSlice(chunk.Group, offset);
    }

    /// <summary>Starts the ring over, once the frame that read it has finished.</summary>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _next = 0;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (Chunk chunk in _chunks)
        {
            chunk.Group.Dispose();
            chunk.Buffer.Dispose();
        }

        _chunks.Clear();
    }

    private Chunk MakeChunk(int index)
    {
        IRhiBuffer buffer = _device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(_slice * Slices),
            RhiBufferUsage.Uniform | RhiBufferUsage.CopyDestination,
            index == 0 ? "push.ring" : $"push.ring.{index}"));

        return new Chunk(buffer, _device.CreatePushGroup(buffer, _slice));
    }

    private readonly record struct Chunk(IRhiBuffer Buffer, IRhiBindGroup Group);

    private static int Align(int value, int alignment) => ((value + alignment - 1) / alignment) * alignment;
}
