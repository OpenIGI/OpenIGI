using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Presentation.Igi1.Hud;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Presentation.Text;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Headless;
using OpenIGI.UI.Layout;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>The remastered computer: where its pieces stand, what its feed is coloured with, and the order it is drawn in.</summary>
public sealed class TacticalComputerTests
{
    [Theory]
    [InlineData(640, 480)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(3440, 1440)]
    [InlineData(3840, 2160)]
    public void The_tabs_stand_in_the_top_strip_and_the_map_and_a_page_between_the_strips(int width, int height)
    {
        float line = 11f * ComputerText.ScaleFor(UiScale.Automatic, height);
        float[] names = [line * 2f, line * 6f, line * 2f, line * 3f];
        UiRect[] tabs = new UiRect[names.Length];
        TacticalComputerChrome chrome = TacticalComputerChrome.Measure(width, height, line, pageVisible: true, names, tabs);

        Assert.Equal(0f, chrome.Top.Y);
        Assert.Equal(height, chrome.Bottom.Bottom);
        Assert.Equal(chrome.Top.Bottom, chrome.Map.Y);
        Assert.Equal(chrome.Bottom.Y, chrome.Map.Bottom);
        Assert.True(chrome.Map.Height > height * 0.8f);

        for (int i = 0; i < tabs.Length; i++)
        {
            Assert.True(tabs[i].Y >= chrome.Top.Y && tabs[i].Bottom <= chrome.Top.Bottom);
            Assert.True(tabs[i].Width > names[i]);
            Assert.True(tabs[i].Height >= line);
            if (i > 0)
            {
                Assert.True(tabs[i].X > tabs[i - 1].Right);
            }

            // A tab is clicked where it is drawn: the one rectangle answers both.
            Assert.True(chrome.OverInterface(tabs[i].X + 1f, tabs[i].Y + 1f));
        }

        Assert.True(chrome.Page.Y > chrome.Top.Bottom && chrome.Page.Bottom < chrome.Bottom.Y);
        Assert.True(chrome.Page.X >= 0f && chrome.Page.Right <= width);
        Assert.True(chrome.Page.Width <= (TacticalComputerChrome.PageWidth * chrome.Unit) + 0.5f);
    }

    [Fact]
    public void The_bare_map_is_not_the_interface_and_a_page_is_only_while_it_is_up()
    {
        UiRect[] tabs = new UiRect[1];
        TacticalComputerChrome map = TacticalComputerChrome.Measure(1920, 1080, 22f, pageVisible: false, [40f], tabs);
        Assert.False(map.OverInterface(960f, 540f));
        Assert.True(map.OverInterface(960f, 4f));
        Assert.True(map.OverInterface(960f, 1076f));

        TacticalComputerChrome page = map with { PageVisible = true };
        Assert.True(page.OverInterface(960f, 540f));
        Assert.False(page.OverInterface(page.Page.X - 8f, 540f));
    }

    [Theory]
    [InlineData(480, 1)]
    [InlineData(720, 1)]
    [InlineData(1080, 2)]
    [InlineData(1440, 2)]
    [InlineData(2160, 4)]
    public void The_text_is_drawn_at_a_whole_number_of_pixels_a_pixel_or_the_players_own(int height, int scale)
    {
        Assert.Equal(scale, ComputerText.ScaleFor(UiScale.Automatic, height));
        Assert.Equal(3, ComputerText.ScaleFor(3, height));
    }

    [Fact]
    public void The_feed_is_coloured_from_the_titles_own_look_and_keeps_how_far_it_has_come_up()
    {
        HudTheme theme = Igi1HudTheme.Theme;
        MapFeedSettings signal = new() { Amount = 0.6f, Boot = 0.3f, Sweep = 0.2f, SweepPosition = 0.4f, Seconds = 9f };
        MapFeedSettings feed = TacticalComputerRenderer.FeedFor(theme, signal, curvature: 0.01f, dim: 0.5f, burst: 0.25f);

        Assert.Equal(0.6f, feed.Amount);
        Assert.Equal(0.3f, feed.Boot);
        Assert.Equal(0.2f, feed.Sweep);
        Assert.Equal(0.4f, feed.SweepPosition);
        Assert.Equal(9f, feed.Seconds);
        Assert.Equal(0.5f, feed.Dim);
        Assert.Equal(0.25f, feed.Burst);
        Assert.Equal(0.01f, feed.Curvature);
        Assert.Equal(theme.Fx.ScanlineDensity, feed.ScanlineDensity);

        // Project I.G.I.'s display is green: every stop of its feed is, and each is brighter than the last.
        Assert.True(feed.Body.G > feed.Body.R && feed.Body.G > feed.Body.B);
        Assert.True(feed.Highlight.G > feed.Highlight.R && feed.Highlight.G > feed.Highlight.B);
        Assert.True(feed.Shadow.G < feed.Body.G && feed.Body.G < feed.Highlight.G);
        Assert.Equal(theme.Palette.Dim.G / 255f, feed.Body.G);
    }

    [Fact]
    public void The_feed_is_laid_over_the_picture_first_and_the_display_over_it_through_the_visor()
    {
        Assert.True(UiFontLibrary.TypographyAvailable);
        using RecordingGraphicsDevice device = new();
        using TacticalComputerRenderer computer = NewComputer(device);
        Assert.True(computer.Load());
        computer.Open(default);
        SpriteBatch batch = new(device);

        computer.Draw(batch, new NoObjectives(), default, [], [], Frame(opacity: 1f));

        (MapFeedSettings feed, int drawsBefore) = Assert.Single(device.MapFeeds);
        Assert.Equal(1f, feed.Amount);
        Assert.Equal(TacticalComputerRenderer.TubeCurvature, feed.Curvature);
        Assert.Equal(0, drawsBefore);
        Assert.Collection(
            device.HudLayerEvents,
            begin => Assert.False(begin.Composed),
            compose =>
            {
                Assert.True(compose.Composed);
                Assert.True(compose.DrawsBefore > device.HudLayerEvents[0].DrawsBefore);
                Assert.Equal(1f, compose.Fx.Intensity);
                Assert.Equal(1f, compose.Fx.Appear);
                Assert.Equal(feed.Curvature, compose.Fx.Curvature);
            });
    }

    [Fact]
    public void Before_the_display_has_come_up_only_the_feed_is_drawn()
    {
        Assert.True(UiFontLibrary.TypographyAvailable);
        using RecordingGraphicsDevice device = new();
        using TacticalComputerRenderer computer = NewComputer(device);
        Assert.True(computer.Load());
        computer.Open(default);
        SpriteBatch batch = new(device);

        computer.Draw(batch, new NoObjectives(), default, [], [], Frame(opacity: 0f));

        Assert.Single(device.MapFeeds);
        Assert.Empty(device.HudLayerEvents);
        Assert.Empty(device.Draws);
    }

    [Fact]
    public void A_device_with_no_feed_and_no_layer_still_draws_the_display_flat_and_unbent()
    {
        Assert.True(UiFontLibrary.TypographyAvailable);
        using RecordingGraphicsDevice device = new() { SupportsMapFeed = false, SupportsHudLayer = false };
        using TacticalComputerRenderer computer = NewComputer(device);
        Assert.True(computer.Load());
        computer.Open(default);
        SpriteBatch batch = new(device);

        computer.Draw(batch, new NoObjectives(), default, [], [], Frame(opacity: 1f));

        Assert.Empty(device.MapFeeds);
        Assert.Empty(device.HudLayerEvents);
        Assert.NotEmpty(device.Draws);
    }

    private static ComputerFrame Frame(float opacity) =>
        new(1920, 1080, UiScale.Automatic, opacity, new MapFeedSettings { Amount = 1f, Seconds = 1f }, 0f);

    private static TacticalComputerRenderer NewComputer(RecordingGraphicsDevice device) =>
        new(device, new ResourceRegistry(new VirtualFileSystem()), NullLogger.Instance, static key => key, Igi1HudTheme.Theme);

    private sealed class NoObjectives : IComputerObjectives
    {
        public IReadOnlyList<MissionObjective> Objectives { get; } = [];

        public IReadOnlyList<MissionComputerLogEntry> Log { get; } = [];
    }
}
