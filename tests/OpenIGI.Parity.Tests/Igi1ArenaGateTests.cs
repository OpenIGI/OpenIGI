using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>A versus match's switch-worked gates: worked both ways, and from both sides.</summary>
public sealed class Igi1ArenaGateTests(Xunit.Abstractions.ITestOutputHelper output)
{
    // Mission 1's gate by the yard (doors 402 and 403), opened by switch 500 on its east side only.
    private const int Leaf = 402;
    private const int OtherLeaf = 403;
    private const int EastSwitch = 500;

    [RequiresGameDataFact]
    public void A_gate_switched_from_one_side_gets_a_switch_on_the_other_and_each_opens_and_shuts_it_on_every_end()
    {
        using Igi1DedicatedTrio trio = new(GameModes.TeamDeathmatch, 1, 0.05, 0.05);
        Assert.True(trio.RunUntil(() => trio.AllIn, 3000) >= 0, "the players never both came in");
        trio.RunUntil(() => false, 30);

        WorldInteractionRegistry server = trio.Server!.World.Interactions!;
        WorldInteractionRegistry[] ends = [server, .. trio.Players.Select(p => p.Level!.World.Interactions!)];

        // Every end made the same switch, in the same place in its list: what keeps a press in step.
        int[] counts = [.. ends.Select(e => e.Items.Count)];
        Assert.All(counts, c => Assert.Equal(counts[0], c));
        WorldInteraction east = server.Items.Single(i => i.Task.Id == EastSwitch);
        WorldInteraction west = server.Items.Last(i => i.Type == InteractionActivateType.Switch);
        Assert.NotEqual(EastSwitch, west.Task.Id);
        WorldInteraction leaf = server.Items.Single(i => i.Task.Id == Leaf);
        double gateX = leaf.TaskPosition.X;
        output.WriteLine($"east {east.Task.Id} at {east.Anchor} faces ({east.Orientation.M12:0.00},{east.Orientation.M22:0.00}); "
            + $"west {west.Task.Id} at {west.Anchor} faces ({west.Orientation.M12:0.00},{west.Orientation.M22:0.00}); gate x {gateX}");
        Assert.True(east.Anchor.X > gateX && west.Anchor.X < gateX, "the new switch is not across the gate from the old");
        Assert.InRange(Math.Abs((east.Anchor.X - gateX) + (west.Anchor.X - gateX)), 0, 0.2 * WorldUnits.PerMetre);
        Assert.InRange(Math.Abs(east.Anchor.Y - west.Anchor.Y), 0, 0.2 * WorldUnits.PerMetre);
        Assert.True(east.Orientation.M12 > 0.9f && west.Orientation.M12 < -0.9f, "the new switch does not face away from the gate on its side");

        // Its post came with it, mirrored as the switch is: the level's stand the east switch is mounted on.
        Engine.Tasks.DeclaredTask eastPost = Assert.IsType<Engine.Tasks.DeclaredTask>(east.Task.Parent);
        Engine.Tasks.DeclaredTask westPost = Assert.IsType<Engine.Tasks.DeclaredTask>(west.Task.Parent);
        Assert.NotSame(eastPost, westPost);
        Assert.True(eastPost.TryGet("Position", out Vector3d eastPostAt));
        Assert.True(westPost.TryGet("Position", out Vector3d westPostAt));
        Assert.InRange(Math.Abs((eastPostAt.X - gateX) + (westPostAt.X - gateX)), 0, 0.2 * WorldUnits.PerMetre);

        // A player on the west side, a step from the new switch and looking at it, is offered it.
        Vector3d eye = west.Anchor + new Vector3d(-0.8 * WorldUnits.PerMetre, 0, 0);
        float lookEast = MathF.Atan2(1f, 0f);
        Assert.Same(west, server.Resolve(eye, lookEast));

        WorldInteraction[] leaves = [.. ends.Select(e => e.Items.Single(i => i.Task.Id == Leaf))];
        WorldInteraction[] others = [.. ends.Select(e => e.Items.Single(i => i.Task.Id == OtherLeaf))];
        bool AllOpen() => leaves.Concat(others).All(l => l.Door!.IsFullyOpen);
        bool AllShut() => leaves.Concat(others).All(l => l.Door!.IsFullyClosed);
        Assert.True(AllShut(), "the gate did not start shut");

        // The west switch opens it, a second press of it shuts it, and the east one opens it again.
        foreach ((WorldInteraction pressed, Func<bool> then, string what) in new (WorldInteraction, Func<bool>, string)[]
        {
            (west, AllOpen, "open to the new switch"),
            (west, AllShut, "shut to a second press"),
            (east, AllOpen, "open to the old switch"),
            (east, AllShut, "shut to the old switch"),
        })
        {
            pressed.Press();
            int ticks = trio.RunUntil(then, 30 * 10);
            output.WriteLine($"{what}: {ticks} ticks");
            Assert.True(ticks >= 0, $"the gate did not {what} on every end");
            trio.RunUntil(() => false, 15);
        }
    }

    [RequiresGameDataFact]
    public void A_gate_already_switched_from_both_sides_gets_no_new_switch_but_shuts_to_them()
    {
        using Igi1DedicatedTrio trio = new(GameModes.TeamDeathmatch, 1, 0.05, 0.05);
        Assert.True(trio.RunUntil(() => trio.AllIn, 3000) >= 0, "the players never both came in");
        trio.RunUntil(() => false, 30);

        // Doors 408 and 409, switches 505 east and 506 west.
        WorldInteractionRegistry server = trio.Server!.World.Interactions!;
        Assert.Equal(1, server.Items.Count(i => i.Type == InteractionActivateType.Switch && i.Task.Id > 4000));
        WorldInteraction[] leaves = [.. server.Items.Where(i => i.Task.Id is 408 or 409)];
        WorldInteraction west = server.Items.Single(i => i.Task.Id == 506);
        west.Press();
        Assert.True(trio.RunUntil(() => leaves.All(l => l.Door!.IsFullyOpen), 300) >= 0, "the gate did not open");
        trio.RunUntil(() => false, 15);
        west.Press();
        Assert.True(trio.RunUntil(() => leaves.All(l => l.Door!.IsFullyClosed), 300) >= 0, "the gate did not shut");
    }
}
