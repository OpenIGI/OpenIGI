using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Input;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>
/// A joiner's own player on what carries it -- a ladder, a conveyor belt, water -- over a line with a real round trip, its
/// game stalling as a browser's does: it stays where its server has it, and is not pulled about by its server's word.
/// </summary>
[Collection(ParityGroup.Name)]
public sealed class Igi2CoopClientMoveTests
{
    // A round trip between continents, and a stall of a second: a page taking in a new part of a level.
    private const double Ping = 0.35;
    private const int Stall = 30;

    // A mission that starts its player in water.
    private const int WaterMission = 21;

    /// <summary>
    /// A joiner climbing a ladder whose game stalls for a second and then runs the ticks it owes climbs on and gets off at the
    /// top, uncorrected. Its commands once went sequenced: the stall's were sent in one instant, arrived in any order, and all
    /// but the newest few were thrown away, so the server's copy hung steps below a client that had already got off -- and
    /// from then every snapshot pulled the client's falling player back to the ladder.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_joiner_climbing_a_ladder_through_a_stall_gets_off_at_the_top_uncorrected()
    {
        using Igi2CoopPair pair = OnALadder(out IIgi2Ladder ladder);
        Igi2HumanPlayer own = pair.Joiner!.Human, copy = pair.Host!.NetworkHumans[0].Human;
        long before = pair.Client.Session!.Reconciler.Corrections;

        Igi2CoopPair.Hold(pair.JoinerInput, KeyCode.W, true);
        pair.RunUntil(() => false, 40);
        StallJoiner(pair);
        Assert.True(
            pair.RunUntil(() => own.ClimbMode == 0 && copy.ClimbMode == 0 && Grounded(own) && Grounded(copy), 400) >= 0,
            $"never off the ladder on both: joiner state {own.Locomotion.CurrentState}, server's copy {copy.Locomotion.CurrentState}");
        Igi2CoopPair.Hold(pair.JoinerInput, KeyCode.W, false);
        pair.RunUntil(() => false, 60);

        double top = ladder.MagicVertex(3).Z;
        Assert.True(own.Position.Z > top - 4096 && copy.Position.Z > top - 4096, $"not at the top ({top:F0}): joiner {own.Position.Z:F0}, server's copy {copy.Position.Z:F0}");
        Assert.True(Vector3d.Distance(own.Position, copy.Position) < 512, $"the joiner stands {Vector3d.Distance(own.Position, copy.Position):F0} units from its server's copy");
        long corrections = pair.Client.Session.Reconciler.Corrections - before;
        Assert.True(corrections == 0, $"{corrections} corrections on the ladder; last error {pair.Client.Session.Reconciler.LastError:F0}; dropped {pair.Host.NetworkHumans[0].Commands.Dropped}");
    }

    /// <summary>
    /// Two copies of one player that have come apart on a ladder both let go: the server's when its client's commands say the
    /// client is off it, the client's when its server has had it off for two seconds. Held, the one on the ladder was where
    /// every snapshot pulled the other.
    /// </summary>
    /// <param name="serverLetsGoFirst">Whether the copy that leaves the ladder first is the server's.</param>
    [RequiresTitleDataTheory("igi2")]
    [InlineData(false)]
    [InlineData(true)]
    public void A_ladder_only_one_copy_of_the_joiner_is_on_is_let_go_of_by_the_other(bool serverLetsGoFirst)
    {
        using Igi2CoopPair pair = OnALadder(out _);
        Igi2HumanPlayer own = pair.Joiner!.Human, copy = pair.Host!.NetworkHumans[0].Human;
        (serverLetsGoFirst ? copy : own).LetGoOfClimb();
        Assert.True(
            pair.RunUntil(() => own.ClimbMode == 0 && copy.ClimbMode == 0, 240) >= 0,
            $"still on the ladder: joiner {own.ClimbMode}, server's copy {copy.ClimbMode}");
        Assert.Null(own.AttachParent);
        Assert.Null(copy.AttachParent);
    }

    /// <summary>
    /// A joiner carried by a conveyor belt through a stall is never corrected: every command it ran reaches its server, which
    /// runs them all. Twenty-eight of a second's thirty were lost once, and the joiner was thrown back nine metres.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_joiner_riding_a_conveyor_through_a_stall_is_never_corrected()
    {
        using Igi2CoopPair pair = Igi2CoopPairTests.Joined();
        pair.Ping(Ping);
        pair.RunUntil(() => false, 120);
        Igi2HumanPlayer own = pair.Joiner!.Human, copy = pair.Host!.NetworkHumans[0].Human;
        Put(pair, OnABelt(pair), 0f);
        Assert.True(Carried(own) && Carried(copy), "the joiner does not stand on the belt on both games");
        long before = pair.Client.Session!.Reconciler.Corrections;
        Vector3d from = own.Position;

        pair.RunUntil(() => false, 40);
        StallJoiner(pair);
        pair.RunUntil(() => false, 120);
        Assert.True(Vector3d.Distance(from, own.Position) > 40960, "the belt did not carry the joiner");
        long corrections = pair.Client.Session.Reconciler.Corrections - before;
        Assert.True(corrections == 0, $"{corrections} corrections on the belt; last error {pair.Client.Session.Reconciler.LastError:F0}; dropped {pair.Host.NetworkHumans[0].Commands.Dropped}");
        Assert.Equal(0, pair.Host.NetworkHumans[0].Commands.Dropped);
    }

    /// <summary>
    /// A joiner afloat is not corrected: the water puts it at its height every tick, by the level's clock, and its server ran
    /// the same command a few ticks of swell away. Every third tick its server's word once moved it by the difference.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_joiner_treading_water_is_not_corrected()
    {
        using Igi2CoopPair pair = Igi2CoopPairTests.JoinedOn(WaterMission);
        pair.Ping(Ping);
        Igi2HumanPlayer own = pair.Joiner!.Human;
        Assert.True(pair.RunUntil(() => own.IsSwimming, 300) >= 0, "the mission no longer starts its player in water");
        pair.RunUntil(() => false, 120);
        long before = pair.Client.Session!.Reconciler.Corrections;
        pair.RunUntil(() => false, 300);
        long corrections = pair.Client.Session.Reconciler.Corrections - before;
        Assert.True(own.IsSwimming, "the joiner left the water");
        Assert.True(corrections == 0, $"{corrections} corrections in ten seconds afloat; last error {pair.Client.Session.Reconciler.LastError:F0}");
    }

    // Both games' copies of the joiner on the mission's shortest ladder tall enough to climb, at its foot, the line slow.
    private static Igi2CoopPair OnALadder(out IIgi2Ladder ladder)
    {
        Igi2CoopPair pair = Igi2CoopPairTests.Joined();
        pair.Ping(Ping);
        pair.RunUntil(() => false, 120);
        IIgi2Ladder found = pair.Joiner!.Activatables.OfType<IIgi2Ladder>().Where(static l => l.Steps > 8).OrderBy(static l => l.Steps).First();
        ladder = found;
        IReadOnlyList<float> m = found.Frame;
        Vector3d foot = found.MagicVertex(2);
        Put(pair, new Vector3d(foot.X + (m[1] * 2500.0), foot.Y + (m[4] * 2500.0), foot.Z + 200.0), Igi2Pose.Gamma(m[0], m[3]) + MathF.PI);

        Igi2HumanPlayer own = pair.Joiner.Human, copy = pair.Host!.NetworkHumans[0].Human;
        // The use key, wherever the profile the test runs under has it.
        KeyCode[] use = [KeyCode.E, KeyCode.F, KeyCode.RightShift];
        Array.ForEach(use, key => Igi2CoopPair.Hold(pair.JoinerInput, key, true));
        pair.RunUntil(() => false, 4);
        Array.ForEach(use, key => Igi2CoopPair.Hold(pair.JoinerInput, key, false));
        Assert.True(pair.RunUntil(() => own.ClimbMode == 1 && copy.ClimbMode == 1, 90) >= 0, $"not on the ladder on both: joiner {own.ClimbMode}, server's copy {copy.ClimbMode}");
        return pair;
    }

    // The server's copy alone is put there: the joiner's own player is carried after it by its server's word, as a spawn or a
    // revival carries it.
    private static void Put(Igi2CoopPair pair, Vector3d at, float gamma)
    {
        Igi2HumanPlayer copy = pair.Host!.NetworkHumans[0].Human;
        copy.PopPositionDroppingGround(at);
        copy.PopOrientation(gamma);
        pair.Joiner!.Human.PopOrientation(gamma);
        pair.RunUntil(() => false, 90);
    }

    // The joiner's game stalls while the host's runs on, then runs the ticks it owes in one go.
    private static void StallJoiner(Igi2CoopPair pair)
    {
        for (int frame = 0; frame < Stall; frame++)
        {
            pair.HostGame.Step();
        }

        for (int frame = 0; frame < Stall; frame++)
        {
            pair.JoinerGame.Step();
        }
    }

    // A spot on a running belt's moving faces, found with the host's own player and that put back.
    private static Vector3d OnABelt(Igi2CoopPair pair)
    {
        Igi2ConveyorBeltTask belt = pair.Host!.LevelTasks().OfType<Igi2ConveyorBeltTask>().First(static b => b.XSpeed != 0f || b.YSpeed != 0f);
        Igi2HumanPlayer probe = pair.Host.Human;
        Vector3d home = probe.Position;
        Vector3d? spot = null;
        for (int x = -12; x <= 12 && spot is null; x++)
        {
            for (int y = -12; y <= 12 && spot is null; y++)
            {
                probe.PopPositionDroppingGround(belt.Position + new Vector3d(x * 2048.0, y * 2048.0, 6000));
                probe.SnapToGround();
                if (Carried(probe))
                {
                    spot = probe.Position + new Vector3d(0, 0, 100);
                }
            }
        }

        probe.PopPositionDroppingGround(home);
        probe.SnapToGround();
        Assert.True(spot is not null, "no spot on the belt's moving faces");
        return spot!.Value;
    }

    private static bool Grounded(Igi2HumanPlayer human) => (human.Signal & Igi2PlayerSignal.Grounded) != 0;

    private static bool Carried(Igi2HumanPlayer human) =>
        human.GroundCollider is { } ground
        && human.Surroundings.PushOnConveyor(ground, human.GroundIntersection) is { } push
        && (push.X != 0f || push.Y != 0f);
}
