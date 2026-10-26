using OpenIGI.Game.Physics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>One wheel as the car step consumes it: the record entry plus its body-space offset.</summary>
/// <param name="Definition">The record entry (steerable, driven, steer limit, body index).</param>
/// <param name="OffsetUnits">
/// The wheel's offset from the body origin in body axes, in world units: X across the body,
/// Y along its forward axis, Z up.
/// </param>
/// <param name="LateralFriction">The across-the-wheel grip from the wheel body (0.95 on every car).</param>
/// <param name="LongitudinalFriction">The rolling resistance from the wheel body (0.05).</param>
/// <param name="RadiusUnits">
/// The drawn wheel's radius, which sets how fast it rolls. Retail keeps it on the wheel's own task
/// record; here it comes from the wheel model's own size, and falls back to the suspension travel.
/// </param>
public readonly record struct CarWheel(
    CarWheelTypeDefinition Definition,
    Vector3d OffsetUnits,
    float LateralFriction,
    float LongitudinalFriction,
    float RadiusUnits = 0f);

/// <summary>The live rigid-body state a car carries between physics ticks.</summary>
public sealed class CarMotionState
{
    /// <summary>Gets or sets the body velocity in world units per tick.</summary>
    public Vector3d Velocity { get; set; }

    /// <summary>Gets or sets the yaw rate in radians per tick.</summary>
    public double YawRate { get; set; }

    /// <summary>Gets or sets the accumulated wheel roll angle in radians.</summary>
    public double WheelRoll { get; set; }

    /// <summary>Gets or sets the live steering deflection in radians, for the steered wheels.</summary>
    public double WheelSteer { get; set; }

    /// <summary>Gets or sets the smoothed wheel spin rate in radians per tick.</summary>
    public double WheelSpinRate { get; set; }

    /// <summary>Gets or sets the body's vertical velocity in world units per tick.</summary>
    public double RiseRate { get; set; }

    /// <summary>Gets each drawn wheel's own roll, radians, when the car's body tracks its wheels one by one.</summary>
    public double[] WheelRolls { get; } = new double[RetailCarBody.MaxWheels];

    /// <summary>Gets each drawn wheel's own spin rate, radians a tick.</summary>
    public double[] WheelSpinRates { get; } = new double[RetailCarBody.MaxWheels];

    /// <summary>Gets how far each drawn wheel's spring has it up from its attach point, world units.</summary>
    public double[] WheelLifts { get; } = new double[RetailCarBody.MaxWheels];

    /// <summary>Gets or sets a value indicating whether the wheels are tracked one by one, in the arrays above.</summary>
    public bool HasWheelStates { get; set; }

    /// <summary>Clears the motion, leaving the body parked.</summary>
    public void Reset()
    {
        Velocity = default;
        YawRate = 0d;
        WheelRoll = 0d;
        WheelSteer = 0d;
        WheelSpinRate = 0d;
        RiseRate = 0d;
        Array.Clear(WheelRolls);
        Array.Clear(WheelSpinRates);
        Array.Clear(WheelLifts);
        HasWheelStates = false;
    }
}

/// <summary>The retail CAR force model, run once per 30 Hz physics tick.</summary>
/// <remarks>
/// <para>
/// Retail drives a car entirely through forces on a rigid body. <c>Car::Tick</c> (<c>0x42ECA0</c>)
/// smooths the two controls, runs the suspension sweep (<c>0x42EF00</c>) and hands the body to the
/// shared step <c>0x4FC0E0(body, 44.600887, 0.97, 0.9)</c>. That first constant is gravity: at 4096
/// units to the metre and 30 ticks to the second, 44.600887 units per tick squared is 9.80 m/s².
/// The other two are the per-tick retention applied to linear and angular velocity.
/// </para>
/// <para>
/// Per contacting wheel, <c>0x430D10</c> applies a drive force of
/// <c>EngineForce * throttle * g / drivenWheelCount</c> less the body speed, directed along the body
/// forward axis rotated by that wheel's steering angle (<c>MaximumSteerDegrees * pi/180 * steering</c>),
/// then the tyre model <c>0x430960</c> opposes the contact point's slip: the slip direction is taken
/// into wheel space, scaled by the lateral and longitudinal friction of the wheel body, taken back
/// out, and applied with a magnitude of <c>slip * mass * 0.1</c> while the tyre grips, dropping to
/// <c>normalLoad * 0.24</c> once the slip passes <c>4 * normalLoad / mass</c>. Steering is
/// therefore emergent -- the front wheels push the nose round and the rear tyres resist the resulting
/// sideslip -- rather than a yaw rate written straight into the transform.
/// </para>
/// <para>
/// This is the planar reduction of that model: the cutscene cars run on level ground, so the
/// suspension's only contribution is the normal load it carries, which at rest is exactly
/// <c>mass * g</c> shared between the contacting wheels. Roll and pitch, and the vertical degree of
/// freedom, stay with the authored transform.
/// </para>
/// </remarks>
public static class CarDynamics
{
    /// <summary>Gravity in world units per tick squared -- retail's 44.600887, or 9.8 m/s².</summary>
    public const float GravityUnitsPerTickSquared = 44.600887f;

    /// <summary>The per-tick linear retention <c>Car::Tick</c> passes to the shared body step.</summary>
    public const float LinearRetention = 0.97f;

    /// <summary>The per-tick angular retention from the same call.</summary>
    public const float AngularRetention = 0.9f;

    /// <summary>The slip-proportional term of the tyre force (<c>0x430960</c>).</summary>
    public const float SlipForceScale = 0.1f;

    /// <summary>The share of the wheel's normal load a sliding tyre holds (<c>0x430960</c>).</summary>
    public const float SlidingLoadFactor = 0.24f;

    /// <summary>
    /// The slip at which a tyre breaks away, as a multiple of load over mass (<c>0x430960</c>). Below
    /// it the tyre grips and its force rises with slip; above it the force drops to the sliding share.
    /// </summary>
    public const float StictionLoadFactor = 4f;

    /// <summary>Advances one car by a single physics tick.</summary>
    /// <param name="motion">The body's live velocity and yaw rate, updated in place.</param>
    /// <param name="physics">The car's parsed physics record.</param>
    /// <param name="wheels">The car's wheels with their body-space offsets.</param>
    /// <param name="position">The body's world position, which the suspension probes from.</param>
    /// <param name="forward">The body's forward axis in world space (the orientation's second column).</param>
    /// <param name="lateral">The body's across axis in world space (the orientation's first column).</param>
    /// <param name="steering">The smoothed steering control, -1 to 1.</param>
    /// <param name="thrust">The smoothed throttle control.</param>
    /// <param name="handbrake">Whether the parking brake is engaged.</param>
    /// <param name="groundHeightAt">
    /// Resolves the surface height under a wheel, in world units. Without it the body keeps whatever
    /// height it was given and only the ground plane is simulated.
    /// </param>
    /// <returns>The world-unit translation and the yaw change, both for this tick.</returns>
    public static (Vector3d Translation, double YawDelta) Step(
        CarMotionState motion,
        in CarPhysicsDefinition physics,
        IReadOnlyList<CarWheel> wheels,
        Vector3d position,
        Vector3d forward,
        Vector3d lateral,
        float steering,
        float thrust,
        bool handbrake,
        Func<Vector3d, double>? groundHeightAt = null)
    {
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(wheels);

        double mass = physics.Mass > 0f ? physics.Mass : 1d;
        int contacts = 0;
        int driven = 0;
        for (int i = 0; i < wheels.Count; i++)
        {
            if (!wheels[i].Definition.HasContactPoint)
            {
                continue;
            }

            contacts++;
            if (wheels[i].Definition.IsEngine)
            {
                driven++;
            }
        }

        if (contacts == 0)
        {
            return (default, 0d);
        }

        // 0x430960 takes the load as the WHOLE body weight along the contact normal, per wheel --
        // it is not shared between them -- so each tyre can hold the car on its own.
        double normalLoad = mass * GravityUnitsPerTickSquared;
        double breakaway = StictionLoadFactor * normalLoad / mass;
        double inertia = InertiaAboutUp(physics, mass);
        double speed = motion.Velocity.Length;

        Vector3d force = default;
        double torque = 0d;

        for (int i = 0; i < wheels.Count; i++)
        {
            CarWheel wheel = wheels[i];
            if (!wheel.Definition.HasContactPoint)
            {
                continue;
            }

            double steer = wheel.Definition.IsSteering
                ? wheel.Definition.MaxAngleRadians * steering
                : 0d;
            double cos = Math.Cos(steer);
            double sin = Math.Sin(steer);
            Vector3d offset = (lateral * wheel.OffsetUnits.X) + (forward * wheel.OffsetUnits.Y);

            if (wheel.Definition.IsEngine && thrust != 0f && driven > 0)
            {
                double engine = (double)physics.EngineForce * thrust * GravityUnitsPerTickSquared / driven;
                if (physics.SkidSteer)
                {
                    engine *= SkidSteerFactor(steering, wheel.OffsetUnits.X);
                }

                double magnitude = engine - speed;

                // Toward the across axis for a positive input. 0x430D10 hands sub_4B3BE0 the
                // negated angle, but that builds the rotation the other way round, so the net
                // effect is this: a probe of the retail game shows the truck's forward axis turning
                // onto its across axis while the smoothed steering is positive.
                Vector3d drive = ((forward * cos) + (lateral * sin)) * magnitude;
                force += drive;
                torque += Vector3d.Cross(offset, drive).Z;
            }

            // The contact point's own velocity, which is what the tyre resists.
            Vector3d pointVelocity = motion.Velocity + Perpendicular(offset, motion.YawRate);
            double slip = pointVelocity.Length;
            if (slip <= 1e-9d)
            {
                continue;
            }

            Vector3d opposing = pointVelocity / -slip;
            double across = Vector3d.Dot(opposing, lateral);
            double along = Vector3d.Dot(opposing, forward);

            // Into wheel space, scale each axis by its own friction, and back out. The scaled vector
            // is deliberately not renormalised: its length carries the reduced grip. The handbrake
            // raises the rolling resistance to the lateral value, which is how retail locks a wheel.
            //
            // The tyre turns the OPPOSITE way from the drive direction above: 0x430D10 negates the
            // angle for the drive and hands the positive one to 0x430960, and against the matrix
            // those two build that comes out as +angle here and -angle there. Using the drive's sign
            // for both leaves a steered wheel skating sideways -- at 45 degrees it reads a slip
            // almost entirely across the tyre, which brakes the car instead of turning it.
            double wheelAcross = (across * cos) - (along * sin);
            double wheelAlong = (along * cos) + (across * sin);
            wheelAcross *= wheel.LateralFriction;
            wheelAlong *= handbrake ? wheel.LateralFriction : wheel.LongitudinalFriction;
            double bodyAcross = (wheelAcross * cos) + (wheelAlong * sin);
            double bodyAlong = (wheelAlong * cos) - (wheelAcross * sin);

            // Retail compares the slip against the breakaway point and picks one of two branches --
            // a gripping tyre whose force grows with slip, or a sliding one pinned to 0.24 of its
            // load. The force drops at the crossover, which is the stiction break every car feels.
            double grip = slip > breakaway
                ? normalLoad * SlidingLoadFactor
                : slip * mass * SlipForceScale;
            Vector3d friction = ((lateral * bodyAcross) + (forward * bodyAlong)) * grip;
            force += friction;
            torque += Vector3d.Cross(offset, friction).Z;
        }

        motion.Velocity += force / mass;
        motion.YawRate += torque / inertia;

        double rise = Suspend(motion, physics, wheels, position, forward, lateral, mass, groundHeightAt);
        Vector3d translation = motion.Velocity + new Vector3d(0d, 0d, rise);
        double yawDelta = motion.YawRate;

        // 0x4FBF90 integrates first and damps the stored velocity afterwards, so the step the body
        // takes this tick is the undamped one. Damping before the move instead is a 3% shorter step:
        // it flatters Mission 2's parking spot and costs Mission 1's route more than it gains.
        motion.Velocity *= LinearRetention;
        motion.YawRate *= AngularRetention;

        AnimateWheels(motion, physics, wheels, translation.Length, steering, handbrake);
        return (translation, yawDelta);
    }

    /// <summary>Turns and steers the drawn wheels for one tick of travel.</summary>
    /// <param name="motion">The car's motion state, whose wheel fields are advanced.</param>
    /// <param name="physics">The car's parsed physics record.</param>
    /// <param name="wheels">The car's wheels.</param>
    /// <param name="travelled">How far the body moved this tick, world units.</param>
    /// <param name="steering">The smoothed steering control.</param>
    /// <param name="handbrake">Whether the parking brake is engaged.</param>
    public static void AnimateWheels(
        CarMotionState motion,
        in CarPhysicsDefinition physics,
        IReadOnlyList<CarWheel> wheels,
        double travelled,
        float steering,
        bool handbrake)
    {
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(wheels);
        // Wheel::Tick (0x4301C0) drives the drawn wheels: a wheel in contact takes the speed of its
        // body's contact point, ignoring anything under 40.96 units a tick, and folds it into a
        // smoothed spin rate of speed/radius at 0.3 of the new value to 0.7 of the old. The roll
        // accumulates against that rate, and a steered wheel's angle is the NEGATIVE of
        // MaximumSteerDegrees * steering * pi/180 -- the same sign as the drive direction above.
        motion.WheelSpinRate = DrawnWheel.NextSpinRate(travelled, WheelRadiusUnits(physics, wheels), motion.WheelSpinRate);
        if (handbrake)
        {
            motion.WheelSpinRate = 0d;
        }

        motion.WheelRoll -= motion.WheelSpinRate;
        motion.WheelSteer = -MaximumSteerRadians(wheels) * steering;
    }

    /// <summary>Rolls, steers and lifts each drawn wheel of a car on the original's body -- <c>Wheel_Run</c> and <c>Wheel_Move</c>.</summary>
    /// <param name="motion">The car's motion, whose per-wheel state is advanced.</param>
    /// <param name="body">The car's body, after its tick.</param>
    /// <param name="wheels">The car's wheels, for their radii.</param>
    public static void AnimateWheels(CarMotionState motion, RetailCarBody body, IReadOnlyList<CarWheel> wheels)
    {
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(wheels);
        int count = Math.Min(body.Definition.Wheels.Length, RetailCarBody.MaxWheels);
        for (int i = 0; i < count; i++)
        {
            double radius = i < wheels.Count && wheels[i].RadiusUnits > 0f ? wheels[i].RadiusUnits : 0d;
            double rate = DrawnWheel.NextSpinRate(body.DrawnWheelSpeed(i), radius, motion.WheelSpinRates[i]);
            if (body.Handbrake)
            {
                rate = 0d;
            }

            motion.WheelSpinRates[i] = rate;
            motion.WheelRolls[i] -= rate;
            motion.WheelLifts[i] = body.DrawnWheelLift(i);
        }

        motion.WheelSteer = -MaximumSteerRadians(wheels) * body.Steering;
        motion.HasWheelStates = true;
    }

    /// <summary>Rides the springs, returning how far the body rises this tick.</summary>
    /// <remarks>
    /// The wheel sweep (<c>0x42EF00</c>) casts from <c>wheelZ + travel + 4096</c> down to
    /// <c>wheelZ - 2048</c> and reads the compression as <c>(travel + 4096) - distance</c> -- which
    /// is how far the wheel's origin sits UNDER the surface. It pushes the body up by
    /// <c>compression / travel * stiffness * mass * g</c>, so at rest every wheel sits a quarter of
    /// its travel into its spring for a stiffness of 0.5. A wheel driven past the full travel keeps
    /// it and its force grows by another <c>1 + depth / 4096</c>, and the body is lifted clear of
    /// the deepest of them. Gravity is the whole weight at the origin.
    /// Without this the body holds whatever height it was handed: no settle when it is boarded, and
    /// no ride over the ground it crosses.
    /// </remarks>
    private static double Suspend(
        CarMotionState motion,
        in CarPhysicsDefinition physics,
        IReadOnlyList<CarWheel> wheels,
        Vector3d position,
        Vector3d forward,
        Vector3d lateral,
        double mass,
        Func<Vector3d, double>? groundHeightAt)
    {
        if (groundHeightAt is null)
        {
            return 0d;
        }

        double spring = 0d;
        double deepest = 0d;
        bool grounded = false;
        for (int i = 0; i < wheels.Count; i++)
        {
            CarWheel wheel = wheels[i];
            double travel = wheel.Definition.MaxSpringDistance;
            if (!wheel.Definition.HasContactPoint || travel <= 0d)
            {
                continue;
            }

            Vector3d at = position
                + (lateral * wheel.OffsetUnits.X)
                + (forward * wheel.OffsetUnits.Y)
                + new Vector3d(0d, 0d, wheel.OffsetUnits.Z);
            double ground = groundHeightAt(at);
            if (!double.IsFinite(ground))
            {
                continue;
            }

            // How far the wheel's origin is UNDER the surface. 0x42EF00 casts from
            // wheelZ + travel + 4096 down to wheelZ - 2048 and takes the compression as
            // (travel + 4096) - distance, which is exactly that: nought when the surface reaches
            // the origin, and the full travel when it stands a travel above it. The origin
            // therefore rides BELOW the ground, not above -- resting it above instead sank the
            // Mission 1 truck by a compression, half the depth of its wheels.
            double sink = ground - at.Z;
            grounded = true;
            if (sink <= 0d)
            {
                continue;
            }

            double compression = Math.Min(sink, travel);
            double force = compression / travel * wheel.Definition.SpringStiffness * mass
                * GravityUnitsPerTickSquared;
            if (sink > travel)
            {
                // Bottomed: the spring keeps its full travel and stiffens by another metre's worth
                // of depth, and the body is lifted clear of the deepest of them.
                double depth = sink - travel;
                force *= 1d + (depth / WorldScene.UnitsPerMetre);
                deepest = Math.Max(deepest, depth);
            }

            spring += force;
        }

        if (!grounded)
        {
            return 0d;
        }

        motion.RiseRate += (spring - (mass * GravityUnitsPerTickSquared)) / mass;
        double rise = motion.RiseRate + deepest;
        motion.RiseRate *= LinearRetention;
        return rise;
    }

    /// <summary>A skid-steered wheel's share of its drive (<c>0x4304B0</c>).</summary>
    /// <param name="steering">The smoothed steering control, -1 to 1.</param>
    /// <param name="lateralOffset">The wheel's offset along the body's across axis.</param>
    /// <returns>1, or the eased share of the side the car turns towards.</returns>
    /// <remarks>
    /// The wheel's body side is 1 on the right (a positive across offset) and 0 on the left -- probed on
    /// Mission 7's APC and T80. Beyond half lock the inner side's share goes negative and it drives
    /// backwards, which is how a tracked vehicle turns on the spot.
    /// </remarks>
    public static double SkidSteerFactor(float steering, double lateralOffset)
    {
        bool right = lateralOffset > 0d;
        if (steering > 0.0099999998f)
        {
            return right ? 1d - (2d * steering) : 1d;
        }

        if (steering < -0.0099999998f)
        {
            return right ? 1d : 1d + (2d * steering);
        }

        return 1d;
    }

    /// <summary>Gets the largest steering deflection any of the car's wheels can reach.</summary>
    /// <param name="wheels">The car's wheels.</param>
    /// <returns>The deflection in radians, or nought when nothing steers.</returns>
    public static double MaximumSteerRadians(IReadOnlyList<CarWheel> wheels)
    {
        ArgumentNullException.ThrowIfNull(wheels);
        double most = 0d;
        for (int i = 0; i < wheels.Count; i++)
        {
            if (wheels[i].Definition.IsSteering)
            {
                most = Math.Max(most, wheels[i].Definition.MaxAngleRadians);
            }
        }

        return most;
    }

    /// <summary>The yaw inertia of the authored dimension box, in world units.</summary>
    private static double InertiaAboutUp(in CarPhysicsDefinition physics, double mass)
    {
        double across = Math.Max(0.1d, physics.Dimensions.X) * WorldScene.UnitsPerMetre;
        double along = Math.Max(0.1d, physics.Dimensions.Y) * WorldScene.UnitsPerMetre;
        return Math.Max(1d, mass * ((across * across) + (along * along)) / 12d);
    }

    /// <summary>
    /// The radius the drawn wheels roll on. The physics record has no radius -- retail reads it from
    /// each wheel task's own record -- so this takes the one measured off the wheel model, and falls
    /// back to the suspension travel only when no model size was supplied.
    /// </summary>
    private static double WheelRadiusUnits(in CarPhysicsDefinition physics, IReadOnlyList<CarWheel> wheels)
    {
        for (int i = 0; i < wheels.Count; i++)
        {
            if (wheels[i].RadiusUnits > 0f)
            {
                return wheels[i].RadiusUnits;
            }
        }

        for (int i = 0; i < physics.Wheels.Count; i++)
        {
            if (physics.Wheels[i].MaxSpringDistance > 0f)
            {
                return physics.Wheels[i].MaxSpringDistance;
            }
        }

        return WorldScene.UnitsPerMetre * 0.5d;
    }

    private static Vector3d Perpendicular(Vector3d offset, double yawRate) =>
        new(-offset.Y * yawRate, offset.X * yawRate, 0d);
}
