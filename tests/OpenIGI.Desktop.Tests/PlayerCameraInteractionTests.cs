using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1;
using Xunit;

namespace OpenIGI.Desktop.Tests;

public sealed class PlayerCameraInteractionTests
{
    [Theory]
    [InlineData(InteractionActivateType.Door)]
    [InlineData(InteractionActivateType.Cabinet)]
    [InlineData(InteractionActivateType.Terminal)]
    [InlineData(InteractionActivateType.Generic)]
    [InlineData(InteractionActivateType.PlaceExplosive)]
    [InlineData(InteractionActivateType.StationaryGun)]
    public void SelfDrivingInteractionExitPreservesItsAuthoredRoot(InteractionActivateType type)
    {
        PlayerBody body = new()
        {
            Position = new Vector3d(12345.0, -67890.0, 24680.0),
        };

        PlayerCamera.RestoreBodyAfterInteraction(body, type, terrain: null!);

        Assert.Equal(new Vector3d(12345.0, -67890.0, 24680.0), body.Position);
    }

    [Theory]
    [InlineData(InteractionActivateType.StationaryGun)]
    [InlineData(InteractionActivateType.Fence)]
    [InlineData(InteractionActivateType.Wire)]
    public void TraversalAndMountedStatesDoNotRunTheInteractionGroundSnap(InteractionActivateType type)
    {
        PlayerBody body = new()
        {
            Position = new Vector3d(12345.0, -67890.0, 24680.0),
        };

        PlayerCamera.SnapBodyForInteraction(body, type, terrain: null!);

        Assert.Equal(new Vector3d(12345.0, -67890.0, 24680.0), body.Position);
    }

    [Fact]
    public void TerminalGroundSnapCannotPromoteFurnitureAboveTheStandingPlayerToFloor()
    {
        const double floor = 10_000.0;
        const double desktop = floor + 2_800.0;
        PlayerBody body = new()
        {
            Position = new Vector3d(100.0, 200.0, desktop + 900.0),
            Walls = (Vector3d origin, Vector3d _, out WorldRayHit hit) =>
            {
                double height = origin.Z > desktop ? desktop : floor;
                hit = new WorldRayHit(
                    new Vector3d(origin.X, origin.Y, height),
                    new Vector3f(0f, 0f, 1f),
                    origin.Z - height);
                return true;
            },
        };

        PlayerCamera.SnapBodyForInteraction(
            body,
            InteractionActivateType.Terminal,
            terrain: null,
            standingHeight: floor);

        Assert.Equal(floor, body.Position.Z);
        Assert.True(body.IsGrounded);
    }
}
