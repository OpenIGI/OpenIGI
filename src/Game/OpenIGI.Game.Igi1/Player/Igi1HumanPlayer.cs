using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// Project I.G.I.'s player standing in a level that is not the game's own mission: its rig, its life, and its task's
/// events -- what a round, a blast, a blow, a fall and a powered fence do to it.
/// </summary>
/// <remarks>
/// <para>
/// It is <c>HumanPlayer</c> as another game's level hosts it. What hurts it and how it dies are the game's one set of rules
/// (<see cref="Igi1PlayerRules"/>), the ones the mission's own player and a co-op partner are hurt by; what it is shown and
/// told is done on this machine (<see cref="Igi1PlayerSenses"/>), in the place it stands -- its sounds, its corpse and its
/// death camera (<see cref="IIgi1PlayerSurroundings"/>).
/// </para>
/// <para>
/// The body's blast is weighed with the human bound the game's humans share: there are no soldiers of the level's own to
/// read a player's from.
/// </para>
/// </remarks>
/// <param name="rig">The player's rig.</param>
/// <param name="life">The player's life.</param>
/// <param name="surroundings">What the player is hurt and dies in.</param>
public sealed class Igi1HumanPlayer(PlayerCamera rig, Igi1PlayerLife life, IIgi1PlayerSurroundings surroundings) : IHumanEvents, IHarmedPlayer
{
    private readonly Igi1PlayerRules _rules = new(surroundings ?? throw new ArgumentNullException(nameof(surroundings)));
    private readonly Igi1PlayerSenses _senses = new(() => rig, life, surroundings);

    /// <summary>Gets the player's rig.</summary>
    public PlayerCamera Rig { get; } = rig ?? throw new ArgumentNullException(nameof(rig));

    /// <summary>Gets the player's life.</summary>
    public Igi1PlayerLife Life { get; } = life ?? throw new ArgumentNullException(nameof(life));

    /// <inheritdoc/>
    PlayerCamera? IHarmedPlayer.Rig => Rig;

    /// <inheritdoc/>
    HumanHealth IHarmedPlayer.Health => Life.Health;

    /// <inheritdoc/>
    bool IHarmedPlayer.IsDead
    {
        get => Life.IsDead;
        set => Life.IsDead = value;
    }

    /// <inheritdoc/>
    IPlayerSenses IHarmedPlayer.Senses => _senses;

    /// <inheritdoc/>
    public bool EvaluateWeaponCollision(HumanTask human, in WeaponCollisionQuery query) => true;

    /// <inheritdoc/>
    public void WeaponHit(HumanTask human, in WeaponHit hit)
    {
        if (hit.Context is Igi1WeaponHit round)
        {
            TakeRound(round);
        }
        else
        {
            // Another game's round: where it meets the player's body is this game's to find.
            TakeRound(Igi1WeaponHit.FromShared(hit, Igi1WeaponHit.Meet(hit, Rig.Body.Position, new Vector3f(-MathF.Sin(Rig.Body.Yaw), MathF.Cos(Rig.Body.Yaw), 0f))));
        }
    }

    /// <inheritdoc/>
    public void ExplosionHit(HumanTask human, in ExplosionHit hit)
    {
        Igi1ExplosionHit blast = hit.Context as Igi1ExplosionHit ?? Igi1ExplosionHit.FromShared(hit);
        Blast(blast.Position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor, blast.KilledBy);
    }

    /// <summary>A round's hit on the player (<c>0x412440</c>).</summary>
    /// <param name="round">The round.</param>
    public void TakeRound(Igi1WeaponHit round) => _rules.TakeRound(this, round);

    /// <summary>A blast's share of the player (<c>0x412990</c>).</summary>
    /// <param name="position">The blast's centre, in units.</param>
    /// <param name="radiusUnits">The full-damage radius, in units.</param>
    /// <param name="falloffUnits">The falloff beyond it, in units.</param>
    /// <param name="damageFactor">The blast's damage.</param>
    /// <param name="killedBy">The weapon category a death it causes is reported under.</param>
    public void Blast(Vector3d position, float radiusUnits, float falloffUnits, float damageFactor, WeaponCategory killedBy) =>
        _rules.Blast(this, position, radiusUnits, falloffUnits, damageFactor, killedBy, (float)HumanSeparation.RadiusInUnits);

    /// <summary>A close-combat blow (<c>0x412CE0</c>).</summary>
    /// <param name="attacker">The attacker's position, which decides front or back.</param>
    /// <param name="damage">The already-scaled damage.</param>
    public void Strike(Vector3d attacker, float damage) => _rules.Strike(this, attacker, damage);

    /// <summary>The player's landing (<c>0x408460</c>).</summary>
    /// <param name="impact">The landing.</param>
    public void Land(HumanFallImpact impact) => _rules.Land(this, impact);

    /// <summary>A powered fence's shock (<c>0x412F80</c>).</summary>
    /// <param name="fence">The fence.</param>
    public void Shock(WorldInteraction fence) => _rules.Shock(this, fence, Life.ElectricFenceDose);

    /// <summary>Starts the player's death (state 47, <c>0x40E560</c>).</summary>
    /// <param name="from">Where the kill came from.</param>
    /// <param name="zone">The last zone struck.</param>
    /// <param name="bone">The bone the lethal hit landed on, or -1.</param>
    /// <param name="category">The weapon category the death is reported under.</param>
    public void Die(Vector3d from, int zone, int bone, WeaponCategory category) => _rules.Die(this, from, zone, bone, category);
}
