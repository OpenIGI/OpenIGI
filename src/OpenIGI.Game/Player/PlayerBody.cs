using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Player;

/// <summary>What the player is asking for this tick.</summary>
/// <param name="Forward">Forward intent, −1 to 1.</param>
/// <param name="Strafe">Sideways intent, −1 to 1.</param>
/// <param name="YawDelta">How far to turn this tick, in radians.</param>
/// <param name="PitchDelta">How far to look up or down this tick, in radians.</param>
/// <param name="Run">Whether the run modifier is held.</param>
/// <param name="Crouch">Whether the crouch modifier is held.</param>
/// <param name="Jump">Whether jump was pressed this tick — the rising edge, not the level.</param>
public readonly record struct PlayerCommand(
    float Forward,
    float Strafe,
    float YawDelta,
    float PitchDelta,
    bool Run,
    bool Crouch,
    bool Jump);

/// <summary>
/// A player standing in the world: root-motion movement, gravity, and the terrain under its feet.
/// </summary>
/// <remarks>
/// <para>
/// The tick order is the original's, from <c>0x4109A0</c>: turn, then take the animation's delta
/// translation and rotate it into world space (<see cref="HumanMotion.ApplyRootMotion"/>), then move
/// and settle against the ground.
/// </para>
/// <para>
/// <b>What is faithful and what is not.</b> Gravity, the drag, the root-motion scale and rotation, the
/// tick rate, the eye height and the ground query are all read from the game. What is <i>not</i> here
/// is the object sweep — <c>0x4CDB20</c> is unread — so the body collides with terrain only and walks
/// through placed models. The locomotion state machine (<c>player.md</c> §5) also is not driving the
/// animation choice yet; the caller supplies the delta translation.
/// </para>
/// </remarks>
public sealed class PlayerBody
{
    /// <summary>How high the eye sits above the feet while standing, in world units.</summary>
    /// <remarks>
    /// The wall probe's high sample (<see cref="HumanWallProbe.HighProbeStandingInUnits"/>) is taken at
    /// head height, which is 1.8 m — so that is where the view goes.
    /// </remarks>
    public const double StandingEyeHeight = HumanWallProbe.HighProbeStandingInUnits;

    /// <summary>How high the eye sits while crouched.</summary>
    /// <remarks>The crouched middle probe, <see cref="HumanWallProbe.MiddleProbeCrouchingInUnits"/>, doubled.</remarks>
    public const double CrouchingEyeHeight = HumanWallProbe.MiddleProbeCrouchingInUnits * 2.0;

    /// <summary>How fast the eye moves between standing and crouched, in units per tick.</summary>
    /// <remarks>
    /// <b>Divergence, deliberate.</b> The original drives the crouch through the locomotion state
    /// machine and its animations; with those not yet driving the body, the eye is eased at a fixed
    /// rate so the view does not snap. Nothing depends on the value.
    /// </remarks>
    public const double CrouchEaseUnitsPerTick = 400.0;

    /// <summary>The upward speed a jump starts with, in units per tick.</summary>
    /// <remarks><see cref="HumanPlayerParameters.JumpVerticalSpeed"/>; 1024 in retail.</remarks>
    public double JumpSpeed { get; init; } = 1024.0;

    /// <summary>The horizontal speed a jump carries, in units per tick.</summary>
    /// <remarks>
    /// <see cref="HumanPlayerParameters.JumpHorizontalSpeed"/> — 17.5 km/h, which is 663.7 units a
    /// tick, or 4.86 m/s. That is almost exactly the run speed the forward animation covers, so a
    /// running jump carries the pace you were already going.
    /// </remarks>
    public double JumpHorizontalSpeed { get; init; } =
        17.5 * HumanPlayerParameters.UnitsPerTickPerKilometrePerHour;

    /// <summary>How far the pitch may travel from level, in radians.</summary>
    public const float PitchLimit = 1.5533431f;

    /// <summary>Gets or sets where the player's feet are.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets which way the player faces, in radians.</summary>
    public float Yaw { get; set; }

    /// <summary>Gets or sets how far up or down the player looks, in radians.</summary>
    public float Pitch { get; set; }

    /// <summary>Gets the player's velocity, which only matters while airborne.</summary>
    public Vector3f Velocity { get; private set; }

    /// <summary>Gets whether the player is standing on something.</summary>
    public bool IsGrounded { get; private set; }

    /// <summary>Gets whether the player is crouched.</summary>
    public bool IsCrouched { get; private set; }

    /// <summary>Gets where the eye currently is, easing between the two heights.</summary>
    public double EyeHeight { get; private set; } = StandingEyeHeight;

    /// <summary>Gets the eye's world position — where the view is taken from.</summary>
    public Vector3d Eye => new(Position.X, Position.Y, Position.Z + EyeHeight);

    /// <summary>Advances the player by one tick.</summary>
    /// <param name="command">What the player asked for.</param>
    /// <param name="terrain">The level's terrain, for the ground query.</param>
    /// <param name="rootDelta">
    /// The animation's delta translation for this tick, in the body's own frame: <b>X is sideways and
    /// Y is forward</b>, matching the view's right and forward axes.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is <see langword="null"/>.</exception>
    public void Tick(in PlayerCommand command, TerrainData terrain, Vector3f rootDelta)
    {
        ArgumentNullException.ThrowIfNull(terrain);

        Yaw += command.YawDelta;
        Pitch = Math.Clamp(Pitch + command.PitchDelta, -PitchLimit, PitchLimit);

        IsCrouched = command.Crouch;
        double targetEye = IsCrouched ? CrouchingEyeHeight : StandingEyeHeight;
        EyeHeight += Math.Clamp(targetEye - EyeHeight, -CrouchEaseUnitsPerTick, CrouchEaseUnitsPerTick);

        // Root motion: the animation's own delta, scaled and turned by the body's facing. There is no
        // speed constant - see HumanMotion.ApplyRootMotion.
        Vector3f step = HumanMotion.ApplyRootMotion(rootDelta, FacingBasis(Yaw));

        // On the ground the animation moves the body; in the air the velocity does, and the two must
        // not both apply or a jump would travel at twice its speed.
        Vector3d next = IsGrounded
            ? new Vector3d(Position.X + step.X, Position.Y + step.Y, Position.Z)
            : Position;

        if (IsGrounded && command.Jump)
        {
            // A jump takes the direction you are ALREADY moving, normalised, at a fixed horizontal
            // speed - it does not read the keys again. That is why you cannot steer in mid-air: you
            // carry the momentum you had at take-off and nothing changes it until you land.
            float horizontalX = 0f;
            float horizontalY = 0f;
            float length = MathF.Sqrt((step.X * step.X) + (step.Y * step.Y));
            if (length > 0f)
            {
                horizontalX = step.X / length * (float)JumpHorizontalSpeed;
                horizontalY = step.Y / length * (float)JumpHorizontalSpeed;
            }

            Velocity = new Vector3f(horizontalX, horizontalY, (float)JumpSpeed);
            IsGrounded = false;
        }

        if (!IsGrounded)
        {
            Velocity = HumanMotion.Integrate(Velocity);
            next = new Vector3d(next.X + Velocity.X, next.Y + Velocity.Y, next.Z + Velocity.Z);
        }

        Settle(ref next, terrain);
        Position = next;
    }

    /// <summary>Advances the player by one tick, taking its movement from its own animation.</summary>
    /// <param name="command">What the player asked for.</param>
    /// <param name="terrain">The level's terrain.</param>
    /// <param name="animator">The locomotion animator, which supplies the delta translation.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The faithful path: the input becomes motion flags, the flags pick a locomotion state
    /// (<see cref="HumanIdleTransitions"/>, transcribed from state zero's own per-tick), the state names
    /// an animation, and the animation's root-motion track says how far the body moved. No speed
    /// constant is consulted anywhere.
    /// </para>
    /// <para>
    /// <b>What is still missing:</b> only the transitions <i>out of idle</i> are driven. The full
    /// 49-state graph — the turns, the transitions between movement states, the stance changes — is
    /// mapped in <see cref="HumanLocomotionGraph"/> and not yet walked, so a direction change restarts
    /// a cycle rather than blending through the state the original would pass through.
    /// </para>
    /// </remarks>
    public void TickAnimated(in PlayerCommand command, TerrainData terrain, HumanLocomotionAnimator animator)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(animator);

        animator.Enter(SelectState(command));
        Tick(command, terrain, animator.Advance());
    }

    /// <summary>Turns this tick's intent into the locomotion state the original would be in.</summary>
    /// <remarks>
    /// The order is state zero's own: airborne first, then the stance, then the four movement bits in
    /// the order forward, backward, right, left. Diagonals therefore resolve to a single state — the
    /// first bit that matches — exactly as the original resolves them.
    /// </remarks>
    private int SelectState(in PlayerCommand command)
    {
        if (!IsGrounded)
        {
            return HumanLocomotionGraph.Falling;
        }

        HumanMotionState flags = HumanMotionState.OnGround;

        if (command.Crouch)
        {
            flags |= HumanMotionState.Crouched;
        }

        if (command.Forward > 0f)
        {
            flags |= HumanMotionState.MoveForward;
        }
        else if (command.Forward < 0f)
        {
            flags |= HumanMotionState.MoveBackward;
        }

        if (command.Strafe > 0f)
        {
            flags |= HumanMotionState.MoveRight;
        }
        else if (command.Strafe < 0f)
        {
            flags |= HumanMotionState.MoveLeft;
        }

        return HumanIdleTransitions.Next(flags, command.Jump);
    }

    /// <summary>The body's orientation about the vertical, as the root-motion rotation uses it.</summary>
    /// <remarks>
    /// <para>
    /// Only yaw turns the body: the pitch is the view's alone, so looking down does not walk a player
    /// into the floor.
    /// </para>
    /// <para>
    /// <b>The columns are the view's own axes</b>, because the root-motion multiply reads the matrix by
    /// rows. <see cref="OpenIGI.Rendering.Camera3D.LookRotation"/> gives right as
    /// <c>(cos, −sin, 0)</c> and forward as <c>(sin, cos, 0)</c>; putting those in as columns is what
    /// makes a delta of <c>(strafe, forward, 0)</c> come out along the axes the player is looking down.
    /// Building it the other way up transposes it, and the player then walks at right angles to the
    /// view — which is exactly the bug this comment exists to stop recurring.
    /// </para>
    /// </remarks>
    private static Matrix3f FacingBasis(float yaw)
    {
        float cos = MathF.Cos(yaw);
        float sin = MathF.Sin(yaw);

        return Matrix3f.FromRows(
            new Vector3f(cos, sin, 0f),
            new Vector3f(-sin, cos, 0f),
            new Vector3f(0f, 0f, 1f));
    }

    /// <summary>Puts the body on the ground, or leaves it falling.</summary>
    /// <remarks>
    /// The step-down budget is the human's own: nothing while airborne, and
    /// <see cref="HumanGroundQuery.GroundedStepDownInUnits"/> while walking, which is what lets a body
    /// follow a slope down without leaving the surface at every bump.
    /// </remarks>
    private void Settle(ref Vector3d position, TerrainData terrain)
    {
        double from = position.Z + HumanGroundQuery.ProbeOriginStandingInUnits;

        if (!TerrainHeightQuery.TryGetGround(terrain, position.X, position.Y, from, out TerrainGround ground))
        {
            IsGrounded = false;
            return;
        }

        double stepDown = IsGrounded
            ? HumanGroundQuery.GroundedStepDownInUnits
            : HumanGroundQuery.AirborneStepDownInUnits;

        if (position.Z <= ground.Height + stepDown)
        {
            position = new Vector3d(position.X, position.Y, ground.Height);
            Velocity = new Vector3f(Velocity.X, Velocity.Y, 0f);
            IsGrounded = true;
            return;
        }

        IsGrounded = false;
    }

    /// <summary>How far a placement search looks below its start, in world units.</summary>
    /// <remarks>
    /// Half a kilometre. Placement is <b>not</b> the runtime ground query and does not share its
    /// four-metre reach: dropping a player into a level starts from wherever the caller was looking,
    /// which may be tens of metres up. The search walks down in whole probe lengths and takes the
    /// first ground it meets, so it lands on the highest surface below the start rather than the
    /// lowest.
    /// </remarks>
    public const double PlacementSearchUnits = 500.0 * 4096.0;

    /// <summary>Drops the player onto the ground beneath it, for placing one at the start of a level.</summary>
    /// <param name="terrain">The level's terrain.</param>
    /// <returns><see langword="false"/> when there is nothing under the player to stand on.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is <see langword="null"/>.</exception>
    public bool PlaceOnGround(TerrainData terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);

        double from = Position.Z + HumanGroundQuery.ProbeOriginStandingInUnits;
        double floor = from - PlacementSearchUnits;

        while (from > floor)
        {
            if (TerrainHeightQuery.TryGetGround(terrain, Position.X, Position.Y, from, out TerrainGround ground))
            {
                Position = new Vector3d(Position.X, Position.Y, ground.Height);
                Velocity = default;
                IsGrounded = true;
                return true;
            }

            from -= TerrainHeightQuery.SearchDepthUnits;
        }

        return false;
    }
}
