using OpenIGI.Core.Diagnostics;
using Xunit.Abstractions;

namespace OpenIGI.TestSupport;

/// <summary>A logger that writes to a test's own output, which the runner shows beside that test.</summary>
/// <remarks>
/// For a test that wants to read what the code under test logged. A test that asserts on what was logged
/// uses a <see cref="MemorySink"/>, which is a logger too.
/// </remarks>
/// <param name="output">The test's output.</param>
/// <param name="minimumLevel">The lowest level written.</param>
public sealed class TestOutputLogger(ITestOutputHelper output, LogLevel minimumLevel = LogLevel.Trace) : IOpenIgiLogger
{
    private readonly ITestOutputHelper _output = output ?? throw new ArgumentNullException(nameof(output));

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel level, string category) => level >= minimumLevel && level < LogLevel.None;

    /// <inheritdoc/>
    public void Log(in LogRecord record)
    {
        if (!IsEnabled(record.Level, record.Category))
        {
            return;
        }

        using StringWriter line = new(System.Globalization.CultureInfo.InvariantCulture);
        LogFormatter.WriteConsoleLine(line, record);

        try
        {
            _output.WriteLine(line.ToString().TrimEnd());
        }
        catch (InvalidOperationException)
        {
            // Logged after the test finished, from a thread it started; there is no output left to write to.
        }
    }
}
