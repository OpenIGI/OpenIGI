using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The one thing the third-person view changes about the game rather than about the picture: which way
/// a round goes. Most of these are proofs that it changes nothing.
/// </summary>
public sealed class AimBasisTests
{
    private const double Metre = 4096.0;

    /// <summary>First person passes no target, and nothing moves.</summary>
    [Fact]
    public void WithNoTargetTheFrameIsUntouched()
    {
        (Vector3f forward, Vector3f right, Vector3f up) = Frame();
        Vector3f f = forward, r = right, u = up;

        Assert.False(AimBasis.Converge(Eye(), null, ref f, ref r, ref u));

        Assert.Equal(forward, f);
        Assert.Equal(right, r);
        Assert.Equal(up, u);
    }

    /// <summary>
    /// A target already on the eye's forward turns the frame onto itself, which is where it was.
    /// </summary>
    /// <remarks>
    /// The reason first person needs no flag: there the camera IS the eye, so the point under the middle
    /// of the screen is on the body's own forward and this is the identity.
    /// </remarks>
    [Fact]
    public void ATargetOnTheExistingForwardLeavesTheFrameWhereItWas()
    {
        (Vector3f forward, Vector3f right, Vector3f up) = Frame();
        Vector3f f = forward, r = right, u = up;

        Vector3d eye = Eye();
        Vector3d target = new(
            eye.X + (forward.X * 40.0 * Metre),
            eye.Y + (forward.Y * 40.0 * Metre),
            eye.Z + (forward.Z * 40.0 * Metre));

        AimBasis.Converge(eye, target, ref f, ref r, ref u);

        AssertClose(forward, f);
        AssertClose(right, r);
        AssertClose(up, u);
    }

    /// <summary>The barrel ends up pointing exactly at what was asked for.</summary>
    [Fact]
    public void TheBarrelTurnsOntoTheTarget()
    {
        (Vector3f forward, Vector3f right, Vector3f up) = Frame();
        Vector3d eye = Eye();

        // Ten metres ahead and two to the left of the eye's own line, which is the shape of the error a
        // shoulder-mounted camera creates.
        Vector3d target = new(eye.X - (2.0 * Metre), eye.Y + (10.0 * Metre), eye.Z);

        Assert.True(AimBasis.Converge(eye, target, ref forward, ref right, ref up));

        double reach = Math.Sqrt((2.0 * 2.0) + (10.0 * 10.0));
        Assert.Equal(-2.0 / reach, forward.X, 4);
        Assert.Equal(10.0 / reach, forward.Y, 4);
        Assert.Equal(0.0, forward.Z, 4);
    }

    /// <summary>The turned frame is still square and still right-handed, so the spread cone is too.</summary>
    /// <remarks>
    /// The cone opens across the two axes beside the barrel. Left skewed, a shotgun's pattern would come
    /// out sheared; left mirrored, it would come out reflected — invisible in a rifle round and obvious
    /// in a spread of nine.
    /// </remarks>
    [Fact]
    public void TheTurnedFrameStaysOrthonormalAndRightHanded()
    {
        (Vector3f forward, Vector3f right, Vector3f up) = Frame();
        Vector3d eye = Eye();
        Vector3d target = new(eye.X - (2.0 * Metre), eye.Y + (10.0 * Metre), eye.Z + (3.0 * Metre));

        Assert.True(AimBasis.Converge(eye, target, ref forward, ref right, ref up));

        Assert.Equal(1.0, Length(forward), 4);
        Assert.Equal(1.0, Length(right), 4);
        Assert.Equal(1.0, Length(up), 4);
        Assert.Equal(0.0, Dot(forward, right), 4);
        Assert.Equal(0.0, Dot(forward, up), 4);
        Assert.Equal(0.0, Dot(right, up), 4);

        // right x forward is up, the handedness the engine's own frame has.
        Vector3f cross = new(
            (right.Y * forward.Z) - (right.Z * forward.Y),
            (right.Z * forward.X) - (right.X * forward.Z),
            (right.X * forward.Y) - (right.Y * forward.X));
        AssertClose(up, cross);
    }

    /// <summary>A target behind the eye is refused rather than fired at.</summary>
    /// <remarks>
    /// The camera traces from behind the player, so it can meet a wall the player is backed against.
    /// Converging on that would send the round through the player's own back.
    /// </remarks>
    [Fact]
    public void ATargetBehindTheEyeIsRefused()
    {
        (Vector3f forward, Vector3f right, Vector3f up) = Frame();
        Vector3f f = forward, r = right, u = up;

        Vector3d eye = Eye();
        Vector3d behind = new(eye.X, eye.Y - (3.0 * Metre), eye.Z);

        Assert.False(AimBasis.Converge(eye, behind, ref f, ref r, ref u));
        Assert.Equal(forward, f);
    }

    /// <summary>A target all but on top of the eye is refused too.</summary>
    [Fact]
    public void ATargetInsideTheMinimumReachIsRefused()
    {
        (Vector3f forward, Vector3f right, Vector3f up) = Frame();
        Vector3f f = forward, r = right, u = up;

        Vector3d eye = Eye();
        Vector3d close = new(eye.X, eye.Y + (AimBasis.MinimumReachInUnits * 0.5), eye.Z);

        Assert.False(AimBasis.Converge(eye, close, ref f, ref r, ref u));
        Assert.Equal(forward, f);
    }

    /// <summary>
    /// The turn is small at distance and large up close, which is what makes the crosshair honest.
    /// </summary>
    [Fact]
    public void TheTurnShrinksWithRange()
    {
        Vector3d eye = Eye();

        float Angle(double metresAhead)
        {
            (Vector3f forward, Vector3f right, Vector3f up) = Frame();
            Vector3d target = new(
                eye.X - (0.55 * Metre),
                eye.Y + (metresAhead * Metre),
                eye.Z);
            AimBasis.Converge(eye, target, ref forward, ref right, ref up);
            return MathF.Acos(Math.Clamp(forward.Y, -1f, 1f));
        }

        float near = Angle(3.0);
        float far = Angle(100.0);

        Assert.True(near > far, "a near target should need more of a turn than a distant one.");
        Assert.True(far < 0.01f, $"a hundred metres out the turn should be all but nothing, not {far} rad.");
    }

    // Yaw zero: looking along +Y with X right and Z up, which is the frame a level view carries.
    private static (Vector3f Forward, Vector3f Right, Vector3f Up) Frame() =>
        (new Vector3f(0f, 1f, 0f), new Vector3f(1f, 0f, 0f), new Vector3f(0f, 0f, 1f));

    private static Vector3d Eye() => new(10.0 * Metre, 20.0 * Metre, 1.6 * Metre);

    private static double Dot(Vector3f left, Vector3f right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    private static double Length(Vector3f value) => Math.Sqrt(Dot(value, value));

    private static void AssertClose(Vector3f expected, Vector3f actual)
    {
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(expected.Z, actual.Z, 4);
    }
}
