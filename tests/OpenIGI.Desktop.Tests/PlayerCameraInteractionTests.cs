using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
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
}
