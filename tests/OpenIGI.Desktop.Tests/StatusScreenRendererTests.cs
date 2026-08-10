using OpenIGI.Engine.Graphics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Weapons;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>Checks the executable-recovered retail HUD against the shipped resources.</summary>
public sealed class StatusScreenRendererTests
{
    [RequiresGameDataFact]
    public void EveryRecoveredAmmoGaugeHasTheFramesItsRetailHandlerCanSelect()
    {
        IresArchive status = IresArchive.Read(File.ReadAllBytes(
            Path.Combine(GameData.RequireRoot(), "screens", "game", "status", "status.res")));

        Assert.True(Asset(status, StatusScreenLayout.MagazineSprite).Frames.Count > StatusScreenLayout.MagazineFrames);
        Assert.True(Asset(status, StatusScreenLayout.BarrelSprite).Frames.Count
            > StatusScreenLayout.BarrelFrame(roundsInClip: 0, secondSubCounter: 0));
        Assert.True(Asset(status, StatusScreenLayout.ShellSprite).Frames.Count
            > StatusScreenLayout.ShellFrame(roundsInClip: 0));
        Assert.NotEmpty(Asset(status, StatusScreenLayout.RocketSprite).Frames);
        Assert.NotEmpty(Asset(status, StatusScreenLayout.M16AlternativeSprite).Frames);
    }

    [RequiresGameDataFact]
    public void HealthBarUsesTheRetailSpritesAndBottomCropAtAnyResolution()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        RecordingGraphicsDevice device = new();
        using StatusScreenRenderer renderer = new(device);
        SpriteBatch batch = new(device);

        Assert.Equal("COMPUTER:computer.res", StatusScreenRenderer.FontArchive);
        Assert.Equal("LOCAL:computer/computer.res", StatusScreenRenderer.LocalFontArchive);
        Assert.Equal("font3.fnt", StatusScreenRenderer.FontResource);
        Assert.Equal("font1.fnt", StatusScreenRenderer.SubtitleFontResource);
        Assert.True(renderer.Load(fileSystem));

        IresArchive status = IresArchive.Read(fileSystem.ReadAllBytes(StatusScreenLayout.Archive));
        SpriteFrame background = Frame(status, StatusScreenLayout.HealthBackgroundSprite);
        SpriteFrame foreground = Frame(status, StatusScreenLayout.HealthForegroundSprite);

        Draw(width: 640, height: 480, scale: 1);
        Assert.Equal(2, device.Draws.Count);
        AssertFrameAtAnchor(device.Draws[0], background, 40f, 430f, scale: 1f, inset: 0f);
        AssertBottomQuarter(device.Draws[1], foreground, 40f, 430f, scale: 1f);

        // The same authored sizes are magnified, while the IDA-recovered fractional anchors follow a
        // widescreen framebuffer. This catches both fixed-640 positioning and unscaled 4K artwork.
        device.Draws.Clear();
        Draw(width: 1920, height: 1080, scale: 2);
        Assert.Equal(2, device.Draws.Count);
        AssertFrameAtAnchor(device.Draws[0], background, 120f, 967.5f, scale: 2f, inset: 0f);
        AssertBottomQuarter(device.Draws[1], foreground, 120f, 967.5f, scale: 2f);

        void Draw(int width, int height, int scale)
        {
            batch.Begin();
            renderer.DrawPlayerStatus(
                batch,
                healthFraction: 0.25f,
                interactionProgress: null,
                missionTimeRemainingTicks: null,
                width,
                height,
                scale);
            batch.End();
        }
    }

    [RequiresGameDataFact]
    public void CutsceneSubtitleUsesTheTallWhiteRetailFaceInsteadOfTheGreenStatusFace()
    {
        IresArchive computer = IresArchive.Read(File.ReadAllBytes(
            Path.Combine(GameData.RequireRoot(), "computer", "computer.res")));
        SpriteAsset subtitle = Asset(computer, StatusScreenRenderer.SubtitleFontResource);
        SpriteAsset status = Asset(computer, StatusScreenRenderer.FontResource);

        Assert.Equal(18, subtitle.DefaultHeight);
        Assert.Equal(9, status.DefaultHeight);

        bool foundOpaqueWhite = false;
        foreach (var texture in subtitle.Textures)
        {
            foreach (var pixel in texture.GetLevel(0))
            {
                if (pixel.A == 0)
                {
                    continue;
                }

                Assert.True(pixel.R == pixel.G && pixel.G == pixel.B,
                    $"subtitle glyph contains baked colour ({pixel.R},{pixel.G},{pixel.B})");
                foundOpaqueWhite |= pixel is { R: 255, G: 255, B: 255, A: 255 };
            }
        }

        Assert.True(foundOpaqueWhite);
    }

    private static SpriteFrame Frame(IresArchive archive, string name)
        => Assert.Single(Asset(archive, name).Frames);

    private static SpriteAsset Asset(IresArchive archive, string name)
    {
        IresEntry entry = Assert.Single(
            archive.Entries,
            candidate => Path.GetFileName(candidate.Name).Equals(name, StringComparison.OrdinalIgnoreCase));
        return SpriteLoader.Load(entry.Data, name);
    }

    private static void AssertFrameAtAnchor(
        RecordingGraphicsDevice.DrawCall draw,
        in SpriteFrame frame,
        float anchorX,
        float anchorY,
        float scale,
        float inset)
    {
        Assert.Equal(anchorX - (inset * scale) - (frame.Width * scale * 0.5f) + (frame.OffsetX * scale),
            draw.Vertices[0].X, 3);
        Assert.Equal(anchorY - (inset * scale) - (frame.Height * scale * 0.5f) + (frame.OffsetY * scale),
            draw.Vertices[0].Y, 3);
        Assert.Equal(frame.Width * scale, draw.Vertices[1].X - draw.Vertices[0].X, 3);
        Assert.Equal(frame.Height * scale, draw.Vertices[3].Y - draw.Vertices[0].Y, 3);
    }

    private static void AssertBottomQuarter(
        RecordingGraphicsDevice.DrawCall draw,
        in SpriteFrame frame,
        float anchorX,
        float anchorY,
        float scale)
    {
        float inset = StatusScreenLayout.HealthForegroundInset;
        float expectedTop = anchorY - (inset * scale) - (frame.Height * scale * 0.5f)
            + (frame.OffsetY * scale) + (frame.Height * scale * 0.75f);
        Assert.Equal(anchorX - (inset * scale) - (frame.Width * scale * 0.5f) + (frame.OffsetX * scale),
            draw.Vertices[0].X, 3);
        Assert.Equal(expectedTop, draw.Vertices[0].Y, 3);
        Assert.Equal(frame.Height * scale * 0.25f, draw.Vertices[3].Y - draw.Vertices[0].Y, 3);
        Assert.Equal(frame.V0 + ((frame.V1 - frame.V0) * 0.75f), draw.Vertices[0].V, 5);
        Assert.Equal(frame.V1, draw.Vertices[3].V, 5);
    }
}
