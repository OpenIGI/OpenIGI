using OpenIGI.Audio;
using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Audio.Tests;

/// <summary>
/// The listener basis, and the difference between the two matrices that both look like "the way the
/// camera faces".
/// </summary>
public sealed class AudioListenerStateTests
{
    /// <summary>
    /// A world-to-view rotation of the shape <c>Camera3D.LookRotation</c> returns: rows are the view
    /// axes, in the order right, up, forward. Transcribed rather than referenced, because
    /// <c>OpenIGI.Audio</c> may not depend on the renderer.
    /// </summary>
    private static Matrix3f LookRotation(float yaw, float pitch)
    {
        float cy = MathF.Cos(yaw);
        float sy = MathF.Sin(yaw);
        float cp = MathF.Cos(pitch);
        float sp = MathF.Sin(pitch);

        return Matrix3f.FromRows(
            new Vector3f(cy, -sy, 0f),
            new Vector3f(-sy * sp, -cy * sp, cp),
            new Vector3f(sy * cp, cy * cp, sp));
    }

    [Fact]
    public void AnUnrotatedListenerFacesTheEngineForwardAxisAndNotStraightUp()
    {
        AudioListenerState listener = AudioListenerState.FromViewRotation(default, LookRotation(0f, 0f));

        AssertDirection(new Vector3f(0f, 1f, 0f), listener.Forward);
        AssertDirection(new Vector3f(0f, 0f, 1f), listener.Up);
        AssertDirection(new Vector3f(1f, 0f, 0f), listener.Right);
    }

    [Fact]
    public void HandingTheViewRotationStraightToTheOrientationSwapsForwardAndUp()
    {
        // The bug this factory exists to prevent, pinned so it cannot come back quietly. Reading the
        // view matrix's COLUMNS makes the listener face straight up, which puts a source overhead dead
        // ahead of the ear -- centred, unpanned, and seemingly right on top of the player.
        AudioListenerState wrong = new()
        {
            Orientation = LookRotation(0f, 0f),
        };

        AssertDirection(new Vector3f(0f, 0f, 1f), wrong.Forward);
        AssertDirection(new Vector3f(0f, 1f, 0f), wrong.Up);
    }

    [Fact]
    public void TurningRightSwingsForwardOntoThePositiveXAxis()
    {
        // A quarter turn. Which way the world turns under the ear is what decides whether the stereo
        // image is mirrored, so it is asserted rather than assumed.
        AudioListenerState listener =
            AudioListenerState.FromViewRotation(default, LookRotation(MathF.PI / 2f, 0f));

        AssertDirection(new Vector3f(1f, 0f, 0f), listener.Forward);
        AssertDirection(new Vector3f(0f, -1f, 0f), listener.Right);
        AssertDirection(new Vector3f(0f, 0f, 1f), listener.Up);
    }

    [Fact]
    public void LookingUpTiltsForwardTowardTheSkyAndUpBackward()
    {
        AudioListenerState listener =
            AudioListenerState.FromViewRotation(default, LookRotation(0f, MathF.PI / 2f));

        AssertDirection(new Vector3f(0f, 0f, 1f), listener.Forward);
        AssertDirection(new Vector3f(0f, -1f, 0f), listener.Up);
    }

    [Fact]
    public void TheBasisStaysOrthonormalWherverItIsPointed()
    {
        for (int step = 0; step < 8; step++)
        {
            float yaw = step * (MathF.PI / 4f);
            AudioListenerState listener =
                AudioListenerState.FromViewRotation(default, LookRotation(yaw, 0.3f));

            Assert.Equal(1f, Length(listener.Forward), 4);
            Assert.Equal(1f, Length(listener.Up), 4);
            Assert.Equal(1f, Length(listener.Right), 4);
            Assert.Equal(0f, Dot(listener.Forward, listener.Up), 4);
            Assert.Equal(0f, Dot(listener.Forward, listener.Right), 4);
            Assert.Equal(0f, Dot(listener.Up, listener.Right), 4);
        }
    }

    private static void AssertDirection(Vector3f expected, Vector3f actual)
    {
        Assert.Equal(expected.X, actual.X, 5);
        Assert.Equal(expected.Y, actual.Y, 5);
        Assert.Equal(expected.Z, actual.Z, 5);
    }

    private static float Dot(Vector3f a, Vector3f b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static float Length(Vector3f v) => MathF.Sqrt(Dot(v, v));
}
