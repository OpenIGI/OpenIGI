using OpenIGI.Input;
using Xunit;

namespace OpenIGI.Input.Tests;

/// <summary>
/// The keyboard's press queue — the original's <c>Input_HasEvent</c>/<c>Input_NextEvent</c> pair.
/// </summary>
/// <remarks>
/// The queue exists because a consumer can run slower than the host's event dispatch. The menu is
/// exactly that: the window dispatches key events at the display's rate and the manager reads them
/// from a task update at 30 Hz. Edge detection against a per-frame shadow loses those presses, which
/// is what made the arrow keys and enter appear dead in the shell.
/// </remarks>
public sealed class KeyboardPressQueueTests
{
    [Fact]
    public void APressSurvivesFramesThatNobodyReads()
    {
        KeyboardState keyboard = new();

        keyboard.SetKey(KeyCode.Return, down: true);
        keyboard.SetKey(KeyCode.Return, down: false);

        // Three frames pass with no reader — the shadow is taken each time.
        keyboard.BeginFrame();
        keyboard.BeginFrame();
        keyboard.BeginFrame();

        Assert.False(keyboard.WasPressed(KeyCode.Return));
        Assert.True(keyboard.TryDequeuePress(out KeyCode key));
        Assert.Equal(KeyCode.Return, key);
    }

    [Fact]
    public void PressesComeBackInTheOrderTheyArrived()
    {
        KeyboardState keyboard = new();

        keyboard.SetKey(KeyCode.Down, down: true);
        keyboard.SetKey(KeyCode.Down, down: false);
        keyboard.SetKey(KeyCode.Return, down: true);

        Assert.True(keyboard.TryDequeuePress(out KeyCode first));
        Assert.True(keyboard.TryDequeuePress(out KeyCode second));
        Assert.False(keyboard.TryDequeuePress(out _));

        Assert.Equal(KeyCode.Down, first);
        Assert.Equal(KeyCode.Return, second);
    }

    [Fact]
    public void HoldingAKeyQueuesItOnce()
    {
        KeyboardState keyboard = new();

        keyboard.SetKey(KeyCode.Right, down: true);
        keyboard.SetKey(KeyCode.Right, down: true);
        keyboard.SetKey(KeyCode.Right, down: true);

        Assert.Equal(1, keyboard.PendingPressCount);
    }

    [Fact]
    public void ThePolledPathQueuesItsRisingEdgesToo()
    {
        KeyboardState keyboard = new();
        byte[] frame = new byte[KeyboardState.KeyCount];

        frame[(byte)KeyCode.Up] = KeyboardState.DownValue;
        keyboard.Update(frame);
        keyboard.Update(frame);

        Assert.Equal(1, keyboard.PendingPressCount);
    }

    [Fact]
    public void TheQueueStopsGrowingAtItsCapacity()
    {
        KeyboardState keyboard = new();

        for (int i = 0; i < KeyboardState.PressQueueCapacity * 2; i++)
        {
            keyboard.SetKey(KeyCode.Space, down: true);
            keyboard.SetKey(KeyCode.Space, down: false);
        }

        Assert.Equal(KeyboardState.PressQueueCapacity, keyboard.PendingPressCount);
    }

    [Fact]
    public void LosingFocusDropsWhatWasWaiting()
    {
        KeyboardState keyboard = new();

        keyboard.SetKey(KeyCode.Escape, down: true);
        keyboard.Release();

        Assert.Equal(0, keyboard.PendingPressCount);
    }
}
