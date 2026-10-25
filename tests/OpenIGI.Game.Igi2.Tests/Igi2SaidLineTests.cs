using OpenIGI.Game.Igi2.Flow;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// A line the game says at run time (<c>StatusMessage_New</c>, <c>igi2 0x50FEB0</c>): forced on the screen and shown once, it
/// is printed and then gone, on whichever clock runs it.
/// </summary>
public sealed class Igi2SaidLineTests
{
    [Fact]
    public void A_forced_line_is_printed_at_once_and_then_is_done()
    {
        Igi2StatusMessageScreen screen = new();
        Igi2StatusMessageTask line = new()
        {
            Text = "PICKEDUPWEAPONAK47",
            IsForceSend = true,
            IsSendOnce = true,
            Screen = screen,
        };
        line.InitText();

        // Its first tick puts it on the screen, with no script asked whether to send it.
        Assert.Equal(StatusMessageStep.Running, line.Step(0, static () => throw new InvalidOperationException("a forced line is not asked")));
        Assert.True(line.IsSent);
        Assert.Contains(screen.Slots, static s => s.IsUsed && s.Text == "PICKEDUPWEAPONAK47");

        // The screen prints it a letter at a time; once it is done printing and the line's time is out, the line is gone.
        StatusMessageStep step = StatusMessageStep.Running;
        int tick = 1;
        for (; tick < 2000 && step != StatusMessageStep.Done; tick++)
        {
            screen.Run();
            step = line.Step(tick, static () => false);
        }

        Assert.Equal(StatusMessageStep.Done, step);
        Assert.DoesNotContain(screen.Slots, static s => s.IsUsed);
        Assert.InRange(tick, 60, 1000);
    }
}
