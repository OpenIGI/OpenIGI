using OpenIGI.Frontend;
using OpenIGI.Input;
using OpenIGI.UI.Screens;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>Covers the screen stack's push/pop choreography and input routing, with no GL context.</summary>
public sealed class ScreenStackTests
{
    private static LayoutContext Context() => new(1920f, 1080f);

    private static void Pump(ScreenStack stack, float seconds, InputState? input = null)
    {
        input ??= new InputState();
        for (float t = 0f; t < seconds; t += 1f / 60f)
        {
            input.BeginFrame();
            stack.Update(Context(), input, 1f / 60f);
        }
    }

    /// <summary>A screen that records lifecycle calls and can be told to request a pop.</summary>
    private sealed class ProbeScreen : Screen
    {
        public int Updates { get; private set; }

        public int Exits { get; private set; }

        protected override UiElement BuildRoot(LayoutContext context) => new Panel();

        protected override void OnUpdate(float elapsedSeconds) => Updates++;

        protected override void OnExit() => Exits++;
    }

    /// <summary>A screen that shows a hidden page from its update, the way a tab click does.</summary>
    private sealed class TabScreen : Screen
    {
        public Panel Page { get; } = new() { Width = 200f, Height = 100f, Visibility = Visibility.Collapsed };

        public bool Reveal { get; set; }

        protected override UiElement BuildRoot(LayoutContext context)
        {
            var root = new Panel();
            root.AddChild(Page);
            return root;
        }

        protected override void OnUpdate(float elapsedSeconds)
        {
            if (Reveal && Page.Visibility != Visibility.Visible)
            {
                Page.Visibility = Visibility.Visible;
                RequestLayout();
            }
        }
    }

    [Fact]
    public void A_page_shown_by_input_is_laid_out_before_the_frame_is_drawn()
    {
        var stack = new ScreenStack();
        var screen = new TabScreen();
        stack.Reset(screen, animate: false);
        Pump(stack, 0.1f);
        Assert.True(screen.Page.Bounds.IsEmpty || screen.Page.Bounds.Width == 0f);

        // The frame the page is revealed in: laid out again after the update, so the draw that follows
        // places it where it belongs rather than where it was (nowhere) a frame ago.
        screen.Reveal = true;
        var input = new InputState();
        input.BeginFrame();
        stack.Update(Context(), input, 1f / 60f);

        Assert.Equal(200f, screen.Page.Bounds.Width);
        Assert.Equal(100f, screen.Page.Bounds.Height);
        Assert.False(screen.LayoutRequested);
    }

    [Fact]
    public void Push_makes_the_new_screen_the_top()
    {
        var stack = new ScreenStack();
        var first = new ProbeScreen { Name = "first" };
        var second = new ProbeScreen { Name = "second" };

        stack.Reset(first, animate: false);
        Assert.Same(first, stack.Top);

        stack.Push(second);
        Assert.Same(second, stack.Top);
        Assert.Equal(2, stack.Count);
    }

    [Fact]
    public void Entering_screen_becomes_active_after_its_transition()
    {
        var stack = new ScreenStack();
        var screen = new ProbeScreen();
        stack.Reset(screen);

        Assert.Equal(ScreenPhase.Entering, screen.Phase);
        Pump(stack, 0.6f);
        Assert.Equal(ScreenPhase.Active, screen.Phase);
        Assert.Equal(1f, screen.Transition, 2);
    }

    [Fact]
    public void A_pushed_screen_covers_the_one_below()
    {
        var stack = new ScreenStack();
        var below = new ProbeScreen();
        var above = new ProbeScreen();
        stack.Reset(below, animate: false);
        stack.Push(above);

        Assert.Equal(ScreenPhase.Covered, below.Phase);
    }

    [Fact]
    public void Pop_uncovers_the_screen_below_and_removes_the_top_after_it_fades()
    {
        var stack = new ScreenStack();
        var below = new ProbeScreen { Name = "below" };
        var above = new ProbeScreen { Name = "above" };
        stack.Reset(below, animate: false);
        stack.Push(above, animate: false);

        Assert.True(stack.Pop());
        Assert.Equal(ScreenPhase.Active, below.Phase); // uncovered immediately
        Assert.Same(below, stack.Top);

        Pump(stack, 0.6f);
        Assert.Equal(1, stack.Count); // the exited screen has been removed
        Assert.Same(below, stack.Top);
    }

    [Fact]
    public void The_last_screen_cannot_be_popped()
    {
        var stack = new ScreenStack();
        stack.Reset(new ProbeScreen(), animate: false);
        Assert.False(stack.Pop());
        Assert.Equal(1, stack.Count);
    }

    [Fact]
    public void Reset_exits_every_existing_screen_before_replacing_the_stack()
    {
        var stack = new ScreenStack();
        var first = new ProbeScreen();
        var second = new ProbeScreen();

        stack.Reset(first, animate: false);
        stack.Push(second, animate: false);
        stack.Reset(new ProbeScreen(), animate: false);

        Assert.Equal(1, first.Exits);
        Assert.Equal(1, second.Exits);
        Assert.Equal(1, stack.Count);
    }

    [Fact]
    public void A_back_press_on_the_top_screen_pops_it()
    {
        var stack = new ScreenStack();
        stack.Reset(new ProbeScreen { Name = "root" }, animate: false);
        stack.Push(new ProbeScreen { Name = "child" }, animate: false);

        var input = new InputState();
        input.BeginFrame();
        input.Gamepad.SetButton(GamepadButton.East, true); // back
        stack.Update(Context(), input, 1f / 60f);

        // The child requested a pop; let its exit finish.
        Pump(stack, 0.6f);
        Assert.Equal(1, stack.Count);
        Assert.Equal("root", stack.Top!.Name);
    }

    [Fact]
    public void Covered_screens_still_advance_their_animation()
    {
        var stack = new ScreenStack();
        var below = new ProbeScreen();
        stack.Reset(below, animate: false);
        int before = below.Updates;
        stack.Push(new ProbeScreen(), animate: false);
        Pump(stack, 0.2f);
        Assert.True(below.Updates > before, "a covered screen should keep updating");
    }

    /// <summary>A screen holding one control, so the pointer has something to rest on.</summary>
    private sealed class TooltipScreen : Screen
    {
        public TooltipScreen(UiTheme theme) => Row = new Button(theme, "Shadows") { Tooltip = "Sun shadows." };

        public Button Row { get; }

        protected override UiElement BuildRoot(LayoutContext context)
        {
            var panel = new Panel();
            panel.AddChild(Row);
            return panel;
        }
    }

    private static UiTheme Theme()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "Barlow-Regular.ttf");
        return new UiTheme(UiFont.ForMeasurement(FontAtlasBuilder.Build(File.ReadAllBytes(path), 48f)));
    }

    // The pointer put somewhere and left there: position without motion, which is what resting is.
    private static void Rest(ScreenStack stack, InputState input, UiVec2 at, float seconds)
    {
        for (float t = 0f; t < seconds; t += 1f / 60f)
        {
            input.BeginFrame();
            input.Mouse.SetPosition(at.X, at.Y);
            stack.Update(Context(), input, 1f / 60f);
        }
    }

    [Fact]
    public void A_tooltip_waits_for_the_pointer_to_rest_and_goes_again_when_it_moves()
    {
        var stack = new ScreenStack();
        var screen = new TooltipScreen(Theme());
        stack.Reset(screen, animate: false);
        Pump(stack, 0.1f); // build and lay out, so the row has bounds to be over

        var input = new InputState();
        UiVec2 over = screen.Row.Bounds.Center;

        // Arriving is not resting: the pointer moves onto the row and the page says nothing yet.
        input.BeginFrame();
        input.Mouse.SetPosition(over.X, over.Y);
        input.Mouse.AddMotion(6f, 6f);
        stack.Update(Context(), input, 1f / 60f);
        Assert.Null(screen.TooltipControl);

        Rest(stack, input, over, 0.3f);
        Assert.Null(screen.TooltipControl); // still inside the wait

        Rest(stack, input, over, 0.4f);
        Assert.Same(screen.Row, screen.TooltipControl);

        // And any movement takes it away again, so it never trails the pointer across the page.
        input.BeginFrame();
        input.Mouse.SetPosition(over.X + 40f, over.Y);
        input.Mouse.AddMotion(40f, 0f);
        stack.Update(Context(), input, 1f / 60f);
        Assert.Null(screen.TooltipControl);
    }

    [Fact]
    public void A_control_with_nothing_to_say_never_shows_a_tooltip()
    {
        var stack = new ScreenStack();
        var screen = new TooltipScreen(Theme());
        screen.Row.Tooltip = string.Empty;
        stack.Reset(screen, animate: false);
        Pump(stack, 0.1f);

        Rest(stack, new InputState(), screen.Row.Bounds.Center, 1.5f);
        Assert.Null(screen.TooltipControl);
    }
}
