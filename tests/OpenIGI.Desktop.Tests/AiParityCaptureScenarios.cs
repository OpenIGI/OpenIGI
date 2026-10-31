using System.Globalization;
using System.Text;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Host;
using OpenIGI.Input;
using OpenIGI.Presentation.Igi1;
using OpenIGI.TestSupport;
using Xunit;
using static OpenIGI.Desktop.Tests.AiScenarioSupport;
using OpenIGI.Platform.Headless;
using OpenIGI.Rendering.Headless;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Diagnostic runs, not checks: the port halves of the probe's cutscene and patrol parity captures, one line
/// per thing in shot or per soldier, in the probe's units and layouts.
/// </summary>
/// <remarks>Each runs only when its own environment variable names the trace file.</remarks>
[Collection(AiScenarioSupport.CollectionName)]
public sealed class AiParityCaptureScenarios : IDisposable
{
    private readonly AiScenarioSupport _support = new();

    /// <inheritdoc/>
    public void Dispose() => _support.Dispose();

    /// <summary>
    /// The port half of the probe's <c>scene</c> hook set: a played opening cutscene, one line per thing in shot
    /// per tick, in the probe's units and layouts.
    /// </summary>
    /// <remarks>
    /// Runs only when <c>OPENIGI_SCENE_CAPTURE</c> names the trace file; <c>_MISSION</c> picks the mission (12)
    /// and <c>_TICKS</c> how many scene ticks to record (2000). Each tick logs the running scene, its tick and
    /// shot, the camera (position in engine units, orientation, field of view), every elevator, and every
    /// soldier's drawn origin, orientation and clip.
    /// </remarks>
    [RequiresGameDataFact]
    public void CutsceneParityCapture()
    {
        string? output = Environment.GetEnvironmentVariable("OPENIGI_SCENE_CAPTURE");
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        int mission = EnvInt("OPENIGI_SCENE_CAPTURE_MISSION", 12);
        int ticks = EnvInt("OPENIGI_SCENE_CAPTURE_TICKS", 2000);
        StringBuilder log = new();
        using RecordingGraphicsDevice device = new();
        GameHost host = Host(device, new HeadlessGameWindow(),
            "-mission", mission.ToString(CultureInfo.InvariantCulture), "-lockstep", "-frames", "1000000");
        host.Start();
        host.ScriptedViewInput = new InputState();

        CutsceneRuntime? runtime = null;
        for (int i = 0; i < 20_000 && runtime?.Frame is null; i++)
        {
            Assert.True(host.Step());
            runtime = MissionOf(host).Cutscenes;
            if (MissionOf(host).Elevators is ElevatorRegistry early)
            {
                foreach (PlacedElevator lift in early.Elevators.Where(static lift => lift.State.TransformChangedThisTick))
                {
                    log.AppendLine(CultureInfo.InvariantCulture,
                        $"[early] step={i} id={lift.Task.Id} pos=({lift.State.Position.X:0.000},{lift.State.Position.Y:0.000},{lift.State.Position.Z:0.000}) frame={runtime?.Frame is not null}");
                }
            }
        }

        Assert.NotNull(runtime?.Frame);
        int lastSceneTick = int.MinValue;
        int lastScene = -1;
        for (int step = 0; step < ticks * 4 && runtime.Frame is { } _; step++)
        {
            Assert.True(host.Step());
            if (runtime.Frame is not { } frame)
            {
                break;
            }

            int sceneTick = frame.Scene.Ticks;
            if (sceneTick == lastSceneTick && frame.Scene.Id == lastScene)
            {
                continue;
            }

            lastSceneTick = sceneTick;
            lastScene = frame.Scene.Id;
            Vector3d at = frame.Camera.Position * Metre;
            Matrix3f m = frame.Camera.Orientation;
            log.AppendLine(CultureInfo.InvariantCulture,
                $"[scene] id={frame.Scene.Id} tick={sceneTick} shot={frame.ShotIndex} cam=({at.X:0.000},{at.Y:0.000},{at.Z:0.000}) fov={frame.FieldOfView:0.000000000} m=[{m.M11:0.000000} {m.M12:0.000000} {m.M13:0.000000}; {m.M21:0.000000} {m.M22:0.000000} {m.M23:0.000000}; {m.M31:0.000000} {m.M32:0.000000} {m.M33:0.000000}]");

            if (MissionOf(host).Elevators is ElevatorRegistry elevators)
            {
                foreach (PlacedElevator elevator in elevators.Elevators)
                {
                    Vector3d p = elevator.State.Position;
                    Matrix3f e = elevator.Orientation;
                    log.AppendLine(CultureInfo.InvariantCulture,
                        $"[elevator] scene={sceneTick} id={elevator.Task.Id} pos=({p.X:0.000},{p.Y:0.000},{p.Z:0.000}) m=[{e.M11:0.000000} {e.M12:0.000000} {e.M13:0.000000}; {e.M21:0.000000} {e.M22:0.000000} {e.M23:0.000000}; {e.M31:0.000000} {e.M32:0.000000} {e.M33:0.000000}]");
                }
            }

            if (host.Soldiers() is SoldierRenderer soldiers)
            {
                foreach (SoldierGroup group in soldiers.GroupsForTesting)
                {
                    foreach (SoldierMember member in group.Soldiers)
                    {
                        SoldierView view = member.Agent.View;
                        Vector3d where = member.TickWhere * Metre;
                        Matrix3f o = member.TickOrientation;
                        bool staged = member.Agent.IsCutsceneActor;
                        string attached = runtime.TryGetAttachedDrawTransform(view.Soldier, out Vector3d riding, out _)
                            ? FormattableString.Invariant($" riding=({riding.X * Metre:0},{riding.Y * Metre:0},{riding.Z * Metre:0})")
                            : string.Empty;
                        log.AppendLine(CultureInfo.InvariantCulture,
                            $"[human] scene={sceneTick} id={view.Soldier.Id} staged={staged} drawn=({where.X:0.000},{where.Y:0.000},{where.Z:0.000}) anim={view.CurrentAnimation}{attached} m=[{o.M11:0.000000} {o.M12:0.000000} {o.M13:0.000000}; {o.M21:0.000000} {o.M22:0.000000} {o.M23:0.000000}; {o.M31:0.000000} {o.M32:0.000000} {o.M33:0.000000}] sample=({view.RootSample.X:0.0},{view.RootSample.Y:0.0},{view.RootSample.Z:0.0}) feet=({view.Position.X:0.0},{view.Position.Y:0.0},{view.Position.Z:0.0})");
                    }
                }
            }

            if (sceneTick >= ticks)
            {
                break;
            }
        }

        File.WriteAllText(output, log.ToString());
    }

    /// <summary>
    /// The port's half of the patrol parity capture: the same <c>[pw]</c> line a hook in the running original
    /// writes from retail's HumanAI tick, for every soldier, on a change of body state, patrol command, deadline or
    /// graph node and every 10 level ticks otherwise. <c>OPENIGI_PATROL_CAPTURE=file</c>,
    /// <c>OPENIGI_PATROL_CAPTURE_MISSION</c> (4) and <c>OPENIGI_PATROL_CAPTURE_TICKS</c> (5400). Positions are the
    /// port's feet; retail logs the origin, about 0.97 m higher on a standing soldier.
    /// </summary>
    [RequiresGameDataFact]
    public void PatrolParityCapture()
    {
        string? output = Environment.GetEnvironmentVariable("OPENIGI_PATROL_CAPTURE");
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        int mission = EnvInt("OPENIGI_PATROL_CAPTURE_MISSION", 4);
        int ticks = EnvInt("OPENIGI_PATROL_CAPTURE_TICKS", 5400);
        int every = Math.Max(1, EnvInt("OPENIGI_PATROL_CAPTURE_EVERY", 10));
        (int Looker, int Target)[] slotPairs = [.. (Environment.GetEnvironmentVariable("OPENIGI_PATROL_CAPTURE_SLOTS") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split(':'))
            .Where(parts => parts.Length == 2)
            .Select(parts => (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture)))];
        StringBuilder log = new();
        using RecordingGraphicsDevice device = new();
        // OPENIGI_PATROL_CAPTURE_DIFFICULTY: the difficulty the probe's profile played at (the probe logs it at START).
        // A dropped gun falls the original's way: the realistic body lands, and draws its clatter, on other ticks.
        List<string> captureArgs = ["-mission", mission.ToString(CultureInfo.InvariantCulture), "-skipcutscene", "-lockstep", "-frames", "1000000", "+g_realistic_drops", "0"];
        if (Environment.GetEnvironmentVariable("OPENIGI_PATROL_CAPTURE_DIFFICULTY") is { Length: > 0 } captureDifficulty)
        {
            captureArgs.AddRange(["-difficulty", captureDifficulty]);
        }

        GameHost host = Host(device, new HeadlessGameWindow(), [.. captureArgs]);

        // OPENIGI_PATROL_CAPTURE_RNG=1: every draw from the level's global stream, for the diff against the
        // probe's [rng]: the level counter during the tick (-1 at load) and the first caller outside the generator.
        if (Environment.GetEnvironmentVariable("OPENIGI_PATROL_CAPTURE_RNG") == "1"
            && MissionOf(host).SoundRandom is OpenIGI.Core.Numerics.IgiRandom stream)
        {
            stream.Drew = _ =>
            {
                int counter = host.Soldiers() is SoldierRenderer r ? r.Simulation.TickCount - 1 : -1;
                string site = "?";
                foreach (System.Diagnostics.StackFrame frame in new System.Diagnostics.StackTrace(1, false).GetFrames())
                {
                    if (frame.GetMethod() is { DeclaringType: { } type } method
                        && type != typeof(OpenIGI.Core.Numerics.IgiRandom)
                        && !type.Name.Contains("<>", StringComparison.Ordinal)
                        && !method.Name.Contains('<', StringComparison.Ordinal))
                    {
                        site = $"{type.Name}.{method.Name}";
                        break;
                    }
                }

                log.AppendLine(CultureInfo.InvariantCulture, $"[rng] lt={counter} site={site} who={SoldierSimulation.TickingSoldierId}");
            };

            // The probe's [free]: every node a claiming free-position search tests, and its verdict.
            OpenIGI.Game.Igi1.Ai.AiFreePosition.Tested = (claimant, node, toward, verdict) =>
            {
                if (claimant is not Igi1HumanAi who || host.Soldiers() is not SoldierRenderer r)
                {
                    return;
                }

                int counter = r.Simulation.TickCount - 1;
                int id = -1;
                foreach ((SoldierView v, Igi1HumanAi b) in r.Simulation.Brains)
                {
                    if (ReferenceEquals(b, who))
                    {
                        id = v.Soldier.Id;
                    }
                }

                log.AppendLine(FormattableString.Invariant($"[free] lt={counter} id={id} node={node} toward={(toward ? 1 : 0)} {verdict}"));
            };

            // Which casts rebuilt whose palette, for the eye-matrix comparison.
            SoldierSimulation.PaletteRebuildTraced = (id, why, from, to) =>
            {
                int counter = host.Soldiers() is SoldierRenderer r ? r.Simulation.TickCount - 1 : -1;
                log.AppendLine(FormattableString.Invariant($"[rebuild] lt={counter} id={id} by={why} who={SoldierSimulation.TickingSoldierId} from=({from.X:0},{from.Y:0},{from.Z:0}) to=({to.X:0},{to.Y:0},{to.Z:0})"));
            };

            // The probe's [drop]: where a dying soldier's gun starts falling.
            SoldierSimulation.WeaponDropTraced = (id, at, feet, root) =>
            {
                int counter = host.Soldiers() is SoldierRenderer r ? r.Simulation.TickCount - 1 : -1;
                log.AppendLine(FormattableString.Invariant($"[drop] lt={counter} id={id} pos=({at.X:0.0},{at.Y:0.0},{at.Z:0.0}) feet=({feet.X:0.0},{feet.Y:0.0},{feet.Z:0.0}) root={root:0.0}"));
            };

            // The probe's [dmg]: every round a soldier takes.
            SoldierSimulation.DamageTraced = (id, zone, bone, factor, accum, max) =>
            {
                int counter = host.Soldiers() is SoldierRenderer r ? r.Simulation.TickCount - 1 : -1;
                log.AppendLine(FormattableString.Invariant($"[dmg] lt={counter} id={id} zone={zone} bone={bone} factor={factor:0.000} accum={accum:0.000} max={max:0.000}"));
            };

            // The probe's [fire]: each round's base forward and the gun's rounds this pull.
            SoldierSimulation.RoundSpreadTraced = (who, pull, forward) =>
            {
                if (host.Soldiers() is not SoldierRenderer r)
                {
                    return;
                }

                int counter = r.Simulation.TickCount - 1;
                int id = -1;
                foreach ((SoldierView v, Igi1HumanAi b) in r.Simulation.Brains)
                {
                    if (ReferenceEquals(b, who))
                    {
                        id = v.Soldier.Id;
                    }
                }

                log.AppendLine(FormattableString.Invariant($"[fire] lt={counter} id={id} pull={pull} fwd=({forward.X:0.000000},{forward.Y:0.000000},{forward.Z:0.000000}) yaw={who.Facing:0.000000} ryaw={who.RecoilYaw:0.000000} pitch={who.ViewPitch:0.000000} rpitch={who.RecoilPitch:0.000000}"));
            };

            // The probe's [march]: every pass of a soldier's round, its segment and what the cast met.
            OpenIGI.Game.Igi1.Weapons.GunShotTask.PassCast = (round, from, to, remaining, contact) =>
            {
                int counter = host.Soldiers() is SoldierRenderer r ? r.Simulation.TickCount - 1 : -1;
                int shooter = round.Shooter is SoldierView v ? v.Soldier.Id : -1;
                string what = contact is not { } c
                    ? "hit=0"
                    : FormattableString.Invariant(
                        $"hit=1 obj={c.Payload?.GetType().Name ?? "-"} human={(c.StruckHuman ? 1 : 0)} hasobj={(c.HasObject ? 1 : 0)} entry='{c.Entry?.Name ?? "-"}' exit='{c.Exit?.Name ?? "-"}' pos=({c.Position.X:0.0},{c.Position.Y:0.0},{c.Position.Z:0.0}) n=({c.Normal.X:0.0000},{c.Normal.Y:0.0000},{c.Normal.Z:0.0000}) dist={c.Distance:0.00}");
                log.AppendLine(
                    FormattableString.Invariant(
                        $"[march] lt={counter} shooter={shooter} rem={remaining:0.0} defl={(round.Deflected ? 1 : 0)} from=({from.X:0.0},{from.Y:0.0},{from.Z:0.0}) to=({to.X:0.0},{to.Y:0.0},{to.Z:0.0}) {what}"));
            };
        }

        // OPENIGI_PATROL_CAPTURE_TRACER=1: the probe's [tracer] add -- every tracer a soldier gun's march files
        // (0x47C4C0), with the task's live count before it.
        if (Environment.GetEnvironmentVariable("OPENIGI_PATROL_CAPTURE_TRACER") == "1")
        {
            OpenIGI.Game.Igi1.Weapons.GunShotTask.TracerFiled = (task, tracer) =>
            {
                int counter = -1;
                int owner = -1;
                if (host.Soldiers() is SoldierRenderer r)
                {
                    counter = r.Simulation.TickCount - 1;
                    owner = r.Simulation.GunShotsForTesting.FirstOrDefault(pair => ReferenceEquals(pair.Value, task)).Key?.Soldier.Id ?? -1;
                }

                log.AppendLine(FormattableString.Invariant(
                    $"[tracer] add lt={counter} owner={owner} count={task.Tracers.Count} from=({tracer.Start.X:0.0},{tracer.Start.Y:0.0},{tracer.Start.Z:0.0}) to=({tracer.End.X:0.0},{tracer.End.Y:0.0},{tracer.End.Z:0.0}) head={tracer.StartDistance:0.0} tail={tracer.EndDistance:0.0} range={tracer.Range:0.0} life={tracer.Life}"));
            };
        }

        host.Start();
        host.ScriptedViewInput = new InputState();
        Dictionary<Igi1HumanAi, (int Tick, int State, int Index, int Deadline, int Node, int Dest, int Logged, int Alert, int Sight)> tracks = [];
        Dictionary<Igi1HumanAi, AiEventQueues> observed = [];
        Dictionary<Igi1HumanAi, string> lastFollow = [];
        bool treeDumped = false;
        HashSet<OpenIGI.Game.World.ConditionalContainerTask> containers = [];
        Dictionary<OpenIGI.Game.World.ConditionalContainerTask, bool> containerActive = [];
        int lastTick = int.MinValue;

        // OPENIGI_PATROL_CAPTURE_TASKS=AnimTask_850.nTick,ConditionalContainer_300.isRun,...: the probe's [task], the
        // named script variables logged when they change, at "tick=T" -- the state after T ticks.
        string[] watchedVariables = (Environment.GetEnvironmentVariable("OPENIGI_PATROL_CAPTURE_TASKS") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries);
        Dictionary<string, string> watchedValues = [];
        for (int step = 0; step < ticks * 4; step++)
        {
            Assert.True(host.Step());
            if (host.Soldiers() is not SoldierRenderer soldiers)
            {
                continue;
            }

            if (watchedVariables.Length > 0)
            {
                OpenIGI.Engine.Tasks.ScriptVarRegistry variables = host.Loop().Variables;
                int ran = soldiers.Simulation.TickCount;
                foreach (string name in watchedVariables)
                {
                    string value = variables.TryRead(name, out OpenIGI.Scripting.Runtime.QvmValue read)
                        ? read.ToString() ?? "?"
                        : "-";
                    if (watchedValues.GetValueOrDefault(name) != value)
                    {
                        watchedValues[name] = value;
                        log.AppendLine(CultureInfo.InvariantCulture, $"[task] tick={ran} {name}={value}");
                    }
                }
            }

            Dictionary<SoldierView, Igi1HumanAi> brains = soldiers.Simulation.Brains;
            if (!treeDumped)
            {
                // Each soldier's ancestry, root last: what holds it in the level tree.
                treeDumped = true;
                foreach (SoldierView view in brains.Keys)
                {
                    List<string> chain = [];
                    for (OpenIGI.Engine.Tasks.GameTask? at = view.Soldier; at is not null; at = at.Parent)
                    {
                        chain.Add(at is OpenIGI.Game.World.ConditionalContainerTask container
                            ? FormattableString.Invariant($"{at.Type.Name}#{at.Id} '{at.Label}' [{container.Condition}]")
                            : FormattableString.Invariant($"{at.Type.Name}#{at.Id} '{at.Label}'"));
                        if (at is OpenIGI.Game.World.ConditionalContainerTask watched)
                        {
                            containers.Add(watched);
                        }
                    }

                    log.AppendLine(CultureInfo.InvariantCulture, $"[tree] id={view.Soldier.Id} {string.Join(" < ", chain)}");
                }
            }

            foreach (OpenIGI.Game.World.ConditionalContainerTask container in containers)
            {
                if (!containerActive.TryGetValue(container, out bool was) || was != container.IsActive)
                {
                    containerActive[container] = container.IsActive;
                    log.AppendLine(CultureInfo.InvariantCulture,
                        $"[cc] step={step} tick={lastTick} id={container.Id} '{container.Label}' active={container.IsActive}");
                }
            }

            foreach ((SoldierView view, Igi1HumanAi brain) in brains)
            {
                // The probe's [pwq]/[pwev]: what each AI accepted into its queue and what it dispatched.
                if (!observed.TryGetValue(brain, out AiEventQueues? queues) || !ReferenceEquals(queues, brain.Events))
                {
                    observed[brain] = brain.Events;
                    int id = view.Soldier.Id;

                    // Labelled with the level's counter during the tick, as the probe labels them: a post from a
                    // soldier earlier in the walk reaches one whose own LastTick is still last tick's.
                    SoldierRenderer renderer = soldiers;
                    int Counter() => renderer.Simulation.TickCount - 1;
                    brain.Events.Queued = record => log.AppendLine(CultureInfo.InvariantCulture,
                        $"[pwq] tick={Counter()} id={id} type={(int)record.Type} filter=0x{(int)record.Filter:X} radius={record.Radius:0} source={record.Source} subject={record.Subject} pos=({record.Position.X:0},{record.Position.Y:0},{record.Position.Z:0})");
                    brain.Dispatched = (type, priority) => log.AppendLine(CultureInfo.InvariantCulture,
                        $"[pwev] tick={Counter()} id={id} type={(int)type} priority={priority}");
                    Action<Vector3d, Vector3d, int>? cast = brain.SightCast;
                    brain.SightCast = (from, to, target) =>
                    {
                        log.AppendLine(CultureInfo.InvariantCulture, $"[pwlos] tick={Counter()} looker={id} target={target}");
                        cast?.Invoke(from, to, target);
                    };
                }

                if (brain.Activity == AiSoldierActivity.Dead)
                {
                    // A corpse's origin each tick (retail keeps logging its dead): the level counter, feet + root.
                    int deadTick = soldiers.Simulation.TickCount - 1;
                    log.AppendLine(CultureInfo.InvariantCulture,
                        $"[pwdead] lt={deadTick} id={view.Soldier.Id} pos=({brain.Position.X:0.0},{brain.Position.Y:0.0},{brain.Position.Z + view.RootSample.Z:0.0}) clip={view.CurrentAnimation} facing={brain.Facing:0.00000} fall=({brain.DeathFallVelocity.X:0.00},{brain.DeathFallVelocity.Y:0.00})");
                }

                int tick = brain.LastTick;
                if (!tracks.TryGetValue(brain, out var track))
                {
                    track = (-1, -99, -99, -99, -99, -99, -1000, -1, -1);
                }

                if (track.Tick == tick)
                {
                    continue;
                }

                // OPENIGI_PATROL_CAPTURE_SLOTS=<looker>:<target>[,...]: the probe's [pwslot], at the end of the tick.
                foreach ((int looker, int target) in slotPairs)
                {
                    if (looker == view.Soldier.Id)
                    {
                        log.AppendLine(CultureInfo.InvariantCulture,
                            $"[pwslot] tick={tick} looker={looker} target={target} {brain.SlotState(target)}");
                    }
                }

                lastTick = Math.Max(lastTick, tick);
                AiPatrolRoute? route = brain.Route;
                int index = route?.CurrentIndex ?? -1;
                int deadline = route?.Deadline ?? -1;
                int state = brain.Body.State;
                int alert = brain.Alerted ? 1 : 0;
                int sight = brain.SeesEnemy ? 1 : 0;
                bool changed = state != track.State || index != track.Index || deadline != track.Deadline
                    || brain.FollowState != lastFollow.GetValueOrDefault(brain)
                    || brain.CurrentNode != track.Node || brain.DestinationNode != track.Dest
                    || alert != track.Alert || sight != track.Sight;
                if (!changed && tick - track.Logged < every)
                {
                    tracks[brain] = track with { Tick = tick };
                    continue;
                }

                tracks[brain] = (tick, state, index, deadline, brain.CurrentNode, brain.DestinationNode, tick, alert, sight);
                lastFollow[brain] = brain.FollowState;
                Vector3d eyeAt = brain.Eye.Position;
                Matrix3f em = brain.Eye.Orientation;
                log.AppendLine(CultureInfo.InvariantCulture,
                    $"[pweye] tick={tick} id={view.Soldier.Id} eye=({eyeAt.X:0},{eyeAt.Y:0},{eyeAt.Z:0}) m=[{em.M11:0.0000} {em.M12:0.0000} {em.M13:0.0000} | {em.M21:0.0000} {em.M22:0.0000} {em.M23:0.0000} | {em.M31:0.0000} {em.M32:0.0000} {em.M33:0.0000}] bone={BoneText(view)} clip={view.CurrentAnimation}");
                log.AppendLine(CultureInfo.InvariantCulture, $"[pwgun] tick={tick} id={view.Soldier.Id} {HandText(view)}");
                Matrix3f m = brain.Orientation;
                Vector3d p = brain.Position;
                AiPatrolCommand? command = route?.Current;
                log.AppendLine(CultureInfo.InvariantCulture,
                    $"[pw] tick={tick} id={view.Soldier.Id} type={brain.Type} pos=({p.X:0.0},{p.Y:0.0},{p.Z:0.0}) f=({m.M11:0.00000},{m.M12:0.00000},{m.M21:0.00000},{m.M22:0.00000}) state={state} patrol={(route is null ? 0 : 1)} path={route?.Path.Id ?? -1} cmd={index} kind={(int)(command?.Kind ?? (AiPatrolCommandKind)(-9))} op={command?.Operand ?? 0} deadline={deadline} crouch={(route?.IsCrouching == true ? 1 : 0)} walk={(route?.IsWalking == true ? 1 : 0)} node={brain.CurrentNode} dest={brain.DestinationNode} alert={alert} sight={sight} act={brain.Activity} combat={brain.PatrolAction} moving={brain.IsMovingInCombat} follow={brain.FollowState} vphase={brain.SweepPhase} clip={view.CurrentAnimation} ct={view.Bones.SourceOf(0)?.Time ?? -1f:0.0000} dur={view.Bones.SourceOf(0)?.Animation.Duration ?? -1f:0.0000} root={RootText(view)} nvis={brain.PatrolNotVisibleTicks} aim={brain.AimText} claim={brain.ClaimedNodeId} cpos={brain.ClaimedPositionText} wp={(brain.DebugWaypoint is { } w ? FormattableString.Invariant($"({w.X:0.0},{w.Y:0.0})") : "-")} lfs={brain.LastFreeSearch}");
            }

            if (lastTick >= ticks)
            {
                break;
            }
        }

        File.WriteAllText(output, log.ToString());
    }

    // The playing clip's sampled root and its last delta, against the probe's nref/nd.
    private static string RootText(SoldierView view) => view.Bones.SourceOf(0) is { } clip
        ? FormattableString.Invariant(
            $"({clip.SampleRoot().X:0.00},{clip.SampleRoot().Y:0.00},{clip.SampleRoot().Z:0.00}) d=({clip.RootDelta.X:0.00},{clip.RootDelta.Y:0.00},{clip.RootDelta.Z:0.00})")
        : "-";

    // The eye bone's rotation in the unrotated pose, for the [pweye] comparison against retail's palette entry.
    // The hand the gun hangs from, from the origin (feet + root), in the pose now and in the pose a tick on.
    private static string HandText(SoldierView view)
    {
        string Of(OpenIGI.Formats.Animation.BonePose pose)
        {
            if (HumanAttachPoints.ThirdPersonWeapon >= pose.BoneCount)
            {
                return "-";
            }

            Vector3f hand = view.Orientation.Transposed().Transform(pose.Positions[HumanAttachPoints.ThirdPersonWeapon]);
            return FormattableString.Invariant(
                $"({view.Position.X + hand.X:0.0},{view.Position.Y + hand.Y:0.0},{view.Position.Z + view.RootSample.Z + hand.Z:0.0})");
        }

        return $"pos={Of(view.UnrotatedPose())} ahead={Of(view.UnrotatedPoseAhead())}";
    }

    private static string BoneText(SoldierView view)
    {
        OpenIGI.Formats.Animation.BonePose pose = view.UnrotatedPose();
        if (HumanAttachPoints.AiEye >= pose.BoneCount)
        {
            return "-";
        }

        Matrix3f b = pose.Rotations[HumanAttachPoints.AiEye];
        return FormattableString.Invariant(
            $"[{b.M11:0.0000} {b.M12:0.0000} {b.M13:0.0000} | {b.M21:0.0000} {b.M22:0.0000} {b.M23:0.0000} | {b.M31:0.0000} {b.M32:0.0000} {b.M33:0.0000}]");
    }

    private GameHost Host(RecordingGraphicsDevice device, HeadlessGameWindow window, params string[] args) =>
        _support.Host(device, window, args);
}
