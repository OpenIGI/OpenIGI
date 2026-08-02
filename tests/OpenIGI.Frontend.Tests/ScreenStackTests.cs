using OpenIGI.Frontend;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
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

        protected override UiElement BuildRoot(LayoutContext context) => new Panel();

        protected override void OnUpdate(float elapsedSeconds) => Updates++;
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
}
