using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Multiplayer;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Input;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>Co-op: the guards a joiner is shown go where the host's went, smoothly, however the line carries the snapshots.</summary>
[Collection(ParityGroup.Name)]
public sealed class Igi1CoopGuardMotionTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const double Metre = 4096;

    /// <summary>
    /// The host's gunfire sets the guards running, and the joiner's copies of them are watched frame by frame: each is drawn
    /// between two snapshots, where the host's guard stood at the moment drawn -- not carried on past the newest one along its
    /// velocity, which overshoots every turn and stop and is put back when the next snapshot comes.
    /// </summary>
    /// <param name="ping">The round trip, in seconds.</param>
    /// <param name="bad">Whether the line also wanders and loses a packet in twenty each way.</param>
    [RequiresGameDataTheory]
    [InlineData(0.1, false)]
    [InlineData(0.1, true)]
    [InlineData(0.25, true)]
    public void The_joiners_guards_are_drawn_where_the_hosts_went(double ping, bool bad)
    {
        using Igi1CoopPair pair = new(mission: 1);
        try
        {
            Game.Player.PlayerCheatCVars.CheatsEnabled.Value = true;
            Game.Player.PlayerCheatCVars.InfiniteHealth.Value = true;
            Assert.True(pair.RunUntil(() => pair.Joined && pair.Host!.World.NetworkPlayers.Count == 1, 3000) >= 0, "the joiner was never welcomed");
            pair.Ping(ping);
            if (bad)
            {
                pair.JoinerLink.Send.Jitter = pair.JoinerLink.Receive.Jitter = 0.03;
                pair.JoinerLink.Send.Loss = pair.JoinerLink.Receive.Loss = 0.05;
            }

            ClientSession session = pair.Client.Session!;

            Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
            pair.Host.World.PlayerLife.Health.Invulnerable = true;
            partner.Health.Invulnerable = true;
            pair.RunUntil(() => false, 150);

            Game.Igi1.Ai.SoldierSimulation there = pair.Host.World.Soldiers!;
            Game.Igi1.Ai.SoldierSimulation here = pair.Joiner!.World.Soldiers!;
            Dictionary<long, Dictionary<int, Vector3d>> truth = [];
            Dictionary<int, Vector3d> last = [];
            long samples = session.Remote.Samples, extrapolations = session.Remote.Extrapolations, corrections = session.Reconciler.Corrections;
            int frames = 0, watched = 0, jumps = 0, astray = 0, high = 0, moving = 0;
            double worstStep = 0, worstAstray = 0, worstHigh = 0, sumAstray = 0;
            for (int frame = 0; frame < 900 && pair.Step(); frame++)
            {
                // The host's rounds into the air, a burst every five seconds: the guards hear them and run.
                pair.HostInput.Mouse.SetButton(MouseButton.Left, frame % 150 < 20);
                if (pair.Session.Session?.LastSnapshot is not { } taken || session.DrawnTick is not { } drawn)
                {
                    continue;
                }

                Dictionary<int, Vector3d> now = [];
                foreach (Game.Igi1.Player.SoldierView guard in there.SoldierViews)
                {
                    if (!guard.IsDead && Game.World.ConditionalContainerGate.IsTaskActive(guard.Soldier))
                    {
                        now[there.RuntimeIdOf(guard)] = guard.Position;
                    }
                }

                truth[taken.Tick] = now;
                truth.Remove(taken.Tick - 90);
                frames++;
                long before = (long)Math.Floor(drawn);
                foreach ((int id, _) in now)
                {
                    if (here.SoldierByRuntimeId(id) is not { IsDead: false } shown)
                    {
                        continue;
                    }

                    Vector3d at = shown.Position;
                    if (last.TryGetValue(id, out Vector3d was))
                    {
                        double step = (at - was).Length / Metre;
                        watched++;
                        moving += step > 0.01 ? 1 : 0;
                        worstStep = Math.Max(worstStep, step);
                        jumps += step > 0.5 ? 1 : 0;
                    }

                    last[id] = at;

                    // Where the host's guard stood at the moment the joiner says it drew.
                    if (truth.TryGetValue(before, out Dictionary<int, Vector3d>? a) && a.TryGetValue(id, out Vector3d from)
                        && truth.TryGetValue(before + 1, out Dictionary<int, Vector3d>? b) && b.TryGetValue(id, out Vector3d to))
                    {
                        Vector3d meant = Vector3d.Lerp(from, to, drawn - before);
                        double off = (at - meant).Length / Metre;
                        double up = (at.Z - meant.Z) / Metre;
                        sumAstray += off;
                        worstAstray = Math.Max(worstAstray, off);
                        worstHigh = Math.Max(worstHigh, Math.Abs(up));
                        astray += off > 0.25 ? 1 : 0;
                        high += Math.Abs(up) > 0.25 ? 1 : 0;
                    }
                }
            }

            pair.HostInput.Mouse.SetButton(MouseButton.Left, false);
            long asked = session.Remote.Samples - samples;
            long carried = session.Remote.Extrapolations - extrapolations;
            double seconds = frames / 30.0;
            string report = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"ping {ping * 1000:0} ms bad {bad}: interp {session.Clock!.AppliedInterpolationDelay * 1000:0} ms, rtt {session.RoundTripTime * 1000:0} ms; " +
                $"extrapolated {carried} of {asked} samples ({100.0 * carried / Math.Max(1, asked):0.0}%); " +
                $"guard frames {watched} ({moving} moving): jumps>0.5m {jumps}, worst step {worstStep:0.00} m; " +
                $"astray>0.25m {astray}, worst {worstAstray:0.00} m, mean {sumAstray / Math.Max(1, watched) * 100:0.0} cm; high>0.25m {high}, worst {worstHigh:0.00} m; " +
                $"corrections {(session.Reconciler.Corrections - corrections) / seconds:0.00}/s");
            output.WriteLine(report);

            Assert.True(moving > 300, $"the guards hardly moved; {report}");
            Assert.True(carried * 50 <= asked, $"the joiner's guards were carried past the newest snapshot; {report}");
            Assert.True(jumps == 0, $"a guard jumped; {report}");
            Assert.True(astray * 100 <= watched, $"the joiner's guards strayed from where the host's went; {report}");
        }
        finally
        {
            Core.Configuration.CVarRegistry.Default.ResetAll();
        }
    }
}
