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
/// <param name="blasts">What a blast does to the player.</param>
/// <param name="sounds">The rounds' impact sounds.</param>
public sealed class Igi1PlayerEvents(MissionWorld mission, Igi1PlayerHarm harm, Igi1Blasts blasts, Igi1HumanSounds sounds) : IHumanEvents
{
    /// <inheritdoc/>
    /// <remarks>The player's half of the evaluate is its probability roll, which the cast still makes itself.</remarks>
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
        blasts.BlastPlayer(harm.Own, blast.Position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor, blast.KilledBy);
    }

    /// <summary>The player's hit handler for the mission's own player.</summary>
    /// <param name="round">The round.</param>
    public void TakeRound(Igi1WeaponHit round) => TakeRound(harm.Own, round);

    /// <summary>One call of the player's hit handler (<c>0x412440</c>): the damage, the grunt, the shake, the flash and the shove.</summary>
    /// <param name="target">The player the round met: the mission's own, or a co-op partner.</param>
    /// <param name="round">The round.</param>
    public void TakeRound(IHarmedPlayer target, Igi1WeaponHit round)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(round);
        if (target.Rig is not { } rig)
        {
            return;
        }

        HumanHealth health = target.Health;
        HumanHit struck = round.Hit;
        health.Apply(struck.Zone, round.DamageFactor);
        sounds.PlayBulletImpact(struck.Position, struckHuman: true, surfaceMaterial: 0);

        // 0x412440's order: the grunt, then the shake, then the flash from the corners the round came in through, each on
        // its own draw.
        Vector3d travel = round.End - round.Origin;
        if (!health.IsDead)
        {
            target.Senses.Grunt();
        }

        target.Senses.HitShake();
        target.Senses.FlashFrom(new Vector3f((float)travel.X, (float)travel.Y, (float)travel.Z));

        // Every connecting round SHOVES the player 5 km/h along its travel -- the hit handler (0x412440) normalises the
        // round's direction and adds x/y x 189.62962 into the slide accumulator, whose 0.9-a-tick damping then bleeds it
        // off. This is what makes sustained fire stagger the player in retail.
        if (!health.IsDead)
        {
            double length = Math.Sqrt((travel.X * travel.X) + (travel.Y * travel.Y) + (travel.Z * travel.Z));
            if (length > 1e-6)
            {
                target.Shove(
                    rig,
                    (float)(travel.X / length * PlayerBody.HitShoveUnitsPerTick),
                    (float)(travel.Y / length * PlayerBody.HitShoveUnitsPerTick));
            }
        }

        if (health.IsDead)
        {
            harm.BeginPlayerDeath(target, round.Origin, struck.Zone, struck.Bone, round.Category);
        }
    }
}
