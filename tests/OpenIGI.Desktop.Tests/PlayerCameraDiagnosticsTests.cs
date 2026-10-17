using OpenIGI.Core.Diagnostics;
using Xunit;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Desktop.Tests;

public sealed class PlayerCameraDiagnosticsTests
{
    [Fact]
    public void WallBlockProbesAreSkippedWhenLoggingIsDisabled()
    {
        PlayerCamera camera = new(NullLogger.Instance);

        Assert.False(camera.WallBlockDiagnosticsEnabled);
    }

    [Fact]
    public void WallBlockProbesRunOnlyForVerboseInformationLogging()
    {
        PlayerCamera camera = new(new MemorySink(minimumLevel: LogLevel.Information));

        Assert.True(camera.WallBlockDiagnosticsEnabled);
    }

}
