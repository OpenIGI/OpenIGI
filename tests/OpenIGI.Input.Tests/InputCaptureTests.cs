using OpenIGI.Input;
using Xunit;

namespace OpenIGI.Input.Tests;

/// <summary>
/// Pins the capture flag the developer console relies on.
/// </summary>
/// <remarks>
/// Capture is not the same thing as <see cref="InputState.Suppressed"/>. Suppression reproduces the
/// original's global disable by clearing the device; capture must <em>not</em> clear it, because on an
/// event-driven host a held key is reported once and clearing it would lose the key until it was
/// released and pressed again.
/// </remarks>
public sealed class InputCaptureTests
{
    [Fact]
    public void CapturedKeyboardReadsAsIdle()
    {
        InputState input = new();
        input.Keyboard.BeginFrame();
        input.Keyboard.SetKey(KeyCode.W, down: true);

        Assert.True(input.Keyboard.IsDown(KeyCode.W));
        Assert.True(input.Keyboard.WasPressed(KeyCode.W));

        input.Captured = true;

        Assert.False(input.Keyboard.IsDown(KeyCode.W));
        Assert.True(input.Keyboard.IsUp(KeyCode.W));
        Assert.False(input.Keyboard.WasPressed(KeyCode.W));
    }

    [Fact]
    public void CaptureLeavesTheUnderlyingStateIntact()
    {
        InputState input = new();
        input.Keyboard.BeginFrame();
        input.Keyboard.SetKey(KeyCode.W, down: true);
        input.Captured = true;

        // The next frame arrives with no new event for W, because it never went up. If capture had
        // cleared the state, the player would stop walking and never start again.
        input.BeginFrame();

        Assert.False(input.Captured);
        Assert.True(input.Keyboard.IsDown(KeyCode.W));
    }

    [Fact]
    public void CaptureHidesQueuedPressesAndCharacters()
    {
        InputState input = new();
        input.Keyboard.SetKey(KeyCode.A, down: true);
        input.Keyboard.EnqueueCharacter('a');
        input.Captured = true;

        Assert.Equal(0, input.Keyboard.PendingPressCount);
        Assert.False(input.Keyboard.TryDequeuePress(out _));
        Assert.False(input.Keyboard.TryDequeueCharacter(out _));

        input.Captured = false;

        // Hidden, not discarded: the capturing code may hand the frame back mid-frame.
        Assert.True(input.Keyboard.TryDequeuePress(out KeyCode key));
        Assert.Equal(KeyCode.A, key);
        Assert.True(input.Keyboard.TryDequeueCharacter(out char typed));
        Assert.Equal('a', typed);
    }

    [Fact]
    public void CapturedMouseReportsNoMotionOrButtonsButKeepsItsPosition()
    {
        InputState input = new();
        input.Mouse.BeginFrame();
        input.Mouse.SetPosition(320f, 240f);
        input.Mouse.AddMotion(12f, -4f);
        input.Mouse.AddWheel(1f);
        input.Mouse.SetButton(MouseButton.Right, down: true);

        input.Captured = true;

        Assert.Equal(0f, input.Mouse.DeltaX);
        Assert.Equal(0f, input.Mouse.DeltaY);
        Assert.Equal(0f, input.Mouse.WheelDelta);
        Assert.False(input.Mouse.IsDown(MouseButton.Right));

        // The shell that took the mouse is the very thing that needs to know where the pointer is.
        Assert.Equal(320f, input.Mouse.X);
        Assert.Equal(240f, input.Mouse.Y);
    }

    [Fact]
    public void BeginFrameClearsCapture()
    {
        InputState input = new() { Captured = true };

        input.BeginFrame();

        Assert.False(input.Captured);
    }

    [Fact]
    public void SuppressionStillClearsTheDeviceAsTheOriginalDoes()
    {
        InputState input = new();
        input.Keyboard.BeginFrame();
        input.Keyboard.SetKey(KeyCode.W, down: true);

        input.Suppressed = true;
        input.BeginFrame();

        // Unlike capture, suppression reproduces the original's zeroed key array, so a key held across
        // the disable comes back as a fresh press.
        Assert.False(input.Keyboard.IsDown(KeyCode.W));
    }
}
