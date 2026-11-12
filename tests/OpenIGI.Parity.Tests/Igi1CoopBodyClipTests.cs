using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Multiplayer;
using OpenIGI.Game.World;
using OpenIGI.Host;
using OpenIGI.Input;
using OpenIGI.Presentation.Igi1;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>
/// Co-op: a player's whole-body clips -- a computer, a cabinet, a ladder's top -- are drawn on the other game as the player's
/// own game draws them: the same clip, at the same height, no gun in its hand.
/// </summary>
[Collection(ParityGroup.Name)]
public sealed class Igi1CoopBodyClipTests
{
    // Further apart than this, the other game draws the head somewhere the player's own never had it.
    private const double MostApartMetres = 0.3;

    [RequiresGameDataTheory]
    [InlineData(true, InteractionActivateType.Terminal)]
    [InlineData(false, InteractionActivateType.Terminal)]
    [InlineData(true, InteractionActivateType.Fence)]
    [InlineData(false, InteractionActivateType.Fence)]
    public void A_player_working_a_task_is_drawn_on_the_other_game_as_its_own_draws_it(bool hostActs, InteractionActivateType type)
    {
        using Igi1CoopPair pair = Joined();
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        InputState input = hostActs ? pair.HostInput : pair.JoinerInput;
        Game.Igi1.Player.PlayerCamera actor = hostActs ? pair.Host.World.Player! : pair.Joiner!.World.Player!;

        // At a task of the kind, facing it, on both games, then the use key: the highest first, which in mission 1 are the
        // office computers on the floor one stands on, not the bunker's under the ground.
        List<string> tried = [];
        WorldInteraction? task = null;
        IEnumerable<WorldInteraction> candidates = pair.Host.World.Interactions!.Items
            .Where(i => i.Type == type && i.IsInWorld && i.IsAvailable && !i.IsCompleted)
            .OrderByDescending(i => i.BodyPose(i.Anchor).Position.Z);
        foreach (WorldInteraction candidate in candidates)
        {
            (Vector3d at, _) = candidate.BodyPose(candidate.Anchor);
            Rigs(pair, partner, hostActs, rig => rig.Body.Position = at + new Vector3d(0, 0, 512));
            pair.RunUntil(() => false, 15);
            Rigs(pair, partner, hostActs, rig =>
            {
                Vector3d toward = candidate.CandidatePoint(rig.Body.Eye) - rig.Body.Eye;
                rig.Body.Yaw = (float)Math.Atan2(toward.X, toward.Y);
            });

            Igi1CoopPair.Hold(input, KeyCode.E, true);
            if (pair.RunUntil(() => actor.PlaysInteractionClip, 20) >= 0)
            {
                task = candidate;
                break;
            }

            Igi1CoopPair.Hold(input, KeyCode.E, false);
            pair.RunUntil(() => false, 2);
            tried.Add($"{candidate.Task.Name}: pose {at}, feet {actor.Body.Position}, offered {actor.ActionSprite}");
        }

        Assert.True(task is not null, $"the {(hostActs ? "host" : "joiner")} began at no {type}: {string.Join(" | ", tried)}");
        DrawnAsOwn(pair, partner, hostActs, 75, $"{task.Task.Name} by the {(hostActs ? "host" : "joiner")}", () => actor.PlaysInteractionClip);
        Igi1CoopPair.Hold(input, KeyCode.E, false);
    }

    [RequiresGameDataTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_player_getting_on_a_ladder_at_its_top_is_drawn_on_the_other_game_as_its_own_draws_it(bool hostActs)
    {
        using Igi1CoopPair pair = Joined();
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        Game.Igi1.Player.PlayerCamera own = pair.Joiner!.World.Player!;
        object controller = typeof(Game.Igi1.Player.PlayerCamera).GetField("_ladder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(own)!;
        var ladders = (IReadOnlyList<LadderPlacement>)controller.GetType().GetField("_ladders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(controller)!;
        LadderPlacement ladder = ladders.OrderBy(l => l.Top.Z - l.Bottom.Z).Last();

        // On the roof behind the ladder's head, facing it.
        Vector3f normal = ladder.Orientation.Column2;
        Vector3d back = new Vector3d(-normal.X, -normal.Y, 0);
        back /= back.Length;
        Rigs(pair, partner, hostActs, rig =>
        {
            rig.Body.Position = ladder.ActivationTop + (back * 3000.0) + new Vector3d(0, 0, 2048);
            rig.Body.Yaw = (float)Math.Atan2(-back.X, -back.Y);
        });
        pair.RunUntil(() => false, 30);

        InputState input = hostActs ? pair.HostInput : pair.JoinerInput;
        Game.Igi1.Player.PlayerCamera actor = hostActs ? pair.Host.World.Player! : own;
        Igi1CoopPair.Hold(input, KeyCode.E, true);
        bool mounted = pair.RunUntil(() => actor.IsOnLadder, 20) >= 0;
        Igi1CoopPair.Hold(input, KeyCode.E, false);
        Assert.True(mounted, $"the {(hostActs ? "host" : "joiner")} never got on the ladder");

        // The top clip, then a few rungs down.
        Igi1CoopPair.Hold(input, KeyCode.S, true);
        DrawnAsOwn(pair, partner, hostActs, 120, $"ladder top by the {(hostActs ? "host" : "joiner")}", () => actor.IsOnLadder);
        Igi1CoopPair.Hold(input, KeyCode.S, false);
    }

    // Runs the pair, each frame setting where the other game draws the actor's head against where the actor's own game drew
    // it over the last two seconds: the other game draws it a little late, never somewhere it never was.
    private static void DrawnAsOwn(Igi1CoopPair pair, Igi1NetworkPlayer partner, bool hostActs, int frames, string what, Func<bool> busy)
    {
        GameHost actorGame = hostActs ? pair.HostGame : pair.JoinerGame;
        GameHost viewerGame = hostActs ? pair.JoinerGame : pair.HostGame;
        object key = hostActs ? Igi1ReplicatedWorld.HostEntity : partner;
        SoldierRenderer actorBodies = Assert.IsType<SoldierRenderer>(actorGame.SoldiersForTesting);
        SoldierRenderer viewerBodies = Assert.IsType<SoldierRenderer>(viewerGame.SoldiersForTesting);

        List<Vector3d> ownHeads = [];
        double worst = 0;
        string worstAt = string.Empty;
        int compared = 0;
        int busyFrames = 0;
        bool heldGun = false;
        for (int frame = 0; frame < frames && busy(); frame++)
        {
            Assert.True(pair.Step());
            busyFrames++;
            if (actorBodies.PlayerBodyHeadForTesting is { } ownHead)
            {
                ownHeads.Add(ownHead);
            }

            // The other game's copy needs its snapshots: judge it once the actor has been at it a while.
            if (frame < 20 || viewerBodies.PartnerHeadForTesting(key) is not { } drawn || ownHeads.Count == 0)
            {
                continue;
            }

            heldGun |= viewerBodies.PartnerHoldsGunForTesting(key);
            double nearest = ownHeads.Skip(Math.Max(0, ownHeads.Count - 60)).Min(h => Vector3d.Distance(h, drawn));
            compared++;
            if (nearest > worst)
            {
                worst = nearest;
                worstAt = $"frame {frame}: drawn {drawn}, own {ownHeads[^1]}";
            }
        }

        Assert.True(busyFrames >= 40, $"{what}: the actor was at it only {busyFrames} frames");
        Assert.True(compared > 10, $"{what}: the other game drew the actor on only {compared} frames");
        Assert.True(worst < MostApartMetres, $"{what}: the other game drew the head {worst:0.00} m from anywhere the actor's own had it ({worstAt})");
        Assert.False(heldGun, $"{what}: the other game drew a gun in the actor's hand");
    }

    // Sets the actor's rig on both games: its own, and the copy the host runs of a joiner.
    private static void Rigs(Igi1CoopPair pair, Igi1NetworkPlayer partner, bool hostActs, Action<Game.Igi1.Player.PlayerCamera> set)
    {
        if (hostActs)
        {
            set(pair.Host!.World.Player!);
            return;
        }

        set(pair.Joiner!.World.Player!);
        set(partner.Player);
    }

    private static Igi1CoopPair Joined()
    {
        Igi1CoopPair pair = new(mission: 1);
        Assert.True(pair.RunUntil(() => pair.Joined && pair.Host!.World.NetworkPlayers.Count == 1, 3000) >= 0, "the joiner was never welcomed");
        return pair;
    }
}
