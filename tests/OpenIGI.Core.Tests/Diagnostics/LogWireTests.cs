using OpenIGI.Core.Diagnostics;
using Xunit;

namespace OpenIGI.Core.Tests.Diagnostics;

/// <summary>Pins the line format the game writes and the launcher reads.</summary>
public sealed class LogWireTests
{
    [Fact]
    public void Write_ThenTryDecode_RoundTrips()
    {
        LogRecord sent = new(
            new DateTimeOffset(2026, 10, 3, 14, 2, 11, 250, TimeSpan.FromHours(2)),
            LogLevel.Warning,
            "AI.Gunner",
            "tab\there\nnew line \\ backslash",
            null,
            7);

        Assert.True(LogWire.TryDecode(Encode(sent), out LogRecord received));

        Assert.Equal(sent.Timestamp, received.Timestamp);
        Assert.Equal(sent.Timestamp.Offset, received.Timestamp.Offset);
        Assert.Equal(sent.Level, received.Level);
        Assert.Equal(sent.Category, received.Category);
        Assert.Equal(sent.Message, received.Message);
        Assert.Equal(sent.ThreadId, received.ThreadId);
    }

    [Fact]
    public void TryDecode_CarriesTheExceptionTextAfterTheMessage()
    {
        LogRecord sent = LogRecord.Now(LogLevel.Error, "Vfs", "read failed", new IOException("disk gone"));

        Assert.True(LogWire.TryDecode(Encode(sent), out LogRecord received));

        Assert.StartsWith("read failed" + Environment.NewLine, received.Message, StringComparison.Ordinal);
        Assert.Contains("disk gone", received.Message, StringComparison.Ordinal);
        Assert.Null(received.Exception);
    }

    [Theory]
    [InlineData("plain text from an older build")]
    [InlineData("\u001eOIGI1\tnot\ta\trecord")]
    [InlineData("")]
    [InlineData(null)]
    public void TryDecode_RefusesAnythingElse(string? line) =>
        Assert.False(LogWire.TryDecode(line, out _));

    [Fact]
    public void Write_IsOneLine()
    {
        string line = Encode(LogRecord.Now(LogLevel.Information, "Boot", "a\r\nb", new InvalidOperationException("x\ny")));

        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
    }

    private static string Encode(in LogRecord record)
    {
        using StringWriter writer = new(System.Globalization.CultureInfo.InvariantCulture);
        LogWire.Write(writer, record);
        return writer.ToString().TrimEnd('\r', '\n');
    }
}
