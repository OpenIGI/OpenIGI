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
/// <b>What is faithful and what is not.</b> Gravity, the air control, the root-motion scale and
/// rotation, the tick rate, the eye height, the ground query, the wall sweep and the slope slide are
/// all read from the game — the tunables from <see cref="Parameters"/>, which is the script's own.
/// What is <i>not</i> is the broad phase: <see cref="Walls"/> walks a flat list of placed objects
/// rather than the octree <c>0x4C6940</c> commits into. It finds the same surfaces. The locomotion
/// state machine drives the direction (<see cref="HumanLocomotionStates.ForDirection"/>) but not yet
/// the full 49-state graph.
/// </para>
/// </remarks>
public sealed class PlayerBody
{
    private float? _pendingLandingVelocityZ;
    private Vector3f? _pendingWalkOffVelocity;
    private Action<HumanFallImpact>? _fallImpactSink;

    /// <summary>How high the eye sits above the feet while standing, in world units.</summary>
    /// <remarks>
    /// The wall probe's high sample (<see cref="HumanWallProbe.HighProbeStandingInUnits"/>) is taken at
    /// head height, which is 1.8 m — so that is where the view goes.
    /// </remarks>
    public const double StandingEyeHeight = HumanWallProbe.HighProbeStandingInUnits;

    /// <summary>How high the eye sits while crouched.</summary>
    /// <remarks>
    /// The crouched head height, exactly as standing uses the standing head height: the wall probe's
    /// crouched high sample, <see cref="HumanWallProbe.HighProbeCrouchingInUnits"/> — 1.3 m. An earlier
    /// pass doubled the crouched <i>middle</i> probe instead, which put the eye at 0.4 m — knee height —
    /// low enough to dip under the drawn terrain on any uneven ground.
    /// </remarks>
    public const double CrouchingEyeHeight = HumanWallProbe.HighProbeCrouchingInUnits;

    /// <summary>How fast the eye moves between standing and crouched, in units per tick.</summary>
    /// <remarks>
    /// <b>Divergence, deliberate.</b> The original drives the crouch through the locomotion state
    /// machine and its animations; with those not yet driving the body, the eye is eased at a fixed
    /// rate so the view does not snap. Nothing depends on the value.
    /// </remarks>
    public const double CrouchEaseUnitsPerTick = 400.0;

    /// <summary>Gets or sets the tuning <c>humanplayer.qvm</c> publishes.</summary>
    /// <remarks>
    /// <para>
    /// Every number below comes from here when it is set. The fallbacks are the retail values, so a body
    /// with no parameters behaves like the shipped player rather than like nothing — but the fallbacks
    /// are a convenience for tests, not the contract. <b>The game is meant to run off the script.</b>
    /// </para>
    /// </remarks>
    public HumanPlayerParameters? Parameters { get; set; }

    /// <summary>Gets the player's accumulated damage.</summary>
    /// <remarks>
    /// The unconfigured body starts with the base human value. A composed runtime supplies the health
    /// object its HUD and death state observe through <see cref="UseHealth"/>; that object's maximum must
    /// come from <see cref="HumanPlayerParameters.MaximumHealth"/>, argument 7 of <c>humanplayer.qvm</c>.
    /// </remarks>
    public HumanHealth Health { get; private set; } = new(HumanHitZoneTable.Player);

    /// <summary>Uses the owning runtime's player-health accumulator for fall damage.</summary>
    /// <param name="health">The same instance the HUD and death state observe.</param>
    /// <remarks>
    /// A composed game must replace the body's base accumulator with its live player object so a landing
    /// cannot damage a private copy that the HUD never reads. The desktop builds that object using the
    /// maximum published by <c>humanplayer.qvm</c>, rather than assuming the shipped script's value.
    /// </remarks>
    public void UseHealth(HumanHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);
        Health = health;
    }

    /// <summary>Gets the red full-screen damage wash raised by a damaging landing.</summary>
    public HumanDamageFlash DamageFlash { get; } = new();

    /// <summary>Gets the most recently processed landing, or <see langword="null"/> before the first.</summary>
    public HumanFallImpact? LastFallImpact { get; private set; }

    /// <summary>Routes landing presentation and AI-hearing effects to the owning runtime.</summary>
    public void SetFallImpactSink(Action<HumanFallImpact>? sink) => _fallImpactSink = sink;

    /// <summary>Gets the upward speed a jump starts with, in units per tick.</summary>
    /// <remarks><see cref="HumanPlayerParameters.JumpVerticalSpeed"/>; 27 km/h, exactly 1024, in retail.</remarks>
    public double JumpSpeed => Parameters?.JumpVerticalSpeed ?? 1024.0;

    /// <summary>Gets how much air control one tick adds, in units per tick.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="HumanPlayerParameters.Speed3"/> — argument 3, 0.5 km/h, about 19 units a tick. The
    /// airborne integrator adds this along each held movement axis every tick.
    /// </para>
    /// <para>
    /// It accumulates into the velocity rather than replacing it, so it is an acceleration: a jump can be
    /// leaned into over its two dozen ticks, by something over a metre, but it cannot be turned. This is
    /// why the original lets you keep pressing keys mid-air without the jump becoming steerable.
    /// </para>
    /// </remarks>
    public double AirControlSpeed => Parameters?.Speed3 ?? 0.5 * HumanPlayerParameters.UnitsPerTickPerKilometrePerHour;

    /// <summary>Gets the horizontal speed a jump carries, in units per tick.</summary>
    /// <remarks>
    /// <see cref="HumanPlayerParameters.JumpHorizontalSpeed"/> — 17.5 km/h, which is 663.7 units a
    /// tick, or 4.86 m/s. That is almost exactly the run speed the forward animation covers, so a
    /// running jump carries the pace you were already going.
    /// </remarks>
    public double JumpHorizontalSpeed =>
        Parameters?.JumpHorizontalSpeed ?? 17.5 * HumanPlayerParameters.UnitsPerTickPerKilometrePerHour;

    /// <summary>Gets the factor the animation's delta translation is scaled by.</summary>
    /// <remarks><see cref="HumanPlayerParameters.DeltaTranslationScale"/> — argument 0, 1.75 in retail.</remarks>
    public float DeltaTranslationScale =>
        Parameters?.DeltaTranslationScale ?? HumanMotion.DefaultDeltaTranslationScale;

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

    /// <summary>Gets or sets the velocity a jump inherits from whatever the body was standing on.</summary>
    /// <remarks>
    /// <para>
    /// <c>Human+1832</c>. The jump entry adds it to the take-off velocity on all three axes, and
    /// <c>0x4632D0</c> zeroes it in the same breath as it clears the on-ground bit — so it is consumed,
    /// not carried. Nothing else in the airborne path reads it.
    /// </para>
    /// <para>
    /// <b>It is zero for a player standing on terrain</b>, which is why the jump behaves the same with
    /// and without it today. What fills it in is unresolved; a moving surface or an explosion's shove is
    /// the shape of thing it must be for. It is exposed so that when the writer is found there is
    /// somewhere for the value to go, rather than the term being quietly dropped.
    /// </para>
    /// </remarks>
    public Vector3f InheritedVelocity { get; set; }

    /// <summary>Gets or sets the ray cast the wall sweep uses, or <see langword="null"/> for terrain only.</summary>
    /// <remarks>
    /// Build one with <see cref="ObjectCollisionIndex.Build"/> and hand over its
    /// <see cref="ObjectCollisionIndex.Caster"/>. Left unset the body still stands, walks and falls —
    /// it simply walks through anything placed, which is what it did before the sweep existed.
    /// </remarks>
    public WorldRayCast? Walls { get; set; }

    /// <summary>Gets whether the last tick's move was stopped by a wall.</summary>
    /// <remarks>Flag <c>0x8</c> in <c>Human+756</c>, raised by <c>0x461A70</c> when it slides.</remarks>
    public bool TouchedWall { get; private set; }

    /// <summary>Gets the slope slide the body has accumulated, in units per tick.</summary>
    /// <remarks>
    /// <see cref="HumanSlopeSlide"/>'s four bands, damped by 0.9 a tick. Zero on anything under 45°,
    /// which is most ground.
    /// </remarks>
    public Vector3f SlopeSlide { get; private set; }

    /// <summary>The horizontal shove one connecting round gives the player, in units per tick.</summary>
    /// <remarks>
    /// <c>189.62962</c> — exactly <b>5 km/h</b> on the 37.9259 units-per-tick-per-km/h grid. The
    /// player's hit handler (<c>0x412440</c>) normalises the round's travel direction and adds
    /// <c>x·189.62962</c> and <c>y·189.62962</c> into <c>Human+1648</c>/<c>+1652</c> — the same
    /// damped accumulator the slope slide runs on, so the shove decays by 0.9 a tick on its own.
    /// </remarks>
    public const float HitShoveUnitsPerTick = 189.62962f;

    /// <summary>Shoves the body horizontally — the per-hit knock of the player's hit handler.</summary>
    /// <param name="x">The X to add to the slide accumulator, in units per tick.</param>
    /// <param name="y">The Y to add.</param>
    /// <remarks>See <see cref="HitShoveUnitsPerTick"/>. Z is never written — the original adds only X and Y.</remarks>
    public void AddHitShove(float x, float y) =>
        SlopeSlide = new Vector3f(SlopeSlide.X + x, SlopeSlide.Y + y, SlopeSlide.Z);

    /// <summary>Gets the normal of the ground under the feet, or <see langword="null"/> when airborne.</summary>
    public Vector3f? GroundNormal { get; private set; }

    /// <summary>Gets whether the player is standing on something.</summary>
    public bool IsGrounded { get; private set; }

    /// <summary>Gets whether the player is crouched.</summary>
    public bool IsCrouched { get; private set; }

    /// <summary>Gets where the eye currently is, easing between the two heights.</summary>
    public double EyeHeight { get; private set; } = StandingEyeHeight;

    /// <summary>Gets the eye's world position — where the view is taken from.</summary>
    public Vector3d Eye => new(Position.X, Position.Y, Position.Z + EyeHeight);

    /// <summary>Detaches the ordinary ground mover before a ladder mount is committed.</summary>
    /// <remarks>
    /// <c>sub_4113E0</c> zeroes all six velocity accumulators and snaps the human off its ground task.
    /// Keeping a pre-mount fall or slope velocity would otherwise throw the body off the ladder later.
    /// </remarks>
    public void BeginLadderTraversal()
    {
        _pendingLandingVelocityZ = null;
        _pendingWalkOffVelocity = null;
        Velocity = default;
        InheritedVelocity = default;
        SlopeSlide = default;
        GroundNormal = null;
        IsGrounded = false;
        TouchedWall = false;
    }

    /// <summary>Runs state 40's damped slide velocity through the normal wall and ground collision pass.</summary>
    /// <param name="terrain">The level terrain.</param>
    /// <param name="animationDelta">Animation 169's world-space root delta for this tick.</param>
    /// <returns><see langword="true"/> when the slide generated the ground-collision signal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is <see langword="null"/>.</exception>
    public bool TickLadderSlide(TerrainData terrain, Vector3d animationDelta)
    {
        ArgumentNullException.ThrowIfNull(terrain);

        Velocity = HumanMotion.IntegrateLadderSlide(Velocity);
        Vector3d next = new(
            Position.X + animationDelta.X + Velocity.X,
            Position.Y + animationDelta.Y + Velocity.Y,
            Position.Z + animationDelta.Z + Velocity.Z);

        if (Walls is not null)
        {
            HumanWallSweepResult swept = HumanWallSweep.Sweep(Position, next, false, IsCrouched, Walls);
            next = swept.Destination;
            TouchedWall = swept.TouchedWall;
        }
        else
        {
            TouchedWall = false;
        }

        Settle(ref next, terrain, Position);
        Position = next;
        return IsGrounded;
    }

    /// <summary>Restores ordinary movement after the ladder's deinitialization flag is consumed.</summary>
    /// <param name="terrain">The level terrain.</param>
    /// <param name="placeOnGround">
    /// Whether to resolve the highest surface below the authored endpoint. Top animation 170 uses this
    /// to commit onto the roof instead of allowing numerical drift to fall through it.
    /// </param>
    public void EndLadderTraversal(TerrainData terrain, bool placeOnGround)
    {
        ArgumentNullException.ThrowIfNull(terrain);

        Velocity = default;
        InheritedVelocity = default;
        SlopeSlide = default;
        TouchedWall = false;
        if (placeOnGround)
        {
            IsGrounded = false;
            GroundNormal = null;
            PlaceOnGround(terrain);
        }
    }

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

        ProcessPendingLanding();

        // Grounded states observe a lost ground bit on the tick after collision. State 29's entry
        // (0x40BDB0) then copies the PREVIOUS tick's transformed animation delta into horizontal
        // velocity and returns without running gravity or air control. Keep that one-tick transition
        // explicit so walking off a ledge carries the pace that crossed it instead of dropping straight
        // down, and so neither the crossing step nor gravity is applied twice.
        bool enteredFallFromGround = false;
        if (!IsGrounded && _pendingWalkOffVelocity is Vector3f walkOffVelocity)
        {
            Velocity = walkOffVelocity;
            _pendingWalkOffVelocity = null;
            InheritedVelocity = default;
            enteredFallFromGround = true;
        }

        Yaw += command.YawDelta;
        Pitch = Math.Clamp(Pitch + command.PitchDelta, -PitchLimit, PitchLimit);

        IsCrouched = command.Crouch;
        double targetEye = IsCrouched ? CrouchingEyeHeight : StandingEyeHeight;
        EyeHeight += Math.Clamp(targetEye - EyeHeight, -CrouchEaseUnitsPerTick, CrouchEaseUnitsPerTick);

        // Root motion: the animation's own delta, scaled and turned by the body's facing. There is no
        // speed constant - see HumanMotion.ApplyRootMotion.
        Matrix3f facing = FacingBasis(Yaw);
        Vector3f step = HumanMotion.ApplyRootMotion(rootDelta, facing, DeltaTranslationScale);

        // The move is velocity PLUS the animation's step, always - 0x4614B0 adds them rather than
        // choosing between them. On the ground the velocity is zero and the step carries; in the air
        // the falling animation covers no ground and the velocity carries.
        Vector3d next = new(
            Position.X + step.X + SlopeSlide.X,
            Position.Y + step.Y + SlopeSlide.Y,
            Position.Z + SlopeSlide.Z);

        bool tookOff = false;

        if (IsGrounded && command.Jump)
        {
            // A jump takes the direction you are ALREADY moving, normalised, at a fixed horizontal
            // speed - it does not read the keys again. So the arc is set by the step you were mid-way
            // through, and the air control below can only lean it.
            float horizontalX = 0f;
            float horizontalY = 0f;
            float length = MathF.Sqrt((step.X * step.X) + (step.Y * step.Y));
            if (length > 0f)
            {
                horizontalX = step.X / length * (float)JumpHorizontalSpeed;
                horizontalY = step.Y / length * (float)JumpHorizontalSpeed;
            }

            // The inherited velocity is added and then consumed - 0x4632D0 zeroes it in the same breath
            // as it clears the on-ground bit.
            Velocity = new Vector3f(
                horizontalX + InheritedVelocity.X,
                horizontalY + InheritedVelocity.Y,
                (float)JumpSpeed + InheritedVelocity.Z);

            InheritedVelocity = default;
            IsGrounded = false;
            tookOff = true;
        }

        // The airborne integrator does not run on the tick you leave the ground. The jump entry
        // (0x40BA70) installs the airborne per-tick in the state slot and RETURNS - unlike every
        // transition inside 0x40BBD0, which sets the slot and calls it straight away. So the launch is
        // the take-off velocity applied clean and gravity does not bite until the tick after.
        if (!IsGrounded)
        {
            if (!tookOff && !enteredFallFromGround)
            {
                // Gravity, then air control - the integrator adds the steering to the velocity rather
                // than replacing it, so holding a direction through a jump bends the arc a little.
                Velocity = HumanMotion.Integrate(Velocity);

                if (AirControlSpeed != 0.0)
                {
                    Vector3f nudge = HumanMotion.AirControl(
                        command.Forward, command.Strafe, facing, (float)AirControlSpeed);

                    Velocity = new Vector3f(
                        Velocity.X + nudge.X,
                        Velocity.Y + nudge.Y,
                        Velocity.Z + nudge.Z);
                }
            }

            // The move itself happens on the take-off tick as much as on any other: the state function
            // sets the velocity and the per-tick's move-and-collide runs afterwards regardless.
            next = new Vector3d(next.X + Velocity.X, next.Y + Velocity.Y, next.Z + Velocity.Z);
        }

        // The walls, then the ground. 0x4614B0 runs the world sweep (0x461A70) before the ground query
        // (0x462390) for the same reason: sliding along a wall changes where you land.
        if (Walls is not null)
        {
            HumanWallSweepResult swept = HumanWallSweep.Sweep(Position, next, IsGrounded, IsCrouched, Walls);
            next = swept.Destination;
            TouchedWall = swept.TouchedWall;
        }
        else
        {
            TouchedWall = false;
        }

        bool wasAirborne = !IsGrounded;
        float landingVelocityZ = Velocity.Z;
        Settle(ref next, terrain, Position);
        if (wasAirborne && IsGrounded && landingVelocityZ < 0f)
        {
            _pendingLandingVelocityZ = landingVelocityZ;
        }
        else if (!wasAirborne && !IsGrounded)
        {
            // State 29 entry (0x40BDB0): Human+1636/+1640 receive the current transformed root
            // translation at +1312/+1316 plus the ground task's delta at +1832/+1836. Z receives only
            // +1840. Settle has already spent the root step that crossed the edge; this pending copy is
            // the velocity the following tick carries onward.
            _pendingWalkOffVelocity = new Vector3f(
                step.X + InheritedVelocity.X,
                step.Y + InheritedVelocity.Y,
                InheritedVelocity.Z);
        }

        // The slide is accumulated from the ground under the feet, damped, and carried into the next
        // tick's move - which is why a slope you cannot stand on keeps moving you after you stop
        // pressing anything.
        SlopeSlide = HumanSlopeSlide.Damp(
            HumanSlopeSlide.Accumulate(SlopeSlide, IsGrounded ? GroundNormal : null));

        Position = next;
        DamageFlash.Tick();
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
    /// <para>
    /// Airborne first, then the stance, then the four movement bits, and
    /// <see cref="HumanLocomotionStates.ForDirection"/> turns them into a state.
    /// </para>
    /// <para>
    /// <b>Correction.</b> This used to take the first movement bit that matched, on the reading that
    /// state zero's per-tick only reaches the four cardinals. It does — but only because from a
    /// standstill you can start in one direction; the diagonals are reached a tick later, from the
    /// movement states themselves. Resolving forward-and-right to state 1 meant holding W and D walked
    /// straight ahead. It resolves to state 2.
    /// </para>
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

        return HumanLocomotionStates.ForDirection(flags);
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
    /// <para>
    /// The step-down budget is the human's own: nothing while airborne, and
    /// <see cref="HumanGroundQuery.GroundedStepDownInUnits"/> while walking, which is what lets a body
    /// follow a slope down without leaving the surface at every bump.
    /// </para>
    /// <para>
    /// <b>Terrain and objects, in one query.</b> The original's downward ground cast (<c>0x4CBE40</c>)
    /// sweeps the placed models (<c>0x4CDB20</c>) <i>and</i> the terrain octree (<c>0x4CB050</c>) in the
    /// same call, and keeps whichever surface sits nearest the probe — terrain overwrites an object hit
    /// only when it is the closer of the two (<c>collision.md</c> §8). For a downward probe that is
    /// simply the higher of the two surfaces. Without the object half a body walks <i>through</i> a
    /// crate, a ramp or a building floor and stands on the terrain beneath it, which is the collision
    /// bug this restores.
    /// </para>
    /// </remarks>
    private void Settle(ref Vector3d position, TerrainData terrain, Vector3d movementStart)
    {
        double from = position.Z + HumanGroundQuery.ProbeOrigin(IsCrouched);

        bool found = false;
        double height = double.NegativeInfinity;
        Vector3f normal = default;

        // A damaging fall can cover more than the 0.4 m between the feet and the ordinary probe origin
        // in one 30 Hz tick. Querying only below the destination then starts underneath the triangle and
        // loses the terrain forever. The retail mover casts the move before its endpoint ground query;
        // preserve that continuous part for terrain by taking the first upward-facing triangle crossed
        // by the feet. This is what prevents a high fall tunnelling under the map.
        if (!IsGrounded && position.Z < movementStart.Z)
        {
            WorldTraceRequest fall = new(movementStart, position, WorldTraceMask.Terrain);
            if (TerrainRayTrace.TryTrace(terrain, fall, out WorldTraceHit crossed) && crossed.Normal.Z > 0f)
            {
                found = true;
                height = crossed.Position.Z;
                normal = crossed.Normal;
            }
        }

        if (!found && TerrainHeightQuery.TryGetGround(terrain, position.X, position.Y, from, out TerrainGround ground))
        {
            found = true;
            height = ground.Height;
            normal = new Vector3f((float)ground.NormalX, (float)ground.NormalY, (float)ground.NormalZ);
        }

        // The object half of the cast: a downward ray at the placed models. Its nearest hit is the
        // highest surface below the probe, so a top, a ramp or a floor above the terrain wins here.
        if (Walls is not null)
        {
            Vector3d probeTop = new(position.X, position.Y, from);
            Vector3d probeFloor = new(position.X, position.Y, from - TerrainHeightQuery.SearchDepthUnits);

            if (Walls(probeTop, probeFloor, out WorldRayHit hit) && (!found || hit.Position.Z > height))
            {
                found = true;
                height = hit.Position.Z;
                normal = hit.Normal;
            }
        }

        if (!found)
        {
            IsGrounded = false;
            GroundNormal = null;
            return;
        }

        double stepDown = IsGrounded
            ? HumanGroundQuery.GroundedStepDownInUnits
            : HumanGroundQuery.AirborneStepDownInUnits;

        if (position.Z <= height + stepDown)
        {
            position = new Vector3d(position.X, position.Y, height);
            Velocity = new Vector3f(Velocity.X, Velocity.Y, 0f);
            IsGrounded = true;
            GroundNormal = normal;
            return;
        }

        IsGrounded = false;
        GroundNormal = null;
    }

    private void ProcessPendingLanding()
    {
        if (_pendingLandingVelocityZ is not float landingVelocityZ)
        {
            return;
        }

        _pendingLandingVelocityZ = null;

        // The airborne state runs once more on the tick after the collision flag was raised. It applies
        // gravity before it notices that flag, so retail measures one final gravity step in the impact.
        HumanFallImpact impact = HumanFallDamage.Calculate(
            landingVelocityZ - HumanMotion.GravityPerTick,
            Health.MaximumHealth);

        Health.ApplyDirect(HumanDamage.FrontZone, impact.Damage);
        if (impact.Damage > 0f)
        {
            DamageFlash.Hit();
        }

        LastFallImpact = impact;
        _fallImpactSink?.Invoke(impact);
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

        _pendingWalkOffVelocity = null;

        double from = Position.Z + HumanGroundQuery.ProbeOriginStandingInUnits;
        double floor = from - PlacementSearchUnits;

        bool found = false;
        double height = double.NegativeInfinity;

        // Terrain: walk down in whole probe lengths and take the first — the highest — ground found.
        double probe = from;
        while (probe > floor)
        {
            if (TerrainHeightQuery.TryGetGround(terrain, Position.X, Position.Y, probe, out TerrainGround ground))
            {
                height = ground.Height;
                found = true;
                break;
            }

            probe -= TerrainHeightQuery.SearchDepthUnits;
        }

        // Objects: one downward cast finds the highest floor or platform below the spawn, so a body a
        // level authors on a building floor lands on it rather than dropping through to the terrain
        // beneath — the same object half the runtime ground query gained in Settle.
        if (Walls is not null
            && Walls(
                new Vector3d(Position.X, Position.Y, from),
                new Vector3d(Position.X, Position.Y, floor),
                out WorldRayHit hit)
            && (!found || hit.Position.Z > height))
        {
            height = hit.Position.Z;
            found = true;
        }

        if (!found)
        {
            return false;
        }

        Position = new Vector3d(Position.X, Position.Y, height);
        Velocity = default;
        IsGrounded = true;
        return true;
    }
}
