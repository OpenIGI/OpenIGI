using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The flashlight as a thing held in a hand: its switch and its aim.</summary>
public sealed class FlashlightRigTests
{
    private static readonly Vector3f North = new(0f, 1f, 0f);
    private static readonly Vector3f East = new(1f, 0f, 0f);
    private static readonly Vector3f Up = new(0f, 0f, 1f);

    [Fact]
    public void The_switch_ramps_on_quickly_and_is_put_out_at_once()
    {
        FlashlightRig rig = new();
        Assert.False(rig.IsOn);

        Assert.True(rig.Toggle());
        Step(rig, 0.01f, North);
        Assert.InRange(rig.Level, 0.01f, 0.99f);

        Step(rig, 0.05f, North);
        Assert.Equal(1f, rig.Level);

        rig.ForceOff();
        Assert.False(rig.IsOn);
        Assert.Equal(0f, rig.Level);
    }

    [Fact]
    public void A_paused_frame_moves_nothing()
    {
        FlashlightRig rig = new();
        rig.Toggle();
        Step(rig, 0.016f, North);
        Vector3f before = rig.Aim;
        float level = rig.Level;

        Step(rig, 0f, Rotate(North, 20f));

        Assert.Equal(level, rig.Level);
        Assert.Equal(before, rig.Aim);
    }

    [Fact]
    public void The_aim_trails_a_turn_and_settles_without_overshooting()
    {
        FlashlightRig rig = new();
        Step(rig, 0.016f, North);

        Vector3f target = Rotate(North, 30f);
        float previous = Angle(rig.Aim, target);
        for (int i = 0; i < 120; i++)
        {
            Step(rig, 0.016f, target);
            float gap = Angle(rig.Aim, target);

            // Always closing, never past it.
            Assert.True(gap <= previous + 1e-4f);
            Assert.True(Angle(rig.Aim, North) <= 30f + 1e-3f);
            previous = gap;
        }

        Assert.True(previous < 0.05f);
    }

    [Fact]
    public void A_cut_is_taken_as_given_rather_than_swept_across()
    {
        FlashlightRig rig = new();
        Step(rig, 0.016f, North);

        Vector3f behind = new(0f, -1f, 0f);
        Step(rig, 0.016f, behind);

        Assert.True(Angle(rig.Aim, behind) < 0.01f);
    }

    [Fact]
    public void Walking_sways_the_beam_and_standing_barely_does()
    {
        FlashlightRig still = new();
        FlashlightRig walking = new();
        float stillMost = 0f;
        float walkingMost = 0f;

        for (int i = 0; i < 200; i++)
        {
            still.Update(0.016f, North, East, Up, 0f, 0f, 0f, 1f);
            walking.Update(0.016f, North, East, Up, 0.08f, 1f, 0f, 1f);
            stillMost = MathF.Max(stillMost, Angle(still.Aim, North));
            walkingMost = MathF.Max(walkingMost, Angle(walking.Aim, North));
        }

        Assert.True(stillMost < 0.15f);
        Assert.InRange(walkingMost, 0.3f, 1f);
    }

    private static void Step(FlashlightRig rig, float seconds, Vector3f target) =>
        rig.Update(seconds, target, East, Up, 0f, 0f, 0.06f, 0f);

    private static Vector3f Rotate(Vector3f v, float degrees)
    {
        float r = degrees * MathF.PI / 180f;
        return new Vector3f(
            (v.X * MathF.Cos(r)) - (v.Y * MathF.Sin(r)),
            (v.X * MathF.Sin(r)) + (v.Y * MathF.Cos(r)),
            v.Z);
    }

    private static float Angle(Vector3f a, Vector3f b)
    {
        float dot = Math.Clamp((a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z), -1f, 1f);
        return MathF.Acos(dot) * 180f / MathF.PI;
    }
}
