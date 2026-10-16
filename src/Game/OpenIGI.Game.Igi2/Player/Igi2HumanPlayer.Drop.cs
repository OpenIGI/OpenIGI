using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Player;

/// <content>
/// Dropping the weapon in hand (<c>HumanPlayer_RunActionIdle</c>, <c>HumanPlayer_RunActionChangeWeapon</c>): the drop key, freshly
/// pressed, on a weapon its type lets be dropped in a single-player game, puts it away for the next of its category, or the next
/// there is; once the change goes ahead it lands on the ground as a pickup with its load and the ammunition carried for it, and is
/// carried no more.
/// </content>
public sealed partial class Igi2HumanPlayer
{
    // ptDropWeaponType and isDropWeapon: the weapon to drop once the change goes ahead.
    private Igi2WeaponTypeTask? _dropWeaponType;
    private bool _isDropWeapon;

    // The idle state's drop branch: only with no change asked for, and a weapon in hand that may be dropped.
    private void TryDropWeapon()
    {
        if (!Channels.WasPressed(Igi2HumanChannel.DropWeapon) || HeldItem is not { IsActive: true } held
            || !held.WeaponType.CanWeaponBeDroppedInSinglePlayerGame || Weapon.Inventory is not { } inventory)
        {
            return;
        }

        Igi2WeaponTypeTask type = held.WeaponType;
        _dropWeaponType = type;
        _isDropWeapon = true;
        if (inventory.NextInCategory(type.WeaponCategory, type) is { } next)
        {
            ChangeWeaponId = next.Number;
            SelectWeapon(forceCycle: false);
        }
        else
        {
            SelectWeapon(forceCycle: true);
        }
    }

    /// <summary>
    /// The weapon put away lands as a pickup (<c>GunPickup_NewFromWeapon</c>): turned as the hand held it, at the body's root raised
    /// 1228.8, thrown along the player's heading give or take an eighth of a turn (one draw of the run stream), not thrown short,
    /// with its load and the ammunition carried for its two fire types; then it is carried no more and its ammunition goes with it.
    /// </summary>
    private void DropWeapon()
    {
        if (!_isDropWeapon)
        {
            return;
        }

        _isDropWeapon = false;
        Igi2WeaponTypeTask? type = _dropWeaponType;
        _dropWeaponType = null;
        if (type is not null)
        {
            ThrowWeapon(type, removeCarriedAmmo: true);
        }
    }

    /// <summary>
    /// Throws a carried weapon down as a pickup, <c>GunPickup_NewFromWeapon</c> and <c>Weapon_DisableWeaponType</c>: turned as the
    /// hand holds it, at the body's root raised 1228.8, thrown along the heading give or take an eighth of a turn (one draw of the
    /// run stream), with its load and, if asked, the ammunition carried for it, which then goes with it.
    /// </summary>
    /// <param name="type">The weapon.</param>
    /// <param name="removeCarriedAmmo">Whether its carried ammunition goes with it.</param>
    private void ThrowWeapon(Igi2WeaponTypeTask type, bool removeCarriedAmmo)
    {
        if (Weapon.Inventory is not { } inventory || inventory.FindWeapon(type.WeaponId) is not { } item)
        {
            return;
        }

        (_, Matrix3f held) = WeaponPlacement();
        Vector3d root = HeldRootPosition();
        Vector3d at = new(root.X, root.Y, root.Z + 1228.800048828125);
        Matrix3f o = Orientation;
        (_, _, float gamma) = Igi2Pose.Angles([o.M11, o.M12, o.M13, o.M21, o.M22, o.M23, o.M31, o.M32, o.M33]);
        float spread = (float)((ushort)Random.NextRaw() * IgiRandom.Scale);
        float throwGamma = (((Fpu)spread * 1.5707963705062866) + gamma - 0.7853981852531433).F;
        int[] pockets = new int[2];
        for (int slot = 0; slot < pockets.Length; slot++)
        {
            Igi2FireTypeRounds rounds = inventory.FireTypeRounds(item, slot);
            pockets[slot] = rounds.AmmoType is null || !removeCarriedAmmo ? 0 : rounds.Available;
        }

        Surroundings.ThrowGunPickup(
            type,
            at,
            [held.M11, held.M12, held.M13, held.M21, held.M22, held.M23, held.M31, held.M32, held.M33],
            throwGamma,
            item.Loaded(0),
            pockets,
            isThrowShort: false);
        inventory.Disable(item, removeCarriedAmmo);
    }
}
