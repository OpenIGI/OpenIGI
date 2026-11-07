using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Player;

/// <content>
/// Dropping what is in hand, in a multiplayer match (<see cref="PlayerAction.DropWeapon"/>): a gun is thrown as I.G.I. 2's player
/// throws one (<c>HumanPlayer_RunActionChangeWeapon</c>, <c>GunPickup_NewFromWeapon</c>), a pickup with the rounds left in it for
/// anyone to take; the bomb a bomb mode plays is left for the mode to put down.
/// </content>
/// <remarks>
/// I.G.I. 2 throws it from the body's root raised 1228.8, along the heading turned at random by up to an eighth of a turn either
/// way, at 614.4 a tick along the ground and 273.07 up (its long throw, <c>isThrowShort</c> false). Here the gun then flies as
/// a body against the level's triangles, as the remaster's drops do, drawn between ticks as any dropped gun is.
/// </remarks>
public sealed partial class PlayerCamera
{
    // The drop key pressed with the bomb in hand, until the mode takes it.
    private bool _bombDropAsked;

    /// <summary>Gets or sets whether the drop key drops what is in hand: a multiplayer match's rule, off in the campaign.</summary>
    public bool CanDropWeapons { get; set; }

    /// <summary>Takes the request to drop the bomb this player holds, made with the drop key since the last time it was taken.</summary>
    /// <returns><see langword="true"/> once for each press with the bomb in hand.</returns>
    public bool TakeBombDrop()
    {
        bool asked = _bombDropAsked;
        _bombDropAsked = false;
        return asked;
    }

    /// <summary>Gets whether a weapon is one the drop key throws down: a gun, not a knife, a grenade, a mine or a kit.</summary>
    /// <param name="weapon">The weapon.</param>
    /// <returns><see langword="true"/> for a gun, pistol or shotgun.</returns>
    public static bool IsDroppable(WeaponType weapon) =>
        weapon is { Category: WeaponCategory.Gun or WeaponCategory.Pistol or WeaponCategory.Shotgun };

    // The drop key, freshly pressed: the gun in hand thrown down -- laid by this machine unless it only mirrors another's
    // pickups, when the server's lands here as any of its drops does -- and carried no more; the bomb asked of the mode.
    private void TickDrop()
    {
        if (_weapons?.InHand is not { } held || held.Weapon.Category != WeaponCategory.Bomb)
        {
            _bombDropAsked = false;
        }

        if (!CanDropWeapons || _weapons?.InHand is not { } inHand || !_input.IsRisingEdge(PlayerInputSlot.DropWeapon))
        {
            return;
        }

        if (inHand.Weapon.Category == WeaponCategory.Bomb)
        {
            _bombDropAsked = true;
            return;
        }

        if (!IsDroppable(inHand.Weapon))
        {
            return;
        }

        if (_pickups.Guns is { Mirrored: false } guns)
        {
            double heading = PickupThrow.Heading(_body.Yaw, far: true, _random.NextDouble());
            Vector3d origin = _body.Origin;
            Vector3d at = new(origin.X, origin.Y, origin.Z + PickupThrow.AboveRoot);
            Vector3d velocity = PickupThrow.Velocity(heading, far: true);
            int rounds = inHand.Gun.State.RoundsInClip;
            guns.Throw(at, Matrix3f.RotationZ(-(float)heading), inHand.Weapon, velocity, rounds >= 0 ? rounds : null, _weapons);
        }

        _weapons.Drop(inHand.Weapon.WeaponId);
        PoseChanged = true;
    }
}
