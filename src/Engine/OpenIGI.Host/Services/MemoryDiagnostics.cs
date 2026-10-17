using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Text;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Presentation;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Services;

/// <summary>Where the game's memory is, and giving back what a finished load leaves idle.</summary>
/// <param name="device">The graphics device, whose own allocations and caches are reported and trimmed.</param>
/// <param name="logger">Where reports are logged.</param>
internal sealed class MemoryDiagnostics(IGraphicsDevice device, IOpenIgiLogger logger) : IHostService
{
    /// <summary>The category memory reports are logged under.</summary>
    public const string LogCategory = "Memory";

    /// <inheritdoc/>
    public string Name => "Memory";

    /// <summary>Registers <c>mem_report</c>.</summary>
    /// <param name="console">The console to register with.</param>
    public void Register(ConsoleSystem console)
    {
        ArgumentNullException.ThrowIfNull(console);
        console.Commands.Add(
            "mem_report",
            "Prints where the game's memory is: the managed heap, the process, the graphics device and its caches.",
            // Logged rather than printed, so the report reaches the console, the log file and standard output
            // alike: a scripted soak (-runat, -backdropcycle) reads it back from the last two. The Memory
            // category is on at Information whatever the verbosity; see DesktopLogging.
            _ => logger.Information(LogCategory, Describe()));
    }

    /// <summary>Logs where the memory is, after something worth measuring.</summary>
    /// <param name="after">What has just happened, which heads the report.</param>
    public void Report(string after) => logger.Information(LogCategory, $"{after}:\n{Describe()}");

    /// <summary>Describes where the process's memory is, managed and native.</summary>
    /// <returns>A multi-line report.</returns>
    public string Describe()
    {
        var text = new StringBuilder();
        CultureInfo invariant = CultureInfo.InvariantCulture;

        using (Process self = Process.GetCurrentProcess())
        {
            text.Append(invariant, $"Process: {Mib(self.PrivateMemorySize64)} private, {Mib(self.WorkingSet64)} working set.\n");
        }

        GCMemoryInfo gc = GC.GetGCMemoryInfo();
        long largeObjects = gc.GenerationInfo.Length > 3 ? gc.GenerationInfo[3].SizeAfterBytes : 0;
        text.Append(invariant,
            $"Managed: {Mib(gc.HeapSizeBytes)} heap ({Mib(largeObjects)} large objects), "
            + $"{Mib(gc.FragmentedBytes)} fragmented, {Mib(gc.TotalCommittedBytes)} committed; "
            + $"{GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)} collections; "
            + $"{(GCSettings.IsServerGC ? "server" : "workstation")} GC.\n");

        text.Append(device.DescribeMemory());
        return text.ToString().TrimEnd('\n');
    }

    /// <summary>Gives back what a finished load leaves idle: upload staging, and the large-object heap's holes.</summary>
    /// <remarks>
    /// Called under the held frame of a backdrop swap, where a collection's pause is not seen. A level's
    /// worth of decoded pictures has just gone through the large-object heap, which the collector does
    /// not compact unless asked -- the holes they leave are memory the process keeps.
    /// </remarks>
    public void TrimAfterLoad()
    {
        device.TrimMemory();
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    /// <summary>Drops what a level's load kept for its uploads, once they are done.</summary>
    /// <param name="world">The world's renderer, whose decode cache holds every picture it decoded.</param>
    /// <param name="detailMaps">The material detail maps, whose unclaimed entries go.</param>
    /// <remarks>
    /// The decode cache holds every picture it decoded until the renderer takes it; whatever the level
    /// never drew stays there for as long as the level does. Once the level is resident nothing more will
    /// be taken, so the rest goes, and the staging that carried the uploads with it.
    /// </remarks>
    public void ReleaseLoadLeftovers(IWorldPresenter? world, MaterialDetailPack detailMaps)
    {
        ArgumentNullException.ThrowIfNull(detailMaps);
        if (world?.Decoded is { } decoded)
        {
            decoded.ReleaseUntaken();
            world.Decoded = null;
        }

        detailMaps.ReleaseUnclaimed();
        device.TrimMemory();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    private static string Mib(long bytes) =>
        FormattableString.Invariant($"{bytes / (1024.0 * 1024.0):F0} MiB");
}
