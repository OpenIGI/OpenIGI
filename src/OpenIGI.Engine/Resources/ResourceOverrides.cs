using OpenIGI.Core;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Resources;

namespace OpenIGI.Engine.Resources;

/// <summary>
/// A directory of loose files that stand in for the game's own assets, addressed the way the
/// installation addresses them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The folder mirrors the game.</b> An asset is overridden by putting a file where that asset lives
/// in the installation — <c>overrides/common/textures/000_04_1.tex</c> replaces the shared texture,
/// <c>overrides/missions/location0/level10/textures/000_04_1.tex</c> replaces level 10's own, and
/// <c>overrides/missions/location0/level1/terrain/terrain.tex</c> replaces a file that was already
/// loose. Nothing has to be declared and there is no manifest: the path IS the identity.
/// </para>
/// <para>
/// <b>That is what makes it able to replace an archived asset at all.</b> Most of this game's art lives
/// inside <c>.res</c> archives and is addressed by a name that is not unique — the art is layered, so
/// <c>common</c>, a location and a level can each ship a different <c>000_04_1</c>, and which one a
/// level draws depends on mount order. A replacement keyed by that name could not tell them apart. A
/// replacement keyed by WHERE THE ARCHIVE IS can, because an archive has exactly one place.
/// </para>
/// <para>
/// <b>Rescannable by design.</b> <see cref="Rescan"/> rebuilds the index and reports what moved, so a
/// file dropped in while the game is running can be picked up without a restart. The bytes are read on
/// demand and cached against the file's timestamp and length, so a rescan that finds nothing new costs
/// a directory walk and nothing else.
/// </para>
/// </remarks>
public sealed class ResourceOverrides
{
    private const string LogCategory = "Overrides";

    /// <summary>The directory name, relative to the installation root.</summary>
    public const string DirectoryName = "overrides";

    private readonly IIgiLogger _logger;
    private readonly Dictionary<string, Candidate> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes a new instance of the <see cref="ResourceOverrides"/> class.</summary>
    /// <param name="root">The overrides directory, or <see langword="null"/> or absent for none.</param>
    /// <param name="logger">Where to report what was found.</param>
    public ResourceOverrides(string? root, IIgiLogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;

        if (!string.IsNullOrWhiteSpace(root))
        {
            Root = Path.GetFullPath(root);
        }
    }

    /// <summary>Gets the overrides directory, or <see langword="null"/> when none was configured.</summary>
    public string? Root { get; }

    /// <summary>Gets how many files the last scan found.</summary>
    public int Count => _files.Count;

    /// <summary>Gets or sets a value indicating whether every override is ignored.</summary>
    /// <remarks>
    /// The comparison switch. Bypassing rather than unloading is deliberate: the index and the bytes stay
    /// where they are, so flipping it back costs nothing and a folder somebody spent an hour building is
    /// never thrown away by a keypress.
    /// </remarks>
    public bool Bypassed { get; set; }

    /// <summary>Gets a value indicating whether any override is present and in use.</summary>
    public bool IsActive => !Bypassed && _files.Count > 0;

    /// <summary>Gets every override path found, relative to the root and normalised.</summary>
    public IReadOnlyCollection<string> Paths => _files.Keys;

    /// <summary>Rebuilds the index from the directory.</summary>
    /// <returns>What changed since the previous scan.</returns>
    /// <remarks>
    /// Safe to call at any time, including with no directory configured, in which case it reports an
    /// empty scan rather than failing — an installation with no overrides is the ordinary one.
    /// </remarks>
    public ScanResult Rescan()
    {
        Dictionary<string, Candidate> previous = new(_files, StringComparer.OrdinalIgnoreCase);
        _files.Clear();

        if (Root is null || !Directory.Exists(Root))
        {
            return new ScanResult(0, 0, previous.Count, 0);
        }

        int added = 0;
        int changed = 0;

        foreach (string path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            string relative = ResourcePath.Normalize(Path.GetRelativePath(Root, path));

            if (relative.Length == 0)
            {
                continue;
            }

            FileInfo info;

            try
            {
                info = new FileInfo(path);
            }
            catch (IOException)
            {
                continue;
            }

            Candidate candidate = new(path, info.Length, info.LastWriteTimeUtc);

            if (!previous.TryGetValue(relative, out Candidate existing))
            {
                added++;
            }
            else if (existing.Length != candidate.Length || existing.Written != candidate.Written)
            {
                changed++;
            }
            else
            {
                // Unchanged: carry the decoded bytes across so a rescan does not throw away work.
                candidate = existing;
            }

            _files[relative] = candidate;
        }

        int removed = previous.Keys.Count(key => !_files.ContainsKey(key));

        return new ScanResult(_files.Count, added, removed, changed);
    }

    /// <summary>Determines whether a path is overridden.</summary>
    /// <param name="relativePath">The path, from one of the <see cref="OverridePath"/>.</param>
    /// <returns><see langword="true"/> when an override file is present.</returns>
    public bool Has(string relativePath) =>
        relativePath.Length > 0 && _files.ContainsKey(relativePath);

    /// <summary>Reads an override's bytes.</summary>
    /// <param name="relativePath">The path, from one of the <see cref="OverridePath"/>.</param>
    /// <param name="data">Receives the bytes, or empty when there is no such override.</param>
    /// <returns><see langword="true"/> when an override was read.</returns>
    /// <remarks>
    /// Read once and kept. A resource's bytes are handed out as a <see cref="ReadOnlyMemory{T}"/> that
    /// the registry holds for as long as the entry lives, so they cannot be a transient buffer.
    /// </remarks>
    public bool TryRead(string relativePath, out ReadOnlyMemory<byte> data)
    {
        data = default;

        if (Bypassed || relativePath.Length == 0 || !_files.TryGetValue(relativePath, out Candidate candidate))
        {
            return false;
        }

        if (candidate.Data is { } cached)
        {
            data = cached;
            return true;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(candidate.Path);
            _files[relativePath] = candidate with { Data = bytes };
            data = bytes;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(LogCategory, $"Override '{relativePath}' could not be read: {error.Message}");
            return false;
        }
    }

    /// <summary>What a <see cref="Rescan"/> found.</summary>
    /// <param name="Total">How many override files are present now.</param>
    /// <param name="Added">How many were not there before.</param>
    /// <param name="Removed">How many have gone.</param>
    /// <param name="Changed">How many are the same path with different contents.</param>
    public readonly record struct ScanResult(int Total, int Added, int Removed, int Changed)
    {
        /// <summary>Gets a value indicating whether anything moved since the previous scan.</summary>
        public bool Moved => Added > 0 || Removed > 0 || Changed > 0;

        /// <inheritdoc/>
        public override string ToString() =>
            $"{Total} override(s); {Added} new, {Changed} changed, {Removed} gone";
    }

    private readonly record struct Candidate(string Path, long Length, DateTime Written)
    {
        public byte[]? Data { get; init; }
    }
}
