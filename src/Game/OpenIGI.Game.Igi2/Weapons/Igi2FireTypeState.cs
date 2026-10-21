using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>Where one of a weapon's two fire types is in a trigger pull, <c>WeaponFireTypeData_s</c> (28 bytes).</summary>
/// <remarks>Two of these sit at <c>Weapon+0x68</c>, one per fire type.</remarks>
public sealed class Igi2FireTypeState
{
    /// <summary>Gets or sets a value indicating whether the pull's first shot is still to fire, <c>isFirstFire</c>.</summary>
    public bool IsFirstFire { get; set; }

    /// <summary>Gets or sets a value indicating whether the trigger is down, <c>isFireStatus</c>.</summary>
    public bool IsFireStatus { get; set; }

    /// <summary>Gets or sets a value indicating whether the trigger is to be let go at the end of the tick, <c>isReleaseFire</c>.</summary>
    public bool IsReleaseFire { get; set; }

    /// <summary>Gets or sets the tick the trigger went down, <c>nFireStatusTick</c>.</summary>
    public int FireStatusTick { get; set; }

    /// <summary>Gets or sets the tick of the last shot, <c>nLastStatusTick</c>.</summary>
    public int LastStatusTick { get; set; }

    /// <summary>Gets or sets the rounds fired since the trigger went down, <c>nRoundsFired</c>.</summary>
    public int RoundsFired { get; set; }
}

/// <summary>A shot as the weapon sends it to itself, <c>WeaponNetFireContext_s</c> (48 bytes).</summary>
/// <param name="WeaponId">The weapon's number, as a byte.</param>
/// <param name="FireType">The fire type, as a byte.</param>
/// <param name="HeldTime">The ticks the trigger has been down, as a byte.</param>
/// <param name="Position">Where it fires from, in whole centimetres.</param>
/// <param name="Direction">Which way, as the three angles of its orientation.</param>
/// <remarks>
/// The single-player campaign dispatches it straight to the weapon's own handler (<c>Weapon_SendWeaponEvent</c>,
/// <c>igi2 0x51C090</c>), but the message's narrowing is the shot's: the position goes through whole centimetres and the
/// orientation through three angles.
/// </remarks>
public readonly record struct Igi2FireMessage(byte WeaponId, byte FireType, byte HeldTime, (int X, int Y, int Z) Position, (float Alpha, float Beta, float Gamma) Direction);

/// <summary>A trigger let go as the weapon sends it to itself, <c>WeaponNetReleaseContext_s</c>.</summary>
/// <param name="WeaponId">The weapon's number.</param>
/// <param name="FireType">The fire type.</param>
/// <param name="HeldTime">The ticks the trigger was down.</param>
/// <param name="Position">Where it was let go, in world units: the release keeps the position whole.</param>
/// <param name="Direction">Which way, as the three angles of its orientation.</param>
public readonly record struct Igi2ReleaseMessage(int WeaponId, int FireType, int HeldTime, Vector3d Position, (float Alpha, float Beta, float Gamma) Direction);

/// <summary>What a fire type is handed to fire or let go with, <c>WeaponFireContext_s.tIn</c>.</summary>
/// <param name="Weapon">The weapon firing.</param>
/// <param name="Operator">The human holding it, or <see langword="null"/>.</param>
/// <param name="Position">Where it fires from.</param>
/// <param name="Orientation">Which way, as nine floats, row by row; the handler may turn it per bullet and puts it back.</param>
/// <param name="ParentSpeed">The operator's accumulated speed, <c>Human+0x5F0</c>, or none.</param>
/// <param name="HeldTime">The ticks the trigger has been down.</param>
/// <param name="FireTypeParameters">The fire type's own parameters, the task in the weapon's slot.</param>
/// <param name="WeaponType">The weapon type.</param>
/// <param name="RoundsFired">The rounds fired since the trigger went down.</param>
public readonly record struct Igi2FireRequest(
    Igi2WeaponFiring Weapon,
    IIgi2WeaponOwner? Operator,
    Vector3d Position,
    float[] Orientation,
    Vector3f ParentSpeed,
    int HeldTime,
    object FireTypeParameters,
    Igi2WeaponTypeTask WeaponType,
    int RoundsFired);

/// <summary>What a fire type reports back, <c>WeaponFireContext_s.tOut</c>.</summary>
/// <param name="BulletsFired">The rounds it took, <c>nBulletsFired</c>.</param>
/// <param name="Delay">The ticks before the next shot, <c>nDelay</c>.</param>
/// <param name="KickbackDamping">How fast the view's kick dies away.</param>
/// <param name="CrossHairDamping">How fast the precision settles; one unless the handler says otherwise.</param>
/// <param name="AlphaKickback">The kick upwards, in degrees.</param>
/// <param name="GammaKickback">The kick sideways, in degrees.</param>
public readonly record struct Igi2FireResult(int BulletsFired, int Delay, float KickbackDamping, float CrossHairDamping, float AlphaKickback, float GammaKickback)
{
    /// <summary>Gets what a handler that fired nothing reports: the context as <c>Weapon_SingleFireHandler</c> clears it.</summary>
    public static Igi2FireResult Nothing => new(0, 0, 0f, 1f, 0f, 0f);
}

/// <summary>What a fire type is asked to zoom with, <c>WeaponZoomContext_s.tIn</c> (<c>Weapon_SingleZoomHandler</c>, <c>igi2 0x51D560</c>).</summary>
/// <param name="Weapon">The weapon.</param>
/// <param name="Operator">The weapon's holder, its parent.</param>
/// <param name="ZoomIn">Whether the view narrows (<c>isInOut</c>).</param>
/// <param name="FireTypeParameters">The fire type's parameters task.</param>
public readonly record struct Igi2ZoomRequest(Igi2WeaponFiring Weapon, IIgi2WeaponOwner? Operator, bool ZoomIn, object FireTypeParameters);

/// <summary>A fire type that answers the zoom keys, a type with a <c>Weapon_eZoomQTaskEvent</c> handler.</summary>
public interface IIgi2ZoomHandler
{
    /// <summary>Steps the zoom.</summary>
    /// <param name="request">What it zooms with.</param>
    void Zoom(in Igi2ZoomRequest request);
}

/// <summary>One fire type's handlers: what it does when fired, <c>Weapon_eFireQTaskEvent</c>, and let go, <c>Weapon_eReleaseQTaskEvent</c>.</summary>
public interface IIgi2FireHandler
{
    /// <summary>Fires once.</summary>
    /// <param name="request">What it fires with.</param>
    /// <returns>What it fired.</returns>
    Igi2FireResult Fire(in Igi2FireRequest request);

    /// <summary>Gets a value indicating whether the fire type has a release handler at all.</summary>
    bool HasRelease { get; }

    /// <summary>Lets the trigger go.</summary>
    /// <param name="request">What it was fired with.</param>
    /// <returns>What letting go fired, nothing for most.</returns>
    Igi2FireResult Release(in Igi2FireRequest request);
}
