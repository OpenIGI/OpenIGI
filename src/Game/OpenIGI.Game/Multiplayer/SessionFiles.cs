using System.Security.Cryptography;
using System.Text.RegularExpressions;
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
    public const long MaxFileSize = 64L * 1024 * 1024;

    /// <summary>The most bytes a list's files may come to.</summary>
    public const long MaxTotalSize = 256L * 1024 * 1024;

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
    /// <summary>Gets the game the files are of.</summary>
    string TitleId { get; }

    /// <summary>Lists the files under a folder of the installation, at any depth.</summary>
    /// <param name="directory">The folder, as a list path.</param>
    /// <returns>Their paths, as a list writes them.</returns>
    IEnumerable<string> ListFiles(string directory);

    /// <summary>Reads a file as the session plays it.</summary>
    /// <param name="path">The file, as a list path.</param>
    /// <returns>Its bytes, or <see langword="null"/> when there is none.</returns>
    byte[]? Read(string path);
}

/// <summary>
/// Which of a game's files decide how it plays -- the scripts, the shapes, the ground, the AI's graphs, the weapons -- and
/// so must be a server's own on every player's machine, as against those that only change how it looks or sounds.
/// </summary>
/// <remarks>
/// Textures, lightmaps, sounds and sprites are left out: a player with a texture pack plays the same game as one without.
/// Only Project I.G.I. is described so far, the one game played together; another game has no files to compare.
/// </remarks>
public static partial class GameplayFiles
{
    // Folders every level of the first game plays from, whole.
    private static readonly string[] Igi1SharedFolders =
        ["ammo", "weapons", "humanplayer", "physicsobj", "magicobj", "material", "animtrigger", "common/anims", "common/ai", "missions/location0/common/models"];

    // Files of their own.
    private static readonly string[] Igi1SharedFiles =
        ["lod.qvm", "common/common.dat", "common/common.mtp", "missions/location0/common/location0.dat", "missions/location0/common/location0.mtp"];

    // A level's folder below which only what is seen or heard is left out.
    private static readonly string[] LevelLooks = ["textures/", "lightmaps/", "sounds/"];

    // Never a level's to play from, wherever they lie: pictures, notes, the original's text sources.
    private static readonly string[] NeverExtensions = [".tex", ".qsc", ".bmp", ".tga", ".txt", ".log", ".bak", ".exe", ".dll", ".lnk"];

    /// <summary>Gets whether a path is one a level of a game plays from.</summary>
    /// <param name="titleId">The game.</param>
    /// <param name="path">The path under the installation, any case, either slash.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsGameplay(string titleId, string path)
    {
        if (!string.Equals(titleId, "igi1", StringComparison.Ordinal) || string.IsNullOrEmpty(path))
        {
            return false;
        }

        string p = Normalize(path);
        if (NeverExtensions.Any(e => p.EndsWith(e, StringComparison.Ordinal)))
        {
            return false;
        }

        if (Igi1SharedFiles.Contains(p, StringComparer.Ordinal) || Igi1SharedFolders.Any(f => p.StartsWith(f + "/", StringComparison.Ordinal)))
        {
            return true;
        }

        System.Text.RegularExpressions.Match level = LevelFolder().Match(p);
        return level.Success && !LevelLooks.Any(l => p[level.Length..].StartsWith(l, StringComparison.Ordinal));
    }

    /// <summary>Gets the list a server plays a map from.</summary>
    /// <param name="map">The map, as the session names it: the mission's number.</param>
    /// <param name="source">The installation as the server plays it.</param>
    /// <returns>The list; empty for a game or map whose files are not compared.</returns>
    public static SessionFileList Build(string map, ISessionFileSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        List<SessionFile> files = [];
        if (string.Equals(source.TitleId, "igi1", StringComparison.Ordinal)
            && int.TryParse(map, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int mission))
        {
            SortedSet<string> paths = new(StringComparer.Ordinal);
            foreach (string folder in Igi1SharedFolders.Append($"missions/location0/level{mission}"))
            {
                paths.UnionWith(source.ListFiles(folder).Select(Normalize));
            }

            paths.UnionWith(Igi1SharedFiles);
            foreach (string path in paths.Where(p => IsGameplay(source.TitleId, p) && IsSafePath(p) && BelongsTo(p, mission)))
            {
                if (source.Read(path) is { } data && data.Length <= SessionFileList.MaxFileSize)
                {
                    files.Add(new SessionFile(path, data.Length, SHA256.HashData(data)));
                }
            }
        }

        return new SessionFileList(map, files);
    }

    /// <summary>Gets whether a list of a map may name a path: a file that map's level plays from, or one every level does.</summary>
    /// <param name="titleId">The game.</param>
    /// <param name="map">The map, as the session names it.</param>
    /// <param name="path">The path, as a list writes it.</param>
    /// <returns><see langword="true"/> when it may; a list naming anything else is no list of a level's files at all.</returns>
    public static bool IsListable(string titleId, string map, string path) =>
        IsSafePath(path)
        && IsGameplay(titleId, path)
        && int.TryParse(map, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int mission)
        && BelongsTo(path, mission);

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

    // A level's folder: missions/location0/level<n>/.
    [GeneratedRegex(@"^missions/location0/level(\d+)/")]
    private static partial Regex LevelFolder();

    // Only the map's own level's files, besides the shared ones.
    private static bool BelongsTo(string path, int mission) =>
        LevelFolder().Match(path) is not { Success: true } level
        || level.Groups[1].Value == mission.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A piece of a file a server sent.</summary>
/// <param name="Map">The map of the list the file is in.</param>
/// <param name="Index">The file, by place in that list.</param>
/// <param name="Offset">Where in the file the piece begins.</param>
/// <param name="Data">The piece.</param>
public sealed record FilePiece(string Map, int Index, long Offset, byte[] Data);
