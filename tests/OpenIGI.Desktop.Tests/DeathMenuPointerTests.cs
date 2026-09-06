using Xunit;

namespace OpenIGI.Desktop.Tests;

public sealed class DeathMenuPointerTests
{
    [Fact]
    public void TheFailureScreenOwnsThePointerOnItsOwn()
    {
        // The regression this exists for. The mission-failed screen arrives 151 ticks after the level
        // reports failure, which is while the player is still dead and the death camera is orbiting. It
        // releases the cursor every frame, and every camera branch below it in the frame's cursor chain
        // takes the cursor back -- the death orbit, and then, because _playerMode outlives the player,
        // the look, whose Look() sets IsCapturingMouse and locks. Whichever ran last won, so the pointer
        // flashed between locked and free and never moved.
        //
        // Standing a single branch down is not enough: the chain simply falls through to the next one
        // that locks. The screen has to stop the chain here, the way the pause menu does.
        Assert.True(DesktopGame.MenuOwnsPointer(pauseMenu: false, missionFailedMenu: true));
    }

    [Fact]
    public void ThePauseMenuStillOwnsThePointer()
    {
        Assert.True(DesktopGame.MenuOwnsPointer(pauseMenu: true, missionFailedMenu: false));
    }

    [Fact]
    public void BothAtOnceIsStillTheMenu()
    {
        Assert.True(DesktopGame.MenuOwnsPointer(pauseMenu: true, missionFailedMenu: true));
    }

    [Fact]
    public void OrdinaryPlayLeavesThePointerToTheCameras()
    {
        // With no menu up the chain must fall through, so the look and the death orbit keep the lock
        // that makes the view keep turning however far the mouse is moved.
        Assert.False(DesktopGame.MenuOwnsPointer(pauseMenu: false, missionFailedMenu: false));
    }
}
