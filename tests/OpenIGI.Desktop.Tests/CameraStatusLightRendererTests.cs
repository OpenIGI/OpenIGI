using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
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

        IresArchive archive = IresArchive.Read(fileSystem.ReadAllBytes(CameraStatusLightRenderer.SpriteArchive));
        IresEntry entry = Assert.Single(
            archive.Entries,
            candidate => Path.GetFileName(candidate.Name).Equals(
                CameraStatusLightRenderer.SpriteName,
                StringComparison.OrdinalIgnoreCase));
        SpriteFrame frame = Assert.Single(
            SpriteLoader.Load(entry.Data, CameraStatusLightRenderer.SpriteName).Frames);
        Assert.Equal(0f, frame.U0);
        Assert.Equal(0f, frame.V0);
        Assert.Equal(1f, frame.U1);
        Assert.Equal(1f, frame.V1);
    }
}
