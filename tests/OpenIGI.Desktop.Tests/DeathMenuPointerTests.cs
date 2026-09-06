using Xunit;

namespace OpenIGI.Desktop.Tests;

public sealed class DeathMenuPointerTests
{
    [Fact]
    public void TheDeathOrbitTakesThePointerWhileTheBodyIsStillBeingWatched()
    {
        // The ordinary death shot: the orbit turns the camera around the corpse, so it holds the lock
        // and the mouse keeps turning the view however far it is moved.
        Assert.True(DesktopGame.DeathCameraOwnsPointer(
            playerDead: true,
            deathCameraActive: true,
            missionFailedMenu: false));
    }

    [Fact]
    public void TheFailureScreenTakesThePointerBackFromTheDeathOrbit()
    {
        // The retail failure screen arrives 151 ticks after the level reports its failure, while the
        // death camera is still orbiting. Both ran in the same frame and wrote opposite lock states:
        // the screen released the cursor, the orbit re-took it, and the pointer flashed between the two
        // without ever moving. The screen wins.
        Assert.False(DesktopGame.DeathCameraOwnsPointer(
            playerDead: true,
            deathCameraActive: true,
            missionFailedMenu: true));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void NothingElseHandsTheOrbitThePointer(bool playerDead, bool deathCameraActive)
    {
        // A living player, or a dead one whose camera has finished its pull-back, leaves the pointer to
        // whichever branch below owns it.
        Assert.False(DesktopGame.DeathCameraOwnsPointer(
            playerDead,
            deathCameraActive,
            missionFailedMenu: false));
    }
}
