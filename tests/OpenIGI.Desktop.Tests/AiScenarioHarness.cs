using OpenIGI.Game.Physics;
using System.Globalization;
using System.Reflection;
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

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// A diagnostic run, not a check: a real mission played headless with the player placed in front of a pair
/// of guards, every tick of both brains written to a trace file.
/// </summary>
/// <remarks>
/// Runs only when <c>OPENIGI_AI_SCENARIO</c> names the trace file. <c>OPENIGI_AI_SCENARIO_MISSION</c> picks
/// the mission (default 1), <c>OPENIGI_AI_SCENARIO_TICKS</c> the length (default 600),
/// <c>OPENIGI_AI_SCENARIO_RANGE</c> how far in front of the first guard the player stands in metres
/// (default 10), and <c>OPENIGI_AI_SCENARIO_PAIR</c> ("a,b" soldier task ids) overrides the pair choice,
/// which is otherwise the two closest living enemies of the player.
/// </remarks>
public sealed class AiScenarioHarness : IDisposable
{
    private const double Metre = 4096.0;
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), $"openigi-aiscn-{Guid.NewGuid():N}");

    private LogRouter? _log;

    public AiScenarioHarness() => Directory.CreateDirectory(_scratch);

    [RequiresGameDataFact]
    public void PlayerInFrontOfAGuardPair()
    {
        string? output = Environment.GetEnvironmentVariable("OPENIGI_AI_SCENARIO");
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        int mission = EnvInt("OPENIGI_AI_SCENARIO_MISSION", 1);
        int ticks = EnvInt("OPENIGI_AI_SCENARIO_TICKS", 600);
        double range = EnvInt("OPENIGI_AI_SCENARIO_RANGE", 10) * Metre;

        StringBuilder log = new();

        // Every tracer a soldier gun's march files (0x47C4C0), as the probe's [tracer] add.
        OpenIGI.Game.Igi1.Weapons.GunShotTask.TracerFiled = (task, tracer) => log.AppendLine(FormattableString.Invariant(
            $"[tracer] add count={task.Tracers.Count} from=({tracer.Start.X:0},{tracer.Start.Y:0},{tracer.Start.Z:0}) to=({tracer.End.X:0},{tracer.End.Y:0},{tracer.End.Z:0}) head={tracer.StartDistance:0} tail={tracer.EndDistance:0} range={tracer.Range:0}"));
        using RecordingGraphicsDevice device = new();
        HeadlessGameWindow window = new();
        List<string> hostArgs = ["-mission", mission.ToString(CultureInfo.InvariantCulture), "-skipcutscene", "-lockstep", "-frames", "1000000"];
        if (Environment.GetEnvironmentVariable("OPENIGI_AI_SCENARIO_DIFFICULTY") is { Length: > 0 } difficultyText)
        {
            hostArgs.AddRange(["-difficulty", difficultyText]);
        }

        GameHost host = Host(device, window, [.. hostArgs]);
        host.Start();
        InputState scripted = new();
        host.ScriptedViewInput = scripted;
        int weaponKey = EnvInt("OPENIGI_AI_SCENARIO_WEAPONKEY", 0);
        double aimDrop = Environment.GetEnvironmentVariable("OPENIGI_AI_SCENARIO_AIMDROP") is { Length: > 0 } dropText
            ? double.Parse(dropText, CultureInfo.InvariantCulture)
            : 0.2;
        bool aimAtA = EnvInt("OPENIGI_AI_SCENARIO_AIM", 0) != 0;
        HashSet<int> fireTicks = [.. (Environment.GetEnvironmentVariable("OPENIGI_AI_SCENARIO_FIRE") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => int.Parse(t, CultureInfo.InvariantCulture))];

        FieldInfo playerField = typeof(GameHost).GetField("_player", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo soldiersField = typeof(GameHost).GetField("_soldiers", BindingFlags.NonPublic | BindingFlags.Instance)!;

        PlayerCamera? player = null;
        SoldierRenderer? soldiers = null;
        for (int i = 0; i < 20_000 && (player is null || soldiers is null); i++)
        {
            Assert.True(host.Step());
            player = playerField.GetValue(host) as PlayerCamera;
            soldiers = soldiersField.GetValue(host) as SoldierRenderer;
        }

        Assert.NotNull(player);
        Assert.NotNull(soldiers);
        // The level is withheld through its warm-up and insertion: wait until the AI has run a second.
        for (int i = 0; i < 100_000 && soldiers!.Simulation.TickCount < 30; i++)
        {
            Assert.True(host.Step());
        }

        Assert.True(soldiers!.Simulation.TickCount >= 30, "the AI never ticked");

        IReadOnlyList<KeyValuePair<SoldierView, Igi1HumanAi>> pairs = soldiers.Simulation.BrainPairsForTesting;
        int playerTeam = soldiers.Simulation.PlayerTeam;
        log.AppendLine(CultureInfo.InvariantCulture, $"player team {playerTeam} at {Fmt(player.Body.Position)}");

        // OPENIGI_AI_SCENARIO_SETVAR=<script variable>=<value>@<tick>: the probe's setvar -- e.g. mission 8's
        // EditVariable_50.nValue=1@60, which is AlarmControl_97's Alarm Expression, raises the alarm the level's way.
        (string Name, int Value, int Tick)? setVar = null;
        if (Environment.GetEnvironmentVariable("OPENIGI_AI_SCENARIO_SETVAR") is { Length: > 0 } setText)
        {
            string[] nameRest = setText.Split('=', 2);
            string[] valueTick = nameRest[1].Split('@');
            setVar = (nameRest[0], int.Parse(valueTick[0], CultureInfo.InvariantCulture), int.Parse(valueTick[1], CultureInfo.InvariantCulture));
        }
        foreach ((SoldierView view, Igi1HumanAi brain) in pairs)
        {
            log.AppendLine(
                CultureInfo.InvariantCulture,
                $"soldier {view.Soldier.Id} team {view.Soldier.Team} type {brain.Type} arm {brain.Arm} active {ConditionalContainerGate.IsTaskActive(view.Soldier)} {brain.DescribeTick()}");
        }

        List<KeyValuePair<SoldierView, Igi1HumanAi>> enemies = [.. pairs.Where(p =>
            p.Key.Soldier.Team != playerTeam
            && !p.Key.IsDead
            && ConditionalContainerGate.IsTaskActive(p.Key.Soldier)
            && p.Value.Arm != AiCombatArm.None)];

        KeyValuePair<SoldierView, Igi1HumanAi> first;
        KeyValuePair<SoldierView, Igi1HumanAi> second;
        if (Environment.GetEnvironmentVariable("OPENIGI_AI_SCENARIO_ALARM") is { Length: > 0 })
        {
            // A guard its script gave an alarm trigger, and its nearest living enemy-of-the-player neighbour.
            first = enemies.First(p => p.Value.AlarmTriggerId != -1);
            KeyValuePair<SoldierView, Igi1HumanAi> alarmGuard = first;
            second = enemies.Where(p => !ReferenceEquals(p.Value, alarmGuard.Value))
                .OrderBy(p => Horizontal(p.Value.Position, alarmGuard.Value.Position))
                .First();
        }
        else if (Environment.GetEnvironmentVariable("OPENIGI_AI_SCENARIO_PAIR") is { Length: > 0 } pairText)
        {
            int[] ids = [.. pairText.Split(',').Select(t => int.Parse(t, CultureInfo.InvariantCulture))];
            first = pairs.First(p => p.Key.Soldier.Id == ids[0]);
            second = pairs.First(p => p.Key.Soldier.Id == ids[1]);
        }
        else
        {
            double best = double.MaxValue;
            first = second = default;
            for (int i = 0; i < enemies.Count; i++)
            {
                for (int j = i + 1; j < enemies.Count; j++)
                {
                    double d = Horizontal(enemies[i].Value.Position, enemies[j].Value.Position);
                    if (d > 0.5 * Metre && d < best)
                    {
                        best = d;
                        first = enemies[i];
                        second = enemies[j];
                    }
                }
            }
        }

        Assert.NotNull(first.Value);
        Igi1HumanAi a = first.Value;
        Igi1HumanAi b = second.Value;
        log.AppendLine(
            CultureInfo.InvariantCulture,
            $"pair: A=s{first.Key.Soldier.Id} B=s{second.Key.Soldier.Id} {Horizontal(a.Position, b.Position) / Metre:0.00} m apart");

        // A spot A can see: in front of it, level with its feet, the first of a fan of candidates whose
        // mark is inside A's cone with a clear sight ray. Its body's forward is the local +Y axis.
        Vector3d place = default;
        bool found = false;
        foreach (double metres in new[] { range / Metre, 8.0, 12.0, 6.0, 15.0, 4.0, 20.0 })
        {
            foreach (double degrees in new[] { 0.0, 15.0, -15.0, 30.0, -30.0, 45.0, -45.0 })
            {
                double yaw = degrees * Math.PI / 180.0;
                Vector3d local = new(Math.Sin(yaw), Math.Cos(yaw), 0.0);
                Vector3d direction = a.Orientation.Transposed().Transform(local);
                Vector3d candidate = new(
                    a.Position.X + (direction.X * metres * Metre),
                    a.Position.Y + (direction.Y * metres * Metre),
                    a.Position.Z);
                Vector3d mark = candidate + new Vector3d(0.0, 0.0, Igi1HumanAi.DetectionMarkAboveFeet(crouching: false));
                var probe = a.ProbeDetection(mark);
                if (!found && probe.InCone && probe.LineOfSightClear)
                {
                    place = candidate;
                    found = true;
                    log.AppendLine(CultureInfo.InvariantCulture, $"placing the player {metres} m out at {degrees} degrees: {Fmt(place)}");
                }
            }
        }

        // OPENIGI_AI_SCENARIO_NEAR="metres,degrees": the probe's `near` -- off A's facing, visible or not.
        if (Environment.GetEnvironmentVariable("OPENIGI_AI_SCENARIO_NEAR") is { Length: > 0 } nearText)
        {
            double[] near = [.. nearText.Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture))];
            double heading = a.Facing + (near[1] * Math.PI / 180.0);
            place = new(
                a.Position.X - (Math.Sin(heading) * near[0] * Metre),
                a.Position.Y + (Math.Cos(heading) * near[0] * Metre),
                a.Position.Z);
            found = true;
            log.AppendLine(CultureInfo.InvariantCulture, $"placing the player {near[0]} m out at {near[1]} degrees off A's facing: {Fmt(place)}");
        }

        // OPENIGI_AI_SCENARIO_PLACE="x,y,z": the player's feet exactly there, in world units -- a spot taken from a
        // report or the telemetry overlay.
        if (Environment.GetEnvironmentVariable("OPENIGI_AI_SCENARIO_PLACE") is { Length: > 0 } placeText)
        {
            double[] at = [.. placeText.Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture))];
            place = new Vector3d(at[0], at[1], at[2]);
            found = true;
            log.AppendLine(CultureInfo.InvariantCulture, $"placing the player at {Fmt(place)}");
        }

        if (!found)
        {
            Vector3d forward = a.Orientation.Transposed().Transform(new Vector3d(0.0, 1.0, 0.0));
            place = new(a.Position.X + (forward.X * range), a.Position.Y + (forward.Y * range), a.Position.Z);
            log.AppendLine(CultureInfo.InvariantCulture, $"no clear spot in front of A; forcing {Fmt(place)}");
        }

        foreach (string line in a.DescribeNodesNear(place, 30.0 * Metre))
        {
            log.AppendLine(line);
        }

        // OPENIGI_AI_SCENARIO_RAY="x1,y1,z1,x2,y2,z2": what the port's AI sight ray meets between two points.
        if (Environment.GetEnvironmentVariable("OPENIGI_AI_SCENARIO_RAY") is { Length: > 0 } rayText)
        {
            double[] r = [.. rayText.Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture))];
            log.AppendLine(
                CultureInfo.InvariantCulture,
                $"RAY {soldiers.Simulation.DescribeSightForTesting(new Vector3d(r[0], r[1], r[2]), new Vector3d(r[3], r[4], r[5]))}");
        }

        Dictionary<Igi1HumanAi, int> engagedAt = [];
        Dictionary<Igi1HumanAi, AiEvent> firstReason = [];
        Dictionary<Igi1HumanAi, Vector3d> starts = pairs.ToDictionary(p => p.Value, p => p.Value.Position);
        int lastLogged = -1;
        for (int step = 0; step < ticks * 8; step++)
        {
            int engine = soldiers!.Simulation.TickCount;
            if (engine - lastLogged < 1 && step > 0)
            {
                // Log once per engine tick.
            }

            // Teleported once, as the probe's `near` does; the body falls and stands from there.
            if (step == 0)
            {
                player.Body.Position = place;
            }

            if (setVar is { } write && engine == write.Tick)
            {
                var loop = (OpenIGI.Engine.Flow.GameLoop)typeof(GameHost).GetField("_loop", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
                bool wrote = loop.Variables.TryWrite(write.Name, OpenIGI.Scripting.Runtime.QvmValue.FromInt32(write.Value));
                log.AppendLine(CultureInfo.InvariantCulture, $"t{engine} setvar {write.Name}={write.Value} {(wrote ? "written" : "NOT FOUND")}");
                setVar = null;
            }

            if (aimAtA && a.Activity != AiSoldierActivity.Dead)
            {
                // The probe's `aimat`: the body onto A, the view at its chest (+32 is the same reference
                // on both Humans, so the view drops by the eye's height over it less a chest's).
                double dx = a.Position.X - player.Body.Position.X;
                double dy = a.Position.Y - player.Body.Position.Y;
                double dz = a.Position.Z - player.Body.Position.Z - (aimDrop * Metre);
                player.Body.Yaw = (float)Math.Atan2(dx, dy);  // the port's player faces (sin, cos)
                player.Body.Pitch = (float)Math.Atan2(dz, Math.Sqrt((dx * dx) + (dy * dy)));
            }
            else if (step == 0)
            {
                // Facing A, as the probe's `near` does. The port's player faces (sin yaw, cos yaw).
                player.Body.Yaw = (float)Math.Atan2(a.Position.X - place.X, a.Position.Y - place.Y);
            }

            player.Body.Health.Reset();
            if (!host.Step())
            {
                log.AppendLine(CultureInfo.InvariantCulture, $"the host ended at step {step}");
                break;
            }

            engine = soldiers!.Simulation.TickCount;
            if (engine == lastLogged)
            {
                continue;
            }

            lastLogged = engine;

            // The scenario's own inputs, by ticks since the player was placed: the weapon key on the
            // first tick, released on the third, and a single-tick trigger pull on each fire tick.
            int since = EnvInt("OPENIGI_AI_SCENARIO_TICKS", 600) - ticks;
            if (weaponKey > 0)
            {
                scripted.Keyboard.SetKey(KeyCode.D1 + (byte)(weaponKey - 1), down: since is >= 1 and < 3);
            }

            scripted.Mouse.SetButton(MouseButton.Left, down: fireTicks.Contains(since));
            if (player.FiredThisTick)
            {
                log.AppendLine(CultureInfo.InvariantCulture, $"t{engine} PLAYER FIRED {player.HeldWeaponType?.ScriptId} hits={player.HumanHits} yaw={player.Body.Yaw * 180 / Math.PI:0.0} pitch={player.Body.Pitch * 180 / Math.PI:0.0}");
            }

            foreach ((SoldierView view, Igi1HumanAi brain) in pairs)
            {
                if (brain.LastDispatchedEvent is { } reason
                    && reason is not (AiEvent.Idle or AiEvent.Animation or AiEvent.Create or AiEvent.AlarmOn or AiEvent.AlarmOff)
                    && firstReason.TryAdd(brain, reason))
                {
                    log.AppendLine(
                        CultureInfo.InvariantCulture,
                        $"t{engine} FIRST s{view.Soldier.Id} {reason} after {since} tick(s), {Horizontal(brain.Position, place) / Metre:0.0} m from the player");
                }

                if (since is 1 or 3 or 10)
                {
                    log.AppendLine(CultureInfo.InvariantCulture, $"t{engine} EYE s{view.Soldier.Id} inView={soldiers.IsInViewForTesting(view)} {soldiers.DescribeViewForTesting(view)} {brain.DescribeTick()}");
                }

                if (brain.Activity == AiSoldierActivity.Engaging && !engagedAt.ContainsKey(brain))
                {
                    engagedAt[brain] = since;
                    log.AppendLine(
                        CultureInfo.InvariantCulture,
                        $"t{engine} ENGAGED s{view.Soldier.Id} after {since} tick(s), {Horizontal(brain.Position, place) / Metre:0.0} m from the player, on {brain.LastDispatchedEvent}");
                }
            }

            log.AppendLine(CultureInfo.InvariantCulture, $"t{engine} player {Fmt(player.Body.Position)}");
            if (since is 1 or 2 or 3 or 5 or 10 or 40)
            {
                Vector3d playerMark = player.Body.Position + new Vector3d(0.0, 0.0, Igi1HumanAi.DetectionMarkAboveFeet(crouching: false));
                log.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"t{engine} SIGHT A eye={Fmt(a.EyePosition())} -> {Fmt(playerMark)}: {soldiers.Simulation.DescribeSightForTesting(a.EyePosition(), playerMark)}");
            }
            log.AppendLine(CultureInfo.InvariantCulture, $"  A {a.DescribeTick()}");
            log.AppendLine(
                CultureInfo.InvariantCulture,
                $"  B {b.DescribeTick()} dA={Horizontal(a.Position, b.Position) / Metre:0.0} search={b.DescribeFreeSearch()} last={b.LastFreeSearch}");
            if (--ticks <= 0)
            {
                break;
            }
        }

        log.AppendLine("movement over the run:");
        foreach ((SoldierView view, Igi1HumanAi brain) in pairs)
        {
            log.AppendLine(
                CultureInfo.InvariantCulture,
                $"  s{view.Soldier.Id} moved {Horizontal(starts[brain], brain.Position) / Metre:0.00} m active {ConditionalContainerGate.IsTaskActive(view.Soldier)} {brain.DescribeTick()}");
        }

        host.Stop();
        File.WriteAllText(output, log.ToString());
    }

    /// <summary>
    /// The probe's spawn shoot-out: the player never moves from the mission's spawn, turns each tick onto the
    /// nearest living guard it has a clear line to, and taps the trigger only while one is in sight.
    /// </summary>
    /// <remarks>
    /// Runs only when <c>OPENIGI_AI_SPAWN</c> names the trace file. Mirrors the probe script
    /// <c>god 1 / 5 weapon N / 10 aimat nearest DROP / fire 2 every 12 ticks from 30 / reload 2 every 150
    /// from 160</c>: <c>OPENIGI_AI_SCENARIO_MISSION</c>, <c>_TICKS</c> (1200), <c>_DIFFICULTY</c>,
    /// <c>_WEAPONKEY</c> (2) and <c>_AIMDROP</c> (0.3). Sight is the AI's own ray, from the player's eye to
    /// where the aim line crosses the guard, so nothing behind a wall or a crate is shot at.
    /// </remarks>
    [RequiresGameDataFact]
    public void StandAtSpawnAndShoot()
    {
        string? output = Environment.GetEnvironmentVariable("OPENIGI_AI_SPAWN");
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        int mission = EnvInt("OPENIGI_AI_SCENARIO_MISSION", 2);
        int ticks = EnvInt("OPENIGI_AI_SCENARIO_TICKS", 1200);
        int weaponKey = EnvInt("OPENIGI_AI_SCENARIO_WEAPONKEY", 2);
        double drop = Environment.GetEnvironmentVariable("OPENIGI_AI_SCENARIO_AIMDROP") is { Length: > 0 } dropText
            ? double.Parse(dropText, CultureInfo.InvariantCulture)
            : 0.3;
        // The aim line leaves the player's eye (feet + PlayerBody.StandingEyeHeight, retail's origin + 0.69)
        // pitched as origin-to-origin less the drop, so at the guard it crosses feet + eye height - drop.
        double eyeHeight = PlayerBody.StandingEyeHeight / Metre;

        StringBuilder log = new();
        using RecordingGraphicsDevice device = new();
        List<string> hostArgs = ["-mission", mission.ToString(CultureInfo.InvariantCulture), "-skipcutscene", "-lockstep", "-frames", "1000000"];
        if (Environment.GetEnvironmentVariable("OPENIGI_AI_SCENARIO_DIFFICULTY") is { Length: > 0 } difficultyText)
        {
            hostArgs.AddRange(["-difficulty", difficultyText]);
        }

        GameHost host = Host(device, new HeadlessGameWindow(), [.. hostArgs]);
        host.Start();
        InputState scripted = new();
        host.ScriptedViewInput = scripted;
        FieldInfo playerField = typeof(GameHost).GetField("_player", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo soldiersField = typeof(GameHost).GetField("_soldiers", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo roundsField = typeof(GameHost).GetField("_guardRounds", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo hitsField = typeof(GameHost).GetField("_guardHits", BindingFlags.NonPublic | BindingFlags.Instance)!;

        PlayerCamera? player = null;
        SoldierRenderer? soldiers = null;
        for (int i = 0; i < 20_000 && (player is null || soldiers is null); i++)
        {
            Assert.True(host.Step());
            player = playerField.GetValue(host) as PlayerCamera;
            soldiers = soldiersField.GetValue(host) as SoldierRenderer;
        }

        Assert.NotNull(player);
        Assert.NotNull(soldiers);
        Vector3d spawn = player.Body.Position;
        log.AppendLine(CultureInfo.InvariantCulture, $"spawn {Fmt(spawn)} team {soldiers.Simulation.PlayerTeam}");

        IReadOnlyList<KeyValuePair<SoldierView, Igi1HumanAi>> pairs = soldiers.Simulation.BrainPairsForTesting;
        bool IsEnemy(SoldierView view) =>
            view.Soldier.Team != soldiers.Simulation.PlayerTeam && ConditionalContainerGate.IsTaskActive(view.Soldier);

        int since = -1;
        int last = -1;
        int shots = 0;
        int lastTarget = int.MinValue;
        for (int step = 0; step < ticks * 40 && since < ticks; step++)
        {
            // Never moves: pinned to the spawn, and never dies (the probe's god 1).
            player.Body.Position = new Vector3d(spawn.X, spawn.Y, player.Body.Position.Z);
            player.Body.Health.Reset();

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
            since++;

            // aimat nearest: the nearest living guard in clear sight from the eye.
            Vector3d feet = player.Body.Position;
            Vector3d eye = feet + new Vector3d(0.0, 0.0, eyeHeight * Metre);
            SoldierView? target = null;
            double best = double.MaxValue;
            foreach ((SoldierView view, Igi1HumanAi _) in pairs)
            {
                if (!IsEnemy(view) || view.IsDead)
                {
                    continue;
                }

                double distance = Vector3d.Distance(view.Position, feet);
                Vector3d point = view.Position + new Vector3d(0.0, 0.0, (eyeHeight - drop) * Metre);
                if (distance < best && soldiers.Simulation.ClearSightForTesting(eye, point))
                {
                    best = distance;
                    target = view;
                }
            }

            int targetId = target?.Soldier.Id ?? -1;
            if (targetId != lastTarget)
            {
                log.AppendLine(CultureInfo.InvariantCulture, $"t{since} aim target -> {targetId} at {(target is null ? -1.0 : best / Metre):0.0} m");
                if (target is not null)
                {
                    Vector3d point = target.Position + new Vector3d(0.0, 0.0, (eyeHeight - drop) * Metre);
                    log.AppendLine(CultureInfo.InvariantCulture, $"   LOS eye {eye.X:0.0} {eye.Y:0.0} {eye.Z:0.0} -> {point.X:0.0} {point.Y:0.0} {point.Z:0.0}: {soldiers.Simulation.DescribeSightForTesting(eye, point)}");
                }
                lastTarget = targetId;
            }

            if (target is not null)
            {
                double dx = target.Position.X - feet.X;
                double dy = target.Position.Y - feet.Y;
                double dz = target.Position.Z - feet.Z - (drop * Metre);
                player.Body.Yaw = (float)Math.Atan2(dx, dy);  // the port's player faces (sin, cos)
                player.Body.Pitch = (float)Math.Atan2(dz, Math.Sqrt((dx * dx) + (dy * dy)));
            }

            // The probe script's buttons, by tick: weapon category at 5, fire 2 ticks every 12 from 30
            // (swallowed with nothing in sight), reload 2 ticks every 150 from 160.
            if (weaponKey > 0)
            {
                scripted.Keyboard.SetKey(KeyCode.D1 + (byte)(weaponKey - 1), down: since == 5);
            }

            bool fireTick = since >= 30 && (since - 30) % 12 < 2;
            scripted.Mouse.SetButton(MouseButton.Left, down: fireTick && target is not null);
            scripted.Keyboard.SetKey(KeyCode.R, down: since >= 160 && (since - 160) % 150 < 2);

            if (player.FiredThisTick)
            {
                shots++;
            }

            if (since % 20 == 0)
            {
                StringBuilder field = new();
                foreach ((SoldierView view, Igi1HumanAi brain) in pairs)
                {
                    double distance = Vector3d.Distance(view.Position, feet);
                    if (IsEnemy(view) && distance <= 80.0 * Metre)
                    {
                        field.Append(CultureInfo.InvariantCulture, $" {view.Soldier.Id}:{distance / Metre:0.0}m/{soldiers.Simulation.DamageForTesting(view):0.00}{(view.IsDead ? "X" : string.Empty)}");
                        if (view.Soldier.Id == EnvInt("OPENIGI_AI_SCENARIO_TRACK", -1))
                        {
                            field.Append(CultureInfo.InvariantCulture, $"@{view.Position.X / Metre:0.00},{view.Position.Y / Metre:0.00}[{brain.Activity}]");
                        }
                    }
                }

                log.AppendLine(CultureInfo.InvariantCulture, $"[field] t{since}{field}");
                foreach ((SoldierView view, Igi1HumanAi brain) in pairs)
                {
                    if (IsEnemy(view) && !view.IsDead
                        && brain.Activity is not (AiSoldierActivity.Idle or AiSoldierActivity.Patrolling))
                    {
                        log.AppendLine(CultureInfo.InvariantCulture, $"   s{view.Soldier.Id} {brain.DescribeTick()}");
                    }
                }
            }
        }

        log.AppendLine(
            CultureInfo.InvariantCulture,
            $"player shots={shots} guard rounds={roundsField.GetValue(host)} hits on player={hitsField.GetValue(host)}");
        foreach ((SoldierView view, Igi1HumanAi brain) in pairs)
        {
            if (IsEnemy(view) && Vector3d.Distance(view.Position, spawn) <= 80.0 * Metre)
            {
                log.AppendLine(CultureInfo.InvariantCulture, $"  s{view.Soldier.Id} dmg={soldiers.Simulation.DamageForTesting(view):0.00} dead={view.IsDead} {brain.DescribeTick()}");
            }
        }

        host.Stop();
        File.WriteAllText(output, log.ToString());
    }

    /// <summary>
    /// The probe's "provoke one guard and take its fire": the player stands at a fixed spot, shoots one guard
    /// once with the Glock so it targets him, then stands still, tracking it, while every guard round is logged.
    /// </summary>
    /// <remarks>
    /// Runs only when <c>OPENIGI_AI_STAND</c> names the trace file. Mirrors the probe script
    /// <c>god 1 / teleport X Y Z+3877.2 YAW / weapon 2 at 10 / aimat ID 0.2 at 50 / fire 1 at 70</c>:
    /// <c>OPENIGI_AI_STAND_POS</c> is the port's feet in units ("x,y,z"), <c>_TARGET</c> the guard's task id,
    /// <c>OPENIGI_AI_SCENARIO_MISSION</c> (1), <c>_TICKS</c> (1800) and <c>_DIFFICULTY</c>. Pair it with
    /// <c>OPENIGI_AI_SCENARIO_LOG</c> and <c>OPENIGI_AI_SCENARIO_LOGSPEC=Weapon=debug</c> for each round's fate.
    /// </remarks>
    [RequiresGameDataFact]
    public void ProvokeOneGuardAndStand()
    {
        string? output = Environment.GetEnvironmentVariable("OPENIGI_AI_STAND");
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        double[] at = [.. (Environment.GetEnvironmentVariable("OPENIGI_AI_STAND_POS") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => double.Parse(word, CultureInfo.InvariantCulture))];
        Assert.Equal(3, at.Length);
        Vector3d stand = new(at[0], at[1], at[2]);
        int targetId = EnvInt("OPENIGI_AI_STAND_TARGET", -1);
        int mission = EnvInt("OPENIGI_AI_SCENARIO_MISSION", 1);
        int ticks = EnvInt("OPENIGI_AI_SCENARIO_TICKS", 1800);
        // aimat's drop below the guard's head line; OPENIGI_AI_STAND_DROP lowers it to wound rather than kill.
        double drop = Environment.GetEnvironmentVariable("OPENIGI_AI_STAND_DROP") is { Length: > 0 } standDrop
            ? double.Parse(standDrop, CultureInfo.InvariantCulture)
            : 0.2;

        StringBuilder log = new();
        using RecordingGraphicsDevice device = new();
        List<string> hostArgs = ["-mission", mission.ToString(CultureInfo.InvariantCulture), "-skipcutscene", "-lockstep", "-frames", "1000000"];
        if (Environment.GetEnvironmentVariable("OPENIGI_AI_SCENARIO_DIFFICULTY") is { Length: > 0 } difficultyText)
        {
            hostArgs.AddRange(["-difficulty", difficultyText]);
        }

        GameHost host = Host(device, new HeadlessGameWindow(), [.. hostArgs]);
        host.Start();
        InputState scripted = new();
        host.ScriptedViewInput = scripted;
        FieldInfo playerField = typeof(GameHost).GetField("_player", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo soldiersField = typeof(GameHost).GetField("_soldiers", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo roundsField = typeof(GameHost).GetField("_guardRounds", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo hitsField = typeof(GameHost).GetField("_guardHits", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo tickField = typeof(SoldierRenderer).GetField("_tick", BindingFlags.NonPublic | BindingFlags.Instance)!;

        PlayerCamera? player = null;
        SoldierRenderer? soldiers = null;
        for (int i = 0; i < 20_000 && (player is null || soldiers is null); i++)
        {
            Assert.True(host.Step());
            player = playerField.GetValue(host) as PlayerCamera;
            soldiers = soldiersField.GetValue(host) as SoldierRenderer;
        }

        Assert.NotNull(player);
        Assert.NotNull(soldiers);

        KeyValuePair<SoldierView, Igi1HumanAi> target = soldiers.Simulation.BrainPairsForTesting
            .FirstOrDefault(pair => pair.Key.Soldier.Id == targetId);
        Assert.NotNull(target.Key);
        log.AppendLine(CultureInfo.InvariantCulture, $"stand {Fmt(stand)} target {targetId} at {Fmt(target.Key.Position)}, {Vector3d.Distance(target.Key.Position, stand) / Metre:0.0} m");

        int since = -1;
        int last = -1;
        int lastRounds = 0;
        for (int step = 0; step < ticks * 40 && since < ticks; step++)
        {
            // Pinned to the spot and never dies (the probe's teleport and god 1).
            player.Body.Position = stand;
            player.Body.Health.Reset();

            if (!host.Step())
            {
                log.AppendLine(CultureInfo.InvariantCulture, $"the host ended at step {step}");
                break;
            }

            int engine = (int)tickField.GetValue(soldiers)!;
            if (engine == last)
            {
                continue;
            }

            last = engine;
            since++;

            // aimat ID 0.2 from tick 50 on, as the probe holds it: the guard's feet plus eye height less the drop.
            if (since >= 50)
            {
                Vector3d feet = player.Body.Position;
                double dx = target.Key.Position.X - feet.X;
                double dy = target.Key.Position.Y - feet.Y;
                double dz = target.Key.Position.Z - feet.Z - (drop * Metre);
                player.Body.Yaw = (float)Math.Atan2(dx, dy);  // the port's player faces (sin, cos)
                player.Body.Pitch = (float)Math.Atan2(dz, Math.Sqrt((dx * dx) + (dy * dy)));
            }

            // One shot at 70; should it miss (retail's first round did too), another every 12 ticks until the
            // guard has been hurt -- the point is to be its target, as a hit makes it.
            bool hurt = soldiers.Simulation.DamageForTesting(target.Key) > 0f;
            scripted.Keyboard.SetKey(KeyCode.D2, down: since == 10);
            scripted.Mouse.SetButton(MouseButton.Left, down: since >= 70 && !hurt && (since - 70) % 12 == 0);
            if (player.FiredThisTick)
            {
                log.AppendLine(CultureInfo.InvariantCulture, $"t{since} player fired");
            }

            int rounds = (int)roundsField.GetValue(host)!;
            if (rounds != lastRounds || since % 60 == 0)
            {
                lastRounds = rounds;
                log.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"t{since} rounds={rounds} hits={hitsField.GetValue(host)} target {targetId} dmg={soldiers.Simulation.DamageForTesting(target.Key):0.00} {target.Value.DescribeTick()}");
            }
        }

        log.AppendLine(
            CultureInfo.InvariantCulture,
            $"guard rounds={roundsField.GetValue(host)} hits on player={hitsField.GetValue(host)}");
        host.Stop();
        File.WriteAllText(output, log.ToString());
    }

    /// <summary>
    /// The port half of the probe's <c>fence</c> capture: the same autopilot script, played headless, with the
    /// player's origin logged every tick in the probe's terms.
    /// </summary>
    /// <remarks>
    /// Runs only when <c>OPENIGI_FENCE_SCENARIO</c> names the trace file and <c>_SCRIPT</c> the probe script
    /// (<c>god</c>, <c>teleport x y z [yaw]</c>, <c>look yaw</c>, <c>use</c>, <c>pos</c>, <c>quit</c>; retail
    /// units and headings), <c>_MISSION</c> the mission (1). Each tick logs the port's task position, where the
    /// body model is drawn -- which is what the probe's <c>pos</c> (<c>Human+32</c>) is -- and the clip's root
    /// height, so the two logs line up record by record.
    /// </remarks>
    [RequiresGameDataFact]
    public void ScriptedFenceCapture()
    {
        string? output = Environment.GetEnvironmentVariable("OPENIGI_FENCE_SCENARIO");
        string? scriptPath = Environment.GetEnvironmentVariable("OPENIGI_FENCE_SCENARIO_SCRIPT");
        if (string.IsNullOrEmpty(output) || string.IsNullOrEmpty(scriptPath))
        {
            return;
        }

        const double OriginAboveFeet = 3877.2;
        int mission = EnvInt("OPENIGI_FENCE_SCENARIO_MISSION", 1);
        List<(int Tick, string Op, double[] Args)> script = [];
        foreach (string raw in File.ReadAllLines(scriptPath))
        {
            string[] words = raw.Split('#')[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2)
            {
                script.Add((
                    int.Parse(words[0], CultureInfo.InvariantCulture),
                    words[1].ToLowerInvariant(),
                    [.. words.Skip(2).Select(word => double.Parse(word, CultureInfo.InvariantCulture))]));
            }
        }

        StringBuilder log = new();
        using RecordingGraphicsDevice device = new();
        GameHost host = Host(device, new HeadlessGameWindow(), "-mission", mission.ToString(CultureInfo.InvariantCulture), "-skipcutscene", "-lockstep", "-frames", "1000000");
        host.Start();
        InputState scripted = new();
        host.ScriptedViewInput = scripted;
        FieldInfo playerField = typeof(GameHost).GetField("_player", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo soldiersField = typeof(GameHost).GetField("_soldiers", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo tickField = typeof(SoldierRenderer).GetField("_tick", BindingFlags.NonPublic | BindingFlags.Instance)!;

        PlayerCamera? player = null;
        SoldierRenderer? soldiers = null;
        for (int i = 0; i < 20_000 && (player is null || soldiers is null); i++)
        {
            Assert.True(host.Step());
            player = playerField.GetValue(host) as PlayerCamera;
            soldiers = soldiersField.GetValue(host) as SoldierRenderer;
        }

        Assert.NotNull(player);
        Assert.NotNull(soldiers);

        bool god = false;
        int useUntil = -1;
        int last = -1;
        int tick = -1;
        bool quit = false;
        for (int step = 0; step < 4_000_000 && !quit; step++)
        {
            if (god)
            {
                player.Body.Health.Reset();
            }

            scripted.Keyboard.SetKey(KeyCode.E, down: tick + 1 < useUntil);
            if (!host.Step())
            {
                log.AppendLine(CultureInfo.InvariantCulture, $"the host ended at step {step}");
                break;
            }

            int engine = (int)tickField.GetValue(soldiers)!;
            if (engine == last)
            {
                continue;
            }

            last = engine;
            tick++;

            // The probe's Human+32 is the model origin. Walking, the port keeps the feet, so the origin is
            // 3877.2 above them; in an interaction it is wherever the body model is drawn.
            Vector3d feet = player.Body.Position;
            Vector3d drawn = soldiers.PlayerBodyDrawnOriginForTesting;
            log.AppendLine(
                CultureInfo.InvariantCulture,
                $"[fence] tick={tick} interaction={player.IsInWorldInteraction} state={player.InteractionLocomotionState} "
                    + $"task=({feet.X:0.0},{feet.Y:0.0},{feet.Z:0.0}) drawn=({drawn.X:0.0},{drawn.Y:0.0},{drawn.Z:0.0}) "
                    + $"rootSampleZ={soldiers.PlayerBodyRootSampleZForTesting:0.0} walkOrigin=({feet.X:0},{feet.Y:0},{feet.Z + OriginAboveFeet:0}) "
                    + $"grounded={player.Body.IsGrounded}");

            foreach ((int at, string op, double[] args) in script.Where(command => command.Tick == tick))
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

                        break;
                    case "look":
                        player.Body.Yaw = (float)(-args[0] * Math.PI / 180.0);
                        if (args.Length >= 2)
                        {
                            player.Body.Pitch = (float)(args[1] * Math.PI / 180.0);
                        }

                        break;
                    case "use":
                        useUntil = tick + 1 + (args.Length >= 1 ? (int)args[0] : 1);
                        log.AppendLine(CultureInfo.InvariantCulture, $"[autopilot] tick={tick} use");
                        break;
                    case "quit":
                        quit = true;
                        break;
                }
            }
        }

        host.Stop();
        File.WriteAllText(output, log.ToString());
    }

    /// <summary>
    /// The port half of the probe's <c>generator</c> capture: the same autopilot script, played headless,
    /// with one Generator's state written in the probe's own record format.
    /// </summary>
    /// <remarks>
    /// Runs only when <c>OPENIGI_GEN_SCENARIO</c> names the trace file. <c>OPENIGI_GEN_SCENARIO_SCRIPT</c>
    /// is the probe script (<c>god</c>, <c>teleport x y z [yaw]</c>, <c>weapon n</c>, <c>aimpoint x y z</c>,
    /// <c>fire [ticks]</c>, <c>pos</c>, <c>quit</c>; retail units and headings), <c>_MISSION</c> the mission
    /// (7) and <c>_ID</c> the generator (99). Retail places the Human's origin, 3877.2 units above the feet the
    /// port keeps, so a teleport's z is lowered by that much; a retail heading g looks along (-sin g, cos g)
    /// and the port's yaw along (sin, cos), so the yaw is negated.
    /// </remarks>
    [RequiresGameDataFact]
    public void ScriptedGeneratorCapture()
    {
        string? output = Environment.GetEnvironmentVariable("OPENIGI_GEN_SCENARIO");
        string? scriptPath = Environment.GetEnvironmentVariable("OPENIGI_GEN_SCENARIO_SCRIPT");
        if (string.IsNullOrEmpty(output) || string.IsNullOrEmpty(scriptPath))
        {
            return;
        }

        const double OriginAboveFeet = 3877.2;
        int mission = EnvInt("OPENIGI_GEN_SCENARIO_MISSION", 7);
        int generatorId = EnvInt("OPENIGI_GEN_SCENARIO_ID", 99);
        string name = string.Create(CultureInfo.InvariantCulture, $"Generator_{generatorId}");
        List<(int Tick, string Op, double[] Args)> script = [];
        foreach (string raw in File.ReadAllLines(scriptPath))
        {
            string line = raw.Split('#')[0].Trim();
            string[] words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2)
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
        InputState scripted = new();
        host.ScriptedViewInput = scripted;
        FieldInfo playerField = typeof(GameHost).GetField("_player", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo soldiersField = typeof(GameHost).GetField("_soldiers", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo loopField = typeof(GameHost).GetField("_loop", BindingFlags.NonPublic | BindingFlags.Instance)!;

        PlayerCamera? player = null;
        SoldierRenderer? soldiers = null;
        for (int i = 0; i < 20_000 && (player is null || soldiers is null); i++)
        {
            Assert.True(host.Step());
            player = playerField.GetValue(host) as PlayerCamera;
            soldiers = soldiersField.GetValue(host) as SoldierRenderer;
        }

        Assert.NotNull(player);
        Assert.NotNull(soldiers);
        OpenIGI.Engine.Flow.GameLoop loop = (OpenIGI.Engine.Flow.GameLoop)loopField.GetValue(host)!;
        DestructibleObjectRegistry destructibles = MissionOf(host).Destructibles!;
        DestructibleObject? generator = destructibles.Objects.FirstOrDefault(item => item.Owner.Id == generatorId);
        log.AppendLine(CultureInfo.InvariantCulture, $"[generator] registered={generator is not null} keepsModel={generator?.KeepsModelWhenDestroyed}");
        if (generator is not null && EnvInt("OPENIGI_GEN_SCENARIO_NEARBY", 0) is > 0 and int nearby)
        {
            FieldInfo sceneField = typeof(GameHost).GetField("_scene", BindingFlags.NonPublic | BindingFlags.Instance)!;
            WorldScene scene = (WorldScene)sceneField.GetValue(host)!;
            foreach (WorldPlacement placement in scene.Placements)
            {
                Vector3d at = placement.Position * WorldScene.UnitsPerMetre;
                if (Environment.GetEnvironmentVariable("OPENIGI_GEN_SCENARIO_OBJ") is { Length: > 0 } objPath
                    && ReferenceEquals(placement.Owner, generator.Owner)
                    && MefMeshBuilder.Build(placement.Model) is { } mesh)
                {
                    // The generator's own mesh, model space, with each subset's texture name.
                    StringBuilder obj = new();
                    foreach (OpenIGI.Core.Graphics.Vertex3D vertex in mesh.Vertices)
                    {
                        obj.AppendLine(CultureInfo.InvariantCulture, $"v {vertex.Position.X} {vertex.Position.Y} {vertex.Position.Z}");
                        obj.AppendLine(CultureInfo.InvariantCulture, $"vt {vertex.DiffuseU} {vertex.DiffuseV}");
                    }

                    foreach (MeshSubset subset in mesh.Subsets)
                    {
                        string texture = subset.HasMaterial && placement.Pack.TryGetTextureName(placement.Instance, subset.MaterialSlot, out string name2)
                            ? name2
                            : "none";
                        obj.AppendLine(CultureInfo.InvariantCulture, $"usemtl {texture}");
                        for (int i = subset.IndexOffset; i < subset.IndexOffset + subset.IndexCount; i += 3)
                        {
                            obj.AppendLine(CultureInfo.InvariantCulture, $"f {mesh.Indices[i] + 1} {mesh.Indices[i + 1] + 1} {mesh.Indices[i + 2] + 1}");
                        }
                    }

                    File.WriteAllText(objPath, obj.ToString());
                }

                if (Vector3d.Distance(at, generator.PositionUnits) <= nearby * Metre)
                {
                    log.AppendLine(CultureInfo.InvariantCulture, $"[nearby] {placement.Instance.Name} owner={placement.Owner?.Name} task={placement.TaskName} at ({at.X:0}, {at.Y:0}, {at.Z:0}) d={Vector3d.Distance(at, generator.PositionUnits) / Metre:0.0} m");
                }
            }
        }

        int Read(string field) =>
            loop.Variables.TryRead($"{name}.{field}", out OpenIGI.Scripting.Runtime.QvmValue value) ? value.ToInt32() : -99;

        string State(string tag, int tick) => string.Create(
            CultureInfo.InvariantCulture,
            $"[generator] {tag} tick={tick} id={generatorId} isExploded={Read("isExploded")} isOn={Read("isOn")} backup={Read("nBackupTimer")} damage={generator?.ExplosionDamageAccumulated ?? 0f:0.000}/{generator?.ExplosionDamageThreshold ?? 0f:0.000}");

        string Pose(int tick) => string.Create(
            CultureInfo.InvariantCulture,
            $"[autopilot] POS tick={tick} pos=({player.Body.Position.X:0}, {player.Body.Position.Y:0}, {player.Body.Position.Z + OriginAboveFeet:0}) yaw={-player.Body.Yaw / Math.PI * 180.0:0.0} pitch={player.Body.Pitch / Math.PI * 180.0:0.0} weapon={player.Weapons?.InHand?.Weapon.WeaponId ?? -1}");

        bool god = false;
        double[]? aimPoint = null;
        int fireUntil = -1;
        int weaponTick = -1;
        int weaponKey = 0;
        string lastState = string.Empty;
        int last = -1;
        int tick = -1;
        bool quit = false;
        for (int step = 0; step < 4_000_000 && !quit; step++)
        {
            if (god)
            {
                player.Body.Health.Reset();
            }

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

            string state = State("RUN", tick);
            string key = state[(state.IndexOf("isExploded", StringComparison.Ordinal))..];
            if (key != lastState || tick % 30 == 0)
            {
                log.AppendLine(state);
                lastState = key;
            }

            foreach ((int at, string op, double[] args) in script.Where(command => command.Tick == tick))
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
                        weaponKey = (int)args[0];
                        weaponTick = tick;
                        break;
                    case "giveweapon":
                    {
                        // The probe sends the Human GunPickup's own message; the port's equivalent is a
                        // pickup lying at the player's feet, which his next tick collects.
                        FieldInfo weaponsField = typeof(GameHost).GetField("_weaponTypes", BindingFlags.NonPublic | BindingFlags.Instance)!;
                        OpenIGI.Game.Weapons.WeaponTypeRegistry roster = (OpenIGI.Game.Weapons.WeaponTypeRegistry)weaponsField.GetValue(host)!;
                        GunPickupRegistry pickups = MissionOf(host).GunPickups!;
                        OpenIGI.Game.Weapons.WeaponType given = roster.Weapons.Single(weapon => weapon.WeaponId == (int)args[0]);
                        pickups.Spawn(player.Body.Position, Matrix3f.Identity, given);
                        log.AppendLine(CultureInfo.InvariantCulture, $"[autopilot] tick={tick} giveweapon {given.WeaponId} ({given.ScriptId})");
                        break;
                    }

                    case "look":
                        aimPoint = null;
                        player.Body.Yaw = (float)(-args[0] * Math.PI / 180.0);
                        if (args.Length >= 2)
                        {
                            player.Body.Pitch = (float)(args[1] * Math.PI / 180.0);
                        }

                        break;
                    case "aimpoint":
                        aimPoint = args.Length >= 3 ? args : null;
                        break;
                    case "fire":
                        fireUntil = tick + (args.Length >= 1 ? (int)args[0] : 1);
                        break;
                    case "pos":
                        log.AppendLine(Pose(tick));
                        break;
                    case "quit":
                        quit = true;
                        break;
                }
            }

            if (aimPoint is not null)
            {
                Vector3d eye = player.Body.Position + new Vector3d(0.0, 0.0, player.Body.EyeHeight);
                double dx = aimPoint[0] - eye.X;
                double dy = aimPoint[1] - eye.Y;
                double dz = aimPoint[2] - eye.Z;
                player.Body.Yaw = (float)Math.Atan2(dx, dy);
                player.Body.Pitch = (float)Math.Atan2(dz, Math.Sqrt((dx * dx) + (dy * dy)));
            }

            if (weaponKey > 0)
            {
                scripted.Keyboard.SetKey(KeyCode.D1 + (byte)(weaponKey - 1), down: tick == weaponTick);
            }

            scripted.Mouse.SetButton(MouseButton.Left, down: tick < fireUntil);
            if (player.FiredThisTick)
            {
                log.AppendLine(CultureInfo.InvariantCulture, $"[autopilot] tick={tick} FIRED");
            }
        }

        host.Stop();
        File.WriteAllText(output, log.ToString());
    }

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
        BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        FieldInfo cutscenesField = typeof(GameHost).GetField("_cutscenes", hidden)!;
        FieldInfo soldiersField = typeof(GameHost).GetField("_soldiers", hidden)!;
        FieldInfo elevatorsField = typeof(GameHost).GetField("_elevators", hidden)!;

        CutsceneRuntime? runtime = null;
        for (int i = 0; i < 20_000 && runtime?.Frame is null; i++)
        {
            Assert.True(host.Step());
            runtime = cutscenesField.GetValue(host) as CutsceneRuntime;
            if (elevatorsField.GetValue(host) is ElevatorRegistry early)
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

            if (elevatorsField.GetValue(host) is ElevatorRegistry elevators)
            {
                foreach (PlacedElevator elevator in elevators.Elevators)
                {
                    Vector3d p = elevator.State.Position;
                    Matrix3f e = elevator.Orientation;
                    log.AppendLine(CultureInfo.InvariantCulture,
                        $"[elevator] scene={sceneTick} id={elevator.Task.Id} pos=({p.X:0.000},{p.Y:0.000},{p.Z:0.000}) m=[{e.M11:0.000000} {e.M12:0.000000} {e.M13:0.000000}; {e.M21:0.000000} {e.M22:0.000000} {e.M23:0.000000}; {e.M31:0.000000} {e.M32:0.000000} {e.M33:0.000000}]");
                }
            }

            if (soldiersField.GetValue(host) is SoldierRenderer soldiers)
            {
                foreach (object group in (System.Collections.IEnumerable)typeof(SoldierRenderer).GetField("_groups", hidden)!.GetValue(soldiers)!)
                {
                    foreach (object member in (System.Collections.IEnumerable)group.GetType().GetProperty("Soldiers")!.GetValue(group)!)
                    {
                        SoldierView view = (SoldierView)member.GetType().GetProperty("View")!.GetValue(member)!;
                        Vector3d where = (Vector3d)member.GetType().GetProperty("TickWhere")!.GetValue(member)! * Metre;
                        Matrix3f o = (Matrix3f)member.GetType().GetProperty("TickOrientation")!.GetValue(member)!;
                        bool staged = member.GetType().GetProperty("IsCutsceneActor")!.GetValue(member) is true;
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
        BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        FieldInfo soldiersField = typeof(GameHost).GetField("_soldiers", hidden)!;
        FieldInfo brainsField = typeof(SoldierRenderer).GetField("_brains", hidden)!;

        // OPENIGI_PATROL_CAPTURE_RNG=1: every draw from the level's global stream, for the diff against the
        // probe's [rng]: the level counter during the tick (-1 at load) and the first caller outside the generator.
        if (Environment.GetEnvironmentVariable("OPENIGI_PATROL_CAPTURE_RNG") == "1"
            && typeof(GameHost).GetField("_soundRandom", hidden)?.GetValue(host) is OpenIGI.Core.Numerics.IgiRandom stream)
        {
            FieldInfo counterField = typeof(SoldierRenderer).GetField("_tick", hidden)!;
            stream.Drew = _ =>
            {
                int counter = soldiersField.GetValue(host) is SoldierRenderer r ? (int)counterField.GetValue(r)! - 1 : -1;
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
                if (claimant is not Igi1HumanAi who || soldiersField.GetValue(host) is not SoldierRenderer r)
                {
                    return;
                }

                int counter = (int)counterField.GetValue(r)! - 1;
                int id = -1;
                foreach ((SoldierView v, Igi1HumanAi b) in (Dictionary<SoldierView, Igi1HumanAi>)brainsField.GetValue(r)!)
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
                int counter = soldiersField.GetValue(host) is SoldierRenderer r ? (int)counterField.GetValue(r)! - 1 : -1;
                log.AppendLine(FormattableString.Invariant($"[rebuild] lt={counter} id={id} by={why} who={SoldierSimulation.TickingSoldierId} from=({from.X:0},{from.Y:0},{from.Z:0}) to=({to.X:0},{to.Y:0},{to.Z:0})"));
            };

            // The probe's [drop]: where a dying soldier's gun starts falling.
            SoldierSimulation.WeaponDropTraced = (id, at, feet, root) =>
            {
                int counter = soldiersField.GetValue(host) is SoldierRenderer r ? (int)counterField.GetValue(r)! - 1 : -1;
                log.AppendLine(FormattableString.Invariant($"[drop] lt={counter} id={id} pos=({at.X:0.0},{at.Y:0.0},{at.Z:0.0}) feet=({feet.X:0.0},{feet.Y:0.0},{feet.Z:0.0}) root={root:0.0}"));
            };

            // The probe's [dmg]: every round a soldier takes.
            SoldierSimulation.DamageTraced = (id, zone, bone, factor, accum, max) =>
            {
                int counter = soldiersField.GetValue(host) is SoldierRenderer r ? (int)counterField.GetValue(r)! - 1 : -1;
                log.AppendLine(FormattableString.Invariant($"[dmg] lt={counter} id={id} zone={zone} bone={bone} factor={factor:0.000} accum={accum:0.000} max={max:0.000}"));
            };

            // The probe's [fire]: each round's base forward and the gun's rounds this pull.
            SoldierSimulation.RoundSpreadTraced = (who, pull, forward) =>
            {
                if (soldiersField.GetValue(host) is not SoldierRenderer r)
                {
                    return;
                }

                int counter = (int)counterField.GetValue(r)! - 1;
                int id = -1;
                foreach ((SoldierView v, Igi1HumanAi b) in (Dictionary<SoldierView, Igi1HumanAi>)brainsField.GetValue(r)!)
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
                int counter = soldiersField.GetValue(host) is SoldierRenderer r ? (int)counterField.GetValue(r)! - 1 : -1;
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
            FieldInfo gunShotsField = typeof(SoldierSimulation).GetField("_gunShots", hidden)!;
            OpenIGI.Game.Igi1.Weapons.GunShotTask.TracerFiled = (task, tracer) =>
            {
                int counter = -1;
                int owner = -1;
                if (soldiersField.GetValue(host) is SoldierRenderer r)
                {
                    counter = r.Simulation.TickCount - 1;
                    if (gunShotsField.GetValue(r.Simulation) is Dictionary<SoldierView, OpenIGI.Game.Igi1.Weapons.GunShotTask> tasks)
                    {
                        owner = tasks.FirstOrDefault(pair => ReferenceEquals(pair.Value, task)).Key?.Soldier.Id ?? -1;
                    }
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
        FieldInfo loopField = typeof(GameHost).GetField("_loop", hidden)!;
        FieldInfo renderTickField = typeof(SoldierRenderer).GetField("_tick", hidden)!;
        for (int step = 0; step < ticks * 4; step++)
        {
            Assert.True(host.Step());
            if (soldiersField.GetValue(host) is not SoldierRenderer soldiers)
            {
                continue;
            }

            if (watchedVariables.Length > 0
                && loopField.GetValue(host) is { } loop
                && loop.GetType().GetProperty("Variables")?.GetValue(loop) is OpenIGI.Engine.Tasks.ScriptVarRegistry variables)
            {
                int ran = (int)renderTickField.GetValue(soldiers)!;
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

            Dictionary<SoldierView, Igi1HumanAi> brains = (Dictionary<SoldierView, Igi1HumanAi>)brainsField.GetValue(soldiers)!;
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
                    int Counter() => (int)renderTickField.GetValue(renderer)! - 1;
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
                    int deadTick = (int)renderTickField.GetValue(soldiers)! - 1;
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
        FieldInfo playerField = typeof(GameHost).GetField("_player", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo soldiersField = typeof(GameHost).GetField("_soldiers", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo loopField = typeof(GameHost).GetField("_loop", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo healthField = typeof(GameHost).GetField("_playerHealth", BindingFlags.NonPublic | BindingFlags.Instance)!;

        PlayerCamera? player = null;
        SoldierRenderer? soldiers = null;
        for (int i = 0; i < 20_000 && (player is null || soldiers is null); i++)
        {
            Assert.True(host.Step());
            player = playerField.GetValue(host) as PlayerCamera;
            soldiers = soldiersField.GetValue(host) as SoldierRenderer;
        }

        Assert.NotNull(player);
        Assert.NotNull(soldiers);
        OpenIGI.Engine.Flow.GameLoop loop = (OpenIGI.Engine.Flow.GameLoop)loopField.GetValue(host)!;
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
        HumanHealth health = (HumanHealth)healthField.GetValue(host)!;
        FieldInfo deadField = typeof(GameHost).GetField("_playerDead", BindingFlags.NonPublic | BindingFlags.Instance)!;
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
                DestructibleObjectRegistry? registry = typeof(GameHost)
                    .GetField("_destructibles", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(host) as DestructibleObjectRegistry;
                DestructibleObject? target = registry?.Objects.FirstOrDefault(
                    item => ReferenceEquals(item.Owner, vehicle) && item.Kind == DestructibleObjectKind.Vehicle);
                string state = target is null ? "none" : $"dying={target.IsDying} destroyed={target.IsDestroyed}";
                if (state != destroyState || (target?.IsDying == true && tick % 30 == 0))
                {
                    destroyState = state;
                    log.AppendLine(CultureInfo.InvariantCulture, $"[vehicle] tick={tick} id={vehicleId} {state}");
                    if (typeof(GameHost).GetField("_world", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)
                        is OpenIGI.Presentation.Rendering.WorldRenderer world)
                    {
                        foreach (string line in world.DescribeFrameForTesting(vehicle))
                        {
                            log.AppendLine(CultureInfo.InvariantCulture, $"[vehicle]    {line}");
                        }
                    }

                    // What a ray straight down through the vehicle's origin meets: its hull, its wreck, or the ground.
                    if (target is not null
                        && typeof(GameHost).GetField("_worldSolids", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)
                            is ObjectCollisionIndex solids)
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

            if (EnvInt("OPENIGI_VEH_SCENARIO_HELI", -1) is int heliId and >= 0
                && typeof(CutsceneRuntime).GetField("_animations", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(runtime) is System.Collections.IEnumerable animations)
            {
                foreach (object animation in animations)
                {
                    Type type = animation.GetType();
                    if (type.GetProperty("TargetId")?.GetValue(animation) is not int targetId || targetId != heliId
                        || type.GetProperty("RanThisTick")?.GetValue(animation) is not true)
                    {
                        continue;
                    }

                    object program = type.GetProperty("Program")!.GetValue(animation)!;
                    float[] channels = (float[])program.GetType().GetField("_channels", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(program)!;
                    int programTick = (int)program.GetType().GetProperty("Tick")!.GetValue(program)!;
                    OpenIGI.Engine.Tasks.GameTask heli = loop.Tasks.FromId(heliId)!;
                    Vector3d at = runtime.TryGetDrawTransform(heli, out Vector3d metres, out _) ? metres * Metre : default;
                    string extra = string.Empty;
                    if (typeof(CutsceneRuntime).GetField("_transforms", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(runtime) is System.Collections.IDictionary transforms
                        && transforms[heliId] is { } state)
                    {
                        Type st = state.GetType();
                        object? S(string name) => st.GetProperty(name)?.GetValue(state);
                        Matrix3f m = (Matrix3f)S("Orientation")!;
                        Vector3d v = (Vector3d)S("Velocity")!;
                        Vector3f l = (Vector3f)S("AngularMomentum")!;
                        Vector3f c = (Vector3f)S("Control")!;
                        extra = string.Create(CultureInfo.InvariantCulture, $" vel=({v.X:0.0},{v.Y:0.0},{v.Z:0.0}) angular=({l.X:G4},{l.Y:G4},{l.Z:G4}) control=({c.X:0.####},{c.Y:0.####},{c.Z:0.####}) thrust={S("Thrust")} ground={S("GroundContact")} m=[{m.M11:0.####} {m.M12:0.####} {m.M13:0.####}; {m.M21:0.####} {m.M22:0.####} {m.M23:0.####}; {m.M31:0.####} {m.M32:0.####} {m.M33:0.####}]");
                    }

                    log.AppendLine(CultureInfo.InvariantCulture, $"[heli] tick={tick} programTick={programTick} pos=({at.X:0.0},{at.Y:0.0},{at.Z:0.0}){extra} channels=({string.Join(",", channels.Select(c => c.ToString("0.#####", CultureInfo.InvariantCulture)))})");
                }
            }

            // OPENIGI_VEH_SCENARIO_BODY=<id>: that car's retail rigid body every tick, bit-exact, for lining the
            // port's drive up against a probe carstep capture.
            if (EnvInt("OPENIGI_VEH_SCENARIO_BODY", -1) is int bodyId and >= 0
                && typeof(CutsceneRuntime).GetField("_transforms", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(runtime) is System.Collections.IDictionary bodyStates
                && bodyStates.Contains(bodyId)
                && bodyStates[bodyId] is { } bodyState)
            {
                if (bodyState.GetType().GetProperty("RetailBody")?.GetValue(bodyState) is RetailCarBody body)
                {
                    string scene = Environment.GetEnvironmentVariable("OPENIGI_VEH_SCENARIO_SCENEVAR") is { Length: > 0 } sceneVar
                        && loop.Variables.TryRead(sceneVar, out OpenIGI.Scripting.Runtime.QvmValue sceneTick) ? $" scene={sceneTick}" : string.Empty;
                    log.AppendLine(CultureInfo.InvariantCulture, $"[body] tick={tick}{scene} pos=({body.Position[0]:R},{body.Position[1]:R},{body.Position[2]:R}) thr={body.Throttle:R}/{body.ThrottleTarget:R} steer={body.Steering:R}/{body.SteeringTarget:R} hb={body.Handbrake} speed={body.Speed:R}");
                }
                else if (bodyState.GetType().GetProperty("RetailPlane")?.GetValue(bodyState) is RetailPlaneBody plane
                    && bodyState.GetType().GetProperty("PhysicsTicks")?.GetValue(bodyState) is int planeSteps
                    && planeSteps != lastPlaneSteps)
                {
                    // The probe's planeflight format: each force as pushed, then the state after the step.
                    lastPlaneSteps = planeSteps;
                    foreach (PointForce f in plane.PushedForces)
                    {
                        log.AppendLine(CultureInfo.InvariantCulture, $"[pforce] f=({f.Force.X:G9},{f.Force.Y:G9},{f.Force.Z:G9}) p=({f.Point.X:G9},{f.Point.Y:G9},{f.Point.Z:G9})");
                    }

                    float[] pm = plane.M;
                    log.AppendLine(CultureInfo.InvariantCulture, $"[pf] tick={tick} step={planeSteps} pos=({plane.Position[0]:R},{plane.Position[1]:R},{plane.Position[2]:R}) m=[{pm[0]:G9} {pm[1]:G9} {pm[2]:G9} {pm[3]:G9} {pm[4]:G9} {pm[5]:G9} {pm[6]:G9} {pm[7]:G9} {pm[8]:G9}] vel=({plane.Velocity[0]:G9},{plane.Velocity[1]:G9},{plane.Velocity[2]:G9}) mom=({plane.Momentum[0]:G9},{plane.Momentum[1]:G9},{plane.Momentum[2]:G9}) speed={plane.Speed:G9} thrust={plane.Thrust:G9}/{plane.ThrustTarget:G9} ctl=({plane.Controls[0]:G9},{plane.Controls[1]:G9},{plane.Controls[2]:G9})");
                }
                else if (bodyState.GetType().GetProperty("RetailHeli")?.GetValue(bodyState) is RetailHeliBody heli)
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
                foreach (object group in (System.Collections.IEnumerable)typeof(SoldierRenderer).GetField("_groups", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(soldiers)!)
                {
                    foreach (object member in (System.Collections.IEnumerable)group.GetType().GetProperty("Soldiers")!.GetValue(group)!)
                    {
                        drawn.Add((Vector3d)member.GetType().GetProperty("TickWhere")!.GetValue(member)! * Metre);
                        object agent = member.GetType().GetProperty("Agent")!.GetValue(member)!;
                        if (agent.GetType().GetProperty("IsCutsceneActor")!.GetValue(agent) is true)
                        {
                            SoldierView view = (SoldierView)agent.GetType().GetProperty("View")!.GetValue(agent)!;
                            System.Collections.IDictionary brains = soldiers.Simulation.Brains;
                            Igi1HumanAi? brain = brains.Contains(view) ? (Igi1HumanAi?)brains[view] : null;
                            object? P(string name) => member.GetType().GetProperty(name)?.GetValue(member);

                            // The port's half of the probe's [human] parent/local: what the actor rides, its offset
                            // in the parent's frame, and where that parent is now.
                            if (typeof(CutsceneRuntime).GetField("_attachments", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(runtime)
                                    is System.Collections.IDictionary attachments
                                && attachments.Contains(view.Soldier)
                                && attachments[view.Soldier] is { } attachment)
                            {
                                Type kind = attachment.GetType();
                                object parentTask = kind.GetProperty("ParentTask")!.GetValue(attachment)!;
                                object parentState = kind.GetProperty("Parent")!.GetValue(attachment)!;
                                Vector3d relative = (Vector3d)kind.GetProperty("RelativePosition")!.GetValue(attachment)!;
                                Vector3d parentAt = (Vector3d)parentState.GetType().GetProperty("Position")!.GetValue(parentState)!;
                                log.AppendLine(CultureInfo.InvariantCulture, $"[attach] tick={tick} id={view.Soldier.Id} parent={((OpenIGI.Engine.Tasks.GameTask)parentTask).Id} {((OpenIGI.Engine.Tasks.GameTask)parentTask).Type.Name} local=({relative.X:0.0},{relative.Y:0.0},{relative.Z:0.0}) parentAt=({parentAt.X:0},{parentAt.Y:0},{parentAt.Z:0})");
                                if (runtime.TryGetAttachedDrawTransform(view.Soldier, out _, out Matrix3f face))
                                {
                                    log.AppendLine(CultureInfo.InvariantCulture, $"[attachface] tick={tick} id={view.Soldier.Id} face=[{face.M11:0.00000} {face.M12:0.00000} {face.M13:0.00000}; {face.M21:0.00000} {face.M22:0.00000} {face.M23:0.00000}; {face.M31:0.00000} {face.M32:0.00000} {face.M33:0.00000}]");
                                }
                            }

                            log.AppendLine(CultureInfo.InvariantCulture, $"[actor] tick={tick} view=({view.Position.X:0.0},{view.Position.Y:0.0},{view.Position.Z:0.0}) brain=({brain?.Position.X:0.0},{brain?.Position.Y:0.0},{brain?.Position.Z:0.0}) act={brain?.Activity} req={brain?.RequestedAnimation} route={brain?.Route?.Current?.Kind} root={P("HasCutsceneRootMotion")} move={P("HasCutsceneGameplayMovement")} sample=({view.RootSample.X:0.0},{view.RootSample.Y:0.0},{view.RootSample.Z:0.0}) anim={view.CurrentAnimation}");
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
                log.AppendLine(CultureInfo.InvariantCulture, $"[autopilot] POS tick={tick} pos=({player.Body.Position.X:0}, {player.Body.Position.Y:0}, {player.Body.Position.Z + OriginAboveFeet:0}) dmg={health.Damage:0.00} dead={deadField.GetValue(host)} shove=({player.Body.HitShove.X:0.00},{player.Body.HitShove.Y:0.00})");
                if (typeof(CutsceneRuntime).GetField("_carAis", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(runtime) is System.Collections.IEnumerable carAis)
                {
                    foreach (object state in carAis)
                    {
                        Type type = state.GetType();
                        object? Prop(string name) => type.GetProperty(name)?.GetValue(state);
                        object? route = Prop("Route");
                        OpenIGI.Engine.Tasks.GameTask? task = Prop("Task") as OpenIGI.Engine.Tasks.GameTask;
                        OpenIGI.Engine.Tasks.GameTask? car = Prop("Car") as OpenIGI.Engine.Tasks.GameTask;
                        log.AppendLine(CultureInfo.InvariantCulture, $"[carai] tick={tick} task={task?.Id} car={car?.Id} active={(task is null ? "?" : ConditionalContainerGate.IsTaskActive(task).ToString())} route={(route is null ? "null" : route.GetType().GetProperty("HasStopped")?.GetValue(route))} stopped={Prop("Stopped")} node={(Prop("TargetNode") as OpenIGI.Formats.Ai.GraphNode)?.Id}@{(Prop("TargetNode") as OpenIGI.Formats.Ai.GraphNode)?.Position.X:0},{(Prop("TargetNode") as OpenIGI.Formats.Ai.GraphNode)?.Position.Y:0} car={((Prop("Transform") is { } tr) ? tr.GetType().GetProperty("Position")?.GetValue(tr) : null)} cmd={Prop("CommandSpeedKmh")} throttle={Prop("Throttle")} brake={Prop("Brake")} steer={Prop("Steering")}");
                    }
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
                        if (typeof(GameHost).GetField("_interactions", BindingFlags.NonPublic | BindingFlags.Instance)!
                            .GetValue(host) is WorldInteractionRegistry interactions
                            && interactions.Items.FirstOrDefault(item => item.Task.Id == (int)args[0]) is { } finished)
                        {
                            typeof(WorldInteraction).GetProperty(nameof(WorldInteraction.IsCompleted))!
                                .SetValue(finished, true);
                            log.AppendLine(CultureInfo.InvariantCulture, $"[autopilot] tick={tick} complete {(int)args[0]}");
                        }
                        else
                        {
                            object? registry = typeof(GameHost).GetField("_interactions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host);
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

    /// <inheritdoc/>
    public void Dispose()
    {
        _log?.Dispose();
        try
        {
            Directory.Delete(_scratch, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// The same scenario over many pairs: every living enemy with another within five metres, each in a
    /// fresh mission, summarised a few lines per pair.
    /// </summary>
    /// <remarks>
    /// Runs only when <c>OPENIGI_AI_SWEEP</c> names the summary file. <c>OPENIGI_AI_SWEEP_COUNT</c> caps the
    /// pairs (default 12), <c>OPENIGI_AI_SWEEP_SKIP</c> skips the first few, and
    /// <c>OPENIGI_AI_SCENARIO_TICKS</c> sets the ticks per pair (default 300).
    /// </remarks>
    [RequiresGameDataFact]
    public void SweepGuardPairs()
    {
        string? output = Environment.GetEnvironmentVariable("OPENIGI_AI_SWEEP");
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        int mission = EnvInt("OPENIGI_AI_SCENARIO_MISSION", 1);
        int ticks = EnvInt("OPENIGI_AI_SCENARIO_TICKS", 300);
        int count = EnvInt("OPENIGI_AI_SWEEP_COUNT", 12);
        int skip = EnvInt("OPENIGI_AI_SWEEP_SKIP", 0);
        StringBuilder log = new();

        List<(int A, int B)> chosen = [];
        using (RecordingGraphicsDevice device = new())
        {
            (GameHost host, PlayerCamera _, SoldierRenderer soldiers) = Boot(device, mission);
            IReadOnlyList<KeyValuePair<SoldierView, Igi1HumanAi>> pairs = soldiers.Simulation.BrainPairsForTesting;
            List<int> enemies = [.. Enumerable.Range(0, pairs.Count).Where(i => IsLiveEnemy(pairs[i], soldiers.Simulation.PlayerTeam))];
            foreach (int i in enemies)
            {
                int nearest = -1;
                double best = 5.0 * Metre;
                foreach (int j in enemies)
                {
                    double d = Horizontal(pairs[i].Value.Position, pairs[j].Value.Position);
                    if (i != j && d > 0.3 * Metre && d < best)
                    {
                        best = d;
                        nearest = j;
                    }
                }

                if (nearest >= 0)
                {
                    chosen.Add((i, nearest));
                }
            }

            host.Stop();
        }

        log.AppendLine(CultureInfo.InvariantCulture, $"{chosen.Count} pair(s) in mission {mission}");
        foreach ((int ai, int bi) in chosen.Skip(skip).Take(count))
        {
            using RecordingGraphicsDevice device = new();
            (GameHost host, PlayerCamera player, SoldierRenderer soldiers) = Boot(device, mission);
            IReadOnlyList<KeyValuePair<SoldierView, Igi1HumanAi>> pairs = soldiers.Simulation.BrainPairsForTesting;
            Igi1HumanAi a = pairs[ai].Value;
            Igi1HumanAi b = pairs[bi].Value;
            string head = string.Create(
                CultureInfo.InvariantCulture,
                $"A=s{pairs[ai].Key.Soldier.Id}#{ai} {a.Type} B=s{pairs[bi].Key.Soldier.Id}#{bi} {b.Type} {Horizontal(a.Position, b.Position) / Metre:0.0} m");
            if (!TryPlaceInView(a, 10.0, out Vector3d place, out string where))
            {
                log.AppendLine(head + ": no spot A can see");
                host.Stop();
                continue;
            }

            Vector3d bStart = b.Position;
            int aFire = 0;
            int bFire = 0;
            int bVisible = 0;
            int aSeen = -1;
            int bJoin = -1;
            double bClosest = double.MaxValue;
            HashSet<string> bEvents = [];
            int last = -1;
            int ran = 0;
            for (int step = 0; step < ticks * 8 && ran < ticks; step++)
            {
                player.Body.Position = place;
                player.Body.Health.Reset();
                if (!host.Step())
                {
                    break;
                }

                int engine = soldiers!.Simulation.TickCount;
                if (engine == last)
                {
                    continue;
                }

                last = engine;
                ran++;
                aFire += a.Combat.PullTrigger ? 1 : 0;
                bFire += b.Combat.PullTrigger ? 1 : 0;
                bVisible += b.SeesEnemy ? 1 : 0;
                if (aSeen < 0 && a.SeesEnemy)
                {
                    aSeen = ran;
                }

                if (bJoin < 0 && b.Activity == AiSoldierActivity.Engaging)
                {
                    bJoin = ran;
                }

                if (b.LastDispatchedEvent is { } dispatched)
                {
                    bEvents.Add(dispatched.ToString());
                }

                bClosest = Math.Min(bClosest, Horizontal(b.Position, place));
            }

            log.AppendLine(
                CultureInfo.InvariantCulture,
                $"{head} player {where} at ({place.X:0},{place.Y:0},{place.Z:0}) A facing {a.Facing * 180.0 / Math.PI:0}: A saw@{aSeen} fire={aFire} | B join@{bJoin} vis={bVisible} fire={bFire} moved={Horizontal(bStart, b.Position) / Metre:0.0} " +
                $"closest={bClosest / Metre:0.0} events=[{string.Join(",", bEvents)}]");
            log.AppendLine(CultureInfo.InvariantCulture, $"    B end: {b.DescribeTick()}");
            log.AppendLine(CultureInfo.InvariantCulture, $"    A end: {a.DescribeTick()}");
            host.Stop();
            File.WriteAllText(output, log.ToString());
        }

        File.WriteAllText(output, log.ToString());
    }

    private static bool IsLiveEnemy(KeyValuePair<SoldierView, Igi1HumanAi> pair, int playerTeam) =>
        pair.Key.Soldier.Team != playerTeam
        && !pair.Key.IsDead
        && ConditionalContainerGate.IsTaskActive(pair.Key.Soldier)
        && pair.Value.Arm != AiCombatArm.None;

    private (GameHost Host, PlayerCamera Player, SoldierRenderer Soldiers) Boot(RecordingGraphicsDevice device, int mission)
    {
        GameHost host = Host(device, "-mission", mission.ToString(CultureInfo.InvariantCulture), "-skipcutscene", "-lockstep", "-frames", "1000000");
        host.Start();
        FieldInfo playerField = typeof(GameHost).GetField("_player", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo soldiersField = typeof(GameHost).GetField("_soldiers", BindingFlags.NonPublic | BindingFlags.Instance)!;
        PlayerCamera? player = null;
        SoldierRenderer? soldiers = null;
        for (int i = 0; i < 20_000 && (player is null || soldiers is null); i++)
        {
            Assert.True(host.Step());
            player = playerField.GetValue(host) as PlayerCamera;
            soldiers = soldiersField.GetValue(host) as SoldierRenderer;
        }

        Assert.NotNull(player);
        Assert.NotNull(soldiers);
        for (int i = 0; i < 100_000 && soldiers!.Simulation.TickCount < 30; i++)
        {
            Assert.True(host.Step());
        }

        return (host, player, soldiers);
    }

    private static bool TryPlaceInView(Igi1HumanAi a, double preferredMetres, out Vector3d place, out string where)
    {
        foreach (double metres in new[] { preferredMetres, 8.0, 12.0, 6.0, 15.0, 4.0, 20.0 })
        {
            foreach (double degrees in new[] { 0.0, 15.0, -15.0, 30.0, -30.0, 45.0, -45.0 })
            {
                double yaw = degrees * Math.PI / 180.0;
                Vector3d direction = a.Orientation.Transposed().Transform(new Vector3d(Math.Sin(yaw), Math.Cos(yaw), 0.0));
                Vector3d candidate = new(
                    a.Position.X + (direction.X * metres * Metre),
                    a.Position.Y + (direction.Y * metres * Metre),
                    a.Position.Z);
                var probe = a.ProbeDetection(candidate + new Vector3d(0.0, 0.0, Igi1HumanAi.DetectionMarkAboveFeet(crouching: false)));
                if (probe.InCone && probe.LineOfSightClear)
                {
                    place = candidate;
                    where = string.Create(CultureInfo.InvariantCulture, $"{metres}m@{degrees}");
                    return true;
                }
            }
        }

        place = default;
        where = "-";
        return false;
    }

    private static int EnvInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), CultureInfo.InvariantCulture, out int value) ? value : fallback;

    private static double Horizontal(Vector3d p, Vector3d q) => Math.Sqrt(((p.X - q.X) * (p.X - q.X)) + ((p.Y - q.Y) * (p.Y - q.Y)));

    private static string Fmt(Vector3d p) =>
        string.Create(CultureInfo.InvariantCulture, $"({p.X / Metre:0.0},{p.Y / Metre:0.0},{p.Z / Metre:0.0})");

    /// <summary>
    /// The log a scenario's hosts write to: the file <c>OPENIGI_AI_SCENARIO_LOG</c> names, at
    /// <see cref="LogLevel.Information"/> and up, or nothing when it is unset.
    /// </summary>
    /// <returns>The logger, shared by every host this harness boots and closed when it is disposed.</returns>
    private IOpenIgiLogger ScenarioLog()
    {
        if (_log is not null)
        {
            return _log;
        }

        if (Environment.GetEnvironmentVariable("OPENIGI_AI_SCENARIO_LOG") is not { Length: > 0 } logPath)
        {
            return NullLogger.Instance;
        }

        // OPENIGI_AI_SCENARIO_LOGSPEC takes the same spec as OPENIGI_LOG, e.g. "Weapon=debug" for every guard
        // round's outcome.
        LogLevelPolicy policy = new(LogLevel.Information);
        if (Environment.GetEnvironmentVariable("OPENIGI_AI_SCENARIO_LOGSPEC") is { Length: > 0 } spec)
        {
            policy.TryApply(spec);
        }

        _log = new LogRouter(policy).AddSink(FileSink.Open(logPath));
        return _log;
    }

    private GameHost Host(RecordingGraphicsDevice device, params string[] args) =>
        Host(device, new HeadlessGameWindow(), args);

    private GameHost Host(RecordingGraphicsDevice device, HeadlessGameWindow window, params string[] args)
    {
        string root = GameData.RequireRoot();
        IGameTitleRuntime runtime = TitleCatalog.Default;
        IOpenIgiLogger logger = ScenarioLog();

        VirtualFileSystem fileSystem = new(logger);
        fileSystem.RegisterDevice(new LocalFileDevice(root));
        FileSystemConfig
            .LoadOrDefault(Path.Combine(root, FileSystemConfig.DefaultFileName), runtime.Layout)
            .ApplyTo(fileSystem);

        HostOptions options = HostOptions.Parse(args);
        return new GameHost(
            new HeadlessPlatformHost(),
            window,
            device,
            new SilentAudioFactory(),
            SynchronousParallelWorkScheduler.Instance,
            AlwaysReadyLoadGate.Instance,
            TitleCatalog.Catalog,
            runtime.Title,
            fileSystem,
            options,
            root,
            logger);
    }

    private sealed class SilentAudioFactory : IAudioDeviceFactory
    {
        public IAudioDevice Open(AudioDeviceOptions options, IOpenIgiLogger logger) => new RecordingAudioDevice();
    }

    // The played mission's registries, which the host keeps together.
    private static MissionWorld MissionOf(GameHost host) =>
        (MissionWorld)typeof(GameHost).GetField("_mission", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
}
