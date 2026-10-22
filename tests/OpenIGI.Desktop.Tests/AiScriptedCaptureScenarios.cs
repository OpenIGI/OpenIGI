using System.Globalization;
using System.Text;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.World;
using OpenIGI.Host;
using OpenIGI.Input;
using OpenIGI.Presentation.Igi1;
using OpenIGI.TestSupport;
using Xunit;
using static OpenIGI.Desktop.Tests.AiScenarioSupport;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Diagnostic runs, not checks: the port halves of the probe's scripted captures -- a provoked guard, the
/// electric fence and a generator -- each played headless and written in the probe's own record format.
/// </summary>
/// <remarks>Each runs only when its own environment variable names the trace file.</remarks>
[Collection(AiScenarioSupport.CollectionName)]
public sealed class AiScriptedCaptureScenarios : IDisposable
{
    private readonly AiScenarioSupport _support = new();

    /// <inheritdoc/>
    public void Dispose() => _support.Dispose();

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

            int engine = soldiers.Simulation.TickCount;
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

            int rounds = host.GuardRoundsFired();
            if (rounds != lastRounds || since % 60 == 0)
            {
                lastRounds = rounds;
                log.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"t{since} rounds={rounds} hits={host.GuardHitsOnPlayer()} target {targetId} dmg={soldiers.Simulation.DamageForTesting(target.Key):0.00} {target.Value.DescribeTick()}");
            }
        }

        log.AppendLine(
            CultureInfo.InvariantCulture,
            $"guard rounds={host.GuardRoundsFired()} hits on player={host.GuardHitsOnPlayer()}");
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

            int engine = soldiers.Simulation.TickCount;
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
        DestructibleObjectRegistry destructibles = MissionOf(host).Destructibles!;
        DestructibleObject? generator = destructibles.Objects.FirstOrDefault(item => item.Owner.Id == generatorId);
        log.AppendLine(CultureInfo.InvariantCulture, $"[generator] registered={generator is not null} keepsModel={generator?.KeepsModelWhenDestroyed}");
        if (generator is not null && EnvInt("OPENIGI_GEN_SCENARIO_NEARBY", 0) is > 0 and int nearby)
        {
            WorldScene scene = host.Scene()!;
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
                        OpenIGI.Game.Weapons.WeaponTypeRegistry roster = MissionOf(host).WeaponTypes!;
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

    private GameHost Host(RecordingGraphicsDevice device, HeadlessGameWindow window, params string[] args) =>
        _support.Host(device, window, args);
}
