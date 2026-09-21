using OpenIGI.Desktop;
using OpenIGI.Host.Menu;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// The going-in sequence, held to what would show if it were wrong: the load must not run before the
/// interface has left, the card must not be up over the live level, and the picture must be fully off
/// by the end.
/// </summary>
public sealed class MissionLoadSequenceTests
{
    private const float Frame = 1f / 60f;

    [Fact]
    public void TheCleanPictureIsWantedFirstAndTheLoadOnlyAfterTheDeparture()
    {
        var sequence = new MissionLoadSequence();
        sequence.Begin(capturePicture: true);

        Assert.True(sequence.WantsCapture);
        Assert.False(sequence.WantsLoad);

        sequence.NotifyCaptured();
        Assert.False(sequence.WantsCapture);
        Assert.True(sequence.HasPicture);

        // Not until the interface has gone and the picture is out of focus.
        int frames = 0;
        while (!sequence.WantsLoad)
        {
            sequence.Update(Frame, ready: false);
            frames++;
            Assert.True(frames < 120, "the load was never asked for");
        }

        Assert.True(frames * Frame >= MissionLoadSequence.DepartSeconds - Frame);
        Assert.Equal(0f, sequence.ShellOpacity, 3);
        Assert.Equal(1f, sequence.Blur, 3);
    }

    [Fact]
    public void ItHoldsUntilTheLevelIsReadyAndThenRevealsAndEnds()
    {
        var sequence = new MissionLoadSequence();
        sequence.Begin(capturePicture: true);
        sequence.NotifyCaptured();

        Run(sequence, ready: false, seconds: MissionLoadSequence.DepartSeconds + 0.1f);
        sequence.NotifyLoaded();
        sequence.Update(Frame, ready: false);
        Assert.Equal(MissionLoadPhase.Hold, sequence.Phase);

        // Waits as long as it takes.
        Run(sequence, ready: false, seconds: 5f);
        Assert.Equal(MissionLoadPhase.Hold, sequence.Phase);
        Assert.Equal(1f, sequence.Opacity, 3);
        Assert.Equal(1f, sequence.Card, 3);

        sequence.Update(Frame, ready: true);
        Assert.Equal(MissionLoadPhase.Reveal, sequence.Phase);

        // The card holds a beat, complete, then is gone before the picture is: nothing is captioned through.
        Run(sequence, ready: true, seconds: MissionLoadSequence.CardOutDelaySeconds * 0.5f);
        Assert.Equal(1f, sequence.Card, 3);
        Run(sequence, ready: true, seconds: (MissionLoadSequence.CardOutDelaySeconds * 0.5f) + MissionLoadSequence.CardOutSeconds + Frame);
        Assert.Equal(0f, sequence.Card, 3);
        Assert.True(sequence.Opacity > 0.1f);

        Run(sequence, ready: true, seconds: MissionLoadSequence.RevealSeconds);
        Assert.Equal(MissionLoadPhase.Idle, sequence.Phase);
        Assert.False(sequence.IsActive);
        Assert.Equal(0f, sequence.Opacity, 3);
    }

    [Fact]
    public void ALevelWithNothingOnScreenSkipsTheCapture()
    {
        var sequence = new MissionLoadSequence();
        sequence.Begin(capturePicture: false);

        Assert.False(sequence.WantsCapture);
        Assert.False(sequence.HasPicture);
    }

    [Fact]
    public void EveryAmountStaysInRange()
    {
        var sequence = new MissionLoadSequence();
        sequence.Begin(capturePicture: true);
        sequence.NotifyCaptured();

        for (int i = 0; i < 60 * 8; i++)
        {
            if (sequence.WantsLoad)
            {
                sequence.NotifyLoaded();
            }

            sequence.Update(Frame, ready: i > 60 * 5);
            Assert.InRange(sequence.Blur, 0f, 1f);
            Assert.InRange(sequence.Opacity, 0f, 1f);
            Assert.InRange(sequence.Card, 0f, 1f);
            Assert.InRange(sequence.Letterbox, 0f, 1f);
            Assert.InRange(sequence.ShellOpacity, 0f, 1f);
            Assert.InRange(sequence.Zoom, 1f, 1.06f);
        }

        Assert.Equal(MissionLoadPhase.Idle, sequence.Phase);
    }

    [Fact]
    public void CancellingClearsItAtOnce()
    {
        var sequence = new MissionLoadSequence();
        sequence.Begin(capturePicture: true);
        sequence.Cancel();

        Assert.False(sequence.IsActive);
        Assert.Equal(1f, sequence.ShellOpacity, 3);
        Assert.Equal(0f, sequence.Opacity, 3);
    }

    private static void Run(MissionLoadSequence sequence, bool ready, float seconds)
    {
        for (float t = 0f; t < seconds; t += Frame)
        {
            sequence.Update(Frame, ready);
        }
    }
}
