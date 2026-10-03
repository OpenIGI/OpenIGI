using OpenIGI.Core.Diagnostics;
using OpenIGI.Platform.Desktop.Logging;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>Pins the log file's rotation, its fallback when another session holds it, and what it writes.</summary>
public sealed class FileSinkTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "openigi-filesink-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Open_RotatesEarlierSessionsAndKeepsOnlySoMany()
    {
        string path = Path.Combine(_folder, "runtime.log");
        for (int session = 1; session <= 4; session++)
        {
            using FileSink sink = FileSink.Open(path, keep: 2);
            sink.Emit(LogRecord.Now(LogLevel.Information, "Boot", $"session {session}"));
        }

        Assert.Contains("session 4", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Contains("session 3", File.ReadAllText(Path.Combine(_folder, "runtime.1.log")), StringComparison.Ordinal);
        Assert.Contains("session 2", File.ReadAllText(Path.Combine(_folder, "runtime.2.log")), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_folder, "runtime.3.log")));
    }

    [Fact]
    public void Open_WritesBesideAFileAnotherSessionHolds()
    {
        string path = Path.Combine(_folder, "runtime.log");
        using FileSink first = FileSink.Open(path);
        using FileSink second = FileSink.Open(path);

        Assert.Equal(Path.GetFullPath(path), first.Path);
        Assert.Equal(Path.Combine(_folder, $"runtime.{Environment.ProcessId}.log"), second.Path);
    }

    [Fact]
    public void Flush_PutsEveryQueuedRecordOnDisk()
    {
        string path = Path.Combine(_folder, "runtime.log");
        using FileSink sink = FileSink.Open(path);
        for (int i = 0; i < 500; i++)
        {
            sink.Emit(LogRecord.Now(LogLevel.Information, "Load", $"line {i}"));
        }

        sink.Flush();

        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new(stream);
        Assert.Contains("INF Load: line 499", reader.ReadToEnd(), StringComparison.Ordinal);
    }
}
