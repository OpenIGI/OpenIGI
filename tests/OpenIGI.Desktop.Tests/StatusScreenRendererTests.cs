using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Graphics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>Checks the executable-recovered retail HUD against the shipped resources.</summary>
public sealed class StatusScreenRendererTests
{
    [Theory]
    [InlineData(12.4f, "RANGE: 12M")]
    [InlineData(999.5f, "RANGE: 1000M")]
    [InlineData(1000f, "RANGE: 1000+M")]
    [InlineData(2500f, "RANGE: 1000+M")]
    public void RetailBinocularRangeUsesTheAuthoredNativeLabels(float metres, string expected)
    {
        Assert.Equal(expected, StatusScreenRenderer.FormatBinocularRange(metres));
    }

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
        Assert.Equal("font4.fnt", StatusScreenRenderer.SubtitleFontResource);
        Assert.Equal("LOCAL:screens/intro/font4.fnt", StatusScreenRenderer.SubtitleFontPath);
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
    public void RetailBinocularsUseTheGlobalDeviceFrameAndOverlayTargetsInScreenSpace()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        RecordingGraphicsDevice device = new();
        using StatusScreenRenderer renderer = new(device);
        SpriteBatch batch = new(device);

        Assert.True(renderer.Load(fileSystem));
        IresArchive global = IresArchive.Read(fileSystem.ReadAllBytes(StatusScreenRenderer.SpriteArchive));
        SpriteAsset deviceFrame = Asset(global, "binoculars.spr");
        SpriteFrame frame = Assert.Single(deviceFrame.Frames);
        Assert.Equal(256, frame.Width);
        Assert.Equal(256, frame.Height);

        batch.Begin();
        renderer.DrawBinocularOverlay(
            batch,
            screenWidth: 640,
            screenHeight: 480,
            targets: [new EnemyOverlayBox(7, 300f, 200f, 340f, 280f, 12f, 1f)],
            headingRadians: 0f);
        batch.End();

        Assert.NotEmpty(device.Draws);
        RecordingGraphicsDevice.DrawCall frameDraw = device.Draws[0];
        Assert.NotEqual(TextureHandle.None, frameDraw.Texture);
        Assert.Equal(0f, frameDraw.Vertices[0].X, 3);
        Assert.Equal(0f, frameDraw.Vertices[0].Y, 3);
        Assert.Equal(640f, frameDraw.Vertices[1].X - frameDraw.Vertices[0].X, 3);
        Assert.Equal(480f, frameDraw.Vertices[3].Y - frameDraw.Vertices[0].Y, 3);

        Assert.Contains(
            device.Draws,
            draw => draw.Texture == TextureHandle.None
                && draw.Vertices.Any(vertex => Math.Abs(vertex.X - 300f) < 0.01f));

        // FUN_004B5860 uses the native vertex strip and 1.5625% x 2.0833334% scales. At
        // 640x480 that produces the two 15x50 horizontal quads at x=240 and x=385.
        Assert.Contains(
            device.Draws,
            draw => draw.Texture == TextureHandle.None
                && draw.Vertices.Any(vertex => Math.Abs(vertex.X - 240f) < 0.01f)
                && draw.Vertices.Any(vertex => Math.Abs(vertex.X - 255f) < 0.01f)
                && draw.Vertices.Any(vertex => Math.Abs(vertex.Y - 215f) < 0.01f)
                && draw.Vertices.Any(vertex => Math.Abs(vertex.Y - 265f) < 0.01f));
        Assert.Contains(
            device.Draws,
            draw => draw.Texture == TextureHandle.None
                && draw.Vertices.Any(vertex => Math.Abs(vertex.X - 385f) < 0.01f)
                && draw.Vertices.Any(vertex => Math.Abs(vertex.X - 400f) < 0.01f)
                && draw.Vertices.Any(vertex => Math.Abs(vertex.Y - 215f) < 0.01f)
                && draw.Vertices.Any(vertex => Math.Abs(vertex.Y - 265f) < 0.01f));

        // The retail builder also emits full-width and full-height line-list axes through
        // FUN_004B5730; keep the native center geometry from regressing to short arms only.
        Assert.Contains(
            device.Draws,
            draw => draw.Texture == TextureHandle.None
                && draw.Vertices.Any(vertex => Math.Abs(vertex.X) < 0.01f && vertex.Y is > 239f and < 241f)
                && draw.Vertices.Any(vertex => Math.Abs(vertex.X - 640f) < 0.01f && vertex.Y is > 239f and < 241f));
        Assert.Contains(
            device.Draws,
            draw => draw.Texture == TextureHandle.None
                && draw.Vertices.Any(vertex => vertex.X is > 319f and < 321f && Math.Abs(vertex.Y) < 0.01f)
                && draw.Vertices.Any(vertex => vertex.X is > 319f and < 321f && Math.Abs(vertex.Y - 480f) < 0.01f));

        device.Draws.Clear();
        batch.Begin();
        renderer.DrawBinocularOverlay(
            batch,
            screenWidth: 1920,
            screenHeight: 1080,
            targets: null,
            headingRadians: 0f);
        batch.End();

        RecordingGraphicsDevice.DrawCall scaledFrame = device.Draws[0];
        Assert.Equal(0f, scaledFrame.Vertices[0].X, 3);
        Assert.Equal(0f, scaledFrame.Vertices[0].Y, 3);
        Assert.Equal(1920f, scaledFrame.Vertices[1].X - scaledFrame.Vertices[0].X, 3);
        Assert.Equal(1080f, scaledFrame.Vertices[3].Y - scaledFrame.Vertices[0].Y, 3);
    }

    [Fact]
    public void RetailBinocularsDrawTheRecoveredAnimatedSweepColumns()
    {
        RecordingGraphicsDevice device = new();
        using StatusScreenRenderer renderer = new(device);
        SpriteBatch batch = new(device);
        BinocularSweep sweep = new();
        BinocularSweepColumn expected = sweep.Tick(0).First(column => column.Height > 0f);
        Bgra32 expectedColour = Bgra32.FromRgb(
            (byte)Math.Clamp(MathF.Round(expected.Phase * 128f), 0f, 128f),
            128,
            0);

        batch.Begin();
        renderer.DrawBinocularOverlay(batch, 640, 480, simulationTick: 0);
        batch.End();

        float left = expected.X * 640f;
        float top = expected.Y * 480f;
        float right = left + (expected.Width * 640f);
        float bottom = top + (expected.Height * 480f);
        Assert.Contains(
            device.Draws,
            draw => draw.Texture == TextureHandle.None
                && draw.Vertices.Chunk(4).Any(quad =>
                    quad.All(vertex => vertex.Color == expectedColour)
                    && quad.Any(vertex => Math.Abs(vertex.X - left) < 0.01f && Math.Abs(vertex.Y - top) < 0.01f)
                    && quad.Any(vertex => Math.Abs(vertex.X - right) < 0.01f && Math.Abs(vertex.Y - bottom) < 0.01f)));
    }

    [Fact]
    public static void RetailBinocularsKeepTheNativeFallbackRangeWithoutAContact()
    {
        Assert.Equal("RANGE: 1000+M", StatusScreenRenderer.BinocularRangeText(null));
    }

    [Fact]
    public static void MedipackChargesRemainVisibleEvenThoughItsNativeGaugeIsNone()
    {
        WeaponType medipack = new() { Category = WeaponCategory.Medipack };

        Assert.Equal(50, StatusScreenRenderer.DisplayedAmmoCount(
            medipack,
            roundsInClip: GunTiming.UnloadedClipSentinel,
            reserve: 50));
        Assert.Equal(50, StatusScreenRenderer.DisplayedAmmoCount(
            medipack,
            roundsInClip: 1,
            reserve: 49));
    }

    [RequiresGameDataFact]
    public void CutsceneSubtitleUsesTheTallWhiteRetailFaceInsteadOfTheGreenStatusFace()
    {
        IresArchive computer = IresArchive.Read(File.ReadAllBytes(
            Path.Combine(GameData.RequireRoot(), "computer", "computer.res")));
        SpriteAsset subtitle = CutsceneFont();
        SpriteAsset status = Asset(computer, StatusScreenRenderer.FontResource);

        Assert.True(subtitle.IsFont);
        Assert.Equal(15, subtitle.DefaultHeight);
        Assert.Equal(9, status.DefaultHeight);
        Assert.NotEqual(subtitle.GetGlyph('A'), status.GetGlyph('A'));
    }

    [RequiresGameDataFact]
    public void CutsceneSubtitleUsesTheRetailPresentationScaleAndBrightSinglePass()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        RecordingGraphicsDevice device = new();
        using StatusScreenRenderer renderer = new(device);
        SpriteBatch batch = new(device);
        Assert.True(renderer.Load(fileSystem));

        SpriteFrame glyph = CutsceneFont().GetGlyph('A');
        const float PresentationScale = 2f; // 1280 / retail 640-pixel presentation width.

        batch.Begin();
        renderer.DrawStatusMessages(
            batch,
            [new StatusMessageDisplay("A", RevealedCharacters: 1, DisplayFrame: 0, IsCutscene: true)],
            screenWidth: 1280,
            screenHeight: 720,
            scale: PresentationScale);
        batch.End();

        RecordingGraphicsDevice.DrawCall draw = Assert.Single(device.Draws);
        Assert.Equal(glyph.Width * PresentationScale, draw.Vertices[1].X - draw.Vertices[0].X, 3);
        Assert.Equal(glyph.Height * PresentationScale, draw.Vertices[3].Y - draw.Vertices[0].Y, 3);
        Assert.Equal(4, draw.Vertices.Length);
        Assert.Equal(new Bgra32(255, 255, 255, 255), draw.Vertices[0].Color);
    }

    private static SpriteFrame Frame(IresArchive archive, string name)
        => Assert.Single(Asset(archive, name).Frames);

    private static SpriteAsset CutsceneFont() => SpriteLoader.Load(
        File.ReadAllBytes(Path.Combine(GameData.RequireRoot(), "screens", "intro", "font4.fnt")),
        StatusScreenRenderer.SubtitleFontResource);

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
