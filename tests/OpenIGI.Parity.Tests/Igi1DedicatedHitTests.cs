using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Multiplayer;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Input;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>
/// Two players on a dedicated server shooting each other, standing still five metres apart on lines of 170 and 100 ms, as a
/// tester reported rounds that left the other untouched: on the first map, after the map is started over, and on the maps
/// and modes the console moves the server on to.
/// </summary>
public sealed class Igi1DedicatedHitTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [RequiresGameDataFact]
    public void Players_on_a_dedicated_server_hurt_each_other_on_every_map_and_mode_it_is_moved_to()
    {
        using Igi1DedicatedTrio trio = new(GameModes.TeamDeathmatch, 1, 0.17, 0.10);
        Assert.True(trio.RunUntil(() => trio.AllIn, 3000) >= 0, "the players never both came in");
        IReadOnlyList<int> arenas = [.. trio.ServerGame.ArenaMissionList.Select(m => m.Id)];
        output.WriteLine($"arenas: {string.Join(", ", arenas)}");

        List<string> failures = [];
        void Check(string stage)
        {
            foreach ((Igi1DedicatedTrio.Player shooter, Igi1DedicatedTrio.Player target) in new[] { (trio.Players[0], trio.Players[1]), (trio.Players[1], trio.Players[0]) })
            {
                Settle(trio);
                (int fired, int landed, string note) = Duel(trio, shooter, target);
                string line = $"{stage}: {shooter.Name} -> {target.Name}: fired {fired}, landed {landed} {note}";
                output.WriteLine(line);
                if (fired == 0 || landed == 0)
                {
                    failures.Add(line);
                }
            }

            foreach (string logged in trio.ServerLog.Where(l => l.Contains("combat:", StringComparison.Ordinal) || l.Contains("shot:", StringComparison.Ordinal)))
            {
                output.WriteLine("  " + logged);
            }

            trio.ServerLog.Clear();
        }

        Check("m1 tdm");
        Restart(trio, "map 1 tdm");
        Check("m1 tdm restarted");
        foreach ((int mission, string mode) in new[] { (2, GameModes.TeamDeathmatch), (3, GameModes.SearchAndDestroy), (1, GameModes.FreeForAll), (2, GameModes.Elimination), (4, GameModes.TeamDeathmatch) })
        {
            if (!arenas.Contains(mission))
            {
                continue;
            }

            Travel(trio, mission, mode);
            Check($"m{mission} {mode}");
        }

        Assert.True(failures.Count == 0, "rounds passed by:\n" + string.Join("\n", failures));
    }

    [RequiresGameDataFact]
    public void A_dedicated_server_meets_a_players_head_where_its_body_is_drawn_whichever_way_it_faces()
    {
        // The head stands forward of the look over the gun. Turned by the yaw rather than the gamma it is drawn with, the
        // body rounds were scored on was the drawn one mirrored: met in the trunk, never in the head the shooter aimed at.
        using Igi1DedicatedTrio trio = new(GameModes.SearchAndDestroy, 1, 0.10, 0.10);
        Assert.True(trio.RunUntil(() => trio.AllIn, 3000) >= 0, "the players never both came in");
        Settle(trio);
        Igi1DedicatedTrio.Player target = trio.Players[1];
        Igi1NetworkPlayer there = trio.OnServer(target);
        Game.Igi1.Ai.SoldierSimulation soldiers = trio.Server!.World.Soldiers!;
        List<string> failures = [];
        foreach (double yaw in new[] { 0.0, Math.PI / 2, Math.PI, -Math.PI / 2, Math.PI / 4, -3 * Math.PI / 4 })
        {
            target.Own.Body.Yaw = there.Player.Body.Yaw = (float)yaw;
            trio.RunUntil(() => false, 20);
            float facing = there.Player.Body.Yaw;
            Vector3d forward = new(Math.Sin(facing), Math.Cos(facing), 0);
            Vector3d side = new(forward.Y, -forward.X, 0);
            Vector3d feet = there.Player.Body.Position;

            // Rays across the body at head height, stepped from behind it to in front: where they meet the head.
            List<double> heads = [];
            for (double ahead = -0.4; ahead <= 0.401; ahead += 0.05)
            {
                for (double height = 1.55; height <= 1.801; height += 0.05)
                {
                    Vector3d at = feet + (forward * ahead * 4096) + new Vector3d(0, 0, height * 4096);
                    if (soldiers.TryCastPartnerBody(there, at - (side * 3 * 4096), at + (side * 3 * 4096), feet, facing, out IReadOnlyList<Game.Player.HumanBodyCrossing> crossings)
                        && crossings.Any(c => c.Hit.Zone == 16))
                    {
                        heads.Add(ahead);
                    }
                }
            }

            string line = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"yaw {facing:F2}: head met on {heads.Count} ray(s), {(heads.Count > 0 ? heads.Average() : double.NaN):F2} m ahead of the feet");
            output.WriteLine(line);
            if (heads.Count == 0 || heads.Average() <= 0.05)
            {
                failures.Add(line);
            }
        }

        Assert.True(failures.Count == 0, "the head was not met where it is drawn:\n" + string.Join("\n", failures));
    }

    [RequiresGameDataFact]
    public void The_server_console_lists_the_players_and_puts_one_in_front_of_another_where_its_own_game_follows()
    {
        using Igi1DedicatedTrio trio = new(GameModes.TeamDeathmatch, 1, 0.17, 0.10);
        Assert.True(trio.RunUntil(() => trio.AllIn, 3000) >= 0, "the players never both came in");
        trio.RunUntil(() => false, 30 * 2);

        IReadOnlyList<string> where = trio.Console("where");
        foreach (string line in where)
        {
            output.WriteLine(line);
        }

        Assert.Equal(2, where.Count);
        Assert.All(where, l => Assert.Contains("hp 100", l, StringComparison.Ordinal));

        string moved = Assert.Single(trio.Console("tp Anya Boris 4"));
        output.WriteLine(moved);
        Igi1NetworkPlayer anya = trio.OnServer(trio.Players[0]);
        Igi1NetworkPlayer boris = trio.OnServer(trio.Players[1]);
        double Apart() => Vector3d.Distance(anya.Player.Body.Position, boris.Player.Body.Position) / 4096;
        output.WriteLine($"apart on the server just after: {Apart():F2} m");
        trio.RunUntil(() => false, 30 * 3);
        double ownApart = Vector3d.Distance(trio.Players[0].Own.Body.Position, boris.Player.Body.Position) / 4096;
        output.WriteLine($"apart on the server 3 s on: {Apart():F2} m; in Anya's own game: {ownApart:F2} m");
        Assert.InRange(Apart(), 3, 5);
        Assert.InRange(ownApart, 3, 5);
    }

    // The console's command, and both players back in and standing once it has taken.
    private void Restart(Igi1DedicatedTrio trio, string command)
    {
        output.WriteLine($"> {command}: {string.Join(" / ", trio.Console(command))}");
        trio.RunUntil(() => false, 30 * 2);
        Assert.True(trio.RunUntil(() => trio.AllIn, 3000) >= 0, $"the players never came back after '{command}'");
    }

    private void Travel(Igi1DedicatedTrio trio, int mission, string mode)
    {
        object?[] before = [trio.Server?.Level, .. trio.Players.Select(p => p.Level?.Level)];
        string command = $"map {mission} {mode}";
        output.WriteLine($"> {command}: {string.Join(" / ", trio.Console(command))}");
        string map = mission.ToString(System.Globalization.CultureInfo.InvariantCulture);
        bool Arrived() =>
            trio.Session.Map == map
            && !ReferenceEquals(trio.Server?.Level, before[0])
            && trio.Players.Select((p, i) => !ReferenceEquals(p.Level?.Level, before[i + 1])).All(static moved => moved)
            && trio.AllIn;
        if (trio.RunUntil(Arrived, 30 * 120) < 0)
        {
            foreach (string line in trio.ServerLog.TakeLast(40))
            {
                output.WriteLine("  | " + line);
            }

            output.WriteLine($"  map {trio.Session.Map}, server level changed {!ReferenceEquals(trio.Server?.Level, before[0])}, partners {trio.Server?.World.NetworkPlayers.Count}, " +
                string.Join(", ", trio.Players.Select(p => $"{p.Name} joined {p.Joined} level changed {!ReferenceEquals(p.Level?.Level, before[trio.Players.ToList().IndexOf(p) + 1])} player {p.Level?.World.Player is not null}")));
            Assert.Fail($"the players never came into mission {mission}");
        }
    }

    // Until the round is played -- past a freeze -- and both stand.
    private static void Settle(Igi1DedicatedTrio trio)
    {
        trio.RunUntil(
            () => trio.Session.Session is { } session
                && session.Mode.Flow.Phase is MatchPhase.Live or MatchPhase.Warmup
                && session.Mode.Report(trio.Server!.World.PlayerTicks).FreezeLeft <= 0
                && trio.Players.All(p => !trio.OnServer(p).IsDead),
            30 * 60);
        trio.RunUntil(() => false, 30);
    }

    // The shooter stands five metres from the target, a gun in hand, and fires at the chest where its own game draws the
    // target, until three rounds have hurt it or eight are out. Landed is counted on the server.
    private static (int Fired, int Landed, string Note) Duel(Igi1DedicatedTrio trio, Igi1DedicatedTrio.Player shooter, Igi1DedicatedTrio.Player target)
    {
        Igi1NetworkPlayer shooterThere = trio.OnServer(shooter);
        Igi1NetworkPlayer targetThere = trio.OnServer(target);
        Vector3d feet = shooterThere.Player.Body.Position;
        Vector3d? ahead = ClearAhead(trio, feet);
        if (ahead is not { } along)
        {
            return (0, 0, "(no clear lane)");
        }

        // Both put where they stand, on the server and in their own games.
        Vector3d spot = feet + (along * 5 * 4096);
        shooter.Own.Body.Position = shooterThere.Player.Body.Position = feet;
        target.Own.Body.Position = targetThere.Player.Body.Position = spot;
        trio.RunUntil(() => false, 45);

        // A gun in hand, picked with the number keys.
        foreach (KeyCode category in new[] { KeyCode.D2, KeyCode.D3, KeyCode.D4, KeyCode.D5 })
        {
            if (shooterThere.Player.Weapons?.InHand?.Weapon.Category is Game.Weapons.WeaponCategory.Pistol or Game.Weapons.WeaponCategory.Gun)
            {
                break;
            }

            shooter.Input.Keyboard.SetKey(category, true);
            trio.RunUntil(() => false, 3);
            shooter.Input.Keyboard.SetKey(category, false);
            trio.RunUntil(() => false, 45);
        }

        if (shooterThere.Player.Weapons?.InHand?.Weapon.Category is not (Game.Weapons.WeaponCategory.Pistol or Game.Weapons.WeaponCategory.Gun))
        {
            return (0, 0, "(no gun in hand)");
        }

        int before = shooterThere.Player.Weapons!.ShotSequence;
        float damage = targetThere.Health.Damage;
        float startDamage = damage;
        int landed = 0;
        int frame = 0;
        trio.RunUntil(
            () =>
            {
                if (shooter.Level!.World.ShownPartners.FirstOrDefault(p => p.Name == target.Name) is { } shown)
                {
                    Vector3d toward = shown.Player.Body.Position + new Vector3d(0, 0, 1.3 * 4096) - shooter.Own.Body.Eye;
                    shooter.Own.Body.Yaw = (float)Math.Atan2(toward.X, toward.Y);
                    shooter.Own.Body.Pitch = (float)Math.Atan2(toward.Z, Math.Sqrt((toward.X * toward.X) + (toward.Y * toward.Y)));
                }

                shooter.Input.Mouse.SetButton(MouseButton.Left, frame++ % 8 < 2);
                if (targetThere.Health.Damage != damage)
                {
                    landed++;
                    damage = targetThere.Health.Damage;
                }

                return targetThere.IsDead || landed >= 3 || shooterThere.Player.Weapons.ShotSequence - before >= 8;
            },
            30 * 20);
        shooter.Input.Mouse.SetButton(MouseButton.Left, false);
        trio.RunUntil(() => false, 15);

        int fired = shooterThere.Player.Weapons.ShotSequence - before;
        string shown2 = shooter.Level!.World.ShownPartners.FirstOrDefault(p => p.Name == target.Name) is { } drawn
            ? $"drawn {Vector3d.Distance(drawn.Player.Body.Position, targetThere.Player.Body.Position) / 4096:F2} m off"
            : "target not drawn";
        return (fired, landed, $"(damage {startDamage:F2}->{targetThere.Health.Damage:F2}, target own hp {target.Level!.World.PlayerHealth.RemainingFraction:P0}, {shown2}, teams {Team(trio, shooter)}/{Team(trio, target)})");
    }

    private static int Team(Igi1DedicatedTrio trio, Igi1DedicatedTrio.Player player) =>
        trio.Session.Session!.Match.Players.Values.FirstOrDefault(p => p.Name == player.Name)?.Team ?? -9;

    // A way along which a round flies five metres clear of anything solid, with ground at its end.
    private static Vector3d? ClearAhead(Igi1DedicatedTrio trio, Vector3d feet)
    {
        if (trio.Server?.World.Solids?.Caster() is not { } cast)
        {
            return null;
        }

        Vector3d from = feet + new Vector3d(0, 0, 1.6 * 4096);
        for (int i = 0; i < 16; i++)
        {
            Vector3d along = new(Math.Sin(i * Math.PI / 8), Math.Cos(i * Math.PI / 8), 0);
            if (!cast(from, from + (along * 7 * 4096), out _)
                && !cast(from + (along * 5 * 4096), feet + (along * 5 * 4096) + new Vector3d(0, 0, 256), out _))
            {
                return along;
            }
        }

        return null;
    }
}
