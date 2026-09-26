using OpenIGI.Input;
using OpenIGI.Platform.Web;
using Xunit;

namespace OpenIGI.Platform.Web.Tests;

/// <summary>The browser window over a page that queues what a test says happened.</summary>
public sealed class WebGameWindowTests
{
    private readonly FakeBrowserPage _page = new();

    [Fact]
    public void Keys_and_characters_reach_the_frame_they_were_pressed_before()
    {
        using WebGameWindow window = Open();

        _page.Queue("key KeyW 1", "char w");
        window.PumpEvents();

        Assert.True(window.Input.Keyboard.IsDown(KeyCode.W));
        Assert.True(window.Input.Keyboard.WasPressed(KeyCode.W));
        Assert.True(window.Input.Keyboard.TryDequeueCharacter(out char typed));
        Assert.Equal('w', typed);

        _page.Queue("key KeyW 0");
        window.PumpEvents();

        Assert.False(window.Input.Keyboard.IsDown(KeyCode.W));
    }

    [Fact]
    public void The_pointer_moves_and_its_buttons_and_wheel_report()
    {
        using WebGameWindow window = Open();

        _page.Queue("move 4 -2 100 50", "move 1 1 101 51", "button 2 1", "wheel 1");
        window.PumpEvents();

        Assert.Equal(5f, window.Input.Mouse.DeltaX);
        Assert.Equal(-1f, window.Input.Mouse.DeltaY);
        Assert.Equal(101f, window.Input.Mouse.X);
        Assert.Equal(51f, window.Input.Mouse.Y);
        Assert.True(window.Input.Mouse.IsDown(MouseButton.Right));
        Assert.Equal(1f, window.Input.Mouse.WheelDelta);
    }

    /// <summary>A click or a tap quicker than a frame is still seen down for one.</summary>
    [Fact]
    public void A_press_and_release_inside_one_frame_is_down_for_that_frame()
    {
        using WebGameWindow window = Open();

        _page.Queue("button 0 1", "button 0 0", "key Space 1", "key Space 0");
        window.PumpEvents();

        Assert.True(window.Input.Mouse.WasPressed(MouseButton.Left));
        Assert.True(window.Input.Keyboard.WasPressed(KeyCode.Space));

        window.PumpEvents();

        Assert.False(window.Input.Mouse.IsDown(MouseButton.Left));
        Assert.True(window.Input.Mouse.WasReleased(MouseButton.Left));
        Assert.False(window.Input.Keyboard.IsDown(KeyCode.Space));
    }

    /// <summary>A key held when the page lost the keyboard never reports its release.</summary>
    [Fact]
    public void Losing_focus_lets_go_of_every_key()
    {
        using WebGameWindow window = Open();

        _page.Queue("key KeyW 1", "button 0 1");
        window.PumpEvents();
        _page.Queue("focus 0");
        window.PumpEvents();

        Assert.False(window.IsFocused);
        Assert.False(window.Input.Keyboard.IsDown(KeyCode.W));
        Assert.False(window.Input.Mouse.IsDown(MouseButton.Left));
    }

    /// <summary>The browser takes the lock back on Escape and keeps the key: it is handed on for a frame.</summary>
    [Fact]
    public void A_lock_lost_while_the_game_wants_it_is_an_escape_for_one_frame()
    {
        using WebGameWindow window = Open();
        window.CursorLocked = true;

        _page.Queue("lock 1");
        window.PumpEvents();
        Assert.True(window.PointerLockGranted);

        _page.Queue("lock 0");
        window.PumpEvents();
        Assert.True(window.Input.Keyboard.WasPressed(KeyCode.Escape));

        window.PumpEvents();
        Assert.False(window.Input.Keyboard.IsDown(KeyCode.Escape));
    }

    /// <summary>The game letting go of the lock itself is not a key.</summary>
    [Fact]
    public void A_lock_the_game_released_is_not_an_escape()
    {
        using WebGameWindow window = Open();
        window.CursorLocked = true;
        _page.Queue("lock 1");
        window.PumpEvents();

        window.CursorLocked = false;
        _page.Queue("lock 0");
        window.PumpEvents();

        Assert.False(window.Input.Keyboard.IsDown(KeyCode.Escape));
    }

    [Fact]
    public void The_pointer_and_the_title_are_asked_of_the_page()
    {
        using WebGameWindow window = Open();

        window.CursorVisible = true;
        window.CursorLocked = true;
        window.Title = "OpenIGI";

        Assert.Equal((true, true), _page.Pointer);
        Assert.Equal("OpenIGI", _page.Title);
    }

    [Fact]
    public void A_new_canvas_size_is_a_resize_and_the_same_one_is_not()
    {
        using WebGameWindow window = Open();
        List<(int Width, int Height)> resizes = [];
        window.Resized += (_, e) => resizes.Add((e.Width, e.Height));

        _page.Queue("resize 1280 720", "resize 1920 1080", "resize 1920 1080");
        window.PumpEvents();

        Assert.Equal([(1920, 1080)], resizes);
        Assert.Equal(1920, window.Width);
        Assert.Equal(1080, window.Height);
    }

    [Fact]
    public void Fullscreen_is_requested_only_when_the_mode_changes()
    {
        using WebGameWindow window = Open();

        window.Mode = WindowMode.BorderlessFullscreen;
        window.Mode = WindowMode.BorderlessFullscreen;
        window.Mode = WindowMode.Windowed;

        Assert.Equal([true, false], _page.FullscreenRequests);
    }

    [Fact]
    public void The_page_going_away_asks_the_game_to_close_once()
    {
        using WebGameWindow window = Open();
        int requests = 0;
        window.CloseRequested += (_, _) => requests++;

        _page.Queue("close", "close");
        window.PumpEvents();
        window.Close();

        Assert.True(window.IsClosing);
        Assert.Equal(1, requests);
    }

    private WebGameWindow Open() => new(_page, WindowSettings.Default);
}
