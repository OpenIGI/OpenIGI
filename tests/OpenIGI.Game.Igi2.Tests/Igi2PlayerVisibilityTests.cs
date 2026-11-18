using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>How visible a shot leaves the player: lit for fifteen ticks, then back to the light it stands in.</summary>
public sealed class Igi2PlayerVisibilityTests
{
    /// <summary>
    /// A shot keeps the player visible whatever the light for fifteen ticks, counted down by the human's own tick
    /// (<c>Human_RunHandler</c>): before the player ran it, the hold never wore off, and after the first shot every guard saw
    /// the player at his full view length -- came out of his barracks and fired from far off for the rest of the mission.
    /// </summary>
    [Fact]
    public void A_shots_hold_on_the_players_visibility_wears_off_after_fifteen_ticks()
    {
        // Two players alike, one of whom fires; both stand in the same light, ticked alike.
        (Player.Igi2HumanPlayer shooter, _, _) = Igi2PlayerFakes.OnFloor();
        (Player.Igi2HumanPlayer still, _, _) = Igi2PlayerFakes.OnFloor();
        Tick();

        shooter.IgnoreLightOnVisibility(15);
        Tick();
        Assert.True(shooter.VisibilityFactor > still.VisibilityFactor, $"lit {shooter.VisibilityFactor}, unlit {still.VisibilityFactor}");

        for (int i = 0; i < 15; i++)
        {
            Tick();
        }

        Assert.Equal(0, shooter.IgnoreLightEffectOnVisibilityTimer);
        Assert.Equal(still.VisibilityFactor, shooter.VisibilityFactor);

        // A tick of each as the level runs it: the update, then the delayed half, whose human tick counts the hold down.
        void Tick()
        {
            foreach (Player.Igi2HumanPlayer player in new[] { shooter, still })
            {
                player.Update();
                player.RunDelayed();
            }
        }
    }
}
