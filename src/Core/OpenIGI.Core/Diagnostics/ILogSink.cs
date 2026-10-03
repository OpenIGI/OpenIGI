namespace OpenIGI.Core.Diagnostics;

/// <summary>A destination a <see cref="LogRouter"/> hands records to: a file, a stream, a console view.</summary>
/// <remarks>
/// The router calls <see cref="Emit"/> from whichever thread logged, one call at a time. A sink must not
/// throw and must not block for long; one that does slow work (a disk) queues it.
/// </remarks>
public interface ILogSink : IDisposable
{
    /// <summary>Gets the lowest severity this sink wants.</summary>
    /// <remarks>Read once, when the sink is added to a router.</remarks>
    LogLevel MinimumLevel { get; }

    /// <summary>Takes one record.</summary>
    /// <param name="record">The record.</param>
    void Emit(in LogRecord record);

    /// <summary>Makes everything emitted so far durable or visible, waiting a short while at most.</summary>
    void Flush();
}
