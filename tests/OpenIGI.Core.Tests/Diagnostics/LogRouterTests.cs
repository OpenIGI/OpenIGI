using OpenIGI.Core.Diagnostics;
using Xunit;

namespace OpenIGI.Core.Tests.Diagnostics;

/// <summary>Pins the router's filtering, its replay to late sinks, and its safety across threads.</summary>
public sealed class LogRouterTests
{
    [Fact]
    public void Log_DropsWhatThePolicyFiltersOut()
    {
        MemorySink sink = new();
        using LogRouter router = new(new LogLevelPolicy(LogLevel.Warning));
        router.AddSink(sink);

        router.Information("Vfs", "mounted");
        router.Warning("Vfs", "missing");

        LogRecord only = Assert.Single(sink.Snapshot());
        Assert.Equal("missing", only.Message);
    }

    [Fact]
    public void Log_EachSinkKeepsItsOwnMinimum()
    {
        MemorySink everything = new();
        MemorySink errors = new(minimumLevel: LogLevel.Error);
        using LogRouter router = new(new LogLevelPolicy(LogLevel.Trace));
        router.AddSink(everything).AddSink(errors);

        router.Debug("Render", "frame");
        router.Error("Render", "lost");

        Assert.Equal(2, everything.Count);
        Assert.Equal("lost", Assert.Single(errors.Snapshot()).Message);
    }

    [Fact]
    public void IsEnabled_IsFalseWhenNoSinkWantsTheLevel()
    {
        using LogRouter router = new(new LogLevelPolicy(LogLevel.Trace));
        router.AddSink(new MemorySink(minimumLevel: LogLevel.Warning));

        Assert.False(router.IsEnabled(LogLevel.Debug, "Render"));
        Assert.True(router.IsEnabled(LogLevel.Warning, "Render"));
    }

    [Fact]
    public void Interpolated_IsNotFormattedWhenDisabled()
    {
        using LogRouter router = new(new LogLevelPolicy(LogLevel.Warning));
        router.AddSink(new MemorySink());
        Formatted counter = new();

        router.Debug("Render", $"value {counter}");
        router.Warning("Render", $"value {counter}");

        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public void AddSink_ReplaysWhatWasLoggedBeforeIt()
    {
        using LogRouter router = new(new LogLevelPolicy(LogLevel.Information), replayCapacity: 2);
        router.Information("Boot", "one");
        router.Information("Boot", "two");
        router.Information("Boot", "three");

        MemorySink late = new();
        router.AddSink(late);

        Assert.Equal(["two", "three"], late.Snapshot().Select(static r => r.Message));
    }

    [Fact]
    public void AddSink_WithoutReplaySeesOnlyWhatFollows()
    {
        using LogRouter router = new();
        router.Information("Boot", "before");

        MemorySink sink = new();
        router.AddSink(sink, replay: false);
        router.Information("Boot", "after");

        Assert.Equal("after", Assert.Single(sink.Snapshot()).Message);
    }

    [Fact]
    public void Log_ASinkThatThrowsDoesNotStopTheOthers()
    {
        MemorySink after = new();
        using LogRouter router = new();
        router.AddSink(new ThrowingSink()).AddSink(after);

        router.Information("Vfs", "mounted");

        Assert.Single(after.Snapshot());
    }

    [Fact]
    public void Log_FromManyThreadsLosesNothing()
    {
        MemorySink sink = new(capacity: 100_000);
        using LogRouter router = new(new LogLevelPolicy(LogLevel.Trace), replayCapacity: 0);
        router.AddSink(sink);

        Parallel.For(0, 8, _ =>
        {
            for (int i = 0; i < 1000; i++)
            {
                router.Information("Load", "line");
            }
        });

        Assert.Equal(8000, sink.Count);
    }

    private sealed class Formatted
    {
        public int Count { get; private set; }

        public override string ToString()
        {
            Count++;
            return "x";
        }
    }

    private sealed class ThrowingSink : ILogSink
    {
        public LogLevel MinimumLevel => LogLevel.Trace;

        public void Emit(in LogRecord record) => throw new InvalidOperationException("sink failed");

        public void Flush()
        {
        }

        public void Dispose()
        {
        }
    }
}
