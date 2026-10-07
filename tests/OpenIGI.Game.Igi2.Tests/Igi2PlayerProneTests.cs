using OpenIGI.Game.Igi2.Player;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Lying flat and crawling, against <c>HumanPlayer_*StateFlat</c> and <c>*StateFlatCrawl</c>.</summary>
public sealed class Igi2PlayerProneTests
{
    /// <summary>Lying down drops the upper body twice over, raises the ground tolerance and keeps the orientation.</summary>
    [Fact]
    public void Lying_down_lies_flat()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeAnimation animation, _) = LieDown();

        Assert.Equal(Igi2PlayerState.Flat, player.Locomotion.CurrentState);
        Assert.Equal(Igi2HumanPose.LyingFlat, player.Pose);
        Assert.Equal([0.4f, 0f], animation.UpperBodyClears);
        Assert.Equal(Igi2HumanPlayer.ProneGroundTolerance, player.GroundTolerance);
        Assert.Equal(0, player.RequestedUpperBody);
    }

    /// <summary>A direction starts a crawl with its loop sound; letting go lies still again and the loop is deleted.</summary>
    [Fact]
    public void A_crawl_holds_its_loop_until_it_stops()
    {
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = LieDown();

        Tick(player, Igi2PlayerAction.MoveForwards);
        Assert.Equal(Igi2PlayerState.FlatCrawl, player.Locomotion.CurrentState);

        for (int i = 0; i < 10; i++)
        {
            Tick(player, Igi2PlayerAction.MoveForwards);
        }

        Igi2PlayerFakes.FakeSound loop = Assert.Single(surroundings.Started);
        Assert.Equal("crawl_loop", loop.Name);
        Assert.Equal(9, loop.Moves);
        Assert.Equal(136.53334f, player.GroundMovementSpeed.Y, 0.001f);

        Tick(player, Igi2PlayerAction.None);
        Assert.Equal(Igi2PlayerState.Flat, player.Locomotion.CurrentState);
        Assert.True(loop.Deleted);
        Assert.Equal(Igi2HumanPlayer.ProneGroundTolerance, player.GroundTolerance);
    }

    /// <summary>A key and its opposite cancel the whole crawl, the other axis included.</summary>
    [Fact]
    public void Opposite_keys_cancel_the_whole_crawl()
    {
        (Igi2HumanPlayer player, _, _) = LieDown();
        Tick(player, Igi2PlayerAction.MoveForwards);

        const Igi2PlayerAction both = Igi2PlayerAction.MoveForwards | Igi2PlayerAction.MoveBackwards | Igi2PlayerAction.MoveRight;
        for (int i = 0; i < 10; i++)
        {
            Tick(player, both);
        }

        Assert.Equal(Igi2PlayerState.FlatCrawl, player.Locomotion.CurrentState);
        Assert.Equal(0f, player.GroundMovementSpeed.X);
        Assert.Equal(0f, player.GroundMovementSpeed.Y);
    }

    /// <summary>Raising the stance from lying gives a toggled crouch, not a held one.</summary>
    [Fact]
    public void Raising_the_stance_crouches()
    {
        (Igi2HumanPlayer player, _, _) = LieDown();

        Tick(player, Igi2PlayerAction.RaiseStance);

        Assert.Equal(Igi2PlayerState.Crouch, player.Locomotion.CurrentState);
        Assert.False(player.CrouchIsHeld);
    }

    /// <summary>Lying flat with lying forbidden gets up to a crouch.</summary>
    [Fact]
    public void Forbidden_lying_gets_up()
    {
        (Igi2HumanPlayer player, _, _) = LieDown();
        player.AllowedStance = Igi2AllowedStance.All & ~Igi2AllowedStance.Lie;

        Tick(player, Igi2PlayerAction.None);

        Assert.Equal(Igi2PlayerState.Crouch, player.Locomotion.CurrentState);
    }

    /// <summary>A crawl into water stands up whatever else it was doing; lying flat does not check.</summary>
    [Fact]
    public void A_crawl_into_deep_water_stands()
    {
        (Igi2HumanPlayer player, _, _) = LieDown();
        Tick(player, Igi2PlayerAction.MoveForwards);
        player.WaterDepth = Igi2HumanPlayer.CrawlWaterDepthLimit;

        Tick(player, Igi2PlayerAction.MoveForwards);

        Assert.Equal(Igi2PlayerState.Stand, player.Locomotion.CurrentState);
    }

    private static (Igi2HumanPlayer Player, Igi2PlayerFakes.FakeAnimation Animation, Igi2PlayerFakes.FakeSurroundings Surroundings) LieDown()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeAnimation animation, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.Player();
        Tick(player, Igi2PlayerAction.None);
        Tick(player, Igi2PlayerAction.ToggleStandLie);
        Tick(player, Igi2PlayerAction.None);
        return (player, animation, surroundings);
    }

    private static void Tick(Igi2HumanPlayer player, Igi2PlayerAction held)
    {
        Igi2PlayerFakes.Feed(player, held);
        player.Signal |= Igi2PlayerSignal.Grounded;
        player.Locomotion.Tick(player);
    }
}
