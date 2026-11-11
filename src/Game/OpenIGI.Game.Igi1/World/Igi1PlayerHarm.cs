using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// What hurts the players of the game's own mission and how they die: the mission's player and, in a co-op session or a
/// match, every other.
/// </summary>
/// <remarks>
/// The rules are the game's one set (<see cref="Igi1PlayerRules"/>), run in the mission's surroundings. This is who they
/// are run on: the mission's own player (<see cref="Own"/>), whose rig and life the mission holds, and the session's other
/// players, each with a rig, a health and senses of its own -- so each is hurt, shown and killed as itself.
/// </remarks>
/// <param name="mission">The mission's world, whose player and partners these are.</param>
/// <param name="surroundings">What the mission's players are hurt in.</param>
public sealed class Igi1PlayerHarm(MissionWorld mission, IIgi1PlayerSurroundings surroundings)
{
    private readonly Igi1PlayerRules _rules = new(surroundings);

    /// <summary>
    /// Gets or sets what is told of a round a player took: the player, who is credited with it (none for a guard's), and
    /// whether it struck the neck, the one zone that counts as a headshot.
    /// </summary>
    public Action<IHarmedPlayer, ShotTally?, bool>? RoundTaken
    {
        get => _rules.RoundTaken;
        set => _rules.RoundTaken = value;
    }

    /// <summary>Gets the mission's own player as the handlers hurt it.</summary>
    public IHarmedPlayer Own { get; } = new MissionPlayer(mission, new Igi1PlayerSenses(() => mission.Player, mission.PlayerLife, surroundings));

    /// <summary>Gets every player the mission's hazards reach: its own, then a co-op session's partners.</summary>
    public IEnumerable<IHarmedPlayer> Players => [Own, .. mission.NetworkPlayers];

    /// <summary>A round's hit on a player (<c>0x412440</c>).</summary>
    /// <param name="target">The player.</param>
    /// <param name="round">The round.</param>
    public void TakeRound(IHarmedPlayer target, Igi1WeaponHit round) => _rules.TakeRound(target, round);

    /// <summary>A blast's share of a player (<c>0x412990</c>).</summary>
    /// <param name="target">The player.</param>
    /// <param name="position">The blast's centre, in units.</param>
    /// <param name="radiusUnits">The full-damage radius, in units.</param>
    /// <param name="falloffUnits">The falloff beyond it, in units.</param>
    /// <param name="damageFactor">The blast's damage.</param>
    /// <param name="killedBy">The weapon category a death it causes is reported under.</param>
    public void BlastPlayer(IHarmedPlayer target, Vector3d position, float radiusUnits, float falloffUnits, float damageFactor, WeaponCategory killedBy) =>
        _rules.Blast(
            target, position, radiusUnits, falloffUnits, damageFactor, killedBy,
            mission.Soldiers?.PlayerHumanRadius ?? (float)HumanSeparation.RadiusInUnits);

    /// <summary>Applies a close-combat blow to the mission's own player (<c>0x412CE0</c>).</summary>
    /// <param name="attacker">The attacker's position, which decides front or back.</param>
    /// <param name="damage">The already-scaled damage (the attacker's Blunt weapon factor).</param>
    public void StrikePlayer(Vector3d attacker, float damage) => StrikePlayer(Own, attacker, damage);

    /// <summary>Applies a close-combat blow to a player (<c>0x412CE0</c>).</summary>
    /// <param name="target">The player struck.</param>
    /// <param name="attacker">The attacker's position, which decides front or back.</param>
    /// <param name="damage">The already-scaled damage (the attacker's Blunt weapon factor).</param>
    public void StrikePlayer(IHarmedPlayer target, Vector3d attacker, float damage) => _rules.Strike(target, attacker, damage);

    /// <summary>Puts the mission's own player back on its feet.</summary>
    /// <param name="at">Where its feet go, in units, or <see langword="null"/> to stand where it fell.</param>
    /// <param name="facing">Which way it faces.</param>
    public void RevivePlayer(Vector3d? at, float facing) => RevivePlayer(Own, at, facing);

    /// <summary>Puts a player that has died back on its feet: standing, armed and at full health, where it is or at a place.</summary>
    /// <param name="target">The player.</param>
    /// <param name="at">Where its feet go, in units, or <see langword="null"/> to stand where it fell.</param>
    /// <param name="facing">Which way it faces.</param>
    /// <remarks>Not retail: see <see cref="Igi1PlayerRules.Revive"/>.</remarks>
    public static void RevivePlayer(IHarmedPlayer target, Vector3d? at, float facing) => Igi1PlayerRules.Revive(target, at, facing);

    /// <summary>Starts the mission's own player's death.</summary>
    /// <param name="from">Where the kill came from, which decides which way the body is thrown.</param>
    /// <param name="zone">The last zone struck, which picks the fall.</param>
    /// <param name="bone">The bone the lethal hit landed on, or -1.</param>
    /// <param name="category">The weapon category the death is reported under.</param>
    public void BeginPlayerDeath(Vector3d from, int zone, int bone, WeaponCategory category) =>
        BeginPlayerDeath(Own, from, zone, bone, category);

    /// <summary>Starts a player's death: its fall and what follows it (state 47, <c>0x40E560</c>).</summary>
    /// <param name="target">The player dying.</param>
    /// <param name="from">Where the kill came from, which decides which way the body is thrown.</param>
    /// <param name="zone">The last zone struck, which picks the fall.</param>
    /// <param name="bone">The bone the lethal hit landed on, or -1.</param>
    /// <param name="category">The weapon category the death is reported under.</param>
    public void BeginPlayerDeath(IHarmedPlayer target, Vector3d from, int zone, int bone, WeaponCategory category) =>
        _rules.Die(target, from, zone, bone, category);

    /// <summary>A player's landing (<c>0x408460</c>), which hurts and can kill the player that landed.</summary>
    /// <param name="player">The rig that landed: the mission's own player's or a partner's.</param>
    /// <param name="impact">The landing.</param>
    public void ResolvePlayerFallImpact(PlayerCamera player, HumanFallImpact impact) => _rules.Land(Of(player), impact);

    /// <summary>A powered fence's shock (<c>0x412F80</c>), dealt to the player that touched it.</summary>
    /// <param name="player">The rig that touched it: the mission's own player's or a partner's.</param>
    /// <param name="fence">The fence.</param>
    public void ResolvePlayerElectricFence(PlayerCamera player, WorldInteraction fence) =>
        _rules.Shock(Of(player), fence, mission.PlayerLife.ElectricFenceDose);

    // Whose rig it is: a partner's when a partner holds it, else the mission's own player's.
    private IHarmedPlayer Of(PlayerCamera rig)
    {
        foreach (Multiplayer.Igi1NetworkPlayer partner in mission.NetworkPlayers)
        {
            if (ReferenceEquals(partner.Player, rig))
            {
                return partner;
            }
        }

        return Own;
    }
}
