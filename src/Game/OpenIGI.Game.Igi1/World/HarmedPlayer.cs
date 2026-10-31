using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// A player the mission can hurt: the mission's own, or a co-op partner. Every harm the original deals its player --
/// a round, a blow, a blast, the death that follows -- is dealt through this, to whichever player it reached, by the
/// one set of handlers (<see cref="Igi1PlayerEvents"/>, <see cref="Igi1PlayerHarm"/>, <see cref="Igi1Blasts"/>).
/// </summary>
/// <remarks>
/// What the handlers decide -- the damage, the shove, the death -- is the game's, and acts on the rig and the health here.
/// What the player feels of it -- its grunt, its view shaking, the red at its screen's corners, its death seen from
/// outside -- belongs to the screen it is played on, and goes through <see cref="Senses"/>: the mission's own player
/// feels it on this machine, a partner on its own.
/// </remarks>
public interface IHarmedPlayer
{
    /// <summary>Gets the player's rig: its body, its view and its weapons; <see langword="null"/> while the mission has none.</summary>
    PlayerCamera? Rig { get; }

    /// <summary>Gets the player's health.</summary>
    HumanHealth Health { get; }

    /// <summary>Gets or sets whether the player lies dead (locomotion state 47): it takes no more input and no more harm.</summary>
    bool IsDead { get; set; }

    /// <summary>Gets the screen and ears the player is played with.</summary>
    IPlayerSenses Senses { get; }
}

/// <summary>How the player's body fell, for the screen it is played on to show.</summary>
/// <param name="Clip">The fall's clip.</param>
/// <param name="Feet">Where it fell.</param>
/// <param name="ThrowFacing">The facing the body is thrown along, away from what killed it.</param>
/// <param name="Bone">The bone the killing round went through, or -1.</param>
/// <param name="Travel">From the killer to the body.</param>
/// <param name="Category">The killing weapon's category.</param>
/// <param name="Velocity">The body's velocity as it fell.</param>
public readonly record struct PlayerFall(
    int Clip, Vector3d Feet, float ThrowFacing, int Bone, Vector3d Travel, WeaponCategory Category, Vector3d Velocity);

/// <summary>
/// What a player feels of what happens to it, on the screen and through the ears it is played with: the reactions the
/// original's player handlers make besides the damage itself, each one primitive, in the handlers' own order.
/// </summary>
/// <remarks>
/// The mission's own player's are <see cref="LocalPlayerSenses"/>, which draw on the sound stream exactly as the
/// original's handlers do. A co-op partner's are its client's: the server records each call and the partner's own
/// game makes it, through its own <see cref="LocalPlayerSenses"/>.
/// </remarks>
public interface IPlayerSenses
{
    /// <summary>The player's grunt, through its rate limit.</summary>
    void Grunt();

    /// <summary>A blow's own sound, <c>punched_01</c> or <c>punched_02</c>.</summary>
    void Blow();

    /// <summary>The shake a round or a blow puts in the view, its strength drawn.</summary>
    void HitShake();

    /// <summary>A shake of a given strength and length.</summary>
    /// <param name="positionUnits">How far the eye moves, in units.</param>
    /// <param name="angleRadians">How far the view turns.</param>
    /// <param name="ticks">How long it lasts.</param>
    void Shake(float positionUnits, float angleRadians, int ticks);

    /// <summary>The red at the corners a round came in through.</summary>
    /// <param name="travel">The round's travel, in world space.</param>
    void FlashFrom(Vector3f travel);

    /// <summary>The red at all four corners, each its own draw.</summary>
    void FlashCorners();

    /// <summary>The red over the whole screen, as a fall's.</summary>
    void FlashCentre();

    /// <summary>A sound of the player's own, heard as the listener's.</summary>
    /// <param name="definition">The sound definition.</param>
    void Sound(string definition);

    /// <summary>The player's death as its screen shows it: the voices it held stopped, its body falling, the camera over it, its cry.</summary>
    /// <param name="fall">How it fell.</param>
    void Died(in PlayerFall fall);

    /// <summary>The player brought back: the red gone, its own eyes again.</summary>
    void Revived();

    /// <summary>The knock the handlers gave the player's body, already on the body here: for the game a partner plays on.</summary>
    /// <param name="x">The X added to the shove accumulator, in units per tick.</param>
    /// <param name="y">The Y added.</param>
    /// <remarks>
    /// The mission's own player has nothing to do: its body took the knock. A partner's client predicts its own body, and
    /// moves it as the server's moved, or it is put back every tick the knock carries the server's on.
    /// </remarks>
    void Shoved(float x, float y)
    {
    }
}

/// <summary>What the handlers do to a player's body.</summary>
internal static class HarmedPlayerBody
{
    /// <summary>Knocks a player's body along the floor (the shove accumulator, <c>Human+1648</c>), and tells its senses.</summary>
    /// <param name="target">The player.</param>
    /// <param name="rig">Its rig.</param>
    /// <param name="x">The X to add, in units per tick.</param>
    /// <param name="y">The Y to add.</param>
    public static void Shove(this IHarmedPlayer target, PlayerCamera rig, float x, float y)
    {
        rig.Body.AddHitShove(x, y);
        target.Senses.Shoved(x, y);
    }
}

/// <summary>The mission's own player, as the handlers hurt it: the rig, health and death the mission state holds.</summary>
/// <param name="mission">The mission.</param>
/// <param name="senses">The machine's own screen and ears.</param>
internal sealed class MissionPlayer(MissionWorld mission, IPlayerSenses senses) : IHarmedPlayer
{
    /// <inheritdoc/>
    public PlayerCamera? Rig => mission.Player;

    /// <inheritdoc/>
    public HumanHealth Health => mission.PlayerHealth;

    /// <inheritdoc/>
    public bool IsDead
    {
        get => mission.IsPlayerDead;
        set => mission.IsPlayerDead = value;
    }

    /// <inheritdoc/>
    public IPlayerSenses Senses => senses;
}
