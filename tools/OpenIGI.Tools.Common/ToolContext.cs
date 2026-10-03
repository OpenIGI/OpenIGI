using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Tools;

/// <summary>What a tool's code is given to run: its arguments, its logger and its output stream.</summary>
/// <remarks>
/// Progress, warnings and failures go through <see cref="Log"/>: plain lines on standard output for
/// progress, <c>warning: </c> and <c>error: </c> lines on the error stream. The tool's actual result — a
/// disassembly, a report, a table — goes to <see cref="Out"/>, so it can be redirected on its own.
/// </remarks>
public sealed class ToolContext
{
    internal ToolContext(string name, IReadOnlyList<string> arguments, IOpenIgiLogger log, TextWriter output, TextWriter error)
    {
        Name = name;
        Arguments = arguments;
        Log = log;
        Out = output;
        Error = error;
    }

    /// <summary>Gets the tool's name, the category its log lines carry.</summary>
    public string Name { get; }

    /// <summary>Gets the arguments, without the switches <see cref="ToolHost"/> handles itself.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>Gets the logger for progress, warnings and failures.</summary>
    public IOpenIgiLogger Log { get; }

    /// <summary>Gets the stream the tool's result is written to.</summary>
    public TextWriter Out { get; }

    /// <summary>Gets the error stream, for usage text.</summary>
    public TextWriter Error { get; }

    /// <summary>Reports progress.</summary>
    /// <param name="message">The line.</param>
    public void Info(string message) => Log.Information(Name, message);

    /// <summary>Reports something the tool recovered from.</summary>
    /// <param name="message">The line.</param>
    public void Warn(string message) => Log.Warning(Name, message);

    /// <summary>Reports a failure and returns <see cref="ToolExitCode.Failure"/>.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="exception">The exception behind it, written out only with <c>--verbose</c>.</param>
    /// <returns><see cref="ToolExitCode.Failure"/>.</returns>
    public int Fail(string message, Exception? exception = null)
    {
        Log.Error(Name, message, Log.IsEnabled(LogLevel.Debug, Name) ? exception : null);
        return ToolExitCode.Failure;
    }

    /// <summary>Reports a wrongly made request, prints the usage and returns <see cref="ToolExitCode.Usage"/>.</summary>
    /// <param name="usage">The usage text.</param>
    /// <param name="message">What was wrong with the request, or <see langword="null"/> when nothing was asked for.</param>
    /// <returns><see cref="ToolExitCode.Usage"/>.</returns>
    public int Usage(string usage, string? message = null)
    {
        if (message is not null)
        {
            Log.Error(Name, message);
        }

        Error.WriteLine(usage);
        return ToolExitCode.Usage;
    }
}
