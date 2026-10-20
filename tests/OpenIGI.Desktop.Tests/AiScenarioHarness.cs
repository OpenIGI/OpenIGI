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
            .Where(static task => task.Type.Name is "Car" or "Heli")];
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
                    case "quit":
                        quit = true;
                        break;
                }
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

        _log = new LogRouter(new LogLevelPolicy(LogLevel.Information)).AddSink(FileSink.Open(logPath));
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
            InlineWorkScheduler.Instance,
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
