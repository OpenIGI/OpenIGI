using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using static OpenIGI.Game.World.CutscenePoses;

namespace OpenIGI.Game.World;

/// <summary>Poses what the renderer draws of a moving task: the task itself, its expanded placements, its wheels and its rotors.</summary>
/// <remarks>
/// Every read is presentation only: it derives a consist car or an attachment from the pose the simulation left, and
/// never steps anything. A rotor's blur samples follow <c>RotorPrimitive::Draw</c> (<c>0x42C630</c>).
/// </remarks>
internal sealed class CutsceneVehicleDraw(
    CutsceneContext context,
    CutsceneAttachments attachments,
    CutsceneVehicleBodies bodies,
    CutsceneVehicleArmament armament,
    CutsceneVehicleParts parts)
{
    private readonly CutsceneVehicleParts _parts = parts;
    private readonly CutsceneContext _context = context;
    private readonly CutsceneAttachments _attachments = attachments;
    private readonly CutsceneVehicleBodies _bodies = bodies;
    private readonly CutsceneVehicleArmament _armament = armament;

    /// <summary>Resolves an animation-driven task transform in render units.</summary>
    public bool TryGetDrawTransform(GameTask task, out Vector3d position, out Matrix3f orientation)
    {
        if (_attachments.TryEvaluateAttachment(task, out Vector3d attachedPosition, out orientation))
        {
            position = attachedPosition / WorldScene.UnitsPerMetre;
            return true;
        }

        if (_context.TaskTransforms.TryGetValue(task, out CutsceneTransformState? state))
        {
            UpdateDerivedPose(state);

            position = state.Position / WorldScene.UnitsPerMetre;
            orientation = state.Orientation;
            // Child Train tasks have no authored world position: even before the engine moves, their
            // derived consist placement must replace WorldScene's position-zero fallback.
            // Collision-parked helis also publish the rested pose so the hull is not left at the
            // authored float height while its container is still closed.
            return state.IsDynamic || state.TrainRoot is not null || state.CollisionEnabled;
        }

        position = default;
        orientation = Matrix3f.Identity;
        return false;
    }

    /// <summary>Reads the latest integrated velocity of a dynamic cutscene vehicle.</summary>
    public bool TryGetDynamicVelocity(GameTask task, out Vector3d velocity)
    {
        if (_context.TaskTransforms.TryGetValue(task, out CutsceneTransformState? state) && state.IsDynamic)
        {
            velocity = state.Velocity / WorldScene.UnitsPerMetre;
            return true;
        }

        velocity = default;
        return false;
    }

    /// <summary>Applies a moving task's rigid transform to one of its authored model placements.</summary>
    /// <remarks>
    /// A retail vehicle task expands into a root model plus all of its magic-object attachments. Those
    /// placements share an owner but not a transform. Replacing each placement with the task origin
    /// collapses rotors, wheels and cabin fittings into one point; this preserves each placement's local
    /// offset and orientation while the owning task moves.
    /// </remarks>
    public bool TryGetPlacementDrawTransform(
        GameTask task,
        Vector3d placementPosition,
        Matrix3f placementOrientation,
        out Vector3d position,
        out Matrix3f orientation)
    {
        Vector3d originPosition;
        Matrix3f originOrientation;
        Vector3d currentPosition;
        Matrix3f currentOrientation;

        if (_attachments.TryGet(task, out CutsceneAttachmentState? attachment))
        {
            if (!_attachments.TryEvaluateAttachment(task, out currentPosition, out currentOrientation))
            {
                position = default;
                orientation = Matrix3f.Identity;
                return false;
            }
            originPosition = attachment.Position;
            originOrientation = attachment.Orientation;
        }
        else if (_context.TaskTransforms.TryGetValue(task, out CutsceneTransformState? state))
        {
            UpdateDerivedPose(state);
            if (!state.IsDynamic && state.TrainRoot is null && !state.CollisionEnabled)
            {
                position = default;
                orientation = Matrix3f.Identity;
                return false;
            }
            originPosition = state.OriginPosition;
            originOrientation = state.AuthoredOrientation;
            currentPosition = state.Position;
            currentOrientation = state.Orientation;
        }
        else
        {
            position = default;
            orientation = Matrix3f.Identity;
            return false;
        }

        Vector3d placementUnits = placementPosition * WorldScene.UnitsPerMetre;
        Vector3d relative = originOrientation.Transform(placementUnits - originPosition);
        Vector3d moved = currentPosition + currentOrientation.Transposed().Transform(relative);
        position = moved / WorldScene.UnitsPerMetre;
        orientation = currentOrientation * originOrientation.Transposed() * placementOrientation;
        return true;
    }

    /// <summary>Applies vehicle motion and the first Rotor draw sample to an expanded placement.</summary>
    public bool TryGetPlacementDrawTransform(
        WorldPlacement placement,
        out Vector3d position,
        out Matrix3f orientation)
    {
        if (TryGetRotorDrawState(placement, out RotorDrawState rotorDraw))
        {
            position = rotorDraw.Position;
            orientation = rotorDraw.GetOrientation(0);
            return true;
        }

        if (_armament.TryGetGunMountDraw(placement, out position, out orientation))
        {
            return true;
        }

        // A plane's legs fold, its hatch shuts, its canopy swings and its control surfaces throw, each about its attachment
        // (0x435A80, 0x435F60, 0x435300, 0x4362C0).
        if (_parts.TryGetPlanePartDraw(placement, out position, out orientation))
        {
            return true;
        }

        if (!TryGetPlacementDrawTransform(
            placement.Owner,
            placement.Position,
            placement.Orientation,
            out position,
            out orientation))
        {
            return false;
        }

        if (TryGetWheelDraw(placement, out Matrix3f spun))
        {
            orientation *= spun;
        }

        // A sliding door (0x4274E0) sits at its attachment moved along the helicopter's axis by how far it has slid.
        if (_parts.TryGetDoorSlide(placement, out Vector3d slide)
            && _context.TaskTransforms.TryGetValue(placement.Owner, out CutsceneTransformState? state))
        {
            position += state.Orientation.Transposed().Transform(slide) / WorldScene.UnitsPerMetre;
        }

        return true;
    }

    /// <summary>Builds the roll and steer a car's drawn wheel carries this tick.</summary>
    /// <param name="placement">The candidate placement.</param>
    /// <param name="rotation">Receives the wheel's own rotation, in its placement frame.</param>
    /// <returns><see langword="false"/> when the placement is not a car wheel.</returns>
    /// <remarks>
    /// A wheel rolls about its across axis by the distance the body has covered over the wheel
    /// radius, and a steered wheel turns about the body's up axis by
    /// <c>MaximumSteerDegrees * pi/180 * steering</c> -- the same angle <c>0x430D10</c> builds the
    /// drive direction with. Without this the wheels are welded to the hull and the truck slides
    /// along on frozen tyres.
    /// </remarks>
    public bool TryGetWheelDraw(WorldPlacement placement, out Matrix3f rotation)
    {
        ArgumentNullException.ThrowIfNull(placement);
        rotation = Matrix3f.Identity;
        if (!string.Equals(placement.MagicTaskTypeName, CutsceneVehicleBodies.WheelTaskTypeName, StringComparison.Ordinal)
            || !_bodies.CarWheels.TryGetValue(placement.Owner, out CarWheel[]? wheels)
            || !_context.TaskTransforms.TryGetValue(placement.Owner, out CutsceneTransformState? state))
        {
            return false;
        }

        bool steerable = _bodies.CarWheelIndices.TryGetValue(placement, out int index)
            && index < wheels.Length
            && wheels[index].Definition.IsSteering;
        // Retail's accumulator counts DOWN as the car goes forward (Wheel::Tick subtracts the spin
        // rate), and it is applied against the wheel's own axle axis, which points the opposite way
        // from the placement frame's X here. Rolling by the raw accumulator spins the tyres
        // backwards while the truck drives forwards.
        rotation = Matrix3f.RotationX((float)-state.CarMotion.WheelRoll);
        if (steerable && state.CarMotion.WheelSteer != 0d)
        {
            rotation = Matrix3f.RotationZ((float)state.CarMotion.WheelSteer) * rotation;
        }

        return true;
    }

    /// <summary>Builds the repeated orientations emitted by retail <c>RotorPrimitive::Draw</c>.</summary>
    public bool TryGetRotorDrawState(WorldPlacement placement, out RotorDrawState draw)
    {
        if (!placement.IsRotor
            || _context.VehiclePhysics?.TryGetRotor(placement.Instance.Name, out RotorPhysicsDefinition rotor) != true
            || !_context.TaskTransforms.TryGetValue(placement.Owner, out CutsceneTransformState? state)
            || !TryGetPlacementDrawTransform(
                placement.Owner,
                placement.Position,
                placement.Orientation,
                out Vector3d position,
                out Matrix3f baseOrientation))
        {
            draw = default;
            return false;
        }

        // Rotor::Tick (0x42D440) advances its private phase by
        // BladeSamples * PhaseStep * collective. RotorPrimitive::Draw (0x42C630) then converts that
        // accumulator with the engine's 21.362831 factor and emits BladeSamples copies, separated by
        // PhaseStep * collective. The repeated blur meshes are why the main and tail rotors look like
        // discs in retail instead of the single frozen blade that the initial renderer produced.
        // A terminal landing parks the physical airframe and clears its live thrust/phase. The
        // authored helicopter still has its visible rotor presentation in retail/editor previews,
        // though: keep a draw-only phase running from the original collective while the body remains
        // stationary. This does not feed lift or torque back into the landed physics state.
        bool previewOnly = !state.IsDynamic || state.TerminalLandingLatched;
        float visualCollective = previewOnly && state.OriginalThrust > 0f
            ? Math.Clamp(state.OriginalThrust, 0f, 1f)
            : state.Thrust;
        float firstAngle;
        if (previewOnly && visualCollective > 0f)
        {
            // The editor preview advances at the visible retail rates (15 rad/s for the main disc,
            // 25 rad/s in the opposite direction for a tail disc). A parked/terminal Heli has no
            // live physics phase to consume, so use the runtime tick as the presentation clock.
            float angularSpeed = rotor.IsTailRotor ? -25f : 15f;
            firstAngle = (_context.SimulationTick / 30f) * angularSpeed * visualCollective;
        }
        else
        {
            // Rotor tasks keep ticking when the body's AnimTask stops emitting channels.
            // Extend the last physics phase through those idle ticks without moving the body.
            float phase = state.TerminalLandingLatched
                ? state.PhysicsTicks * visualCollective
                : state.RotorPhase + (Math.Max(0, _context.SimulationTick - state.RotorPhaseTick) * visualCollective);
            float accumulatedPhase = phase * rotor.PhasePerCollectiveTick;
            firstAngle = accumulatedPhase * 21.362831f;
        }
        float sampleStep = rotor.PhaseStep * visualCollective;

        float maximumTilt = rotor.MaximumTilt;
        float tiltCollective = previewOnly ? visualCollective : state.Thrust;
        float tilt = -Math.Clamp(1f - tiltCollective, -0.1f, 1f) * maximumTilt;

        // The second half of the flap is the airframe's AXIAL speed: the component of the body
        // velocity along its own vertical. 0x42C630 reads that vertical out of the Heli's live
        // matrix at config +120/+132/+144 — the third column of the +112 basis, the same body-up
        // vector Rotor::Tick already uses for lift. Column1 is the body's sideways axis, so the
        // flap followed lateral drift instead of climb and descent.
        //
        // 0.026367188 is 108/4096: it turns the velocity's units-per-tick into km/h, the unit the
        // record's maximum speed is authored in. Retail divides by that speed times 0.4 (80 km/h
        // for both shipped families), so the blade needs a sustained climb to reach the authored
        // five degrees. Dividing by the live collective instead made the term hundreds of times
        // too large: any movement pinned the disc at the full deflection and flipped its sign with
        // the drift, which is what turned the blur samples into an umbrella and then inverted it.
        float referenceSpeed = state.HelicopterPhysics?.MaximumSpeed ?? 0f;
        if (maximumTilt != 0f && referenceSpeed != 0f)
        {
            Vector3f vertical = state.Orientation.Column3;
            float axialVelocity =
                (vertical.X * (float)state.Velocity.X)
                + (vertical.Y * (float)state.Velocity.Y)
                + (vertical.Z * (float)state.Velocity.Z);
            tilt -= axialVelocity * 0.026367188f * maximumTilt / (referenceSpeed * 0.4f);
        }

        tilt = Math.Clamp(tilt, -maximumTilt, maximumTilt);

        draw = new RotorDrawState(
            position,
            baseOrientation,
            Math.Max(0, rotor.BladeSamples),
            firstAngle,
            sampleStep,
            tilt);
        return true;
    }
}
