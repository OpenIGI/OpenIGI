using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Text;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Host;

internal sealed partial class GameHost
{
    /// <summary>The category memory reports are logged under.</summary>
    internal const string MemoryLogCategory = "Memory";

    /// <summary>Registers <c>mem_report</c>.</summary>
    /// <param name="console">The console to register with.</param>
    private void RegisterMemoryCommands(ConsoleSystem console)
    {
        console.Commands.Add(
            "mem_report",
            "Prints where the game's memory is: the managed heap, the process, the graphics device and its caches.",
            // Logged rather than printed, so the report reaches the console, the log file and standard output
            // alike: a scripted soak (-runat, -backdropcycle) reads it back from the last two. The Memory
            // category is on at Information whatever the verbosity; see DesktopLogging.
            _ => _logger.Information(MemoryLogCategory, DescribeMemory()));
    }

    /// <summary>Describes where the process's memory is, managed and native.</summary>
    /// <returns>A multi-line report.</returns>
    private string DescribeMemory()
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

        text.Append(_device.DescribeMemory());
        return text.ToString().TrimEnd('\n');
    }

    /// <summary>Gives back what a finished load leaves idle: upload staging, and the large-object heap's holes.</summary>
    /// <remarks>
    /// Called under the held frame of a backdrop swap, where a collection's pause is not seen. A level's
    /// worth of decoded pictures has just gone through the large-object heap, which the collector does
    /// not compact unless asked -- the holes they leave are memory the process keeps.
    /// </remarks>
    private void TrimAfterLoad()
    {
        _device.TrimMemory();
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    /// <summary>Drops what a level's load kept for its uploads, once they are done.</summary>
    /// <remarks>
    /// The decode cache holds every picture it decoded until the renderer takes it; whatever the level
    /// never drew stays there for as long as the level does. Once the level is resident nothing more will
    /// be taken, so the rest goes, and the staging that carried the uploads with it.
    /// </remarks>
    private void ReleaseLoadLeftovers()
    {
        if (_world?.Decoded is { } decoded)
        {
            decoded.ReleaseUntaken();
            _world.Decoded = null;
        }

        _detailMaps.ReleaseUnclaimed();

        _device.TrimMemory();
    }

    private static string Mib(long bytes) =>
        FormattableString.Invariant($"{bytes / (1024.0 * 1024.0):F0} MiB");
}
