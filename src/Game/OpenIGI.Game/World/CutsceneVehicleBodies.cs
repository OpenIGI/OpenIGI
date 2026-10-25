using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Physics;
using static OpenIGI.Game.World.CutscenePoses;
using static OpenIGI.Game.World.DeclaredTaskValues;

namespace OpenIGI.Game.World;

/// <summary>The cutscene's vehicles as bodies: their wheels, their retail rigid bodies and a car's drive for a tick.</summary>
/// <remarks>
/// A car's controller -- an AnimTask or a CarAI -- sends it channel packets; <see cref="DriveCar"/> is the receiver
/// both arrive through (<c>0x42EA10</c>). A car with a retail body steps <c>Car_Tick</c> on it, and the planes and
/// helicopters get theirs here the first time their AnimTask speaks.
/// </remarks>
internal sealed class CutsceneVehicleBodies(
    CutsceneContext context,
    CutsceneGroundSupport ground,
    Func<GameTask, Vector3d, Vector3d, (GameTask Owner, WorldRayHit Hit, string Label)?>? vehicleObstacleHit,
    Func<GameTask, bool>? vehicleObstacleReleased)
{
    /// <summary>The magic-object task type retail gives a car's rolling wheels.</summary>
    internal const string WheelTaskTypeName = "TASKTYPE_WHEEL";

    /// <summary>The two heights the vehicle obstacle probe sweeps, in metres above the body origin.</summary>
    private static readonly double[] CarProbeHeightsMetres = [0.75d, 1.95d];

    /// <summary>How far past the proposed step the obstacle probe reaches, in world units.</summary>
    private const double CarProbeMarginUnits = 128d;

    /// <summary>
    /// The record a car falls back to when its model has no <c>physicsobj</c> entry: the shipped
    /// truck's mass and dimensions with a single driven, unsteered wheel at the origin.
    /// </summary>
    private static readonly CarPhysicsDefinition DefaultCarPhysics = new(
        3_000f,
        new Vector3f(2.2f, 7.8f, 3f),
        0,
        [],
        0.95f,
        5_000f,
        0.95f,
        0,
        [new CarWheelTypeDefinition(false, true, 0f, 0, (float)WorldScene.UnitsPerMetre * 0.3f, 0.5f)],
        string.Empty,
        []);

    private readonly CutsceneContext _context = context;
    private readonly CutsceneGroundSupport _ground = ground;
    private readonly Func<GameTask, Vector3d, Vector3d, (GameTask Owner, WorldRayHit Hit, string Label)?>? _vehicleObstacleHit = vehicleObstacleHit;
    private readonly Func<GameTask, bool>? _vehicleObstacleReleased = vehicleObstacleReleased;
    private readonly Dictionary<GameTask, CarWheel[]> _carWheels = [];
    private readonly Dictionary<WorldPlacement, int> _carWheelIndices = [];
    private readonly List<RetailRayModel> _retailVehicleModels = [];
    private readonly Dictionary<GameTask, RetailRayModel?> _standingCars = [];
    private IRetailCarWorld? _retailCarWorld;
    private Func<string, OpenIGI.Formats.World.MefModel?>? _retailCarModels;
    private Func<string, MagicObjectDefinition?>? _retailMagic;

    /// <summary>Gets the world the cars' rays are cast into, once <see cref="ConfigureRetailCars"/> has supplied it.</summary>
    public IRetailCarWorld? RetailWorld => _retailCarWorld;

    /// <summary>Gets each car's bound wheels, in the order its physics record lists them.</summary>
    public IReadOnlyDictionary<GameTask, CarWheel[]> CarWheels => _carWheels;

    /// <summary>Gets each drawn wheel placement's index into its car's wheels.</summary>
    public IReadOnlyDictionary<WorldPlacement, int> CarWheelIndices => _carWheelIndices;

    /// <summary>Every live vehicle's sound inputs, as its update left them this tick.</summary>
    /// <returns>One entry per active, undestroyed Car, Heli, Plane or Train car.</returns>
    /// <remarks>
    /// A plane instantiates <c>su27</c> while its "Play sound" holds (<c>0x433690</c>), as a helicopter does
    /// <c>helicopter_1</c> (<c>0x4316D0</c>). Both answer their sounds' parameter query with a two-entry array
    /// (<c>0x434A30</c>, <c>0x432BB0</c>; laid out by <c>0x4E6AB0</c> as a count, then the values) that their
    /// physics step fills after each step (<c>0x433B50</c>, <c>0x431E30</c>): parameter 0 the thrust (<c>+1824</c>),
    /// parameter 1 whether it is above 0.01. <c>plane_01</c> reads parameter 1, so a jet sounds at its curves'
    /// "on" point whenever its engine is running.
    /// <para>
    /// Every car of a train sounds too (<c>0x42B700</c>): <c>train_loop_1</c> for a locomotive pulling wagons,
    /// <c>train_loop_2</c> for anything else, unless its "Play Sound" expression reads 0 -- and no IGI1 level writes one.
    /// Its two parameters are its locomotive's thrust five times over, held to 0.8..1, and whether that thrust is over a
    /// thousandth.
    /// </para>
    /// </remarks>
    public IEnumerable<VehicleSoundSource> VehicleSoundSources()
    {
        foreach ((GameTask task, CutsceneTransformState state) in _context.TaskTransforms)
        {
            if (task is not DeclaredTask vehicle
                || vehicle.Type.Name is not ("Car" or "Heli" or "Plane" or "Train")
                || !ConditionalContainerGate.IsTaskActive(vehicle)
                || _context.Destructibles?.IsDestroyed(vehicle) == true)
            {
                continue;
            }

            if (vehicle.Type.Name == "Train")
            {
                yield return TrainSound(vehicle, state);
                continue;
            }

            bool helicopter = vehicle.Type.Name == "Heli";
            bool plane = vehicle.Type.Name == "Plane";
            string set = helicopter
                ? "helicopter_1"
                : plane
                    ? "su27"
                    : _context.VehiclePhysics?.TryGetCar(TextOrEmpty(vehicle, WorldScene.ModelParameter), out CarPhysicsDefinition car) == true
                        ? car.EngineSoundSet
                        : string.Empty;
            string gate = TextOrEmpty(vehicle, "Play sound");
            bool on = string.IsNullOrWhiteSpace(gate) || _context.Expressions.EvaluateCondition(gate, emptyValue: true, vehicle);
            float throttle = helicopter
                ? state.RetailHeli?.Collective ?? 0f
                : plane
                    ? state.RetailPlane?.Thrust ?? 0f
                    : state.RetailBody?.Throttle ?? 0f;
            float speed = helicopter || plane ? 0f : Math.Min((state.RetailBody?.Speed ?? 0f) * 0.001f, 1f);
            yield return new VehicleSoundSource(
                vehicle,
                set,
                state.Position,
                throttle,
                throttle > 0.01f ? 1f : 0f,
                speed,
                on);
        }
    }

    private VehicleSoundSource TrainSound(DeclaredTask car, CutsceneTransformState state)
    {
        bool pullsWagons = car.Children.Any(child => child.Type.Name == "Train");
        string gate = TextOrEmpty(car, "Play Sound");
        bool on = string.IsNullOrWhiteSpace(gate) || _context.Expressions.EvaluateCondition(gate, emptyValue: true, car);
        float thrust = (state.TrainRoot ?? state).Thrust;
        double level = thrust * 5.0;
        float pitch = (float)(level < 0.800000011920929 ? 0.800000011920929 : Math.Min(level, 1.0));
        return new VehicleSoundSource(
            car,
            pullsWagons ? "train_loop_1" : "train_loop_2",
            state.Position,
            pitch,
            thrust > 0.0010000000474974513f ? 1f : 0f,
            0f,
            on);
    }

    /// <summary>Gets every vehicle in the world at its current origin, as CRITERIA_VEHICLE counts them.</summary>
    /// <remarks>
    /// <c>0x4F4560</c> walks the global vehicle list (<c>dword_A76C40</c>) and tests each vehicle's origin (<c>+32</c>):
    /// every Car and Heli, whatever drives it. Mission 7's escape truck is an AnimTask's, and the areas it drives
    /// through -- the timer that releases it past the checkpoint and the two gates that open for it -- only ever
    /// counted the CarAI cars.
    /// <para>
    /// A vehicle joins the list when it is created (<c>0x42E180</c>) and leaves it only when it is destroyed
    /// (<c>0x42E940</c>); deactivation (<c>0x4F44A0</c>) clears its active flag and leaves it listed. So a vehicle
    /// behind a closed ConditionalContainer still counts where it stands: mission 7's ending hides the truck under
    /// CutScene 1200, and the truck in AreaActivate 177 is what keeps that scene's container open.
    /// </para>
    /// </remarks>
    public IReadOnlyList<VehicleOccupant> VehicleOccupants()
    {
        List<VehicleOccupant> occupants = [];
        foreach ((GameTask task, CutsceneTransformState state) in _context.TaskTransforms)
        {
            if (task.Type.Name is "Car" or "Heli")
            {
                occupants.Add(new VehicleOccupant(task.Id, state.Position));
            }
        }

        return occupants;
    }

    /// <summary>Runs every car on the original's rigid body, in the world its wheel rays are cast into.</summary>
    /// <param name="world">The world the cars' rays query.</param>
    /// <param name="models">Resolves a car's model by name, for its body points and radius.</param>
    /// <remarks>
    /// Each car gets a <see cref="RetailCarBody"/> at its controller's set-up packet -- the reset
    /// <c>0x42EA10</c> performs -- and from then on every physics packet steps it through <c>Car_Tick</c>
    /// (<c>0x42ECA0</c>) instead of the planar <see cref="CarDynamics"/> model. A car whose model or type is
    /// missing stays on the planar model.
    /// </remarks>
    /// <param name="magic">Resolves a magic object by name, for a helicopter's skids and rotors; optional.</param>
    public void ConfigureRetailCars(
        IRetailCarWorld world,
        Func<string, OpenIGI.Formats.World.MefModel?> models,
        Func<string, MagicObjectDefinition?>? magic = null)
    {
        _retailCarWorld = world ?? throw new ArgumentNullException(nameof(world));
        _retailCarModels = models ?? throw new ArgumentNullException(nameof(models));
        _retailMagic = magic;

        // Mission 8's intro truck is reset by its AnimTask while the runtime is constructed; without this
        // it never gets a body and drives the planar model through the whole scene.
        foreach ((GameTask task, CutsceneTransformState state) in _context.TaskTransforms)
        {
            if (state.RetailResetPending)
            {
                ResetRetailBody(task, state);
            }
        }
    }

    /// <summary>
    /// Binds every car's drawn wheels to its physics record, in the order both are authored in.
    /// </summary>
    /// <param name="scene">The built world whose placements carry the wheel attachments.</param>
    /// <remarks>
    /// A car's wheels arrive as <c>TASKTYPE_WHEEL</c> magic-object attachments, one placement each,
    /// in the same order as the record's wheel entries -- on the Mission 1 truck that pairing puts
    /// both steered, driven entries exactly on the two front wheels. Their offsets from the body
    /// origin are what the drive force and the tyre torque are applied at, so the physics reads them
    /// from the model rather than guessing an axle layout.
    /// </remarks>
    /// <param name="modelRadius">
    /// Resolves a wheel model's radius in world units, for the rolling animation. Optional: without
    /// it the wheels still turn, just at the suspension travel rather than their real size.
    /// </param>
    public void BindVehicleWheels(WorldScene scene, Func<string, float>? modelRadius = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _carWheels.Clear();
        _carWheelIndices.Clear();
        Dictionary<GameTask, List<WorldPlacement>> byOwner = [];
        foreach (WorldPlacement placement in scene.Placements)
        {
            if (!string.Equals(placement.MagicTaskTypeName, WheelTaskTypeName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!byOwner.TryGetValue(placement.Owner, out List<WorldPlacement>? list))
            {
                list = [];
                byOwner[placement.Owner] = list;
            }

            list.Add(placement);
        }

        foreach ((GameTask owner, List<WorldPlacement> placements) in byOwner)
        {
            if (!_context.TaskTransforms.TryGetValue(owner, out CutsceneTransformState? state)
                || state.CarPhysics is not { } physics)
            {
                continue;
            }

            // Where each drawn wheel sits in the body's own axes.
            Vector3d[] drawn = new Vector3d[placements.Count];
            double acrossTotal = 0d;
            for (int i = 0; i < placements.Count; i++)
            {
                Vector3d placementUnits = placements[i].Position * WorldScene.UnitsPerMetre;
                drawn[i] = state.AuthoredOrientation.Transform(placementUnits - state.OriginPosition);
                acrossTotal += Math.Abs(drawn[i].X);
            }

            double acrossMagnitude = placements.Count > 0 ? acrossTotal / placements.Count : 0d;

            // Forces are applied at the WHEEL BODIES, of which there are fewer than drawn wheels: a
            // tandem rear pair shares one. Each body sits at the outermost drawn wheel that refers
            // to it, and an axle is symmetric about the body's centreline, so the across offset is
            // the mean of the drawn magnitudes rather than the wheel model's own origin. Reading a
            // running game back confirms both: retail's four bodies on the Mission 1 truck are at
            // (-0.79, +2.81), (-0.79, -2.29), (+0.85, +2.81) and (+0.85, -2.29) metres, which this
            // reproduces to within five centimetres.
            Dictionary<int, Vector3d> bodyOffsets = [];
            for (int i = 0; i < placements.Count && i < physics.Wheels.Count; i++)
            {
                CarWheelTypeDefinition entry = physics.Wheels[i];
                int slot = entry.ContactPoint >= 0 ? entry.ContactPoint : -10 - entry.ContactPoint;
                Vector3d candidate = new(
                    Math.Sign(drawn[i].X) * acrossMagnitude,
                    drawn[i].Y,
                    drawn[i].Z);
                if (!bodyOffsets.TryGetValue(slot, out Vector3d held)
                    || Math.Abs(candidate.Y) > Math.Abs(held.Y))
                {
                    bodyOffsets[slot] = candidate;
                }
            }

            int contacts = 0;
            for (int i = 0; i < physics.Wheels.Count; i++)
            {
                if (physics.Wheels[i].HasContactPoint)
                {
                    contacts++;
                }
            }

            CarWheel[] wheels = new CarWheel[placements.Count];
            for (int i = 0; i < placements.Count; i++)
            {
                CarWheelTypeDefinition definition = i < physics.Wheels.Count
                    ? physics.Wheels[i]
                    : new CarWheelTypeDefinition(false, false, 0f, -1, 0f, 0f);
                int slot = definition.ContactPoint >= 0 ? definition.ContactPoint : -10 - definition.ContactPoint;
                Vector3d offset = bodyOffsets.TryGetValue(slot, out Vector3d bodyOffset)
                    ? bodyOffset
                    : drawn[i];

                // Where the spring is probed from. Retail keeps that point on the wheel body, which
                // is model data the port has no reader for, so it is placed by what it has to
                // achieve: the spring settles with its origin a compression UNDER the surface, and
                // the tyre has to end up ON the surface, so the origin sits a compression below the
                // contact patch. Measuring the running game: its Mission 2 truck rests 0.037 m over
                // the terrain, which puts the bottom of a 0.464 m wheel 0.023 m clear -- touching.
                double radius = Math.Max(0d, modelRadius?.Invoke(placements[i].Instance.Name) ?? 0d);
                double travel = definition.MaxSpringDistance;
                double stiffness = definition.SpringStiffness;
                double settled = contacts > 0 && stiffness > 0d
                    ? Math.Min(travel, travel / (contacts * stiffness))
                    : 0d;
                offset = new Vector3d(offset.X, offset.Y, drawn[i].Z - radius - settled);
                PhysicsContactPointType? body = null;
                for (int b = 0; b < physics.Bodies.Count; b++)
                {
                    if (physics.Bodies[b].MagicVertex == slot)
                    {
                        body = physics.Bodies[b];
                        break;
                    }
                }

                wheels[i] = new CarWheel(
                    definition,
                    offset,
                    body?.FrictionX ?? 0.95f,
                    body?.FrictionY ?? 0.05f,
                    (float)radius);
            }

            _carWheels[owner] = wheels;
            for (int i = 0; i < placements.Count; i++)
            {
                _carWheelIndices[placements[i]] = i;
            }
        }
    }

    /// <summary>Clamps a record retention into the range the native blend assumes.</summary>
    private static float CarRetention(float? retention) =>
        retention is { } value && float.IsFinite(value) && value is >= 0f and <= 1f ? value : 0.95f;

    /// <summary>Drops an axis into the ground plane, falling back when it is degenerate.</summary>
    private static Vector3d FlattenAxis(Vector3f axis, Vector3d fallback)
    {
        Vector3d flat = new(axis.X, axis.Y, 0d);
        double length = flat.Length;
        return length > 1e-6d ? flat / length : fallback;
    }

    /// <summary>The wheels bound to a car, or one driven wheel at the origin when none are bound.</summary>
    private CarWheel[] CarWheelsFor(GameTask? task, in CarPhysicsDefinition physics)
    {
        if (task is not null && _carWheels.TryGetValue(task, out CarWheel[]? wheels) && wheels.Length > 0)
        {
            return wheels;
        }

        // Without the drawn attachments the record's own entries still say how many wheels drive and
        // steer; place them on the authored dimension box so the body still moves and turns.
        CarWheel[] fallback = new CarWheel[Math.Max(1, physics.Wheels.Count)];
        double halfLength = physics.Dimensions.Y * 0.25d * WorldScene.UnitsPerMetre;
        double halfWidth = physics.Dimensions.X * 0.5d * WorldScene.UnitsPerMetre;
        for (int i = 0; i < fallback.Length; i++)
        {
            CarWheelTypeDefinition definition = i < physics.Wheels.Count
                ? physics.Wheels[i]
                : new CarWheelTypeDefinition(false, true, 0f, 0, 0f, 0f);
            fallback[i] = new CarWheel(
                definition,
                new Vector3d(
                    (i % 2) == 0 ? halfWidth : -halfWidth,
                    definition.IsSteering ? halfLength : -halfLength,
                    0d),
                0.95f,
                0.05f);
        }

        return fallback;
    }

    /// <summary>Creates or resets a car's retail body at its placement -- the set-up packet's reset (<c>0x42EA10</c> mode 0).</summary>
    internal void ResetRetailBody(GameTask? task, CutsceneTransformState state)
    {
        if (_retailCarWorld is null)
        {
            // An AnimTask the level starts running sends its set-up packets while this runtime is being
            // built, before the host has the world its rays are cast into. The reset is only deferred.
            state.RetailResetPending = true;
            return;
        }

        state.RetailResetPending = false;
        if (task is not DeclaredTask declared
            || state.CarPhysics is not { } physics)
        {
            return;
        }

        int wheelAttachments = _carWheels.TryGetValue(task, out CarWheel[]? wheels) ? wheels.Length : 0;
        RetailCarPlacement placement = RetailCarSpawn.Placement(declared, wheelAttachments);
        if (state.RetailBody is { } body)
        {
            RetailCarSpawn.Reset(body, placement);
        }
        else if (_retailCarModels?.Invoke(TextOrEmpty(declared, WorldScene.ModelParameter)) is { } model)
        {
            RetailCarBody created = RetailCarSpawn.Create(physics, model, placement);
            created.Owner = task;
            if (OpenIGI.Formats.World.MefCollisionMesh.TryRead(model.Container, meshIndex: 0, out OpenIGI.Formats.World.MefCollisionMesh? mesh)
                && mesh is { Faces.Length: > 0 })
            {
                created.RayModel = new RetailRayModel(task, new RetailRayMesh(mesh), model.NativeMarkerBound);
            }

            state.RetailBody = created;
        }
        else
        {
            return;
        }

        CopyRetailPose(state, state.RetailBody);
        state.PreviousPosition = state.Position;
        state.PreviousOrientation = state.Orientation;
    }

    /// <summary>One physics packet on the retail body: the channels as <c>0x42EA10</c> latches them, then <c>Car_Tick</c>.</summary>
    private void DriveRetailCar(
        GameTask? target,
        CutsceneTransformState transform,
        RetailCarBody body,
        IRetailCarWorld world,
        float channel0,
        float channel1,
        float channel3)
    {
        body.SteeringTarget = channel0;
        if (channel1 != -1f)
        {
            body.ThrottleTarget = channel1;
        }

        body.Handbrake = channel3 != -1f;
        body.Tick(world);

        CopyRetailPose(transform, body);
        transform.TargetSteering = body.SteeringTarget;
        transform.TargetThrust = body.ThrottleTarget;
        transform.Steering = body.Steering;
        transform.Thrust = body.Throttle;
        transform.ParkingBrake = body.Handbrake;
        transform.Speed = body.Speed;
        transform.Velocity = transform.Position - transform.PreviousPosition;
        transform.CarMotion.Velocity = new Vector3d(body.Velocity[0], body.Velocity[1], body.Velocity[2]);
        if (transform.CarPhysics is { } physics)
        {
            CarDynamics.AnimateWheels(
                transform.CarMotion,
                physics,
                CarWheelsFor(target, physics),
                transform.Velocity.Length,
                body.Steering,
                body.Handbrake);
        }

        transform.IsDynamic = true;
    }

    /// <summary>
    /// The vehicles rays can strike, each at its body's current pose: every car whose retail body exists and whose
    /// task is in the world.
    /// </summary>
    /// <returns>The vehicles' ray models.</returns>
    public IReadOnlyList<RetailRayModel> RetailVehicleModels()
    {
        _retailVehicleModels.Clear();
        foreach ((GameTask task, CutsceneTransformState state) in _context.TaskTransforms)
        {
            if (state.CarPhysics is null || !ConditionalContainerGate.IsTaskActive(task))
            {
                continue;
            }

            if (state.RetailBody is { RayModel: { } model } body)
            {
                Array.Copy(body.Position, model.Position, 3);
                Array.Copy(body.M, model.M, 9);
                _retailVehicleModels.Add(model);
            }
            else if (state.RetailBody is null && StandingCarModel(task) is { } standing)
            {
                // A car no controller has reset yet -- or never will, like mission 7's stand-in T80 361 -- stands
                // in the object tree where the level placed it.
                _retailVehicleModels.Add(standing);
            }
        }

        return _retailVehicleModels;
    }

    private RetailRayModel? StandingCarModel(GameTask task)
    {
        if (_standingCars.TryGetValue(task, out RetailRayModel? cached))
        {
            return cached;
        }

        RetailRayModel? model = null;
        if (task is DeclaredTask declared
            && _retailCarModels?.Invoke(TextOrEmpty(declared, WorldScene.ModelParameter)) is { } mef
            && OpenIGI.Formats.World.MefCollisionMesh.TryRead(mef.Container, meshIndex: 0, out OpenIGI.Formats.World.MefCollisionMesh? mesh)
            && mesh is { Faces.Length: > 0 })
        {
            RetailCarPlacement placement = RetailCarSpawn.Placement(declared, 0);
            model = new RetailRayModel(task, new RetailRayMesh(mesh), mef.NativeMarkerBound);
            model.Position[0] = placement.Position.X;
            model.Position[1] = placement.Position.Y;
            model.Position[2] = placement.Position.Z;
            RetailSplineSegment.AnglesToMatrix(placement.Orientation.X, placement.Orientation.Y, placement.Orientation.Z)
                .CopyTo(model.M, 0);
        }

        _standingCars[task] = model;
        return model;
    }

    /// <summary>A plane's retail body, created from its type and model the first time its AnimTask speaks.</summary>
    internal RetailPlaneBody? RetailPlaneFor(DeclaredTask task, CutsceneTransformState state, in PlanePhysicsDefinition physics)
    {
        if (state.RetailPlane is { } existing)
        {
            return existing;
        }

        if (_retailCarModels is not { } models
            || models(TextOrEmpty(task, WorldScene.ModelParameter)) is not { } model
            || _retailMagic is null)
        {
            return null;
        }

        RetailPlaneBody plane = RetailPlaneSpawn.Create(
            physics,
            model,
            RetailPlaneSpawn.Placement(task),
            _retailMagic,
            name => models(name));
        plane.Owner = task;
        state.RetailPlane = plane;
        return plane;
    }

    /// <summary>A helicopter's retail body, created from its type and model the first time its AnimTask speaks.</summary>
    internal RetailHeliBody? RetailHeliFor(DeclaredTask task, CutsceneTransformState state, in HelicopterPhysicsDefinition physics)
    {
        if (state.RetailHeli is { } existing)
        {
            return existing;
        }

        if (_retailCarModels?.Invoke(TextOrEmpty(task, WorldScene.ModelParameter)) is not { } model || _retailMagic is null)
        {
            return null;
        }

        VehiclePhysicsRegistry? registry = _context.VehiclePhysics;
        RetailHeliBody heli = RetailHeliSpawn.Create(
            physics,
            model,
            RetailHeliSpawn.Placement(task),
            _retailMagic,
            name => registry?.TryGetRotor(name, out RotorPhysicsDefinition rotor) == true ? rotor : null);
        heli.Owner = task;
        state.RetailHeli = heli;
        return heli;
    }

    internal static void CopyRetailPose(CutsceneTransformState state, RetailRigidBody body)
    {
        float[] m = body.M;
        state.Position = new Vector3d(body.Position[0], body.Position[1], body.Position[2]);
        state.Orientation = new Matrix3f(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);
        state.Heading = MathF.Atan2(m[3], m[0]);
    }

    /// <summary>Drives one car for a tick from its three control channels (<c>0x42EA10</c>'s physics branch).</summary>
    /// <param name="target">The Car task, or <see langword="null"/>.</param>
    /// <param name="transform">The car's live transform.</param>
    /// <param name="simulationOrder">The dispatch order the pose snapshot is keyed by.</param>
    /// <param name="channel0">The steering target.</param>
    /// <param name="channel1">The throttle target, or -1 to hold the last one.</param>
    /// <param name="channel3">The parking brake: anything but -1 pulls it.</param>
    /// <remarks>An AnimTask's packet and CarAI's own (<c>0x426510</c>) arrive through the same receiver.</remarks>
    internal void DriveCar(
        GameTask? target,
        CutsceneTransformState transform,
        int simulationOrder,
        float channel0,
        float channel1,
        float channel3)
    {
        SnapshotDynamicPose(transform, simulationOrder);
        if (transform.RetailBody is { } retailBody && _retailCarWorld is { } retailWorld)
        {
            DriveRetailCar(target, transform, retailBody, retailWorld, channel0, channel1, channel3);
            return;
        }

        // Car::ReadChannels (0x42EA10) copies ch0 to the steering target and latches ch1 as
        // throttle only when it is not -1.  ch3 is the parking-brake pulse.  The former generic
        // mover had those first two channels reversed and advanced yaw even while stationary,
        // which sends the mission-one outro truck sideways out of its garage.
        transform.TargetSteering = channel0;
        if (channel1 != -1f)
        {
            transform.TargetThrust = channel1;
        }
        transform.ParkingBrake = channel3 != -1f;

        // Car::Tick (0x42ECA0) blends each live control from the model record's retention:
        //     current = current * retention + target * (1 - retention)
        // Retention is record +24 for steering and +32 for throttle; every shipped car publishes
        // a value between 0.9 and 0.998 there.
        CarPhysicsDefinition? carPhysics = transform.CarPhysics;
        float steeringRetention = CarRetention(carPhysics?.SteeringRetention);
        float throttleRetention = CarRetention(carPhysics?.ThrottleRetention);
        transform.Steering = (transform.Steering * steeringRetention)
            + (transform.TargetSteering * (1f - steeringRetention));
        transform.Thrust = (transform.Thrust * throttleRetention)
            + (transform.TargetThrust * (1f - throttleRetention));
        // Car::Tick zeroes a near-zero smoothed throttle at a zero target (0x42ECA0: if
        // +0x730 == 0 and |+0x720| < 0.01, +0x720 = 0). Without it a commanded stop decays
        // asymptotically and the truck freezes mid-route holding residual thrust.
        if (transform.TargetThrust == 0f && MathF.Abs(transform.Thrust) < 0.01f)
        {
            transform.Thrust = 0f;
        }

        float previousHeading = transform.Heading;
        Matrix3f previousOrientation = transform.Orientation;

        // The orientation's second column is the body's forward axis and the first its across
        // axis, which is how 0x430D10 reads them when it builds the drive direction.
        Vector3f forwardAxis = transform.Orientation.Column2;
        Vector3f lateralAxis = transform.Orientation.Column1;
        Vector3d forward = FlattenAxis(forwardAxis, new Vector3d(0d, 1d, 0d));
        Vector3d lateral = FlattenAxis(lateralAxis, new Vector3d(1d, 0d, 0d));

        CarPhysicsDefinition physics = carPhysics ?? DefaultCarPhysics;
        (Vector3d carTranslation, double carYaw) = CarDynamics.Step(
            transform.CarMotion,
            physics,
            CarWheelsFor(target, physics),
            transform.Position,
            forward,
            lateral,
            transform.Steering,
            transform.Thrust,
            transform.ParkingBrake,
            at => _ground.TryFindSupportHeight(at, target, allowDeepRecovery: false, out double height, out _)
                ? height
                : double.NaN);

        transform.Heading += (float)carYaw;
        transform.Orientation = Matrix3f.FromEngineEulerAngles(
            transform.Euler.X,
            transform.Euler.Y,
            transform.Heading);
        transform.Speed = (float)transform.CarMotion.Velocity.Length;
        Vector3d proposedPosition = transform.Position + carTranslation;

        // The obstacle probe is the body's own front, taken from the authored dimensions rather
        // than from a per-level table: half the length ahead of the origin, swept at the two
        // heights that bracket a car's bumper.
        double halfLengthUnits = physics.Dimensions.Y * 0.5d * WorldScene.UnitsPerMetre;
        bool blocked = false;
        GameTask? obstacle = null;
        if (target is not null && _vehicleObstacleHit is not null && carTranslation.Length > 0d)
        {
            Vector3d heading3 = carTranslation / carTranslation.Length;
            for (int heightIndex = 0; heightIndex < CarProbeHeightsMetres.Length && !blocked; heightIndex++)
            {
                Vector3d offset = new(
                    0d,
                    0d,
                    CarProbeHeightsMetres[heightIndex] * WorldScene.UnitsPerMetre);
                Vector3d probeStart = transform.Position + (heading3 * halfLengthUnits) + offset;
                Vector3d probeEnd = proposedPosition
                    + (heading3 * (halfLengthUnits + CarProbeMarginUnits))
                    + offset;
                if (_vehicleObstacleHit.Invoke(target, probeStart, probeEnd) is { } contact)
                {
                    obstacle = contact.Owner;
                    blocked = true;
                }
            }
        }

        if (_vehicleObstacleReleased is not null
            && obstacle is not null
            && string.Equals(obstacle.Type.Name, "Door", StringComparison.Ordinal))
        {
            transform.VehicleLatchedObstacle = obstacle;
        }

        if (transform.VehicleLatchedObstacle is { } latchedDoor)
        {
            if (_vehicleObstacleReleased?.Invoke(latchedDoor) == true)
            {
                transform.VehicleLatchedObstacle = null;
                transform.VehicleGateReleased = true;
                if (ReferenceEquals(obstacle, latchedDoor))
                {
                    blocked = false;
                    obstacle = null;
                }
            }
            else
            {
                blocked = true;
                obstacle ??= latchedDoor;
            }
        }

        if (blocked)
        {
            // Retail resolves a contact by cancelling the motion into the obstacle and letting
            // the body keep whatever is left, so a car that clips scenery slides along it and
            // recovers. Latching the speed to nought instead parks the vehicle for good, which
            // is what stranded the Mission 1 truck against the perimeter fence. The springs are
            // not part of that contact, so the body still rides them.
            transform.Heading = previousHeading;
            transform.Orientation = previousOrientation;
            transform.CarMotion.Velocity = default;
            transform.CarMotion.YawRate = 0d;
            transform.Speed = 0f;
            transform.Position += new Vector3d(0d, 0d, carTranslation.Z);
        }
        else
        {
            transform.Position = proposedPosition;
            transform.Velocity = transform.Position - transform.PreviousPosition;
        }
        transform.IsDynamic = true;
    }
}
