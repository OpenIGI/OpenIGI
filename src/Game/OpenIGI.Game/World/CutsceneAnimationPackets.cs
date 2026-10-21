using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using static OpenIGI.Game.World.DeclaredTaskValues;
using static OpenIGI.Game.World.CutscenePoses;

namespace OpenIGI.Game.World;

/// <summary>Delivers each AnimTask packet to the receiver its target's type registers.</summary>
/// <remarks>
/// Only Train, Car, Heli and Plane register <c>ReadChannels</c> handlers. Each receiver treats counter 0 as its set-up
/// -- it restores the authored state and writes its hold sentinels into the packet's channel block -- and every later
/// packet as one physics step. The steps below are those receivers, in the order the original runs them.
/// </remarks>
internal sealed class CutsceneAnimationPackets(
    CutsceneContext context,
    CutsceneVehicleBodies bodies,
    CutsceneGroundSupport ground)
{
    private readonly CutsceneContext _context = context;
    private readonly CutsceneVehicleBodies _bodies = bodies;
    private readonly CutsceneGroundSupport _ground = ground;

    /// <summary>Delivers one packet of an AnimTask's programme to its target, by the target's type.</summary>
    /// <param name="targetId">The AnimTask's target id.</param>
    /// <param name="simulationOrder">The AnimTask's place in the walk, which the pose snapshot is keyed by.</param>
    /// <param name="program">The programme, positioned at the packet to deliver.</param>
    public void Apply(int targetId, int simulationOrder, AnimDataProgram program)
    {
        if (targetId < 0 || !_context.Transforms.TryGetValue(targetId, out CutsceneTransformState? transform))
        {
            return;
        }

        GameTask? target = _context.Root.Tree.FromId(targetId);
        string type = target?.Type.Name ?? string.Empty;

        // Retail dispatch is table-driven (handler table slot*384 + typeId @ 0xA96AE0) and only
        // Train/Car/Heli/Plane register ReadChannels handlers. Human's
        // registration sub_45E450 registers none, so AnimProgram_End (0x4F4060) skips delivery:
        // an AnimTask targeting a human delivers NOTHING — no translation, no speed, no animation
        // id. Humans animate through their own state machine (state tick 0x4550B0, playback rate
        // engineDelta * 1.5 per Human_GetCutSceneDeltaTime 0x45EAE0), and their pose reaches the
        // cutscene via SetTargetPoseResolver / attachment parenting. Reproduce that here: drop
        // the packet instead of the former thrust-and-slide fallback.
        if (target is not DeclaredTask
            || !(type is "Train" or "Car" or "Heli" or "Plane"))
        {
            return;
        }

        ReadOnlySpan<float> channels = program.Channels;

        if (string.Equals(type, "Train", StringComparison.Ordinal)
            && transform.Consist is { } trainConsist)
        {
            ApplyTrain(transform, trainConsist, simulationOrder, program, channels);
            return;
        }

        float yaw = MathF.Atan2(transform.Orientation.M21, transform.Orientation.M11);
        if (string.Equals(type, "Car", StringComparison.Ordinal))
        {
            ApplyCar(target, transform, simulationOrder, program, channels);
            return;
        }
        else if (string.Equals(type, "Heli", StringComparison.Ordinal))
        {
            ApplyHeli(targetId, target, transform, simulationOrder, program);
            return;
        }
        else if (string.Equals(type, "Plane", StringComparison.Ordinal))
        {
            ApplyPlane(targetId, target, transform, simulationOrder, program, channels);
            return;
        }
    }

    /// <summary><c>Train::ReadChannels</c> (<c>0x42B040</c>): the set-up packet, or one step along the rail.</summary>
    private static void ApplyTrain(
        CutsceneTransformState transform,
        RailroadConsist trainConsist,
        int simulationOrder,
        AnimDataProgram program,
        ReadOnlySpan<float> channels)
    {
        // Train::ReadChannels (IGI.exe 0x42B040) treats tick zero as receiver setup, not a
        // physics update. It installs the channel-one hold sentinel and restores the authored
        // rail state, then returns without changing TrackPosition. The AnimTask sends tick zero
        // twice; advancing on either packet shifts every car before Jones has landed.
        if (program.Tick == 0)
        {
            program.WriteChannel(1, -2f);
            transform.Thrust = transform.OriginalThrust;
            transform.TargetThrust = transform.OriginalThrust;
            transform.Speed = transform.MaxSpeed * transform.Thrust * (4_096_000f / 108_000f);
            transform.IsDynamic = true;
            return;
        }

        SnapshotDynamicPose(transform, simulationOrder);

        // Train::ReadChannels (0x42B040): channel zero is a latched thrust target.  The live
        // thrust approaches it by the authored step, then MaxSpeed converts km/h to engine
        // units per 30 Hz tick (4096000 / 108000).  Flip negates the scalar rail advance; the
        // path evaluator turns that signed distance back into the correct tangent direction.
        if (channels[0] != -1f)
        {
            transform.TargetThrust = channels[0];
        }

        transform.Thrust = MoveTowards(transform.Thrust, transform.TargetThrust, transform.ThrustStep);
        transform.Speed = transform.MaxSpeed * transform.Thrust * (4_096_000f / 108_000f);
        transform.TrackPosition += trainConsist.Railroad.Flipped ? -transform.Speed : transform.Speed;
        if (trainConsist.TryPlace(
            transform.TrackPosition,
            transform.ConsistIndex,
            out Vector3d railPosition,
            out Matrix3f railOrientation))
        {
            transform.Position = railPosition;
            transform.Orientation = railOrientation;
        }

        transform.IsDynamic = true;
    }

    /// <summary><c>Car::ReadChannels</c> (<c>0x42EA10</c>): the set-up packet's reset, or one drive step.</summary>
    private void ApplyCar(
        GameTask? target,
        CutsceneTransformState transform,
        int simulationOrder,
        AnimDataProgram program,
        ReadOnlySpan<float> channels)
    {
        // Car::ReadChannels (IGI.exe 0x42EA10) treats the first packet as receiver setup,
        // not a physics step. Restore the authored body and install the channel hold
        // sentinels before the positive-tick callback starts smoothing controls. Running the
        // generic integration on tick zero advances a truck before its cutscene clock has
        // elapsed and leaves the receiver's channel-two state live for the next packet.
        if (program.Tick == 0)
        {
            transform.Position = transform.OriginPosition;
            transform.Orientation = transform.AuthoredOrientation;
            transform.PreviousPosition = transform.Position;
            transform.PreviousOrientation = transform.Orientation;
            transform.Velocity = default;
            transform.Speed = 0f;
            transform.Steering = 0f;
            transform.TargetSteering = 0f;

            // Car::ReadChannels seeds BOTH the live throttle and its target from the task's
            // authored thrust (+1824 and +1840 from +1856), the way Train::ReadChannels does.
            // Mission 2's truck opens its cutscene already carrying 0.6 and coasts down from
            // it; starting from nought leaves it standing there, because the authored channel
            // only ever sends the -1 hold afterwards.
            transform.Thrust = transform.OriginalThrust;
            transform.TargetThrust = transform.OriginalThrust;
            transform.ParkingBrake = false;
            transform.Heading = transform.Euler.Z;
            transform.VehicleLatchedObstacle = null;
            transform.VehicleGateReleased = false;
            transform.CarMotion.Reset();
            _bodies.ResetRetailBody(target, transform);
            program.WriteChannel(1, -1f);
            program.WriteChannel(2, -2f);
            transform.IsDynamic = true;
            return;
        }

        _bodies.DriveCar(target, transform, simulationOrder, channels[0], channels[1], channels[3]);
    }

    /// <summary>The Heli receiver (<c>0x431B70</c>): the retail body, or the set-up, a terminal hold or one flight step.</summary>
    private void ApplyHeli(
        int targetId,
        GameTask? target,
        CutsceneTransformState transform,
        int simulationOrder,
        AnimDataProgram program)
    {
        HelicopterPhysicsDefinition physics = transform.HelicopterPhysics
            ?? new HelicopterPhysicsDefinition(
                3_000f,
                new Vector3f(2.2f, 7.8f, 3f),
                new Vector3f(50f, 50f, 50f),
                new Vector3f(0.4f, 0.4f, 0.4f),
                0.027f,
                0.003f,
                200f,
                [
                    new(20f, Vector3f.Zero, Vector3f.UnitX),
                    new(3f, Vector3f.Zero, Vector3f.UnitY),
                    new(50f, Vector3f.Zero, Vector3f.UnitZ),
                ]) { MaximumThrust = 3_000f };
        if (_bodies.RetailWorld is { } retailWorld
            && target is DeclaredTask retailHeliTask
            && transform.HelicopterPhysics is { } retailHeliPhysics
            && _bodies.RetailHeliFor(retailHeliTask, transform, retailHeliPhysics) is { } retailHeli)
        {
            ApplyRetailHeli(targetId, target, transform, simulationOrder, program, retailWorld, retailHeliTask, retailHeliPhysics, retailHeli);
            return;
        }

        if (program.Tick == 0)
        {
            ResetHeli(targetId, target, transform, program, physics);
            return;
        }

        // An explicitly configured terminal landing has reached its touchdown. Keep the body
        // parked at the first terrain contact instead of feeding the next AnimTask sample back
        // through the rigid-flight integrator. Without this latch the terminal wait clears the
        // collective, but the horizontal velocity carries the airframe onto the next elevated surface.
        if (transform.TerminalLandingLatched)
        {
            HoldTerminalLanding(targetId, target, transform);
            return;
        }

        FlyHeli(targetId, target, transform, simulationOrder, program, physics);
    }

    /// <summary>One packet on a helicopter's retail body: the reset on the set-up packet, <c>Heli_Tick</c> after it.</summary>
    private void ApplyRetailHeli(
        int targetId,
        GameTask? target,
        CutsceneTransformState transform,
        int simulationOrder,
        AnimDataProgram program,
        IRetailCarWorld retailWorld,
        DeclaredTask retailHeliTask,
        HelicopterPhysicsDefinition retailHeliPhysics,
        RetailHeliBody retailHeli)
    {
        // 0x431B70: every packet latches the channels; mode 0 resets, a later packet steps Heli_Tick.
        retailHeli.Receive(program.Channels);
        if (program.Tick == 0)
        {
            RetailHeliSpawn.Reset(retailHeli, retailHeliPhysics, RetailHeliSpawn.Placement(retailHeliTask));

            program.WriteChannel(0, 0f);
            program.WriteChannel(1, 0f);
            program.WriteChannel(2, 0f);
            program.WriteChannel(3, -1f);
            program.WriteChannel(4, -2f);
            CutsceneVehicleBodies.CopyRetailPose(transform, retailHeli);
            transform.PreviousPosition = transform.Position;
            transform.PreviousOrientation = transform.Orientation;
            transform.Velocity = default;
            transform.Thrust = retailHeli.Collective;
            transform.TargetThrust = retailHeli.CollectiveTarget;
            transform.RotorPhase = 0f;
            transform.RotorPhaseTick = _context.SimulationTick;
            transform.IsDynamic = true;
            return;
        }

        SnapshotDynamicPose(transform, simulationOrder);

        // 0x4F4660 ticks the attachments -- the rotors push their lift and yaw -- right before Heli_Tick.
        retailHeli.RotorTick();
        retailHeli.Tick(retailWorld);
        CutsceneVehicleBodies.CopyRetailPose(transform, retailHeli);
        transform.Thrust = retailHeli.Collective;
        transform.TargetThrust = retailHeli.CollectiveTarget;
        transform.Speed = retailHeli.Speed;
        transform.Velocity = transform.Position - transform.PreviousPosition;
        transform.RotorPhase += transform.Thrust;
        transform.RotorPhaseTick = _context.SimulationTick;
        transform.PhysicsTicks++;
        transform.IsDynamic = true;
        _ground.LogAirframePath(targetId, "Heli", target, transform);
    }

    /// <summary>The Heli set-up packet: the authored pose and collective back, motion cleared, the hold sentinels written.</summary>
    private void ResetHeli(
        int targetId,
        GameTask? target,
        CutsceneTransformState transform,
        AnimDataProgram program,
        HelicopterPhysicsDefinition physics)
    {
        // The retail Heli live callback (0x431B70) is bidirectional: its tick-zero reset restores the
        // authored pose/collective, clears motion and controls, and overwrites the shared
        // AnimTask block with its hold sentinels. Without the channel-three write, the zeroed
        // programme block cancels Original Thrust on tick one and the opening helicopter never
        // reaches the later target-tracked flyby shot.
        transform.Position = transform.OriginPosition;
        transform.Orientation = transform.AuthoredOrientation;
        transform.Velocity = default;
        transform.Control = default;
        transform.Heading = 0f;
        transform.RotorPhase = 0f;
        transform.RotorPhaseTick = _context.SimulationTick;
        transform.AngularMomentum = default;
        transform.QueuedRotorForce = default;
        transform.QueuedRotorTorque = default;
        transform.PhysicsTicks = 0;
        transform.GroundContact = false;
        transform.GroundContactEventLogged = false;
        transform.GroundContactLogged = false;
        transform.TerminalLandingLatched = false;
        transform.TerminalLandingEventLogged = false;
        transform.TerminalLandingPosition = transform.OriginPosition;
        transform.TargetThrust = transform.Thrust;
        // A landed helicopter rests with its skids on the terrain. Retail's Heli collision
        // flag keeps the airframe from sinking through the surface while its collective ramps
        // below hover. Prefer a measurement from an authored pose that is already on a
        // support, but never measure an arbitrary in-flight pose and freeze that gap as the
        // rest height: an authored flight can reach an elevated surface while still above its
        // roof, and measuring that in-flight gap would make it stop in the air when its collective reached zero.
        double measuredRest = _ground.MeasureGroundRestOffset(transform.OriginPosition, target);
        if (CutsceneGroundSupport.IsPlausibleGroundRestOffset(measuredRest, physics))
        {
            transform.GroundRestOffset = Math.Max(measuredRest, CutsceneGroundSupport.DefaultGroundRestOffset(physics));
        }
        else if (double.IsNaN(transform.GroundRestOffset))
        {
            transform.GroundRestOffset = CutsceneGroundSupport.DefaultGroundRestOffset(physics);
            _context.Note(
                $"heli-rest-fallback id={targetId} measured={measuredRest:R} "
                + $"offset={transform.GroundRestOffset:R}");
        }
        // Cache the authored near-ground offset for contact recovery. Do not zero collective
        // here: both Mission 4 Bells follow their AnimTask controls and must still lift.
        if (!double.IsNaN(transform.GroundRestOffset))
        {
            _context.Note($"heli-park-rest id={targetId} offset={transform.GroundRestOffset:R}");
        }

        program.WriteChannel(0, 0f);
        program.WriteChannel(1, 0f);
        program.WriteChannel(2, 0f);
        program.WriteChannel(3, -1f);
        program.WriteChannel(4, -2f);
        // Rotor tasks run after their parent Heli task. Even though the tick-zero channel
        // packet only resets the body, the child rotor still queues lift for tick one.
        RetailHelicopterPhysics.QueueRotorForces(transform, transform.Orientation, physics);
        transform.IsDynamic = true;
    }

    /// <summary>Keeps a helicopter whose terminal landing has latched parked where it touched down.</summary>
    private void HoldTerminalLanding(int targetId, GameTask? target, CutsceneTransformState transform)
    {
        transform.Position = transform.TerminalLandingPosition;
        transform.PreviousPosition = transform.Position;
        transform.Velocity = default;
        transform.Speed = 0f;
        transform.Thrust = 0f;
        transform.TargetThrust = 0f;
        transform.Control = default;
        transform.AngularMomentum = default;
        transform.QueuedRotorForce = default;
        transform.QueuedRotorTorque = default;
        transform.IsDynamic = true;
        transform.PhysicsTicks++;
        _ground.LogAirframePath(targetId, "Heli", target, transform);
    }

    /// <summary>One flight step: the controls smoothed, the collective stepped, the rigid body integrated and kept above ground.</summary>
    private void FlyHeli(
        int targetId,
        GameTask? target,
        CutsceneTransformState transform,
        int simulationOrder,
        AnimDataProgram program,
        HelicopterPhysicsDefinition physics)
    {
        SnapshotDynamicPose(transform, simulationOrder);

        ReadOnlySpan<float> channels = program.Channels;
        // Heli::ReadChannels stores normalized cyclic/yaw controls; it does not integrate them as
        // Euler radians.  The rigid flight step smooths those controls, applies torque and actively
        // levels the airframe.  Treating a held 1 as one radian/second accumulated several full
        // turns during the intro and rolled the linked camera underneath the terrain.
        Vector3f targetControl = new(channels[0], channels[1], channels[2]);
        transform.Control = new Vector3f(
            ((1f - physics.Smoothing.X) * targetControl.X) + (physics.Smoothing.X * transform.Control.X),
            ((1f - physics.Smoothing.Y) * targetControl.Y) + (physics.Smoothing.Y * transform.Control.Y),
            ((1f - physics.Smoothing.Z) * targetControl.Z) + (physics.Smoothing.Z * transform.Control.Z));
        if (channels[3] != -1f)
        {
            transform.TargetThrust = channels[3];
        }

        float collectiveStep = transform.Thrust >= 0.8f
            ? physics.HighCollectiveStep
            : physics.LowCollectiveStep;
        transform.Thrust = MoveTowards(transform.Thrust, transform.TargetThrust, collectiveStep);
        if (transform.Thrust < 0.001f && transform.TargetThrust == 0f)
        {
            transform.Thrust = 0f;
        }
        transform.RotorPhase += transform.Thrust;
        transform.RotorPhaseTick = _context.SimulationTick;
        if (target is DeclaredTask liveHeli)
        {
            transform.CollisionEnabled = BoolStrict(liveHeli, "Collision detection");
        }

        RetailHelicopterPhysics.Apply(transform, physics);
        transform.Position += transform.Velocity;
        if (target is not null)
        {
            _ground.KeepHelicopterAboveGround(target, transform);
        }
        transform.PhysicsTicks++;
        transform.IsDynamic = true;
        _ground.LogAirframePath(targetId, "Heli", target, transform);
    }

    /// <summary>The Plane receiver (<c>0x4337E0</c>): the retail body, or the set-up or one flight step.</summary>
    private void ApplyPlane(
        int targetId,
        GameTask? target,
        CutsceneTransformState transform,
        int simulationOrder,
        AnimDataProgram program,
        ReadOnlySpan<float> channels)
    {
        if (_bodies.RetailWorld is { } planeWorld
            && target is DeclaredTask retailPlaneTask
            && transform.PlanePhysics is { } retailPlanePhysics
            && _bodies.RetailPlaneFor(retailPlaneTask, transform, retailPlanePhysics) is { } retailPlane)
        {
            ApplyRetailPlane(targetId, target, transform, simulationOrder, program, planeWorld, retailPlaneTask, retailPlanePhysics, retailPlane);
            return;
        }

        if (program.Tick == 0)
        {
            ResetPlane(transform);
            return;
        }

        FlyPlane(targetId, target, transform, simulationOrder, channels);
    }

    /// <summary>One packet on a plane's retail body: the reset on the set-up packet, <c>Plane_Physics</c> after it.</summary>
    private void ApplyRetailPlane(
        int targetId,
        GameTask? target,
        CutsceneTransformState transform,
        int simulationOrder,
        AnimDataProgram program,
        IRetailCarWorld planeWorld,
        DeclaredTask retailPlaneTask,
        PlanePhysicsDefinition retailPlanePhysics,
        RetailPlaneBody retailPlane)
    {
        // 0x4337E0: every packet latches the channels; mode 0 resets, a later packet steps Plane_Physics
        // (0x433B50) and the gear legs (0x4F4660).
        retailPlane.Receive(program.Channels);
        if (program.Tick == 0)
        {
            RetailPlaneSpawn.Reset(retailPlane, retailPlanePhysics, RetailPlaneSpawn.Placement(retailPlaneTask));
            CutsceneVehicleBodies.CopyRetailPose(transform, retailPlane);
            transform.PreviousPosition = transform.Position;
            transform.PreviousOrientation = transform.Orientation;
            transform.Velocity = default;
            transform.Thrust = retailPlane.Thrust;
            transform.TargetThrust = retailPlane.ThrustTarget;
            transform.IsDynamic = true;
            return;
        }

        SnapshotDynamicPose(transform, simulationOrder);
        retailPlane.Tick(planeWorld);
        CutsceneVehicleBodies.CopyRetailPose(transform, retailPlane);
        transform.Thrust = retailPlane.Thrust;
        transform.TargetThrust = retailPlane.ThrustTarget;
        transform.Speed = retailPlane.Speed;
        transform.Velocity = transform.Position - transform.PreviousPosition;
        transform.PhysicsTicks++;
        transform.IsDynamic = true;
        _ground.LogAirframePath(targetId, "Plane", target, transform);
    }

    /// <summary>The Plane set-up packet: the authored rigid-body pose back and no movement published.</summary>
    private static void ResetPlane(CutsceneTransformState transform)
    {
        // The receiver's two tick-zero packets restore the authored rigid-body pose and
        // publish no movement. Its positive-tick collision/body integration is Plane-owned:
        // 0x433B50 calls the contact path at 0x434310 and shared body step at 0x4FC0E0.
        // A Heli terrain-rest clamp would therefore turn a fly-by into a terrain follower.
        transform.Position = transform.OriginPosition;
        transform.Orientation = transform.AuthoredOrientation;
        transform.Velocity = default;
        transform.Control = default;
        transform.Heading = 0f;
        transform.Speed = 0f;
        transform.Thrust = 0f;
        transform.TargetThrust = 0f;
        transform.GroundContact = false;
        transform.GroundContactEventLogged = false;
        transform.GroundContactLogged = false;
        transform.IsDynamic = true;
    }

    /// <summary>One generic flight step: the controls retained, the thrust stepped and the body flown along its forward axis.</summary>
    private void FlyPlane(
        int targetId,
        GameTask? target,
        CutsceneTransformState transform,
        int simulationOrder,
        ReadOnlySpan<float> channels)
    {
        SnapshotDynamicPose(transform, simulationOrder);

        // Plane_ReadChannels (0x4337E0) publishes pitch/roll/yaw targets and a latched thrust
        // target.  Plane_Physics (0x433B50) retains those targets with config +8/+12/+16:
        // the shipped SU-27 values are all 0.9.  Do not substitute the former 0.12 guess.
        Vector3f targetControl = new(channels[0], channels[1], channels[2]);
        if (transform.PlanePhysics is { } planePhysics)
        {
            Vector3f retention = planePhysics.ControlRetention;
            transform.Control = new Vector3f(
                (retention.X * transform.Control.X) + ((1f - retention.X) * targetControl.X),
                (retention.Y * transform.Control.Y) + ((1f - retention.Y) * targetControl.Y),
                (retention.Z * transform.Control.Z) + ((1f - retention.Z) * targetControl.Z)) * 0.995f;
        }
        else
        {
            // Unit-level runtimes may deliberately omit the retail registry. Keep their
            // pre-existing generic motion path isolated from the retail-game implementation.
            transform.Control += (targetControl - transform.Control) * 0.12f;
        }
        if (channels[3] != -1f)
        {
            transform.TargetThrust = channels[3];
        }
        if (target is DeclaredTask livePlane)
        {
            transform.CollisionEnabled = BoolStrict(livePlane, "Collision detection");
        }
        // Plane_Physics selects config +32 after thrust reaches 0.8 and config +36 before it.
        // SU-27 supplies 0.054 / 0.006.  The old fixed 0.001 made the Level 3 takeoff spend
        // most of its authored flight window spooling on the runway.
        float thrustStep = transform.PlanePhysics is { } physics
            ? transform.Thrust >= 0.8f ? physics.HighThrustStep : physics.LowThrustStep
            : 0.001f;
        transform.Thrust = MoveTowards(transform.Thrust, transform.TargetThrust, thrustStep);
        transform.Heading += transform.Control.Z * 0.004f;
        transform.Orientation = Matrix3f.FromEngineEulerAngles(
            transform.Control.X * 0.10f,
            transform.Control.Y * 0.18f,
            transform.Heading) * transform.AuthoredOrientation;
        // Plane_Physics adds thrust on body-local +Y. The shared body integrator converts local
        // acceleration to world space by dotting each orientation row with that local vector,
        // which yields Column2. Mission 3's gamma=3pi/2 takeoff therefore travels +X down the
        // runway; Row2 is the inverse direction and visibly makes the SU-27 fly tail-first.
        Vector3f planeForward = transform.Orientation.Column2;
        float desiredSpeed = Math.Max(0f, transform.TargetThrust) * 1_500f;
        transform.Speed += (desiredSpeed - transform.Speed) * 0.025f;
        Vector3d previousPosition = transform.Position;
        transform.Position += new Vector3d(planeForward.X, planeForward.Y, planeForward.Z)
            * transform.Speed;
        transform.Velocity = transform.Position - previousPosition;
        transform.IsDynamic = true;
        _ground.LogAirframePath(targetId, "Plane", target, transform);
    }

    private static float MoveTowards(float value, float target, float step)
    {
        if (step <= 0f || value == target)
        {
            return target;
        }

        return value < target ? Math.Min(value + step, target) : Math.Max(value - step, target);
    }
}
