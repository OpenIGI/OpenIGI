using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Input;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>I.G.I. 2 co-op: the guards a joiner is shown go where the host's went, smoothly, however the line carries the snapshots.</summary>
[Collection(ParityGroup.Name)]
public sealed class Igi2CoopGuardMotionTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const double Metre = 4096;

    /// <summary>
    /// The host's gunfire stirs the guards, and the joiner's copies of them are watched frame by frame: each is drawn between two
    /// snapshots, where the host's guard stood at the moment drawn -- not carried on past the newest snapshot, nor felled or
    /// thrown about by the joiner's own rounds on its copies of them.
    /// </summary>
    /// <param name="ping">The round trip, in seconds.</param>
    /// <param name="bad">Whether the line also wanders and loses a packet in twenty each way.</param>
    /// <param name="wounds">Whether the joiner's rounds strike a guard now and then, each marked on its screen and nothing more.</param>
    [RequiresTitleDataTheory("igi2")]
    [InlineData(0.1, false, false)]
    [InlineData(0.1, true, true)]
    [InlineData(0.25, true, true)]
    public void The_joiners_guards_are_drawn_where_the_hosts_went(double ping, bool bad, bool wounds)
    {
        using Igi2CoopPair pair = Igi2CoopPairTests.Joined();
        pair.Ping(ping);
        if (bad)
        {
            pair.JoinerLink.Send.Jitter = pair.JoinerLink.Receive.Jitter = 0.03;
            pair.JoinerLink.Send.Loss = pair.JoinerLink.Receive.Loss = 0.05;
        }

        ClientSession session = pair.Client.Session!;

        pair.Host!.Human.Damage.IsUnharmed = true;
        Assert.Single(pair.Host.NetworkHumans).Human.Damage.IsUnharmed = true;
        pair.RunUntil(() => false, 150);

        IReadOnlyList<Igi2HumanSoldier> there = pair.Host.Soldiers!.Bodies;
        IReadOnlyList<Igi2HumanSoldier> here = pair.Joiner!.Soldiers!.Bodies;
        Dictionary<long, Dictionary<int, Vector3d>> truth = [];
        Dictionary<int, Vector3d> last = [];
        Dictionary<int, int> lastState = [];
        Dictionary<int, int> hostState = [];
        long samples = session.Remote.Samples, extrapolations = session.Remote.Extrapolations, corrections = session.Reconciler.Corrections;
        int frames = 0, watched = 0, jumps = 0, astray = 0, high = 0, moving = 0, changesHere = 0, changesThere = 0, wounded = 0, deadHere = 0;
        double worstStep = 0, worstAstray = 0, worstHigh = 0, sumAstray = 0;
        OpenIGI.Game.Multiplayer.UserCommandBuffer commands = Assert.Single(pair.Host.NetworkHumans).Commands;
        long queued = 0;
        for (int frame = 0; frame < 900 && pair.Step(); frame++)
        {
            queued += commands.Buffered;

            // The host's rounds, a burst every five seconds: the guards hear them and come.
            pair.HostInput.Mouse.SetButton(MouseButton.Left, frame % 150 < 20);
            if (pair.Session.Session?.LastSnapshot is not { } taken || session.DrawnTick is not { } drawn)
            {
                continue;
            }

            Dictionary<int, Vector3d> now = [];
            for (int i = 0; i < there.Count; i++)
            {
                if (there[i] is { IsActive: true, IsDead: false } guard)
                {
                    now[i] = guard.Position;
                    changesThere += hostState.TryGetValue(i, out int stateWas) && stateWas != guard.Locomotion.CurrentState ? 1 : 0;
                    hostState[i] = guard.Locomotion.CurrentState;
                }
            }

            truth[taken.Tick] = now;
            truth.Remove(taken.Tick - 90);
            frames++;
            long before = (long)Math.Floor(drawn);
            foreach ((int i, _) in now)
            {
                Igi2HumanSoldier shown = here[i];
                if (shown.IsDead)
                {
                    // Down here while the host's lives: no round of the joiner's fells its copy of a guard, only the server's word.
                    deadHere++;
                    continue;
                }

                // A round from the joiner's gun on a guard on the move, heavy enough to kill: marked on its screen, the rest the server's.
                if (wounds && frame % 45 == 30 && wounded <= frame / 45
                    && last.TryGetValue(i, out Vector3d stood) && (shown.Position - stood).Length > 0.01 * Metre)
                {
                    wounded++;
                    shown.WeaponHit(new OpenIGI.Game.Igi2.Weapons.Igi2WeaponHit(
                        new OpenIGI.Game.Physics.WorldIntersection { Position = shown.Position }, 1_000_000f, new Vector3f(40960f, 0f, 0f),
                        pair.Joiner.Human, pair.Joiner.Weapons!.Inventory.Current!.WeaponType, null));
                }

                Vector3d at = shown.Position;
                if (last.TryGetValue(i, out Vector3d was))
                {
                    double step = (at - was).Length / Metre;
                    watched++;
                    moving += step > 0.01 ? 1 : 0;
                    worstStep = Math.Max(worstStep, step);
                    jumps += step > 0.5 ? 1 : 0;
                }

                last[i] = at;
                changesHere += lastState.TryGetValue(i, out int state) && state != shown.Locomotion.CurrentState ? 1 : 0;
                lastState[i] = shown.Locomotion.CurrentState;
                if (truth.TryGetValue(before, out Dictionary<int, Vector3d>? a) && a.TryGetValue(i, out Vector3d from)
                    && truth.TryGetValue(before + 1, out Dictionary<int, Vector3d>? b) && b.TryGetValue(i, out Vector3d to))
                {
                    Vector3d meant = Vector3d.Lerp(from, to, drawn - before);
                    double off = (at - meant).Length / Metre;
                    double up = Math.Abs(at.Z - meant.Z) / Metre;
                    sumAstray += off;
                    worstAstray = Math.Max(worstAstray, off);
                    worstHigh = Math.Max(worstHigh, up);
                    astray += off > 0.25 ? 1 : 0;
                    high += up > 0.25 ? 1 : 0;
                }
            }
        }

        pair.HostInput.Mouse.SetButton(MouseButton.Left, false);
        long asked = session.Remote.Samples - samples;
        long carried = session.Remote.Extrapolations - extrapolations;
        string report = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"igi2 ping {ping * 1000:0} ms bad {bad} wounds {wounded}: interp {session.Clock!.AppliedInterpolationDelay * 1000:0} ms, rtt {session.RoundTripTime * 1000:0} ms; " +
            $"extrapolated {carried} of {asked} samples ({100.0 * carried / Math.Max(1, asked):0.0}%); " +
            $"guard frames {watched} ({moving} moving): jumps>0.5m {jumps}, worst step {worstStep:0.00} m; " +
            $"astray>0.25m {astray}, worst {worstAstray:0.00} m, mean {sumAstray / Math.Max(1, watched) * 100:0.0} cm; high>0.25m {high}, worst {worstHigh:0.00} m; " +
            $"state changes host {changesThere} joiner {changesHere}; down-here-alive-there frames {deadHere}; " +
            $"host holds {queued / 900.0:0.00} of the joiner's commands (held back {commands.Held}); corrections {(session.Reconciler.Corrections - corrections) / (frames / 30.0):0.00}/s");
        output.WriteLine(report);

        Assert.True(carried * 50 <= asked, $"the joiner's guards were carried past the newest snapshot; {report}");
        Assert.True(jumps == 0, $"a guard jumped; {report}");
        Assert.True(astray * 100 <= Math.Max(100, watched), $"the joiner's guards strayed from where the host's went; {report}");
        Assert.Equal(0, deadHere);

        // The host runs the joiner's commands as they arrive, holding back no more than the line's unevenness asks.
        Assert.True(queued / 900.0 < (bad ? 2.5 : 1.0), report);
    }

    /// <summary>
    /// A guard's rounds on the joiner knock its player along the shot on its own screen, and the server's word on the commands
    /// since puts it back no further than a step, a handful of times a hit, standing or on the move.
    /// </summary>
    /// <remarks>
    /// A bound on what it costs today, not what it should: the server's copy flinches and is carried by a knock the client's
    /// replay knows nothing of, so each hit is still corrected three to five times (measured 2026-11-22: 35, 58 and 34
    /// corrections to 12 hits; 58, 56 and 34 with the knock felt late as it was before).
    /// </remarks>
    /// <param name="ping">The round trip, in seconds.</param>
    /// <param name="bad">Whether the line also wanders and loses a packet in twenty each way.</param>
    /// <param name="walking">Whether the joiner walks on through the hits.</param>
    [RequiresTitleDataTheory("igi2")]
    [InlineData(0.1, false, false)]
    [InlineData(0.1, false, true)]
    [InlineData(0.25, true, true)]
    public void A_guards_rounds_knock_the_joiner_a_step_at_most(double ping, bool bad, bool walking)
    {
        using Igi2CoopPair pair = Igi2CoopPairTests.Joined();
        pair.Ping(ping);
        if (bad)
        {
            pair.JoinerLink.Send.Jitter = pair.JoinerLink.Receive.Jitter = 0.03;
            pair.JoinerLink.Send.Loss = pair.JoinerLink.Receive.Loss = 0.05;
        }

        ClientSession session = pair.Client.Session!;
        OpenIGI.Game.Igi2.Player.Igi2HumanPlayer partner = Assert.Single(pair.Host!.NetworkHumans).Human;
        Igi2HumanSoldier guard = pair.Host.Soldiers!.Bodies.First(static s => s is { IsActive: true, IsDead: false } && s.Weapon.Inventory?.Current is not null);
        pair.RunUntil(() => false, 150);

        // What the line costs with no hit at all, over the same walk.
        CoopPair<OpenIGI.Game.Igi2.Player.Igi2TitlePlayer>.Hold(pair.JoinerInput, KeyCode.W, walking);
        long before = session.Reconciler.Corrections;
        pair.RunUntil(() => false, 240);
        long idle = session.Reconciler.Corrections - before;

        before = session.Reconciler.Corrections;
        int hits = 0;
        double worst = 0;
        for (int frame = 0; frame < 480 && pair.Step(); frame++)
        {
            if (frame % 40 == 20)
            {
                hits++;
                partner.WeaponHit(new OpenIGI.Game.Igi2.Weapons.Igi2WeaponHit(
                    new OpenIGI.Game.Physics.WorldIntersection { Position = partner.Position }, 0.0001f, new Vector3f(40960f, 0f, 0f),
                    guard, guard.Weapon.Inventory!.Current!.WeaponType, null));
            }

            worst = Math.Max(worst, session.Reconciler.LastError);
        }

        CoopPair<OpenIGI.Game.Igi2.Player.Igi2TitlePlayer>.Hold(pair.JoinerInput, KeyCode.W, false);
        long corrections = session.Reconciler.Corrections - before;
        string report = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"igi2 knock ping {ping * 1000:0} ms bad {bad} walking {walking}: {hits} hits, {corrections} corrections ({idle} in 8 s with none), worst error {worst / Metre:0.00} m, dead {pair.Joiner!.Human.IsDead}");
        output.WriteLine(report);
        Assert.Equal(0, idle);
        Assert.True(corrections <= hits * 5 && worst < Metre, report);
    }

    /// <summary>
    /// The debugging tools work end to end in two games: both traces written, a player put before a guard, the guard hurt and
    /// killed by the server's commands, and the joiner's copy falling with it. The traces are left for compare.py.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_debugging_tools_trace_both_sides_and_move_and_harm_guards()
    {
        string folder = Path.Combine(Path.GetTempPath(), "openigi-netprobe-test");
        using Igi2CoopPair pair = Igi2CoopPairTests.Joined();
        pair.Ping(0.1);
        pair.RunUntil(() => false, 60);
        output.WriteLine(NetProbe.Command(server: true, ["on"], folder, 0));
        output.WriteLine(NetProbe.Command(server: false, ["on"], folder, 0));
        ServerSession server = pair.Session.Session!;
        string name = pair.Client.PlayerName;
        foreach (string line in server.Debug(["guards", name]).Take(3))
        {
            output.WriteLine(line);
        }

        string moved = Assert.Single(server.Debug(["tp", name, $"nearest:{name}", "6"]));
        output.WriteLine(moved);
        Assert.Contains("stands", moved, StringComparison.Ordinal);
        pair.RunUntil(() => false, 60);
        int guard = server.LastSnapshot!.Humans.Where(static h => ReplicatedEntity.IsSoldier(h.Id) && (h.Flags & ReplicatedHumanCondition.Alive) != 0)
            .OrderBy(h => (h.Position.ToVector() - pair.Host!.NetworkHumans[0].Human.Position).Length).First().Id;
        output.WriteLine(Assert.Single(server.Debug(["hurt", guard.ToString(System.Globalization.CultureInfo.InvariantCulture), "0.3"])));
        pair.RunUntil(() => false, 60);
        output.WriteLine(Assert.Single(server.Debug(["kill", guard.ToString(System.Globalization.CultureInfo.InvariantCulture)])));
        Igi2HumanSoldier copy = pair.Joiner!.Soldiers!.Bodies[Game.Igi2.Multiplayer.Igi2ReplicatedWorld.SoldierIndex(guard)!.Value];
        Assert.True(pair.RunUntil(() => copy.IsDead, 60) >= 0, "the joiner's copy never fell");
        pair.RunUntil(() => false, 60);
        output.WriteLine(NetProbe.Command(server: true, ["off"], folder, 0));
        output.WriteLine(NetProbe.Command(server: false, ["off"], folder, 0));
    }
}
