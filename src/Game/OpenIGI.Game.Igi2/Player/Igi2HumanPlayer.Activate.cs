using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Player;

/// <content>What the player finds to activate, and activating it.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>Gets what the player's view has found to activate this tick, <c>Human_s + 0x748</c>, or <see langword="null"/>.</summary>
    public IIgi2Activatable? EvaluateTarget { get; private set; }

    /// <summary>Gets what the player is activating, <c>Human_s + 0x74C</c>, or <see langword="null"/>.</summary>
    public IIgi2Activatable? ActivatingTarget { get; private set; }

    /// <summary>Gets a value indicating whether the player is activating something, <c>Human_s + 0x750</c>; it looks for nothing meanwhile.</summary>
    public bool IsActivating { get; private set; }

    /// <summary>Gets or sets a value indicating whether an activation has ended since, <c>isCheckActivateButton</c> (<c>Human_s + 0x751</c>).</summary>
    public bool IsCheckActivateButton { get; set; }

    /// <summary>Gets the ticks the current activation has run, <c>Human_s + 0x754</c>.</summary>
    public int ActivateTime { get; private set; }

    /// <summary>The state animation slot picking a lock plays, <c>0x45</c>.</summary>
    public const int PickLockAnimation = 69;

    /// <summary>How far out from a door's vertex a picker kneels, along the door's Y, <c>3481.6</c> (0.85 m).</summary>
    public const double PickReach = 3481.6;

    /// <summary>The ticks at the start of picking during which letting go of the key does not stop it, <c>nTBATimeout</c>.</summary>
    public const int PickReleaseTicks = 20;

    /// <summary>The sound a lock makes as it gives.</summary>
    public const string PickedSound = "pickloop_end";

    /// <summary>The state animation slot hacking a terminal plays, <c>0x49</c>.</summary>
    public const int HackAnimation = 73;

    /// <summary>How far out from a terminal's vertex a hacker kneels, along its Y, <c>2764.8</c> (0.675 m).</summary>
    public const double HackReach = 2764.8;

    /// <summary>The sound a terminal makes once it is hacked.</summary>
    public const string HackedSound = "typecomp_end";

    // A half turn, the float 0x40490FDB.
    private const float HalfTurn = 3.1415927f;

    private IIgi2SoundInstance? _timedActionSound;

    /// <summary>Gets the ticks a timed action takes, <c>nActivateTime</c> (<c>HumanPlayer_t + 0xC34</c>): a lock's pick time.</summary>
    public int TimedActionTime { get; private set; }

    /// <summary>Gets the ticks a timed action has run, <c>nActivateTimer</c> (<c>HumanPlayer_t + 0xC38</c>).</summary>
    public int TimedActionTimer { get; private set; }

    /// <summary>Gets a value indicating whether the status screen shows a timed action's progress, <c>isProgressBar</c>.</summary>
    public bool IsProgressBar { get; private set; }

    /// <summary>Gets the ticks left before letting go of the key stops a timed action, <c>nTBATimeout</c>.</summary>
    public int TbaTimeout { get; private set; }

    /// <summary>Gets the posture a timed action was started from, which it returns to, <c>tActivateContext.ePrevStance</c>.</summary>
    public Igi2HumanPose PreviousStance { get; private set; }

    /// <summary>
    /// Looks for something to activate, <c>Human_UpdateActivate</c> (<c>igi2 0x500D10</c>), once a tick in the update after the
    /// death check.
    /// </summary>
    /// <remarks>
    /// Standing on the ground and activating nothing, the player offers its view to everything whose sphere meets its own —
    /// the length of its root bone's translation plus its model's radius — and keeps the best that resolves itself
    /// (<see cref="Igi2EvaluateActivate"/>). What it is activating counts the tick.
    /// </remarks>
    public void UpdateActivate()
    {
        EvaluateTarget = null;
        if ((Signal & Igi2PlayerSignal.Grounded) != 0 && !IsActivating)
        {
            Matrix3f v = View.Orientation;
            Igi2EvaluateActivate context = new(
                View.Position,
                [v.M11, v.M12, v.M13, v.M21, v.M22, v.M23, v.M31, v.M32, v.M33],
                isPlayer: true);
            Vector3f r = RootTranslation;
            float radius = (Fpu.Sqrt((((Fpu)r.X * r.X) + ((Fpu)r.Y * r.Y)) + ((Fpu)r.Z * r.Z)) + ModelRadius).F;
            foreach (IIgi2Activatable candidate in Surroundings.FindActivatables(Position, radius))
            {
                candidate.EvaluateActivate(context);
            }

            EvaluateTarget = context.Best;
        }

        if (ActivatingTarget is { } activating)
        {
            activating.Activate(ActivateTime);
            ActivateTime++;
        }
    }

    /// <summary>
    /// The idle state's activate branch, <c>HumanPlayer_RunActionIdle</c> (<c>igi2 0x4B439A</c>…<c>0x4B441D</c>): with something
    /// found and the activate key's channel set, and the found thing first on the action menu, the zoom comes off and the
    /// activation starts.
    /// </summary>
    /// <remarks>
    /// The action menu (<c>ActionMenuScreen_IsFirstActionItem</c>, <c>0x524E60</c>) is the status screen's, not ported yet; the
    /// found thing stands as its first item.
    /// </remarks>
    private void TryActivateFromIdle()
    {
        if (EvaluateTarget is not { } target || Channels[Igi2HumanChannel.Activate] == 0f)
        {
            return;
        }

        View.DisableZoomMode();
        InitActivate(target);
    }

    /// <summary>
    /// Starts activating, <c>HumanPlayer_InitActivate</c> (<c>igi2 0x4A06A0</c>): a switch is pressed at once and the activation
    /// ends there; an unlocked door is opened or closed, on the tick the activate key goes down, and the activation ends. What
    /// else a human activates is not ported; retail leaves an activation of an unknown kind running.
    /// </summary>
    /// <remarks>
    /// A locked door that can be picked starts the player picking it (<see cref="Igi2PlayerState.ActivateDoor"/>); one that
    /// cannot leaves the activation running, as retail does, though the view does not find such a door to begin with.
    /// </remarks>
    private void InitActivate(IIgi2Activatable target)
    {
        ActivatingTarget = EvaluateTarget;
        IsActivating = true;
        ActivateTime = 0;
        switch (target.Kind)
        {
            case Igi2ActivateKind.Switch:
                (ActivatingTarget as IIgi2Pressable)?.Press(0);
                EndActivate();
                break;

            case Igi2ActivateKind.Door when ActivatingTarget is IIgi2Door door:
                if (door.IsLocked)
                {
                    if (door.IsPickable)
                    {
                        Go(Igi2PlayerState.ActivateDoor);
                    }

                    break;
                }

                // Only a fresh press: held from the last tick, the key does nothing more to the door.
                if (Channels.Last[(int)Igi2HumanChannel.Activate] == 0f)
                {
                    door.ToggleOpen(Position);

                    // Human_SendDoorAIEvent (igi2 0x49F6F0) tells the AI a door was used; the AI's events are not ported.
                }

                EndActivate();
                break;

            case Igi2ActivateKind.GenericTba or Igi2ActivateKind.Terminal:
                // Only a fresh press starts the work; held from the last tick, the activation ends there.
                if (Channels.Last[(int)Igi2HumanChannel.Activate] != 0f)
                {
                    EndActivate();
                    break;
                }

                Go(target.Kind == Igi2ActivateKind.Terminal ? Igi2PlayerState.ActivateTerminal : Igi2PlayerState.ActivateGenericTba);
                break;
        }
    }

    // State 38 -- HumanPlayer_InitStateActivateDoor 0x4AD2D0, RunStateActivateDoor 0x4AD620.
    private void EnterActivateDoor()
    {
        if (ActivatingTarget is not IIgi2Door door)
        {
            return;
        }

        PreviousStance = Pose;
        Pose = Igi2HumanPose.Standing;
        CurrentUpperBody = 0;
        RequestedUpperBody = 0;
        Animation.ClearUpperBody(0.4f);
        Animation.PlayState(PickLockAnimation, 0, 0f);
        Animation.StepMain();
        TouchPose();

        // Kneeling at the door's vertex, a metre and a bit out along its Y, on the side the player stands, and facing it.
        float[] m = [.. door.CurrentOrientation];
        Vector3d vertex = door.MagicVertex;
        Fpu dx = (Fpu)Position.X - vertex.X;
        Fpu dy = (Fpu)Position.Y - vertex.Y;
        Fpu dz = (Fpu)Position.Z - vertex.Z;
        if (((((Fpu)m[7] * dz) + ((Fpu)m[4] * dy)) + ((Fpu)m[1] * dx)).Value < 0.0)
        {
            m = Igi2Pose.RotateGamma(m, HalfTurn);
        }

        Fpu x = ((((Fpu)m[2] + m[0]) * 0.0) + ((Fpu)m[1] * PickReach)) + vertex.X;
        Fpu y = ((((Fpu)m[5] + m[3]) * 0.0) + ((Fpu)m[4] * PickReach)) + vertex.Y;
        Fpu z = (((Fpu)m[8] + m[6]) * 0.0) + ((Fpu)m[7] * PickReach);
        PopPosition(new Vector3d(x.Value, y.Value, (z + vertex.Z).Value));
        GroundMovementSpeed = default;
        SnapToGround();
        PopOrientation(((Fpu)Igi2Pose.Gamma(m[0], m[3]) + HalfTurn).F);

        BeginTimedAction(door.PickLockTime);
        IsLockAction = true;

        // Its sound plays only while the door stands still, open or shut.
        if (door.Status is 0 or 1)
        {
            _timedActionSound?.Delete();
            _timedActionSound = Surroundings.StartSound(door.ActivateSound);
        }

        AimPrecision(Igi2PrecisionType.Standing);
        TbaTimeout = PickReleaseTicks;
        Locomotion.Current = (_, _) => RunActivateDoor(door);
    }

    private void RunActivateDoor(IIgi2Door door)
    {
        MoveOnGround(0f, GroundAcceleration, 0f, GroundAcceleration);
        if (StepTimedAction() is not { } finished)
        {
            return;
        }

        if (finished)
        {
            door.IsPicked = true;
            door.ToggleOpen(Position);

            // Human_SendDoorAIEvent (igi2 0x49F6F0) tells the AI a door was used; the AI's events are not ported.
            Surroundings.StartSound(PickedSound);
        }

        EndTimedAction();
    }

    // State 42 -- HumanPlayer_InitStateActivateGenericTBA 0x4AE910, RunStateActivateGenericTBA 0x4AFD40. The door's kneel
    // without its turn to the near side, from the second vertex, playing the action's own clip by name.
    private void EnterActivateGenericTba()
    {
        if (ActivatingTarget is IIgi2TimedAction action)
        {
            BeginWork(action, PickReach, () => Animation.PlayNamed(action.ActivateAnimation, 0, 0f), finishedSound: null);
        }
    }

    // State 40 -- HumanPlayer_InitStateActivateTerminal 0x4ADF00, RunStateActivateTerminal 0x4AE210: a timed action of its
    // own clip and reach, which sounds once the hack is done.
    private void EnterActivateTerminal()
    {
        if (ActivatingTarget is IIgi2WorkedActivatable terminal)
        {
            BeginWork(terminal, HackReach, () => Animation.PlayState(HackAnimation, 0, 0f), HackedSound);
        }
    }

    // What the timed action's and the terminal's states share (HumanPlayer_InitStateActivateGenericTBA 0x4AE910 and
    // InitStateActivateTerminal 0x4ADF00): standing, the upper body cleared and the aim settled; the clip begun; kneeling the
    // reach out along its Y from its vertex, facing it; and the timer started on its time, with its sound.
    private void BeginWork(IIgi2WorkedActivatable action, double reach, Action playClip, string? finishedSound)
    {
        PreviousStance = Pose;
        Pose = Igi2HumanPose.Standing;
        CurrentUpperBody = 0;
        RequestedUpperBody = 0;
        Animation.ClearUpperBody(0.4f);
        AimPrecision(Igi2PrecisionType.Standing);
        playClip();
        Animation.StepMain();
        TouchPose();

        IReadOnlyList<float> m = action.CurrentOrientation;
        Vector3d vertex = action.MagicVertex;
        Fpu x = ((((Fpu)m[2] + m[0]) * 0.0) + ((Fpu)m[1] * reach)) + vertex.X;
        Fpu y = ((((Fpu)m[5] + m[3]) * 0.0) + ((Fpu)m[4] * reach)) + vertex.Y;
        Fpu z = (((Fpu)m[8] + m[6]) * 0.0) + ((Fpu)m[7] * reach);
        PopPosition(new Vector3d(x.Value, y.Value, (vertex.Z + z).Value));
        GroundMovementSpeed = default;
        SnapToGround();

        // Off whatever it stood on, so nothing it kneels on carries it.
        DropGround();
        PopOrientation(((Fpu)Igi2Pose.Gamma(m[0], m[3]) + HalfTurn).F);

        IsLockAction = true;
        BeginTimedAction(action.ActivateTime);
        _timedActionSound?.Delete();
        _timedActionSound = Surroundings.StartSound(action.ActivateSound);
        TbaTimeout = PickReleaseTicks;
        Locomotion.Current = (_, _) => RunWork(action, finishedSound);
    }

    // HumanPlayer_RunStateActivateGenericTBA 0x4AFD40 and RunStateActivateTerminal 0x4AE210.
    private void RunWork(IIgi2WorkedActivatable action, string? finishedSound)
    {
        MoveOnGround(0f, GroundAcceleration, 0f, GroundAcceleration);

        // ActionMenuScreen_ptQTask's selection is held on the action (the action menu is not ported).
        if (StepTimedAction() is not { } finished)
        {
            return;
        }

        if (finished)
        {
            action.Finish();
            if (finishedSound is not null)
            {
                Surroundings.StartSound(finishedSound);
            }
        }

        EndTimedAction();
    }

    // The timer and view the door's and the action's states share: so many ticks a second of work, the progress bar on, the
    // view held still looking ahead and the orientation locked.
    private void BeginTimedAction(float seconds)
    {
        TimedActionTime = Fpu.ToInt32((Fpu)seconds * Igi2DoorTask.TicksPerSecond);
        TimedActionTimer = 0;
        CameraMode = 3;
        IsProgressBar = true;
        IsDisableDeltaTranslationScale = true;
        View.LookAhead();
        View.IsLockOrientation = true;
        IsLockOrientation = true;
    }

    // A tick of the timer: null while it runs on, true on the tick it fills with the key still held, false once let go -- after
    // the first two thirds of a second -- or hit, or dead.
    private bool? StepTimedAction()
    {
        bool stop = false;
        if (TbaTimeout != 0)
        {
            TbaTimeout--;
        }
        else
        {
            stop = Channels[Igi2HumanChannel.Activate] == 0f;
        }

        stop |= Has(Igi2PlayerSignal.Dead) || Has(Igi2PlayerSignal.Hit);
        int time = TimedActionTime;
        if (TimedActionTimer < time)
        {
            TimedActionTimer++;
        }

        if (TimedActionTimer == time && !stop)
        {
            return true;
        }

        return stop ? false : null;
    }

    // The end of a timed action: everything unlocked, the activation over, and back to the stance it began from, or the flinch.
    private void EndTimedAction()
    {
        View.IsLockOrientation = false;
        IsLockOrientation = false;
        IsActivating = false;
        IsCheckActivateButton = true;
        IsLockAction = false;
        ActivatingTarget = null;
        CameraMode = 1;
        IsDisableDeltaTranslationScale = false;
        IsProgressBar = false;
        _timedActionSound?.Delete();
        _timedActionSound = null;

        if (Has(Igi2PlayerSignal.Hit))
        {
            Go(Igi2PlayerState.Hit);
        }
        else
        {
            Go(PreviousStance switch
            {
                Igi2HumanPose.Crouching => Igi2PlayerState.Crouch,
                Igi2HumanPose.LyingFlat => Igi2PlayerState.Flat,
                _ => Igi2PlayerState.Stand,
            });
        }
    }

    /// <summary>The activation's end: the player looks again, <c>Human_UnlockActivate</c> (<c>igi2 0x502770</c>).</summary>
    private void EndActivate()
    {
        IsActivating = false;
        IsCheckActivateButton = true;
        ActivatingTarget = null;
    }
}
