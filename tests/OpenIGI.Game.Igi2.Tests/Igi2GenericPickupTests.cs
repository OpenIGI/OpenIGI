using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Pickups;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Generic pickups, as genericpickup.c offers and collects them.</summary>
public sealed class Igi2GenericPickupTests
{
    /// <summary>
    /// With no model the reach is a metre: a view within it across the ground, and no more than a metre and a half plus the reach
    /// below the pickup nor a metre plus the reach above, picks it up; the next tick it sounds and leaves the world.
    /// </summary>
    [Fact]
    public void A_pickup_is_collected_within_reach()
    {
        List<string> sounds = [];
        Igi2GenericPickupTask pickup = new() { Position = new Vector3d(0, 0, 0), PlaySound = sounds.Add };
        Assert.Equal(4096.0, pickup.Reach);

        pickup.Offer(new Vector3d(4096.0, 0, 0));
        Assert.False(pickup.IsPickedUp);
        pickup.Offer(new Vector3d(0, 0, 4096.0 * 3));
        Assert.False(pickup.IsPickedUp);

        pickup.Offer(new Vector3d(2048.0, 0, 4096.0 * 2));
        Assert.True(pickup.IsPickedUp);
        Assert.True(pickup.IsInWorld);

        pickup.Run(Level());
        Assert.False(pickup.IsInWorld);
        Assert.True(pickup.IsDestroyed);
        Assert.Equal([Igi2GenericPickupTask.PickupSound], sounds);
        pickup.Run(Level());
        Assert.Single(sounds);
    }

    private static Flow.Igi2LevelRun Level()
    {
        Engine.Tasks.TaskTypeRegistry types = new();
        Engine.Tasks.TaskTree tree = new(types);
        Engine.Tasks.GameTask root = tree.New(null, types.Register<Engine.Tasks.ContainerTask>("Container"));
        return new(root, new OpenIGI.Scripting.Runtime.NativeRegistry(), new Scripting.Igi2ScriptFunctions());
    }
}
