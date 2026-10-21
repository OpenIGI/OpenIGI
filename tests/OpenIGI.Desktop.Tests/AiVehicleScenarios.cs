using OpenIGI.Game.Physics;
using System.Globalization;
using System.Text;
using OpenIGI.Audio;
using OpenIGI.Composition;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Platform.Desktop.Logging;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Host;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Input;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Presentation.Rendering;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using static OpenIGI.Desktop.Tests.AiScenarioSupport;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// A diagnostic run, not a check: the port half of the probe's <c>vehiclegun</c> capture, one vehicle's guns
/// written in the probe's own record format.
/// </summary>
/// <remarks>Runs only when <c>OPENIGI_VEH_SCENARIO</c> names the trace file.</remarks>
[Collection(AiScenarioSupport.CollectionName)]
public sealed class AiVehicleScenarios : IDisposable
{
    private readonly AiScenarioSupport _support = new();

    /// <inheritdoc/>
    public void Dispose() => _support.Dispose();

    /// <summary>
    /// The port half of the probe's <c>vehiclegun</c> capture: the same autopilot script, played headless,
    /// with one vehicle's guns written in the probe's own record format.
    /// </summary>
    /// <remarks>
    /// Runs only when <c>OPENIGI_VEH_SCENARIO</c> names the trace file. <c>OPENIGI_VEH_SCENARIO_SCRIPT</c> is
    /// the probe script (<c>god</c>, <c>teleport x y z [yaw]</c>, <c>quit</c>; retail units and headings),
    /// <c>_MISSION</c> the mission (7) and <c>_ID</c> the vehicle (354). Each gun logs its pose every tick it
    /// changes, a TRIGGER line when its trigger changes, and a ROUND line for each round it fires.
    /// <c>_CUTSCENE=1</c> plays the opening cutscene instead of skipping it, <c>_SCENEVAR</c> names a script
    /// variable (a scene's <c>nTick</c>) stamped on each <c>[body]</c> line, and <c>_SOLDIERS=n</c> logs every
    /// soldier's feet and drawn origin every n ticks, with each staged actor's brain state -- the port half of
    /// the probe's <c>cutscene</c> hook set.
    /// </remarks>
    [RequiresGameDataFact]
    public void ScriptedVehicleCapture()
    {
        string? output = Environment.GetEnvironmentVariable("OPENIGI_VEH_SCENARIO");
        string? scriptPath = Environment.GetEnvironmentVariable("OPENIGI_VEH_SCENARIO_SCRIPT");
        if (string.IsNullOrEmpty(output) || string.IsNullOrEmpty(scriptPath))
        {
            return;
        }

        const double OriginAboveFeet = 3877.2;
        int mission = EnvInt("OPENIGI_VEH_SCENARIO_MISSION", 7);
        int vehicleId = EnvInt("OPENIGI_VEH_SCENARIO_ID", 354);
        List<(int Tick, string Op, double[] Args)> script = [];
        List<(int Tick, string Name, int Value)> setvars = [];
        foreach (string raw in File.ReadAllLines(scriptPath))
        {
            string line = raw.Split('#')[0].Trim();
            string[] words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2 && words[1].Equals("setvar", StringComparison.OrdinalIgnoreCase) && words.Length >= 4)
            {
                setvars.Add((int.Parse(words[0], CultureInfo.InvariantCulture), words[2], int.Parse(words[3], CultureInfo.InvariantCulture)));
            }
            else if (words.Length >= 2)
            {
                script.Add((
                    int.Parse(words[0], CultureInfo.InvariantCulture),
                    words[1].ToLowerInvariant(),
                    [.. words.Skip(2).Select(word => double.Parse(word, CultureInfo.InvariantCulture))]));
            }
        }

        StringBuilder log = new();
        using RecordingGraphicsDevice device = new();
        // OPENIGI_VEH_SCENARIO_CUTSCENE=1 plays the opening cutscene instead of skipping it.
        string[] hostArguments = EnvInt("OPENIGI_VEH_SCENARIO_CUTSCENE", 0) == 1
            ? ["-mission", mission.ToString(CultureInfo.InvariantCulture), "-lockstep", "-frames", "1000000"]
            : ["-mission", mission.ToString(CultureInfo.InvariantCulture), "-skipcutscene", "-lockstep", "-frames", "1000000"];
        GameHost host = Host(device, new HeadlessGameWindow(), hostArguments);
        host.Start();
        host.ScriptedViewInput = new InputState();

        PlayerCamera? player = null;
        SoldierRenderer? soldiers = null;
        for (int i = 0; i < 20_000 && (player is null || soldiers is null); i++)
        {
            Assert.True(host.Step());
            player = host.Player();
            soldiers = host.Soldiers();
        }

        Assert.NotNull(player);
        Assert.NotNull(soldiers);
        OpenIGI.Engine.Flow.GameLoop loop = host.Loop();
        CutsceneRuntime runtime = MissionOf(host).Cutscenes!;
        OpenIGI.Engine.Tasks.GameTask vehicle = loop.Tasks.FromId(vehicleId)!;
        Assert.NotNull(vehicle);

        int tick = -1;
        Action<VehicleCombatShot>? fired = runtime.VehicleCombatShotFired;
        runtime.VehicleCombatShotFired = shot =>
        {
            log.AppendLine(CultureInfo.InvariantCulture, $"[vgun] ROUND tick={tick} owner={shot.Owner?.Id} weapon={shot.WeaponScriptId} origin=({shot.Origin.X:0},{shot.Origin.Y:0},{shot.Origin.Z:0})");

            fired?.Invoke(shot);
        };

        List<VehicleGunTurret> guns = [];
        for (int index = 0; runtime.GunTurret(vehicle, index) is { } turret; index++)
        {
            guns.Add(turret);
            log.AppendLine(CultureInfo.InvariantCulture, $"[vgun] GUN index={index} weapon={turret.Mount.WeaponModel}");
        }

        List<OpenIGI.Engine.Tasks.DeclaredTask> vehicles = [.. Walk(loop.Tasks.Root)
            .OfType<OpenIGI.Engine.Tasks.DeclaredTask>()
            .Where(static task => task.Type.Name is "Car" or "Heli" or "Plane")];
        HumanHealth health = MissionOf(host).PlayerHealth;
        bool[] triggers = new bool[guns.Count];
        bool god = false;
        int last = -1;
        bool quit = false;
        int maxSteps = EnvInt("OPENIGI_VEH_SCENARIO_STEPS", 200_000);
        int lastPlaneSteps = -1;
        Dictionary<PlacedDoor, string> doorLines = [];
        List<SoldierSnapshot> soldierSnapshots = [];
        string destroyState = string.Empty;
        int vehicleWeaponKey = 0;
        int vehicleWeaponTick = -1;
        int vehicleFireUntil = -1;
        int vehicleUseUntil = -1;
        double[]? vehicleAimPoint = null;

        // OPENIGI_VEH_SCENARIO_POSLOG=<n>: the player's position every n ticks, as the probe's IGI_PROBE_POSLOG.
        int posEvery = Math.Max(1, EnvInt("OPENIGI_VEH_SCENARIO_POSLOG", 30));
        string progress = output + ".progress";
        File.WriteAllText(progress, string.Empty);
        for (int step = 0; step < maxSteps && !quit; step++)
        {
            if (step % 2000 == 0)
            {
                File.AppendAllText(progress, string.Create(
                    CultureInfo.InvariantCulture,
                    $"step={step} tick={tick} engine={soldiers!.Simulation.TickCount} memMB={GC.GetTotalMemory(false) / 1048576}{Environment.NewLine}"));
            }

            if (god)
            {
                // The host keeps the player's damage in its own HumanHealth, not the body's. Retail's god
                // flag stops the accumulation but not the hit, so the player is still shoved.
                player.Body.Health.Reset();
            }

            health.Invulnerable = god;

            if (!host.Step())
            {
                log.AppendLine(CultureInfo.InvariantCulture, $"the host ended at step {step}");
                break;
            }

            int engine = soldiers!.Simulation.TickCount;
            if (engine == last)
            {
                continue;
            }

            last = engine;
            tick++;

            // OPENIGI_VEH_SCENARIO_DESTROY=1: the port half of the probe's vehicle hook set -- the vehicle's
            // dying/destroyed state each time it changes, and what the world renderer draws for every placement
            // it owns on that tick and every 30 after.
            if (EnvInt("OPENIGI_VEH_SCENARIO_DESTROY", 0) != 0)
            {
                DestructibleObjectRegistry? registry = MissionOf(host).Destructibles;
                DestructibleObject? target = registry?.Objects.FirstOrDefault(
                    item => ReferenceEquals(item.Owner, vehicle) && item.Kind == DestructibleObjectKind.Vehicle);
                string state = target is null ? "none" : $"dying={target.IsDying} destroyed={target.IsDestroyed}";
                if (state != destroyState || (target?.IsDying == true && tick % 30 == 0))
                {
                    destroyState = state;
                    log.AppendLine(CultureInfo.InvariantCulture, $"[vehicle] tick={tick} id={vehicleId} {state}");
                    if (host.World() is OpenIGI.Presentation.Rendering.WorldRenderer world)
                    {
                        foreach (string line in world.DescribeFrameForTesting(vehicle))
                        {
                            log.AppendLine(CultureInfo.InvariantCulture, $"[vehicle]    {line}");
                        }
                    }

                    // What a ray straight down through the vehicle's origin meets: its hull, its wreck, or the ground.
                    if (target is not null
                        && MissionOf(host).Solids is ObjectCollisionIndex solids)
                    {
                        Vector3d above = target.PositionUnits + new Vector3d(0.0, 0.0, 5.0 * Metre);
                        Vector3d below = target.PositionUnits - new Vector3d(0.0, 0.0, 3.0 * Metre);
                        string met = solids.TryIdentify(above, below, out string label, out WorldRayHit struck)
                            ? $"{label} at z={struck.Position.Z:0}"
                            : "nothing";
                        log.AppendLine(CultureInfo.InvariantCulture, $"[vehicle]    collision down through origin: {met}");
                    }
                }
            }

            for (int index = 0; index < guns.Count; index++)
            {
                VehicleGunTurret gun = guns[index];
                if (gun.Firing != triggers[index])
                {
                    triggers[index] = gun.Firing;
                    log.AppendLine(CultureInfo.InvariantCulture, $"[vgun] TRIGGER tick={tick} weapon={gun.Mount.WeaponModel} held={(gun.Firing ? 1 : 0)} pitch={gun.Pitch:0.0000} yaw={gun.Yaw:0.0000}");
                }

                if (tick % 10 == 2)
                {
                    log.AppendLine(CultureInfo.InvariantCulture, $"[vgun] POSE tick={tick} weapon={gun.Mount.WeaponModel} pitch={gun.Pitch:0.0000} yaw={gun.Yaw:0.0000}");
                }
            }

            if (EnvInt("OPENIGI_VEH_SCENARIO_HELI", -1) is int heliId and >= 0)
            {
                foreach (CutsceneAnimationState animation in runtime.AnimationsForTesting)
                {
                    if (animation.TargetId != heliId || !animation.RanThisTick)
                    {
                        continue;
                    }

                    OpenIGI.Engine.Tasks.AnimDataProgram program = animation.Program;
                    float[] channels = program.Channels.ToArray();
                    int programTick = program.Tick;
                    OpenIGI.Engine.Tasks.GameTask heli = loop.Tasks.FromId(heliId)!;
                    Vector3d at = runtime.TryGetDrawTransform(heli, out Vector3d metres, out _) ? metres * Metre : default;
                    string extra = string.Empty;
                    if (runtime.TransformsForTesting.TryGetValue(heliId, out CutsceneTransformState? state))
                    {
                        Matrix3f m = state.Orientation;
                        Vector3d v = state.Velocity;
                        Vector3f l = state.AngularMomentum;
                        Vector3f c = state.Control;
                        extra = string.Create(CultureInfo.InvariantCulture, $" vel=({v.X:0.0},{v.Y:0.0},{v.Z:0.0}) angular=({l.X:G4},{l.Y:G4},{l.Z:G4}) control=({c.X:0.####},{c.Y:0.####},{c.Z:0.####}) thrust={state.Thrust} ground={state.GroundContact} m=[{m.M11:0.####} {m.M12:0.####} {m.M13:0.####}; {m.M21:0.####} {m.M22:0.####} {m.M23:0.####}; {m.M31:0.####} {m.M32:0.####} {m.M33:0.####}]");
                    }

                    log.AppendLine(CultureInfo.InvariantCulture, $"[heli] tick={tick} programTick={programTick} pos=({at.X:0.0},{at.Y:0.0},{at.Z:0.0}){extra} channels=({string.Join(",", channels.Select(c => c.ToString("0.#####", CultureInfo.InvariantCulture)))})");
                }
            }

            // OPENIGI_VEH_SCENARIO_BODY=<id>: that car's retail rigid body every tick, bit-exact, for lining the
            // port's drive up against a probe carstep capture.
            if (EnvInt("OPENIGI_VEH_SCENARIO_BODY", -1) is int bodyId and >= 0
                && runtime.TransformsForTesting.TryGetValue(bodyId, out CutsceneTransformState? bodyState))
            {
                if (bodyState.RetailBody is RetailCarBody body)
                {
                    string scene = Environment.GetEnvironmentVariable("OPENIGI_VEH_SCENARIO_SCENEVAR") is { Length: > 0 } sceneVar
                        && loop.Variables.TryRead(sceneVar, out OpenIGI.Scripting.Runtime.QvmValue sceneTick) ? $" scene={sceneTick}" : string.Empty;
                    log.AppendLine(CultureInfo.InvariantCulture, $"[body] tick={tick}{scene} pos=({body.Position[0]:R},{body.Position[1]:R},{body.Position[2]:R}) thr={body.Throttle:R}/{body.ThrottleTarget:R} steer={body.Steering:R}/{body.SteeringTarget:R} hb={body.Handbrake} speed={body.Speed:R}");
                }
                else if (bodyState.RetailPlane is RetailPlaneBody plane
                    && bodyState.PhysicsTicks != lastPlaneSteps)
                {
                    // The probe's planeflight format: each force as pushed, then the state after the step.
                    int planeSteps = bodyState.PhysicsTicks;
                    lastPlaneSteps = planeSteps;
                    foreach (PointForce f in plane.PushedForces)
                    {
                        log.AppendLine(CultureInfo.InvariantCulture, $"[pforce] f=({f.Force.X:G9},{f.Force.Y:G9},{f.Force.Z:G9}) p=({f.Point.X:G9},{f.Point.Y:G9},{f.Point.Z:G9})");
                    }

                    float[] pm = plane.M;
                    log.AppendLine(CultureInfo.InvariantCulture, $"[pf] tick={tick} step={planeSteps} pos=({plane.Position[0]:R},{plane.Position[1]:R},{plane.Position[2]:R}) m=[{pm[0]:G9} {pm[1]:G9} {pm[2]:G9} {pm[3]:G9} {pm[4]:G9} {pm[5]:G9} {pm[6]:G9} {pm[7]:G9} {pm[8]:G9}] vel=({plane.Velocity[0]:G9},{plane.Velocity[1]:G9},{plane.Velocity[2]:G9}) mom=({plane.Momentum[0]:G9},{plane.Momentum[1]:G9},{plane.Momentum[2]:G9}) speed={plane.Speed:G9} thrust={plane.Thrust:G9}/{plane.ThrustTarget:G9} ctl=({plane.Controls[0]:G9},{plane.Controls[1]:G9},{plane.Controls[2]:G9})");
                }
                else if (bodyState.RetailHeli is RetailHeliBody heli)
                {
                    log.AppendLine(CultureInfo.InvariantCulture, $"[body] tick={tick} pos=({heli.Position[0]:R},{heli.Position[1]:R},{heli.Position[2]:R}) thr={heli.Collective:R}/{heli.CollectiveTarget:R} steer={heli.Controls[0]:R}/{heli.Targets[0]:R} hb=False speed={heli.Speed:R}");
                }
            }

            // OPENIGI_VEH_SCENARIO_DOORS=1: every door's pose whenever it changes, in the probe's [door] format.
            if (EnvInt("OPENIGI_VEH_SCENARIO_DOORS", 0) == 1
                && MissionOf(host).Doors is DoorRegistry doorRegistry)
            {
                for (int doorIndex = 0; doorIndex < doorRegistry.Doors.Count; doorIndex++)
                {
                    PlacedDoor door = doorRegistry.Doors[doorIndex];
                    Vector3d du = door.PoseUnits;
                    Matrix3f dm = door.PoseOrientation;
                    string line = string.Create(CultureInfo.InvariantCulture, $"pos=({du.X:R},{du.Y:R},{du.Z:R}) m=[{dm.M11:G9} {dm.M12:G9} {dm.M13:G9} {dm.M21:G9} {dm.M22:G9} {dm.M23:G9} {dm.M31:G9} {dm.M32:G9} {dm.M33:G9}] slide={door.State.SlideFraction:G9} angle={door.State.AngleDegrees:G9}");
                    if (!doorLines.TryGetValue(door, out string? previous) || previous != line)
                    {
                        doorLines[door] = line;
                        log.AppendLine(CultureInfo.InvariantCulture, $"[door] tick={tick} door=port{doorIndex} id={door.Task.Id} {line}");
                    }
                }
            }

            // OPENIGI_VEH_SCENARIO_SOLDIERS=<n>: every soldier's feet every n ticks.
            if (EnvInt("OPENIGI_VEH_SCENARIO_SOLDIERS", 0) is int soldierEvery and > 0 && tick % soldierEvery == 0)
            {
                soldiers.Simulation.Snapshot(soldierSnapshots);
                List<Vector3d> drawn = [];
                foreach (SoldierGroup group in soldiers.GroupsForTesting)
                {
                    foreach (SoldierMember member in group.Soldiers)
                    {
                        drawn.Add(member.TickWhere * Metre);
                        SoldierAgent agent = member.Agent;
                        if (agent.IsCutsceneActor)
                        {
                            SoldierView view = agent.View;
                            Igi1HumanAi? brain = soldiers.Simulation.Brains.GetValueOrDefault(view);

                            // The port's half of the probe's [human] parent/local: what the actor rides, its offset
                            // in the parent's frame, and where that parent is now.
                            if (runtime.AttachmentsForTesting.TryGetValue(view.Soldier, out CutsceneAttachmentState? attachment))
                            {
                                OpenIGI.Engine.Tasks.GameTask parentTask = attachment.ParentTask;
                                Vector3d relative = attachment.RelativePosition;
                                Vector3d parentAt = attachment.Parent.Position;
                                log.AppendLine(CultureInfo.InvariantCulture, $"[attach] tick={tick} id={view.Soldier.Id} parent={parentTask.Id} {parentTask.Type.Name} local=({relative.X:0.0},{relative.Y:0.0},{relative.Z:0.0}) parentAt=({parentAt.X:0},{parentAt.Y:0},{parentAt.Z:0})");
                                if (runtime.TryGetAttachedDrawTransform(view.Soldier, out _, out Matrix3f face))
                                {
                                    log.AppendLine(CultureInfo.InvariantCulture, $"[attachface] tick={tick} id={view.Soldier.Id} face=[{face.M11:0.00000} {face.M12:0.00000} {face.M13:0.00000}; {face.M21:0.00000} {face.M22:0.00000} {face.M23:0.00000}; {face.M31:0.00000} {face.M32:0.00000} {face.M33:0.00000}]");
                                }
                            }

                            log.AppendLine(CultureInfo.InvariantCulture, $"[actor] tick={tick} view=({view.Position.X:0.0},{view.Position.Y:0.0},{view.Position.Z:0.0}) brain=({brain?.Position.X:0.0},{brain?.Position.Y:0.0},{brain?.Position.Z:0.0}) act={brain?.Activity} req={brain?.RequestedAnimation} route={brain?.Route?.Current?.Kind} root={agent.HasCutsceneRootMotion} move={agent.HasCutsceneGameplayMovement} sample=({view.RootSample.X:0.0},{view.RootSample.Y:0.0},{view.RootSample.Z:0.0}) anim={view.CurrentAnimation}");
                        }
                    }
                }

                for (int index = 0; index < soldierSnapshots.Count; index++)
                {
                    SoldierSnapshot snapshot = soldierSnapshots[index];
                    Vector3d feet = snapshot.Position * Metre;
                    Vector3d at = index < drawn.Count ? drawn[index] : default;
                    log.AppendLine(CultureInfo.InvariantCulture, $"[soldier] tick={tick} name={snapshot.Name} feet=({feet.X:0},{feet.Y:0},{feet.Z:0}) drawn=({at.X:0.0},{at.Y:0.0},{at.Z:0.0}) anim={snapshot.Animation} actor={snapshot.IsCutsceneActor}");
                }
            }

            Vector3d origin = player.Body.Position + new Vector3d(0d, 0d, OriginAboveFeet);
            if (runtime.TryRunOver(origin, out OpenIGI.Engine.Tasks.GameTask? runner) && runner is not null)
            {
                Vector3d at = runtime.TryGetDrawTransform(runner, out Vector3d where, out _) ? where * Metre : default;
                log.AppendLine(CultureInfo.InvariantCulture, $"[runover] tick={tick} by={runner.Id} vehicle=({at.X:0},{at.Y:0}) player=({origin.X:0},{origin.Y:0}) d={Math.Sqrt(((at.X - origin.X) * (at.X - origin.X)) + ((at.Y - origin.Y) * (at.Y - origin.Y))) / Metre:0.00} m");
            }

            if (tick % posEvery == 0)
            {
                log.AppendLine(CultureInfo.InvariantCulture, $"[autopilot] POS tick={tick} pos=({player.Body.Position.X:0}, {player.Body.Position.Y:0}, {player.Body.Position.Z + OriginAboveFeet:0}) dmg={health.Damage:0.00} dead={MissionOf(host).IsPlayerDead} shove=({player.Body.HitShove.X:0.00},{player.Body.HitShove.Y:0.00})");
                foreach (CarAiState state in runtime.CarAisForTesting)
                {
                    AiPatrolRoute? route = state.Route;
                    OpenIGI.Engine.Tasks.GameTask task = state.Task;
                    OpenIGI.Engine.Tasks.GameTask car = state.Car;
                    OpenIGI.Formats.Ai.GraphNode? node = state.TargetNode;
                    log.AppendLine(CultureInfo.InvariantCulture, $"[carai] tick={tick} task={task.Id} car={car.Id} active={ConditionalContainerGate.IsTaskActive(task)} route={(route is null ? "null" : route.HasStopped)} stopped={state.Stopped} node={node?.Id}@{node?.Position.X:0},{node?.Position.Y:0} car={state.Transform.Position} cmd={state.CommandSpeedKmh} throttle={state.Throttle} brake={state.Brake} steer={state.Steering}");
                }

                foreach (string name in (Environment.GetEnvironmentVariable("OPENIGI_VEH_SCENARIO_VARS") ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    string value = loop.Variables.TryRead(name, out OpenIGI.Scripting.Runtime.QvmValue read) ? read.ToString() : "unbound";
                    log.AppendLine(CultureInfo.InvariantCulture, $"[var] tick={tick} {name}={value}");
                }

                foreach (OpenIGI.Engine.Tasks.DeclaredTask car in vehicles)
                {
                    Vector3d at = runtime.TryGetDrawTransform(car, out Vector3d metres, out _)
                        ? metres * Metre
                        : car.TryGetValue("Position", out object? raw) && raw is Vector3d authored ? authored : default;
                    log.AppendLine(CultureInfo.InvariantCulture, $"[vpose] tick={tick} id={car.Id} pos=({at.X:0},{at.Y:0},{at.Z:0})");
                }
            }

            foreach ((int _, string name, int value) in setvars.Where(command => command.Tick == tick))
            {
                bool written = loop.Variables.TryWrite(name, OpenIGI.Scripting.Runtime.QvmValue.FromInt32(value));
                log.AppendLine(CultureInfo.InvariantCulture, $"[autopilot] tick={tick} setvar {name}={value} written={written}");
            }

            foreach ((int _, string op, double[] args) in script.Where(command => command.Tick == tick))
            {
                switch (op)
                {
                    case "god":
                        god = args.Length == 0 || args[0] != 0.0;
                        break;
                    case "teleport":
                        player.Body.Position = new Vector3d(args[0], args[1], args[2] - OriginAboveFeet);
                        if (args.Length >= 4)
                        {
                            player.Body.Yaw = (float)(-args[3] * Math.PI / 180.0);
                        }

                        log.AppendLine(CultureInfo.InvariantCulture, $"[autopilot] tick={tick} teleport ({args[0]:0}, {args[1]:0}, {args[2]:0})");
                        break;
                    case "weapon":
                        vehicleWeaponKey = (int)args[0];
                        vehicleWeaponTick = tick;
                        break;
                    case "aimpoint":
                        vehicleAimPoint = args.Length >= 3 ? args : null;
                        break;
                    case "fire":
                        vehicleFireUntil = tick + (args.Length >= 1 ? (int)args[0] : 1);
                        break;
                    case "use":
                        vehicleUseUntil = tick + (args.Length >= 1 ? (int)args[0] : 1);
                        log.AppendLine(CultureInfo.InvariantCulture, $"[autopilot] tick={tick} use");
                        break;
                    case "quit":
                        quit = true;
                        break;
                    case "complete":
                        // complete <task id>: finish that interaction as its completion 0x443EA0 does -- isFinished
                        // is a byte retail's setvar can write, but the port binds it read-only off the interaction.
                        if (MissionOf(host).Interactions is WorldInteractionRegistry interactions
                            && interactions.Items.FirstOrDefault(item => item.Task.Id == (int)args[0]) is { } finished)
                        {
                            finished.CompleteForTesting();
                            log.AppendLine(CultureInfo.InvariantCulture, $"[autopilot] tick={tick} complete {(int)args[0]}");
                        }
                        else
                        {
                            WorldInteractionRegistry? registry = MissionOf(host).Interactions;
                            string ids = registry is WorldInteractionRegistry all
                                ? string.Join(",", all.Items.Select(item => $"{item.Task.Type.Name}#{item.Task.Id}"))
                                : "no registry";
                            log.AppendLine(CultureInfo.InvariantCulture, $"[autopilot] tick={tick} complete {(int)args[0]} NOT FOUND among {ids}");
                        }

                        break;
                }
            }

            // The probe's buttons and aim, as the generator capture plays them: a weapon key for one tick, the
            // trigger held for the fire command's ticks, and the eye held on the aim point.
            if (vehicleAimPoint is { } aim)
            {
                Vector3d eye = player.Body.Position + new Vector3d(0.0, 0.0, player.Body.EyeHeight);
                double dx = aim[0] - eye.X;
                double dy = aim[1] - eye.Y;
                double dz = aim[2] - eye.Z;
                player.Body.Yaw = (float)Math.Atan2(dx, dy);
                player.Body.Pitch = (float)Math.Atan2(dz, Math.Sqrt((dx * dx) + (dy * dy)));
            }

            InputState vehicleInput = host.ScriptedViewInput!;
            if (vehicleWeaponKey > 0)
            {
                vehicleInput.Keyboard.SetKey(KeyCode.D1 + (byte)(vehicleWeaponKey - 1), down: tick == vehicleWeaponTick);
            }

            vehicleInput.Mouse.SetButton(MouseButton.Left, down: tick < vehicleFireUntil);
            vehicleInput.Keyboard.SetKey(KeyCode.E, down: tick < vehicleUseUntil);
            if (player.FiredThisTick)
            {
                log.AppendLine(CultureInfo.InvariantCulture, $"[autopilot] tick={tick} FIRED {player.HeldWeaponType?.ScriptId}");
            }
        }

        foreach (string note in runtime.Diagnostics)
        {
            log.AppendLine(CultureInfo.InvariantCulture, $"[cutscene-note] {note}");
        }

        host.Stop();
        File.WriteAllText(output, log.ToString());
    }

    private static IEnumerable<OpenIGI.Engine.Tasks.GameTask> Walk(OpenIGI.Engine.Tasks.GameTask task)
    {
        foreach (OpenIGI.Engine.Tasks.GameTask child in task.Children)
        {
            yield return child;
            foreach (OpenIGI.Engine.Tasks.GameTask nested in Walk(child))
            {
                yield return nested;
            }
        }
    }

    private GameHost Host(RecordingGraphicsDevice device, HeadlessGameWindow window, params string[] args) =>
        _support.Host(device, window, args);
}
