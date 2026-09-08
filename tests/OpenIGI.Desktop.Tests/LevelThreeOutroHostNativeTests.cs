using OpenIGI.Desktop;
using OpenIGI.Scripting.Runtime;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>Desktop ownership tests for the retail <c>Game_CutsceneDelete</c> native.</summary>
public sealed class LevelThreeOutroHostNativeTests
{
    [Fact]
    public void CutsceneDeleteInvokesTransientCleanupExactlyOnce()
    {
        int clears = 0;

        QvmValue? handled = DesktopGame.TryHandleCutsceneCleanupNative(
            "Game_CutsceneDelete",
            () => clears++);
        QvmValue? ignored = DesktopGame.TryHandleCutsceneCleanupNative(
            "Game_EnableMusic",
            () => clears++);

        Assert.True(handled.HasValue);
        Assert.Equal(1, handled.Value.Integer);
        Assert.Null(ignored);
        Assert.Equal(1, clears);
    }
}
