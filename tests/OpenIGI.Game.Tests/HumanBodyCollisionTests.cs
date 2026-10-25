using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Tests that <see cref="ObjectCollisionIndex.SetHumanBodies"/> folds living soldier bodies into the
/// player cast, so the existing human wall sweep treats a soldier as solid.
/// </summary>
public sealed class HumanBodyCollisionTests
{
    private static readonly double M = WorldScene.UnitsPerMetre;

    // An index with no placed objects, so only the human-body path is exercised.
    private static ObjectCollisionIndex EmptyIndex() => ObjectCollisionIndex.EmptyForTesting();

    [Fact]
    public void LivingSoldierBlocksPlayerRay()
    {
        ObjectCollisionIndex index = EmptyIndex();
        // A soldier standing at the origin, foot at ground level.
        Vector3d soldierFoot = new(0.0, 0.0, 0.0);
        index.SetHumanBodies(new[]
        {
            new HumanBody(soldierFoot, new Vector3d(0.0, 0.0, 1.8 * M), 0.55 * M),
        });

        // The player's chest-height ray walking straight toward the soldier must be stopped.
        Vector3d origin = new(3.0 * M, 0.0, 0.9 * M);
        Vector3d end = new(-1.0 * M, 0.0, 0.9 * M);
        bool hit = index.TryCastForPlayer(origin, end, out WorldRayHit result);

        Assert.True(hit);
        Assert.Equal(0.0, result.Normal.Z, 2); // horizontal surface => wall
        Assert.True(result.Distance > 0.0);
        Assert.InRange(result.Position.X, 0.5 * M, 0.6 * M); // stopped at the capsule's near face
    }

    [Fact]
    public void RemovingBodiesRestoresClearPath()
    {
        ObjectCollisionIndex index = EmptyIndex();
        index.SetHumanBodies(new[]
        {
            new HumanBody(Vector3d.Zero, new Vector3d(0.0, 0.0, 1.8 * M), 0.55 * M),
        });

        // The same ray hits while a body is present...
        Vector3d origin = new(3.0 * M, 0.0, 0.9 * M);
        Vector3d end = new(-1.0 * M, 0.0, 0.9 * M);
        Assert.True(index.TryCastForPlayer(origin, end, out _));

        // ...and is clear once the list is emptied (the soldier died or its container went dormant).
        index.SetHumanBodies([]);
        Assert.False(index.TryCastForPlayer(origin, end, out _));
    }
}
