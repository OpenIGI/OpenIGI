using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>What is told of harm a player took: who is credited with it, with what, and how it struck.</summary>
/// <param name="Credit">The player credited (a guard's or the level's harm has none).</param>
/// <param name="Weapon">The weapon's script id, or empty.</param>
/// <param name="Cause">What it was.</param>
/// <param name="Head">Whether it struck the neck, the one zone that counts as a headshot.</param>
/// <param name="Backstab">Whether it was a blade in an unaware back.</param>
/// <param name="Lands">Whether it is a hit the one who dealt it is shown (a round, a credited blast), rather than the level's harm.</param>
public readonly record struct PlayerHarmNote(
    ShotTally? Credit, string Weapon, KillCause Cause, bool Head = false, bool Backstab = false, bool Lands = true)
{
    /// <summary>The level's own harm: a fall, a fence, nobody's blast.</summary>
    /// <param name="cause">What it was.</param>
    /// <returns>The note.</returns>
    public static PlayerHarmNote Level(KillCause cause) => new(null, string.Empty, cause, Lands: false);
}
