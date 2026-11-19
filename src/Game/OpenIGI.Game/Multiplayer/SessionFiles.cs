using System.IO.Compression;
using System.Security.Cryptography;
using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <summary>One file a level plays from: where it is, how long, and what is in it.</summary>
/// <param name="Path">The path under the installation, lower case with forward slashes (<c>missions/location0/level1/objects.qvm</c>).</param>
/// <param name="Size">Its length in bytes.</param>
/// <param name="Hash">Its SHA-256.</param>
public sealed record SessionFile(string Path, long Size, byte[] Hash)
{
    /// <summary>Gets whether some bytes are this file.</summary>
    /// <param name="data">The bytes.</param>
    /// <returns><see langword="true"/> when their length and hash are its own.</returns>
    public bool Matches(ReadOnlySpan<byte> data) => data.Length == Size && SHA256.HashData(data).AsSpan().SequenceEqual(Hash);
}

/// <summary>The files a server plays a map from, which every player of it must play from too.</summary>
/// <param name="Map">The map, as the session names it.</param>
/// <param name="Files">The files, in a fixed order (a request names them by place in it).</param>
public sealed record SessionFileList(string Map, IReadOnlyList<SessionFile> Files)
{
    /// <summary>The most files a list carries.</summary>
    public const int MaxFiles = 1024;

    /// <summary>The longest path a list carries.</summary>
    public const int MaxPathBytes = 160;

    /// <summary>The longest file a server sends or a client takes.</summary>
    public const long MaxFileSize = 128L * 1024 * 1024;

    /// <summary>The most bytes a list's files may come to.</summary>
    public const long MaxTotalSize = 512L * 1024 * 1024;

    /// <summary>Gets the bytes of every file.</summary>
    public long TotalSize => Files.Sum(static f => f.Size);

    /// <summary>Writes the list after its message kind.</summary>
    /// <param name="writer">Where it is written.</param>
    public void Write(BitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteBits((uint)SessionMessage.Files, 8);
        writer.WriteString(Map, 64);
        writer.WriteVarUInt((uint)Files.Count);
        foreach (SessionFile file in Files)
        {
            writer.WriteString(file.Path, MaxPathBytes);
            writer.WriteVarUInt((uint)file.Size);
            foreach (byte b in file.Hash)
            {
                writer.WriteBits(b, 8);
            }
        }
    }

    /// <summary>Reads a list after its message kind.</summary>
    /// <param name="reader">Where it is read from.</param>
    /// <returns>The list, or <see langword="null"/> when it is malformed or names a path a list may not.</returns>
    public static SessionFileList? Read(ref BitReader reader)
    {
        string map = reader.ReadString(64);
        uint count = reader.ReadVarUInt();
        if (reader.IsOverflowed || count > MaxFiles)
        {
            return null;
        }

        List<SessionFile> files = new((int)count);
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            string path = reader.ReadString(MaxPathBytes);
            long size = reader.ReadVarUInt();
            byte[] hash = new byte[SHA256.HashSizeInBytes];
            for (int b = 0; b < hash.Length; b++)
            {
                hash[b] = (byte)reader.ReadBits(8);
            }

            // A server names only files under the installation that a level plays from: never a path that climbs out of
            // it, roots itself elsewhere, or is anything a client would run.
            if (reader.IsOverflowed || size > MaxFileSize || !GameplayFiles.IsSafePath(path) || !seen.Add(path))
            {
                return null;
            }

            files.Add(new SessionFile(path, size, hash));
        }

        return files.Sum(static f => f.Size) > MaxTotalSize ? null : new SessionFileList(map, files);
    }
}

/// <summary>Where a session reads the files it compares and sends: the installation as the session plays it.</summary>
public interface ISessionFileSource
{
    /// <summary>Gets which of the game's files a map plays from.</summary>
    GameplayFileRules Rules { get; }

    /// <summary>Lists the files under a folder of the installation, at any depth.</summary>
    /// <param name="directory">The folder, as a list path.</param>
    /// <returns>Their paths, as a list writes them.</returns>
    IEnumerable<string> ListFiles(string directory);

    /// <summary>Reads a file as the session plays it.</summary>
    /// <param name="path">The file, as a list path.</param>
    /// <returns>Its bytes, or <see langword="null"/> when there is none.</returns>
    byte[]? Read(string path);
}

/// <summary>How a list writes a path, and which paths it may write at all.</summary>
/// <remarks>Which files a game compares is its own (<see cref="GameplayFileLayout"/>, <see cref="GameplayFileRules"/>).</remarks>
public static class GameplayFiles
{
    /// <summary>Gets whether a path is one a list may name: relative, inside the installation, with nothing odd in it.</summary>
    /// <param name="path">The path, as a list writes it.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsSafePath(string path) =>
        path.Length is > 0 and <= SessionFileList.MaxPathBytes
        && path == Normalize(path)
        && !path.StartsWith('/')
        && !path.Contains(':', StringComparison.Ordinal)
        && path.Split('/').All(static part => part.Length > 0 && part != "." && part != "..")
        && path.All(static c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '/' or '_' or '-' or '.');

    /// <summary>Writes a path as a list does: lower case, forward slashes, no leading slash.</summary>
    /// <param name="path">The path.</param>
    /// <returns>It, normalised.</returns>
    public static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
}

/// <summary>A piece of a file a server sent: one of the pieces its table cut it into (<see cref="ContentChunks"/>).</summary>
/// <param name="Map">The map of the list the file is in.</param>
/// <param name="Index">The file, by place in that list.</param>
/// <param name="Chunk">The piece, by place in the file's table; 0 for a file sent whole.</param>
/// <param name="Deflated">Whether <paramref name="Data"/> is deflated.</param>
/// <param name="Data">The piece, as sent.</param>
public sealed record FilePiece(string Map, int Index, int Chunk, bool Deflated, byte[] Data)
{
    /// <summary>Writes the piece after its message kind.</summary>
    /// <param name="writer">Where it is written.</param>
    public void Write(BitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteBits((uint)SessionMessage.FilePart, 8);
        writer.WriteString(Map, 64);
        writer.WriteVarUInt((uint)Index);
        writer.WriteVarUInt((uint)Chunk);
        writer.WriteBool(Deflated);
        writer.WriteBytes(Data);
    }

    /// <summary>Reads a piece after its message kind.</summary>
    /// <param name="reader">Where it is read from.</param>
    /// <returns>The piece, or <see langword="null"/> when it is malformed.</returns>
    public static FilePiece? Read(ref BitReader reader)
    {
        string map = reader.ReadString(64);
        uint index = reader.ReadVarUInt();
        uint chunk = reader.ReadVarUInt();
        bool deflated = reader.ReadBool();
        byte[] data = reader.ReadBytes(ContentChunks.MaxLength + 64);
        return reader.IsOverflowed || index >= SessionFileList.MaxFiles || chunk >= ContentChunks.MaxCount
            ? null
            : new FilePiece(map, (int)index, (int)chunk, deflated, data);
    }
}

/// <summary>The pieces a server's file is cut into, sent before any of them so a client asks only for those it lacks.</summary>
/// <param name="Map">The map of the list the file is in.</param>
/// <param name="Index">The file, by place in that list.</param>
/// <param name="Chunks">Its pieces, in order.</param>
public sealed record FileChunkTable(string Map, int Index, IReadOnlyList<ContentChunk> Chunks)
{
    /// <summary>Writes the table after its message kind.</summary>
    /// <param name="writer">Where it is written.</param>
    public void Write(BitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteBits((uint)SessionMessage.FileChunks, 8);
        writer.WriteString(Map, 64);
        writer.WriteVarUInt((uint)Index);
        writer.WriteVarUInt((uint)Chunks.Count);
        foreach (ContentChunk chunk in Chunks)
        {
            writer.WriteVarUInt((uint)chunk.Length);
            writer.WriteBits((uint)chunk.Hash, 32);
            writer.WriteBits((uint)(chunk.Hash >> 32), 32);
        }
    }

    /// <summary>Reads a table after its message kind.</summary>
    /// <param name="reader">Where it is read from.</param>
    /// <returns>The table, or <see langword="null"/> when it is malformed.</returns>
    public static FileChunkTable? Read(ref BitReader reader)
    {
        string map = reader.ReadString(64);
        uint index = reader.ReadVarUInt();
        uint count = reader.ReadVarUInt();
        if (reader.IsOverflowed || index >= SessionFileList.MaxFiles || count is 0 or > ContentChunks.MaxCount)
        {
            return null;
        }

        ContentChunk[] chunks = new ContentChunk[count];
        for (int i = 0; i < chunks.Length; i++)
        {
            uint length = reader.ReadVarUInt();
            ulong low = reader.ReadBits(32);
            ulong high = reader.ReadBits(32);
            if (reader.IsOverflowed || length is 0 or > ContentChunks.MaxLength)
            {
                return null;
            }

            chunks[i] = new ContentChunk((int)length, low | (high << 32));
        }

        return new FileChunkTable(map, (int)index, chunks);
    }
}

/// <summary>A client's ask for some pieces of a file whose table it was sent: runs of them, by place in the table.</summary>
/// <param name="Map">The map of the list the file is in.</param>
/// <param name="Index">The file, by place in that list.</param>
/// <param name="Runs">The runs of pieces: the first's place and how many follow it.</param>
public sealed record ChunkWant(string Map, int Index, IReadOnlyList<(int Start, int Count)> Runs)
{
    /// <summary>The most runs one ask carries.</summary>
    public const int MaxRuns = 4096;

    /// <summary>Writes the ask after its message kind.</summary>
    /// <param name="writer">Where it is written.</param>
    public void Write(BitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteBits((uint)SessionMessage.ChunkWant, 8);
        writer.WriteString(Map, 64);
        writer.WriteVarUInt((uint)Index);
        writer.WriteVarUInt((uint)Runs.Count);
        foreach ((int start, int count) in Runs)
        {
            writer.WriteVarUInt((uint)start);
            writer.WriteVarUInt((uint)count);
        }
    }

    /// <summary>Reads an ask after its message kind.</summary>
    /// <param name="reader">Where it is read from.</param>
    /// <returns>The ask, or <see langword="null"/> when it is malformed.</returns>
    public static ChunkWant? Read(ref BitReader reader)
    {
        string map = reader.ReadString(64);
        uint index = reader.ReadVarUInt();
        uint runs = reader.ReadVarUInt();
        if (reader.IsOverflowed || index >= SessionFileList.MaxFiles || runs > MaxRuns)
        {
            return null;
        }

        List<(int, int)> read = new((int)runs);
        for (int i = 0; i < runs; i++)
        {
            uint start = reader.ReadVarUInt();
            uint count = reader.ReadVarUInt();
            if (reader.IsOverflowed || start >= ContentChunks.MaxCount || count is 0 || count > ContentChunks.MaxCount - start)
            {
                return null;
            }

            read.Add(((int)start, (int)count));
        }

        return new ChunkWant(map, (int)index, read);
    }

    /// <summary>Gathers places into runs.</summary>
    /// <param name="places">The places, in rising order.</param>
    /// <returns>The runs.</returns>
    public static IReadOnlyList<(int Start, int Count)> RunsOf(IEnumerable<int> places)
    {
        ArgumentNullException.ThrowIfNull(places);
        List<(int Start, int Count)> runs = [];
        foreach (int place in places)
        {
            if (runs.Count > 0 && runs[^1].Start + runs[^1].Count == place)
            {
                runs[^1] = (runs[^1].Start, runs[^1].Count + 1);
            }
            else
            {
                runs.Add((place, 1));
            }
        }

        return runs;
    }
}

/// <summary>A piece's bytes as they travel: deflated where that makes them smaller, as they are where it does not.</summary>
public static class ChunkCodec
{
    /// <summary>Makes a piece ready to send.</summary>
    /// <param name="data">The piece.</param>
    /// <returns>Whether it is deflated, and the bytes to send.</returns>
    public static (bool Deflated, byte[] Data) Pack(ReadOnlySpan<byte> data)
    {
        using MemoryStream packed = new();
        using (DeflateStream deflate = new(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return packed.Length < data.Length ? (true, packed.ToArray()) : (false, data.ToArray());
    }

    /// <summary>Gets a piece's bytes back, refusing any that do not come to the length its table gave.</summary>
    /// <param name="deflated">Whether the bytes are deflated.</param>
    /// <param name="data">The bytes as sent.</param>
    /// <param name="length">The piece's length.</param>
    /// <returns>The piece, or <see langword="null"/> when the bytes are not a piece of that length.</returns>
    public static byte[]? Unpack(bool deflated, byte[] data, int length)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!deflated)
        {
            return data.Length == length ? data : null;
        }

        try
        {
            using DeflateStream inflate = new(new MemoryStream(data), CompressionMode.Decompress);
            byte[] piece = new byte[length];
            int got = 0;
            for (int read = 1; got < length && read > 0; got += read)
            {
                read = inflate.Read(piece, got, length - got);
            }

            // Never more than the table said: a stream that goes on is no piece of that length.
            return got == length && inflate.ReadByte() < 0 ? piece : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
