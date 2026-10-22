using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// The clip a soldier's body plays this tick, and the step it then takes by it: <c>0x45D080</c>'s machine, then its
/// turn and its move.
/// </summary>
/// <remarks>
/// A patrol ANIMATION command names a clip outright; a soldier in a fight plays its WEAPON's own clips; otherwise the
/// gait picks between the walk cycle and the soldier's stand animation.
/// </remarks>
internal static class SoldierClipSelection
{
    /// <summary>Chooses and plays the clip a soldier's body is in this tick.</summary>
    /// <param name="view">The soldier's rig.</param>
    /// <param name="brain">Its brain, which has ticked.</param>
    /// <param name="combatState">The body state that owns the clip in a fight, or <see langword="null"/>.</param>
    /// <param name="combatClipMissing">The soldiers whose combat clip could not be played, kept up to date here.</param>
    public static void Play(SoldierView view, Igi1HumanAi brain, int? combatState, HashSet<SoldierView> combatClipMissing)
    {
        // A patrol ANIMATION command names a clip outright; a soldier in a fight plays its WEAPON's
        // own clips; otherwise the gait picks between the walk cycle and the soldier's stand
        // animation.
        HumanSoldierClass soldierClass = brain.SoldierClass;

        // A PATROL'S GAITS ARE THE SOLDIER'S OWN FORWARD STATES, picked by the byte at Human+3328. The soldier's
        // create (0x45CC50) sets it to 1 and only a press of input slot 20 (+512) flips it (0x45D080). The AI's
        // channel 0 is that slot, and the walk and run setters every walker calls each tick (0x4525E0, 0x452660)
        // press it exactly when the byte is wrong for the gait: so 1 is WALKING. The machine reads it as signal
        // 0x40000 (0x45CCF0) and a plain forward goes to state 3 with it and state 2 without (0x457A00) -- clip 57
        // walking, clip 3 running, on the guard's table. Mission 12's patrols walk in state 3 on the probe; the
        // port had walked them in the player's walk clip and run them in the combat run (state 17), whose strides
        // carried them half a metre off the original's path. Classes with machines of their own keep theirs.
        bool soldierGaits = brain.Body is HumanSoldierBodyMachine;
        int walkAnimation = soldierGaits
            ? brain.Body.StateTable.AnimationOf(HumanSoldierStates.FastForwardState)
            : soldierClass.WalkAnimation;
        int runAnimation = brain.Body.StateTable.AnimationOf(soldierClass.RunState);
        int patrolRunAnimation = soldierGaits
            ? brain.Body.StateTable.AnimationOf(HumanSoldierStates.ForwardState)
            : runAnimation;
        if (brain.Body.IsFlinching)
        {
            // A flinch owns the body over any other clip: every per-tick leaves for it on the pulse.
            if (!view.PlayStateAnimation(brain.Body.CurrentAnimation, soldierClass.States.Loops(brain.Body.State)))
            {
                combatClipMissing.Add(view);
            }
        }
        else if (brain.RequestedAnimation >= 0)
        {
            view.PlayStateAnimation(brain.RequestedAnimation, loops: false);
        }
        else if (AiCombatChannels.IsInStance(brain.Type, AiStanceChannel.AnimationPlaying, brain.CombatState)
            && brain.Body.CurrentAnimation >= 0)
        {
            // The play-animation state (35 for most types) a patrol's animation command put the body in: its clip, once.
            view.PlayStateAnimation(brain.Body.CurrentAnimation, loops: false);
        }
        else if ((brain.IsMovingInCombat || (brain.Activity == AiSoldierActivity.Panicking && brain.WalksForward))
            && (!soldierGaits || brain.CombatState is HumanSoldierStates.RunState or HumanSoldierStates.RunFiringState
                or HumanSoldierStates.WalkState or HumanSoldierStates.ForwardState or HumanSoldierStates.FastForwardState))
        {
            // A soldier body runs once its machine is in the run: on the tick a flinch or a reload hands it back to
            // standing it stands, and the run starts the tick after.
            // The soldier is travelling a combat route this tick -- advancing, repositioning or hunting a
            // lost target while it watches. A chasing guard RUNS (retail's Combat-Follow runs in state 17
            // = anim 48 at ~4.4 m/s), and the run clip's own root motion is what carries it at that speed
            // (Igi1HumanAi.AdvanceInCombat reads it). Fall back to the walk if the set has no run clip.
            //
            // This test MUST come before the combat-state branch: an engaged hunter otherwise plays
            // the fire pose, whose zero root motion feeds AdvanceInCombat a zero step -- the exact
            // "engaging, never moving" stall. Retail's Follow is a full-body run state (17), not the
            // stand cycle with feet glued down.
            //
            // A soldier body firing on the run is in state 18, whose entry (0x45AAD0) plays the table's clip 52 --
            // the run with the gun up and levelled -- and whose own root motion carries it. Clip 48 is the tense
            // run with the muzzle down; playing it through the burst pointed every running shot at the ground.
            int movingAnimation = soldierGaits && brain.CombatState == HumanSoldierStates.RunFiringState
                && brain.Body.CurrentAnimation >= 0
                ? brain.Body.CurrentAnimation
                : runAnimation;
            if (movingAnimation < 0 || !view.SetMoving(true, movingAnimation))
            {
                view.SetMoving(true, walkAnimation);
            }
        }
        else if (brain.IsMounted)
        {
            // HumanSoldier state 36 is the attached emplacement pose. It keeps ownership while
            // the StationaryGun task reads slot 6 and fires; it does not enter the ordinary
            // 0/12/10/6 carried-weapon body cycle. It is the body's, not the brain's: a gunner the
            // Activate action put on the gun stays on it between fights.
            int mountedState = AiCombatChannels.StateOf(brain.Type, AiStanceChannel.Mounted);
            int animation = brain.Body.StateTable.AnimationOf(mountedState);
            if (animation < 0
                || !view.PlayStateAnimation(animation, soldierClass.States.Loops(mountedState)))
            {
                view.SetMoving(false, walkAnimation);
            }
        }
        else if (combatState is { } combatBodyState)
        {
            int anim = brain.Body.CurrentAnimation;
            bool placed = anim >= 0
                && view.PlayStateAnimation(anim, soldierClass.States.Loops(combatBodyState));
            if (placed)
            {
                combatClipMissing.Remove(view);
            }
            else
            {
                view.SetMoving(false, walkAnimation);
                combatClipMissing.Add(view);
            }
        }
        else if (brain.Activity == AiSoldierActivity.FallenFlat)
        {
            int state = AiCombatChannels.StateOf(brain.Type, AiStanceChannel.Prone);
            int animation = brain.Body.StateTable.AnimationOf(state);
            if (animation < 0
                || !view.PlayStateAnimation(animation, soldierClass.States.Loops(state)))
            {
                view.SetMoving(false, walkAnimation);
            }
        }
        else if (brain.IsCrouching)
        {
            // A patrol's Crouch command holds the kneel channel every tick (0x48E920): the body goes
            // down (state 13) and holds the kneel (19) until a walk releases it.
            int state = brain.CombatState;
            int animation = brain.Body.CurrentAnimation;
            if (animation < 0 || !view.PlayStateAnimation(animation, brain.Body.StateTable.Loops(state)))
            {
                view.SetMoving(false, walkAnimation);
            }
        }
        else if (soldierGaits && brain.CombatState is HumanSoldierStates.WalkState
            or HumanSoldierStates.ForwardState or HumanSoldierStates.FastForwardState)
        {
            // The patrol's gait is the body's forward state, whose own clip it plays: the walk (3), the run (2) or the
            // slow walk (1). The body enters it on the tick the walker first writes forward, and not before it has
            // stood up from whatever it was doing -- a reload that has just ended leaves it standing for that tick.
            int gait = brain.Body.CurrentAnimation;
            if (gait < 0 || !view.SetMoving(true, gait))
            {
                view.SetMoving(true, walkAnimation);
            }
        }
        else if (!soldierGaits && brain.WalksForward && !brain.WalkGait)
        {
            // The patrol cursor's gait flag (+21) is applied EVERY tick by the retail walker
            // (0x48E920): a RunTo leg installs the run gait and it stays until the next movement
            // command changes it. The alarm-response routes (level 1's 2600/2605-2616) are authored
            // as RunTo, so an alarmed guard RUNS between its posts -- playing the walk clip here is
            // what made them amble out of the barracks at 1.28 m/s. The run gait is the soldier's
            // state 2 (see above), and its clip's own root motion carries the brain at run speed;
            // sets without one fall back to the walk.
            if (patrolRunAnimation < 0 || !view.SetMoving(true, patrolRunAnimation))
            {
                view.SetMoving(true, walkAnimation);
            }
        }
        else
        {
            // The body walks while the walker writes forward, and stands while it holds -- at a door,
            // on arrival, or turning on the spot.
            view.SetMoving(!soldierGaits && brain.WalksForward, walkAnimation);
        }
    }

    /// <summary>Turns and moves a soldier's body by the clip this tick chose.</summary>
    /// <param name="view">The soldier's rig.</param>
    /// <param name="brain">Its brain, which steps the body.</param>
    /// <param name="clipBefore">The clip that played before this tick's choice.</param>
    /// <param name="animationBefore">That clip's animation.</param>
    /// <param name="travel">The clip's forward delta this tick.</param>
    /// <param name="lateral">Its sideways delta.</param>
    public static void StepBody(
        SoldierView view,
        Igi1HumanAi brain,
        BoneClip? clipBefore,
        int animationBefore,
        double travel,
        double lateral)
    {
        // The body turns and moves now, after the clip this tick chose (0x45D080: machine, then turn, then move), by
        // that clip's delta: the advance of a clip that played on; nothing for one started this tick -- its first tick's
        // delta is dropped -- except the jump a gait entry makes when it carries the phase across (0x4D65B0). On the
        // probe mission 4's patrol 420 stood still on the tick it reached node 117, where taking the walk's advance first
        // carried it a step past; and squad member 400 moved 68 units on its walk-to-run tick, root(30.13) - root(0).
        BoneClip? clipAfter = view.Bones.SourceOf(0);
        double stepForward = travel;
        double stepLateral = lateral;
        if (clipAfter is not null && !ReferenceEquals(clipAfter, clipBefore))
        {
            Vector3f jump = default;
            if (clipBefore is not null && CarriesGaitPhase(brain, animationBefore, view.CurrentAnimation))
            {
                // 0x45ACA0/0x4581E0 (state 2) and 0x458250 (state 3): the new clip starts at the old one's phase, read
                // before this tick's advance on the original's clock -- where this clock already is after its advance.
                float phase = clipBefore.Time / clipBefore.Animation.Duration;
                clipAfter.SetTime(clipAfter.Animation.Duration * phase);
                Vector3f start = clipAfter.SampleRoot();
                Vector3f zero = clipAfter.SampleRoot(0f);
                jump = new Vector3f(start.X - zero.X, start.Y - zero.Y, start.Z - zero.Z);
            }

            stepForward = jump.Y;
            stepLateral = jump.X;
        }

        brain.StepBodyAfterClip(stepForward, stepLateral);
    }

    /// <summary>Whether a gait entry carries the clip phase across: state 2 from 1, 3 or 17, state 3 from 1 or 2.</summary>
    /// <param name="brain">The soldier.</param>
    /// <param name="fromAnimation">The clip it played before this tick's entry.</param>
    /// <param name="toAnimation">The clip the entry started.</param>
    /// <returns><see langword="true"/> for the gait pairs <c>0x45ACA0</c>, <c>0x4581E0</c> and <c>0x458250</c> carry.</returns>
    private static bool CarriesGaitPhase(Igi1HumanAi brain, int fromAnimation, int toAnimation)
    {
        if (brain.Body is not HumanSoldierBodyMachine)
        {
            return false;
        }

        HumanBodyStateTable table = brain.Body.StateTable;
        bool Plays(int animation, int state) => table.AnimationOf(state) == animation;
        bool fromWalk = Plays(fromAnimation, HumanSoldierStates.WalkState);
        bool fromForward = Plays(fromAnimation, HumanSoldierStates.ForwardState);
        bool fromFast = Plays(fromAnimation, HumanSoldierStates.FastForwardState);
        bool fromRun = Plays(fromAnimation, HumanSoldierStates.RunState);
        return (Plays(toAnimation, HumanSoldierStates.ForwardState) && (fromWalk || fromFast || fromRun))
            || (Plays(toAnimation, HumanSoldierStates.FastForwardState) && (fromWalk || fromForward));
    }
}
