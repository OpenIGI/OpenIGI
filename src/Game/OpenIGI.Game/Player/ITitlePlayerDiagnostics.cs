namespace OpenIGI.Game.Player;

/// <summary>Read-only runtime state a title exposes to the developer console.</summary>
public interface ITitlePlayerDiagnostics
{
    /// <summary>Describes the level's living actor updates, scripts and animation clocks.</summary>
    /// <returns>Diagnostic lines generated only when requested.</returns>
    IEnumerable<string> DescribeActors();
}
