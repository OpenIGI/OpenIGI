using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Pickups;

/// <summary>What a picked-up weapon gives, <c>GunPickupContext_t</c>: the rounds in its load and for each fire type.</summary>
/// <param name="WeaponType">The weapon.</param>
/// <param name="AmmoInClip">The rounds the load holds, <c>nAmmoInClip</c>.</param>
/// <param name="AmmoForFireType">The rounds for each fire type, <c>anAmmoForFiretype</c>.</param>
public readonly record struct Igi2GunPickupGrant(Igi2WeaponTypeTask WeaponType, int AmmoInClip, IReadOnlyList<int> AmmoForFireType);

/// <summary>Who is offered a pickup, <c>PickupContext_t</c>: the human, where its eyes are, and whether it takes the pickup outright.</summary>
/// <param name="Taker">The human.</param>
/// <param name="Eyes">Where its eyes are, <c>HumanView_t.tPos</c> (<c>HumanView+0x1A0</c>): the sight is checked from there.</param>
/// <param name="IsAutoPickup">Whether a pickup within reach is taken outright, <c>isAutoPickup</c>.</param>
public readonly record struct Igi2PickupOffer(IIgi2PickupTaker Taker, Vector3d Eyes, bool IsAutoPickup);

/// <summary>A human a pickup is offered to.</summary>
public interface IIgi2PickupTaker
{
    /// <summary>Gets what the human carries, or <see langword="null"/> for a human with no weapon.</summary>
    Igi2Inventory? Inventory { get; }

    /// <summary>Takes a weapon picked up, <c>Human_GunPickupHandler</c> (<c>igi2 0x500180</c>).</summary>
    /// <param name="grant">What the weapon gives.</param>
    void PickUpGun(in Igi2GunPickupGrant grant);
}

/// <summary>
/// A weapon lying in the level, <c>GunPickup</c>: where it lies, the weapon and rounds it gives, and the test a human
/// passes to take it.
/// </summary>
/// <remarks>
/// <para>
/// A <c>RigidDynCubeObj</c> carrying the weapon's <c>Fixed Model</c>. <c>GunPickup_UpdateInternalDataHandler</c>
/// (<c>igi2 0x46BA00</c>) works out what it gives: a grenade or a medipack one round in its load and one for its first fire
/// type, anything else a full load and nothing more for the first; the second fire type always a full load of its own.
/// </para>
/// <para>
/// Each tick <c>GunPickup_RunHandler</c> (<c>0x46A760</c>) lets a pickup still falling fall, and deletes one that was
/// taken. A pickup the level places starts at rest, so for it the handler is only the deleting. Not yet: the fall, which
/// only a dropped weapon makes (<c>GunPickup_NewFromWeapon</c>, <c>0x46C5E0</c>), and a multiplayer game's respawn.
/// </para>
/// </remarks>
public sealed class Igi2GunPickup
{
    /// <summary>The least a pickup reaches, <c>4096</c> units: a metre.</summary>
    public const float MinimumReach = 4096f;

    /// <summary>How far above or below its reach a pickup still reaches, <c>8192</c> units.</summary>
    public const float HeightReach = 8192f;

    /// <summary>
    /// The groups the sight to a pickup meets, <c>0x08000000</c>: the solid models alone, with neither the ground, the
    /// trees nor the spline segments (<c>GunPickup_IsAbleToPickup</c>, <c>igi2 0x46BFB8</c>).
    /// </summary>
    public const uint SightGroups = Igi2RigidObject.RigidGroup;

    private readonly int[] _ammoForFireType = new int[Igi2WeaponTypeTask.FireTypeSlots];

    /// <summary>Initializes a new instance of the <see cref="Igi2GunPickup"/> class, as the level places it.</summary>
    /// <param name="task">The task the level declares it with, or <see langword="null"/> for one made at run time.</param>
    /// <param name="weaponType">The weapon it gives.</param>
    /// <param name="tables">The session's weapon tables, which say what each fire type loads.</param>
    /// <param name="position">Where it lies.</param>
    /// <param name="orientation">Its orientation, nine floats row by row.</param>
    /// <param name="modelRadius">Its model's radius, LOD 0's <c>vRadius</c>; one for a model that does not exist.</param>
    public Igi2GunPickup(
        Igi2GunPickupTask? task,
        Igi2WeaponTypeTask weaponType,
        Igi2WeaponTables tables,
        Vector3d position,
        IReadOnlyList<float> orientation,
        float modelRadius)
    {
        ArgumentNullException.ThrowIfNull(weaponType);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(orientation);
        Task = task;
        WeaponType = weaponType;
        Position = position;
        Orientation = [.. orientation];
        ModelRadius = modelRadius;

        bool single = weaponType.WeaponCategory is Igi2WeaponCategory.Grenade or Igi2WeaponCategory.Medipack;
        AmmoInClip = single ? 1 : weaponType.AmmoInfo(0, tables).RoundsPerClip;
        _ammoForFireType[0] = single ? 1 : 0;
        _ammoForFireType[1] = weaponType.AmmoInfo(1, tables).RoundsPerClip;
    }

    /// <summary>Gets the task the level declares it with, or <see langword="null"/>.</summary>
    public Igi2GunPickupTask? Task { get; }

    /// <summary>Gets the weapon it gives.</summary>
    public Igi2WeaponTypeTask WeaponType { get; }

    /// <summary>Gets where it lies, <c>tPos</c>.</summary>
    public Vector3d Position { get; }

    /// <summary>Gets its orientation, <c>tOrientation</c>: nine floats, row by row.</summary>
    public float[] Orientation { get; }

    /// <summary>Gets its scale on each local axis, <c>tScale</c> (<c>+0xF8</c>).</summary>
    public Vector3f Scale { get; init; } = new(1f, 1f, 1f);

    /// <summary>Gets its model's radius, LOD 0's <c>vRadius</c>.</summary>
    public float ModelRadius { get; }

    /// <summary>Gets the rounds the load it gives holds, <c>nAmmoInClip</c> (<c>+0x148</c>).</summary>
    public int AmmoInClip { get; }

    /// <summary>Gets the rounds it gives each fire type, <c>anAmmoForFiretype</c> (<c>+0x14C</c>).</summary>
    public IReadOnlyList<int> AmmoForFireType => _ammoForFireType;

    /// <summary>Gets a value indicating whether it was taken and is to be deleted, <c>isDelete</c> (<c>+0x1FC</c>).</summary>
    public bool IsDelete { get; private set; }

    /// <summary>Gets a value indicating whether it is falling, <c>isMoving</c> (<c>+0x1FD</c>): a dropped weapon's.</summary>
    public bool IsMoving { get; init; }

    /// <summary>Gets a value indicating whether a falling pickup has landed, <c>isLanded</c> (<c>+0x1FE</c>).</summary>
    public bool IsLanded { get; init; }

    /// <summary>
    /// Gets how far it reaches: its model's radius scaled by its largest scale, and never under a metre
    /// (<c>GunPickup_IsAbleToPickup</c>, <c>igi2 0x46BC80</c>).
    /// </summary>
    public float Reach
    {
        get
        {
            float larger = Scale.Y <= Scale.Z ? Scale.Z : Scale.Y;
            float largest = larger >= Scale.X ? larger : Scale.X;
            Fpu reach = (Fpu)largest * ModelRadius;
            return reach.Value < MinimumReach ? MinimumReach : reach.F;
        }
    }

    /// <summary>Whether a human with its eyes at a point may take it, <c>GunPickup_IsAbleToPickup</c> (<c>igi2 0x46BC80</c>).</summary>
    /// <param name="eyes">Where the human's eyes are.</param>
    /// <param name="tolerance">How much further across it may be, <c>vRadiusTolerance</c>.</param>
    /// <param name="sight">The level the sight to it is checked through.</param>
    /// <param name="materials">The session's materials, which the sight passes.</param>
    /// <returns><see langword="true"/> when it may be taken.</returns>
    /// <remarks>
    /// Not while it falls. The eyes must be within its reach across, and no more than its reach and two metres above or
    /// below it. And the solid models must leave clear the sight from the eyes to one of three points: the pickup's own
    /// position and its reach either way along its own Y axis, tried in that order from the near end.
    /// </remarks>
    public bool IsAbleToPickup(Vector3d eyes, float tolerance, IIgi2SightQuery sight, Igi2GameMaterials materials)
    {
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(materials);
        if (IsMoving && !IsLanded)
        {
            return false;
        }

        float reach = Reach;
        Fpu dx = (Fpu)Position.X - eyes.X, dy = (Fpu)Position.Y - eyes.Y, dz = (Fpu)Position.Z - eyes.Z;
        Fpu across = Fpu.Sqrt((dy * dy) + (dx * dx));
        if (((Fpu)tolerance + reach).Value <= across.Value
            || ((Fpu)(-HeightReach) - reach).Value >= dz.Value
            || ((Fpu)reach + HeightReach).Value <= dz.Value)
        {
            return false;
        }

        // Each point along the pickup's own Y axis: the matrix's second column times the offset, the zero products of
        // the other two summed after it, then the position added at 24 bits.
        float[] m = Orientation;
        foreach (float along in (ReadOnlySpan<float>)[-reach, 0f, reach])
        {
            Fpu ox = ((Fpu)m[1] * along) + ((Fpu)m[0] * 0f) + ((Fpu)m[2] * 0f);
            Fpu oy = ((Fpu)m[4] * along) + ((Fpu)m[3] * 0f) + ((Fpu)m[5] * 0f);
            Fpu oz = ((Fpu)m[7] * along) + ((Fpu)m[6] * 0f) + ((Fpu)m[8] * 0f);
            Vector3d point = new((ox + Position.X).Value, (oy + Position.Y).Value, (oz + Position.Z).Value);
            if (sight.LineOfSight(new Igi2LineOfSight(eyes, point, materials), SightGroups))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Offers the pickup to a human, <c>GunPickup_PickupHandler</c> (<c>igi2 0x46C0A0</c>).
    /// </summary>
    /// <param name="offer">The human and how it takes pickups.</param>
    /// <param name="sight">The level the sight to the pickup is checked through.</param>
    /// <param name="materials">The session's materials.</param>
    /// <returns>
    /// <see langword="true"/> when the human may take it, <c>isAbleToPickUp</c>: within reach and in sight, and, for a
    /// thrown weapon, a medipack or a mine, not already carrying all the rounds of it that it may.
    /// </returns>
    /// <remarks>A human that takes pickups outright takes one it may (<see cref="Pickup"/>).</remarks>
    public bool Offer(in Igi2PickupOffer offer, IIgi2SightQuery sight, Igi2GameMaterials materials)
    {
        ArgumentNullException.ThrowIfNull(offer.Taker);
        if (!IsAbleToPickup(offer.Eyes, 0f, sight, materials))
        {
            return false;
        }

        if (Igi2WeaponCategory.IsCarriedAsRounds(WeaponType.WeaponCategory)
            && offer.Taker.Inventory is { } inventory
            && WeaponType.AmmoInfo(0, inventory.Tables).AmmoType is { } ammoType
            && inventory.FindAmmo(ammoType.AmmoId) is { } carried
            && carried.Count == carried.Limit)
        {
            return false;
        }

        if (offer.IsAutoPickup)
        {
            Pickup(offer.Taker);
        }

        return true;
    }

    /// <summary>
    /// Gives the human the weapon and marks the pickup for deleting, <c>GunPickup_Pickup</c> (<c>igi2 0x46C010</c>).
    /// </summary>
    /// <param name="taker">The human.</param>
    public void Pickup(IIgi2PickupTaker taker)
    {
        ArgumentNullException.ThrowIfNull(taker);
        taker.PickUpGun(new Igi2GunPickupGrant(WeaponType, AmmoInClip, [.. _ammoForFireType]));
        IsDelete = true;
    }
}
