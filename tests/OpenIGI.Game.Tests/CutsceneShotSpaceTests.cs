using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// A riding key's space undoes itself: what the editor writes back with <see cref="CutsceneShotSpace.ToAuthored"/> is seen where
/// it was, through the game's <see cref="CutsceneShotSpace.ToWorld"/>; and a key's turn is written with the angles nearest its own,
/// so the game's per-angle blend never turns the long way round (<see cref="CameraAngles"/>).
/// </summary>
public sealed class CutsceneShotSpaceTests
{
    [Theory]
    [InlineData(0.3f, -0.2f, 1.1f, 2.0f, 0.4f, -2.7f)]
    [InlineData(0f, 0f, 3.1f, 0f, 0f, -3.1f)]
    [InlineData(1.4f, 0.9f, -0.6f, -0.3f, 1.2f, 0.8f)]
    public void A_view_written_back_is_seen_where_it_was(float a0, float b0, float g0, float a1, float b1, float g1)
    {
        CutsceneShotSpace space = new(
            new Vector3d(1_000_000, -2_000_000, 50_000), Matrix3f.FromEngineEulerAngles(a0, b0, g0),
            new Vector3d(1_400_000, -1_100_000, 52_000), Matrix3f.FromEngineEulerAngles(a1, b1, g1));
        Vector3d seen = new(1_402_000, -1_098_500, 60_000);
        Matrix3f turned = Matrix3f.FromEngineEulerAngles(0.2f, -0.1f, 2.4f);

        (Vector3d at, Matrix3f written) = space.ToAuthored(seen, turned);
        (Vector3d back, Matrix3f turnedBack) = space.ToWorld(at, written);

        Assert.True((back - seen).Length < 0.01, $"{back} vs {seen}");
        AssertSameTurn(turned, turnedBack);
        (Vector3d same, _) = CutsceneShotSpace.World.ToAuthored(seen, turned);
        Assert.Equal(seen, same);
    }

    [Fact]
    public void A_turn_across_the_half_turn_is_written_the_short_way()
    {
        // The key before stands at -179 degrees; the view turns two degrees on, to +179 as the matrix reads back.
        Vector3f near = new(0.1f, 0f, -3.124f);
        Matrix3f turn = Matrix3f.FromEngineEulerAngles(0.1f, 0f, -3.124f - 0.035f);
        Vector3f angles = CameraAngles.Closest(turn, near);

        AssertSameTurn(turn, Matrix3f.FromEngineEulerAngles(angles.X, angles.Y, angles.Z));
        Assert.InRange(angles.Z, -3.124f - 0.04f, -3.124f - 0.03f);
        Assert.InRange(angles.X, 0.09f, 0.11f);
    }

    [Fact]
    public void The_second_euler_solution_is_taken_when_it_is_the_nearer()
    {
        Vector3f written = new(0.4f, 0.3f, -1.2f);
        Matrix3f turn = Matrix3f.FromEngineEulerAngles(written.X, written.Y, written.Z);

        // Whatever form the matrix reads back in, the angles nearest the key's own are its own (wound by whole turns at most).
        Vector3f angles = CameraAngles.Closest(turn, written + new Vector3f(0f, 0f, 12.566371f));
        AssertSameTurn(turn, Matrix3f.FromEngineEulerAngles(angles.X, angles.Y, angles.Z));
        Assert.InRange(angles.Z, -1.2f + 12.566f - 0.01f, -1.2f + 12.567f + 0.01f);
    }

    private static void AssertSameTurn(Matrix3f expected, Matrix3f actual)
    {
        foreach ((Vector3f e, Vector3f a) in new[] { (expected.Row1, actual.Row1), (expected.Row2, actual.Row2), (expected.Row3, actual.Row3) })
        {
            Assert.True(MathF.Abs(e.X - a.X) < 1e-4f && MathF.Abs(e.Y - a.Y) < 1e-4f && MathF.Abs(e.Z - a.Z) < 1e-4f, $"{e} vs {a}");
        }
    }
}
