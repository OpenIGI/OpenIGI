using Xunit;

namespace OpenIGI.Input.Tests;

public sealed class InputStateTests
{
    [Fact]
    public void MouseMotion_AccumulatesWithinAFrameAndResetsAtTheNext()
    {
        MouseState mouse = new();

        mouse.BeginFrame();
        mouse.AddMotion(3, -2);
        mouse.AddMotion(1, 5);

        Assert.Equal(4.0f, mouse.DeltaX);
        Assert.Equal(3.0f, mouse.DeltaY);

        mouse.BeginFrame();
        Assert.Equal(0.0f, mouse.DeltaX);
        Assert.Equal(0.0f, mouse.DeltaY);
    }

    [Fact]
    public void MouseButtons_ReportEdgesAgainstThePreviousFrame()
    {
        MouseState mouse = new();

        mouse.BeginFrame();
        mouse.SetButton(MouseButton.Left, down: true);
        Assert.True(mouse.WasPressed(MouseButton.Left));

        mouse.BeginFrame();
        Assert.True(mouse.IsDown(MouseButton.Left));
        Assert.False(mouse.WasPressed(MouseButton.Left));

        mouse.BeginFrame();
        mouse.SetButton(MouseButton.Left, down: false);
        Assert.True(mouse.WasReleased(MouseButton.Left));
    }

    [Fact]
    public void MousePosition_IsIndependentOfMotion()
    {
        MouseState mouse = new();
        mouse.BeginFrame();
        mouse.SetPosition(100, 200);
        mouse.AddMotion(-5, -5);

        Assert.Equal(100.0f, mouse.X);
        Assert.Equal(200.0f, mouse.Y);
        Assert.Equal(-5.0f, mouse.DeltaX);
    }

    [Fact]
    public void SuppressedInput_ReleasesKeysSoTheyComeBackAsFreshPresses()
    {
        InputState input = new();

        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.W, down: true);
        Assert.True(input.Keyboard.IsDown(KeyCode.W));

        input.Suppressed = true;
        input.BeginFrame();
        Assert.False(input.Keyboard.IsDown(KeyCode.W));
        Assert.True(input.Keyboard.WasReleased(KeyCode.W));

        input.Suppressed = false;
        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.W, down: true);
        Assert.True(input.Keyboard.WasPressed(KeyCode.W));
    }

    [Fact]
    public void Bindings_ResolveAnyBoundKey()
    {
        InputBindings bindings = new();
        bindings.Bind("Forward", KeyCode.W);
        bindings.Bind("Forward", KeyCode.Up);

        KeyboardState keyboard = new();
        keyboard.BeginFrame();
        keyboard.SetKey(KeyCode.Up, down: true);

        Assert.True(bindings.IsDown(keyboard, "Forward"));
        Assert.True(bindings.WasPressed(keyboard, "forward"));
        Assert.False(bindings.IsDown(keyboard, "Back"));
        Assert.Equal(2, bindings.GetKeys("Forward").Count);
    }

    [Fact]
    public void Bindings_IgnoreDuplicateBindsAndSupportClearing()
    {
        InputBindings bindings = new();
        bindings.Bind("Fire", KeyCode.LeftControl);
        bindings.Bind("Fire", KeyCode.LeftControl);

        Assert.Single(bindings.GetKeys("Fire"));
        Assert.True(bindings.Clear("Fire"));
        Assert.Empty(bindings.GetKeys("Fire"));
        Assert.False(bindings.Clear("Fire"));
    }
}
