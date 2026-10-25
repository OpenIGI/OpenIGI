namespace OpenIGI.Game.World;

/// <summary>One localized StatusMessage captured by IGI1's map-computer log.</summary>
public sealed record MissionComputerLogEntry(string TextResource)
{
    /// <summary>Gets the text already resolved, where the entry was logged as text rather than as a resource name.</summary>
    public string Text { get; init; } = string.Empty;
}
