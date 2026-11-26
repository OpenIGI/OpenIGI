using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenIGI.Releases.Maps;

/// <summary>What a community map says of itself, at the head of its package (<see cref="MapPackage"/>).</summary>
/// <param name="Schema">The format; <see cref="CurrentSchema"/> for everything written now.</param>
/// <param name="Id">The map's identifier: lowercase letters, digits and inner hyphens, as a content pack's.</param>
/// <param name="Title">Its name as players see it.</param>
/// <param name="Summary">What it is, in a sentence or two.</param>
/// <param name="Game">The game it is for, by title id: <c>igi1</c> or <c>igi2</c>.</param>
/// <param name="Author">Who made it, as they call themselves.</param>
/// <param name="MissionId">The mission it is, as its script's <c>DefineMission</c> numbers it.</param>
/// <param name="MissionDirectory">The mission's folder, every file of the map inside it, such as <c>missions/location0/level15</c>.</param>
/// <param name="Files">The mission's files, by path from the game's root, in ordinal order.</param>
/// <param name="RemixOf">The map it was made from, by id, or <see langword="null"/>.</param>
public sealed record MapManifest(
    int Schema,
    string Id,
    string Title,
    string Summary,
    string Game,
    string Author,
    int MissionId,
    string MissionDirectory,
    IReadOnlyList<MapFile> Files,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RemixOf = null)
{
    /// <summary>The format this library writes.</summary>
    public const int CurrentSchema = 1;

    /// <summary>Gets the bytes the package carries itself: every file that is not the game's own.</summary>
    [JsonIgnore]
    public long EmbeddedSize => Files.Where(static f => f.Origin is null).Sum(static f => f.Size);
}

/// <summary>One file of a map.</summary>
/// <param name="Path">Where it goes, from the game's root, with forward slashes.</param>
/// <param name="Size">Its length in bytes.</param>
/// <param name="Sha256">The SHA-256 of its bytes, lowercase hex.</param>
/// <param name="Origin">
/// Where the game's own copy of it is, from the game's root, when it is a file of the game the map uses unchanged (a copied
/// level's terrain, say): the map then carries only its name and hash, and the player's own game supplies its bytes. Null for
/// a file the map carries itself.
/// </param>
public sealed record MapFile(
    string Path,
    long Size,
    string Sha256,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Origin = null);

/// <summary>
/// A community map as one file: <c>OIGIMAP1</c>, the manifest's length (four bytes, little-endian), the manifest as JSON, then
/// the bytes of every file the map carries, in the manifest's order. The game's own files it uses are named, not carried.
/// </summary>
/// <remarks>
/// One file, read front to back, so a server can check it as it arrives with nothing but a hash: no archive format to trust.
/// </remarks>
public static class MapPackage
{
    /// <summary>The extension a map's package is saved with.</summary>
    public const string Extension = ".oigimap";

    private static readonly byte[] Magic = "OIGIMAP1"u8.ToArray();

    /// <summary>Writes a package.</summary>
    /// <param name="manifest">What it says of itself; its files in the order their bytes follow.</param>
    /// <param name="contents">Each carried file's bytes, by path.</param>
    /// <param name="into">Where it is written.</param>
    /// <exception cref="ArgumentException">A carried file's bytes are missing or are not what the manifest says.</exception>
    public static void Write(MapManifest manifest, Func<string, ReadOnlyMemory<byte>> contents, Stream into)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(into);
        byte[] head = JsonSerializer.SerializeToUtf8Bytes(manifest, MapJsonContext.Default.MapManifest);
        into.Write(Magic);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, head.Length);
        into.Write(length);
        into.Write(head);
        foreach (MapFile file in manifest.Files.Where(static f => f.Origin is null))
        {
            ReadOnlyMemory<byte> bytes = contents(file.Path);
            if (bytes.Length != file.Size || Hash(bytes.Span) != file.Sha256)
            {
                throw new ArgumentException($"{file.Path} is not the file the manifest names.", nameof(contents));
            }

            into.Write(bytes.Span);
        }
    }

    /// <summary>Reads a package: its manifest, and each carried file's bytes checked against it.</summary>
    /// <param name="package">The package.</param>
    /// <returns>The manifest, and each carried file's bytes by path.</returns>
    /// <exception cref="InvalidDataException">It is not a map, or a file is not what the manifest says.</exception>
    public static (MapManifest Manifest, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Files) Read(ReadOnlyMemory<byte> package)
    {
        MapManifest manifest = ReadManifest(package.Span, out int offset);
        Dictionary<string, ReadOnlyMemory<byte>> files = new(StringComparer.Ordinal);
        foreach (MapFile file in manifest.Files.Where(static f => f.Origin is null))
        {
            if (file.Size < 0 || offset + file.Size > package.Length)
            {
                throw new InvalidDataException($"The package ends inside {file.Path}.");
            }

            ReadOnlyMemory<byte> bytes = package.Slice(offset, (int)file.Size);
            if (Hash(bytes.Span) != file.Sha256)
            {
                throw new InvalidDataException($"{file.Path} is not the file the manifest names.");
            }

            files[file.Path] = bytes;
            offset += (int)file.Size;
        }

        if (offset != package.Length)
        {
            throw new InvalidDataException("The package runs on past its last file.");
        }

        return (manifest, files);
    }

    /// <summary>Reads a package's manifest alone.</summary>
    /// <param name="package">The package, or as much of its start as holds the manifest.</param>
    /// <param name="offset">Where the first carried file's bytes begin.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="InvalidDataException">It is not a map.</exception>
    public static MapManifest ReadManifest(ReadOnlySpan<byte> package, out int offset)
    {
        if (package.Length < Magic.Length + 4 || !package[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a map package.");
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(package[Magic.Length..]);
        offset = Magic.Length + 4 + length;
        if (length <= 0 || length > MapRules.MaxManifestBytes || offset > package.Length)
        {
            throw new InvalidDataException("The map's manifest is cut short or too long.");
        }

        try
        {
            return JsonSerializer.Deserialize(package.Slice(Magic.Length + 4, length), MapJsonContext.Default.MapManifest)
                ?? throw new InvalidDataException("The map's manifest is empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"The map's manifest is not readable: {error.Message}", error);
        }
    }

    /// <summary>The SHA-256 of some bytes, lowercase hex.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The hash.</returns>
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

/// <summary>The JSON the map format reads and writes, generated ahead of time.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MapManifest))]
[JsonSerializable(typeof(MapListing))]
[JsonSerializable(typeof(IReadOnlyList<MapListing>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class MapJsonContext : JsonSerializerContext;
