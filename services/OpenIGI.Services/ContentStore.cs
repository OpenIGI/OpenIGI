using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenIGI.Releases;

namespace OpenIGI.Services;

/// <summary>Where the optional content is and where its chunks are downloaded from (the <c>Content</c> section).</summary>
internal sealed class ContentOptions
{
    /// <summary>Gets or sets the content store: <c>&lt;pack&gt;/&lt;version&gt;/content.json</c> and <c>chunks/</c>.</summary>
    public string Root { get; set; } = "/srv/dl/content";

    /// <summary>Gets or sets where a chunk is downloaded from; see <see cref="ContentIndex.ChunkUrl"/>.</summary>
    public string ChunkUrl { get; set; } = "https://dl.openigi.com/content/chunks/{h2}/{hash}.br";

    /// <summary>Gets or sets where a pack's picture is downloaded from, with <c>{pack}</c> and <c>{name}</c>.</summary>
    public string ThumbnailUrl { get; set; } = "https://dl.openigi.com/content/{pack}/{name}";

    /// <summary>Gets or sets how often the store is looked at for new versions.</summary>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>Keeps the current <see cref="ContentLibrary"/>, rescanning the content store as packs are published.</summary>
/// <remarks>
/// A publish moves a version's chunks in first and its <c>content.json</c> last, as a release does with its
/// <c>release.json</c>, so a version with a manifest is complete: every chunk it names is already there.
/// </remarks>
internal sealed partial class ContentStore(IOptions<ContentOptions> options, ILogger<ContentStore> logger) : BackgroundService
{
    private readonly ContentOptions _options = options.Value;
    // Null until the first scan, so even an empty store builds its index once: the index carries the chunk
    // address, which the empty library from before the first scan does not have.
    private string? _fingerprint;

    /// <summary>Gets the most recent scan.</summary>
    public ContentLibrary Current { get; private set; } = ContentLibrary.Empty;

    /// <summary>Scans the store now, replacing <see cref="Current"/> when anything changed.</summary>
    public void Scan()
    {
        if (!Directory.Exists(_options.Root))
        {
            // No content published yet is a normal state, not a fault: the index is simply empty.
            if (_fingerprint != "-")
            {
                Current = new ContentLibrary([], _options.ChunkUrl, null, _options.ThumbnailUrl);
                _fingerprint = "-";
            }

            return;
        }

        List<string> packs = [.. Directory
            .EnumerateDirectories(_options.Root)
            .Where(static dir => ContentManifest.IsValidId(Path.GetFileName(dir)))
            .Order(StringComparer.Ordinal)];
        List<string> manifests = [.. packs
            .SelectMany(static pack => Directory.EnumerateDirectories(pack))
            .Select(static version => Path.Combine(version, ContentManifest.FileName))
            .Where(File.Exists)
            .Order(StringComparer.Ordinal)];
        List<string> descriptions = [.. packs
            .Select(static pack => Path.Combine(pack, ContentPackInfo.FileName))
            .Where(File.Exists)];

        string fingerprint = string.Join('|', manifests.Concat(descriptions).Select(static path => $"{path}@{File.GetLastWriteTimeUtc(path).Ticks}"));
        if (fingerprint == _fingerprint)
        {
            return;
        }

        List<(ContentManifest, string)> versions = [];
        foreach (string path in manifests)
        {
            try
            {
                string json = File.ReadAllText(path);
                ContentManifest manifest = ReleaseJson.ReadContent(json);
                _ = manifest.ParsedVersion;

                // The folders are the truth the deploy verb wrote; a manifest that disagrees with where it
                // sits would be served under one name and describe another.
                string version = Path.GetFileName(Path.GetDirectoryName(path)!);
                string id = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))!);
                if (manifest.Id != id || manifest.Version != version)
                {
                    throw new FormatException($"it names {manifest.Id} {manifest.Version} but sits in {id}/{version}");
                }

                versions.Add((manifest, json));
            }
            catch (Exception ex) when (ex is JsonException or FormatException or IOException)
            {
                LogBadManifest(logger, path, ex.Message);
            }
        }

        Dictionary<string, ContentPackInfo> infos = new(StringComparer.Ordinal);
        foreach (string path in descriptions)
        {
            try
            {
                ContentPackInfo info = ReleaseJson.ReadPackInfo(File.ReadAllText(path));
                string id = Path.GetFileName(Path.GetDirectoryName(path)!);
                if (info.Id != id)
                {
                    throw new FormatException($"it describes {info.Id} but sits in {id}");
                }

                infos[id] = info;
            }
            catch (Exception ex) when (ex is JsonException or FormatException or IOException)
            {
                LogBadManifest(logger, path, ex.Message);
            }
        }

        Current = new ContentLibrary(versions, _options.ChunkUrl, infos, _options.ThumbnailUrl);
        _fingerprint = fingerprint;
        LogScanned(logger, Current.Count, Current.Tag);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(_options.ScanInterval);
        do
        {
            try
            {
                Scan();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogScanFailed(logger, ex.Message);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Indexed {Count} content pack version(s), tag {Tag}")]
    private static partial void LogScanned(ILogger logger, int count, string tag);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped {Path}: {Reason}")]
    private static partial void LogBadManifest(ILogger logger, string path, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Scanning the content store failed: {Reason}")]
    private static partial void LogScanFailed(ILogger logger, string reason);
}
