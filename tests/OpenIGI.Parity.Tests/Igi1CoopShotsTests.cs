using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Multiplayer;
using OpenIGI.Input;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>Co-op: a guard takes as many of the joiner's rounds to fall as of the host's, with its health as the room sets it.</summary>
[Collection(ParityGroup.Name)]
public sealed class Igi1CoopShotsTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [RequiresGameDataTheory]
    [InlineData(0, 0.0, false)]
    [InlineData(100, 0.0, false)]
    [InlineData(0, 0.2, true)]
    [InlineData(0, 0.4, true, true)]
    [InlineData(0, 0.2, true, true, true)]
    [InlineData(0, 0.2, false, false, false, true)]
    [InlineData(0, 0.2, false, true, false, false, true)]
    [InlineData(0, 0.0, false, false, false, false, false, true)]
    [InlineData(0, 0.2, false, true, false, false, false, true)]
    public void A_guard_falls_to_as_many_of_the_joiners_rounds_as_of_the_hosts(int enemyHealth, double ping, bool running, bool automatic = false, bool hostAway = false, bool head = false, bool live = false, bool thirdPerson = false)
    {
        using Igi1CoopPair pair = Joined();
        try
        {
            // Over the shoulder on both games: the crosshair is the camera's ray, which no longer leaves the eye.
            Game.GameplayCVars.ThirdPerson.Value = thirdPerson;
            // Live guards fire back: both players live through it.
            Game.Player.PlayerCheatCVars.CheatsEnabled.Value = live;
            Game.Player.PlayerCheatCVars.InfiniteHealth.Value = live;
            pair.Ping(ping);

            // A bad line, as the poor-connection chip shows: a round trip that wanders, and a packet in twenty lost each way.
            if (automatic)
            {
                pair.JoinerLink.Send.Jitter = pair.JoinerLink.Receive.Jitter = 0.03;
                pair.JoinerLink.Send.Loss = pair.JoinerLink.Receive.Loss = 0.05;
            }

            Game.Multiplayer.CoopCVars.EnemyHealthPerPlayer.Value = enemyHealth;
            pair.RunUntil(() => false, 60);
            Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
            Game.Igi1.Ai.SoldierSimulation soldiers = pair.Host.World.Soldiers!;
            if (live)
            {
                pair.Host.World.PlayerHealth.Invulnerable = true;
                partner.Health.Invulnerable = true;
            }

            // Two guards of one kind, so each takes the same to fall.
            var guards = soldiers.SoldierViews
                .Where(static v => !v.IsDead && Game.World.ConditionalContainerGate.IsTaskActive(v.Soldier))
                .Where(v => !live || ClearSpot(pair, v) is not null)
                .GroupBy(v => (v.Soldier.Class, soldiers.HealthOf(v).MaximumHealth))
                .First(g => g.Count() >= 2)
                .Take(2)
                .ToArray();
            float scale = 1f + (enemyHealth / 100f);
            Assert.Equal(soldiers.HealthOf(guards[0]).BaseMaximumHealth * scale, soldiers.HealthOf(guards[0]).MaximumHealth, 3);

            (int Rounds, int Missed, string Log) host = RoundsToFell(pair, partner, hostShoots: true, guards[0], running, automatic, hostAway: false, head, live);
            (int Rounds, int Missed, string Log) joiner = RoundsToFell(pair, partner, hostShoots: false, guards[1], running, automatic, hostAway, head, live);
            output.WriteLine($"+{enemyHealth}% ping {ping} running {running} automatic {automatic} hostAway {hostAway} head {head} live {live} thirdPerson {thirdPerson}: host {host.Rounds} rounds, {host.Missed} missed ({host.Log}); joiner {joiner.Rounds} rounds, {joiner.Missed} missed ({joiner.Log})");

            // The rounds that missed, not the rounds it took: a head shot the host happens to land fells a guard in one, and
            // how many land is the netcode's business.
            Assert.True(
                joiner.Missed <= (host.Missed * 3 / 2) + 1,
                $"at +{enemyHealth}%, {ping * 1000:0} ms, {(running ? "running" : "standing")}: the host missed {host.Missed} of {host.Rounds} rounds, the joiner {joiner.Missed} of {joiner.Rounds}; host {host.Log}; joiner {joiner.Log}");
        }
        finally
        {
            Game.GameplayCVars.ThirdPerson.Value = false;
            Game.Player.PlayerCheatCVars.CheatsEnabled.Value = false;
            Game.Player.PlayerCheatCVars.InfiniteHealth.Value = false;
            Game.Multiplayer.CoopCVars.EnemyHealthPerPlayer.Value = 0;
        }
    }

    [RequiresGameDataTheory]
    [InlineData("late")]
    [InlineData("restart")]
    [InlineData("next")]
    public void A_guard_falls_to_as_many_of_the_joiners_rounds_as_of_the_hosts_however_the_joiner_came_in(string how)
    {
        using Igi1CoopPair pair = how switch
        {
            "late" => new Igi1CoopPair(hostArgs: [], joinerArgs: [], joinLater: true),
            "restart" => new Igi1CoopPair(1, 20000, "-runat", "400", "mission"),
            _ => new Igi1CoopPair(mission: 1),
        };

        if (how == "late")
        {
            // The host alone a good while first; the joiner comes in mid-mission.
            Assert.True(pair.RunUntil(() => pair.Host?.World.Player is not null, 3000) >= 0, "the host's level never came up");
            pair.RunUntil(() => false, 300);
            pair.StartJoiner();
        }

        Assert.True(pair.RunUntil(() => pair.Joined && pair.Host!.World.NetworkPlayers.Count == 1, 3000) >= 0, "the joiner was never welcomed");
        object hostLevel = pair.Host!.World.LevelRoot!;
        object joinerLevel = pair.Joiner!.World.LevelRoot!;
        if (how == "next")
        {
            pair.RunUntil(() => false, 150);
            typeof(Host.GameHost).GetMethod("StartMission", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(pair.HostGame, [2]);
        }

        if (how is "restart" or "next")
        {
            Assert.True(
                pair.RunUntil(
                    () => pair.Host!.World.LevelRoot is { } h && !ReferenceEquals(h, hostLevel)
                        && pair.Joiner!.World.LevelRoot is { } j && !ReferenceEquals(j, joinerLevel)
                        && pair.Host.World.NetworkPlayers.Count == 1 && pair.Host.World.Player is not null && pair.Joiner.World.Player is not null
                        && pair.Joiner.World.PlayerTicks > 30,
                    4000) >= 0,
                $"the pair never came into the new level ({how})");
        }

        pair.RunUntil(() => false, 60);
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        Game.Igi1.Ai.SoldierSimulation soldiers = pair.Host.World.Soldiers!;
        var guards = soldiers.SoldierViews
            .Where(static v => !v.IsDead && Game.World.ConditionalContainerGate.IsTaskActive(v.Soldier))
            .GroupBy(v => (v.Soldier.Class, soldiers.HealthOf(v).MaximumHealth))
            .First(g => g.Count() >= 2)
            .Take(2)
            .ToArray();
        (int Rounds, int Missed, string Log) host = RoundsToFell(pair, partner, hostShoots: true, guards[0], running: false, automatic: false, hostAway: false, head: false, live: false);
        (int Rounds, int Missed, string Log) joiner = RoundsToFell(pair, partner, hostShoots: false, guards[1], running: false, automatic: false, hostAway: false, head: false, live: false);
        output.WriteLine($"{how}: host {host.Rounds} rounds ({host.Log}); joiner {joiner.Rounds} rounds ({joiner.Log})");
        // The rounds that missed, not the rounds it took, which a head shot either happens to land changes.
        Assert.True(joiner.Missed <= host.Missed + 1, $"{how}: the host missed {host.Missed} of {host.Rounds} rounds, the joiner {joiner.Missed} of {joiner.Rounds}; host {host.Log}; joiner {joiner.Log}");
    }

    // One shooter, a gun in hand, five metres from a guard -- standing, or running three metres either way across its front --
    // clicking the fire button at the guard's chest where its own game shows the guard, until the host's soldier falls: the
    // rounds that took, as the host's game counts them.
    private static (int Rounds, int Missed, string Log) RoundsToFell(Igi1CoopPair pair, Igi1NetworkPlayer partner, bool hostShoots, Game.Igi1.Player.SoldierView guard, bool running, bool automatic, bool hostAway, bool head, bool live)
    {
        Game.Igi1.Ai.SoldierSimulation soldiers = pair.Host!.World.Soldiers!;
        Game.Igi1.Player.PlayerCamera shooter = hostShoots ? pair.Host.World.Player! : partner.Player;
        Game.Igi1.Player.PlayerCamera own = hostShoots ? shooter : pair.Joiner!.World.Player!;
        InputState input = hostShoots ? pair.HostInput : pair.JoinerInput;
        int id = soldiers.RuntimeIdOf(guard);
        Game.Igi1.Player.SoldierView shown = hostShoots ? guard : pair.Joiner!.World.Soldiers!.SoldierByRuntimeId(id)!;

        // A live guard is left to its own mind: the shooter goes to it instead, eight metres off.
        Vector3d feet = live ? ClearSpot(pair, guard)!.Value : shooter.Body.Position;
        // Five metres off along a line nothing solid crosses -- nor any line to where the guard runs, three metres either side.
        Vector3d ahead = live ? new Vector3d(0, 1, 0) : pair.ClearAhead(feet, running ? 3.5 : 0) ?? new Vector3d(0, 1, 0);

        Vector3d side = new(ahead.Y, -ahead.X, 0);
        Vector3d middle = feet + (ahead * 5 * 4096);
        int tick = 0;
        void Move()
        {
            if (live)
            {
                return;
            }

            // Across and back every four seconds: three metres a second at the middle.
            double across = running ? 3 * 4096 * Math.Sin(tick++ * 2 * Math.PI / 120) : 0;
            float toward = (float)Math.Atan2(-ahead.X, -ahead.Y);
            float facing = running ? toward + (Math.Cos(tick * 2 * Math.PI / 120) >= 0 ? -MathF.PI / 2 : MathF.PI / 2) : toward;
            soldiers.Puppet(guard, new Game.Igi1.Ai.SoldierPuppet(middle + (side * across), facing, guard.CurrentAnimation, -1, 0f, Alive: true));
        }

        void Aim()
        {
            // The chest, or the head where this game poses the guard's eye.
            Vector3d chest = shown.Position + new Vector3d(0, 0, 1.3 * 4096);
            if (head)
            {
                Formats.Animation.BonePose pose = shown.UnrotatedPose();
                Vector3f eye = shown.Orientation.Transposed().Transform(pose.Positions[Game.Player.HumanAttachPoints.AiEye]);
                chest = shown.Position + new Vector3d(eye.X, eye.Y, eye.Z);
            }

            // Where the crosshair is: the eye in first person, the camera over the shoulder in third.
            Vector3d toward = chest - (own.AimConvergenceRay?.OriginUnits ?? own.Body.Eye);
            own.Body.Yaw = (float)Math.Atan2(toward.X, toward.Y);
            own.Body.Pitch = (float)Math.Atan2(toward.Z, Math.Sqrt((toward.X * toward.X) + (toward.Y * toward.Y)));
        }

        foreach (Game.Igi1.Player.PlayerCamera rig in hostShoots ? [shooter] : new[] { own, shooter })
        {
            rig.Body.Position = feet;
        }

        // The host off about its own business, far out of sight of the joiner's fight: its game draws none of it.
        if (hostAway)
        {
            Game.Igi1.Player.PlayerCamera host = pair.Host.World.Player!;
            host.Body.Position = feet + new Vector3d(300 * 4096, 0, 0);
            host.Body.Yaw = MathF.PI / 2;
        }

        pair.RunUntil(() => { Move(); return false; }, 40);

        // A gun in hand, picked with the number keys: the knife in hand at the start reaches two metres. The automatic one
        // is the MP5 (Gun), the other the pistol.
        Game.Weapons.WeaponCategory wanted = automatic ? Game.Weapons.WeaponCategory.Gun : Game.Weapons.WeaponCategory.Pistol;
        KeyCode[] categories = [KeyCode.D2, KeyCode.D3, KeyCode.D4, KeyCode.D5, KeyCode.D6];
        foreach (KeyCode category in categories)
        {
            if (shooter.Weapons?.InHand is { } held && held.Weapon.Category == wanted)
            {
                break;
            }

            Igi1CoopPair.Hold(input, category, true);
            pair.RunUntil(() => { Move(); Aim(); return false; }, 3);
            Igi1CoopPair.Hold(input, category, false);
            pair.RunUntil(() => { Move(); Aim(); return false; }, 45);
        }

        Assert.True(
            shooter.Weapons?.InHand is { } gun && gun.Weapon.Category == wanted,
            $"the {(hostShoots ? "host" : "joiner")} holds no gun: {shooter.Weapons?.InHand?.Weapon.WeaponId}; carries "
            + string.Join(",", shooter.Weapons?.Carried.Select(c => $"{c.Weapon.WeaponId}:{c.Weapon.Category}:{c.Weapon.EffectiveRange:0}") ?? []));
        int shotsBefore = shooter.Weapons!.ShotSequence;
        List<string> log = [$"weapon {shooter.Weapons.InHand?.Weapon.WeaponId} max {soldiers.HealthOf(guard).MaximumHealth:0.0}"];
        float damage = soldiers.HealthOf(guard).Damage;
        int landed = 0;

        // Clicked, as a pistol wants: a press, then let go, over and over; an automatic's held down.
        int frame = 0;
        bool fell = pair.RunUntil(
            () =>
            {
                Move();
                Aim();
                input.Mouse.SetButton(MouseButton.Left, automatic || frame++ % 6 < 2);
                foreach (Game.Igi1.Weapons.GunImpact impact in shooter.Impacts)
                {
                    if (log.Count < 6)
                    {
                        log.Add($"[impact {Vector3d.Distance(impact.Position, shooter.Body.Eye):0} from eye, human {impact.StruckHuman}, guard {Vector3d.Distance(impact.Position, guard.Position):0} away, guard at {guard.Position}, shooter {shooter.Body.Position}]");
                    }
                }

                float now = soldiers.HealthOf(guard).Damage;
                if (now != damage)
                {
                    landed++;
                    log.Add($"r{shooter.Weapons.ShotSequence - shotsBefore}:+{now - damage:0.00}");
                    damage = now;
                }

                return guard.IsDead;
            },
            900) >= 0;

        // The rounds up to the one that felled it: a joiner's commands already on their way, fire held, shoot on at the body.
        int rounds = shooter.Weapons.ShotSequence - shotsBefore;
        input.Mouse.SetButton(MouseButton.Left, false);
        pair.RunUntil(() => false, 30);
        Assert.True(
            fell,
            $"the {(hostShoots ? "host" : "joiner")}'s guard never fell: {rounds} rounds, {string.Join(" ", log)}; shooter dead {(hostShoots ? pair.Host.World.IsPlayerDead : partner.IsDead)} "
            + $"health {(hostShoots ? pair.Host.World.PlayerHealth.Damage : partner.Health.Damage)} grounded {shooter.Body.IsGrounded} at {shooter.Body.Position} feet {feet} "
            + $"clip {shooter.Weapons.InHand?.Gun.State.RoundsInClip} guard dead {guard.IsDead} at {guard.Position}");
        return (rounds, Math.Max(0, rounds - landed), string.Join(" ", log));
    }

    // Somewhere eight metres from a guard with nothing solid between an eye there and its chest, or null.
    private static Vector3d? ClearSpot(Igi1CoopPair pair, Game.Igi1.Player.SoldierView guard)
    {
        if (pair.Host!.World.Solids?.Caster() is not { } cast)
        {
            return null;
        }

        Vector3d chest = guard.Position + new Vector3d(0, 0, 1.3 * 4096);
        for (int i = 0; i < 16; i++)
        {
            double angle = i * Math.PI / 8;
            Vector3d feet = guard.Position + new Vector3d(Math.Sin(angle) * 8 * 4096, Math.Cos(angle) * 8 * 4096, 0);
            Vector3d eye = feet + new Vector3d(0, 0, 1.6 * 4096);
            if (!cast(eye, chest, out _) && !cast(feet + new Vector3d(0, 0, 512), eye, out _))
            {
                return feet + new Vector3d(0, 0, 512);
            }
        }

        return null;
    }

    private static Igi1CoopPair Joined()
    {
        Igi1CoopPair pair = new(mission: 1);
        Assert.True(pair.RunUntil(() => pair.Joined && pair.Host!.World.NetworkPlayers.Count == 1, 3000) >= 0, "the joiner was never welcomed");
        return pair;
    }
}
