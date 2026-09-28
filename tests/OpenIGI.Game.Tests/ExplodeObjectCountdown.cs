using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Runs an ExplodeObject's countdown the way the host's tick does.</summary>
/// <remarks>
/// A hit only arms the countdown (<c>0x481E60</c>/<c>0x481EA0</c>); the object is destroyed on the update
/// that finds it at 0 (<c>0x481970</c> into <c>0x481A60</c>) and its own blast is ready a tick later.
/// </remarks>
internal static class ExplodeObjectCountdown
{
    /// <summary>Ticks the registry until the object has exploded, and returns how many ticks it took.</summary>
    public static int RunUntilExploded(DestructibleObjectRegistry registry, DestructibleObject item, int limit = 600)
    {
        for (int tick = 1; tick <= limit; tick++)
        {
            registry.Tick();
            _ = registry.ConsumeReadyExplosions();
            if (item.IsDestroyed)
            {
                return tick;
            }
        }

        Assert.Fail($"{item.Owner.Name} did not explode within {limit} ticks.");
        return -1;
    }
}
