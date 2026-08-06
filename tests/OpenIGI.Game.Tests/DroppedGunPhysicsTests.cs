using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class DroppedGunPhysicsTests
{
    [Fact]
    public void MeshColliderUsesAtMostTwentySixSourceSupportVerticesForLiveQueries()
    {
        DroppedGunCollider collider = new(Enumerable.Range(0, 10_000)
            .Select(index => new Vector3f(index, index % 37, index % 23)));

        Assert.Equal(10_000, collider.VertexCount);
        Assert.InRange(collider.ContactVertexCount, 1, 26);
    }

    [Fact]
    public void DroppedGunSettlesOnTheGroundInsteadOfFallingThroughIt()
    {
        DroppedGunState state = new(
            new Vector3d(0.0, 0.0, 1_000.0),
            Matrix3f.Identity,
            new Vector3d(0.0, 0.0, -100.0));

        for (int tick = 0; tick < 200 && !state.IsResting; tick++)
        {
            state = DroppedGunPhysics.Advance(state, RagdollWorld.Plane(0.0));
        }

        Assert.True(state.IsResting);
        Assert.Equal(DroppedGunPhysics.CollisionRadius, state.Position.Z, precision: 5);
        Assert.Equal(Vector3d.Zero, state.Velocity);
    }

    [Fact]
    public void DroppedGunSweepKeepsItOnTheNearSideOfAWall()
    {
        DroppedGunState state = new(
            new Vector3d(0.0, -500.0, 4_000.0),
            Matrix3f.Identity,
            new Vector3d(0.0, 1_000.0, 0.0));

        state = DroppedGunPhysics.Advance(state, RagdollWorld.Room(0.0, 0.0));

        Assert.True(
            state.Position.Y <= -DroppedGunPhysics.CollisionRadius,
            $"Dropped gun passed through wall to Y={state.Position.Y:F1}.");
        Assert.True(state.Velocity.Y < 0.0);
    }

    [Fact]
    public void RenderedMeshVerticesRestExactlyOnTerrain()
    {
        DroppedGunCollider collider = new([
            new Vector3f(-1_800f, -120f, -260f),
            new Vector3f(1_800f, 120f, 180f),
        ]);
        DroppedGunState state = new(
            new Vector3d(0.0, 0.0, 100.0),
            Matrix3f.Identity,
            default);

        state = DroppedGunPhysics.Advance(state, RagdollWorld.Plane(0.0), collider);

        Assert.Equal(260.0, state.Position.Z, precision: 5);
    }

    [Fact]
    public void LeadingMeshVertexDoesNotPassThroughAWall()
    {
        DroppedGunCollider collider = new([
            new Vector3f(-100f, -1_800f, -100f),
            new Vector3f(100f, 1_800f, 100f),
        ]);
        DroppedGunState state = new(
            new Vector3d(0.0, -1_850.0, 4_000.0),
            Matrix3f.Identity,
            new Vector3d(0.0, 100.0, 0.0));

        state = DroppedGunPhysics.Advance(state, RagdollWorld.Room(0.0, 0.0), collider);

        Assert.Equal(-1_800.0, state.Position.Y, precision: 5);
    }

    [Fact]
    public void MeshBodyPlacesItsLowestRenderedVertexOnTerrain()
    {
        DroppedGunCollider collider = new([
            new Vector3f(-1_800f, -120f, -260f),
            new Vector3f(1_800f, 120f, 180f),
        ]);
        DroppedGunMeshBody body = collider.CreateRigidBody(
            new Vector3d(0.0, 0.0, 1_000.0),
            Matrix3f.Identity,
            default,
            default);

        for (int tick = 0; tick < 160 && !body.IsAtRest; tick++)
        {
            body.Step(RagdollWorld.Plane(0.0), substeps: 4);
        }

        Assert.True(body.IsAtRest);
        Assert.InRange(body.Position.Z + collider.LowestZ(body.Orientation), -0.01, 0.01);
    }

}
