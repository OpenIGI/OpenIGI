using OpenIGI.Input;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.UI.Tests;

/// <summary>
/// Covers the floating-window widget, its hit regions, the drag and resize state machine, and the
/// row-virtualised list — all through <see cref="RecordingGraphicsDevice"/>, with no GL context.
/// </summary>
public sealed class WindowTests
{
    private static UiTheme Theme()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "JetBrainsMono-Regular.ttf");
        var device = new RecordingGraphicsDevice();
        UiFont font = UiFont.Upload(device, FontAtlasBuilder.Build(File.ReadAllBytes(path), 32f));
        return new UiTheme(font, null, font);
    }

    private static UiWindow Window(UiRect frame) =>
        new(Theme(), "Console") { Frame = frame, GripSize = 6f, TitleHeight = 30f };

    // ---- Hit regions ----

    [Fact]
    public void A_point_outside_the_frame_hits_nothing()
    {
        UiWindow window = Window(new UiRect(100f, 100f, 400f, 300f));
        Assert.Equal(WindowRegion.None, window.RegionAt(new UiVec2(50f, 50f)));
    }

    [Fact]
    public void The_title_bar_is_where_a_drag_starts()
    {
        UiWindow window = Window(new UiRect(100f, 100f, 400f, 300f));
        Assert.Equal(WindowRegion.Title, window.RegionAt(new UiVec2(300f, 115f)));
    }

    [Fact]
    public void The_middle_is_client_area()
    {
        UiWindow window = Window(new UiRect(100f, 100f, 400f, 300f));
        Assert.Equal(WindowRegion.Client, window.RegionAt(new UiVec2(300f, 250f)));
    }

    [Theory]
    [InlineData(101f, 250f, WindowRegion.Left)]
    [InlineData(498f, 250f, WindowRegion.Right)]
    [InlineData(300f, 101f, WindowRegion.Top)]
    [InlineData(300f, 398f, WindowRegion.Bottom)]
    public void Each_edge_reports_itself(float x, float y, WindowRegion expected)
    {
        UiWindow window = Window(new UiRect(100f, 100f, 400f, 300f));
        Assert.Equal(expected, window.RegionAt(new UiVec2(x, y)));
    }

    /// <summary>
    /// Corners must beat edges, or the bottom-right grip — the one people actually aim for — would
    /// only ever resize one axis.
    /// </summary>
    [Theory]
    [InlineData(101f, 101f, WindowRegion.Left | WindowRegion.Top)]
    [InlineData(498f, 101f, WindowRegion.Right | WindowRegion.Top)]
    [InlineData(101f, 398f, WindowRegion.Left | WindowRegion.Bottom)]
    [InlineData(498f, 398f, WindowRegion.Right | WindowRegion.Bottom)]
    public void Each_corner_reports_both_of_its_edges(float x, float y, WindowRegion expected)
    {
        UiWindow window = Window(new UiRect(100f, 100f, 400f, 300f));
        Assert.Equal(expected, window.RegionAt(new UiVec2(x, y)));
    }

    [Fact]
    public void A_window_that_cannot_be_resized_has_no_grips()
    {
        UiWindow window = Window(new UiRect(100f, 100f, 400f, 300f));
        window.IsResizable = false;

        Assert.Equal(WindowRegion.Title, window.RegionAt(new UiVec2(101f, 101f)));
    }

    [Fact]
    public void A_collapsed_window_is_only_its_title_bar()
    {
        UiWindow window = Window(new UiRect(100f, 100f, 400f, 300f));
        window.IsCollapsed = true;

        Assert.Equal(WindowRegion.Title, window.RegionAt(new UiVec2(300f, 110f)));
        Assert.Equal(WindowRegion.None, window.RegionAt(new UiVec2(300f, 250f)));
    }

    // ---- Resize ----

    [Fact]
    public void Dragging_the_right_edge_widens_the_window()
    {
        UiWindow window = Window(new UiRect(100f, 100f, 400f, 300f));
        UiRect start = window.Frame;

        window.ResizeBy(WindowRegion.Right, new UiVec2(50f, 0f), start);

        Assert.Equal(450f, window.Frame.Width);
        Assert.Equal(100f, window.Frame.X);
    }

    [Fact]
    public void Dragging_the_left_edge_moves_it_and_keeps_the_right_still()
    {
        UiWindow window = Window(new UiRect(100f, 100f, 400f, 300f));
        UiRect start = window.Frame;

        window.ResizeBy(WindowRegion.Left, new UiVec2(50f, 0f), start);

        Assert.Equal(150f, window.Frame.X);
        Assert.Equal(500f, window.Frame.Right);
    }

    [Fact]
    public void A_corner_drag_resizes_both_axes()
    {
        UiWindow window = Window(new UiRect(100f, 100f, 400f, 300f));
        UiRect start = window.Frame;

        window.ResizeBy(WindowRegion.Right | WindowRegion.Bottom, new UiVec2(40f, 60f), start);

        Assert.Equal(440f, window.Frame.Width);
        Assert.Equal(360f, window.Frame.Height);
    }

    [Fact]
    public void Resizing_never_goes_below_the_minimum()
    {
        UiWindow window = Window(new UiRect(100f, 100f, 400f, 300f));
        window.MinSize = new UiVec2(320f, 140f);
        UiRect start = window.Frame;

        window.ResizeBy(WindowRegion.Right | WindowRegion.Bottom, new UiVec2(-900f, -900f), start);

        Assert.Equal(320f, window.Frame.Width);
        Assert.Equal(140f, window.Frame.Height);
    }

    /// <summary>
    /// Resolving against the frame the drag began from, rather than accumulating per frame, is what
    /// stops a drag that pushed past the minimum and came back from having drifted by the clamped
    /// remainder.
    /// </summary>
    [Fact]
    public void A_drag_past_the_minimum_and_back_returns_exactly_where_it_started()
    {
        UiWindow window = Window(new UiRect(100f, 100f, 400f, 300f));
        window.MinSize = new UiVec2(320f, 140f);
        UiRect start = window.Frame;

        window.ResizeBy(WindowRegion.Right, new UiVec2(-900f, 0f), start);
        window.ResizeBy(WindowRegion.Right, new UiVec2(0f, 0f), start);

        Assert.Equal(start, window.Frame);
    }

    // ---- Snapping and clamping ----

    [Fact]
    public void An_edge_near_the_viewport_snaps_flush_to_it()
    {
        UiWindow window = Window(new UiRect(6f, 5f, 400f, 300f));
        window.SnapToEdges(new UiVec2(1920f, 1080f), distance: 12f);

        Assert.Equal(0f, window.Frame.X);
        Assert.Equal(0f, window.Frame.Y);
    }

    [Fact]
    public void An_edge_beyond_the_snap_distance_stays_put()
    {
        UiWindow window = Window(new UiRect(40f, 40f, 400f, 300f));
        window.SnapToEdges(new UiVec2(1920f, 1080f), distance: 12f);

        Assert.Equal(40f, window.Frame.X);
    }

    [Fact]
    public void The_right_edge_snaps_to_the_far_side_too()
    {
        UiWindow window = Window(new UiRect(1516f, 40f, 400f, 300f));
        window.SnapToEdges(new UiVec2(1920f, 1080f), distance: 12f);

        Assert.Equal(1920f, window.Frame.Right);
    }

    /// <summary>
    /// The failure this prevents is permanent: a window dragged off the top, or a resolution change
    /// that leaves it beyond the screen, is saved to the settings file and restored out of reach on
    /// every launch after.
    /// </summary>
    [Fact]
    public void A_window_can_never_be_left_out_of_reach()
    {
        UiWindow window = Window(new UiRect(-5000f, -5000f, 400f, 300f));
        window.ClampIntoView(new UiVec2(1920f, 1080f));

        Assert.True(window.Frame.Y >= 0f);
        Assert.True(window.Frame.Right > 0f);

        window.Frame = new UiRect(9000f, 9000f, 400f, 300f);
        window.ClampIntoView(new UiVec2(1920f, 1080f));

        Assert.True(window.Frame.X < 1920f);
        Assert.True(window.Frame.Y < 1080f);
    }

    // ---- The layer ----

    [Fact]
    public void Pressing_a_window_brings_it_to_the_front()
    {
        var theme = Theme();
        var layer = new WindowLayer();
        UiWindow back = layer.Add(new UiWindow(theme, "back") { Frame = new UiRect(0f, 0f, 400f, 300f) });
        UiWindow front = layer.Add(new UiWindow(theme, "front") { Frame = new UiRect(600f, 0f, 400f, 300f) });

        Assert.Same(front, layer.Focused);

        var mouse = new MouseState();
        mouse.SetPosition(100f, 10f);
        mouse.BeginFrame();
        mouse.SetPosition(100f, 10f);
        mouse.SetButton(MouseButton.Left, true);

        layer.UpdatePointer(mouse, new UiVec2(1920f, 1080f));

        Assert.Same(back, layer.Focused);
        Assert.Same(back, layer.Windows[^1]);
    }

    [Fact]
    public void Overlapping_windows_hand_the_point_to_the_front_one()
    {
        var theme = Theme();
        var layer = new WindowLayer();
        layer.Add(new UiWindow(theme, "back") { Frame = new UiRect(0f, 0f, 400f, 300f) });
        UiWindow front = layer.Add(new UiWindow(theme, "front") { Frame = new UiRect(0f, 0f, 400f, 300f) });

        Assert.Same(front, layer.WindowAt(new UiVec2(200f, 150f))!.Value.Window);
    }

    [Fact]
    public void A_closed_window_is_not_hit()
    {
        var layer = new WindowLayer();
        UiWindow window = layer.Add(new UiWindow(Theme(), "w") { Frame = new UiRect(0f, 0f, 400f, 300f) });
        window.IsOpen = false;

        Assert.Null(layer.WindowAt(new UiVec2(200f, 150f)));
    }

    /// <summary>
    /// Dragging must be driven by pointer position rather than by frame deltas, because the deltas
    /// read as zero the moment input is captured — which is exactly when a console is being used.
    /// </summary>
    [Fact]
    public void A_title_drag_follows_the_pointer_across_frames()
    {
        var layer = new WindowLayer { SnapDistance = 0f };
        UiWindow window = layer.Add(new UiWindow(Theme(), "w") { Frame = new UiRect(100f, 100f, 400f, 300f) });
        var viewport = new UiVec2(1920f, 1080f);

        var mouse = new MouseState();
        mouse.SetPosition(300f, 110f);
        mouse.BeginFrame();
        mouse.SetPosition(300f, 110f);
        mouse.SetButton(MouseButton.Left, true);
        layer.UpdatePointer(mouse, viewport);

        mouse.BeginFrame();
        mouse.SetPosition(360f, 190f);
        layer.UpdatePointer(mouse, viewport);

        Assert.Equal(160f, window.Frame.X);
        Assert.Equal(180f, window.Frame.Y);
        Assert.True(layer.IsManipulating);

        mouse.BeginFrame();
        mouse.SetButton(MouseButton.Left, false);
        layer.UpdatePointer(mouse, viewport);

        Assert.False(layer.IsManipulating);
    }

    [Fact]
    public void Releasing_a_drag_lands_the_window_on_whole_pixels()
    {
        var layer = new WindowLayer { SnapDistance = 0f };
        UiWindow window = layer.Add(new UiWindow(Theme(), "w") { Frame = new UiRect(100f, 100f, 400f, 300f) });
        var viewport = new UiVec2(1920f, 1080f);

        var mouse = new MouseState();
        mouse.SetPosition(300f, 110f);
        mouse.BeginFrame();
        mouse.SetPosition(300f, 110f);
        mouse.SetButton(MouseButton.Left, true);
        layer.UpdatePointer(mouse, viewport);

        mouse.BeginFrame();
        mouse.SetPosition(300.4f, 110.6f);
        layer.UpdatePointer(mouse, viewport);

        mouse.BeginFrame();
        mouse.SetButton(MouseButton.Left, false);
        layer.UpdatePointer(mouse, viewport);

        Assert.Equal(MathF.Round(window.Frame.X), window.Frame.X);
        Assert.Equal(MathF.Round(window.Frame.Y), window.Frame.Y);
    }

    // ---- Opening a window that was closed ----

    private static void Layout(WindowLayer layer, float width = 1280f, float height = 720f)
    {
        var context = new LayoutContext(width, height, reference: new UiVec2(width, height));
        layer.Measure(context, new UiVec2(width, height));
        layer.Arrange(new UiRect(0f, 0f, width, height));
    }

    /// <summary>
    /// The crash this prevents: visibility used to be decided during arrange, so a window opened this
    /// frame was still flagged collapsed when it was measured. Measure returns immediately for a
    /// collapsed element without measuring its content, and arrange then threw on content that had
    /// never been measured — a crash on the first frame any panel was shown, and only that frame.
    /// </summary>
    [Fact]
    public void A_window_opened_after_being_closed_lays_out_without_throwing()
    {
        var theme = Theme();
        var layer = new WindowLayer();
        var window = new UiWindow(theme, "panel") { Frame = new UiRect(10f, 10f, 400f, 300f) };
        window.Content = new Label(theme.Body, "content", 14f, theme.Text);
        layer.Add(window);

        window.IsOpen = false;
        Layout(layer);

        window.IsOpen = true;
        Layout(layer);

        Assert.Equal(Visibility.Visible, window.Visibility);
        Assert.False(window.Bounds.IsEmpty);
    }

    [Fact]
    public void A_window_open_from_the_start_lays_out_without_throwing()
    {
        var theme = Theme();
        var layer = new WindowLayer();
        var window = new UiWindow(theme, "panel") { Frame = new UiRect(10f, 10f, 400f, 300f) };
        window.Content = new Label(theme.Body, "content", 14f, theme.Text);
        layer.Add(window);

        Layout(layer);

        Assert.False(window.Bounds.IsEmpty);
    }

    /// <summary>
    /// A closed window's whole subtree must have empty bounds, or its content keeps answering clicks
    /// at the place it was last laid out.
    /// </summary>
    [Fact]
    public void Closing_a_window_empties_its_bounds()
    {
        var theme = Theme();
        var layer = new WindowLayer();
        var window = new UiWindow(theme, "panel") { Frame = new UiRect(10f, 10f, 400f, 300f) };
        var content = new Label(theme.Body, "content", 14f, theme.Text);
        window.Content = content;
        layer.Add(window);

        Layout(layer);
        Assert.False(content.Bounds.IsEmpty);

        window.IsOpen = false;
        Layout(layer);

        Assert.True(window.Bounds.IsEmpty);
        Assert.True(content.Bounds.IsEmpty);
    }

    [Fact]
    public void Panels_can_be_opened_and_closed_repeatedly()
    {
        var theme = Theme();
        var layer = new WindowLayer();
        var window = new UiWindow(theme, "panel") { Frame = new UiRect(10f, 10f, 400f, 300f) };
        window.Content = new Label(theme.Body, "content", 14f, theme.Text);
        layer.Add(window);

        for (int i = 0; i < 5; i++)
        {
            window.IsOpen = false;
            Layout(layer);
            window.IsOpen = true;
            Layout(layer);
        }

        Assert.False(window.Bounds.IsEmpty);
    }

    [Fact]
    public void The_close_button_asks_to_close_rather_than_dragging()
    {
        var layer = new WindowLayer();
        UiWindow window = layer.Add(new UiWindow(Theme(), "w") { Frame = new UiRect(100f, 100f, 400f, 300f) });
        bool asked = false;
        window.CloseRequested += (_, _) => asked = true;

        var mouse = new MouseState();
        UiVec2 centre = window.CloseRect.Center;
        mouse.SetPosition(centre.X, centre.Y);
        mouse.BeginFrame();
        mouse.SetPosition(centre.X, centre.Y);
        mouse.SetButton(MouseButton.Left, true);

        layer.UpdatePointer(mouse, new UiVec2(1920f, 1080f));

        Assert.True(asked);
        Assert.False(layer.IsManipulating);
    }
}
