using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// The player's direct world interactions: what is offered, entering a retail interaction state, and the
/// states that then own locomotion — the timed picks, searches, hacks and plants, the fence vault, the
/// wire ride and the stationary gun.
/// </summary>
/// <remarks>
/// While a state is active it owns the body: the ordinary walk does not run, and the state places the root
/// exactly where its initializer put it.
/// </remarks>
internal sealed class PlayerInteractionDriver
{
    private readonly IPlayerInteractionHost _host;
    private readonly PlayerBody _body;
    private readonly PlayerInputState _input;
    private readonly IgiRandom _random;
    private readonly PlayerShotResolver _shots;
    private WorldInteraction? _activeInteraction;

    // The gun on the stand the player is on, when they are on one. The emplacement's weapon is its
    // own task in the original, fired by the stand with the occupant's trigger (0x474360); this is it.
    private HeldGun? _mountedGun;
    private int _interactionTick;
    private bool _interactionUseArmed;
    private Vector3d _fenceRootDelta;
    private Vector3d _fenceAppliedDelta;
    private bool _fenceClipEnded;
    private int _electricFenceCooldown;
    private float _interactionFacing;
    private Vector3d _wireDirection;
    private double _wireSpeed;

    // The level wire's hand-over-hand state (0x40DDA0): which way the drag clip plays (HumanPlayer+0xD80), the step
    // event last taken (+wireStep), a new step event the last advance crossed, which drag sound comes next, and a
    // sound waiting to be played.
    private int _wireDragDirection;
    private float _wireStep = -1f;
    private float? _wireStepCrossed;
    private int _wireDragSound;
    private string? _pendingWireSound;

    /// <summary>Initializes the driver for one player.</summary>
    /// <param name="host">The player the interactions belong to.</param>
    /// <param name="body">The body the states place.</param>
    /// <param name="input">The player's input state.</param>
    /// <param name="random">The player's random stream, which a mounted gun's cone draws from.</param>
    /// <param name="shots">Where a mounted gun's rounds are resolved.</param>
    internal PlayerInteractionDriver(
        IPlayerInteractionHost host,
        PlayerBody body,
        PlayerInputState input,
        IgiRandom random,
        PlayerShotResolver shots)
    {
        _host = host;
        _body = body;
        _input = input;
        _random = random;
        _shots = shots;
    }

    /// <summary>Gets or sets the activatable the last scan offered, or <see langword="null"/>.</summary>
    internal WorldInteraction? Near { get; set; }

    /// <summary>Gets the interaction whose state currently owns locomotion, or <see langword="null"/>.</summary>
    internal WorldInteraction? Active => _activeInteraction;

    /// <summary>Gets the stand's own gun while the player is on one.</summary>
    internal HeldGun? MountedGun => _mountedGun;

    /// <summary>Gets the stationary gun the player is on and firing, or <see langword="null"/>.</summary>
    internal AiStationaryGun? MountedStationaryGun => _mountedGun is null ? null : _activeInteraction?.StationaryGun;

    /// <summary>Gets the active interaction's retail locomotion state, or -1.</summary>
    internal int LocomotionState => _activeInteraction is null
        ? -1
        : HumanInteractionDispatch.Dispatch(
            _activeInteraction.Type,
            DoorNeedsPicking(_activeInteraction)).LocomotionState;

    /// <summary>Gets the interaction body's facing in the engine/model gamma convention.</summary>
    /// <remarks>
    /// The walking camera's yaw is horizontally mirrored relative to IGI's <c>Z(gamma)</c> body frame
    /// (the same conversion <see cref="LadderPlacement.PlayerYaw"/> documents). The animated body and
    /// mode-3 camera consume the original gamma, so feeding them the camera yaw turns the cable pose
    /// the wrong way and leaves the authored hand contact laterally off the wire.
    /// </remarks>
    internal float Facing => _interactionFacing;

    /// <summary>Consumes this tick's authored root motion for a self-driving interaction.</summary>
    /// <param name="rootDelta">The clip's world-space root step this tick.</param>
    /// <param name="clipEnded">Whether a non-looping clip reached its end this tick.</param>
    /// <remarks>
    /// <para>
    /// Fence animation 171 carries Jones across the obstacle in its root track, and the mover (<c>0x4614B0</c>)
    /// applies all three components of the step to the Human's origin -- the height included, which is how the
    /// origin rises over the fence. The body is drawn at that origin with the root's own height taken out
    /// (<see cref="Ai.ISoldierPresenter.StartPlayerInteractionAnimation"/>), as on a ladder. Keeping the task at its
    /// mount height and drawing the root height on top instead put the whole vault 0.96 m too high: the mount
    /// is already the origin, 3891.2 above the fence's base, and the clip's first root sample is 3938.6.
    /// </para>
    /// <para>
    /// The step moved is the clip's PREVIOUS one. A clip node samples its root and takes the difference
    /// (<c>0x4D4840</c>) before it advances its clock (<c>0x4D4B60</c>), so the origin stands still on the
    /// update that mounts, and the frame on which the clip ends is never moved at all. Measured on mission 1's
    /// fence, retail's origin is the port's of one update before, tick for tick, and its fall starts with the
    /// second-to-last step, not the last.
    /// </para>
    /// </remarks>
    /// <param name="stepEventTime">The time of the step event (8) the clip crossed, if it crossed one.</param>
    internal void ApplyRootMotion(Vector3d rootDelta, bool clipEnded = false, float? stepEventTime = null)
    {
        if (IsOnLevelWire)
        {
            // State 42 rides the drag clip's own root motion (Human_SetExactMotion): the body moves as far as the clip
            // steps, and a step event it has not taken stops it next tick.
            _body.Position += rootDelta;
            if (stepEventTime is { } step && step != _wireStep)
            {
                _wireStepCrossed = step;
            }

            return;
        }

        if (_activeInteraction?.Type != InteractionActivateType.Fence)
        {
            return;
        }

        _body.Position += _fenceRootDelta;
        _fenceAppliedDelta = _fenceRootDelta;
        _fenceRootDelta = rootDelta;
        _fenceClipEnded |= clipEnded;
    }

    /// <summary>Gets whether the active clip loops in the original state initializer.</summary>
    internal bool AnimationLoops => LocomotionState is 30 or 32 or 35 or 36 or 42 or 43;

    /// <summary>Gets the retail held-interaction progress, or <see langword="null"/> outside a timed state.</summary>
    /// <remarks>
    /// HumanPlayer+3432 is enabled only by states 30, 31, 32, 35 and 36. The status handler divides
    /// the current tick at +3428 by the authored total at +3424; fence, wire and stationary-gun states
    /// deliberately do not display this bar.
    /// </remarks>
    internal float? Progress => _activeInteraction?.Type switch
    {
        InteractionActivateType.Door or InteractionActivateType.Cabinet
            or InteractionActivateType.Terminal or InteractionActivateType.Generic
            or InteractionActivateType.PlaceExplosive when _activeInteraction.DurationTicks > 0 =>
                Math.Clamp(_interactionTick / (float)_activeInteraction.DurationTicks, 0f, 1f),
        _ => null,
    };

    /// <summary>Gets the retail full-body animation selected by the active state.</summary>
    internal int Animation => _activeInteraction?.Type switch
    {
        InteractionActivateType.Door => 89,
        InteractionActivateType.Cabinet => 84,
        InteractionActivateType.Terminal => 90,
        InteractionActivateType.StationaryGun => 198,
        InteractionActivateType.Fence => HumanLocomotionStates.For(41).AnimationId,
        InteractionActivateType.Wire => IsSloped(_activeInteraction.End - _activeInteraction.Anchor)
            ? HumanLocomotionStates.For(43).AnimationId
            : HumanLocomotionStates.For(42).AnimationId,
        InteractionActivateType.Generic or InteractionActivateType.PlaceExplosive =>
            _activeInteraction.ActivateAnimation,
        _ => -1,
    };

    /// <summary>Gets the retail action sprite selected by the current task.</summary>
    /// <param name="ladders">The ladders, whose action shows when no task is offered.</param>
    internal string? ActionSprite(PlayerLadderController ladders) => Near is { } interaction
        ? DoorNeedsPicking(interaction) ? "action_picklock.spr" : interaction.ActionSprite
        : ladders.ShowAction ? StatusScreenLayout.LadderActionSprite : null;

    /// <summary>What the remaster's world-anchored interaction marker should point at.</summary>
    /// <param name="eye">The human's view position, in world units.</param>
    /// <param name="tickBlend">
    /// How far this frame stands between the last two simulation states, so a marker on something that
    /// moves — a swinging door, a button riding a lift — sits where that thing is being drawn rather
    /// than where the tick left it.
    /// </param>
    /// <param name="ladders">The ladders, whose offer is marked when no task is running or offered.</param>
    /// <returns>The offer to mark, or <see langword="null"/> when there is none.</returns>
    /// <remarks>
    /// <para>
    /// The same three offers the retail action sprite is chosen from — the resolved activatable, the
    /// ladder in reach, and the timed state currently running — described as flat data so the prompt
    /// can be laid out and tested without reaching back into the simulation.
    /// </para>
    /// <para>
    /// A running interaction is only offered while it has a progress reading. That is the retail
    /// condition for a progress display (HumanPlayer+3432 is set by states 30, 31, 32, 35 and 36 alone),
    /// and it is the right one here too: a fence vault, a wire ride and a mounted gun are not things the
    /// player is being invited to press a key for while they happen.
    /// </para>
    /// </remarks>
    internal InteractionPromptTarget? Prompt(Vector3d eye, float tickBlend, PlayerLadderController ladders)
    {
        if (_activeInteraction is { } running)
        {
            return Progress is { } progress
                ? Describe(running, eye, tickBlend, progress)
                : null;
        }

        if (Near is { } offered)
        {
            return Describe(offered, eye, tickBlend, progress: null);
        }

        return ladders.Prompt();
    }

    /// <summary>Offers this tick's move to the powered fences, and shocks the player once the guard allows.</summary>
    /// <param name="interactions">The level's activatables, or <see langword="null"/> without any.</param>
    /// <param name="previousPosition">Where the body stood before this tick's move.</param>
    internal void TouchFences(WorldInteractionRegistry? interactions, Vector3d previousPosition)
    {
        WorldInteraction? electricFence = interactions?.FindElectrifiedFenceContact(
            previousPosition,
            _body.Position);
        if (ElectricFenceContact.Advance(ref _electricFenceCooldown, electricFence) is { } touchedFence)
        {
            _host.OnElectrifiedFenceTouched(touchedFence);
        }
    }

    /// <summary>Scans for what is offered and takes the use key's rising edge on it.</summary>
    /// <param name="ladders">The ladders, whose most head-on offer competes with the activatables'.</param>
    /// <param name="interactions">The level's activatables, or <see langword="null"/> without any.</param>
    /// <param name="terrain">The level's terrain.</param>
    /// <returns><see langword="true"/> when an interaction was entered or a ladder mounted this tick.</returns>
    internal bool OfferAndActivate(
        PlayerLadderController ladders,
        WorldInteractionRegistry? interactions,
        IGroundHeightField? terrain)
    {
        // Which ladder, if any, is in reach — for the action icon and for mounting.
        ladders.UpdateNear();
        double interactionAngle = double.MaxValue;
        Near = interactions?.Resolve(
            _body.Eye, _body.Yaw, _body.Pitch, out interactionAngle);
        if (ladders.Near is not null && ladders.NearAngle <= interactionAngle)
        {
            Near = null;
        }
        else if (Near is not null)
        {
            ladders.ForgetNear();
        }

        // The use key mounts the ladder in reach. Taken on the rising edge so holding it does not remount
        // the instant the climb ends.
        if (_input.IsRisingEdge(PlayerInputSlot.Activate) && Near is { } offered)
        {
            Activate(offered, terrain);
            return true;
        }

        if (ladders.Near is not null && _input.IsRisingEdge(PlayerInputSlot.Activate))
        {
            // Puts the player on the ladder in reach: face it and snap onto it.
            ladders.Mount();
            _host.MarkPoseChanged();
            _host.SyncView();
            return true;
        }

        return false;
    }

    // Enters the state the dispatch names for the activated task, placing the body where its initializer does.
    private void Activate(WorldInteraction interaction, IGroundHeightField? terrain)
    {
        InteractionDispatchResult dispatch = HumanInteractionDispatch.Dispatch(
            interaction.Type, DoorNeedsPicking(interaction));
        switch (dispatch.Action)
        {
            case InteractionAction.PressSwitch:
                interaction.Press();
                _host.OnTaskActivated(interaction.Task.Id);
                break;
            case InteractionAction.OpenDoor:
                interaction.Door?.Toggle();
                _host.OnAiNoise(AiEvent.Door);
                _host.OnTaskActivated(interaction.Task.Id);
                break;
            case InteractionAction.MountWire:
                BeginWire(interaction);
                return;
            case InteractionAction.ClimbFence:
            case InteractionAction.PickLock:
            case InteractionAction.EnterState:
                if (interaction.Type == InteractionActivateType.StationaryGun && !interaction.BeginUse())
                {
                    Near = null;
                    return;
                }

                _activeInteraction = interaction;
                _interactionTick = 0;
                _interactionUseArmed = false;
                double standingHeight = _body.Position.Z;
                (Vector3d position, float facing) = interaction.BodyPose(_body.Position);
                if (interaction.Type == InteractionActivateType.PlaceExplosive)
                {
                    // State 36 places from the eye, not the feet: it casts to the target, backs the body off the
                    // contact and moves the charge model there, all on entry.
                    ChargePlantPose plant = interaction.PlantPose(_body.Eye, _host.PlantTrace());
                    interaction.PlaceTask(plant.Contact, plant.Facing);
                    (position, facing) = (plant.Position, plant.Facing);
                }

                _body.Position = position;
                SnapBodyForInteraction(_body, interaction.Type, terrain, standingHeight);
                _interactionFacing = facing;
                _body.Yaw = -facing;
                _fenceRootDelta = default;
                _fenceAppliedDelta = default;
                _fenceClipEnded = false;
                if (interaction.Type != InteractionActivateType.StationaryGun)
                {
                    interaction.BeginUse();
                }
                else if (interaction.StationaryGun is { Weapon: null } unarmed)
                {
                    // The stand is mounted but has no gun to fire: its WEAPON_ID_* never resolved against
                    // the roster (SoldierRenderer.Arm binds it). Say so, rather than leave the player in
                    // the interaction orbit wondering why nothing fires.
                    _host.OnUnarmedStationaryGun(unarmed);
                }
                else if (interaction.StationaryGun is { Weapon: { } gunWeapon } gun)
                {
                    // State 33 puts the carried weapon away (0x4137E0 with none) and the stand's own
                    // gun takes the trigger. An emplacement gun names no ammunition record, so it
                    // never spends from a magazine; the stand counts its own rounds.
                    _host.Weapons?.ClearFiredThisTick();
                    _mountedGun = new HeldGun(gunWeapon);
                    gun.TickOccupied();
                    FaceAlongGun(gun);
                    _host.DropViewTickPair();
                    _host.SyncView();
                }
                if (interaction.Type == InteractionActivateType.Fence && interaction.IsElectrified)
                {
                    if (ElectricFenceContact.TryShock(ref _electricFenceCooldown, interaction) is { } touchedFence)
                    {
                        _host.OnElectrifiedFenceTouched(touchedFence);
                    }
                }
                break;
        }

        Near = null;
    }

    /// <summary>Stands the body on the floor beneath an entered state's authored root, for the states that snap it.</summary>
    internal static void SnapBodyForInteraction(
        PlayerBody body,
        InteractionActivateType type,
        IGroundHeightField? terrain,
        double? standingHeight = null)
    {
        if (type is InteractionActivateType.Door
            or InteractionActivateType.Cabinet
            or InteractionActivateType.Terminal
            or InteractionActivateType.Generic
            or InteractionActivateType.PlaceExplosive)
        {
            if (standingHeight is { } floorReference)
            {
                // The authored interaction root is relative to the task and can sit above the floor
                // (Terminal_333's root is at keyboard height). Start Human_SnapToGround from the
                // human's standing height so collision furniture above their feet cannot become the
                // new floor. The 0.4 m probe still admits ordinary floor variation at the new X/Y.
                body.Position = new Vector3d(body.Position.X, body.Position.Y, floorReference);
            }

            // IGI2 HumanPlayer_InitStateActivate* names this sequence explicitly: Human_PopPosition,
            // then Human_SnapToGround. IGI1 has the same pair at sub_4631A0/sub_463260.
            body.SnapInteractionToGround(terrain);
        }
    }

    private void BeginWire(WorldInteraction wire)
    {
        Vector3d segment = wire.End - wire.Anchor;
        if (segment.LengthSquared == 0.0)
        {
            return;
        }

        if (!IsSloped(segment))
        {
            BeginWireDrag(wire, segment);
            return;
        }

        // sub_411F20 projects onto the wire but never mounts on a raw endpoint: both ends reserve
        // exactly 3072 units. Mounting on the endpoint made the next state-42/43 tick read "at end"
        // and immediately hand the body back to the ground mover.
        Vector3d bodyPoint = wire.WireBodyPoint(_body.Eye);
        _wireDirection = segment.Normalized();
        if (_wireDirection.Z > 0.0)
        {
            _wireDirection = -_wireDirection;
        }

        _wireSpeed = 0.0;
        _body.BeginLadderTraversal();
        _body.Position = bodyPoint;
        _body.Yaw = (float)Math.Atan2(_wireDirection.X, _wireDirection.Y);
        _interactionFacing = -_body.Yaw;
        _activeInteraction = wire;
        _interactionTick = 0;
        _interactionUseArmed = false;
        wire.BeginUse();
        Near = null;
    }

    /// <summary>One tick of the active state: its use, its timer and its end.</summary>
    internal void Tick(IGroundHeightField? terrain)
    {
        WorldInteraction interaction = _activeInteraction!;

        // 0x460EE0: a human whose state holds a task uses it every tick it holds it.
        interaction.Use();
        bool use = _input.IsSet(PlayerInputSlot.Activate);
        if (!use)
        {
            _interactionUseArmed = true;
        }

        if (interaction.Type == InteractionActivateType.StationaryGun)
        {
            // 0x40CAE0: a fresh Use press leaves the gun.
            if (use && _interactionUseArmed)
            {
                End(completed: false, terrain);
                return;
            }

            TickMountedGun();
            return;
        }

        if (interaction.Type == InteractionActivateType.Wire)
        {
            TickWire(terrain, use);
            return;
        }

        // All timed retail states are cancelled as soon as Use is released. Fence climbing is a
        // committed traversal, so it is the sole exception.
        if (!use && interaction.Type != InteractionActivateType.Fence)
        {
            End(completed: false, terrain);
            return;
        }

        _interactionTick++;
        if (interaction.Type == InteractionActivateType.Fence)
        {
            TickFence(interaction, terrain);
            return;
        }

        if (_interactionTick >= interaction.DurationTicks)
        {
            End(completed: true, terrain);
        }
    }

    private void TickWire(IGroundHeightField? terrain, bool use)
    {
        WorldInteraction wire = _activeInteraction!;
        if (IsSloped(wire.End - wire.Anchor))
        {
            _wireSpeed = (-_wireDirection.Z * 88.309761) + (_wireSpeed * 0.99000001);
            _body.Position += _wireDirection * _wireSpeed;
        }
        else
        {
            TickWireDrag();
        }

        Vector3d segment = wire.End - wire.Anchor;
        // The body-to-cable offset is perpendicular to the cable, so projecting the body itself gives
        // the same along-wire coordinate. Adding world Z here is wrong for a sloped wire frame.
        double coordinate = Vector3d.Dot(_body.Position - wire.Anchor, segment) / segment.LengthSquared;
        double margin = Math.Min(0.49, 3072.0 / segment.Length);
        if ((use && _interactionUseArmed) || coordinate < margin || coordinate > 1.0 - margin)
        {
            End(completed: false, terrain);
        }
    }

    /// <summary>Mounts a level wire hand over hand — <c>sub_411AE0</c> into state 42 (<c>0x40DD60</c>).</summary>
    /// <remarks>
    /// The task hangs 2150.4 below the cable at the mount's projection, kept 3072 from either end, and faces down the
    /// cable's axis toward its far half: <c>yaw + pi</c> when it mounts past the middle. The drag clip starts still.
    /// </remarks>
    private void BeginWireDrag(WorldInteraction wire, Vector3d segment)
    {
        Vector3d axis = segment.Normalized();
        Vector3d mount = wire.WireMountPoint(_body.Eye);
        Vector3d middle = (wire.Anchor + wire.End) * 0.5;
        _wireDirection = Vector3d.Dot(mount - middle, axis) > 0.0 ? -axis : axis;
        _wireSpeed = 0.0;
        _wireDragDirection = 0;
        _wireStep = -1f;
        _wireStepCrossed = null;
        _pendingWireSound = null;
        _body.BeginLadderTraversal();
        _body.Position = wire.WireBodyPoint(_body.Eye, WorldInteraction.WireDragDrop);
        _body.Yaw = (float)Math.Atan2(_wireDirection.X, _wireDirection.Y);
        _interactionFacing = -_body.Yaw;
        _activeInteraction = wire;
        _interactionTick = 0;
        _interactionUseArmed = false;
        wire.BeginUse();
        Near = null;
    }

    /// <summary>One tick of state 42 — <c>0x40DDA0</c>.</summary>
    /// <remarks>
    /// A step event the clip has not taken before stops it; while it stands still, Forward plays it on and Back plays
    /// it backward, each start sounding the next of <c>wire_drag_1</c> and <c>wire_drag_2</c> (<c>0x538C48</c>).
    /// </remarks>
    private void TickWireDrag()
    {
        if (_wireStepCrossed is { } step)
        {
            _wireStepCrossed = null;
            _wireStep = step;
            _wireDragDirection = 0;
        }

        if (_wireDragDirection != 0)
        {
            return;
        }

        if (_input.IsSet(PlayerInputSlot.MoveUp))
        {
            _wireDragDirection = 1;
        }

        if (_input.IsSet(PlayerInputSlot.MoveDown))
        {
            _wireDragDirection = -1;
        }

        if (_wireDragDirection != 0)
        {
            _pendingWireSound = $"wire_drag_{WireDragSounds[_wireDragSound] + 1}";
            _wireDragSound = (_wireDragSound - 1) & 1;
        }
    }

    // gWireDragSounds (0x538C48).
    private static readonly int[] WireDragSounds = [0, 1];

    // Wire_IsSloped (0x43F8A0): the cable's pitch over two degrees makes it a zip-line.
    private static bool IsSloped(Vector3d segment) =>
        Math.Abs(segment.Normalized().Z) > Math.Sin(0.034906585);

    /// <summary>Gets whether the player hangs from a level wire, hand over hand.</summary>
    internal bool IsOnLevelWire =>
        _activeInteraction is { Type: InteractionActivateType.Wire } wire && !IsSloped(wire.End - wire.Anchor);

    /// <summary>Gets which way the drag clip plays this tick: forward, backward, or still.</summary>
    internal int WireDragDirection => _wireDragDirection;

    /// <summary>Takes the drag sound the last tick started, if it started one.</summary>
    internal string? TakeWireDragSound()
    {
        string? sound = _pendingWireSound;
        _pendingWireSound = null;
        return sound;
    }

    private void TickFence(WorldInteraction fence, IGroundHeightField? terrain)
    {
        // State 41 plants the task root at the authored fence mount and lets animation 171 carry it over.
        // It leaves on the clip's end bit (0x40DD00); the six-second duration accessor (0x440270), the clip's
        // own length, only bounds a climb whose clip never reports. At the end the origin is wherever the clip
        // left it -- 0.94 m up, on the far side -- and state 29 drops it from there with the last step moved as
        // its velocity, rather than snapping to a landing point.
        if (!_fenceClipEnded && _interactionTick < fence.DurationTicks)
        {
            _host.OnAiNoise(AiEvent.Fence);
        }
        else
        {
            _body.BeginFallFromVault(_body.Position, _fenceAppliedDelta);
            End(completed: false, terrain);
        }
    }

    /// <summary>One tick of the gun the player is on: its kick, its trigger and its rounds.</summary>
    /// <remarks>
    /// <para>
    /// <c>0x474190</c> with an occupant: the barrel is shown at its aim plus the last kick, then the
    /// stand sends its gun the occupant's fire input (<c>0x474360</c>) and keeps the kick it answers.
    /// </para>
    /// <para>
    /// The message carries <b>no origin and no orientation</b> — both are null, where the carried-gun
    /// path (<c>0x462E50</c>) passes the eye. So <c>0x478BA0</c> falls back on the gun's own frame: the
    /// round leaves the gun model's magic vertex 2 along the gun's barrel, not the player's eye.
    /// </para>
    /// </remarks>
    private void TickMountedGun()
    {
        _shots.ClearImpacts();
        if (_mountedGun is null || MountedStationaryGun is not { } gun)
        {
            _shots.TickProjectiles();
            return;
        }

        gun.TickOccupied();
        FaceAlongGun(gun);

        bool trigger = _input.IsSet(PlayerInputSlot.Fire)
            && (gun.Ammunition < 0 || gun.RemainingAmmunition > 0);
        _mountedGun.Tick(trigger);

        Matrix3f m = gun.GunOrientation;
        Vector3f right = new(m.M11, m.M21, m.M31);
        Vector3f forward = new(m.M12, m.M22, m.M32);
        Vector3f up = new(m.M13, m.M23, m.M33);
        GunFireResult result = _mountedGun.Fire(trigger, gun.MuzzlePosition, forward, right, up, _random);
        if (result.Fired)
        {
            gun.ConsumeRound();
        }

        gun.RecordShot(result.Fired, result.RecoilX, result.RecoilZ);
        if (result.Fired)
        {
            _shots.ResolveMountedRounds(result.Projectiles, _mountedGun.Weapon);
        }

        _shots.TickProjectiles();
        _host.SyncView();
    }

    // Keeps the body facing down the barrel, so the compass reads true on the stand and the player
    // steps off looking where the gun was.
    internal void FaceAlongGun(AiStationaryGun gun)
    {
        Matrix3f m = gun.GunOrientation;
        _body.Yaw = MathF.Atan2(m.M12, m.M22);
        _body.Pitch = Math.Clamp(
            MathF.Asin(Math.Clamp(m.M32, -1f, 1f)),
            -PlayerBody.PitchLimit,
            PlayerBody.PitchLimit);
    }

    /// <summary>Leaves the active state, finishing or cancelling its task, and hands the body and the view back.</summary>
    internal void End(bool completed, IGroundHeightField? terrain)
    {
        WorldInteraction interaction = _activeInteraction!;
        if (completed)
        {
            interaction.CompleteUse();
            if (interaction.Door is not null)
            {
                // A finished pick unlocks and toggles the door, and posts the same noise (0x40C160).
                _host.OnAiNoise(AiEvent.Door);
            }

            _host.OnTaskActivated(interaction.Task.Id);
        }
        else
        {
            interaction.CancelUse();
        }

        if (_mountedGun is not null)
        {
            // 0x40CAE0 hands the view back (0x464710) and the carried weapon returns with it.
            _mountedGun = null;
            _host.DropViewTickPair();
        }

        _activeInteraction = null;
        _interactionTick = 0;
        _interactionFacing = -_body.Yaw;
        RestoreBodyAfterInteraction(_body, interaction.Type, terrain);
        _host.MarkPoseChanged();
        _host.SyncView();
    }

    /// <summary>Leaves the body where an ended state put it, or drops it from a wire.</summary>
    internal static void RestoreBodyAfterInteraction(
        PlayerBody body,
        InteractionActivateType type,
        IGroundHeightField? terrain)
    {
        if (type == InteractionActivateType.Wire)
        {
            // Wire cleanup enters the ordinary fall state. Snapping to the terrain here is what made a
            // release/end look like an instant teleport to ground instead of a drop from the cable.
            body.EndLadderTraversal();
        }
        // A fence ends in the fall it has already been handed (PlayerBody.BeginFallFromVault), so it, too, is left
        // where it is.
        // Retail self-driving interaction states leave the root exactly where their initializer put it.
        // Re-running spawn placement here searches up to half a kilometre downward and can move the
        // player onto an unrelated surface beneath the building when Use is released.
    }

    private static bool DoorNeedsPicking(WorldInteraction interaction)
    {
        return interaction.Type == InteractionActivateType.Door && interaction.IsLocked;
    }

    private static InteractionPromptTarget Describe(
        WorldInteraction interaction,
        Vector3d eye,
        float tickBlend,
        float? progress) =>
        new(
            interaction.Task.Id,
            interaction.Type,
            interaction.ActionSprite,
            interaction.MarkerPoint(eye, tickBlend),
            interaction.DurationTicks,
            DoorNeedsPicking(interaction),

            // A door that is not fully closed is offered as one to close, because that is what the
            // toggle this prompt is describing will do to it.
            interaction.Door is { IsFullyClosed: false },
            progress);
}
