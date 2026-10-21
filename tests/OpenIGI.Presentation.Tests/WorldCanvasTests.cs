using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Rendering;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>Locks down the surface a world-space marker is laid on: where it faces, and where it lands.</summary>
public sealed class WorldCanvasTests
{
    private const int Width = 1920;
    private const int Height = 1080;

    [Fact]
    public void TheOriginLandsWhereTheAnchorIs()
    {
        WorldCanvas canvas = Canvas(new Vector3d(0.0, 2.5, 1.7));

        Assert.True(canvas.TryProject(0f, 0f, out float x, out float y));

        Assert.Equal(960f, x, 1);
        Assert.Equal(540f, y, 1);
        Assert.Equal(2.5, canvas.DistanceMetres, 3);
    }

    [Fact]
    public void TheCanvasStandsUpAndTurnsItsFaceToTheEye()
    {
        // Looking along +Y at an anchor ahead: the canvas's right is world +X and its up is world +Z,
        // so a point to its right is to the right on screen and a point above it is higher up.
        WorldCanvas canvas = Canvas(new Vector3d(0.0, 2.5, 1.7));

        Assert.True(canvas.TryProject(100f, 0f, out float rightX, out float rightY));
        Assert.True(canvas.TryProject(0f, -100f, out float upX, out float upY));

        Assert.True(rightX > 960f);
        Assert.Equal(540f, rightY, 1);
        Assert.True(upY < 540f);
        Assert.Equal(960f, upX, 1);
    }

    [Fact]
    public void TheFaceFollowsTheViewerAroundTheObject()
    {
        // Seen from the side, the same canvas has turned: its right is no longer world +X, but it is
        // still square-on, so its own right still reads as right on the screen.
        var eye = new Camera3D(new Vector3d(2.5, 0.0, 1.7), Camera3D.LookRotation(-MathF.PI * 0.5f, 0f));
        var canvas = new WorldCanvas(new Vector3d(0.0, 0.0, 1.7), eye, 1f, 16f / 9f, Width, Height, 0.0024f);

        Assert.True(canvas.TryProject(0f, 0f, out float centreX, out float centreY));
        Assert.True(canvas.TryProject(100f, 0f, out float rightX, out float rightY));

        Assert.Equal(960f, centreX, 1);
        Assert.Equal(540f, centreY, 1);
        Assert.True(rightX > centreX);
        Assert.Equal(centreY, rightY, 1);
    }

    [Fact]
    public void ItIsSmallerFurtherAwayInProportionToTheDistance()
    {
        WorldCanvas near = Canvas(new Vector3d(0.0, 1.5, 1.7));
        WorldCanvas far = Canvas(new Vector3d(0.0, 3.0, 1.7));

        float nearWidth = Span(near);
        float farWidth = Span(far);

        // Twice as far is half the size. That is the whole difference between a surface standing in the
        // level and a marker drawn at a projected point, which would need a scale rule to fake it.
        Assert.Equal(nearWidth * 0.5f, farWidth, 1);
    }

    [Fact]
    public void ItForeshortensWhenItIsLookedDownOn()
    {
        // From above, the canvas keeps its width but loses height on the screen, exactly as a sign
        // standing on the floor does.
        var level = new Camera3D(new Vector3d(0.0, 0.0, 1.0), Camera3D.LookRotation(0f, 0f));
        var above = new Camera3D(new Vector3d(0.0, 0.0, 3.0), Camera3D.LookRotation(0f, -MathF.PI * 0.35f));
        var straight = new WorldCanvas(new Vector3d(0.0, 3.0, 1.0), level, 1f, 16f / 9f, Width, Height, 0.0024f);
        var tilted = new WorldCanvas(new Vector3d(0.0, 3.0, 1.0), above, 1f, 16f / 9f, Width, Height, 0.0024f);

        Assert.True(ScreenHeight(tilted) < ScreenHeight(straight) * 0.95f);

        static float ScreenHeight(in WorldCanvas canvas)
        {
            Assert.True(canvas.TryProject(0f, -50f, out _, out float top));
            Assert.True(canvas.TryProject(0f, 50f, out _, out float bottom));
            return MathF.Abs(bottom - top);
        }
    }

    [Fact]
    public void NothingBehindTheEyeIsProjected()
    {
        WorldCanvas behind = Canvas(new Vector3d(0.0, -2.5, 1.7));

        Assert.False(behind.TryProject(0f, 0f, out _, out _));
    }

    private static WorldCanvas Canvas(Vector3d anchorMetres)
    {
        var eye = new Camera3D(new Vector3d(0.0, 0.0, 1.7), Camera3D.LookRotation(0f, 0f));
        return new WorldCanvas(anchorMetres, eye, 1f, 16f / 9f, Width, Height, 0.0024f);
    }

    private static float Span(in WorldCanvas canvas)
    {
        Assert.True(canvas.TryProject(-50f, 0f, out float left, out _));
        Assert.True(canvas.TryProject(50f, 0f, out float right, out _));
        return right - left;
    }
}
