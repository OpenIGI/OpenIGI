using System.Globalization;
using System.Reflection;
using System.Text;
using OpenIGI.Audio;
using OpenIGI.Composition;
using OpenIGI.Core.Diagnostics;
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
using OpenIGI.TestSupport;
using Xunit;

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
        // The level is withheld through its warm-up and insertion: wait until the AI has run a second.
        for (int i = 0; i < 100_000 && (int)tickField.GetValue(soldiers)! < 30; i++)
        {
            Assert.True(host.Step());
        }

        Assert.True((int)tickField.GetValue(soldiers)! >= 30, "the AI never ticked");

        IReadOnlyList<KeyValuePair<SoldierView, AiSoldier>> pairs = soldiers.BrainPairsForTesting;
        int playerTeam = soldiers.PlayerTeam;
        log.AppendLine(CultureInfo.InvariantCulture, $"player team {playerTeam} at {Fmt(player.Body.Position)}");
        foreach ((SoldierView view, AiSoldier brain) in pairs)
        {
            log.AppendLine(
                CultureInfo.InvariantCulture,
                $"soldier {view.Soldier.Id} team {view.Soldier.Team} type {brain.Type} arm {brain.Arm} active {ConditionalContainerGate.IsTaskActive(view.Soldier)} {brain.DescribeTick()}");
        }

        List<KeyValuePair<SoldierView, AiSoldier>> enemies = [.. pairs.Where(p =>
            p.Key.Soldier.Team != playerTeam
            && !p.Key.IsDead
            && ConditionalContainerGate.IsTaskActive(p.Key.Soldier)
            && p.Value.Arm != AiCombatArm.None)];

        KeyValuePair<SoldierView, AiSoldier> first;
        KeyValuePair<SoldierView, AiSoldier> second;
        if (Environment.GetEnvironmentVariable("OPENIGI_AI_SCENARIO_ALARM") is { Length: > 0 })
        {
            // A guard its script gave an alarm trigger, and its nearest living enemy-of-the-player neighbour.
            first = enemies.First(p => p.Value.AlarmTriggerId != -1);
            KeyValuePair<SoldierView, AiSoldier> alarmGuard = first;
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
        AiSoldier a = first.Value;
        AiSoldier b = second.Value;
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
                Vector3d mark = candidate + new Vector3d(0.0, 0.0, AiSoldier.DetectionMarkAboveFeet(crouching: false));
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
                $"RAY {soldiers.DescribeSightForTesting(new Vector3d(r[0], r[1], r[2]), new Vector3d(r[3], r[4], r[5]))}");
        }

        Dictionary<AiSoldier, int> engagedAt = [];
        Dictionary<AiSoldier, AiEvent> firstReason = [];
        Dictionary<AiSoldier, Vector3d> starts = pairs.ToDictionary(p => p.Value, p => p.Value.Position);
        int lastLogged = -1;
        for (int step = 0; step < ticks * 8; step++)
        {
            int engine = (int)tickField.GetValue(soldiers)!;
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

            engine = (int)tickField.GetValue(soldiers)!;
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

            foreach ((SoldierView view, AiSoldier brain) in pairs)
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
                Vector3d playerMark = player.Body.Position + new Vector3d(0.0, 0.0, AiSoldier.DetectionMarkAboveFeet(crouching: false));
                log.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"t{engine} SIGHT A eye={Fmt(a.EyePosition())} -> {Fmt(playerMark)}: {soldiers.DescribeSightForTesting(a.EyePosition(), playerMark)}");
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
        foreach ((SoldierView view, AiSoldier brain) in pairs)
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
        Vector3d spawn = player.Body.Position;
        log.AppendLine(CultureInfo.InvariantCulture, $"spawn {Fmt(spawn)} team {soldiers.PlayerTeam}");

        IReadOnlyList<KeyValuePair<SoldierView, AiSoldier>> pairs = soldiers.BrainPairsForTesting;
        bool IsEnemy(SoldierView view) =>
            view.Soldier.Team != soldiers.PlayerTeam && ConditionalContainerGate.IsTaskActive(view.Soldier);

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

            int engine = (int)tickField.GetValue(soldiers)!;
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
            foreach ((SoldierView view, AiSoldier _) in pairs)
            {
                if (!IsEnemy(view) || view.IsDead)
                {
                    continue;
                }

                double distance = Vector3d.Distance(view.Position, feet);
                Vector3d point = view.Position + new Vector3d(0.0, 0.0, (eyeHeight - drop) * Metre);
                if (distance < best && soldiers.ClearSightForTesting(eye, point))
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
                    log.AppendLine(CultureInfo.InvariantCulture, $"   LOS eye {eye.X:0.0} {eye.Y:0.0} {eye.Z:0.0} -> {point.X:0.0} {point.Y:0.0} {point.Z:0.0}: {soldiers.DescribeSightForTesting(eye, point)}");
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
                foreach ((SoldierView view, AiSoldier brain) in pairs)
                {
                    double distance = Vector3d.Distance(view.Position, feet);
                    if (IsEnemy(view) && distance <= 80.0 * Metre)
                    {
                        field.Append(CultureInfo.InvariantCulture, $" {view.Soldier.Id}:{distance / Metre:0.0}m/{soldiers.DamageForTesting(view):0.00}{(view.IsDead ? "X" : string.Empty)}");
                        if (view.Soldier.Id == EnvInt("OPENIGI_AI_SCENARIO_TRACK", -1))
                        {
                            field.Append(CultureInfo.InvariantCulture, $"@{view.Position.X / Metre:0.00},{view.Position.Y / Metre:0.00}[{brain.Activity}]");
                        }
                    }
                }

                log.AppendLine(CultureInfo.InvariantCulture, $"[field] t{since}{field}");
                foreach ((SoldierView view, AiSoldier brain) in pairs)
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
        foreach ((SoldierView view, AiSoldier brain) in pairs)
        {
            if (IsEnemy(view) && Vector3d.Distance(view.Position, spawn) <= 80.0 * Metre)
            {
                log.AppendLine(CultureInfo.InvariantCulture, $"  s{view.Soldier.Id} dmg={soldiers.DamageForTesting(view):0.00} dead={view.IsDead} {brain.DescribeTick()}");
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
        GameHost host = Host(device, new HeadlessGameWindow(), "-mission", mission.ToString(CultureInfo.InvariantCulture), "-skipcutscene", "-lockstep", "-frames", "1000000");
        host.Start();
        InputState scripted = new();
        host.ScriptedViewInput = scripted;
        FieldInfo playerField = typeof(GameHost).GetField("_player", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo soldiersField = typeof(GameHost).GetField("_soldiers", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo loopField = typeof(GameHost).GetField("_loop", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo destructiblesField = typeof(GameHost).GetField("_destructibles", BindingFlags.NonPublic | BindingFlags.Instance)!;
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
        OpenIGI.Engine.Flow.GameLoop loop = (OpenIGI.Engine.Flow.GameLoop)loopField.GetValue(host)!;
        DestructibleObjectRegistry destructibles = (DestructibleObjectRegistry)destructiblesField.GetValue(host)!;
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
                    foreach (OpenIGI.Rendering.Vertex3D vertex in mesh.Vertices)
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

            int engine = (int)tickField.GetValue(soldiers)!;
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
                        FieldInfo pickupsField = typeof(GameHost).GetField("_gunPickups", BindingFlags.NonPublic | BindingFlags.Instance)!;
                        OpenIGI.Game.Weapons.WeaponTypeRegistry roster = (OpenIGI.Game.Weapons.WeaponTypeRegistry)weaponsField.GetValue(host)!;
                        GunPickupRegistry pickups = (GunPickupRegistry)pickupsField.GetValue(host)!;
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

    /// <inheritdoc/>
    public void Dispose()
    {
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
            IReadOnlyList<KeyValuePair<SoldierView, AiSoldier>> pairs = soldiers.BrainPairsForTesting;
            List<int> enemies = [.. Enumerable.Range(0, pairs.Count).Where(i => IsLiveEnemy(pairs[i], soldiers.PlayerTeam))];
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
        FieldInfo tickField = typeof(SoldierRenderer).GetField("_tick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach ((int ai, int bi) in chosen.Skip(skip).Take(count))
        {
            using RecordingGraphicsDevice device = new();
            (GameHost host, PlayerCamera player, SoldierRenderer soldiers) = Boot(device, mission);
            IReadOnlyList<KeyValuePair<SoldierView, AiSoldier>> pairs = soldiers.BrainPairsForTesting;
            AiSoldier a = pairs[ai].Value;
            AiSoldier b = pairs[bi].Value;
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

                int engine = (int)tickField.GetValue(soldiers)!;
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

    private static bool IsLiveEnemy(KeyValuePair<SoldierView, AiSoldier> pair, int playerTeam) =>
        pair.Key.Soldier.Team != playerTeam
        && !pair.Key.IsDead
        && ConditionalContainerGate.IsTaskActive(pair.Key.Soldier)
        && pair.Value.Arm != AiCombatArm.None;

    private static (GameHost Host, PlayerCamera Player, SoldierRenderer Soldiers) Boot(RecordingGraphicsDevice device, int mission)
    {
        GameHost host = Host(device, "-mission", mission.ToString(CultureInfo.InvariantCulture), "-skipcutscene", "-lockstep", "-frames", "1000000");
        host.Start();
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
        for (int i = 0; i < 100_000 && (int)tickField.GetValue(soldiers)! < 30; i++)
        {
            Assert.True(host.Step());
        }

        return (host, player, soldiers);
    }

    private static bool TryPlaceInView(AiSoldier a, double preferredMetres, out Vector3d place, out string where)
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
                var probe = a.ProbeDetection(candidate + new Vector3d(0.0, 0.0, AiSoldier.DetectionMarkAboveFeet(crouching: false)));
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

    private static GameHost Host(RecordingGraphicsDevice device, params string[] args) =>
        Host(device, new HeadlessGameWindow(), args);

    private static GameHost Host(RecordingGraphicsDevice device, HeadlessGameWindow window, params string[] args)
    {
        string root = GameData.RequireRoot();
        IGameTitleRuntime runtime = TitleCatalog.Default;
        IIgiLogger logger = Environment.GetEnvironmentVariable("OPENIGI_AI_SCENARIO_LOG") is { Length: > 0 } logPath
            ? new FileLogger(logPath)
            : NullLogger.Instance;

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

#pragma warning disable CA1001 // A diagnostic run's log lives as long as the process.
    private sealed class FileLogger(string path) : IIgiLogger
    {
        private readonly TextWriter _writer = TextWriter.Synchronized(File.CreateText(path));

        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;

        public void Log(LogLevel level, string category, string message, Exception? exception = null)
        {
            _writer.WriteLine($"{level} [{category}] {message} {exception}");
            _writer.Flush();
        }
    }

#pragma warning restore CA1001

    private sealed class SilentAudioFactory : IAudioDeviceFactory
    {
        public IAudioDevice Open(AudioDeviceOptions options, IIgiLogger logger) => new RecordingAudioDevice();
    }
}
