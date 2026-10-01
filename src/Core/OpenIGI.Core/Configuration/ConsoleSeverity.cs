namespace OpenIGI.Core.Configuration;

/// <summary>
/// How a line of console output should be presented.
/// </summary>
/// <remarks>The console view maps these onto colours; the severity is not a logging level.</remarks>
public enum ConsoleSeverity
{
    /// <summary>Ordinary output from a command.</summary>
    Output,

    /// <summary>The command line the user submitted, echoed back above its output.</summary>
    Echo,

    /// <summary>Something worth reading but not a failure, such as "takes effect after a restart".</summary>
    Notice,

    /// <summary>The command did something unexpected but completed.</summary>
    Warning,

    /// <summary>The command failed.</summary>
    Error,
}
