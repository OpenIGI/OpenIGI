using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Level;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.World;

/// <summary>One live world <c>GunPickup</c> and the weapon its ID resolves to.</summary>
public sealed class GunPickup
{
    private Vector3d _position;
    private Matrix3f _orientation;
    private DroppedGunState? _motion;
    private DroppedGunState? _previousMotion;
    private DroppedGunCollider? _collider;
    private DroppedGunMeshBody? _body;
    private RigidMeshBody? _rigid;
    private RetailGunDrop? _retail;
    private bool _realistic;
    private (Vector3d Position, double Speed)? _impact;
    private Vector3d _drawPosition;
    private Matrix3f _drawOrientation;
    private Vector3d _startPosition;
    private Matrix3f _startOrientation;
    private bool _startCollected;

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
        _startPosition = position;
        _startOrientation = orientation;
        Weapon = weapon;
        Ammo = ammo;
    }

    internal void MarkLevelStart()
    {
        _startPosition = Position;
        _startOrientation = Orientation;
        _startCollected = IsCollected;
    }

    // Back where the match began with it, lying still.
    internal void ResetToLevelStart()
    {
        _position = _startPosition;
        _orientation = _startOrientation;
        _motion = null;
        _previousMotion = null;
        _body = null;
        _rigid = null;
        _retail = null;
        _realistic = false;
        _impact = null;
        _drawPosition = _startPosition;
        _drawOrientation = _startOrientation;
        IsCollected = _startCollected;
    }

    /// <summary>Lays the pickup at rest where another machine has it: a co-op server's drop, as a client shows it.</summary>
    /// <param name="position">Where it lies.</param>
    /// <param name="orientation">How it lies.</param>
    public void Rest(Vector3d position, Matrix3f orientation)
    {
        _motion = null;
        _position = position;
        _orientation = orientation;
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

    /// <summary>Gets the rounds its magazine holds, for one a player dropped; <see langword="null"/> for a full one.</summary>
    public int? Rounds { get; internal set; }

    // The inventory that dropped it, which does not take it back until it has stepped out of reach.
    internal object? HeldOffFrom { get; set; }

    /// <summary>Gets how a player threw it -- where it left the hand, how it was turned, and its speed in units a tick -- or
    /// <see langword="null"/> for any other pickup: what another machine flies the same throw from.</summary>
    public (Vector3d Position, Matrix3f Orientation, Vector3d Velocity)? Launch { get; internal set; }

    /// <summary>Gets or sets whether walking over it takes it: off for a bomb mode's bomb, which the mode hands out by its own rule.</summary>
    public bool WalkOverTakes { get; set; } = true;

    /// <summary>Gets whether it is still in the air or sliding: a drop not yet come to rest.</summary>
    public bool IsMoving => _motion is { IsResting: false };

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
        if (_motion is { } motion && _realistic)
        {
            _body = collider.CreateRigidBody(motion.Position, motion.Orientation, motion.Velocity, InitialSpin());
        }
    }

    /// <summary>Gets the weapon's real mass, from its retail record, in kilograms.</summary>
    /// <remarks>A record that names none weighs what a typical rifle does.</remarks>
    public double MassKilograms => Weapon.MassInGrams > 0 ? Weapon.MassInGrams / 1000.0 : 3.0;

    /// <summary>Starts the fall.</summary>
    /// <param name="realistic">Whether to fall as a rigid body; otherwise exactly as the original's pickup.</param>
    /// <param name="velocity">What the gun leaves the hand with, units per tick; the original ignores it.</param>
    internal void StartFalling(bool realistic, Vector3d velocity)
    {
        _realistic = realistic;
        Matrix3f orientation = realistic ? _orientation : RetailGunDrop.LyingOrientation(_orientation);
        _motion = new DroppedGunState(_position, orientation, realistic ? velocity : default);
        _previousMotion = _motion;
        _drawPosition = _position;
        _drawOrientation = orientation;
    }

    /// <summary>Takes the landing this pickup made on its last tick, if it made one.</summary>
    /// <param name="position">Where it struck, in world units.</param>
    /// <param name="speed">How fast it closed, units per tick.</param>
    /// <returns><see langword="true"/> once per landing.</returns>
    internal bool TryTakeImpact(out Vector3d position, out double speed)
    {
        (position, speed) = _impact ?? default;
        bool any = _impact is not null;
        _impact = null;
        return any;
    }

    internal void Tick(RagdollWorld world)
    {
        // A gun at rest is drawn at rest. Without this the blend keeps interpolating from the last moving
        // state -- for the original's drop, a tick of fall below the floor -- on every frame for ever,
        // and the gun flickers up and down in place.
        if (_motion is { IsResting: true } resting)
        {
            _previousMotion = resting;
            return;
        }

        if (_motion is { } motion && !motion.IsResting)
        {
            _previousMotion = motion;
            if (!_realistic)
            {
                _retail ??= new RetailGunDrop(
                    motion.Position,
                    RetailGunDrop.RestHeightBelow(motion.Position, world.Trace ?? world.Cast));
                double falling = _retail.FallSpeed;
                if (_retail.Tick())
                {
                    _impact = (_retail.Position, Math.Abs(falling));
                }

                _motion = new DroppedGunState(_retail.Position, motion.Orientation, default, _retail.IsResting);
                return;
            }

            if (world.Triangles is { } triangles && _collider?.Hull is { } hull)
            {
                // The gun's real mass and the inertia of its own mesh, against the real world triangles.
                _rigid ??= new RigidMeshBody(
                    hull,
                    MassKilograms,
                    motion.Position,
                    motion.Orientation,
                    motion.Velocity,
                    InitialSpin() * 0.5);
                _rigid.Step(triangles);
                if (_rigid.LastImpactSpeed > 0.0)
                {
                    _impact = (_rigid.LastImpactPosition, _rigid.LastImpactSpeed);
                }

                _motion = new DroppedGunState(_rigid.Position, _rigid.Orientation, _rigid.Velocity, _rigid.IsAtRest);
                return;
            }

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
/// <para><c>GunPickup</c>'s proximity handler at <c>0x46D230</c> tests the Human's origin (<c>+32</c>, sent every
/// tick by <c>0x410F70</c>), not its feet, against the pickup: planar distance must be less than 2867.2 units
/// (0.7 m) -- or the model's bounding radius, when that is larger -- and vertical distance less than 4096
/// units (1 m). It does not require aiming or the activate key. On success it sends the human the
/// weapon id plus one full magazine, marks itself for deletion and publishes <c>PICKEDUPWEAPON…</c>.</para>
/// <para>Presentation owns the sound and notification; this registry owns only the level data, range
/// test and task lifetime.</para>
/// </remarks>
public sealed class GunPickupRegistry : ILevelReset
{
    private readonly List<GunPickup> _items = [];
    private readonly List<GunPickupImpact> _impacts = [];
    private readonly AmmoTypeRegistry _ammunition;
    private ScriptVarRegistry? _variables;
    private bool _inWalk;

    /// <summary>
    /// Gets or sets a value indicating whether a taken pickup keeps its task: for a level a match's rounds put back
    /// (<see cref="ILevelReset"/>), where the task has to be there to come back. The pickup is out of play by being taken either
    /// way; only the original's deletion of its task, which nothing reads in such a level, is not done.
    /// </summary>
    public bool KeepsTakenTasks { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a taken pickup is withdrawn rather than deleted: for a level whose save games
    /// may put it back. See <see cref="TakenPickupTask"/>.
    /// </summary>
    public bool WithdrawsTakenTasks { get; set; }

    private GunPickupRegistry(AmmoTypeRegistry ammunition) => _ammunition = ammunition;

    /// <summary>
    /// A registry with no pickup of a level's own: another game's level, whose guns lie where it lays them (<see cref="Lay"/>) and
    /// fall where its humans drop them (<see cref="Spawn"/>).
    /// </summary>
    /// <param name="ammunition">The ammunition table, which names what each weapon is loaded with.</param>
    /// <returns>The registry.</returns>
    public static GunPickupRegistry Empty(AmmoTypeRegistry ammunition)
    {
        ArgumentNullException.ThrowIfNull(ammunition);
        return new GunPickupRegistry(ammunition);
    }

    /// <summary>Gets every resolved pickup, including consumed ones for diagnostics.</summary>
    public IReadOnlyList<GunPickup> Items => _items;

    /// <summary>Builds the pickup list from a loaded level tree, and exports each authored pickup's <c>isPickedUp</c>.</summary>
    /// <param name="root">The level's task tree.</param>
    /// <param name="weapons">The weapon table, which names what each pickup is.</param>
    /// <param name="ammunition">The ammunition table, which names what each weapon is loaded with.</param>
    /// <param name="variables">Where the pickups' variables are exported, or <see langword="null"/> for none.</param>
    /// <remarks>
    /// GunPickup exports <c>%s.isPickedUp</c> on its byte <c>+297</c> (<c>0x46D170</c>), as GenericPickup and AmmoPickup
    /// do theirs. Mission 11 counts GunPickup_202's into EditVariable_101.
    /// </remarks>
    public static GunPickupRegistry Build(
        GameTask root,
        WeaponTypeRegistry weapons,
        AmmoTypeRegistry ammunition,
        ScriptVarRegistry? variables = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(weapons);
        ArgumentNullException.ThrowIfNull(ammunition);

        GunPickupRegistry registry = new(ammunition) { _variables = variables };
        Collect(root, registry, weapons, ammunition);
        if (variables is not null)
        {
            foreach (GunPickup pickup in registry._items)
            {
                if (pickup.Task is { } task)
                {
                    Export(variables, pickup, task);
                }
            }
        }

        return registry;
    }

    private static void Export(ScriptVarRegistry variables, GunPickup pickup, GameTask task) =>
        TakenPickupTask.Export(variables, task, () => pickup.IsCollected, value => pickup.IsCollected = value);

    /// <summary>
    /// Gets or sets whether the pickups are a co-op client's, taken when the server says who took them rather than by whoever
    /// walks over them here.
    /// </summary>
    public bool Mirrored { get; set; }

    /// <summary>Consumes every pickup within the retail reach of the Human's origin this tick.</summary>
    /// <param name="position">The Human's origin (<c>+32</c>) in engine units, not its feet.</param>
    /// <param name="inventory">The player's weapon inventory.</param>
    /// <returns>The pickups collected in level-tree order.</returns>
    public IReadOnlyList<GunPickup> CollectAt(Vector3d position, PlayerWeapons inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (Mirrored)
        {
            return [];
        }

        List<GunPickup>? collected = null;
        foreach (GunPickup pickup in _items)
        {
            bool reached = PickupReach.IsWithinRange(position, pickup.Position);
            if (ReferenceEquals(pickup.HeldOffFrom, inventory))
            {
                pickup.HeldOffFrom = reached ? inventory : null;
                continue;
            }

            if (!pickup.IsCollected && pickup.WalkOverTakes && reached && CollectInto(pickup, inventory))
            {
                (collected ??= []).Add(pickup);
            }
        }

        return collected ?? [];
    }

    /// <summary>Puts a pickup in a player's hands and takes it out of the level, as walking over it does.</summary>
    /// <param name="pickup">The pickup.</param>
    /// <param name="inventory">The player's weapons.</param>
    /// <returns><see langword="false"/> when it was already taken.</returns>
    public bool CollectInto(GunPickup pickup, PlayerWeapons inventory)
    {
        ArgumentNullException.ThrowIfNull(pickup);
        ArgumentNullException.ThrowIfNull(inventory);
        if (pickup.IsCollected || !_items.Contains(pickup) || (inventory.LimitsCarry && !CarrySlots.Admits(inventory, pickup.Weapon, pickup.Ammo)))
        {
            return false;
        }

        // GunPickup::Update forwards its configured clip size. The human handler deliberately
        // leaves an existing weapon in place and turns the same payload into loose reserve ammo.
        inventory.PickUp(pickup.Weapon, pickup.Ammo, pickup.Rounds ?? pickup.Weapon.RoundsPerClip);
        Take(pickup);
        return true;
    }

    /// <summary>
    /// Takes a pickup out of the level as collecting it does, into no one's hands: another game's pickup lies in its place, for
    /// that game's player to take.
    /// </summary>
    /// <param name="pickup">The pickup.</param>
    public void Withdraw(GunPickup pickup)
    {
        ArgumentNullException.ThrowIfNull(pickup);
        if (pickup.IsCollected || !_items.Contains(pickup))
        {
            return;
        }

        Take(pickup);
    }

    /// <summary>Puts each authored pickup's update into the level's walk, where its task deletes itself once taken.</summary>
    /// <param name="walker">The level walk.</param>
    /// <remarks>See <see cref="TakenPickupTask"/>.</remarks>
    public void AttachTo(LevelRun walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        _inWalk = true;
        foreach (GunPickup pickup in _items)
        {
            if (pickup.Task is { } task)
            {
                TakenPickupTask.BindUpdate(
                    walker,
                    task,
                    () => pickup.IsCollected && !KeepsTakenTasks,
                    () => WithdrawsTakenTasks && !Saves.SaveGameLock.IsHeld,
                    _variables,
                    _variables is { } variables ? () => Export(variables, pickup, task) : null);
            }
        }
    }

    // Takes the pickup out of the level. In a level walk, its task stays until its own update deletes it. Without
    // a walk, nothing would run that update, so the task goes now.
    private void Take(GunPickup pickup)
    {
        pickup.IsCollected = true;
        if (!_inWalk && !KeepsTakenTasks && !(WithdrawsTakenTasks && !Saves.SaveGameLock.IsHeld) && pickup.Task is { } task)
        {
            TakenPickupTask.Delete(task, _variables);
        }
    }

    /// <summary>Creates a runtime pickup when a human drops their gun.</summary>
    /// <remarks>
    /// Gun's drop handler calls <c>GunPickup_Create</c> at <c>0x46C980</c>, passing the gun's weapon id
    /// and world transform. Runtime drops deliberately use the same type and range test as authored
    /// <c>GunPickup</c> tasks; only their task owner is absent.</remarks>
    /// <param name="position">The hand position, in world units.</param>
    /// <param name="orientation">The hand orientation.</param>
    /// <param name="weapon">The weapon.</param>
    /// <param name="velocity">What the gun leaves the hand with, units per tick.</param>
    /// <param name="rounds">The rounds left in its magazine, or <see langword="null"/> for a full one.</param>
    /// <param name="droppedBy">The inventory of the player who dropped it, which cannot take it back until it steps out of reach.</param>
    /// <returns>The pickup.</returns>
    public GunPickup Spawn(
        Vector3d position, Matrix3f orientation, WeaponType weapon, Vector3d velocity = default, int? rounds = null, PlayerWeapons? droppedBy = null)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        bool hasAmmo = _ammunition.TryGetByCalibre(weapon.CalibreId, out AmmoType ammo);
        GunPickup pickup = new(null, position, orientation, weapon, hasAmmo ? ammo : null) { Rounds = rounds, HeldOffFrom = droppedBy };
        pickup.StartFalling(GameplayCVars.RealisticDrops.Value, velocity);
        _items.Add(pickup);
        Spawned?.Invoke(pickup);
        return pickup;
    }

    /// <summary>Throws a gun a player lets go of, as I.G.I. 2's player throws its own: always flown as a body, whatever
    /// <see cref="GameplayCVars.RealisticDrops"/> says of the guns the dead let fall, so the throw's speed carries it.</summary>
    /// <param name="position">Where it leaves the hand, in world units.</param>
    /// <param name="orientation">How it is turned.</param>
    /// <param name="weapon">The weapon.</param>
    /// <param name="velocity">Its speed, units a tick.</param>
    /// <param name="rounds">The rounds left in its magazine, or <see langword="null"/> for a full one.</param>
    /// <param name="droppedBy">The thrower's inventory, which cannot take it back until it steps out of reach; none on a
    /// machine flying another's throw.</param>
    /// <returns>The pickup.</returns>
    public GunPickup Throw(Vector3d position, Matrix3f orientation, WeaponType weapon, Vector3d velocity, int? rounds, PlayerWeapons? droppedBy = null)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        bool hasAmmo = _ammunition.TryGetByCalibre(weapon.CalibreId, out AmmoType ammo);
        GunPickup pickup = new(null, position, orientation, weapon, hasAmmo ? ammo : null)
        {
            Rounds = rounds,
            HeldOffFrom = droppedBy,
            Launch = (position, orientation, velocity),
        };
        pickup.StartFalling(realistic: true, velocity);
        _items.Add(pickup);
        Spawned?.Invoke(pickup);
        return pickup;
    }

    /// <summary>Lays a dropped gun where another machine's lies, at rest: a co-op server's drop, as a client shows it.</summary>
    /// <param name="position">Where it lies.</param>
    /// <param name="orientation">How it lies.</param>
    /// <param name="weapon">The weapon.</param>
    /// <param name="rounds">The rounds left in its magazine, or <see langword="null"/> for a full one.</param>
    /// <returns>The pickup.</returns>
    public GunPickup SpawnResting(Vector3d position, Matrix3f orientation, WeaponType weapon, int? rounds = null)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        bool hasAmmo = _ammunition.TryGetByCalibre(weapon.CalibreId, out AmmoType ammo);
        GunPickup pickup = new(null, position, orientation, weapon, hasAmmo ? ammo : null) { Rounds = rounds };
        _items.Add(pickup);
        Spawned?.Invoke(pickup);
        return pickup;
    }

    /// <summary>
    /// What a save keeps of the guns lying in the level: each the level placed, in the order it placed them -- taken or not, and
    /// where it lies -- then each dropped since and not taken, by its weapon. Not retail: Project I.G.I. kept no save games.
    /// </summary>
    /// <param name="weaponAt">The weapon type at a place in the title's table, or <see langword="null"/> for none there.</param>
    /// <returns>The section.</returns>
    /// <remarks>
    /// A taken gun's task is deleted, so a placed gun is kept by its place among the placed rather than against its task. One
    /// still falling is read back lying where it had got to.
    /// </remarks>
    public Saves.ISaveSection SaveSection(Func<int, WeaponType?> weaponAt)
    {
        ArgumentNullException.ThrowIfNull(weaponAt);
        return new Saves.ExchangedSection("Igi1GunPickups", exchange =>
        {
            GunPickup[] placed = [.. _items.Where(static p => p.Task is not null)];
            int count = exchange.Int(placed.Length);
            for (int i = 0; i < count; i++)
            {
                GunPickup? pickup = i < placed.Length ? placed[i] : null;
                bool taken = exchange.Bool(pickup?.IsCollected ?? true);
                Vector3d position = exchange.Position(pickup?.Position ?? default);
                Matrix3f orientation = exchange.Frame(pickup?.Orientation ?? default);
                int? rounds = exchange.Int(pickup?.Rounds);
                if (exchange.IsReading && pickup is not null)
                {
                    pickup.Rest(position, orientation);
                    pickup.Rounds = rounds;
                    if (taken && !pickup.IsCollected)
                    {
                        Take(pickup);
                    }
                    else if (!taken)
                    {
                        // Taken since the save was made: it lies there again.
                        pickup.IsCollected = false;
                    }
                }
            }

            // The guns dropped since the level came up go before the save's own are made again, as I.G.I. 2's load deletes
            // every dropped gun first.
            if (exchange.IsReading)
            {
                for (int i = _items.Count - 1; i >= 0; i--)
                {
                    if (_items[i] is { Task: null } loose)
                    {
                        _items.RemoveAt(i);
                        Removed?.Invoke(loose);
                    }
                }
            }

            GunPickup[] dropped = [.. _items.Where(static p => p.Task is null && !p.IsCollected)];
            int drops = exchange.Int(dropped.Length);
            for (int i = 0; i < drops; i++)
            {
                GunPickup? pickup = i < dropped.Length ? dropped[i] : null;
                int weapon = exchange.Int(pickup?.Weapon.Index ?? -1);
                Vector3d position = exchange.Position(pickup?.Position ?? default);
                Matrix3f orientation = exchange.Frame(pickup?.Orientation ?? default);
                int? rounds = exchange.Int(pickup?.Rounds);
                if (exchange.IsReading && weaponAt(weapon) is { } type)
                {
                    SpawnResting(position, orientation, type, rounds);
                }
            }
        });
    }

    /// <summary>Lays a gun where another game's level laid one of its own: lying still, as a level's authored pickup lies.</summary>
    /// <param name="position">Where it lies, in world units.</param>
    /// <param name="orientation">How it lies.</param>
    /// <param name="weapon">The weapon.</param>
    /// <returns>The pickup.</returns>
    public GunPickup Lay(Vector3d position, Matrix3f orientation, WeaponType weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        bool hasAmmo = _ammunition.TryGetByCalibre(weapon.CalibreId, out AmmoType ammo);
        GunPickup pickup = new(null, position, orientation, weapon, hasAmmo ? ammo : null);
        _items.Add(pickup);
        return pickup;
    }

    /// <summary>Raised as a human drops a gun (<see cref="Spawn"/>), once the pickup lies in the list.</summary>
    public event Action<GunPickup>? Spawned;

    /// <summary>Raised as a dropped gun leaves the level for good: a round, or a save game read, put the level back without it.</summary>
    public event Action<GunPickup>? Removed;

    // The guns there were as the match began; every other was dropped since.
    private readonly HashSet<GunPickup> _atStart = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<GunPickup> _authored = new(ReferenceEqualityComparer.Instance);
    private bool _levelCleared;

    /// <summary>
    /// Gets or sets a value indicating whether the guns the level itself lays about are all gone, for good: a versus match,
    /// where a gun is bought or taken off a player who dropped it. Guns dropped later still lie where they fall; every round
    /// put back keeps the level's own gone.
    /// </summary>
    public bool LevelCleared
    {
        get => _levelCleared;
        set
        {
            // Only as it first goes: what lies about by then is the level's own; a gun dropped after is a player's.
            bool clearing = value && !_levelCleared;
            _levelCleared = value;
            if (clearing)
            {
                foreach (GunPickup pickup in _items)
                {
                    _authored.Add(pickup);
                    pickup.IsCollected = true;
                }
            }
        }
    }

    /// <inheritdoc/>
    public void MarkLevelStart()
    {
        _atStart.Clear();
        foreach (GunPickup pickup in _items)
        {
            pickup.MarkLevelStart();
            _atStart.Add(pickup);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The guns there were as the match began lie where they lay then, taken or not as they were; every gun dropped since is
    /// gone.
    /// </remarks>
    public void ResetToLevelStart(ObjectCollisionIndex? solids)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            GunPickup pickup = _items[i];
            if (!_atStart.Contains(pickup))
            {
                _items.RemoveAt(i);
                Removed?.Invoke(pickup);
            }
            else
            {
                pickup.ResetToLevelStart();
                if (_levelCleared && _authored.Contains(pickup))
                {
                    pickup.IsCollected = true;
                }
            }
        }

        _impacts.Clear();
    }

    /// <summary>Advances runtime gun drops; authored level pickups remain fixed in place.</summary>
    public void Tick(RagdollWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _impacts.Clear();
        foreach (GunPickup pickup in _items)
        {
            if (!pickup.IsCollected)
            {
                pickup.Tick(world);
                if (pickup.TryTakeImpact(out Vector3d at, out double speed))
                {
                    GunPickupImpact impact = new(pickup, at, speed);
                    _impacts.Add(impact);
                    Landed?.Invoke(impact);
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets what is told of each landing as the gun makes it: <c>0x46CEA0</c> draws its clatter in the
    /// pickup's own tick, so whoever plays it draws there too.
    /// </summary>
    public Action<GunPickupImpact>? Landed { get; set; }

    /// <summary>Gets the landings dropped guns made on the last tick, for their sound.</summary>
    public IReadOnlyList<GunPickupImpact> Impacts => _impacts;

    private static void Collect(
        GameTask task,
        GunPickupRegistry registry,
        WeaponTypeRegistry weapons,
        AmmoTypeRegistry ammunition)
    {
        if (task is DeclaredTask declared
            && string.Equals(declared.Type.Name, "GunPickup", StringComparison.Ordinal)
            && declared.TryGet("Position", out Vector3d position)
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

    private static Matrix3f OrientationOf(DeclaredTask task) =>
        task.TryGetValue("Orientation", out object? raw) && raw is Vector3f angles
            ? Matrix3f.FromEngineEulerAngles(angles.X, angles.Y, angles.Z)
            : Matrix3f.Identity;
}

/// <summary>One landing a dropped gun made.</summary>
/// <param name="Pickup">The gun.</param>
/// <param name="Position">Where it struck, in world units.</param>
/// <param name="Speed">How fast it closed with the surface, units per tick.</param>
public readonly record struct GunPickupImpact(GunPickup Pickup, Vector3d Position, double Speed);
