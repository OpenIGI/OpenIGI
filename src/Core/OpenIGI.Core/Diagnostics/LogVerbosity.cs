namespace OpenIGI.Core.Diagnostics;

/// <summary>The player-facing choice of how much the game logs, behind <c>con_log_verbosity</c>.</summary>
/// <remarks>A coarse view of <see cref="LogLevel"/>; <see cref="LogLevelPolicy"/> accepts both spellings.</remarks>
public enum LogVerbosity
{
    /// <summary>Write nothing.</summary>
    Off,

    /// <summary>Write warnings and failures only.</summary>
    Minimal,

    /// <summary>Write normal progress messages as well as warnings and failures.</summary>
    Verbose,
}
