using OpenIGI.Core.Diagnostics;
using Xunit;

namespace OpenIGI.Core.Tests.Diagnostics;

/// <summary>Pins that a contained call's failure is logged and stays its own.</summary>
public sealed class ContainedTests
{
    [Fact]
    public void Run_ReturnsTrueAndLogsNothingWhenTheCallReturns()
    {
        MemorySink log = new();
        int called = 0;

        Assert.True(Contained.Run(log, "Mods", "A listener", () => called++));
        Assert.True(Contained.Run(log, "Mods", "A listener", 2, static n => Assert.Equal(2, n)));
        Assert.True(Contained.Run(log, "Mods", "A source", 3, static n => n * 2, out int doubled));

        Assert.Equal(1, called);
        Assert.Equal(6, doubled);
        Assert.Equal(0, log.Count);
    }

    [Fact]
    public void Run_LogsAThrowAsAnErrorAndReturnsFalse()
    {
        MemorySink log = new();
        InvalidOperationException thrown = new("broken");

        Assert.False(Contained.Run(log, "Mods", "A listener", () => throw thrown));
        Assert.False(Contained.Run(log, "Mods", "A source", 0, int (_) => throw thrown, out int result));

        Assert.Equal(0, result);
        IReadOnlyList<LogRecord> records = log.Snapshot();
        Assert.Equal(2, records.Count);
        Assert.All(records, record =>
        {
            Assert.Equal(LogLevel.Error, record.Level);
            Assert.Equal("Mods", record.Category);
            Assert.Same(thrown, record.Exception);
        });
        Assert.Equal("A listener failed: broken", records[0].Message);
        Assert.Equal("A source failed: broken", records[1].Message);
    }
}
