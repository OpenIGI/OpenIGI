using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;

#pragma warning disable CS1591 // Public data members are documented by the owning interaction contract.

namespace OpenIGI.Game.World;

/// <summary>One retail task which can answer the human player's <c>GetActivate</c> query.</summary>
public sealed class WorldInteraction
{
    /// <summary>The fixed length of fence locomotion state 41, returned by IGI1 <c>0x440270</c>.</summary>
    public const int FenceTraversalTicks = 6 * 30;

    private readonly object _userOccupant = new();
    /// <summary>The definition a switch clicks with as it comes to rest on (<c>0x4450F0</c>).</summary>
    public const string SwitchSound = "switch";

    /// <summary>The definition a powered fence's sound area buzzes with (<c>0x43FC30</c>).</summary>
    public const string FenceSound = "elfence_loop";

    private int _pressStamp;
    private bool _humming;
    private Action<InteractionSoundEvent>? _sounds;
    private int _useStamp;
    private int _completeStamp;
    private Func<bool?>? _onCondition;
    private Func<double>? _lockedCondition;
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
        int placedTaskId = -1,
        int activateAnimation = -1)
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
        PlacedTaskId = placedTaskId;
        ActivateAnimation = activateAnimation;
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

    /// <summary>Marks the interaction completed, as its completion does, for the scenario runs that script one.</summary>
    internal void CompleteForTesting() => IsCompleted = true;
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

    /// <summary>Gets where the task stands, its <c>Position</c>, before any lift carries it.</summary>
    public Vector3d TaskPosition { get; init; }

    /// <summary>Gets the model a switch wears: 0 on, 1 on and pressed, 2 off, 3 off and pressed, 4 destroyed (<c>0x4450F0</c>).</summary>
    public int ModelSlot { get; private set; }

    /// <summary>Gets a fence's span along its own X, its model's magic vertices 0 and 1, which its sound area covers.</summary>
    public (float First, float Second)? FenceSpan { get; init; }

    /// <summary>Gets or sets where the task's sounds go.</summary>
    internal Action<InteractionSoundEvent>? Sounds
    {
        get => _sounds;
        set => _sounds = value;
    }

    /// <summary>Gets a value indicating whether a GenericTBA runs ExplodeObject's update, and its children with it: its <c>Explodable</c> flag (<c>+568</c>).</summary>
    public bool IsExplodable { get; init; }

    /// <summary>
    /// The clip states 35 and 36 play on entry (<c>Activate Anim</c>, the Int16 at <c>+586</c> read by <c>0x4485D0</c>,
    /// started looping with no blend), or -1. Every retail charge names 245.
    /// </summary>
    public int ActivateAnimation { get; }

    /// <summary>A PlaceExplosiveTBA's <c>Task ID to place</c> (<c>+684</c>, -1 by default), the charge model it moves.</summary>
    public int PlacedTaskId { get; }

    /// <summary>Where the last plant put the placed task, in world units, or <see langword="null"/> before one.</summary>
    public Vector3d? PlacedPosition { get; private set; }

    /// <summary>The placed task's orientation from the last plant.</summary>
    public Matrix3f PlacedOrientation { get; private set; } = Matrix3f.Identity;

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

    /// <summary>How far below a sloped wire the zip-line hangs its rider — <c>sub_411F20</c>'s 3686.4.</summary>
    public const double ZiplineDrop = 3686.4;

    /// <summary>How far below a level wire the hand-over-hand rider hangs — <c>sub_411AE0</c>'s 2150.4.</summary>
    public const double WireDragDrop = 2150.4;

    /// <summary>Gets the human task position whose animation places its hands on the cable.</summary>
    /// <param name="from">The position projected onto the cable.</param>
    /// <param name="drop">How far below the cable the task hangs: <see cref="ZiplineDrop"/> or <see cref="WireDragDrop"/>.</param>
    /// <remarks>
    /// IGI1 <c>sub_411F20</c> and <c>sub_411AE0</c> subtract the drop along column 2 of the frame built by
    /// <c>sub_43F820</c>/<c>sub_414A60</c>. That column is cable-relative up, not world Z; on a slope
    /// it deliberately contains a horizontal component.
    /// </remarks>
    public Vector3d WireBodyPoint(Vector3d from, double drop = ZiplineDrop)
    {
        Vector3d segment = End - Anchor;
        if (Type != InteractionActivateType.Wire || segment.LengthSquared == 0.0)
        {
            return Anchor;
        }

        Vector3d up = WireTransform.CableUp(segment.Normalized());

        return WireMountPoint(from) - (up * drop);
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
            InteractionActivateType.PlaceExplosive => UntracedPlantPose(from),
            InteractionActivateType.Fence => FenceMountPose(from),
            _ => (from, Facing),
        };
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

    /// <summary>State 36's entry (<c>0x40D180</c>): the body pose and the point the charge goes, from the eye.</summary>
    /// <param name="eye">The human's view position (<c>HumanView+224</c>), in world units.</param>
    /// <param name="trace">The world cast <c>0x4CC2A0</c> makes, or <see langword="null"/> to cast nothing.</param>
    /// <returns>The pose before the ground snap.</returns>
    /// <remarks>
    /// The entry casts from the eye to the target (the task plus its model's magic vertex 1, <see cref="Anchor"/>).
    /// On a hit the charge goes to the contact and the body faces the struck surface's normal; on a miss the
    /// "contact" is the eye and the direction is eye-to-target. Either way gamma is <c>atan2(d.y, d.x) + π/2</c>
    /// (<c>0x4B2D00</c>) and the body stands 2048 units back from the contact along the normalised eye-to-target
    /// line (<c>0x4B3100</c>). Measured in Mission 6 with the probe: planting tank 1601 from the south-west put
    /// the body at contact − 2048·d̂ and gamma at −0.981745, the facet normal's, not eye-to-target's −0.927.
    /// </remarks>
    public ChargePlantPose PlantPose(Vector3d eye, WorldTraceCast? trace)
    {
        Vector3d target = Anchor;
        Vector3d contact = eye;
        Vector3d along = target - eye;
        Vector3d direction = along;
        if (trace is not null
            && trace(new WorldTraceRequest(eye, target, WorldTraceMask.World), out WorldTraceHit hit))
        {
            contact = hit.Position;
            direction = new Vector3d(hit.Normal.X, hit.Normal.Y, hit.Normal.Z);
        }

        float facing = direction.X == 0.0
            ? MathF.PI / 2f
            : (float)Math.Atan2(direction.Y, direction.X) + (MathF.PI / 2f);
        Vector3d back = along.LengthSquared > 0.0 ? along.Normalized() * 2048.0 : default;
        return new ChargePlantPose(contact - back, facing, contact);
    }

    private (Vector3d Position, float Facing) UntracedPlantPose(Vector3d eye)
    {
        ChargePlantPose plant = PlantPose(eye, trace: null);
        return (plant.Position, plant.Facing);
    }

    /// <summary>
    /// Moves the placed task to a plant's contact, as state 36's entry writes the EditRigidObj's position and
    /// orientation parameters (<c>+240</c>, <c>+264</c>) with <c>0x4B38E0(π/2, 0, gamma)</c>. The object reads
    /// them when it is next activated (<c>0x4F2B90</c>), which in Mission 6 is the ConditionalContainer that
    /// shows the charge once <c>isFinished</c> rises.
    /// </summary>
    public void PlaceTask(Vector3d contact, float facing)
    {
        if (PlacedTaskId < 0)
        {
            return;
        }

        PlacedPosition = contact;
        PlacedOrientation = WorldScene.ToOrientation(new Vector3f(MathF.PI / 2f, 0f, facing));
    }

    private (Vector3d Position, float Facing) Pose(Vector3d localOffset, float facing) =>
        (Anchor + Orientation.Transposed().Transform(localOffset), facing);

    // State 33's entry, 0x40C970. It takes the gun task's own position and orientation (0x444B30), not
    // the stand's magic vertex, and stands the body 2867.2 units along the stand's local Y -- the barrel
    // axis -- on whichever side it came from: the orientation is turned by pi (0x4B3790) when the body is
    // on the -Y side. The body keeps its own height and faces along the turned stand's heading plus pi,
    // which from the +Y side is down the barrel. Measured on the running original (mission 4 task 775): the
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
            // measured from magic vertex 0. From the barrel side retail offers nothing (measured on the
            // running original, mission 4 task 775: Use pressed in front of the gun never enters state 33).
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
                // A GenericTBA or PlaceExplosiveTBA finishing (0x443EA0) sets isFinished and isFinishedThisTick
                // and stamps the tick, nothing else. Its explosion parameters belong to the ExplodeObject it
                // derives from, whose update the task runs only while Explodable (+568) is set (0x4480B0); every
                // retail charge -- Mission 2's three, Mission 6's two, Mission 8's -- has it FALSE. What blows up
                // is the level's own ExplodeObjects, on their own expressions: Mission 6's tanks 600 ticks after
                // the second charge (LevelTimer_1604), Mission 2's at its outro cutscene.
                IsCompleted = true;
                break;
        }
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

                // On a change of its on, pressed or destroyed byte it wears one of its five models, and the first, on and
                // at rest, comes with its click: the "switch" definition where it stands.
                if (IsOn != LastOn || Pressed.Value != Pressed.Last || IsDestroyed != LastDestroyed)
                {
                    ModelSlot = IsDestroyed ? 4 : IsOn ? (Pressed.Value ? 1 : 0) : (Pressed.Value ? 3 : 2);
                    if (ModelSlot == 0)
                    {
                        Sound(InteractionSound.Played, SwitchSound, TaskPosition + _offset);
                    }
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
                RunFenceHum();
                InUse.CommitTick();
                break;
            default:
                InUse.CommitTick();
                break;
        }
    }

    // 0x43FC30: a powered fence makes its own AmbientArea, buzzing elfence_loop in a box two metres up its middle, the
    // length of the span between its magic vertices 0 and 1 and a metre and a half past each end; it deletes it with the power.
    private void RunFenceHum()
    {
        if (_humming && !IsElectrified)
        {
            _humming = false;
            Sound(InteractionSound.AreaStopped, string.Empty, default);
        }
        else if (!_humming && IsElectrified && FenceSpan is (float first, float second))
        {
            _humming = true;
            double middle = ((double)second + first) * 0.5;
            Vector3f offset = Orientation.Transposed().Transform(new Vector3f((float)middle, 0f, 8192f));
            _sounds?.Invoke(new InteractionSoundEvent(
                Task,
                InteractionSound.AreaStarted,
                FenceSound,
                TaskPosition + new Vector3d(offset.X, offset.Y, offset.Z),
                Orientation,
                new Vector3d((((double)second - first) * 0.00012207031) + 1.5, 1.75, 2.0)));
        }
    }

    private void Sound(InteractionSound what, string sound, Vector3d at) =>
        _sounds?.Invoke(new InteractionSoundEvent(Task, what, sound, at, Matrix3f.Identity, default));

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
        byte locked = IsLocked ? (byte)1 : (byte)0;
        DoorExpressions.Lock(ref locked, IsPicked, _lockedCondition);
        IsLocked = locked != 0;
    }

    internal void SetConditions(
        Func<bool?>? on,
        Func<double>? locked,
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
        IsLocked = locked is not null && unchecked((byte)(long)locked()) != 0;
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
