using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Searching a cabinet.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>The state clip a search starts with.</summary>
    public const int SearchStartClip = 70;

    /// <summary>The state clip a search loops on.</summary>
    public const int SearchLoopClip = 71;

    /// <summary>The state clip a search ends with.</summary>
    public const int SearchEndClip = 72;

    /// <summary>How far before a cabinet's search vertex the player kneels, 0.85 m.</summary>
    public const double SearchReach = 3481.6;

    /// <summary>The ticks a search can be let go of without ending it, at its start.</summary>
    public const int SearchReleaseTicks = 20;

    /// <summary>Gets the phase of the search clip, <c>nSearchCabinetState</c>: starting, looping, ending.</summary>
    public int SearchPhase { get; private set; }

    // State 39 -- HumanPlayer_InitStateActivateCabinet: standing at 0.85 m before the cabinet's search vertex, facing it, the start
    // clip begun and stepped, tba_searchcab playing, the timer set to its search time -- and, as in the original, not reset, so it
    // runs on from wherever an earlier timed action left it -- and the view held still.
    private void EnterActivateCabinet()
    {
        if (ActivatingTarget is not Igi2CabinetTask cabinet)
        {
            return;
        }

        StanceBeforeClimb = Pose;
        Pose = Igi2HumanPose.Standing;
        CurrentUpperBody = 0;
        RequestedUpperBody = 0;
        Animation.ClearUpperBody(0.4f);
        Animation.PlayState(SearchStartClip, 0, 0f);
        SearchPhase = 0;
        Animation.StepMain();

        IReadOnlyList<float> m = cabinet.Frame;
        Vector3d vertex = cabinet.SearchVertex;
        PopPosition(new Vector3d(
            ((((Fpu)m[2] + m[0]) * 0.0) + ((Fpu)m[1] * SearchReach) + vertex.X).Value,
            ((((Fpu)m[5] + m[3]) * 0.0) + ((Fpu)m[4] * SearchReach) + vertex.Y).Value,
            ((((Fpu)m[8] + m[6]) * 0.0) + ((Fpu)m[7] * SearchReach) + vertex.Z).Value));
        GroundMovementSpeed = default;
        SnapToGround();
        PopOrientation(((Fpu)Igi2Pose.Angles(m).Gamma + HalfTurn).F);
        IsLockAction = true;
        _timedActionSound?.Delete();
        _timedActionSound = Surroundings.StartSound("tba_searchcab");
        TimedActionTime = Fpu.ToInt32((Fpu)cabinet.SearchTime * 30.0);
        CameraMode = 3;
        IsProgressBar = true;
        IsDisableDeltaTranslationScale = true;
        View.LookAhead();
        View.IsLockOrientation = true;
        IsLockOrientation = true;
        AimPrecision(Igi2PrecisionType.Standing);
        TbaTimeout = SearchReleaseTicks;
        Locomotion.Current = (_, _) => RunActivateCabinet(cabinet);
    }

    // HumanPlayer_RunStateActivateCabinet: the start clip gives way to the loop, and the loop to the end clip as the time left comes to
    // the end clip's length; the drawer follows the clip's triggers; letting go after the first twenty ticks, a hit or death cuts the
    // search short and shuts the drawer; held to its end, the cabinet is searched. Either way the player stands up again, or is hit.
    private void RunActivateCabinet(Igi2CabinetTask cabinet)
    {
        bool isCut = false;
        MoveOnGround(0f, GroundAcceleration, 0f, GroundAcceleration);
        ActionMenu.SetNoDrawTimeout(ActivationMenuHide);
        if (SearchPhase == 1)
        {
            int endTicks = 30 * (Animation.StateTotalTime(SearchEndClip) / 4800);
            if (TimedActionTimer > TimedActionTime - endTicks)
            {
                Animation.PlayState(SearchEndClip, 0, 0.2f);
                SearchPhase = 2;
            }
        }
        else if (SearchPhase == 0 && Has(Igi2PlayerSignal.AnimationEnded))
        {
            Animation.PlayState(SearchLoopClip, 0, 0f);
            SearchPhase = 1;
        }

        if (TbaTimeout != 0)
        {
            TbaTimeout--;
        }
        else if (Channels[Igi2HumanChannel.Activate] == 0f)
        {
            isCut = true;
        }

        Igi2PlayerSignal signal = Signal;
        isCut |= (signal & (Igi2PlayerSignal.Dead | Igi2PlayerSignal.Hit)) != 0;
        if (TimedActionTimer < TimedActionTime)
        {
            TimedActionTimer++;
        }

        if ((signal & Igi2PlayerSignal.OpenDrawer) != 0)
        {
            Signal &= ~Igi2PlayerSignal.OpenDrawer;
            cabinet.OpenDrawer();
        }

        if ((signal & Igi2PlayerSignal.CloseDrawer) != 0)
        {
            Signal &= ~Igi2PlayerSignal.CloseDrawer;
            cabinet.CloseDrawer();
        }

        if (TimedActionTimer == TimedActionTime && !isCut)
        {
            cabinet.Searched();
        }
        else if (isCut)
        {
            cabinet.SnapDrawerClosed();
            _timedActionSound?.Delete();
            _timedActionSound = null;
        }
        else
        {
            return;
        }

        IsLockAction = false;
        View.IsLockOrientation = false;
        IsLockOrientation = false;
        EndActivate();
        CameraMode = 1;
        IsProgressBar = false;
        IsDisableDeltaTranslationScale = false;
        if ((signal & Igi2PlayerSignal.Hit) != 0)
        {
            Go(Igi2PlayerState.Hit);
        }
        else
        {
            GoBackToStance();
        }
    }
}
