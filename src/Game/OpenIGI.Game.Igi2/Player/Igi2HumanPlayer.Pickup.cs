using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Pickups;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Player;

/// <content>The weapons lying in the level, which the player takes as it comes within reach of them.</content>
public sealed partial class Igi2HumanPlayer : IIgi2PickupTaker
{
    /// <summary>The sound a weapon picked up makes, <c>weaponpickup_1</c> (<c>Human_PickupGun</c>, <c>igi2 0x50015D</c>).</summary>
    public const string PickupSound = "weaponpickup_1";

    /// <summary>The key of the message a weapon newly carried shows, before the weapon's name (<c>igi2 0x617B04</c>).</summary>
    public const string PickedUpMessagePrefix = "PICKEDUPWEAPON";

    /// <summary>Gets or sets the level's other pickups, the things collected by walking into them.</summary>
    public IReadOnlyList<Pickups.Igi2GenericPickupTask> GenericPickups { get; set; } = [];

    /// <summary>Gets or sets the level's pickups, or <see langword="null"/> for a level with none.</summary>
    public Igi2PickupList? Pickups { get; set; }

    /// <inheritdoc/>
    Igi2Inventory? IIgi2PickupTaker.Inventory => Weapon.Inventory;

    /// <summary>
    /// Takes a weapon picked up: <c>Human_GunPickupHandler</c> (<c>igi2 0x500180</c>) over <c>Human_PickupGun</c>
    /// (<c>0x4FFF20</c>).
    /// </summary>
    /// <param name="grant">What the weapon gives.</param>
    /// <remarks>
    /// <para>
    /// Only a living human with weapons takes one. A weapon not carried before is carried now, with the message that
    /// names it, and loaded and given its rounds (<see cref="Igi2Inventory.SetAmmoForWeapon"/>). One carried already only
    /// adds its rounds to what is carried of each fire type's ammunition, and, unless it is a weapon carried as rounds of
    /// itself, its load's worth to the first's as well. Either way the pickup sounds.
    /// </para>
    /// </remarks>
    public void PickUpGun(in Igi2GunPickupGrant grant)
    {
        if (Weapon.Inventory is not { } inventory || IsDead)
        {
            return;
        }

        Igi2WeaponTypeTask weaponType = grant.WeaponType;
        if (inventory.EnableWeaponType(weaponType.WeaponId, addAmmo: false))
        {
            Surroundings.ShowStatusMessage(PickedUpMessagePrefix + weaponType.WeaponName);
            if (inventory.FindWeapon(weaponType.WeaponId) is { } item)
            {
                inventory.SetAmmoForWeapon(item, grant.AmmoInClip, grant.AmmoForFireType);
            }
        }
        else
        {
            for (int slot = 0; slot < Igi2WeaponTypeTask.FireTypeSlots; slot++)
            {
                if (weaponType.AmmoInfo(slot, inventory.Tables).AmmoType is { } ammoType)
                {
                    inventory.AddAmmo(ammoType.AmmoId, grant.AmmoForFireType[slot]);
                }
            }

            if (weaponType.AmmoInfo(0, inventory.Tables).AmmoType is { } first
                && !Igi2WeaponCategory.IsCarriedAsRounds(weaponType.WeaponCategory))
            {
                inventory.AddAmmo(first.AmmoId, grant.AmmoInClip);
            }
        }

        Surroundings.PlaySound(PickupSound);
    }

    /// <summary>Offers the player each of the level's pickups in turn, <c>HumanPlayer_Pickup</c> (<c>igi2 0x49F8C0</c>).</summary>
    /// <remarks>
    /// <para>
    /// Not when dead, and no weapon while one is being changed or the binoculars are up. A weapon is taken outright when it
    /// is one already carried, which only adds its rounds, or when nothing of its category is: within reach, in sight, and
    /// not refused for carrying all its rounds already (<see cref="Igi2GunPickup.Offer"/>).
    /// </para>
    /// <para>
    /// A weapon of a category carried in another weapon is only offered: within reach, with nothing to activate in view, the action
    /// menu offers to swap it for each weapon of its category carried (<c>HumanPlayer_RegisterWeaponPickupActionItem</c>,
    /// <c>0x49F810</c>). When the activate key goes down with that swap first on the menu, the weapon is taken and the one carried
    /// thrown down in its place, with its carried ammunition unless the two share it; the walk over the list ends there. Taking
    /// the one in hand asks for a change to the one taken.
    /// </para>
    /// </remarks>
    private void Pickup()
    {
        if (IsDead)
        {
            return;
        }

        // Everything else on the pickup list is offered whatever the hands are doing (GameFunctions_ePickupQTaskEvent).
        foreach (Pickups.Igi2GenericPickupTask generic in GenericPickups)
        {
            if (generic.IsInWorld)
            {
                generic.Offer(View.Position);
            }
        }

        if (Pickups is not { } pickups)
        {
            return;
        }

        IReadOnlyList<Igi2GunPickup> items = pickups.Items;
        int count = items.Count;
        for (int i = 0; i < count; i++)
        {
            Igi2GunPickup pickup = items[i];
            if (ActionState == Igi2PlayerActionState.ChangeWeapon || PreviousWeaponType is not null)
            {
                continue;
            }

            bool isAutoPickup = false;
            bool isSwap = false;
            Igi2Inventory? inventory = Weapon.Inventory;
            int category = pickup.WeaponType.WeaponCategory;
            Igi2WeaponItem? carried = inventory?.NextInCategory(category, null);
            if (Channels.WasPressed(Igi2HumanChannel.Activate))
            {
                // The swap first on the action menu, if it is one of this pickup's, is taken.
                for (; carried is not null; carried = inventory!.NextInCategory(category, carried.WeaponType))
                {
                    if (ActionMenu.IsFirst(pickup, carried.WeaponType))
                    {
                        isAutoPickup = true;
                        isSwap = true;
                        break;
                    }
                }
            }
            else if (inventory?.FindWeapon(pickup.WeaponType.WeaponId) is { IsActive: true })
            {
                isAutoPickup = true;
                carried = null;
            }
            else if (carried is null)
            {
                isAutoPickup = true;
                isSwap = true;
            }

            bool offered = pickup.Offer(new Igi2PickupOffer(this, View.Position, isAutoPickup), pickups.Sight, pickups.Materials);
            if (!isSwap)
            {
                if (offered && EvaluateTarget is null && inventory is not null)
                {
                    for (Igi2WeaponItem? other = carried; other is not null; other = inventory.NextInCategory(category, other.WeaponType))
                    {
                        RegisterWeaponSwap(pickup, other.WeaponType);
                    }
                }

                continue;
            }

            if (offered && carried is not null)
            {
                SwapFor(pickup, carried);
                return;
            }
        }
    }

    /// <summary>
    /// Offers a swap on the action menu, <c>HumanPlayer_RegisterWeaponPickupActionItem</c>: unless a pickup of the same weapon is
    /// offered for the same carried one already.
    /// </summary>
    private void RegisterWeaponSwap(Igi2GunPickup pickup, Igi2WeaponTypeTask give)
    {
        if (ActionMenu.IsOffered((item, context) =>
            item is Igi2GunPickup other && ReferenceEquals(context, give) && ReferenceEquals(other.WeaponType, pickup.WeaponType)))
        {
            return;
        }

        ActionMenu.Register(pickup, give);
    }

    /// <summary>
    /// The swap taken (<c>HumanPlayer_Pickup</c>'s end): the carried weapon thrown down, with its carried ammunition unless the
    /// weapon taken fires the same, and a change asked for to the one taken if the thrown one was in hand.
    /// </summary>
    private void SwapFor(Igi2GunPickup pickup, Igi2WeaponItem carried)
    {
        if (Weapon.Inventory is not { } inventory)
        {
            return;
        }

        Igi2WeaponTypeTask give = carried.WeaponType;
        bool shareAmmo = ReferenceEquals(
            pickup.WeaponType.AmmoInfo(0, inventory.Tables).AmmoType,
            give.AmmoInfo(0, inventory.Tables).AmmoType);
        bool wasHeld = ReferenceEquals(HeldItem?.WeaponType, give);
        ThrowWeapon(give, removeCarriedAmmo: !shareAmmo);
        if (wasHeld)
        {
            ChangeWeaponId = inventory.FindWeapon(pickup.WeaponType.WeaponId)?.Number ?? ChangeWeaponId;
        }
    }
}
