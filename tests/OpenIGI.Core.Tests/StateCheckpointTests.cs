using OpenIGI.Core.Simulation;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>
/// A <see cref="StateCheckpoint"/> puts a graph of objects back exactly, into the same objects: fields (readonly ones too), the
/// BCL's collections grown or shrunk since, structs holding references, closures, cycles; what is outside the state is left
/// alone, and an array that did not change is shared with the checkpoint before.
/// </summary>
public sealed class StateCheckpointTests
{
    private static readonly StateBoundary Boundary = new(static t => t.Namespace == typeof(StateCheckpointTests).Namespace && t != typeof(Service));

    [Fact]
    public void Fields_collections_and_structs_come_back_into_the_same_objects()
    {
        var child = new Body { Health = 10, Position = new Spot(1, 2) };
        var world = new World(new Service { Calls = 1 });
        world.Bodies.Add(child);
        world.ByName["a"] = child;
        world.Seen.Add(3);
        world.Pairs = [new Link(child, 1.5f)];
        world.Self = world;
        int counter = 0;
        world.Tick = () => counter++;

        StateCheckpoint checkpoint = StateCheckpoint.Capture([world], Boundary);

        child.Health = 0;
        child.Position = new Spot(9, 9);
        world.Bodies.Add(new Body());
        world.Bodies.Add(new Body());
        world.ByName.Clear();
        world.Seen.Remove(3);
        world.Seen.Add(4);
        world.Pairs[0] = new Link(null, 0f);
        world.Counter.Value = 99;
        world.Service.Calls = 5;
        world.Tick = null;

        checkpoint.Restore();

        Assert.Equal(10, child.Health);
        Assert.Equal(new Spot(1, 2), child.Position);
        Assert.Same(child, Assert.Single(world.Bodies));
        Assert.Same(child, world.ByName["a"]);
        Assert.Equal([3], world.Seen);
        Assert.Same(child, world.Pairs[0].Body);
        Assert.Equal(1.5f, world.Pairs[0].Weight);
        Assert.Equal(7, world.Counter.Value);
        Assert.Same(world, world.Self);
        Assert.NotNull(world.Tick);

        // Outside the state: the service is the same object, as it was left.
        Assert.Equal(5, world.Service.Calls);

        // A checkpoint is never written into: restored again after more changes, it gives the same.
        child.Health = 3;
        world.Bodies.Clear();
        checkpoint.Restore();
        Assert.Equal(10, child.Health);
        Assert.Single(world.Bodies);
    }

    [Fact]
    public void A_closure_over_state_is_recorded_with_it()
    {
        var world = new World(new Service());
        var body = new Body { Health = 1 };
        world.Tick = () => body.Health++;
        StateCheckpoint checkpoint = StateCheckpoint.Capture([world], Boundary);
        world.Tick();
        world.Tick();
        checkpoint.Restore();
        Assert.Equal(1, body.Health);
    }

    [Fact]
    public void An_unchanged_array_is_shared_with_the_checkpoint_before()
    {
        var world = new World(new Service()) { Data = new int[100_000] };
        StateCheckpoint first = StateCheckpoint.Capture([world], Boundary);
        StateCheckpoint second = StateCheckpoint.Capture([world], Boundary, first);
        Assert.True(second.OwnBytes < 1000, $"{second.OwnBytes} bytes of its own");

        world.Data[5] = 1;
        StateCheckpoint third = StateCheckpoint.Capture([world], Boundary, second);
        Assert.True(third.OwnBytes >= 400_000);
        first.Restore();
        Assert.Equal(0, world.Data[5]);
    }

    [Fact]
    public void Two_runs_compare_by_where_their_objects_stand()
    {
        static World Run(int health)
        {
            var world = new World(new Service());
            world.Bodies.Add(new Body { Health = 4 });
            world.Bodies.Add(new Body { Health = health, Position = new Spot(1, 2) });
            return world;
        }

        StateCheckpoint a = StateCheckpoint.Capture([Run(5)], Boundary);
        Assert.Empty(a.Differences(StateCheckpoint.Capture([Run(5)], Boundary)));
        string difference = Assert.Single(a.Differences(StateCheckpoint.Capture([Run(6)], Boundary)));
        Assert.Contains("Health", difference, StringComparison.Ordinal);
        Assert.Contains("[1]", difference, StringComparison.Ordinal);
    }

    private sealed class Service
    {
        public int Calls { get; set; }
    }

    private readonly record struct Spot(float X, float Y);

    private readonly record struct Link(Body? Body, float Weight);

    private sealed class Box
    {
        public int Value { get; set; } = 7;
    }

    private sealed class Body
    {
        public int Health { get; set; }

        public Spot Position { get; set; }
    }

    private sealed class World(Service service)
    {
        public Service Service { get; } = service;

        public List<Body> Bodies { get; } = [];

        public Dictionary<string, Body> ByName { get; } = [];

        public HashSet<int> Seen { get; } = [];

        public Link[] Pairs { get; set; } = [];

        public Box Counter { get; } = new();

        public World? Self { get; set; }

        public Action? Tick { get; set; }

        public int[] Data { get; set; } = [];
    }
}
