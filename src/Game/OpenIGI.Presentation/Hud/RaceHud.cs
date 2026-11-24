namespace OpenIGI.Presentation.Hud;

/// <summary>A split of a race: a point both runs reached, and how far apart they reached it.</summary>
/// <param name="Label">What was reached: an objective, the finish.</param>
/// <param name="Seconds">When the player reached it, into the run.</param>
/// <param name="DeltaSeconds">How much later than the ghost the player reached it; negative is sooner.</param>
public sealed record RaceSplit(string Label, double Seconds, double DeltaSeconds);

/// <summary>What a race's HUD shows: how far ahead of the ghost or behind it the player is, and the splits so far.</summary>
/// <param name="DeltaSeconds">How much later than the ghost the player is where they stand now, negative ahead; null off its route.</param>
/// <param name="Splits">The splits reached so far, in order.</param>
/// <param name="LatestShown">How strongly the newest split is held up in the middle of the screen, one when just reached, fading to nought.</param>
public sealed record RaceHud(double? DeltaSeconds, IReadOnlyList<RaceSplit> Splits, float LatestShown);
