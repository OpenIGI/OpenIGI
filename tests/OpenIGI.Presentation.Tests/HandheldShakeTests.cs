using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Cameras;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>
/// The handheld wander, held to the two properties that make it usable in a shot: it never grows beyond
/// what was declared, and it is exactly nothing at the ends.
/// </summary>
public sealed class HandheldShakeTests
{
    [Fact]
    public void TheSameMomentAlwaysGivesTheSameWander()
    {
        // Deterministic and seeded, not time-seeded, so a -lockstep capture of the entry can be compared
        // frame for frame against another run of the same command line.
        var first = new HandheldShake();
        var second = new HandheldShake();

        for (float t = 0f; t < 6f; t += 0.031f)
        {
            first.Sample(t, 1f, out float ay, out float ap, out float ar);
            second.Sample(t, 1f, out float by, out float bp, out float br);

            Assert.Equal(ay, by);
            Assert.Equal(ap, bp);
            Assert.Equal(ar, br);
        }
    }

    [Fact]
    public void NoAxisEverExceedsWhatItDeclares()
    {
        var shake = new HandheldShake();

        for (float t = 0f; t < 120f; t += 0.0037f)
        {
            shake.Sample(t, 1f, out float yaw, out float pitch, out float roll);

            Assert.True(MathF.Abs(yaw) <= HandheldShake.MaximumYaw, $"yaw {yaw} at {t}");
            Assert.True(MathF.Abs(pitch) <= HandheldShake.MaximumPitch, $"pitch {pitch} at {t}");
            Assert.True(MathF.Abs(roll) <= HandheldShake.MaximumRoll, $"roll {roll} at {t}");
        }
    }

    [Fact]
    public void AZeroWeightIsExactlyStillRatherThanNearlyStill()
    {
        // The hand-over at the end of the entry depends on this being exact: a fraction of a degree of
        // residual wander on the last cinematic frame is a visible snap when the player takes over.
        var shake = new HandheldShake();

        for (float t = 0f; t < 5f; t += 0.05f)
        {
            shake.Sample(t, 0f, out float yaw, out float pitch, out float roll);

            Assert.Equal(0f, yaw);
            Assert.Equal(0f, pitch);
            Assert.Equal(0f, roll);
        }
    }

    [Fact]
    public void TheWanderIsSmoothRatherThanSteppedBetweenFrames()
    {
        // Value noise interpolated linearly has a corner at every lattice point, and a camera that
        // changes direction on a corner reads as a mechanism rather than as a hand. Smoothing the
        // interpolant is what removes it, and this is the property that would notice if it were dropped:
        // over a frame at 60 Hz no axis may move more than a small fraction of its own range.
        var shake = new HandheldShake();
        const float frame = 1f / 60f;

        shake.Sample(0f, 1f, out float previousYaw, out float previousPitch, out float previousRoll);
        for (float t = frame; t < 30f; t += frame)
        {
            shake.Sample(t, 1f, out float yaw, out float pitch, out float roll);

            Assert.True(MathF.Abs(yaw - previousYaw) < HandheldShake.MaximumYaw * 0.2f);
            Assert.True(MathF.Abs(pitch - previousPitch) < HandheldShake.MaximumPitch * 0.2f);
            Assert.True(MathF.Abs(roll - previousRoll) < HandheldShake.MaximumRoll * 0.2f);

            previousYaw = yaw;
            previousPitch = pitch;
            previousRoll = roll;
        }
    }

    [Fact]
    public void ARollOfExactlyZeroReturnsTheOrientationUntouched()
    {
        // Not an optimisation. It is what makes the shot's final frame bit-identical to the pose it was
        // aiming at, rather than that pose multiplied by an identity built out of a cosine and a sine.
        Matrix3f look = Camera3D.LookRotation(0.7f, -0.3f);
        Matrix3f rolled = HandheldShake.ApplyRoll(look, 0f);

        Assert.Equal(look.Row1, rolled.Row1);
        Assert.Equal(look.Row2, rolled.Row2);
        Assert.Equal(look.Row3, rolled.Row3);
    }

    [Fact]
    public void RollingKeepsTheOrientationAProperRotation()
    {
        // A roll mixes two of the rows. If it mixed them wrongly the frame would stop being orthonormal,
        // and every normal the renderer transforms through it would be quietly skewed.
        Matrix3f look = Camera3D.LookRotation(1.1f, -1.4f);

        foreach (float roll in new[] { 0.003f, -0.003f, 0.25f, -0.25f })
        {
            Matrix3f rolled = HandheldShake.ApplyRoll(look, roll);

            Assert.Equal(1f, Length(rolled.Row1), 5);
            Assert.Equal(1f, Length(rolled.Row2), 5);
            Assert.Equal(1f, Length(rolled.Row3), 5);
            Assert.Equal(0f, Dot(rolled.Row1, rolled.Row2), 5);
            Assert.Equal(0f, Dot(rolled.Row1, rolled.Row3), 5);
            Assert.Equal(0f, Dot(rolled.Row2, rolled.Row3), 5);

            // And it must be a roll: the direction the camera looks does not change.
            Assert.Equal(look.Row3.X, rolled.Row3.X, 6);
            Assert.Equal(look.Row3.Y, rolled.Row3.Y, 6);
            Assert.Equal(look.Row3.Z, rolled.Row3.Z, 6);
        }
    }

    private static float Length(Vector3f v) => MathF.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));

    private static float Dot(Vector3f a, Vector3f b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
}
