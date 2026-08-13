using OpenIGI.Desktop;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Desktop.Tests;

public sealed class PlayerCameraHealthTests
{
    [Fact]
    public void CameraUsesTheRuntimeHealthObjectForItsBody()
    {
        PlayerCamera camera = new();
        HumanHealth runtimeHealth = new(HumanHitZoneTable.Player, maximumHealth: 3f);

        camera.UseHealth(runtimeHealth);

        Assert.Same(runtimeHealth, camera.Body.Health);
    }
}
