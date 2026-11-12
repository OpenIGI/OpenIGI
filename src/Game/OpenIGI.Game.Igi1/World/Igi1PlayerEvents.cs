using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// <c>HumanPlayer</c>'s own hit events, as <c>0x40FE30</c> links them: <c>WeaponHit</c> to <c>0x412440</c> and
/// <c>ExplosionHit</c> to <c>0x412990</c>.
/// </summary>
/// <remarks>The player's task is given these when the player is stood in the level.</remarks>
/// <param name="mission">The mission, whose player they hurt.</param>
/// <param name="harm">The player's grunt, shake and death.</param>
public sealed class Igi1PlayerEvents(MissionWorld mission, Igi1PlayerHarm harm) : IHumanEvents
{
    /// <inheritdoc/>
    public bool EvaluateWeaponCollision(HumanTask human, in WeaponCollisionQuery query) => true;

    /// <inheritdoc/>
    public void WeaponHit(HumanTask human, in WeaponHit hit)
    {
        if (hit.Context is Igi1WeaponHit round)
        {
            TakeRound(harm.Own, round);
        }
        else if (mission.Player is { } player)
        {
            // Another game's round: where it meets the player's body is this game's to find.
            TakeRound(harm.Own, Igi1WeaponHit.FromShared(hit, Igi1WeaponHit.Meet(hit, player.Body.Position, new Vector3f(-MathF.Sin(player.Body.Yaw), MathF.Cos(player.Body.Yaw), 0f))));
        }
    }

    /// <inheritdoc/>
    public void ExplosionHit(HumanTask human, in ExplosionHit hit)
    {
        Igi1ExplosionHit blast = hit.Context as Igi1ExplosionHit ?? Igi1ExplosionHit.FromShared(hit);
        harm.BlastPlayer(harm.Own, blast.Position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor, blast.KilledBy);
    }

    /// <summary>A round's hit on the mission's own player.</summary>
    /// <param name="round">The round.</param>
    public void TakeRound(Igi1WeaponHit round) => TakeRound(harm.Own, round);

    /// <summary>A round's hit on a player of the mission (<c>0x412440</c>).</summary>
    /// <param name="target">The player: the mission's own, or a co-op partner.</param>
    /// <param name="round">The round.</param>
    public void TakeRound(IHarmedPlayer target, Igi1WeaponHit round) => harm.TakeRound(target, round);
}
