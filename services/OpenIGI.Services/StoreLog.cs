namespace OpenIGI.Services;

/// <summary>The messages the release and content stores log, written once for both.</summary>
internal static partial class StoreLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Indexed {Count} {Kind}, tag {Tag}")]
    public static partial void Scanned(ILogger logger, int count, string kind, string tag);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped {Path}: {Reason}")]
    public static partial void BadManifest(ILogger logger, string path, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {Store} store {Root} does not exist")]
    public static partial void MissingRoot(ILogger logger, string store, string root);

    [LoggerMessage(Level = LogLevel.Error, Message = "Scanning the {Store} store failed: {Reason}")]
    public static partial void ScanFailed(ILogger logger, string store, string reason);
}
