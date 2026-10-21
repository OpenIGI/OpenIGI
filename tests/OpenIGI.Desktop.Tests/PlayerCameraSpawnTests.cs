using OpenIGI.Desktop;
using OpenIGI.Game.Player;
using Xunit;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Desktop.Tests;

/// <summary>Tests the authored HumanPlayer object-task heading at the camera boundary.</summary>
public sealed class PlayerCameraSpawnTests
{
    [Theory]
    [InlineData(3.09598708152771f)] // Level 1 HumanPlayer_0 Gamma from objects.qvm.
    [InlineData(3.6479880809783936f)] // Level 2.
    [InlineData(-3.455751895904541f)] // Level 3.
    [InlineData(2.9321529865264893f)] // Level 4.
    [InlineData(2.5132739543914795f)] // Level 5.
    [InlineData(1.4660769701004028f)] // Level 6.
    [InlineData(4.2895960807800293f)] // Level 7.
    [InlineData(3.6651909351348877f)] // Level 8.
    [InlineData(4.2935090065002441f)] // Level 9.
    [InlineData(0f)] // Level 10.
    [InlineData(1.3071240186691284f)] // Level 11.
    [InlineData(-0.31415900588035583f)] // Level 12.
    [InlineData(3.979351043701172f)] // Level 13.
    [InlineData(-1.3799949884414673f)] // Level 14.
    public void PlaceAtUsesTheRetailCameraHeadingForAnAuthoredHumanPlayerGamma(float gamma)
    {
        PlayerCamera camera = new();
        PlayerSpawnPoint spawn = new(default, gamma, "000_01_1", Team: 0);

        camera.PlaceAt(terrain: null, spawn);

        // Object task Gamma is the human's engine-facing angle. The player camera's yaw uses the
        // opposite world-to-view convention, as do stationary-gun and fence interaction hand-offs.
        Assert.Equal(-gamma, camera.Yaw, precision: 6);
    }
}
