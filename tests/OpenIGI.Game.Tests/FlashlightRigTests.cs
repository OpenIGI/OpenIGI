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
    public void Nothing_lights_until_the_latch_snaps_then_it_settles_to_full()
    {
        FlashlightRig rig = new() { ContactWear = 0f };
        Assert.False(rig.IsOn);

        Assert.True(rig.Toggle());
        Step(rig, FlashlightRig.OnLatchSeconds * 0.9f, North);
        Assert.Equal(0f, rig.Level);

        Step(rig, 0.03f, North);
        Assert.InRange(rig.Level, 0.3f, 1.1f);

        // A touch bright while it is cold, never a flash.
        float highest = rig.Level;
        for (int i = 0; i < 60; i++)
        {
            Step(rig, 0.016f, North);
            highest = MathF.Max(highest, rig.Level);
        }

        Assert.InRange(highest, 1f, 1.07f);
        Assert.Equal(1f, rig.Level);

        rig.ForceOff();
        Assert.False(rig.IsOn);
        Assert.Equal(0f, rig.Level);
    }

    [Fact]
    public void Switching_off_drops_most_of_the_light_at_once_and_bleeds_the_rest_away()
    {
        FlashlightRig rig = new() { ContactWear = 0f };
        rig.Toggle();
        for (int i = 0; i < 80; i++)
        {
            Step(rig, 0.016f, North);
        }

        Assert.False(rig.Toggle());
        Step(rig, FlashlightRig.OffLatchSeconds * 0.9f, North);
        Assert.Equal(1f, rig.Level, 3);

        // A frame straddling the release still holds some of the full light; the one after is the glow.
        Step(rig, 0.05f, North);
        Step(rig, 0.016f, North);
        Assert.InRange(rig.Level, 0.01f, 0.2f);

        for (int i = 0; i < 40; i++)
        {
            Step(rig, 0.016f, North);
        }

        Assert.Equal(0f, rig.Level);
    }

    [Fact]
    public void A_worn_switch_blips_dark_before_it_catches()
    {
        FlashlightRig rig = new() { ContactWear = 1f };
        rig.Toggle();

        bool lit = false;
        bool darkAfterLit = false;
        for (int i = 0; i < 200; i++)
        {
            Step(rig, 0.002f, North);
            lit |= rig.Level > 0.3f;
            darkAfterLit |= lit && rig.Level < 0.05f;
        }

        Assert.True(darkAfterLit);
        for (int i = 0; i < 60; i++)
        {
            Step(rig, 0.016f, North);
        }

        Assert.Equal(1f, rig.Level);
    }

    [Fact]
    public void The_light_is_the_same_whatever_the_frame_rate()
    {
        static float Energy(float frame)
        {
            FlashlightRig rig = new() { ContactWear = 1f };
            rig.Toggle();
            float sum = 0f;
            for (float t = 0f; t < 0.4f; t += frame)
            {
                Step(rig, frame, North);
                sum += rig.Level * frame;
            }

            return sum;
        }

        // The light given out over the switch-on, as a frame's exposure adds it up: a blip is seen whether
        // the frames are thirty milliseconds apart or four.
        Assert.Equal(Energy(1f / 240f), Energy(1f / 30f), 1);
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

    // atan2 of the cross and the dot rather than acos of the dot: acos near one moves in steps of a
    // fiftieth of a degree in single precision, which reads as the aim jumping back once it has converged.
    private static float Angle(Vector3f a, Vector3f b)
    {
        float dot = (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
        float cx = (a.Y * b.Z) - (a.Z * b.Y);
        float cy = (a.Z * b.X) - (a.X * b.Z);
        float cz = (a.X * b.Y) - (a.Y * b.X);
        return MathF.Atan2(MathF.Sqrt((cx * cx) + (cy * cy) + (cz * cz)), dot) * 180f / MathF.PI;
    }
}
