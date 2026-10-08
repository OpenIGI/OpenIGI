using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>Where a weapon fires from and which way: <c>WeaponGetParentParameterContext_s</c>.</summary>
/// <param name="Position">The position, in world units.</param>
/// <param name="Orientation">The orientation, nine floats row by row; its columns are the axes, Y forward.</param>
public readonly record struct Igi2FireFrame(Vector3d Position, float[] Orientation);

/// <summary>What a weapon asks of the human that holds it.</summary>
/// <remarks>
/// The weapon is a child task of its human and reads it through the human's event handlers and fields: the parent's
/// <c>Weapon_eGetParentParametersQTaskEvent</c> and <c>Weapon_eGetWeaponObjectEvent</c> handlers, its accumulated speed,
/// its flash effect and its mission score.
/// </remarks>
public interface IIgi2WeaponOwner
{
    /// <summary>Gets the game's tick count, <c>Flow_ptFlow-&gt;nTicks</c>.</summary>
    int Ticks { get; }

    /// <summary>Gets the game's run random stream, <c>Game.tRunRandomState</c>.</summary>
    IgiRandom Random { get; }

    /// <summary>Gets a value indicating whether the owner is the player (<c>HumanPlayer_eQTaskType</c>).</summary>
    bool IsPlayer { get; }

    /// <summary>Gets the ticks the player has held a grenade with its pin out, <c>nGrenadeHeldTime</c> (<c>HumanPlayer+0xCAC</c>).</summary>
    int GrenadeHeldTime { get; }

    /// <summary>Gets the owner's accumulated speed, <c>tAccumulatedSpeed</c> (<c>Human+0x5F0</c>), which a shot inherits.</summary>
    Vector3f AccumulatedSpeed { get; }

    /// <summary>Gets the owner's team, <c>eTeam</c>, which the AI events it causes carry.</summary>
    int Team { get; }

    /// <summary>Gets a value indicating whether the owner's view is zoomed, <c>HumanView.isZoomMode</c>.</summary>
    bool IsZoomMode { get; }

    /// <summary>Gets a value indicating whether the owner answers where its weapon fires from; without, the weapon does nothing.</summary>
    /// <remarks>Whether the owner's type has a <c>Weapon_eGetParentParametersQTaskEvent</c> handler.</remarks>
    bool HasFireFrame { get; }

    /// <summary>Asks where the weapon fires from (<c>Weapon_eGetParentParametersQTaskEvent</c>).</summary>
    /// <param name="addInaccuracy">Whether to turn it by the owner's inaccuracy, which may draw from the run stream.</param>
    /// <returns>The frame.</returns>
    Igi2FireFrame GetFireFrame(bool addInaccuracy);

    /// <summary>
    /// Counts the drawn weapon objects the owner holds the weapon by (<c>Weapon_eGetWeaponObjectEvent</c>,
    /// <c>Human_GetWeaponObjectHandler</c>, <c>igi2 0x500A10</c>).
    /// </summary>
    /// <returns>The number; a muzzle flash and a casing need at least one.</returns>
    int WeaponObjectCount();

    /// <summary>Lights the owner with a shot's flash, <c>tFlashEffect</c> (<c>Human+0x804</c>).</summary>
    /// <param name="flash">The colour, red to blue.</param>
    void Flash(Vector3f flash);

    /// <summary>Keeps the owner visible whatever the light for a while, <c>nIgnoreLightEffectOnVisibilityTimer</c> (<c>Human+0x7C0</c>).</summary>
    /// <param name="ticks">The ticks.</param>
    void IgnoreLightOnVisibility(int ticks);

    /// <summary>Counts shots towards the owner's accuracy score, <c>tMissionScoreData</c> shots (<c>Human+0x1EC</c>).</summary>
    /// <param name="shots">The shots.</param>
    void CountShots(int shots);
}
