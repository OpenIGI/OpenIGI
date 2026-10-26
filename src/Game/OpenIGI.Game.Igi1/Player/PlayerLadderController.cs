using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// The player's ladders: which one is in reach, mounting it, and the retail climb states 37 to 40
/// that move the body rung by rung, over the top and down the slide.
/// </summary>
/// <remarks>
/// The climb itself is <see cref="HumanLadderClimb"/>'s; this carries the body along with it, because on
/// a ladder the ordinary walk, gravity and collision do not run.
/// </remarks>
internal sealed class PlayerLadderController
{
    /// <summary>How far up a ladder a climber takes hold — chest height, in world units.</summary>
    private const double LadderGrabHeightUnits = 1.25 * WorldScene.UnitsPerMetre;

    private readonly PlayerBody _body;
    private readonly HumanLadderClimb _climb = new();
    private IReadOnlyList<LadderPlacement> _ladders = [];
    private LadderPlacement? _nearLadder;
    private bool _nearLadderAtTop;
    private double _nearLadderAngle = double.MaxValue;
    private int _nearLadderIndex = -1;

    /// <summary>Initializes the controller for one body.</summary>
    /// <param name="body">The body the climb carries.</param>
    internal PlayerLadderController(PlayerBody body)
    {
        _body = body;
    }

    /// <summary>Gets the ladder in reach, or <see langword="null"/>.</summary>
    internal LadderPlacement? Near => _nearLadder;

    /// <summary>Gets how far off the aim the ladder in reach is, for the arbitration with the interactions.</summary>
    internal double NearAngle => _nearLadderAngle;

    /// <summary>Gets whether the player is on a ladder.</summary>
    internal bool IsOnLadder => _climb.IsOnLadder;

    /// <summary>Gets the yaw the climbing body faces — into the ladder, in radians.</summary>
    internal float Facing => _climb.Facing;

    /// <summary>Gets the active retail ladder locomotion phase.</summary>
    internal LadderTraversalPhase Phase => _climb.Phase;

    /// <summary>Gets animation 168's current playback direction.</summary>
    internal int Direction => _climb.Direction;

    /// <summary>Gets the climb animation — state 37's binding, animation 168 in the player's set.</summary>
    internal static int ClimbAnimation => HumanLocomotionStates.For(37).AnimationId;

    /// <summary>Gets state 40's slide animation, 169 in the retail player set.</summary>
    internal static int SlideAnimation => HumanLocomotionStates.For(40).AnimationId;

    /// <summary>Gets states 38/39's shared top transition animation, 170 in the retail player set.</summary>
    internal static int TopAnimation => HumanLocomotionStates.For(38).AnimationId;

    /// <summary>Gets whether a ladder is in reach and its retail action icon should be shown.</summary>
    internal bool ShowAction => _nearLadder is not null && !_climb.IsOnLadder;

    /// <summary>Makes the level's ladders climbable.</summary>
    /// <param name="ladders">The ladders the scene collected, in world units.</param>
    internal void Use(IReadOnlyList<LadderPlacement> ladders) => _ladders = ladders;

    /// <summary>Drops the ladder in reach, because an interaction is the more head-on offer.</summary>
    internal void ForgetNear() => _nearLadder = null;

    /// <summary>The remaster's marker for the ladder in reach, or <see langword="null"/> when none is offered.</summary>
    internal InteractionPromptTarget? Prompt() =>
        ShowAction && _nearLadder is { } ladder
            ? DescribeLadder(ladder, _nearLadderAtTop, _nearLadderIndex)
            : null;

    // The nearest ladder that is close enough, faced and within height to mount.
    internal void UpdateNear()
    {
        _nearLadder = null;
        _nearLadderAtTop = false;
        _nearLadderAngle = double.MaxValue;
        _nearLadderIndex = -1;
        double best = double.MaxValue;

        for (int index = 0; index < _ladders.Count; index++)
        {
            LadderPlacement ladder = _ladders[index];

            // Human_UpdateActivate evaluates tasks from the HumanView position, not the task's feet.
            // Using the feet makes the strict ladder-origin height test fail until the player jumps.
            if (!ladder.CanActivate(_body.Eye, _body.Yaw, out bool atTop, out double angle))
            {
                continue;
            }

            // The shared resolver keeps the most head-on task, not the nearest one.
            if (angle < best)
            {
                best = angle;
                _nearLadder = ladder;
                _nearLadderAtTop = atTop;
                _nearLadderAngle = angle;

                // Kept so the marker can tell one ladder from another: a level declares no task id for
                // them, and the prompt has to know when the thing under the player's aim has changed.
                _nearLadderIndex = index;
            }
        }
    }

    // Puts the player on the ladder in reach: face it and snap onto it.
    internal void Mount()
    {
        LadderPlacement ladder = _nearLadder!;

        // sub_4113E0 takes the task's gamma, adds PI and commits both exact column-based mount offsets.
        _body.BeginLadderTraversal();
        _climb.Mount(ladder, _nearLadderAtTop);
        _body.Yaw = ladder.PlayerYaw;
        _body.Position = _climb.Position;
        _nearLadder = null;
        _nearLadderIndex = -1;
    }

    /// <summary>Decides the next rung at a boundary — begins a rung, hands off, or holds (state 37).</summary>
    /// <param name="up">Whether up (W) is held.</param>
    /// <param name="down">Whether down (S) is held.</param>
    /// <param name="slide">Whether slide (use) is held.</param>
    /// <returns>The decision; a stepping result means the host should start the rung's clip.</returns>
    /// <remarks>
    /// Up/down only act at a rung boundary. Use is checked independently and may enter the retail slide
    /// state during a rung (<c>sub_40D6B0</c>).
    /// </remarks>
    internal LadderStepResult DecideStep(bool up, bool down, bool slide)
    {
        LadderStepResult result = _climb.Decide(up, down, slide);
        if (result == LadderStepResult.Dismounted)
        {
            LeaveLadder();
        }
        else
        {
            _body.Position = _climb.Position;
        }

        return result;
    }

    /// <summary>Moves the climbing body by the climb clip's root-motion delta for this tick.</summary>
    /// <param name="rootDelta">The clip's root motion, rotated into the facing frame, in world units.</param>
    internal void Move(Vector3d rootDelta)
    {
        _climb.Move(rootDelta);
        _body.Position = _climb.Position;
    }

    /// <summary>Moves the body by the frame animation 168 runs on entering state 37.</summary>
    /// <param name="rootDelta">That frame's root motion, rotated into the facing frame, in world units.</param>
    internal void MoveEntryFrame(Vector3d rootDelta)
    {
        _climb.MoveEntryFrame(rootDelta);
        _body.Position = _climb.Position;
    }

    /// <summary>Commits the rung whose clip has just completed, returning the climb to a boundary.</summary>
    internal void CompleteStep()
    {
        _climb.CompleteStep();
        _body.Position = _climb.Position;
    }

    /// <summary>Takes a crossing of rung event 8 unless it repeats the event time the climb last took.</summary>
    /// <param name="time">The crossed event's authored time.</param>
    /// <returns><see langword="true"/> when the crossing counts; see <see cref="HumanLadderClimb.AcceptRungEvent"/>.</returns>
    internal bool AcceptRungEvent(float time) => _climb.AcceptRungEvent(time);

    /// <summary>Marks a crossing of rung event 8 in states 37, 38 or 39: the AI hears it and a rung sounds.</summary>
    /// <param name="noise">Where the AI's ladder noise is offered, or <see langword="null"/>.</param>
    /// <returns>The rung sound's definition name, to be played at <see cref="PlayerCamera.LadderFeet"/>.</returns>
    /// <remarks>
    /// The three states run the same block (<c>0x40D6B0</c>, <c>0x40D920</c>, <c>0x40DA80</c>), so the
    /// top transitions in animation 170 make both the noise and the sound too, not only a climbed rung.
    /// </remarks>
    internal string TakeStep(Action<AiEvent>? noise)
    {
        noise?.Invoke(AiEvent.Ladder);
        return _climb.NextStepSound();
    }

    /// <summary>Consumes animation 170's endpoint, and lets go of the ladder when state 39 detaches.</summary>
    internal void CompleteTopTransition()
    {
        LadderTraversalPhase previous = _climb.Phase;
        if (!_climb.CompleteTopTransition())
        {
            return;
        }

        _body.Position = _climb.Position;
        if (previous == LadderTraversalPhase.GettingOffTop)
        {
            LeaveLadder();
        }
    }

    // Off the ladder at either end. 0x4113E0 only turns collision back on: the Human's origin stays where the climb left it,
    // a little above the ground or the roof, so the body is in the air and falls the last few centimetres into state 29,
    // landing with the surface's step (0x40BE40). The climb carries the origin; the body stands on its feet,
    // StandingOriginHeight below. Snapping the origin to the first surface under it instead stood a player who had
    // climbed to a roof on the ladder's own top, short of the roof and without the landing.
    private void LeaveLadder()
    {
        Vector3d origin = _climb.Position;
        _body.Position = new Vector3d(origin.X, origin.Y, origin.Z - PlayerBody.StandingOriginHeight);
        _body.EndLadderTraversal();
    }

    /// <summary>Advances state 40 and detaches when the normal mover reports ground contact.</summary>
    /// <param name="rootDelta">Animation 169's world-space root delta.</param>
    /// <param name="terrain">The level terrain.</param>
    /// <returns><see langword="true"/> when the slide ended this tick.</returns>
    internal bool TickSlide(Vector3d rootDelta, IGroundHeightField? terrain)
    {
        if (_climb.Phase != LadderTraversalPhase.SlidingDown)
        {
            return false;
        }

        bool grounded = _body.TickLadderSlide(terrain, rootDelta);
        _climb.UpdateSlidePosition(_body.Position);
        if (!grounded)
        {
            return false;
        }

        _climb.CompleteSlide();
        _body.EndLadderTraversal();
        return true;
    }

    private static InteractionPromptTarget DescribeLadder(LadderPlacement ladder, bool atTop, int index) =>
        new(
            // Negative, so a ladder's identity can never collide with a declared task's id.
            -(index + 1),
            InteractionActivateType.Ladder,
            StatusScreenLayout.LadderActionSprite,
            LadderMarker(ladder, atTop),
            DurationTicks: 0,
            Locked: false,
            DoorOpen: false,
            Progress: null);

    /// <summary>The rung a ladder's marker sits on: the one the player is about to take hold of.</summary>
    /// <remarks>
    /// Fixed to the end being mounted rather than tracking the player up and down the stile. A ladder
    /// is metres long and a marker in the middle of one marks nothing, but a marker that slides as the
    /// player walks belongs to the player rather than to the ladder.
    /// </remarks>
    private static Vector3d LadderMarker(LadderPlacement ladder, bool atTop)
    {
        if (atTop)
        {
            return ladder.Top;
        }

        Vector3d climb = ladder.Top - ladder.Bottom;
        double length = climb.Length;
        return length <= 0.0
            ? ladder.Bottom
            : ladder.Bottom + (climb / length * Math.Min(length, LadderGrabHeightUnits));
    }
}
