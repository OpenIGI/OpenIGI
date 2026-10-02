using System.Text.Json.Serialization;

namespace OpenIGI.Releases;

/// <summary>One version of an optional content pack, as stored beside it on the server as <c>content.json</c>.</summary>
/// <remarks>
/// <para>
/// A pack is a folder of files the game reads -- the high resolution textures are an overrides folder -- and
/// is published apart from the game's releases, on its own version line. Every file is listed as the
/// content-defined chunks it is made of (<see cref="ContentChunker"/>), and the chunks are stored once each
/// on the server, by hash, for every version of every pack together.
/// </para>
/// <para>
/// That is what makes an update small. A new version of a pack names its files' chunks the same way, so the
/// launcher fetches only the chunks it does not already hold in the version it has: a texture rebuilt inside a
/// quarter-gigabyte archive costs the chunks around that texture, not the archive.
/// </para>
/// </remarks>
/// <param name="Schema">The manifest format; <see cref="CurrentSchema"/> for everything written now.</param>
/// <param name="Id">The pack's identifier: lowercase letters, digits and hyphens, such as <c>hd-textures</c>.</param>
/// <param name="Version">The pack's own version, without a leading <c>v</c>.</param>
/// <param name="Title">The pack's name as players see it.</param>
/// <param name="Summary">One or two sentences on what it changes.</param>
/// <param name="Game">The game it is for, by title id, such as <c>igi1</c>.</param>
/// <param name="MinGameVersion">
/// The oldest game build that can read it, or <see langword="null"/> when any can. The block-compressed texture
/// pack needs a build that understands block-compressed pictures.
/// </param>
/// <param name="Published">When this version was published.</param>
/// <param name="Size">The installed size: every file's length together.</param>
/// <param name="Files">The files, by path relative to the pack's root, in ordinal order.</param>
public sealed record ContentManifest(
    int Schema,
    string Id,
    string Version,
    string Title,
    string Summary,
    string Game,
    string? MinGameVersion,
    DateTimeOffset Published,
    long Size,
    IReadOnlyList<ContentFile> Files)
{
    /// <summary>The manifest format this library writes.</summary>
    public const int CurrentSchema = 1;

    /// <summary>The file name a manifest is stored under, in its version's folder.</summary>
    public const string FileName = "content.json";

    /// <summary>Gets the parsed <see cref="Version"/>.</summary>
    /// <exception cref="FormatException">The manifest's version is not a semantic version.</exception>
    [JsonIgnore]
    public ReleaseVersion ParsedVersion => ReleaseVersion.Parse(Version);

    /// <summary>Gets the listing entry that stands for this version in the content index.</summary>
    [JsonIgnore]
    public ContentPack Listing => new(Id, Version, Title, Summary, Game, MinGameVersion, Published, Size, DownloadSize);

    /// <summary>The largest picture a pack may have, in bytes.</summary>
    public const int MaxThumbnailBytes = 2 * 1024 * 1024;

    /// <summary>Gets what installing this version from nothing downloads: every distinct chunk, as stored.</summary>
    [JsonIgnore]
    public long DownloadSize => Files
        .SelectMany(static file => file.Chunks)
        .DistinctBy(static chunk => chunk.Hash)
        .Sum(static chunk => (long)chunk.StoredSize);

    /// <summary>Whether an identifier is one a pack may have.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns><see langword="true"/> for lowercase letters, digits and inner hyphens, at most 64 long.</returns>
    public static bool IsValidId(string? id) =>
        id is { Length: > 0 and <= 64 }
        && id[0] != '-'
        && id[^1] != '-'
        && id.All(static c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
}

/// <summary>One file of a content pack.</summary>
/// <param name="Path">Its path relative to the pack's root, with forward slashes.</param>
/// <param name="Size">Its length in bytes.</param>
/// <param name="Sha256">The SHA-256 of the whole file, lowercase hex.</param>
/// <param name="Chunks">The chunks it is made of, in order; their sizes add up to <see cref="Size"/>.</param>
public sealed record ContentFile(string Path, long Size, string Sha256, IReadOnlyList<ContentChunk> Chunks);

/// <summary>One stored chunk of a content file.</summary>
/// <param name="Hash">The SHA-256 of the chunk's bytes, lowercase hex; also its name on the server.</param>
/// <param name="Size">Its length in bytes.</param>
/// <param name="StoredSize">The length of the compressed object the server stores and sends for it.</param>
public sealed record ContentChunk(string Hash, int Size, int StoredSize);

/// <summary>A content pack as the index lists it: its newest version, without the file list.</summary>
/// <param name="Id">The pack's identifier.</param>
/// <param name="Version">Its newest version.</param>
/// <param name="Title">Its name as players see it.</param>
/// <param name="Summary">What it changes.</param>
/// <param name="Game">The game it is for, by title id.</param>
/// <param name="MinGameVersion">The oldest game build that can read it, if there is one.</param>
/// <param name="Published">When that version was published.</param>
/// <param name="Size">Its installed size.</param>
/// <param name="Download">What installing it from nothing downloads.</param>
/// <param name="Thumbnail">The address of the pack's picture, or <see langword="null"/> when it has none.</param>
public sealed record ContentPack(
    string Id,
    string Version,
    string Title,
    string Summary,
    string Game,
    string? MinGameVersion,
    DateTimeOffset Published,
    long Size,
    long Download,
    string? Thumbnail = null)
{
    /// <summary>Gets the parsed <see cref="Version"/>.</summary>
    /// <exception cref="FormatException">The version is not a semantic version.</exception>
    [JsonIgnore]
    public ReleaseVersion ParsedVersion => ReleaseVersion.Parse(Version);

    /// <summary>Whether a game build is new enough to read the pack.</summary>
    /// <param name="gameVersion">The installed game's version, or <see langword="null"/> when none is.</param>
    /// <returns>
    /// <see langword="true"/> when the pack names no minimum, or the game is at least that version.
    /// </returns>
    public bool SupportsGame(string? gameVersion)
    {
        if (MinGameVersion is null)
        {
            return true;
        }

        return gameVersion is not null
            && ReleaseVersion.TryParse(gameVersion, out ReleaseVersion installed)
            && ReleaseVersion.TryParse(MinGameVersion, out ReleaseVersion minimum)
            && installed.CompareTo(minimum) >= 0;
    }
}

/// <summary>What the update service lists of the optional content: every pack's newest version.</summary>
/// <param name="ChunkUrl">
/// Where a chunk is downloaded from, with <c>{h2}</c> for the first two characters of its hash and
/// <c>{hash}</c> for the whole hash.
/// </param>
/// <param name="Packs">The packs, by identifier.</param>
public sealed record ContentIndex(string ChunkUrl, IReadOnlyList<ContentPack> Packs)
{
    /// <summary>Works out where one chunk is downloaded from.</summary>
    /// <param name="hash">The chunk's hash.</param>
    /// <returns>Its address.</returns>
    public Uri ChunkAddress(string hash) =>
        new(ChunkUrl.Replace("{h2}", hash[..2], StringComparison.Ordinal)
            .Replace("{hash}", hash, StringComparison.Ordinal));
}

/// <summary>
/// What describes a pack as a whole rather than one version of it, stored as <c>&lt;pack&gt;/pack.json</c>: the
/// name and summary players see, and its picture.
/// </summary>
/// <remarks>
/// Kept apart from the versions so a pack can be renamed, described again or given a new picture without
/// publishing a version: nothing a player has installed changes, and nothing is downloaded but the picture.
/// Each field that is set overrides what the newest version's manifest says.
/// </remarks>
/// <param name="Schema">The format; <see cref="CurrentSchema"/> for everything written now.</param>
/// <param name="Id">The pack.</param>
/// <param name="Title">The name players see, or <see langword="null"/> to keep the manifest's.</param>
/// <param name="Summary">The summary players see, or <see langword="null"/> to keep the manifest's.</param>
/// <param name="Thumbnail">
/// The picture's file name beside <c>pack.json</c>, <c>thumbnail-&lt;hash&gt;.&lt;png|jpg|webp&gt;</c>, or
/// <see langword="null"/> for none. Named by its content, so a new picture is a new name and can be cached for good.
/// </param>
public sealed record ContentPackInfo(int Schema, string Id, string? Title, string? Summary, string? Thumbnail)
{
    /// <summary>The format this library writes.</summary>
    public const int CurrentSchema = 1;

    /// <summary>The file name it is stored under, in the pack's folder.</summary>
    public const string FileName = "pack.json";

    /// <summary>Whether a name is one a pack's picture may have.</summary>
    /// <param name="name">The file name.</param>
    /// <returns><see langword="true"/> for <c>thumbnail-</c>, twelve hex digits, and a PNG, JPEG or WebP extension.</returns>
    public static bool IsValidThumbnail(string? name) =>
        name is { Length: > 0 }
        && System.Text.RegularExpressions.Regex.IsMatch(name, @"^thumbnail-[0-9a-f]{12}\.(png|jpg|webp)$");

    /// <summary>Works out a picture's extension from its first bytes.</summary>
    /// <param name="data">The picture.</param>
    /// <returns><c>png</c>, <c>jpg</c> or <c>webp</c>; <see langword="null"/> for anything else.</returns>
    public static string? PictureExtension(ReadOnlySpan<byte> data) =>
        data.StartsWith((byte[])[0x89, (byte)'P', (byte)'N', (byte)'G']) ? "png"
        : data.StartsWith((byte[])[0xFF, 0xD8, 0xFF]) ? "jpg"
        : data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8) ? "webp"
        : null;

    /// <summary>Applies the pack's own description over a version's listing.</summary>
    /// <param name="listing">The newest version's listing.</param>
    /// <param name="thumbnailUrl">Where the picture is served from, given the pack and the file name.</param>
    /// <returns>The listing as players see it.</returns>
    public ContentPack Apply(ContentPack listing, Func<string, string, string> thumbnailUrl)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(thumbnailUrl);
        return listing with
        {
            Title = string.IsNullOrWhiteSpace(Title) ? listing.Title : Title,
            Summary = string.IsNullOrWhiteSpace(Summary) ? listing.Summary : Summary,
            Thumbnail = IsValidThumbnail(Thumbnail) ? thumbnailUrl(listing.Id, Thumbnail!) : null,
        };
    }
}
