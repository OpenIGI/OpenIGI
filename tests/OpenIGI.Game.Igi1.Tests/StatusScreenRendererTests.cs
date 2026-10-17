using OpenIGI.Game.Igi1.World;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Graphics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Presentation.Igi1.World;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>Checks the executable-recovered retail HUD against the shipped resources.</summary>
public sealed class StatusScreenRendererTests
{
    [Theory]
    [InlineData(12.4f, "DISTANCE: 12M")]
    [InlineData(999.5f, "DISTANCE: 1000M")]
    [InlineData(1000f, "DISTANCE: 1000+M")]
    [InlineData(2500f, "DISTANCE: 1000+M")]
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
    public void RetailBinocularsDoNotStretchTheStatusIconAndOverlayTargetsInScreenSpace()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        RecordingGraphicsDevice device = new();
        using StatusScreenRenderer renderer = new(device);
        SpriteBatch batch = new(device);

        Assert.True(renderer.Load(fileSystem));
        batch.Begin();
        renderer.DrawBinocularOverlay(
            batch,
            screenWidth: 640,
            screenHeight: 480,
            targets: [new EnemyOverlayBox(7, 300f, 200f, 340f, 280f, 12f, 1f)],
            headingRadians: 0f);
        batch.End();

        Assert.NotEmpty(device.Draws);
        // r2 and Ghidra agree that 0x470fa0 resolves its binocular asset separately from
        // STATUSSCREEN:binoculars.spr. The latter is a 37x26 inventory icon and must not turn
        // into an opaque fullscreen quad when the player opens the device.
        Assert.DoesNotContain(
            device.Draws,
            draw => draw.Texture != TextureHandle.None
                && draw.Vertices.Length == 4
                && Math.Abs(draw.Vertices[0].X) < 0.01f
                && Math.Abs(draw.Vertices[0].Y) < 0.01f
                && Math.Abs(draw.Vertices[1].X - 640f) < 0.01f
                && Math.Abs(draw.Vertices[3].Y - 480f) < 0.01f);

        // FUN_00471480 draws COMMON/SPRITES' 256x256 binocular texture twice through
        // FUN_004B51F0. At 640x480 its native s = width / 256 overscan is 2.5 pixels: the left
        // panel runs from (-2.5, -2.5) to (320, 482.5), while the mirrored right panel runs from
        // (642.5, -2.5) back to the centre. This keeps the original cropped lens/frame layout at
        // every aspect ratio rather than substituting a flat green placeholder.
        Assert.Contains(
            device.Draws,
            draw => draw.Texture != TextureHandle.None
                && draw.Vertices.Chunk(4).Any(quad =>
                    Math.Abs(quad[0].X + 2.5f) < 0.01f
                    && Math.Abs(quad[0].Y + 2.5f) < 0.01f
                    && Math.Abs(quad[1].X - 320f) < 0.01f
                    && Math.Abs(quad[2].Y - 482.5f) < 0.01f));
        Assert.Contains(
            device.Draws,
            draw => draw.Texture != TextureHandle.None
                && draw.Vertices.Chunk(4).Any(quad =>
                    Math.Abs(quad[0].X - 642.5f) < 0.01f
                    && Math.Abs(quad[0].Y + 2.5f) < 0.01f
                    && Math.Abs(quad[1].X - 320f) < 0.01f
                    && Math.Abs(quad[3].Y - 482.5f) < 0.01f));

        Assert.Contains(
            device.Draws,
            draw => draw.Texture == TextureHandle.None
                && draw.Vertices.Any(vertex => Math.Abs(vertex.X - 300f) < 0.01f));

        // The target strip is hostile red: FUN_00471B60's (64,0,0) through FUN_004B5730's 1/128
        // channel scale is half-bright opaque red. This also guards the BGRA/RGBA channel boundary:
        // a raw Bgra32(128, 0, 0, ...) literal would render the retail marker blue.
        Assert.Contains(
            device.Draws,
            draw => draw.Texture == TextureHandle.None
                && draw.Vertices.Count(vertex => vertex.Color.B == 0
                    && vertex.Color.G == 0
                    && vertex.Color.R == 128
                    && vertex.Color.A == 255) >= 16);

        var markerVertices = device.Draws
            .SelectMany(draw => draw.Vertices)
            .Where(vertex => vertex.Color.B == 0
                && vertex.Color.G == 0
                && vertex.Color.R == 128
                && vertex.Color.A == 255)
            .ToArray();
        Assert.True(markerVertices.Length >= 16);
        Assert.InRange(markerVertices.Max(vertex => vertex.X) - markerVertices.Min(vertex => vertex.X), 39f, 45f);
        Assert.InRange(markerVertices.Max(vertex => vertex.Y) - markerVertices.Min(vertex => vertex.Y), 79f, 85f);

        // FUN_004B5860 walks unk_534070 as a line strip, so each reticle arm is a thin "[" / "]"
        // bracket outline (1.5625% x 2.0833334% scales, ±9.375% anchors), not a filled block. At
        // 640x480 the left bracket spans x∈[240,255], y∈[215,265], and the right mirrors it to
        // x∈[385,400]. The sight axes have no vertices inside those x-bands, so filtering the reticle
        // green there isolates the bracket geometry.
        Vertex2D[] reticleGreen = device.Draws
            .Where(draw => draw.Texture == TextureHandle.None)
            .SelectMany(draw => draw.Vertices)
            .Where(vertex => vertex.Color.B == 64 && vertex.Color.G == 160
                && vertex.Color.R == 64 && vertex.Color.A == 210)
            .ToArray();

        Vertex2D[] leftBracket = reticleGreen.Where(vertex => vertex.X is > 235f and < 260f).ToArray();
        Assert.NotEmpty(leftBracket);
        Assert.InRange(leftBracket.Min(vertex => vertex.X), 238f, 241f);
        Assert.InRange(leftBracket.Max(vertex => vertex.X), 254f, 257f);
        Assert.InRange(leftBracket.Min(vertex => vertex.Y), 213f, 217f);
        Assert.InRange(leftBracket.Max(vertex => vertex.Y), 263f, 267f);

        Vertex2D[] rightBracket = reticleGreen.Where(vertex => vertex.X is > 380f and < 405f).ToArray();
        Assert.NotEmpty(rightBracket);
        Assert.InRange(rightBracket.Min(vertex => vertex.X), 383f, 386f);
        Assert.InRange(rightBracket.Max(vertex => vertex.X), 399f, 402f);
        Assert.InRange(rightBracket.Min(vertex => vertex.Y), 213f, 217f);
        Assert.InRange(rightBracket.Max(vertex => vertex.Y), 263f, 267f);

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

        // FUN_00472050 closes the overlay with the compass strip at the bottom-right of the sight.
        // Its ticks and labels use the opaque phosphor green (64,160,64,255) — distinct from the
        // half-transparent reticle green (alpha 210) — laid across x in [62.5%, 90.625%] of the width.
        var compassGeometry = device.Draws
            .Where(draw => draw.Texture == TextureHandle.None)
            .SelectMany(draw => draw.Vertices)
            .Where(vertex => vertex.Color.B == 64 && vertex.Color.G == 160
                && vertex.Color.R == 64 && vertex.Color.A == 255)
            .ToArray();
        Assert.NotEmpty(compassGeometry);
        // The strip occupies the bottom-right quadrant: x around [62.5%, 90.625%] of 640 and y in the
        // 79-90% band. A cardinal glyph centred on a wrapped tick can spill a few pixels past the
        // edges, so the bounds are generous rather than exact.
        Assert.All(compassGeometry, vertex => Assert.InRange(vertex.X, 380f, 595f));
        Assert.All(compassGeometry, vertex => Assert.InRange(vertex.Y, 360f, 470f));

        device.Draws.Clear();
        batch.Begin();
        renderer.DrawBinocularOverlay(
            batch,
            screenWidth: 1920,
            screenHeight: 1080,
            targets: null,
            headingRadians: 0f);
        batch.End();

        Assert.DoesNotContain(
            device.Draws,
            draw => draw.Texture != TextureHandle.None
                && draw.Vertices.Length == 4
                && Math.Abs(draw.Vertices[0].X) < 0.01f
                && Math.Abs(draw.Vertices[0].Y) < 0.01f
                && Math.Abs(draw.Vertices[1].X - 1920f) < 0.01f
                && Math.Abs(draw.Vertices[3].Y - 1080f) < 0.01f);
        Assert.Contains(
            device.Draws,
            draw => draw.Texture == TextureHandle.None
                && draw.Vertices.Any(vertex => Math.Abs(vertex.X) < 0.01f && vertex.Y is > 539f and < 541f)
                && draw.Vertices.Any(vertex => Math.Abs(vertex.X - 1920f) < 0.01f && vertex.Y is > 539f and < 541f));
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
        Assert.Equal("DISTANCE: 1000+M", StatusScreenRenderer.BinocularRangeText(null));
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
