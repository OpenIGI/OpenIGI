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

/// <summary>
/// A ring of uniform memory that push data is written into and bound from.
/// </summary>
/// <remarks>
/// <para>
/// One buffer, written a slice at a time and bound by dynamic offset, because that is the only shape
/// WebGPU offers for what a push constant does. A slice is aligned to what the device requires of a
/// dynamic uniform offset, which is 256 bytes on every adapter this has met, so a block of 112 bytes
/// still costs 256 — the price of not having push constants.
/// </para>
/// <para>
/// The ring is written through the queue rather than through the encoder, which is what makes it
/// correct: a queue write is ordered against the submissions around it, so everything written while a
/// frame is being recorded lands before that frame runs. It is also why the ring has to be large enough
/// for a whole frame's worth of pushes — nothing is reused until the frame that read it has finished.
/// </para>
/// </remarks>
public sealed class WebGpuPushRing : IDisposable
{
    private readonly WebGpuRhiDevice _device;
    private readonly IRhiBuffer _buffer;
    private readonly byte[] _staging;
    private readonly int _slice;
    private int _next;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="WebGpuPushRing"/> class.</summary>
    /// <param name="device">The device the buffer lives on.</param>
    /// <param name="slices">How many pushes one frame may make before the ring wraps.</param>
    /// <param name="alignment">What the device requires of a dynamic uniform offset.</param>
    public WebGpuPushRing(WebGpuRhiDevice device, int slices = 4096, int alignment = 256)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        _slice = Math.Max(alignment, Align(Math.Max(WebGpuPushBlocks.LargestBlock, 1), alignment));
        _staging = new byte[_slice];

        _buffer = device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(_slice * slices),
            RhiBufferUsage.Uniform | RhiBufferUsage.CopyDestination,
            "push.ring"));

        Slices = slices;
    }

    /// <summary>Gets the buffer the push data lives in.</summary>
    public IRhiBuffer Buffer => _buffer;

    /// <summary>Gets how many bytes one slice occupies.</summary>
    public int SliceBytes => _slice;

    /// <summary>Gets how many slices the ring holds.</summary>
    public int Slices { get; }

    /// <summary>Writes one push block and says where it landed.</summary>
    /// <param name="data">The bytes, which must fit in one slice.</param>
    /// <returns>The dynamic offset to bind it at.</returns>
    /// <exception cref="InvalidOperationException">The block is larger than a slice, or the ring is full.</exception>
    public uint Write(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (data.Length > _slice)
        {
            throw new InvalidOperationException(
                $"A push of {data.Length} bytes does not fit the ring's {_slice}-byte slice.");
        }

        if (_next >= Slices)
        {
            throw new InvalidOperationException(
                $"The push ring's {Slices} slices are spent for this frame. Reset it once the frame that "
                + "used them has finished, or build it with more.");
        }

        int slice = _next++;
        uint offset = (uint)(slice * _slice);

        // Written a whole slice at a time, zero-filled past the data: a uniform binding is sized to the
        // block, and leaving the tail at whatever the last frame put there would hand a shader values
        // nobody wrote.
        Array.Clear(_staging);
        data.CopyTo(_staging);
        _device.WriteBuffer<byte>(_buffer, offset, _staging);

        return offset;
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
        _buffer.Dispose();
    }

    private static int Align(int value, int alignment) => ((value + alignment - 1) / alignment) * alignment;
}
