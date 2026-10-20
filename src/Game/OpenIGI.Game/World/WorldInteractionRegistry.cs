using OpenIGI.Game.Level;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Scripting.Runtime;

#pragma warning disable CS1591 // Public data members are documented by the owning interaction contract.

namespace OpenIGI.Game.World;

/// <summary>Authored explosion data carried by a GenericTBA interaction.</summary>
public sealed record GenericTbaExplosionParameters(
    bool Explodable, string DestroyedModelName, float DamageScale, float ExplosionRadiusMetres,
    float ExplosionFalloffMetres, float ExplosionDamageScale, float ExplosionDelaySeconds,
    int FragmentCount, int FireballCount, string ExplosionExpression, string ExplosionSound)
{
    public float ExplosionRadiusUnits => ExplosionDamage.ToUnits(ExplosionRadiusMetres);
    public float ExplosionFalloffUnits => ExplosionDamage.ToUnits(ExplosionFalloffMetres);
}

/// <summary>One retail task which can answer the human player's <c>GetActivate</c> query.</summary>
public sealed class WorldInteraction
{
    /// <summary>The fixed length of fence locomotion state 41, returned by IGI1 <c>0x440270</c>.</summary>
    public const int FenceTraversalTicks = 6 * 30;

    private readonly object _userOccupant = new();
    private int _pressStamp;
    private int _useStamp;
    private int _completeStamp;
    private Func<bool?>? _onCondition;
    private Func<bool?>? _lockedCondition;
    private Func<bool>? _openCondition;
    private Func<bool>? _closeCondition;
    private Func<bool?>? _electricCondition;
    private Action? _onPress;
    private readonly Vector3d _authoredAnchor;
    private readonly Vector3d _authoredEnd;
    private Func<Vector3d>? _dynamicOffset;
    private Func<float, Vector3d>? _presentedOffset;
    private Vector3d _offset;
    private readonly PlacedDoor? _placedDoor;
    private readonly Vector3d _authoredMarker;
    private readonly Vector3d _markerLocal;
    private bool _explosionConsumed;
    private bool _explosionPending;

    internal WorldInteraction(
        DeclaredTask task,
        InteractionActivateType type,
        Vector3d anchor,
        Vector3d end,
        Matrix3f orientation,
        float facing,
        int durationTicks,
        string actionSprite,
        PlacedDoor? door,
        bool isPickable,
        AiStationaryGun? stationaryGun,
        Vector3d localBoundsMinimum,
        Vector3d localBoundsMaximum,
        bool hasLocalBounds,
        Vector3d markerPoint,
        Vector3d markerLocal,
        GenericTbaExplosionParameters? explosion = null)
    {
        Task = task;
        Type = type;
        _placedDoor = door;
        if (door is not null)
        {
            door.Conditions = RunDoorConditions;
        }

        _authoredMarker = markerPoint;
        _markerLocal = markerLocal;
        _authoredAnchor = anchor;
        _authoredEnd = end;
        Orientation = orientation;
        Facing = facing;
        DurationTicks = durationTicks;
        ActionSprite = actionSprite;
        IsPickable = isPickable;
        StationaryGun = stationaryGun;
        LocalBoundsMinimum = localBoundsMinimum;
        LocalBoundsMaximum = localBoundsMaximum;
        HasLocalBounds = hasLocalBounds;
        Explosion = explosion;
    }

    public DeclaredTask Task { get; }
    public InteractionActivateType Type { get; }
    public Vector3d Anchor => _authoredAnchor + _offset;
    public Vector3d End => _authoredEnd + _offset;
    public Matrix3f Orientation { get; }
    public float Facing { get; }
    public int DurationTicks { get; }
    public string ActionSprite { get; }
    public DoorState? Door => _placedDoor?.State;
    /// <summary>Whether a locked door allows the player to pick its lock.</summary>
    public bool IsPickable { get; }
    public AiStationaryGun? StationaryGun { get; }
    public Vector3d LocalBoundsMinimum { get; }
    public Vector3d LocalBoundsMaximum { get; }
    public bool HasLocalBounds { get; }
    public ScriptFlag InUse { get; } = new();
    public ScriptFlag Pressed { get; } = new();
    public bool IsOn { get; set; } = true;
    /// <summary>Whether this interaction's player-use state currently owns its stationary gun.</summary>
    public bool IsPlayerUsingStationaryGun => StationaryGun is { } gun
        && InUse.Value
        && gun.IsOccupiedBy(_userOccupant);
    /// <summary>Whether an authored electric fence is currently powered.</summary>
    public bool IsElectrified { get; private set; }
    public bool IsCompleted { get; private set; }
    public bool CompletedThisTick { get; private set; }
    public bool IsSearched { get; internal set; }
    public bool IsPicked { get; private set; }
    public bool IsLocked { get; private set; }
    public bool IsDestroyed { get; internal set; }

    /// <summary>Gets or sets what <see cref="IsOn"/> was the last tick the task ran, its <c>isLastOn</c>.</summary>
    internal bool LastOn { get; set; } = true;

    /// <summary>Gets or sets what <see cref="IsDestroyed"/> was the last tick the task ran, its <c>isLastDestroyed</c>.</summary>
    internal bool LastDestroyed { get; set; }

    /// <summary>Gets a value indicating whether a round breaks this switch, its <c>Destructable</c> flag.</summary>
    public bool IsDestructable { get; init; }

    /// <summary>Gets a value indicating whether a GenericTBA runs ExplodeObject's update, and its children with it: its <c>Explodable</c> flag (<c>+568</c>).</summary>
    public bool IsExplodable { get; init; }
    public bool ExplosionTriggeredThisTick { get; private set; }
    public GenericTbaExplosionParameters? Explosion { get; }

    /// <summary>Gets a value indicating whether the task is in the world: every conditional container above it is active.</summary>
    public bool IsInWorld => ConditionalContainerGate.IsTaskActive(Task);

    /// <summary>Whether the task still offers itself to the retail activation scan.</summary>
    /// <remarks>
    /// Each type's GetActivate: a terminal and a GenericTBA (<c>0x443C80</c>) ask only that it is on, so a hacked terminal is
    /// offered again; a cabinet (<c>0x444860</c>) asks nothing of being searched; a fence (<c>0x4400C0</c>) offers nothing
    /// while it is electrified.
    /// </remarks>
    public bool IsAvailable => Type switch
    {
        InteractionActivateType.Terminal or InteractionActivateType.Generic
            or InteractionActivateType.PlaceExplosive => IsOn,
        InteractionActivateType.Fence => !IsElectrified,
        InteractionActivateType.Door => !IsLocked || IsPickable,
        // Switch::GetActivate (0x445730) offers nothing once the switch is broken.
        InteractionActivateType.Switch => !IsDestroyed,
        _ => true,
    };

    /// <summary>A round strikes the task: its <c>WeaponHit</c> handler.</summary>
    /// <returns>Whether the round broke it.</returns>
    /// <remarks>
    /// <para>
    /// A switch's handler (<c>0x445840</c>, I.G.I. 2's <c>Switch_WeaponHitHandler</c> the same) breaks it when it is
    /// <c>Destructable</c>; nothing ever mends it. Its update then wears its fifth model, and it is never offered again.
    /// Mission 8's three breakable switches name one model for every state, so they look the same broken.
    /// </para>
    /// <para>A cabinet's handler is the engine's empty one: the round stops on it and nothing happens.</para>
    /// </remarks>
    public bool WeaponHit()
    {
        if (Type != InteractionActivateType.Switch || !IsDestructable || IsDestroyed)
        {
            return false;
        }

        IsDestroyed = true;
        return true;
    }

    /// <summary>Where a marker for this task belongs, in world units.</summary>
    /// <param name="from">The human's view position, for the tasks whose answer depends on it.</param>
    /// <param name="tickBlend">How far the frame stands between the last two simulation states.</param>
    /// <returns>A point on the thing itself.</returns>
    /// <remarks>
    /// <para>
    /// The middle of the model, not the task's own origin and not <see cref="CandidatePoint"/>. An
    /// origin is wherever the author put it — the hinge edge of a door, the floor under a terminal —
    /// and the activation point moves with the player by design, which is exactly what a marker must
    /// not do: it belongs to the object, and an object does not move when you take a step sideways.
    /// </para>
    /// <para>
    /// A door is the exception that proves it. Its marker rides the leaf's live swing and slide, so it
    /// stays on the door as the door moves — and only then, because the door has moved. It rides the
    /// <em>presented</em> pose, not the tick's: everything moves on the thirty-hertz tick and is drawn
    /// far more often than that, so a mark that reads the tick state judders along beside a door that
    /// glides.
    /// </para>
    /// <para>
    /// A fence and a wire are the other exception: they are longer than a player can use at once, so
    /// the part of them being offered is the part being marked, which is what
    /// <see cref="CandidatePoint"/> already answers.
    /// </para>
    /// </remarks>
    public Vector3d MarkerPoint(Vector3d from, float tickBlend = 1f)
    {
        if (_placedDoor is { } door)
        {
            (Vector3d position, Matrix3f orientation) = door.PresentedPose(tickBlend, smooth: true);
            Vector3f local = new((float)_markerLocal.X, (float)_markerLocal.Y, (float)_markerLocal.Z);
            Vector3f swung = orientation.Transposed().Transform(local);
            Vector3d leaf = position * WorldScene.UnitsPerMetre;
            return new Vector3d(leaf.X + swung.X, leaf.Y + swung.Y, leaf.Z + swung.Z);
        }

        return Type is InteractionActivateType.Fence or InteractionActivateType.Wire
            ? CandidatePoint(from)
            : _authoredMarker + (_presentedOffset?.Invoke(tickBlend) ?? _offset);
    }

    /// <summary>The nearest point offered to the shared activation resolver.</summary>
    public Vector3d CandidatePoint(Vector3d from)
    {
        if (Type == InteractionActivateType.Door)
        {
            // 0x446C62/0x446CCF offer magic vertex 0 and the human projected onto the door's
            // local X/Z interaction plane. The registry scores the projected point first and the
            // magic point separately, just as the original resolver receives both offers.
            Vector3d local = Orientation.Transform(from - Anchor);
            Vector3d projected = Orientation.Transposed().Transform(new Vector3d(local.X, 0.0, local.Z));
            return Anchor + projected;
        }

        if (Type == InteractionActivateType.Fence && HasLocalBounds)
        {
            // Fence::GetActivate scores the model bounds, not the task origin. Retail fence 302_01_1
            // is eight metres long, so treating its origin as the only candidate makes almost the
            // entire visible fence impossible to use.
            Vector3d local = Orientation.Transform(from - Anchor);
            Vector3d nearest = new(
                Math.Clamp(local.X, LocalBoundsMinimum.X, LocalBoundsMaximum.X),
                Math.Clamp(local.Y, LocalBoundsMinimum.Y, LocalBoundsMaximum.Y),
                Math.Clamp(local.Z, LocalBoundsMinimum.Z, LocalBoundsMaximum.Z));
            return Anchor + Orientation.Transposed().Transform(nearest);
        }

        if (Type != InteractionActivateType.Wire || End == Anchor)
        {
            return Anchor;
        }

        Vector3d segment = End - Anchor;
        double along = Vector3d.Dot(from - Anchor, segment) / segment.LengthSquared;
        return Vector3d.Lerp(Anchor, End, Math.Clamp(along, 0.0, 1.0));
    }

    /// <summary>Projects a mount onto a wire while preserving its retail 3072-unit end margins.</summary>
    public Vector3d WireMountPoint(Vector3d from)
    {
        Vector3d segment = End - Anchor;
        if (Type != InteractionActivateType.Wire || segment.LengthSquared == 0.0)
        {
            return Anchor;
        }
        double margin = Math.Min(0.49, 3072.0 / segment.Length);
        double along = Vector3d.Dot(from - Anchor, segment) / segment.LengthSquared;
        return Vector3d.Lerp(Anchor, End, Math.Clamp(along, margin, 1.0 - margin));
    }

    /// <summary>Gets the human task position whose animation places its hands on the cable.</summary>
    /// <remarks>
    /// IGI1 <c>sub_411F20</c> subtracts 3686.4 along column 2 of the frame built by
    /// <c>sub_43F820</c>/<c>sub_414A60</c>. That column is cable-relative up, not world Z; on a slope
    /// it deliberately contains a horizontal component.
    /// </remarks>
    public Vector3d WireBodyPoint(Vector3d from)
    {
        Vector3d segment = End - Anchor;
        if (Type != InteractionActivateType.Wire || segment.LengthSquared == 0.0)
        {
            return Anchor;
        }

        Vector3d up = WireTransform.CableUp(segment.Normalized());

        return WireMountPoint(from) - (up * 3686.4);
    }

    /// <summary>Gets the exact root placement and gamma of a retail self-driving interaction state.</summary>
    /// <remarks>
    /// These are the constants in the state initializers at <c>0x40BED0</c>, <c>0x40C2F0</c>,
    /// <c>0x40C670</c>, <c>0x40C970</c> and <c>0x40CF70</c>. The interaction animations were authored
    /// against these roots; playing them at the player's arbitrary standing point leaves the hands
    /// visibly detached from the lock, cabinet, keyboard or gun.
    /// </remarks>
    public (Vector3d Position, float Facing) BodyPose(Vector3d from)
    {
        Vector3d local = Orientation.Transform(from - Anchor);
        return Type switch
        {
            InteractionActivateType.Door when local.X >= 0.0 =>
                Pose(new Vector3d(-409.6, 2949.12, 0.0), Facing + MathF.PI),
            InteractionActivateType.Door =>
                Pose(new Vector3d(-409.6, -2949.12, 0.0), Facing),
            InteractionActivateType.Cabinet =>
                Pose(new Vector3d(778.24, 2457.6, 0.0), Facing + MathF.PI),
            InteractionActivateType.Terminal =>
                Pose(new Vector3d(0.0, 2764.8, 0.0), Facing + MathF.PI),
            InteractionActivateType.StationaryGun => StationaryGunPose(from),
            InteractionActivateType.Generic =>
                Pose(new Vector3d(0.0, 3481.6, 0.0), Facing + MathF.PI),
            InteractionActivateType.PlaceExplosive => PlaceExplosivePose(from),
            InteractionActivateType.Fence => FenceMountPose(from),
            _ => (from, Facing),
        };
    }

    /// <summary>Gets the landing root on the far side of the fence mount returned by <see cref="BodyPose"/>.</summary>
    public Vector3d FenceExitPoint(Vector3d from)
    {
        if (Type != InteractionActivateType.Fence)
        {
            return from;
        }

        Vector3d local = Orientation.Transform(from - Anchor);
        local = new Vector3d(
            ClampFenceX(local.X),
            local.Y >= 0.0 ? -2457.6 : 2457.6,
            3891.2);
        return Anchor + Orientation.Transposed().Transform(local);
    }

    private (Vector3d Position, float Facing) FenceMountPose(Vector3d from)
    {
        Vector3d local = Orientation.Transform(from - Anchor);
        bool positiveSide = local.Y >= 0.0;
        local = new Vector3d(
            ClampFenceX(local.X),
            positiveSide ? 2457.6 : -2457.6,
            3891.2);
        Vector3d position = Anchor + Orientation.Transposed().Transform(local);
        return (position, positiveSide ? Facing + MathF.PI : Facing);
    }

    private (Vector3d Position, float Facing) PlaceExplosivePose(Vector3d from)
    {
        Vector3d towardTarget = (Anchor - from).Normalized();
        if (towardTarget.LengthSquared == 0.0)
        {
            return (from, Facing);
        }

        // State 36 raycasts this line and backs the body root 2048 units off the contact. Anchor is the
        // unobstructed result; collision remains the caller's concern, but the authored stand-off and
        // target-facing gamma are preserved here.
        Vector3d position = Anchor - (towardTarget * 2048.0);
        float facing = (float)Math.Atan2(towardTarget.X, towardTarget.Y);
        return (position, facing);
    }

    private (Vector3d Position, float Facing) Pose(Vector3d localOffset, float facing) =>
        (Anchor + Orientation.Transposed().Transform(localOffset), facing);

    // State 33's entry, 0x40C970. It takes the gun task's own position and orientation (0x444B30), not
    // the stand's magic vertex, and stands the body 2867.2 units along the stand's local Y -- the barrel
    // axis -- on whichever side it came from: the orientation is turned by pi (0x4B3790) when the body is
    // on the -Y side. The body keeps its own height and faces along the turned stand's heading plus pi,
    // which from the +Y side is down the barrel. Measured in retail (a probe, mission 4 task 775): the
    // body lands 2867 units behind the gun at yaw 1.2687, the gun's gamma -1.8729 plus pi.
    private (Vector3d Position, float Facing) StationaryGunPose(Vector3d from)
    {
        Vector3d origin = (StationaryGun?.Position ?? _authoredAnchor) + _offset;
        bool fromBehind = Orientation.Transform(from - origin).Y >= 0.0;
        Vector3d along = Orientation.Transposed().Transform(new Vector3d(0.0, fromBehind ? 2867.2 : -2867.2, 0.0));
        return (
            new Vector3d(origin.X + along.X, origin.Y + along.Y, from.Z),
            fromBehind ? Facing + MathF.PI : Facing);
    }

    private double ClampFenceX(double x) => HasLocalBounds
        ? Math.Clamp(x, LocalBoundsMinimum.X, LocalBoundsMaximum.X)
        : x;

    /// <summary>Applies the task-specific front/side gate before the shared aim resolver.</summary>
    public bool CanOfferFrom(Vector3d from)
    {
        if (Type == InteractionActivateType.Door)
        {
            Vector3d local = Orientation.Transform(from - Anchor);
            return local.X > -4096.0 && local.X < 1843.2
                && local.Z > -4096.0 && local.Z < 4096.0;
        }

        if (Type is InteractionActivateType.Wire or InteractionActivateType.Fence)
        {
            return true;
        }

        if (Type == InteractionActivateType.StationaryGun && StationaryGun is { } gun)
        {
            // StationaryGun::GetActivate (0x474E30): only a free gun (+540 clear), and only from behind
            // it -- the eye on the positive side of the turned stand's local Y (+112's middle column),
            // measured from magic vertex 0. From the barrel side retail offers nothing (a probe,
            // mission 4 task 775: Use pressed in front of the gun never enters state 33).
            if (gun.IsOccupied)
            {
                return false;
            }

            Matrix3f turned = gun.TurnedOrientation;
            Vector3d toEye = from - Anchor;
            return (turned.M12 * toEye.X) + (turned.M22 * toEye.Y) + (turned.M32 * toEye.Z) >= 0.0;
        }

        if (Type is InteractionActivateType.Terminal or InteractionActivateType.Switch)
        {
            // Terminal::GetActivate (0x443C80) and Switch::GetActivate (0x445730) transform the
            // human-to-magic-vertex delta by the task's complete orientation. Each offers only
            // positive local Y and a 3072-unit local-X strip. Deriving these axes from Facing
            // reverses the positive side for a rotated control (notably the Mission 14 elevator
            // buttons), placing its activation area beside or behind the visible switch.
            Vector3d local = Orientation.Transform(from - Anchor);
            return local.Y > 0.0 && Math.Abs(local.X) < 3072.0;
        }

        if (Type == InteractionActivateType.Generic)
        {
            // GenericTBA's authored use pose is expressed in the task's model frame.  The former
            // sin/cos gate treated Gamma as a player-facing angle, which is the opposite frame for
            // rotated charges: their visible front was rejected even though it is within the regular
            // two-metre activation scan.  Keep the old two-metre lateral width in the actual model
            // frame, shared with BodyPose and the transformed magic vertex.
            Vector3d local = Orientation.Transform(from - Anchor);
            return local.Y > 0.0 && Math.Abs(local.X) < 8192.0;
        }

        if (Type == InteractionActivateType.Cabinet)
        {
            // Cabinet::GetActivate and the state-34 search pose both use the task's model frame.
            // Reconstructing its front from Gamma reverses the usable side for quarter-turned
            // cabinets, including Level 6's three upper-office cabinets (1500-1502).
            Vector3d local = Orientation.Transform(from - Anchor);
            return local.Y > 0.0 && Math.Abs(local.X) < 8192.0;
        }

        if (Type == InteractionActivateType.Switch)
        {
            // Switch::GetActivate 0x444500: offer from magic-vertex 0 (already in Anchor).
            // Local Y is the panel front (orientation column 1); local X is the 3072-unit strip
            // (DAT_00531890). Facing/sin-cos reverses that side for many authored gammas.
            Vector3d local = Orientation.Transform(from - Anchor);
            return local.Y > 0.0 && Math.Abs(local.X) < 3072.0;
        }

        Vector3d toHuman = from - Anchor;
        Vector3d forward = new(Math.Sin(Facing), Math.Cos(Facing), 0.0);
        Vector3d right = new(Math.Cos(Facing), -Math.Sin(Facing), 0.0);
        double inFront = Vector3d.Dot(toHuman, forward);
        double lateral = Math.Abs(Vector3d.Dot(toHuman, right));

        return Type switch
        {
            InteractionActivateType.StationaryGun => inFront > 0.0 && lateral < 8192.0,
            InteractionActivateType.PlaceExplosive => inFront > 0.0,
            _ => true,
        };
    }

    /// <summary>Gets or sets the level's tick, which a press, a use and a completion are stamped with.</summary>
    internal Func<int> Clock { get; set; } = static () => 0;

    /// <summary>The switch is pressed (<c>0x444BC0</c>): <c>isPressed</c> shifts into <c>isLastPressed</c>, is set, and is stamped.</summary>
    public void Press()
    {
        Pressed.Shift(true);
        _pressStamp = Clock();
        _onPress?.Invoke();
    }

    /// <summary>
    /// A human holding the task uses it this tick: the task's Use, which the human's activate (<c>0x460EE0</c>) calls every tick
    /// it holds it.
    /// </summary>
    /// <remarks>
    /// A terminal (<c>0x443D80</c>), a GenericTBA and a cabinet set <c>isInUse</c> and stamp it, and their update clears it once
    /// two ticks have passed the stamp, so it lasts a use and the two ticks after; a wire (<c>0x43F6D0</c>) sets it unstamped,
    /// and its human clears it as it leaves the wire.
    /// </remarks>
    public void Use()
    {
        InUse.Value = true;
        _useStamp = Clock();
    }

    // The types whose isInUse their own update clears on its stamp, rather than their human as it lets go.
    private bool UseDecays => Type is InteractionActivateType.Terminal or InteractionActivateType.Generic
        or InteractionActivateType.PlaceExplosive or InteractionActivateType.Cabinet;

    /// <summary>Begins use and reports whether this interaction acquired its resource.</summary>
    /// <returns><see langword="false"/> when another occupant owns a stationary gun.</returns>
    public bool BeginUse()
    {
        if (StationaryGun is not null && !StationaryGun.TryMount(_userOccupant))
        {
            return false;
        }

        Use();
        return true;
    }

    public void CancelUse()
    {
        StationaryGun?.Dismount(_userOccupant);
        if (!UseDecays)
        {
            InUse.Value = false;
        }
    }

    public void CompleteUse()
    {
        if (Type == InteractionActivateType.Generic && IsCompleted)
        {
            return;
        }
        StationaryGun?.Dismount(_userOccupant);
        if (!UseDecays)
        {
            InUse.Value = false;
        }

        // 0x443EA0: the finish, its this-tick byte and its stamp, which the task's update clears two ticks on.
        CompletedThisTick = true;
        _completeStamp = Clock();
        switch (Type)
        {
            case InteractionActivateType.Cabinet:
                IsSearched = true;
                break;
            case InteractionActivateType.Door:
                IsPicked = true;
                Door?.Toggle();
                break;
            default:
                IsCompleted = true;
                if (Type == InteractionActivateType.Generic && Explosion is not null)
                {
                    ExplosionTriggeredThisTick = true;
                    _explosionPending = true;
                }
                break;
        }
    }

    /// <summary>Applies the delayed destruction edge once its authored blast is presented.</summary>
    public void MarkExplosionPresented(ObjectCollisionIndex? collision = null)
    {
        if (Type == InteractionActivateType.Generic && Explosion is not null && IsCompleted)
        {
            IsDestroyed = true;
            collision?.SetPassable(Task, passable: true);
        }
    }

    internal bool TryConsumeExplosion(out GenericTbaExplosionParameters parameters)
    {
        if (!_explosionPending || _explosionConsumed || Explosion is null)
        {
            parameters = null!;
            return false;
        }

        _explosionConsumed = true;
        _explosionPending = false;
        parameters = Explosion;
        return true;
    }

    /// <summary>The task's update, where its pulses clear on their stamps and its expressions are read.</summary>
    /// <param name="now">The level's tick.</param>
    /// <remarks>
    /// <para>
    /// Each type's own update, as retail runs it: a switch (<c>0x4450F0</c>) reads its On expression, then, two ticks past its
    /// press, shifts <c>isPressed</c> into <c>isLastPressed</c> and clears it, so <c>isLastPressed</c> holds for the one update
    /// that clears it; a terminal (<c>0x4438E0</c>) and a GenericTBA (<c>0x4480B0</c>) read their On expression, are off while
    /// exploded, and clear <c>isInUse</c> and the finished-this-tick byte two ticks past their stamps; a cabinet
    /// (<c>0x444590</c>) clears <c>isInUse</c> likewise; a fence (<c>0x43FC30</c>) is electrified as its expression says, or
    /// not at all. Every <c>isLast*</c> takes its value in the same update, after the clear.
    /// </para>
    /// <para>
    /// An expression slot that is empty or did not compile leaves its byte as it was, so a script's write to it stands; a door's
    /// expressions are its own update's (<see cref="PlacedDoor.Conditions"/>).
    /// </para>
    /// </remarks>
    internal void Run(int now)
    {
        _offset = _dynamicOffset?.Invoke() ?? default;
        switch (Type)
        {
            case InteractionActivateType.Switch:
                if (_onCondition?.Invoke() is bool switchedOn)
                {
                    IsOn = switchedOn;
                }

                if (now > _pressStamp + 2)
                {
                    Pressed.EndPulse();
                }

                LastOn = IsOn;
                LastDestroyed = IsDestroyed;
                break;
            case InteractionActivateType.Terminal:
            case InteractionActivateType.Generic:
            case InteractionActivateType.PlaceExplosive:
                if (_onCondition?.Invoke() is bool on)
                {
                    IsOn = on;
                }

                ClearUse(now);
                if (now > _completeStamp + 2)
                {
                    CompletedThisTick = false;
                }

                break;
            case InteractionActivateType.Cabinet:
                ClearUse(now);
                break;
            case InteractionActivateType.Fence:
                IsElectrified = _electricCondition?.Invoke() ?? false;
                InUse.CommitTick();
                break;
            default:
                InUse.CommitTick();
                break;
        }

        ExplosionTriggeredThisTick = false;
    }

    private void ClearUse(int now)
    {
        if (now > _useStamp + 2)
        {
            InUse.Value = false;
        }

        InUse.CommitTick();
    }

    // The door's own update runs these (0x446D10), where the level walk reaches it: Open and Close, then Locked, which a
    // picked lock overrides; with no Locked expression the lock stands as it was.
    private void RunDoorConditions()
    {
        _placedDoor!.State.RunConditions(_openCondition, _closeCondition);
        if (_lockedCondition?.Invoke() is bool locked)
        {
            IsLocked = locked;
        }

        if (IsPicked)
        {
            IsLocked = false;
        }
    }

    internal void SetConditions(
        Func<bool?>? on,
        Func<bool?>? locked,
        Func<bool>? open = null,
        Func<bool>? close = null,
        Func<bool?>? electric = null)
    {
        _onCondition = on;
        _lockedCondition = locked;
        _openCondition = open;
        _closeCondition = close;
        _electricCondition = electric;
        IsOn = on?.Invoke() ?? true;
        IsElectrified = electric?.Invoke() ?? false;
        IsLocked = locked?.Invoke() ?? false;
    }

    internal void SetDynamicOffset(Func<Vector3d> offset) => _dynamicOffset = offset;

    /// <summary>Sets where this task's marker is carried to, as the frame presents it.</summary>
    /// <remarks>
    /// The counterpart to <see cref="SetDynamicOffset"/>, and deliberately separate from it: the
    /// simulation's own offset has to be the tick's exact value, because activation is decided on it,
    /// while a marker has to sit where the object is being drawn this frame or it judders against it.
    /// </remarks>
    internal void SetPresentedOffset(Func<float, Vector3d> offset) => _presentedOffset = offset;

    internal void SetPressHandler(Action handler) => _onPress = handler;

    internal bool SegmentTouchesFence(Vector3d from, Vector3d to)
    {
        if (Type != InteractionActivateType.Fence || !IsElectrified || !HasLocalBounds)
        {
            return false;
        }

        Vector3d localFrom = Orientation.Transform(from - Anchor);
        Vector3d localTo = Orientation.Transform(to - Anchor);
        Vector3d delta = localTo - localFrom;
        const double tolerance = HumanSeparation.RadiusInUnits;
        double enter = 0.0;
        double exit = 1.0;
        return Slab(localFrom.X, delta.X, LocalBoundsMinimum.X - tolerance, LocalBoundsMaximum.X + tolerance,
                ref enter, ref exit)
            && Slab(localFrom.Y, delta.Y, LocalBoundsMinimum.Y - tolerance, LocalBoundsMaximum.Y + tolerance,
                ref enter, ref exit)
            && Slab(localFrom.Z, delta.Z, LocalBoundsMinimum.Z - tolerance, LocalBoundsMaximum.Z + tolerance,
                ref enter, ref exit);
    }

    internal bool PointTouchesFence(Vector3d point)
        => SegmentTouchesFence(point, point);

    private static bool Slab(
        double origin,
        double direction,
        double minimum,
        double maximum,
        ref double enter,
        ref double exit)
    {
        if (Math.Abs(direction) < double.Epsilon)
        {
            return origin >= minimum && origin <= maximum;
        }

        double near = (minimum - origin) / direction;
        double far = (maximum - origin) / direction;
        if (near > far)
        {
            (near, far) = (far, near);
        }

        enter = Math.Max(enter, near);
        exit = Math.Min(exit, far);
        return enter <= exit;
    }
}

/// <summary>
/// All direct retail world interactions, with the original two-metre, most-head-on resolver.
/// </summary>
public sealed class WorldInteractionRegistry
{
    /// <summary><c>Human_UpdateActivate</c>'s regular query radius: two metres.</summary>
    public const double ActivateRadius = 8192.0;

    private readonly List<WorldInteraction> _items = [];
    private readonly List<GeneratorPowerState> _generators = [];
    private int _updates;
    private bool _inWalk;

    public IReadOnlyList<WorldInteraction> Items => _items;

    public static WorldInteractionRegistry Build(
        GameTask root,
        LoadedMission mission,
        DoorRegistry doors,
        ScriptVarRegistry variables,
        ElevatorRegistry? elevators = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(doors);
        ArgumentNullException.ThrowIfNull(variables);

        Dictionary<string, MefModel> models = ModelsByName(mission);
        LevelExpressionRuntime expressions = new(variables);
        WorldInteractionRegistry registry = new();
        CollectGenerators(root, registry, variables, expressions);
        Collect(root, registry, models, doors, variables, expressions, elevators);
        return registry;
    }

    /// <summary>Settles every generator whose ExplodeObject exploded after this tick's <see cref="Tick"/>.</summary>
    public void SettleGenerators()
    {
        foreach (GeneratorPowerState generator in _generators)
        {
            if (generator.IsInWorld())
            {
                generator.Settle();
            }
        }
    }

    /// <summary>
    /// Puts every task into the level's update walk, each where the tree places it and in its own update's order: a switch,
    /// terminal, cabinet and generator run their children first (an ExplodeObject's update is its children, then its blast);
    /// a GenericTBA runs them only when it is Explodable, as only then does it run ExplodeObject's update (<c>0x4480B0</c>); a
    /// fence (<c>0x43FC30</c>) and a stationary gun (<c>0x474190</c>) run them last; a wire (<c>0x43F340</c>) not at all. A
    /// door is its own registry's.
    /// </summary>
    /// <param name="walker">The level walk.</param>
    /// <remarks>Once attached, <see cref="Tick"/> does nothing: the walk is the update.</remarks>
    public void AttachTo(LevelRun walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        foreach (GeneratorPowerState generator in _generators)
        {
            walker.Bind(generator.Task, task =>
            {
                walker.RunChildren(task);
                generator.Tick();
            });
        }

        foreach (WorldInteraction item in _items)
        {
            switch (item.Type)
            {
                case InteractionActivateType.Door:
                    break;
                case InteractionActivateType.Fence:
                    walker.Bind(item.Task, task =>
                    {
                        item.Run(Now);
                        walker.RunChildren(task);
                    });
                    break;
                case InteractionActivateType.StationaryGun:
                    walker.Bind(item.Task, task =>
                    {
                        item.Run(Now);
                        if (item.StationaryGun is { IsOccupied: false } emptyGun)
                        {
                            emptyGun.TickEmpty();
                        }

                        walker.RunChildren(task);
                    });
                    break;
                case InteractionActivateType.Wire:
                    walker.Bind(item.Task, () => item.Run(Now));
                    break;
                case InteractionActivateType.Generic or InteractionActivateType.PlaceExplosive when !item.IsExplodable:
                    walker.Bind(item.Task, () => item.Run(Now));
                    break;
                default:
                    walker.Bind(item.Task, task =>
                    {
                        walker.RunChildren(task);
                        item.Run(Now);
                    });
                    break;
            }
        }

        // The level counts its tick once its tree has run; a registry without the level's clock counts with it.
        walker.AddTickEnd(() => _updates++);
        _inWalk = true;
    }

    /// <summary>Advances task pulses and <c>Last</c> script shadows once at 30 Hz, for a level whose walk does not run them.</summary>
    public void Tick()
    {
        if (_inWalk)
        {
            return;
        }

        _updates++;
        int now = Now;

        // A task in a container that is not running is not in the world: the walk does not reach it, so it does not
        // update, and nothing can be activated on it.
        foreach (GeneratorPowerState generator in _generators)
        {
            if (generator.IsInWorld())
            {
                generator.Tick();
            }
        }

        foreach (WorldInteraction item in _items)
        {
            if (!item.IsInWorld)
            {
                continue;
            }

            item.Run(now);

            // An empty gun's aim sinks on its own tick (0x474190). An occupied one is ticked by
            // whoever is on it, with their look.
            if (item.StationaryGun is { IsOccupied: false } emptyGun)
            {
                emptyGun.TickEmpty();
            }
        }
    }

    /// <summary>
    /// Gets or sets the level's tick, LevelFlow's <c>nTick</c> (<c>+104</c> of the task <c>flt_BC2360</c> points at), which
    /// the tasks stamp their pulses with; <see langword="null"/>, or a clock that answers <see langword="null"/>, counts this
    /// registry's own updates instead.
    /// </summary>
    public Func<int?>? Clock { get; set; }

    // The tick a press or a use made between two updates is stamped with: the level's, or the updates counted so far.
    private int Now => Clock?.Invoke() ?? _updates;

    /// <summary>Applies the task activation produced by an AI body's activate input channel.</summary>
    /// <param name="taskId">The level task id named by <c>AIAction_Activate</c>.</param>
    /// <returns><see langword="true"/> when the id names a direct interaction.</returns>
    public bool ActivateFromAi(int taskId)
    {
        WorldInteraction? item = _items.FirstOrDefault(candidate => candidate.Task.Id == taskId);
        if (item is null)
        {
            return false;
        }

        switch (item.Type)
        {
            case InteractionActivateType.Switch:
                item.Press();
                break;
            case InteractionActivateType.Door:
                item.Door?.Toggle();
                break;
            case InteractionActivateType.StationaryGun:
                // AIAction_Activate reaches the gunner's own mount state. Player interaction's
                // BeginUse/CompleteUse pair uses a private player sentinel and would dismount it.
                break;
            default:
                // 0x454DA0 / 0x459D50 press a switch and toggle a door, and otherwise only use what they hold: an AI's
                // activate never hacks a terminal, searches a cabinet or finishes a GenericTBA.
                item.Use();
                break;
        }

        return true;
    }

    /// <summary>Resolves the live stationary-gun task a HumanAI's gunner id names.</summary>
    public AiStationaryGun? FindStationaryGun(int taskId) =>
        _items.FirstOrDefault(item => item.Task.Id == taskId)?.StationaryGun;

    /// <summary>Finds the interaction owned by a placed task.</summary>
    public bool TryGet(GameTask task, out WorldInteraction? interaction)
    {
        ArgumentNullException.ThrowIfNull(task);
        interaction = _items.FirstOrDefault(item => ReferenceEquals(item.Task, task));
        return interaction is not null;
    }

    /// <summary>Hands a round's <c>WeaponHit</c> to the interaction a task is, if it is one.</summary>
    /// <param name="task">The task the round struck.</param>
    /// <returns>Whether the round broke it.</returns>
    public bool WeaponHit(GameTask? task) =>
        task is not null && TryGet(task, out WorldInteraction? item) && item!.WeaponHit();

    /// <summary>Consumes each authored GenericTBA explosion edge at most once.</summary>
    public IEnumerable<(WorldInteraction Interaction, GenericTbaExplosionParameters Parameters)> ConsumeExplosions()
    {
        foreach (WorldInteraction item in _items)
        {
            if (item.TryConsumeExplosion(out GenericTbaExplosionParameters parameters))
            {
                yield return (item, parameters);
            }
        }
    }

    /// <summary>Finds a powered electric fence touched by a player movement segment.</summary>
    public WorldInteraction? FindElectrifiedFenceContact(Vector3d from, Vector3d to) =>
        _items.FirstOrDefault(item => item.IsInWorld && item.SegmentTouchesFence(from, to));

    /// <summary>Finds the powered fence whose collision volume contains a nearby player centre.</summary>
    public WorldInteraction? FindElectrifiedFenceNear(Vector3d point) =>
        _items.FirstOrDefault(item => item.IsInWorld && item.PointTouchesFence(point));

    /// <summary>Finds the eligible task nearest the centre of the player's aim.</summary>
    public WorldInteraction? Resolve(Vector3d eye, float yaw, float pitch = 0f)
        => Resolve(eye, yaw, pitch, out _);

    /// <summary>Finds the eligible task and returns its off-axis angle for cross-family arbitration.</summary>
    public WorldInteraction? Resolve(Vector3d eye, float yaw, float pitch, out double resolvedAngle)
    {
        Vector3d look = new(
            Math.Sin(yaw) * Math.Cos(pitch),
            Math.Cos(yaw) * Math.Cos(pitch),
            Math.Sin(pitch));
        WorldInteraction? best = null;
        double bestAngle = double.MaxValue;

        foreach (WorldInteraction item in _items)
        {
            if (!item.IsInWorld || !item.IsAvailable || !item.CanOfferFrom(eye))
            {
                continue;
            }

            double radius = item.Type == InteractionActivateType.PlaceExplosive
                ? Math.Max(
                    ActivateRadius,
                    ReadReal(
                        item.Task,
                        "Activate radius (meters)",
                        ReadReal(item.Task, "Activate radius(meters)", 2f)) * WorldScene.UnitsPerMetre)
                : ActivateRadius;
            Consider(item, item.CandidatePoint(eye), radius);
            if (item.Type == InteractionActivateType.Door)
            {
                Consider(item, item.Anchor, radius);
            }
        }

        resolvedAngle = bestAngle;
        return best;

        void Consider(WorldInteraction item, Vector3d point, double radius)
        {
            Vector3d delta = point - eye;
            double distance = delta.Length;
            if (distance <= 0.0 || distance >= radius)
            {
                return;
            }

            double cosine = Vector3d.Dot(delta / distance, look);
            if (cosine <= 0.0)
            {
                return;
            }

            double angle = Math.Acos(Math.Clamp(cosine, -1.0, 1.0));
            if (angle < bestAngle)
            {
                bestAngle = angle;
                best = item;
            }
        }
    }

    private static void Collect(
        GameTask task,
        WorldInteractionRegistry registry,
        IReadOnlyDictionary<string, MefModel> models,
        DoorRegistry doors,
        ScriptVarRegistry variables,
        LevelExpressionRuntime expressions,
        ElevatorRegistry? elevators)
    {
        // Every task is collected, in a container that is running or not: retail binds a task's variables as it is
        // created, and a container's ACTIVATE only puts its children in the world (0x487A70). Mission 14 streams its
        // building sections in through AreaActivate containers; skipping their subtrees at load left every door,
        // switch and terminal in them without its exports or its prompt once the player walked in.
        if (task is DeclaredTask declared && TypeOf(task.Type.Name) is { } type)
        {
            Vector3d taskPosition = PositionOf(declared, type);
            Vector3d start = taskPosition;
            Vector3d end = ReadPosition(declared, "Stop position", start);
            float facing = FacingOf(declared);
            Matrix3f orientation = OrientationOf(declared);
            int magicIndex = type is InteractionActivateType.Terminal
                or InteractionActivateType.Generic or InteractionActivateType.PlaceExplosive ? 1 : 0;
            string modelName = ModelNameOf(declared, type);
            // A wire traverses the dynamic model's transformed magic-vertex line. Its Start/Stop task
            // parameters place and stretch that model, but the small local X/Z magic offset is still
            // part of the cable the hands touch. The scene builder uses this same value.
            if (type == InteractionActivateType.Wire
                && models.TryGetValue(modelName, out MefModel? wireModel)
                && WireTransform.TryCreate(declared, wireModel, out WireTransform wire))
            {
                start = wire.CableStart;
                end = wire.CableEnd;
            }

            // Fence evaluates its model bounds. It does not substitute magic vertex 0 for the task position.
            if (type is not InteractionActivateType.Wire and not InteractionActivateType.Fence
                && models.TryGetValue(modelName, out MefModel? model)
                && model.TryGetMagicVertex(magicIndex, out Vector3f local))
            {
                Vector3f offset = orientation.Transposed().Transform(local);
                start += new Vector3d(offset.X, offset.Y, offset.Z);
            }

            PlacedDoor? door = type == InteractionActivateType.Door
                ? doors.Doors.FirstOrDefault(candidate => ReferenceEquals(candidate.Task, task))
                : null;
            AiStationaryGun? stationaryGun = type == InteractionActivateType.StationaryGun
                ? new AiStationaryGun(
                    declared.Id,
                    taskPosition,
                    orientation,
                    ReadReal(declared, "Max up angle", 25f),
                    ReadReal(declared, "Max down angle", -25f),
                    ReadReal(declared, "Max sideways angle", 90f),
                    ReadString(declared, "Weapon ID", "WEAPON_ID_M2HB"),
                    (int)ReadReal(declared, "Ammo", -1f))
                : null;
            if (stationaryGun is not null
                && models.TryGetValue(modelName, out MefModel? standModel)
                && standModel.MagicVertexCount >= 2
                && standModel.TryGetMagicVertex(1, out Vector3f mount))
            {
                // 0x474420 asks 0x414E20 for magic vertex 1 of a stand that has any, and 0x414E20
                // answers the origin unless the stand has more than one.
                stationaryGun.MountVertex = mount;
            }

            (Vector3d localBoundsMinimum, Vector3d localBoundsMaximum, bool hasLocalBounds) =
                type == InteractionActivateType.Fence
                    && models.TryGetValue(modelName, out MefModel? fenceModel)
                    ? BoundsOf(fenceModel)
                    : default;
            // Where a marker for this task goes: the middle of its model, in the task's own frame and
            // then in the world. Every activatable in the level has a model, and its middle is the one
            // point unarguably on the thing; the task position is only where the author dropped its
            // origin, which for a door is the hinge edge and for a terminal the floor beneath it.
            (Vector3d markerPoint, Vector3d markerLocal) =
                MarkerOf(taskPosition, orientation, start, models, modelName);
            WorldInteraction item = new(
                declared, type, start, end, orientation, facing,
                DurationOf(declared, type), SpriteOf(declared, type), door,
                ReadBool(declared, "Pickable"), stationaryGun,
                localBoundsMinimum, localBoundsMaximum, hasLocalBounds,
                markerPoint, markerLocal,
                type == InteractionActivateType.Generic ? ExplosionOf(declared) : null)
            {
                IsDestructable = type == InteractionActivateType.Switch && ReadBool(declared, "Destructable"),
                IsExplodable = type is InteractionActivateType.Generic or InteractionActivateType.PlaceExplosive && ReadBool(declared, "Explodable"),
            };
            if (elevators is not null)
            {
                // Interaction anchors, activation radii and player positions are engine units. The
                // render path uses metres, so do not reuse its converted elevator displacement here.
                item.SetDynamicOffset(() => elevators.GetCurrentDisplacementInUnits(item.Task));

                // The lift's mesh is drawn between ticks; a marker on a button riding it has to be too.
                item.SetPresentedOffset(blend =>
                    elevators.TryGetDisplacement(item.Task, blend, smooth: true, out Vector3d metres)
                        ? metres * WorldScene.UnitsPerMetre
                        : elevators.GetCurrentDisplacementInUnits(item.Task));
                if (type == InteractionActivateType.Switch)
                {
                    item.SetPressHandler(() => elevators.RequestFromSwitch(item.Task));
                }
            }
            string onExpression = ReadString(declared, "On Expression", ReadString(declared, "On", string.Empty));
            string lockedExpression = ReadString(declared, "Locked expression", string.Empty);
            string openExpression = ReadString(declared, "Open door expression", string.Empty);
            string closeExpression = ReadString(declared, "Close door expression", string.Empty);
            string electricExpression = ReadString(declared, "Electric Expression", string.Empty);

            // A terminal's update (0x4438E0) and a GenericTBA's (0x4480B0) force isOn off once the ExplodeObject they derive
            // from has exploded (+400), before they look at the On expression -- a terminal blown up is a terminal switched
            // off. A switch (0x4450F0) reads its On expression alone.
            string explodedSymbol = $"{declared.Name}.isExploded";
            Func<bool> exploded = () => variables.TryRead(explodedSymbol, out QvmValue blown) && blown.ToInt32() != 0;
            Func<bool?>? on = ByteSlot(expressions, onExpression, declared);
            item.SetConditions(
                type is InteractionActivateType.Terminal or InteractionActivateType.Generic or InteractionActivateType.PlaceExplosive
                    ? () => exploded() ? false : on?.Invoke()
                    : type is InteractionActivateType.Switch
                    ? on
                    : null,
                type == InteractionActivateType.Door ? ByteSlot(expressions, lockedExpression, declared) : null,
                type == InteractionActivateType.Door && !string.IsNullOrWhiteSpace(openExpression)
                    ? () => expressions.EvaluateCondition(openExpression, emptyValue: false, declared)
                    : null,
                type == InteractionActivateType.Door && !string.IsNullOrWhiteSpace(closeExpression)
                    ? () => expressions.EvaluateCondition(closeExpression, emptyValue: false, declared)
                    : null,
                type == InteractionActivateType.Fence ? ByteSlot(expressions, electricExpression, declared) : null);
            item.Clock = () => registry.Now;
            registry._items.Add(item);
            BindVariables(item, variables);
        }

        foreach (GameTask child in task.Children)
        {
            Collect(child, registry, models, doors, variables, expressions, elevators);
        }
    }

    private static void CollectGenerators(
        GameTask task,
        WorldInteractionRegistry registry,
        ScriptVarRegistry variables,
        LevelExpressionRuntime expressions)
    {
        if (task is DeclaredTask declared
            && string.Equals(task.Type.Name, "Generator", StringComparison.Ordinal))
        {
            string onExpression = ReadString(
                declared,
                "On expression",
                ReadString(declared, "On Expression", string.Empty));
            // Generator::Run (0x43B500) forces isOn off once the ExplodeObject under it has exploded
            // (+400), before it ever looks at the On expression.
            string explodedSymbol = $"{declared.Name}.isExploded";
            GeneratorPowerState state = new(
                declared,
                ByteSlot(expressions, onExpression, declared),
                () => variables.TryRead(explodedSymbol, out QvmValue exploded) && exploded.ToInt32() != 0,
                ReadBool(declared, "Use backup timer"),
                ReadReal(declared, "Seconds until power backup", 10f),
                () => ConditionalContainerGate.IsTaskActive(declared));
            registry._generators.Add(state);
            variables.BindBool(declared, "isOn", () => state.IsOn, value => state.IsOn = value);
            variables.BindInt(
                declared,
                "nBackupTimer",
                () => state.BackupTimer,
                value => state.BackupTimer = value);

            // The constructor (0x43B490) leaves isOn (+572) off; only the update (0x43B500) evaluates it. A generator in
            // a section the level has not streamed in yet therefore reads off until it first updates.
            if (ConditionalContainerGate.IsTaskActive(declared))
            {
                state.Initialize();
            }
        }

        foreach (GameTask child in task.Children)
        {
            CollectGenerators(child, registry, variables, expressions);
        }
    }

    private sealed class GeneratorPowerState(
        GameTask task,
        Func<bool?>? onCondition,
        Func<bool> isExploded,
        bool useBackupTimer,
        float backupSeconds,
        Func<bool> isInWorld)
    {
        public GameTask Task { get; } = task;

        public Func<bool> IsInWorld { get; } = isInWorld;

        private bool _explodedWhenTicked;

        public bool IsOn { get; set; }
        public int BackupTimer { get; set; } = -1;

        public void Initialize() => IsOn = onCondition?.Invoke() ?? IsOn;

        public void Tick()
        {
            _explodedWhenTicked = isExploded();
            Run(_explodedWhenTicked);
        }

        /// <summary>Runs the tick again for a generator that exploded after it had already ticked.</summary>
        /// <remarks>
        /// Retail's Run calls the ExplodeObject update first, so the explosion and the power cut share a
        /// tick. The port's destructibles tick after the interactions; re-running the whole Run is safe
        /// because every step of it is settled by the state the first pass left.
        /// </remarks>
        public void Settle()
        {
            if (!_explodedWhenTicked && isExploded())
            {
                _explodedWhenTicked = true;
                Run(exploded: true);
            }
        }

        private void Run(bool exploded)
        {
            // 0x43B500: off once exploded; else its On expression, when it has one that compiled -- a generator with none
            // keeps what it had, off from the constructor, and comes on only by its backup.
            if (exploded)
            {
                IsOn = false;
            }
            else if (onCondition?.Invoke() is bool on)
            {
                IsOn = on;
            }

            if (!useBackupTimer)
            {
                return;
            }

            // Generator::Run (0x43B500) starts the backup only after primary power is lost, then
            // publishes power again when the authored 30 Hz countdown reaches zero; the countdown is
            // (__int64)(seconds * 30.0), taken as it starts.
            if (!IsOn && BackupTimer == -1)
            {
                BackupTimer = (int)(backupSeconds * 30.0);
            }

            if (BackupTimer <= 0)
            {
                IsOn = true;
            }
            else
            {
                BackupTimer--;
            }
        }
    }

    private static InteractionActivateType? TypeOf(string name) => name switch
    {
        "Switch" => InteractionActivateType.Switch,
        "Door" => InteractionActivateType.Door,
        "Cabinet" => InteractionActivateType.Cabinet,
        "Terminal" => InteractionActivateType.Terminal,

        // SCameraControl (0x440C20) is registered under Terminal (word_53C680): it inherits the terminal's
        // update, its hack and its isInUse/isOn/isHacked exports, and adds only its isDetection. The cameras
        // of missions 5 to 9 are powered by SCameraControl_N.isOn, so without the terminal there is no isOn
        // and every one of them stayed dark.
        "SCameraControl" => InteractionActivateType.Terminal,
        "StationaryGun" => InteractionActivateType.StationaryGun,
        "Wire" => InteractionActivateType.Wire,
        "Fence" => InteractionActivateType.Fence,
        "GenericTBA" => InteractionActivateType.Generic,
        "PlaceExplosiveTBA" => InteractionActivateType.PlaceExplosive,
        _ => null,
    };

    // A task's expression slot that writes a byte: nothing when the slot is empty or did not compile (sub_4F16C0), else the
    // low byte of the value truncated, as the update stores (__int64)sub_4F16E0 -- so 0.5 and 256 are both false.
    private static Func<bool?>? ByteSlot(LevelExpressionRuntime expressions, string source, GameTask owner) =>
        string.IsNullOrWhiteSpace(source)
            ? null
            : () => expressions.TryEvaluateReal(source, owner, out double value) ? unchecked((byte)(long)value) != 0 : null;

    private static void BindVariables(WorldInteraction item, ScriptVarRegistry variables)
    {
        switch (item.Type)
        {
            case InteractionActivateType.Switch:
                // Switch's exports (0x445860): the six bytes, each writable.
                variables.BindBool(item.Task, "isOn", () => item.IsOn, value => item.IsOn = value);
                variables.BindBool(item.Task, "isLastOn", () => item.LastOn, value => item.LastOn = value);
                variables.BindFlag(item.Task, "isPressed", item.Pressed);
                variables.BindBool(item.Task, "isDestroyed", () => item.IsDestroyed, value => item.IsDestroyed = value);
                variables.BindBool(item.Task, "isLastDestroyed", () => item.LastDestroyed, value => item.LastDestroyed = value);
                break;
            case InteractionActivateType.Cabinet:
                variables.BindFlag(item.Task, "isInUse", item.InUse);
                // The export is the object's own byte, so a script or probe write sticks.
                variables.BindBool(item.Task, "isSearched", () => item.IsSearched, value => item.IsSearched = value);
                break;
            case InteractionActivateType.Terminal:
                variables.BindFlag(item.Task, "isInUse", item.InUse);
                variables.BindBool(item.Task, "isOn", () => item.IsOn, value => item.IsOn = value);
                variables.BindBool(item.Task, "isHacked", () => item.IsCompleted, _ => { });
                variables.BindBool(item.Task, "isHackedThisTick", () => item.CompletedThisTick, _ => { });
                break;
            case InteractionActivateType.Generic:
            case InteractionActivateType.PlaceExplosive:
                variables.BindFlag(item.Task, "isInUse", item.InUse);
                variables.BindBool(item.Task, "isOn", () => item.IsOn, value => item.IsOn = value);
                variables.BindBool(item.Task, "isFinished", () => item.IsCompleted, _ => { });
                variables.BindBool(item.Task, "isFinishedThisTick", () => item.CompletedThisTick, _ => { });
                break;
            case InteractionActivateType.Door when item.Door is { } door:
                // A door that has never updated reports nothing yet (see DoorState.HasTicked).
                variables.BindBool(item.Task, "isLocked", () => door.HasTicked && item.IsLocked, _ => { });
                variables.BindBool(item.Task, "isOpen", () => door.HasTicked && door.IsFullyOpen, _ => { });
                variables.BindBool(item.Task, "isClosed", () => door.HasTicked && door.IsFullyClosed, _ => { });
                variables.BindBool(item.Task, "isLastOpen", () => door.HasTicked && door.WasFullyOpen, _ => { });
                variables.BindBool(item.Task, "isLastClosed", () => door.HasTicked && door.WasFullyClosed, _ => { });
                variables.BindBool(item.Task, "isPicked", () => item.IsPicked, _ => { });
                variables.BindInt(item.Task, "nDoorOpenTicks", () => door.TicksOpen, _ => { });
                break;
        }
    }

    private static int DurationOf(DeclaredTask task, InteractionActivateType type)
    {
        string parameter = type switch
        {
            InteractionActivateType.Door => "Pick lock time (s)",
            InteractionActivateType.Cabinet => "Search Time (s)",
            InteractionActivateType.Terminal => "Hack Time (s)",
            InteractionActivateType.Generic or InteractionActivateType.PlaceExplosive => "Activate Time (s)",
            _ => string.Empty,
        };
        return type == InteractionActivateType.Fence
            ? WorldInteraction.FenceTraversalTicks
            : parameter.Length == 0 ? 0 : Math.Max(1, (int)(ReadReal(task, parameter, 4f) * 30f));
    }

    private static string SpriteOf(DeclaredTask task, InteractionActivateType type) => type switch
    {
        InteractionActivateType.Switch => "action_pressbutton.spr",
        InteractionActivateType.Door => "a_door.spr",
        InteractionActivateType.Cabinet => "action_searchcab.spr",
        InteractionActivateType.Terminal => "action_comput.spr",
        InteractionActivateType.StationaryGun => "stationary.spr",
        InteractionActivateType.Wire => "a_wire.spr",
        InteractionActivateType.Fence => "a_fence.spr",
        InteractionActivateType.Generic or InteractionActivateType.PlaceExplosive =>
            ReadString(task, "Click to select sprite", "a_c4.spr")
                .Replace("STATUSSCREEN:", string.Empty, StringComparison.OrdinalIgnoreCase),
        _ => string.Empty,
    };

    /// <summary>The middle of a task's model, in the world and in the model's own frame.</summary>
    /// <remarks>
    /// Falls back to the task's activation anchor for anything whose model is missing, which keeps a
    /// marker on the object rather than at the origin of the level.
    /// </remarks>
    private static (Vector3d World, Vector3d Local) MarkerOf(
        Vector3d taskPosition,
        Matrix3f orientation,
        Vector3d anchor,
        IReadOnlyDictionary<string, MefModel> models,
        string modelName)
    {
        if (!models.TryGetValue(modelName, out MefModel? model))
        {
            return (anchor, default);
        }

        (Vector3d minimum, Vector3d maximum, bool hasBounds) = BoundsOf(model);
        if (!hasBounds)
        {
            return (anchor, default);
        }

        Vector3d local = (minimum + maximum) * 0.5;
        Vector3f world = orientation.Transposed()
            .Transform(new Vector3f((float)local.X, (float)local.Y, (float)local.Z));
        return (
            new Vector3d(taskPosition.X + world.X, taskPosition.Y + world.Y, taskPosition.Z + world.Z),
            local);
    }

    private static (Vector3d Minimum, Vector3d Maximum, bool HasBounds) BoundsOf(MefModel model)
    {
        ReadOnlySpan<MefVertex> vertices = model.Vertices;
        if (vertices.IsEmpty)
        {
            return default;
        }

        Vector3f first = vertices[0].Position;
        Vector3d minimum = new(first.X, first.Y, first.Z);
        Vector3d maximum = minimum;
        for (int i = 1; i < vertices.Length; i++)
        {
            Vector3f point = vertices[i].Position;
            minimum = new Vector3d(
                Math.Min(minimum.X, point.X),
                Math.Min(minimum.Y, point.Y),
                Math.Min(minimum.Z, point.Z));
            maximum = new Vector3d(
                Math.Max(maximum.X, point.X),
                Math.Max(maximum.Y, point.Y),
                Math.Max(maximum.Z, point.Z));
        }

        return (minimum, maximum, true);
    }

    private static Vector3d PositionOf(DeclaredTask task, InteractionActivateType type) =>
        type == InteractionActivateType.Wire
            ? ReadPosition(task, "Start position", default)
            : ReadPosition(task, "Position", ReadPosition(task, "Position start", default));

    private static string ModelNameOf(DeclaredTask task, InteractionActivateType type) =>
        type == InteractionActivateType.Switch
            ? WorldScene.SwitchModelNameOf(task)
            : ReadString(task, "Model", string.Empty);

    private static float FacingOf(DeclaredTask task)
    {
        if (task.TryGetValue("Orientation", out object? orientation))
        {
            return orientation switch { float angle => angle, Vector3f angles => angles.Z, _ => 0f };
        }
        return task.TryGetValue("Gamma", out object? gamma) && gamma is float value ? value : 0f;
    }

    private static Matrix3f OrientationOf(DeclaredTask task)
    {
        // A door's and a fence's frame are built by 0x4B38E0 exactly, the same matrix the world scene draws them with.
        if (string.Equals(task.Type.Name, "Door", StringComparison.Ordinal)
            && task.TryGetValue("Orientation", out object? doorAngles)
            && doorAngles is Vector3f door)
        {
            return RetailOrientation.EulerMatrix(door.X, door.Y, door.Z);
        }

        if (string.Equals(task.Type.Name, "Fence", StringComparison.Ordinal)
            && task.TryGetValue("Gamma", out object? fenceGamma)
            && fenceGamma is float fence)
        {
            return RetailOrientation.EulerMatrix(0f, 0f, fence);
        }

        if (task.TryGetValue("Orientation", out object? orientation))
        {
            return orientation switch
            {
                Vector3f angles => WorldScene.ToOrientation(angles),
                float gamma => Matrix3f.RotationZ(gamma),
                _ => Matrix3f.Identity,
            };
        }

        return task.TryGetValue("Gamma", out object? rawGamma) && rawGamma is float value
            ? Matrix3f.RotationZ(value)
            : Matrix3f.Identity;
    }

    private static Vector3d ReadPosition(DeclaredTask task, string name, Vector3d fallback) =>
        task.TryGetValue(name, out object? value) && value is Vector3d position ? position : fallback;

    private static bool ReadBool(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is bool flag && flag;

    private static float ReadReal(DeclaredTask task, string name, float fallback) =>
        task.TryGetValue(name, out object? value) ? value switch
        {
            float single => single,
            double real => (float)real,
            int integer => integer,
            _ => fallback,
        } : fallback;

    private static string ReadString(DeclaredTask task, string name, string fallback) =>
        task.TryGetValue(name, out object? value) && value is string text && text.Length > 0 ? text : fallback;

    private static GenericTbaExplosionParameters? ExplosionOf(DeclaredTask task)
    {
        string destroyedModel = ReadString(task, "Destroyed model", string.Empty);
        float damageScale = ReadReal(task, "Damage scale", 1f);
        float radius = ReadReal(task, "Explosion radius", 0f);
        float falloff = ReadReal(task, "Explosion falloff radius", 0f);
        float explosionDamageScale = ReadReal(task, "Explosion damage scale", 1f);
        float delay = ReadReal(task, "Explosion delay", 0f);
        int fragments = ReadInt(task, "Explosion fragments", 0);
        int fireballs = ReadInt(task, "Explosion fireballs", 0);
        string expression = ReadString(task, "Explosion expression", string.Empty);
        string sound = ReadString(task, "Explosion sound", string.Empty);
        bool explodable = ReadBool(task, "Explodable");
        bool hasExplosionContent = explodable || destroyedModel.Length > 0 || fragments > 0 || fireballs > 0
            || expression.Length > 0 || sound.Length > 0 || radius > 0f || falloff > 0f || delay > 0f;
        return hasExplosionContent
            ? new(explodable, destroyedModel, damageScale, radius, falloff, explosionDamageScale, delay,
                fragments, fireballs, expression, sound)
            : null;
    }

    private static int ReadInt(DeclaredTask task, string name, int fallback) =>
        task.TryGetValue(name, out object? value) ? value switch
        {
            int integer => integer,
            short small => small,
            float real => (int)real,
            double real => (int)real,
            _ => fallback,
        } : fallback;

    private static Dictionary<string, MefModel> ModelsByName(LoadedMission mission)
    {
        Dictionary<string, MefModel> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (LoadedPack pack in new[] { mission.CommonPack, mission.LevelPack })
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                if (pack.TryGetModel(instance, out MefModel? model) && model is not null)
                {
                    result[instance.Name] = model;
                }
            }
        }
        return result;
    }
}
