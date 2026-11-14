using OpenIGI.Core.Numerics;
using OpenIGI.Game.Objects;
using Xunit;

namespace OpenIGI.Game.Tests.Objects;

/// <summary>How a rotating object turns in both games (Project I.G.I. <c>0x439090</c>, I.G.I. 2 <c>RotatingObject_RunHandler</c>).</summary>
public sealed class RotationRuleTests
{
    [Fact]
    public void A_speed_steps_toward_its_target_by_its_acceleration_over_a_tick_and_stops_on_it()
    {
        Assert.Equal(1f, RotationRule.StepSpeed(0f, 90f, 30f), 4);
        Assert.Equal(89f, RotationRule.StepSpeed(90f, 0f, 30f), 4);
        Assert.Equal(90f, RotationRule.StepSpeed(89.5f, 90f, 30f));
        Assert.Equal(0f, RotationRule.StepSpeed(0.5f, 0f, 30f));
        Assert.Equal(45f, RotationRule.StepSpeed(45f, 45f, 30f));
    }

    [Fact]
    public void An_axis_with_no_acceleration_never_changes_speed()
    {
        Assert.Equal(90f, RotationRule.StepSpeed(90f, 0f, 0f));
        Assert.Equal(0f, RotationRule.StepSpeed(0f, 90f, 0f));
    }

    [Fact]
    public void An_angle_gains_its_speed_over_a_tick_and_wraps_from_above_only()
    {
        Assert.Equal(3f, RotationRule.StepAngle(0f, 90f), 4);
        Assert.Equal(1f, RotationRule.StepAngle(358f, 90f), 4);
        Assert.Equal(-3f, RotationRule.StepAngle(0f, -90f), 4);
    }

    [Fact]
    public void At_rest_it_faces_as_its_level_placed_it()
    {
        Matrix3f placed = Matrix3f.RotationZ(0.7f);

        Matrix3f faced = RotationRule.Orientation(placed, 0f, 0f, 0f);

        Assert.Equal(placed.M11, faced.M11, 6);
        Assert.Equal(placed.M12, faced.M12, 6);
        Assert.Equal(placed.M33, faced.M33, 6);
    }

    [Fact]
    public void Its_sound_is_played_with_its_speed_over_its_authored_speed_held_to_nought_to_one()
    {
        Assert.Equal(0.5f, RotationRule.SoundLevel(0f, 0f, 45f, 1f, 1f, 90f), 5);
        Assert.Equal(1f, RotationRule.SoundLevel(90f, 90f, 90f, 90f, 90f, 90f));
        Assert.Equal(0f, RotationRule.SoundLevel(0f, 0f, -45f, 1f, 1f, 90f));
    }

    [Fact]
    public void An_axis_authored_with_no_speed_makes_the_level_nought()
    {
        // 0 / 0 is no number at all, and the sum with it is none either.
        Assert.Equal(0f, RotationRule.SoundLevel(0f, 0f, 45f, 0f, 0f, 90f));
    }
}
