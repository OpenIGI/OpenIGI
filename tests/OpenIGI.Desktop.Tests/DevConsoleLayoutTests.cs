using OpenIGI.Desktop;
using OpenIGI.Host.DevTools;
using OpenIGI.TestSupport;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Covers the serialised panel geometry that carries the developer console's arrangement through
/// <c>settings.cfg</c>, and the key names the <c>bind</c> command uses.
/// </summary>
public sealed class DevConsoleLayoutTests
{
    private static UiWindow Window(UiRect frame)
    {
        var device = new RecordingGraphicsDevice();
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "JetBrainsMono-Regular.ttf");
        UiFont font = UiFont.Upload(device, FontAtlasBuilder.Build(File.ReadAllBytes(path), 32f));
        return new UiWindow(new UiTheme(font, null, font)) { Frame = frame };
    }

    [Fact]
    public void A_layout_round_trips_through_its_rendered_form()
    {
        UiWindow window = Window(new UiRect(40f, 60f, 1200f, 620f));
        string text = DevConsoleLayout.Render(new Dictionary<string, UiWindow> { ["console"] = window });

        Dictionary<string, PanelPlacement> parsed = DevConsoleLayout.Parse(text);

        PanelPlacement placement = Assert.Contains("console", parsed);
        Assert.Equal(new UiRect(40f, 60f, 1200f, 620f), placement.Frame);
        Assert.True(placement.IsOpen);
        Assert.False(placement.IsCollapsed);
    }

    [Fact]
    public void The_open_and_collapsed_flags_survive()
    {
        UiWindow window = Window(new UiRect(10f, 10f, 400f, 300f));
        window.IsOpen = false;
        window.IsCollapsed = true;

        Dictionary<string, PanelPlacement> parsed = DevConsoleLayout.Parse(
            DevConsoleLayout.Render(new Dictionary<string, UiWindow> { ["cvars"] = window }));

        PanelPlacement placement = Assert.Contains("cvars", parsed);
        Assert.False(placement.IsOpen);
        Assert.True(placement.IsCollapsed);
    }

    /// <summary>
    /// A layout is written by a tool, may be hand-edited, and may name panels an older or newer build
    /// does not have. One bad segment must cost that panel its position and nothing else.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("console")]
    [InlineData("console:")]
    [InlineData("console:1,2")]
    [InlineData("console:a,b,c,d")]
    [InlineData("console:0,0,0,0")]
    [InlineData("|||")]
    public void Malformed_input_is_skipped_rather_than_throwing(string text)
    {
        Dictionary<string, PanelPlacement> parsed = DevConsoleLayout.Parse(text);
        Assert.DoesNotContain("console", parsed);
    }

    [Fact]
    public void One_bad_segment_does_not_lose_the_others()
    {
        Dictionary<string, PanelPlacement> parsed = DevConsoleLayout.Parse(
            "console:10,10,800,400,1,0|broken:oops|watch:20,20,300,120,1,0");

        Assert.Contains("console", parsed);
        Assert.Contains("watch", parsed);
        Assert.DoesNotContain("broken", parsed);
    }

    [Fact]
    public void Null_is_treated_as_an_empty_layout()
    {
        Assert.Empty(DevConsoleLayout.Parse(null));
    }

    /// <summary>
    /// A panel saved on a large display and restored on a small one must not end up larger than the
    /// screen, with its own resize grips beyond the edge and no way back.
    /// </summary>
    [Fact]
    public void Applying_a_placement_shrinks_it_to_fit_a_smaller_display()
    {
        UiWindow window = Window(new UiRect(0f, 0f, 100f, 100f));
        window.MinSize = new UiVec2(320f, 140f);

        DevConsoleLayout.Apply(
            window,
            new PanelPlacement(new UiRect(100f, 100f, 3800f, 2000f), true, false),
            new UiVec2(1280f, 720f));

        Assert.True(window.Frame.Width <= 1280f);
        Assert.True(window.Frame.Height <= 720f);
        Assert.True(window.Frame.Y >= 0f);
    }

    [Fact]
    public void Applying_a_placement_never_leaves_it_off_screen()
    {
        UiWindow window = Window(new UiRect(0f, 0f, 400f, 300f));

        DevConsoleLayout.Apply(
            window,
            new PanelPlacement(new UiRect(9000f, 9000f, 400f, 300f), true, false),
            new UiVec2(1920f, 1080f));

        Assert.True(window.Frame.X < 1920f);
        Assert.True(window.Frame.Y < 1080f);
    }

    [Fact]
    public void Rendered_layouts_are_stable_so_settings_do_not_churn()
    {
        UiWindow a = Window(new UiRect(1f, 2f, 300f, 200f));
        UiWindow b = Window(new UiRect(3f, 4f, 300f, 200f));

        string first = DevConsoleLayout.Render(new Dictionary<string, UiWindow> { ["b"] = b, ["a"] = a });
        string second = DevConsoleLayout.Render(new Dictionary<string, UiWindow> { ["a"] = a, ["b"] = b });

        Assert.Equal(first, second);
    }
}
