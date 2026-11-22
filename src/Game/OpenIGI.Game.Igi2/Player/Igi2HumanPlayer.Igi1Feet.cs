using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Project I.G.I.'s movement under the player (<see cref="Igi2PlayerMovement.Igi1"/>).</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>Gets or sets whose movement the player has this tick.</summary>
    /// <remarks>Set before each tick from the setting, or from a co-op client's command on the server.</remarks>
    public Igi2PlayerMovement Movement { get; set; }

    /// <summary>Gets or sets Project I.G.I.'s body, which moves the player on foot when <see cref="Movement"/> asks for it.</summary>
    /// <remarks><see langword="null"/> where the level gave the player none to stand on: it then moves as I.G.I. 2's own always.</remarks>
    public Igi1FootMotion? Igi1Feet { get; set; }

    /// <summary>
    /// Gets a value indicating whether Project I.G.I.'s body moves the player now: it has one and asks for it, and it is on its
    /// feet in the world -- standing, turning, crouched, walking, running, jumping, falling or taking a hit.
    /// </summary>
    /// <remarks>
    /// Everything else is I.G.I. 2's own and moves as it does: a ladder, a wire, a fence, the water, lying and crawling, the peek,
    /// the things worked by hand, a gun manned, a silent kill, death, and anything carrying the player.
    /// </remarks>
    public bool StandsOnIgi1Feet =>
        Movement == Igi2PlayerMovement.Igi1
        && Igi1Feet is not null
        && AttachParent is null
        && !IsDead
        && IsOnFoot(Locomotion.CurrentState);

    // Whether the jump state was entered because Project I.G.I.'s body took off, rather than by I.G.I. 2's jump key.
    private bool TookOffOnIgi1Feet => StandsOnIgi1Feet && Igi1Feet!.TookOff;

    private static bool IsOnFoot(int state) => state is Igi2PlayerState.Stand or Igi2PlayerState.StandTurn
        or (>= Igi2PlayerState.RunForward and <= Igi2PlayerState.WalkLeft)
        or Igi2PlayerState.Crouch or Igi2PlayerState.CrouchTurn
        or (>= Igi2PlayerState.CrouchWalkForward and <= Igi2PlayerState.CrouchWalkLeft)
        or Igi2PlayerState.Jump or Igi2PlayerState.Fall or Igi2PlayerState.Hit;

    /// <summary>
    /// The move on Project I.G.I.'s body: carried by the platform as I.G.I. 2 carries the player, then stepped by the first game's
    /// own walk with this tick's controls, and the player put where it stands.
    /// </summary>
    /// <remarks>
    /// The body finds its own ground, walls and roof. I.G.I. 2's footing is then looked for under where it stands, without moving
    /// it, for what the sequel reads of the ground -- the platform it rides, the material its steps sound, a conveyor's push --
    /// and the ground bit is the body's, so the states follow what it did.
    /// </remarks>
    private void MoveOnIgi1Feet(Igi1FootMotion feet)
    {
        Vector3d start = Position;
        UpdatePlatformMovement(ref start, updatePosition: true, updateOrientation: true);

        feet.Step(start, Gamma, Igi1Command());
        LandFromIgi1Feet(feet);
        if (feet.Body.IsGrounded && GroundCollider is { } ground && Surroundings.PushOnConveyor(ground, GroundIntersection) is { } push)
        {
            feet.Push(push);
        }

        Vector3d to = feet.Body.Position;
        Vector3d footing = to;
        GroundCollision(to, ref footing, GroundTolerance);
        Locomotion.Flags = feet.Body.IsGrounded
            ? Locomotion.Flags | (uint)Igi2PlayerSignal.Grounded
            : Locomotion.Flags & ~(uint)Igi2PlayerSignal.Grounded;

        GroundMovementSpeed = default;
        DeltaTranslation = default;
        Speed = feet.Body.Velocity;
        ArriveFrom(start, to);
    }

    /// <summary>The tick's controls as Project I.G.I.'s body reads them.</summary>
    /// <remarks>
    /// The move keys as the first game's axes; the pace as the walk toggle stands once this tick's press has flipped it, as the
    /// first game flips its latch before it moves; the stance I.G.I. 2's own keys chose this tick, so the clips the states play
    /// are the ones the body moves by; the jump on its key's press, except on a lift, where the first game allows none either.
    /// The turn is the player's own, already taken.
    /// </remarks>
    private PlayerCommand Igi1Command()
    {
        float forward = Axis(Igi2HumanChannel.MoveForwards, Igi2HumanChannel.MoveBackwards);
        float strafe = Axis(Igi2HumanChannel.MoveRight, Igi2HumanChannel.MoveLeft);
        bool jump = Channels.WasPressed(Igi2HumanChannel.Jump) && !Surroundings.IsOnElevator;
        return new PlayerCommand(forward, strafe, 0f, 0f, Run: RunsByKeys, Crouch: Pose == Igi2HumanPose.Crouching, Jump: jump);
    }

    private float Axis(Igi2HumanChannel positive, Igi2HumanChannel negative) =>
        (Channels.IsHeld(positive) ? 1f : 0f) - (Channels.IsHeld(negative) ? 1f : 0f);

    /// <summary>
    /// A landing Project I.G.I.'s body made, in the tick it made it as that game takes it: I.G.I. 2's damage and the noise its AI
    /// hears, by the speed the first game measures (the fall's, straight down). The view's dip is the first game's own
    /// (<see cref="Igi1FootMotion.LandingKick"/>), so the sequel's is not armed.
    /// </summary>
    private void LandFromIgi1Feet(Igi1FootMotion feet)
    {
        if (feet.TakeLanding() is { } landing)
        {
            LandAt(landing.SpeedMetresPerSecond, viewKick: 0f);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// On Project I.G.I.'s body, that game's camera (<see cref="Igi1FootMotion.Eye"/>): over the feet by the body's origin in
    /// the pose and its stance's offset, dipped by its landings, so the view sinks into a crouch and rides the walk, the jump and
    /// the landing as the first game's does.
    /// </remarks>
    protected override Vector3d ViewEyes => StandsOnIgi1Feet && Igi1Feet!.IsSeated ? Igi1Feet.Eye : base.ViewEyes;

    /// <inheritdoc/>
    protected override float ViewObjectDip =>
        StandsOnIgi1Feet && Igi1Feet!.IsSeated ? Igi1Feet.LandingKick.ViewModelOffsetUnits : base.ViewObjectDip;
}
