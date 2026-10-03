using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Host.Infrastructure;

/// <summary>The log's console variables.</summary>
/// <remarks>
/// The player's coarse choice of how much is logged. <c>OPENIGI_LOG</c> overrides it for a run, and
/// <c>log_level</c> refines it per category for a session; see <see cref="LogLevelPolicy"/>.
/// </remarks>
[CVarContainer]
public static class LoggingCVars
{
    /// <summary>How much the game logs. Minimal is the default, for gameplay performance.</summary>
    public static readonly EnumCVar<LogVerbosity> Verbosity = CVar.Choice(
        "con_log_verbosity",
        LogVerbosity.Minimal,
        CVarOptions.Archive,
        "Runtime log output: off, minimal (warnings/errors), or verbose (normal diagnostics).");
}
