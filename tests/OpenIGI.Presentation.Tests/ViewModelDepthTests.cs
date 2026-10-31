using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Rendering.Headless;

namespace OpenIGI.Presentation.Tests;

public sealed class ViewModelDepthTests
{
    [Fact]
    public void BeginClearsForegroundDepthAndKeepsViewModelSelfOcclusion()
    {
        var device = new RecordingGraphicsDevice();
        device.SetDepthState(DepthState.TestOnly);

        ViewModelDepth.Begin(device);

        Assert.Equal(1, device.DepthClearCount);
        Assert.Equal(DepthState.TestAndWrite, device.DepthState);
    }
}
