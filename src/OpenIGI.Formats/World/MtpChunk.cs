using OpenIGI.Core;

namespace OpenIGI.Formats.World;

/// <summary>One chunk of an <c>.mtp</c> pack.</summary>
/// <param name="Id">The chunk tag, spelled as it appears on disk.</param>
/// <param name="HeaderOffset">The offset of the chunk header within the pack.</param>
/// <param name="Payload">The payload bytes, a slice of the pack buffer and excluding the padding that follows.</param>
public readonly record struct MtpChunk(FourCc Id, int HeaderOffset, ReadOnlyMemory<byte> Payload)
{
    /// <summary>Gets the payload length in bytes, excluding padding.</summary>
    public int Size => Payload.Length;

    /// <inheritdoc/>
    public override string ToString() => $"{Id} @{HeaderOffset} size={Size}";
}
