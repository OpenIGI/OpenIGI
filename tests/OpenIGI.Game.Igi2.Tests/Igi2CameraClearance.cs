using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Player;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The check the orbiting cameras are held to: never against or inside the level.</summary>
internal static class Igi2CameraClearance
{
    // The camera stands in the open: no line of a quarter metre from it, in any of the six axis directions, strikes anything but
    // the player's own body, and the ground is below it.
    public static void AssertClear(Collision.Igi2CollisionWorld world, Igi2HumanPlayer human, Vector3d eye)
    {
        OpenIGI.Game.Physics.WorldLineTrace near = new(6);
        Vector3d[] axes = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];
        for (int i = 0; i < axes.Length; i++)
        {
            near.Starts[i] = eye;
            near.Ends[i] = eye + (axes[i] * 512.0);
        }

        world.TraceLines(near, uint.MaxValue);
        for (int i = 0; i < axes.Length; i++)
        {
            OpenIGI.Game.Physics.WorldIntersection hit = near.Intersections[i];
            Assert.False(hit.IsHit && !ReferenceEquals(hit.HitObject?.Owner, human), $"the camera at {eye} is against something along {axes[i]}.");
        }

        OpenIGI.Game.Physics.WorldLineTrace up = new(1);
        up.Starts[0] = eye;
        up.Ends[0] = eye + new Vector3d(0, 0, 409600.0);
        world.TraceLines(up, Collision.Igi2CollisionWorld.TerrainGroup);
        Assert.False(up.Intersections[0].IsHit, $"the camera at {eye} is under the ground.");
    }
}
