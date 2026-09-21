using OpenIGI.Core.Diagnostics;
using OpenIGI.Presentation.Igi1;
using Xunit;

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
        PlayerCamera camera = new(new InformationLogger());

        Assert.True(camera.WallBlockDiagnosticsEnabled);
    }

    private sealed class InformationLogger : IIgiLogger
    {
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;

        public void Log(LogLevel level, string category, string message, Exception? exception = null)
        {
        }
    }
}
