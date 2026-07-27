namespace OpenIGI.Core.Diagnostics;

/// <summary>Severity of a log message.</summary>
public enum LogLevel
{
    /// <summary>Very fine-grained tracing, typically per-tick or per-resource.</summary>
    Trace = 0,

    /// <summary>Diagnostic information useful while developing.</summary>
    Debug = 1,

    /// <summary>Normal progress messages such as subsystem start-up and level loads.</summary>
    Information = 2,

    /// <summary>Something unexpected that the engine recovered from.</summary>
    Warning = 3,

    /// <summary>An operation failed, but the engine keeps running.</summary>
    Error = 4,

    /// <summary>An unrecoverable condition; the original engine would call <c>Sys_FatalError</c> here.</summary>
    Fatal = 5,

    /// <summary>Used only to disable logging; never passed to a log call.</summary>
    None = 6,
}
