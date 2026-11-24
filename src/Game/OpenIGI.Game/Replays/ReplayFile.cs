using System.IO.Compression;

namespace OpenIGI.Game.Replays;

/// <summary>
/// A replay as a file (<c>.oigr</c>): what it is of (<see cref="ReplayHeader"/>), how it ended (<see cref="ReplayResult"/>) and the
/// inputs it was played with (<see cref="InputTape"/>).
/// </summary>
/// <remarks>
/// Four bytes <c>OIGR</c>, the format's version as two, the body's length as four and its SHA-256, then the body deflated. Read
/// strictly and within limits -- a file may come from anyone -- so it can be refused, never trusted: the hash only says the
/// file is whole, and what it claims is checked by playing it again.
/// </remarks>
/// <param name="Header">What it is of.</param>
/// <param name="Result">How it ended.</param>
/// <param name="Tape">The inputs.</param>
public sealed record ReplayFile(ReplayHeader Header, ReplayResult Result, InputTape Tape)
{
    /// <summary>The file name ending a replay is saved under.</summary>
    public const string Extension = ".oigr";

    /// <summary>The format this build writes and reads.</summary>
    public const ushort FormatVersion = 1;

    /// <summary>The largest replay file read, in bytes.</summary>
    public const int MaxFileBytes = 4 * 1024 * 1024;

    /// <summary>The largest a replay is let grow to as it is inflated, in bytes.</summary>
    public const int MaxInflatedBytes = 64 * 1024 * 1024;

    private static ReadOnlySpan<byte> Magic => "OIGR"u8;

    /// <summary>Writes the replay.</summary>
    /// <param name="stream">Where it goes.</param>
    public void Write(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] body = Body();
        stream.Write(Magic);
        stream.Write([(byte)(FormatVersion & 0xff), (byte)(FormatVersion >> 8)]);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, body.Length);
        stream.Write(length);
        stream.Write(System.Security.Cryptography.SHA256.HashData(body));
        using DeflateStream deflate = new(stream, CompressionLevel.SmallestSize, leaveOpen: true);
        deflate.Write(body);
    }

    // Everything the file says, before it is deflated.
    private byte[] Body()
    {
        using MemoryStream memory = new();
        using (BinaryWriter writer = new(memory, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            Header.Write(writer);
            Result.Write(writer);
            InputTapeCodec.Write(writer, Tape);
        }

        return memory.ToArray();
    }

    /// <summary>The replay as bytes.</summary>
    /// <returns>The file's bytes.</returns>
    public byte[] ToBytes()
    {
        using MemoryStream memory = new();
        Write(memory);
        return memory.ToArray();
    }

    /// <summary>Reads a replay.</summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <returns>The replay.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a replay this build reads.</exception>
    public static ReplayFile Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxFileBytes)
        {
            throw new InvalidDataException($"A replay of {bytes.Length} bytes, past the {MaxFileBytes} one may be.");
        }

        if (bytes.Length < PreambleBytes || !bytes[..4].SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a replay.");
        }

        ushort version = (ushort)(bytes[4] | (bytes[5] << 8));
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"A replay of format {version}; this build reads {FormatVersion}.");
        }

        int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes[6..10]);
        if (length < 0 || length > MaxInflatedBytes)
        {
            throw new InvalidDataException($"A replay says it is {length} bytes inflated, past the {MaxInflatedBytes} one may be.");
        }

        byte[] inflated = Inflate(bytes[PreambleBytes..], length);
        if (inflated.Length != length || !System.Security.Cryptography.SHA256.HashData(inflated).AsSpan().SequenceEqual(bytes[10..PreambleBytes]))
        {
            throw new InvalidDataException("A replay is not whole: its body is not the one its hash says.");
        }

        using MemoryStream memory = new(inflated, writable: false);
        using BinaryReader reader = new(memory, System.Text.Encoding.UTF8, leaveOpen: true);
        try
        {
            ReplayHeader header = ReplayHeader.Read(reader);
            ReplayResult result = ReplayResult.Read(reader);
            InputTape tape = InputTapeCodec.Read(reader);
            if (memory.Position != memory.Length)
            {
                throw new InvalidDataException("A replay goes on past its end.");
            }

            return new ReplayFile(header, result, tape);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("A replay ends part way through.", ex);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("A replay's numbers are not numbers.", ex);
        }
    }

    // The magic, the version, the body's length and its hash.
    private const int PreambleBytes = 4 + 2 + 4 + 32;

    // The deflated part, inflated no further than the length it says, which is never past MaxInflatedBytes.
    private static byte[] Inflate(ReadOnlySpan<byte> deflated, int length)
    {
        using MemoryStream source = new(deflated.ToArray(), writable: false);
        using DeflateStream inflate = new(source, CompressionMode.Decompress);
        using MemoryStream target = new();
        byte[] buffer = new byte[81920];
        try
        {
            int read;
            while ((read = inflate.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (target.Length + read > length)
                {
                    throw new InvalidDataException("A replay inflates past the length it says.");
                }

                target.Write(buffer, 0, read);
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (IOException ex)
        {
            throw new InvalidDataException("A replay's body is not deflated data.", ex);
        }

        return target.ToArray();
    }
}
