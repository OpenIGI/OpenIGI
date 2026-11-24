namespace OpenIGI.Presentation;

/// <summary>What the foot of a completed mission's rank screen says: how this end goes on, and when the squad goes on by itself.</summary>
/// <param name="ContinueKey">The key that goes on, as its cap names it, or <see langword="null"/> where this end cannot (a co-op client).</param>
/// <param name="SecondsLeft">The whole seconds before a co-op squad goes on by itself, or <see langword="null"/> for a single player's.</param>
/// <param name="WaitsForHost">
/// Whether the host can go on before the countdown runs out, for a co-op client to be told so; never on a dedicated server's.
/// </param>
/// <param name="Board">What became of the run sent to the boards -- checking, placed, not ranked and why -- or null for none.</param>
/// <param name="BoardGood">Whether that went well, badly, or neither yet (null).</param>
public readonly record struct MissionScorePrompt(string? ContinueKey, int? SecondsLeft = null, bool WaitsForHost = false, string? Board = null, bool? BoardGood = null);
