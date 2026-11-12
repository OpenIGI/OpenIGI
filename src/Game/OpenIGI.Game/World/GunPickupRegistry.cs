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

    /// <summary>Initializes a new instance of the <see cref="GunPickup"/> class.</summary>
    public GunPickup(
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

    /// <summary>Marks the state the level starts in, to be put back by <see cref="ResetToLevelStart"/>.</summary>
    public void MarkLevelStart()
    {
        _startPosition = Position;
        _startOrientation = Orientation;
        _startCollected = IsCollected;
    }

    // Back where the match began with it, lying still.
    /// <summary>Puts back the state the level started in.</summary>
    public void ResetToLevelStart()
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
    public bool IsCollected { get; set; }

    /// <summary>
    /// Gets the pickup's place among those made while the level runs, from one; zero for one the level placed. A dropped
    /// gun is a task made under the level's <c>Dynamic</c> task (<c>0x479C20</c>), run there after everything made before it.
    /// </summary>
    public long Made { get; set; }

    /// <summary>Gets the rounds its magazine holds, for one a player dropped; <see langword="null"/> for a full one.</summary>
    public int? Rounds { get; set; }

    // The inventory that dropped it, which does not take it back until it has stepped out of reach.
    /// <summary>Gets the inventory that dropped the gun, which cannot take it back until it steps out of reach.</summary>
    public object? HeldOffFrom { get; set; }

    /// <summary>Gets how a player threw it -- where it left the hand, how it was turned, and its speed in units a tick -- or
    /// <see langword="null"/> for any other pickup: what another machine flies the same throw from.</summary>
    public (Vector3d Position, Matrix3f Orientation, Vector3d Velocity)? Launch { get; set; }

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
    public void StartFalling(bool realistic, Vector3d velocity)
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
    public bool TryTakeImpact(out Vector3d position, out double speed)
    {
        (position, speed) = _impact ?? default;
        bool any = _impact is not null;
        _impact = null;
        return any;
    }

    /// <summary>Advances a dropped gun one tick of its fall.</summary>
    public void Tick(RagdollWorld world)
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

/// <summary>One landing a dropped gun made.</summary>
/// <param name="Pickup">The gun.</param>
/// <param name="Position">Where it struck, in world units.</param>
/// <param name="Speed">How fast it closed with the surface, units per tick.</param>
public readonly record struct GunPickupImpact(GunPickup Pickup, Vector3d Position, double Speed);
