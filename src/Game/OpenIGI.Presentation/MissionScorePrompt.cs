namespace OpenIGI.Presentation;

/// <summary>What the foot of a completed mission's rank screen says: how this end goes on, and when the squad goes on by itself.</summary>
/// <param name="ContinueKey">The key that goes on, as its cap names it, or <see langword="null"/> where this end cannot (a co-op client).</param>
/// <param name="SecondsLeft">The whole seconds before a co-op squad goes on by itself, or <see langword="null"/> for a single player's.</param>
/// <param name="WaitsForHost">Whether this end waits for the host to go on: a co-op client's.</param>
public readonly record struct MissionScorePrompt(string? ContinueKey, int? SecondsLeft = null, bool WaitsForHost = false);
