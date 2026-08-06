using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.World;

/// <summary>One live world <c>GunPickup</c> and the weapon its ID resolves to.</summary>
public sealed class GunPickup
{
    private readonly Vector3d _position;
    private readonly Matrix3f _orientation;
    private DroppedGunState? _motion;
    private DroppedGunState? _previousMotion;
    private DroppedGunCollider? _collider;
    private DroppedGunMeshBody? _body;
    private Vector3d _drawPosition;
    private Matrix3f _drawOrientation;

    internal GunPickup(
        DeclaredTask? task,
        Vector3d position,
        Matrix3f orientation,
        WeaponType weapon,
        AmmoType? ammo)
    {
        Task = task;
        _position = position;
        _orientation = orientation;
        Weapon = weapon;
        Ammo = ammo;
    }

    /// <summary>Gets the authored task that owns this pickup, if it came from level data.</summary>
    public DeclaredTask? Task { get; }

    /// <summary>Gets the pickup centre in engine units.</summary>
    public Vector3d Position => _motion?.Position ?? _position;

    /// <summary>Gets the world orientation the weapon model uses.</summary>
    public Matrix3f Orientation => _motion?.Orientation ?? _orientation;

    /// <summary>Gets the presentation-interpolated position for rendering.</summary>
    public Vector3d DrawPosition => IsDynamic ? _drawPosition : Position;

    /// <summary>Gets the presentation-interpolated orientation for rendering.</summary>
    public Matrix3f DrawOrientation => IsDynamic ? _drawOrientation : Orientation;

    /// <summary>Gets the weapon that will be acquired.</summary>
    public WeaponType Weapon { get; }

    /// <summary>Gets the ammunition record the weapon resolves to, if it has one.</summary>
    public AmmoType? Ammo { get; }

    /// <summary>Gets whether the pickup has already been consumed.</summary>
    public bool IsCollected { get; internal set; }

    /// <summary>Gets whether this pickup is a physical runtime gun drop.</summary>
    public bool IsDynamic => _motion is not null;

    /// <summary>Sets the rendered weapon hierarchy used for physical contacts after a runtime drop.</summary>
    /// <remarks>
    /// Rendering owns model loading, so it supplies these vertices after the pickup is created and before
    /// the next simulation tick. Authored stationary pickups do not need a collider.
    /// </remarks>
    public void UseCollider(DroppedGunCollider collider)
    {
        ArgumentNullException.ThrowIfNull(collider);
        _collider = collider;
        if (_motion is { } motion)
        {
            _body = collider.CreateRigidBody(motion.Position, motion.Orientation, motion.Velocity, InitialSpin());
        }
    }

    internal void StartFalling()
    {
        _motion = new DroppedGunState(_position, _orientation, default);
        _previousMotion = _motion;
        _drawPosition = _position;
        _drawOrientation = _orientation;
    }

    internal void Tick(RagdollWorld world)
    {
        if (_motion is { } motion && !motion.IsResting)
        {
            _previousMotion = motion;
            if (_body is { } body)
            {
                // Every rendered weapon contact point is swept through terrain and world surfaces.  No
                // enclosing capsule participates in a dropped pickup's collision.
                body.Step(world, substeps: 4);
                _motion = new DroppedGunState(
                    body.Position,
                    body.Orientation,
                    default,
                    body.IsAtRest);
            }
            else
            {
                _motion = DroppedGunPhysics.Advance(motion, world, _collider);
            }
        }
    }

    /// <summary>Blends the last two simulation states for a rendered frame.</summary>
    public void Present(float tickFraction, bool smooth)
    {
        DroppedGunState current = _motion ?? new DroppedGunState(_position, _orientation, default);
        DroppedGunState previous = _previousMotion ?? current;
        float alpha = smooth ? Math.Clamp(tickFraction, 0f, 1f) : 1f;
        _drawPosition = new Vector3d(
            previous.Position.X + ((current.Position.X - previous.Position.X) * alpha),
            previous.Position.Y + ((current.Position.Y - previous.Position.Y) * alpha),
            previous.Position.Z + ((current.Position.Z - previous.Position.Z) * alpha));
        _drawOrientation = Interpolate(previous.Orientation, current.Orientation, alpha);
    }

    private Vector3d InitialSpin()
    {
        uint state = unchecked((uint)Weapon.WeaponId * 747796405u)
            ^ unchecked((uint)(long)_position.X * 2891336453u)
            ^ unchecked((uint)(long)_position.Y * 1181783497u);
        return new Vector3d(Next(ref state), Next(ref state), Next(ref state));

        static double Next(ref uint state)
        {
            state = (state * 1664525u) + 1013904223u;
            return (((state >> 8) & 0xFFFFu) / 65535.0 - 0.5) * 0.32;
        }
    }

    private static Matrix3f Interpolate(Matrix3f from, Matrix3f to, float alpha)
    {
        Vector3f row1 = (from.Row1 + ((to.Row1 - from.Row1) * alpha)).Normalized();
        Vector3f blended3 = from.Row3 + ((to.Row3 - from.Row3) * alpha);
        Vector3f row2 = Vector3f.Cross(blended3, row1).Normalized();
        return Matrix3f.FromRows(row1, row2, Vector3f.Cross(row1, row2));
    }
}

/// <summary>Finds and resolves the original game's automatic world weapon pickups.</summary>
/// <remarks>
/// <para><c>GunPickup::Update</c> at <c>0x46D230</c> tests the human's feet against the pickup every
/// tick: planar distance must be less than 2867.2 units (0.7 m) and vertical distance less than 4096
/// units (1 m). It does not require aiming or the activate key. On success it sends the human the
/// weapon id plus one full magazine, marks itself for deletion and publishes <c>PICKEDUPWEAPON…</c>.</para>
/// <para>Presentation owns the sound and notification; this registry owns only the level data, range
/// test and task lifetime.</para>
/// </remarks>
public sealed class GunPickupRegistry
{
    /// <summary>The original's horizontal pickup radius, in engine units.</summary>
    public const double PickupRadius = 2867.2;

    /// <summary>The original's vertical pickup tolerance, in engine units.</summary>
    public const double VerticalTolerance = 4096.0;

    private readonly List<GunPickup> _items = [];
    private readonly AmmoTypeRegistry _ammunition;

    private GunPickupRegistry(AmmoTypeRegistry ammunition) => _ammunition = ammunition;

    /// <summary>Gets every resolved pickup, including consumed ones for diagnostics.</summary>
    public IReadOnlyList<GunPickup> Items => _items;

    /// <summary>Builds the pickup list from a loaded level tree.</summary>
    public static GunPickupRegistry Build(
        GameTask root,
        WeaponTypeRegistry weapons,
        AmmoTypeRegistry ammunition)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(weapons);
        ArgumentNullException.ThrowIfNull(ammunition);

        GunPickupRegistry registry = new(ammunition);
        Collect(root, registry, weapons, ammunition);
        return registry;
    }

    /// <summary>Consumes every pickup the player's feet overlap this tick.</summary>
    /// <param name="position">The player feet position in engine units.</param>
    /// <param name="inventory">The player's weapon inventory.</param>
    /// <returns>The pickups collected in level-tree order.</returns>
    public IReadOnlyList<GunPickup> CollectAt(Vector3d position, PlayerWeapons inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        List<GunPickup>? collected = null;
        foreach (GunPickup pickup in _items)
        {
            if (pickup.IsCollected || !IsWithinRange(position, pickup.Position))
            {
                continue;
            }

            // GunPickup::Update forwards its configured clip size. The human handler deliberately
            // leaves an existing weapon in place and turns the same payload into loose reserve ammo.
            inventory.PickUp(pickup.Weapon, pickup.Ammo, pickup.Weapon.RoundsPerClip);
            pickup.IsCollected = true;
            if (pickup.Task is { } task)
            {
                task.Tree.Delete(task);
            }
            (collected ??= []).Add(pickup);
        }

        return collected ?? [];
    }

    /// <summary>Creates a runtime pickup when a human drops their gun.</summary>
    /// <remarks>
    /// Gun's drop handler calls <c>GunPickup_Create</c> at <c>0x46C980</c>, passing the gun's weapon id
    /// and world transform. Runtime drops deliberately use the same type and range test as authored
    /// <c>GunPickup</c> tasks; only their task owner is absent.</remarks>
    public GunPickup Spawn(Vector3d position, Matrix3f orientation, WeaponType weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        bool hasAmmo = _ammunition.TryGetByCalibre(weapon.CalibreId, out AmmoType ammo);
        GunPickup pickup = new(null, position, orientation, weapon, hasAmmo ? ammo : null);
        pickup.StartFalling();
        _items.Add(pickup);
        return pickup;
    }

    /// <summary>Advances runtime gun drops; authored level pickups remain fixed in place.</summary>
    public void Tick(RagdollWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        foreach (GunPickup pickup in _items)
        {
            if (!pickup.IsCollected)
            {
                pickup.Tick(world);
            }
        }
    }

    /// <summary>Tests the exact horizontal/vertical split used by retail.</summary>
    public static bool IsWithinRange(Vector3d player, Vector3d pickup)
    {
        double x = pickup.X - player.X;
        double y = pickup.Y - player.Y;
        return Math.Sqrt((x * x) + (y * y)) < PickupRadius
            && Math.Abs(pickup.Z - player.Z) < VerticalTolerance;
    }

    private static void Collect(
        GameTask task,
        GunPickupRegistry registry,
        WeaponTypeRegistry weapons,
        AmmoTypeRegistry ammunition)
    {
        if (task is DeclaredTask declared
            && string.Equals(declared.Type.Name, "GunPickup", StringComparison.Ordinal)
            && TryReadPosition(declared, out Vector3d position)
            && declared.TryGetValue("ID", out object? raw)
            && raw is string id
            && weapons.TryGetByScriptId(id, out WeaponType weapon))
        {
            bool hasAmmo = ammunition.TryGetByCalibre(weapon.CalibreId, out AmmoType ammo);
            registry._items.Add(new GunPickup(
                declared,
                position,
                OrientationOf(declared),
                weapon,
                hasAmmo ? ammo : null));
        }

        foreach (GameTask child in task.Children)
        {
            Collect(child, registry, weapons, ammunition);
        }
    }

    private static bool TryReadPosition(DeclaredTask task, out Vector3d position)
    {
        if (task.TryGetValue("Position", out object? raw) && raw is Vector3d value)
        {
            position = value;
            return true;
        }

        position = default;
        return false;
    }

    private static Matrix3f OrientationOf(DeclaredTask task) =>
        task.TryGetValue("Orientation", out object? raw) && raw is Vector3f angles
            ? Matrix3f.FromEngineEulerAngles(angles.X, angles.Y, angles.Z)
            : Matrix3f.Identity;
}
