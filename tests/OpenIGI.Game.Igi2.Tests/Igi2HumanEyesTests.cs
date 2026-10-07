using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Placing the view on the body, <c>HumanView_UpdateBody</c>, and the lying player's view limits, <c>HumanPlayer_CheckViewCollision</c>.</summary>
public sealed class Igi2HumanEyesTests
{
    private static readonly Vector3d Feet = new(1000.0, -2000.0, 50000.0);

    /// <summary>
    /// The eyes stand above the feet by the posture's height, looking along the body's heading plus the view's own turn.
    /// The height is added on the 24-bit FPU, so at a Z of 50000 standing's 6922.24 lands on 56922.23828125.
    /// </summary>
    [Theory]
    [InlineData(Igi2HumanPose.Standing, 56922.23828125)]
    [InlineData(Igi2HumanPose.Crouching, 54096.0)]
    [InlineData(Igi2HumanPose.LyingFlat, 51228.796875)]
    public void The_eyes_stand_above_the_feet_by_the_posture(Igi2HumanPose pose, double eyes)
    {
        (Igi2HumanPlayer player, _, _) = Igi2PlayerFakes.Player();
        player.Position = Feet;
        player.Pose = pose;
        player.Gamma = 0.5f;
        player.View.Pitch = 0.25f;

        player.UpdateViewBody();

        Assert.Equal(new Vector3d(Feet.X, Feet.Y, eyes), player.View.Position);
        float[] expected = Igi2Pose.Rotation(0.25f, 0f, 0.5f);
        Assert.Equal(expected[0], player.View.Orientation.M11);
        Assert.Equal(expected[5], player.View.Orientation.M23);
        Assert.Equal(expected[7], player.View.Orientation.M32);
    }

    /// <summary>A landing dips the eyes along a half sine over fifteen ticks, then the dip is over.</summary>
    [Fact]
    public void A_landing_dips_the_eyes_along_a_half_sine()
    {
        (Igi2HumanPlayer player, _, _) = Igi2PlayerFakes.Player();
        player.Position = Feet;
        player.View.ImpactOffset = -1024f;
        double standing = Igi2Human.EyesAt(Feet, Igi2HumanPose.Standing).Z;

        player.UpdateViewBody();
        Assert.Equal(standing, player.View.Position.Z);

        player.UpdateViewBody();
        Assert.InRange(player.View.Position.Z, standing - 213.0, standing - 212.0);

        for (int tick = 2; tick < 15; tick++)
        {
            player.UpdateViewBody();
        }

        Assert.Equal(0f, player.View.ImpactOffset);
    }

    /// <summary>A lean that would put the eyes through a ceiling stops them 204.8 short of it.</summary>
    [Fact]
    public void A_lean_stops_short_of_what_is_in_the_way()
    {
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.Player();
        player.Position = Feet;
        double eyes = Igi2Human.EyesAt(Feet, Igi2HumanPose.Standing).Z;
        surroundings.World.Surfaces.Add(new Igi2PlayerFakes.FakeCollider(eyes + 1000.0, facesUp: false));
        player.View.PeekOffset = new Vector3f(0f, 0f, 3000f);

        player.UpdateViewBody();

        Assert.InRange(player.View.Position.Z, eyes + 795.0, eyes + 795.4);
    }

    /// <summary>Lying flat, ground rising ahead keeps the view from looking down into it.</summary>
    [Fact]
    public void Lying_flat_the_ground_ahead_limits_the_view_down()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeSurroundings surroundings) = Lying();
        double eyes = player.View.Position.Z;
        surroundings.World.Surfaces.Add(new Igi2PlayerFakes.FakeCollider(eyes - 1024.0));
        player.View.IdealPitch = -1f;

        player.CheckViewCollision();

        Assert.Equal((-Fpu.Atan2(0.5, 1.0)).F, player.View.IdealPitch);
    }

    /// <summary>Lying flat under something low, the view cannot look up into it.</summary>
    [Fact]
    public void Lying_flat_a_ceiling_ahead_limits_the_view_up()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeSurroundings surroundings) = Lying();
        double eyes = player.View.Position.Z;
        surroundings.World.Surfaces.Add(new Igi2PlayerFakes.FakeCollider(eyes + 1024.0, facesUp: false));
        player.View.IdealPitch = 1f;

        player.CheckViewCollision();

        Assert.Equal(Fpu.Atan2(0.5, 1.0).F, player.View.IdealPitch);
    }

    /// <summary>Standing, the view is not limited.</summary>
    [Fact]
    public void Standing_the_view_is_not_limited()
    {
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.Player();
        player.UpdateViewBody();
        surroundings.World.Surfaces.Add(new Igi2PlayerFakes.FakeCollider(player.View.Position.Z - 1024.0));
        player.View.IdealPitch = -1f;

        player.CheckViewCollision();

        Assert.Equal(-1f, player.View.IdealPitch);
    }

    private static (Igi2HumanPlayer Player, Igi2PlayerFakes.FakeSurroundings Surroundings) Lying()
    {
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.Player();
        player.Position = Feet;
        player.Pose = Igi2HumanPose.LyingFlat;
        player.UpdateViewBody();
        return (player, surroundings);
    }
}
