using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenIGI.Releases;

namespace OpenIGI.Services;

/// <summary>Keeps the current <see cref="ReleaseIndex"/>, rescanning the download store as releases land.</summary>
/// <remarks>
/// The release workflow uploads a release's archives first and its <c>release.json</c> last, so a release
/// with a manifest is a complete one: a scan that runs mid-upload simply does not see it yet. A manifest
/// that does not parse is skipped and logged rather than taking the whole feed down with it.
/// </remarks>
internal sealed partial class ReleaseStore(IOptions<ReleaseOptions> options, ILogger<ReleaseStore> logger) : BackgroundService
{
    private readonly ReleaseOptions _options = options.Value;
    private string _fingerprint = string.Empty;

    /// <summary>Gets the most recent scan.</summary>
    public ReleaseIndex Current { get; private set; } = ReleaseIndex.Empty;

    /// <summary>Scans the store now, replacing <see cref="Current"/> when anything changed.</summary>
    public void Scan()
    {
        if (!Directory.Exists(_options.Root))
        {
            LogMissingRoot(logger, _options.Root);
            return;
        }

        List<string> manifests = [.. Directory
            .EnumerateFiles(_options.Root, ReleaseManifest.FileName, SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateDirectories(_options.Root)
                .Select(static dir => Path.Combine(dir, ReleaseManifest.FileName))
                .Where(File.Exists))
            .Order(StringComparer.Ordinal)];

        // Cheap enough to do every few seconds: one stat per release, and the manifests are only read again
        // when one of them was added, removed or rewritten.
        string fingerprint = string.Join('|', manifests.Select(static path => $"{path}@{File.GetLastWriteTimeUtc(path).Ticks}"));
        if (fingerprint == _fingerprint)
        {
            return;
        }

        List<ReleaseManifest> releases = [];
        foreach (string path in manifests)
        {
            try
            {
                ReleaseManifest manifest = ReleaseJson.ReadManifest(File.ReadAllText(path));
                _ = manifest.ParsedVersion;
                releases.Add(manifest);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or IOException)
            {
                LogBadManifest(logger, path, ex.Message);
            }
        }

        Current = new ReleaseIndex(releases, _options.DownloadUrl);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Indexed {Count} release(s), tag {Tag}")]
    private static partial void LogScanned(ILogger logger, int count, string tag);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped {Path}: {Reason}")]
    private static partial void LogBadManifest(ILogger logger, string path, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The release store {Root} does not exist")]
    private static partial void LogMissingRoot(ILogger logger, string root);

    [LoggerMessage(Level = LogLevel.Error, Message = "Scanning the release store failed: {Reason}")]
    private static partial void LogScanFailed(ILogger logger, string reason);
}
