using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Turning the body, <c>Human_UpdateBody</c>, and aiming the view, <c>HumanView_UpdateView</c>.</summary>
public sealed class Igi2HumanTurnTests
{
    /// <summary>The turn is minus π times the input times the field of view, added to the heading.</summary>
    [Fact]
    public void The_turn_input_turns_the_body()
    {
        (Igi2HumanPlayer player, _, _) = Igi2PlayerFakes.Player();
        Look(player, x: 0.01f, y: 0f);

        player.UpdateBody();

        float turn = (-((Fpu)0.01f * 3.1415927f) * 1f).F;
        Assert.Equal(turn, player.TurningSpeed);
        Assert.Equal(turn, player.IdealGamma);
        Assert.Equal(turn, player.Gamma);
        Assert.InRange(player.Orientation.M11, MathF.Cos(turn) - 1e-6f, MathF.Cos(turn) + 1e-6f);
        Assert.InRange(player.Orientation.M21, MathF.Sin(turn) - 1e-6f, MathF.Sin(turn) + 1e-6f);
    }

    /// <summary>A zoomed view turns the body slower, by the field of view.</summary>
    [Fact]
    public void A_narrow_view_turns_slower()
    {
        (Igi2HumanPlayer player, _, _) = Igi2PlayerFakes.Player();
        player.View.FieldOfView = 0.25f;
        Look(player, x: 0.01f, y: 0f);

        player.UpdateBody();

        Assert.Equal((-((Fpu)0.01f * 3.1415927f) * 0.25f).F, player.TurningSpeed);
    }

    /// <summary>A locked body does not turn.</summary>
    [Fact]
    public void A_locked_body_does_not_turn()
    {
        (Igi2HumanPlayer player, _, _) = Igi2PlayerFakes.Player();
        player.Locomotion.Enter(Igi2PlayerState.Die, player);
        Look(player, x: 0.5f, y: 0f);
        float before = player.IdealGamma;

        player.UpdateBody();

        Assert.Equal(before, player.IdealGamma);
        Assert.Equal(0f, player.TurningSpeed);
    }

    /// <summary>
    /// Lying flat, a turn that would swing the head into a wall is refused whole (<c>Human_IsGammaRotationAllowed</c>), and pushes
    /// the body off the wall by the size of the turn times 81.92; with open ground around, the same turn is taken.
    /// </summary>
    [Fact]
    public void Lying_flat_a_wall_refuses_the_turn()
    {
        (Igi2HumanPlayer open, _, _) = Igi2PlayerFakes.Player();
        open.Pose = Igi2HumanPose.LyingFlat;
        Look(open, x: -0.05f, y: 0f);
        open.UpdateBody();
        Assert.NotEqual(0f, open.TurningSpeed);

        (Igi2HumanPlayer boxed, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.Player();
        boxed.Pose = Igi2HumanPose.LyingFlat;
        surroundings.World.Others.Add(new Igi2PlayerFakes.FakeWall(100.0));
        float before = boxed.IdealGamma;
        Look(boxed, x: -0.05f, y: 0f);

        boxed.UpdateBody();

        Assert.Equal(0f, boxed.TurningSpeed);
        Assert.Equal(before, boxed.IdealGamma);
        float size = Math.Abs(open.TurningSpeed);
        Assert.Equal(((Fpu)size * -1f * 81.91999816894531f).F, boxed.DeltaTranslation.X);
    }

    /// <summary>At the default field of view the pitch input is averaged over two ticks; the step is π times it.</summary>
    [Fact]
    public void The_pitch_averages_two_ticks_of_input()
    {
        Igi2HumanView view = new();
        Igi2HumanChannels channels = new();
        Frame(channels, y: 0.1f);
        Frame(channels, y: 0.3f);

        view.UpdateView(channels);

        float expected = (((Fpu)3.1415927f * (((Fpu)0.3f + 0.1f) * 0.5)) * 1f).F;
        Assert.Equal(expected, view.IdealPitch);
        Assert.Equal(expected, view.Pitch);
    }

    /// <summary>Small input is eased: divided by two less its size times 16⅔.</summary>
    [Fact]
    public void Small_pitch_input_is_eased()
    {
        Igi2HumanView view = new();
        Igi2HumanChannels channels = new();
        Frame(channels, y: 0.03f);
        Frame(channels, y: 0.03f);

        view.UpdateView(channels);

        Assert.InRange(view.IdealPitch, 0.0628f, 0.0629f);
    }

    /// <summary>The pitch is held within 85 degrees.</summary>
    [Fact]
    public void The_pitch_stops_at_85_degrees()
    {
        Igi2HumanView view = new();
        Igi2HumanChannels channels = new();
        for (int i = 0; i < 20; i++)
        {
            Frame(channels, y: 1f);
            view.UpdateView(channels);
        }

        Assert.Equal(Igi2HumanView.PitchLimit, view.IdealPitch);
        Assert.Equal(Igi2HumanView.PitchLimit, view.Pitch);
    }

    /// <summary>The field-of-view buttons narrow it by four percent and put it back.</summary>
    [Fact]
    public void The_field_of_view_buttons_narrow_and_reset_it()
    {
        Igi2HumanView view = new();
        Igi2HumanChannels channels = new();
        Frame(channels, y: 0f, button: Igi2HumanChannel.FieldOfViewNarrow);
        view.UpdateView(channels);
        Assert.Equal(0.96f, view.FieldOfView);

        Frame(channels, y: 0f, button: Igi2HumanChannel.FieldOfViewReset);
        view.UpdateView(channels);
        Assert.Equal(1f, view.FieldOfView);
    }

    private static void Look(Igi2HumanPlayer player, float x, float y)
    {
        float[] frame = new float[Igi2HumanChannels.Count];
        frame[(int)Igi2HumanChannel.LookX] = x;
        frame[(int)Igi2HumanChannel.LookY] = y;
        player.Channels.Accept(frame);
    }

    private static void Frame(Igi2HumanChannels channels, float y, Igi2HumanChannel? button = null)
    {
        float[] frame = new float[Igi2HumanChannels.Count];
        frame[(int)Igi2HumanChannel.LookY] = y;
        if (button is { } held)
        {
            frame[(int)held] = 1f;
        }

        channels.Accept(frame);
    }
}
