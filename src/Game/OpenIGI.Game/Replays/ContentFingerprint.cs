using System.Security.Cryptography;
using System.Text;
using OpenIGI.Game.Multiplayer;

namespace OpenIGI.Game.Replays;

/// <summary>
/// What a level was played from, in one value: the SHA-256 of the list of files that make how it plays (the same files a
/// multiplayer server holds its players to, <see cref="GameplayFileRules"/>), each by path, length and hash, in path order.
/// </summary>
/// <remarks>
/// The textures, sounds and lightmaps are not in it -- they only look or sound -- so a player may light the level as they like;
/// a changed script, model or table changes it, and a replay played from other files is not the run it was.
/// </remarks>
public static class ContentFingerprint
{
    /// <summary>The fingerprint of a level's files.</summary>
    /// <param name="files">The files, as <see cref="GameplayFileRules.Build"/> lists them.</param>
    /// <returns>The fingerprint, as 64 lower-case hex digits.</returns>
    public static string Of(SessionFileList files)
    {
        ArgumentNullException.ThrowIfNull(files);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> size = stackalloc byte[8];
        foreach (SessionFile file in files.Files.OrderBy(static f => f.Path, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file.Path));
            hash.AppendData([0]);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(size, file.Size);
            hash.AppendData(size);
            hash.AppendData(file.Hash);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
