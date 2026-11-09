using OpenIGI.Input;
using OpenIGI.Presentation.Hud.Remastered.Touch;
using OpenIGI.UI.Layout;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>
/// The touch controls (<see cref="TouchControls"/>) keep each finger at what it came down on: a thumb on the stick stays the
/// stick while another presses a button, and a quick tap holds its button for the frame it lands in.
/// </summary>
public sealed class TouchControlsTests
{
    private static readonly LayoutContext Phone = new(1200f, 540f);

    private static TouchButtonSpot Spot(TouchControls controls, TouchButton button)
    {
        foreach (TouchButtonSpot spot in controls.Layout)
        {
            if (spot.Button == button)
            {
                return spot;
            }
        }

        throw new InvalidOperationException($"{button} is not laid out.");
    }

    [Fact]
    public void StickStaysWhileAnotherFingerHoldsAButton()
    {
        TouchState touch = new();
        TouchControls controls = new();
        controls.Update(touch, TouchControlMode.Play, Phone);

        touch.BeginFrame();
        touch.Down(1, 150f, 400f);
        controls.Update(touch, TouchControlMode.Play, Phone);

        touch.BeginFrame();
        touch.Move(1, 150f, 330f);
        controls.Update(touch, TouchControlMode.Play, Phone);
        Assert.True(controls.Stick.Y < -TouchControls.StickDeadZone);

        TouchButtonSpot jump = Spot(controls, TouchButton.Jump);
        touch.BeginFrame();
        touch.Down(2, jump.Centre.X, jump.Centre.Y);
        controls.Update(touch, TouchControlMode.Play, Phone);

        Assert.Contains(TouchButton.Jump, controls.Held);
        Assert.True(controls.Stick.Y < -TouchControls.StickDeadZone);
        Assert.Equal(150f, controls.StickOrigin!.Value.X);

        touch.BeginFrame();
        touch.Up(2, jump.Centre.X, jump.Centre.Y);
        controls.Update(touch, TouchControlMode.Play, Phone);
        touch.BeginFrame();
        controls.Update(touch, TouchControlMode.Play, Phone);

        Assert.DoesNotContain(TouchButton.Jump, controls.Held);
        Assert.True(controls.Stick.Y < -TouchControls.StickDeadZone);
    }

    [Fact]
    public void ATapHoldsItsButtonForTheFrameItLandsIn()
    {
        TouchState touch = new();
        TouchControls controls = new();
        controls.Update(touch, TouchControlMode.Play, Phone);
        TouchButtonSpot reload = Spot(controls, TouchButton.Reload);

        touch.BeginFrame();
        touch.Down(7, reload.Centre.X, reload.Centre.Y);
        touch.Up(7, reload.Centre.X, reload.Centre.Y);
        controls.Update(touch, TouchControlMode.Play, Phone);
        Assert.Contains(TouchButton.Reload, controls.Held);

        touch.BeginFrame();
        controls.Update(touch, TouchControlMode.Play, Phone);
        Assert.Empty(controls.Held);
    }

    [Fact]
    public void A_matchs_buttons_are_laid_out_only_where_the_match_offers_them_and_none_lands_on_another()
    {
        TouchState touch = new();
        TouchControls controls = new() { MatchButtons = TouchMatchButtons.None };
        controls.Update(touch, TouchControlMode.Play, Phone);
        TouchButton[] match = [TouchButton.Scoreboard, TouchButton.Buy, TouchButton.Team, TouchButton.Drop];
        Assert.DoesNotContain(controls.Layout, s => match.Contains(s.Button));

        controls.MatchButtons = TouchMatchButtons.Scoreboard | TouchMatchButtons.Buy | TouchMatchButtons.Team | TouchMatchButtons.Drop;
        controls.Update(touch, TouchControlMode.Play, Phone);
        foreach (TouchButton button in match)
        {
            Assert.Contains(controls.Layout, s => s.Button == button);
        }

        // No two buttons overlap, so a thumb on one never presses another.
        TouchButtonSpot[] round = [.. controls.Layout.Where(static s => !s.Card)];
        for (int a = 0; a < round.Length; a++)
        {
            for (int b = a + 1; b < round.Length; b++)
            {
                float dx = round[a].Centre.X - round[b].Centre.X, dy = round[a].Centre.Y - round[b].Centre.Y;
                Assert.True(
                    MathF.Sqrt((dx * dx) + (dy * dy)) >= round[a].Radius + round[b].Radius,
                    $"{round[a].Button} overlaps {round[b].Button}");
            }
        }

        // A tap on the shop presses it.
        TouchButtonSpot buy = Spot(controls, TouchButton.Buy);
        touch.BeginFrame();
        touch.Down(3, buy.Centre.X, buy.Centre.Y);
        controls.Update(touch, TouchControlMode.Play, Phone);
        Assert.Contains(TouchButton.Buy, controls.Held);
    }
}
