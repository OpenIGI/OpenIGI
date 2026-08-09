using OpenIGI.Engine.Vfs;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

public sealed class CameraStatusLightRendererTests
{
    [RequiresGameDataFact]
    public void RetailAlarmLightSpriteLoadsFromTheGlobalSpriteArchive()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        RecordingGraphicsDevice device = new();
        using CameraStatusLightRenderer renderer = new(device);

        Assert.True(renderer.Load(fileSystem));
        Assert.True(renderer.IsReady);
    }
}
