using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Pins that the sun handed to the renderer stays fixed in the world as the camera turns.
/// </summary>
/// <remarks>
/// <para>
/// This is the failure that would be quietest: a sun rotated by the wrong matrix still lights the
/// scene, and every single frame looks defensible on its own — it is only across a turn that the light
/// visibly swings with the player, and by then it reads as "the lighting looks odd" rather than as a
/// transform bug.
/// </para>
/// <para>
/// So the expression the shell uses is asserted directly. <c>Camera3D.CreateModelView</c> carries the
/// same warning: the rows of the orientation are the view axes, and <c>Transform</c> applies the
/// engine's row-vector convention, which dots the columns and is the inverse rotation.
/// </para>
/// </remarks>
public sealed class SunViewSpaceTests
{
    private static Vector3f ToViewSpace(Camera3D camera, Vector3f worldDirection) =>
        camera.Orientation.Transposed().Transform(worldDirection);

    [Fact]
    public void TurningTheCameraRotatesTheSunInViewSpaceByTheSameAngle()
    {
        Vector3f world = new(0f, 1f, 0f);

        Camera3D facingNorth = new(default, Camera3D.LookRotation(0f, 0f));
        Camera3D turnedQuarter = new(default, Camera3D.LookRotation(MathF.PI / 2f, 0f));

        Vector3f ahead = ToViewSpace(facingNorth, world);
        Vector3f afterTurn = ToViewSpace(turnedQuarter, world);

        // Straight ahead becomes straight to one side after a quarter turn. If the sun were being left
        // in view space it would still read as straight ahead.
        Assert.Equal(1f, ahead.Z, 4);
        Assert.Equal(0f, ahead.X, 4);
        Assert.Equal(0f, afterTurn.Z, 4);
        Assert.Equal(1f, MathF.Abs(afterTurn.X), 4);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.4f)]
    [InlineData(1.9f)]
    [InlineData(3.4f)]
    [InlineData(-2.2f)]
    public void TheSunKeepsItsElevationAtEveryYaw(float yaw)
    {
        // Mission 1's sun: 60 degrees up. Turning on the spot cannot change how high the sun is, and
        // the view-space Y axis is up for a camera with no pitch — so this is the invariant that
        // catches a transform which is subtly the wrong way round rather than obviously so.
        Vector3f world = new(0.433f, 0.0866f, 0.866f);
        world = world.Normalized();

        Camera3D camera = new(default, Camera3D.LookRotation(yaw, 0f));
        Vector3f view = ToViewSpace(camera, world);

        Assert.Equal(world.Z, view.Y, 4);
        Assert.Equal(1f, view.Length, 4);
    }

    [Fact]
    public void TheTransposeMatters()
    {
        // The trap, stated as a test: at a non-zero yaw the two conventions disagree, so a reviewer
        // changing this line finds out immediately rather than at the next screenshot.
        Vector3f world = new Vector3f(0.4f, 0.2f, 0.9f).Normalized();
        Camera3D camera = new(default, Camera3D.LookRotation(0.9f, 0.2f));

        Vector3f correct = camera.Orientation.Transposed().Transform(world);
        Vector3f wrong = camera.Orientation.Transform(world);

        Assert.True(
            MathF.Abs(correct.X - wrong.X) > 0.1f || MathF.Abs(correct.Z - wrong.Z) > 0.1f,
            "the two conventions must differ, or this test is proving nothing");
    }
}
