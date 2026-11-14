using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Multiplayer;
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
    public void Players_on_a_dedicated_server_cannot_walk_through_each_other()
    {
        // 0x462BA0: a human's move that would end within 0.55 m of another living one, and nearer it, is refused.
        using Igi1DedicatedTrio trio = new(GameModes.TeamDeathmatch, 1, 0.10, 0.10);
        Assert.True(trio.RunUntil(() => trio.AllIn, 3000) >= 0, "the players never both came in");
        Settle(trio);
        Igi1DedicatedTrio.Player walker = trio.Players[0];
        Igi1DedicatedTrio.Player standing = trio.Players[1];
        Igi1NetworkPlayer walkerThere = trio.OnServer(walker);
        Igi1NetworkPlayer standingThere = trio.OnServer(standing);
        Vector3d feet = walkerThere.Player.Body.Position;
        Vector3d along = ClearAhead(trio, feet) ?? throw new Xunit.Sdk.XunitException("no clear lane");
        Vector3d spot = feet + (along * 1.2 * 4096);
        walker.Own.Body.Position = walkerThere.Player.Body.Position = feet;
        standing.Own.Body.Position = standingThere.Player.Body.Position = spot;
        trio.RunUntil(() => false, 45);

        // Facing the other, 1.2 m off, walking at it for six seconds: without the pass it ends inside the other's body.
        double nearestThere = double.MaxValue, nearestOwn = double.MaxValue;
        trio.RunUntil(
            () =>
            {
                Vector3d toward = standingThere.Player.Body.Position - walker.Own.Body.Position;
                walker.Own.Body.Yaw = (float)Math.Atan2(toward.X, toward.Y);
                walker.Input.Keyboard.SetKey(KeyCode.W, true);
                nearestThere = Math.Min(nearestThere, Flat(walkerThere.Player.Body.Position, standingThere.Player.Body.Position));
                nearestOwn = Math.Min(nearestOwn, Flat(walker.Own.Body.Position, standingThere.Player.Body.Position));
                return false;
            },
            30 * 6);
        walker.Input.Keyboard.SetKey(KeyCode.W, false);
        trio.RunUntil(() => false, 15);
        double walked = Flat(feet, walkerThere.Player.Body.Position);
        output.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"walked {walked:F2} m; nearest on the server {nearestThere:F2} m, in the walker's own game {nearestOwn:F2} m"));
        Assert.True(walked > 0.3, "the walker never walked up to the other");
        Assert.InRange(nearestThere, 0.45, 2.5);
        Assert.InRange(nearestOwn, 0.45, 2.5);
    }

    private static double Flat(Vector3d a, Vector3d b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y))) / 4096;

    [RequiresGameDataFact]
    public void A_knife_in_a_players_back_kills_it_from_every_side_of_its_back_and_one_from_the_front_does_not()
    {
        // A tester stood at another's back, as near as the bodies allow, and stabbed: at some angles nothing was met. The
        // stabber's own body was told by a 1 m square about the blade's start, which on a diagonal held the other's body too.
        using Igi1DedicatedTrio trio = new(GameModes.TeamDeathmatch, 1, 0.10, 0.10);
        Assert.True(trio.RunUntil(() => trio.AllIn, 3000) >= 0, "the players never both came in");
        Igi1DedicatedTrio.Player stabber = trio.Players[0];
        Igi1DedicatedTrio.Player stabbed = trio.Players[1];
        List<string> failures = [];
        foreach ((double degrees, bool back) in new[] { (0.0, true), (30.0, true), (45.0, true), (-45.0, true), (-30.0, true), (180.0, false) })
        {
            Settle(trio);
            Igi1NetworkPlayer stabberThere = trio.OnServer(stabber);
            Igi1NetworkPlayer stabbedThere = trio.OnServer(stabbed);
            Vector3d feet = stabberThere.Player.Body.Position;
            Vector3d along = ClearAhead(trio, feet) ?? throw new Xunit.Sdk.XunitException("no clear lane");

            // The stabbed looks down the lane; the stabber stands 65 cm from it, the given angle round from its back.
            Vector3d spot = feet + (along * 1.0 * 4096);
            float facing = (float)Math.Atan2(along.X, along.Y);
            double round = facing + Math.PI + (degrees * Math.PI / 180);
            Vector3d at = spot + (new Vector3d(Math.Sin(round), Math.Cos(round), 0) * 0.65 * 4096);
            stabber.Own.Body.Position = stabberThere.Player.Body.Position = at;
            stabbed.Own.Body.Position = stabbedThere.Player.Body.Position = spot;
            trio.RunUntil(
                () =>
                {
                    stabbed.Own.Body.Yaw = facing;
                    return false;
                },
                30);

            // The knife in hand.
            foreach (KeyCode key in new[] { KeyCode.D1, KeyCode.D2, KeyCode.D3, KeyCode.D4, KeyCode.D5 })
            {
                if (stabberThere.Player.Weapons?.InHand?.Weapon.Category == Game.Weapons.WeaponCategory.Knife)
                {
                    break;
                }

                stabber.Input.Keyboard.SetKey(key, true);
                trio.RunUntil(() => false, 3);
                stabber.Input.Keyboard.SetKey(key, false);
                trio.RunUntil(() => false, 45);
            }

            Assert.Equal(Game.Weapons.WeaponCategory.Knife, stabberThere.Player.Weapons?.InHand?.Weapon.Category);
            float before = stabbedThere.Health.Damage;
            int frame = 0;
            trio.RunUntil(
                () =>
                {
                    stabbed.Own.Body.Yaw = facing;
                    Vector3d toward = stabbedThere.Player.Body.Position + new Vector3d(0, 0, 1.2 * 4096) - stabber.Own.Body.Eye;
                    stabber.Own.Body.Yaw = (float)Math.Atan2(toward.X, toward.Y);
                    stabber.Own.Body.Pitch = (float)Math.Atan2(toward.Z, Math.Sqrt((toward.X * toward.X) + (toward.Y * toward.Y)));
                    stabber.Input.Mouse.SetButton(MouseButton.Left, frame++ < 3);
                    return stabbedThere.Health.Damage != before;
                },
                45);
            stabber.Input.Mouse.SetButton(MouseButton.Left, false);
            trio.RunUntil(() => false, 30);
            string line = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{degrees:F0} deg from the back: damage {before:F2} -> {stabbedThere.Health.Damage:F2}, dead {stabbedThere.IsDead}");
            output.WriteLine(line);
            if (stabbedThere.Health.Damage == before || stabbedThere.IsDead != back)
            {
                failures.Add(line);
            }

            // Back on its feet for the next.
            if (stabbedThere.IsDead)
            {
                trio.RunUntil(() => !stabbedThere.IsDead, 30 * 20);
            }
        }

        Assert.True(failures.Count == 0, "the knife did not do what it should:\n" + string.Join("\n", failures));
    }

    [RequiresGameDataFact]
    public void A_pistol_shot_plays_once_on_the_shooters_body_in_the_other_players_game()
    {
        // A tester saw another's pistol shot repeat on its body: the server holds the torso on the shot's clip until the gun
        // is ready, and the other game looped every torso clip it was sent.
        using Igi1DedicatedTrio trio = new(GameModes.TeamDeathmatch, 1, 0.10, 0.10);
        Assert.True(trio.RunUntil(() => trio.AllIn, 3000) >= 0, "the players never both came in");
        Settle(trio);
        Igi1DedicatedTrio.Player shooter = trio.Players[0];
        Igi1DedicatedTrio.Player watcher = trio.Players[1];
        foreach (KeyCode key in new[] { KeyCode.D2, KeyCode.D3 })
        {
            if (shooter.Own.Weapons?.InHand?.Weapon.Category == Game.Weapons.WeaponCategory.Pistol)
            {
                break;
            }

            shooter.Input.Keyboard.SetKey(key, true);
            trio.RunUntil(() => false, 3);
            shooter.Input.Keyboard.SetKey(key, false);
            trio.RunUntil(() => false, 45);
        }

        Assert.Equal(Game.Weapons.WeaponCategory.Pistol, shooter.Own.Weapons?.InHand?.Weapon.Category);
        int fireClip = shooter.Own.Weapons!.InHand!.Value.Weapon.UpperBodyFireAnimation;
        bool? seen = null;
        int frame = 0;
        trio.RunUntil(
            () =>
            {
                shooter.Input.Mouse.SetButton(MouseButton.Left, frame++ < 2);
                if (watcher.Level!.World.PartnerBodies?.Invoke() is { } bodies)
                {
                    foreach (Game.Igi1.Ai.PartnerBody body in bodies)
                    {
                        if (body.Pose.UpperBodyAnimation == fireClip)
                        {
                            seen = body.Pose.UpperBodyLoops;
                            return true;
                        }
                    }
                }

                return false;
            },
            30 * 3);
        shooter.Input.Mouse.SetButton(MouseButton.Left, false);
        Assert.True(seen is not null, "the shot's torso clip never reached the other game");
        Assert.False(seen.Value, "the other game loops the shot's torso clip");
    }

    [RequiresGameDataFact]
    public void A_hit_and_a_kill_on_another_player_mark_the_shooters_own_crosshair_and_sound_in_either_display()
    {
        // The crosshair's marks and the hit sounds read the player's own tally, which only the soldiers counted into: a hit on
        // a player, settled by the server, marked nothing in either display.
        using Igi1DedicatedTrio trio = new(GameModes.TeamDeathmatch, 1, 0.10, 0.10);
        Assert.True(trio.RunUntil(() => trio.AllIn, 3000) >= 0, "the players never both came in");
        Settle(trio);
        Igi1DedicatedTrio.Player shooter = trio.Players[0];
        Igi1DedicatedTrio.Player target = trio.Players[1];
        // A session marks and sounds every hit whatever the settings, and leaves them as they were.
        Rendering.HudCVars.HitMarkers.Value = false;
        Rendering.HudCVars.HitSounds.Value = false;
        Assert.True(Rendering.HudCVars.ShowsHitMarkers, "a session's hits went unmarked with the setting off");
        Assert.True(Rendering.HudCVars.SoundsHits, "a session's hits went unsounded with the setting off");
        Assert.False(Rendering.HudCVars.HitMarkers.Value);
        Game.Player.ShotTally tally = shooter.Own.Shots;
        int hitsBefore = tally.Hits;
        int killsBefore = tally.Kills;
        (int fired, int landed, string note) = Duel(trio, shooter, target);
        trio.RunUntil(() => false, 30);
        output.WriteLine($"fired {fired}, landed {landed} {note}; tally hits {hitsBefore}->{tally.Hits}, kills {killsBefore}->{tally.Kills}, soldier hits {tally.SoldierHits}");
        Assert.True(landed > 0, "no round landed");
        Assert.True(tally.Hits - hitsBefore >= landed, "the shooter's own tally missed its hits on the other player");
        Assert.Equal(0, tally.SoldierHits);

        // And a kill.
        Igi1NetworkPlayer targetThere = trio.OnServer(target);
        Settle(trio);
        (_, _, _) = Duel(trio, shooter, target);
        trio.RunUntil(() => targetThere.IsDead, 30 * 10);
        for (int i = 0; i < 6 && !targetThere.IsDead; i++)
        {
            Settle(trio);
            (_, _, _) = Duel(trio, shooter, target);
        }

        trio.RunUntil(() => false, 30);
        output.WriteLine($"target dead {targetThere.IsDead}; tally kills {killsBefore}->{tally.Kills}");
        Assert.True(tally.Kills > killsBefore, "the shooter's own tally missed its kill of the other player");
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

        int before = shooterThere.Player.Weapons.ShotSequence;
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
        return (fired, landed, $"(damage {startDamage:F2}->{targetThere.Health.Damage:F2}, target own hp {target.Level!.World.PlayerLife.Health.RemainingFraction:P0}, {shown2}, teams {Team(trio, shooter)}/{Team(trio, target)})");
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
