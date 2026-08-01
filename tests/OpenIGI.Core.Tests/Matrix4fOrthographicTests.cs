using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>
/// Pins the orthographic projection shadow cascades are fitted with.
/// </summary>
/// <remarks>
/// It has to agree with <see cref="Matrix4f.CreatePerspective"/> on both conventions the port settled
/// in M6 — left-handed view space with <c>+Z</c> into the screen, and depth mapped to OpenGL's
/// <c>[-1, 1]</c> — or a shadow would be sampled at the wrong depth and every surface would either
/// shadow itself or nothing at all.
/// </remarks>
public sealed class Matrix4fOrthographicTests
{
    [Fact]
    public void TheBoxCornersMapToTheEdgesOfClipSpace()
    {
        Matrix4f projection = Matrix4f.CreateOrthographic(-10f, 10f, -5f, 5f, 1f, 101f);

        Vector3f nearBottomLeft = projection.TransformPoint(new Vector3f(-10f, -5f, 1f));
        Vector3f farTopRight = projection.TransformPoint(new Vector3f(10f, 5f, 101f));

        Assert.Equal(-1f, nearBottomLeft.X, 5);
        Assert.Equal(-1f, nearBottomLeft.Y, 5);
        Assert.Equal(-1f, nearBottomLeft.Z, 5);
        Assert.Equal(1f, farTopRight.X, 5);
        Assert.Equal(1f, farTopRight.Y, 5);
        Assert.Equal(1f, farTopRight.Z, 5);
    }

    [Fact]
    public void DepthAgreesWithThePerspectiveProjectionsRange()
    {
        // Both must land the near plane on -1 and the far plane on +1; a shadow map compared against
        // depth from the other convention would be uniformly wrong rather than obviously broken.
        Matrix4f ortho = Matrix4f.CreateOrthographic(100f, 100f, 2f, 200f);
        Matrix4f perspective = Matrix4f.CreatePerspective(1.2f, 1.5f, 2f, 200f);

        Assert.Equal(
            perspective.TransformPoint(new Vector3f(0f, 0f, 2f)).Z,
            ortho.TransformPoint(new Vector3f(0f, 0f, 2f)).Z,
            4);
        Assert.Equal(
            perspective.TransformPoint(new Vector3f(0f, 0f, 200f)).Z,
            ortho.TransformPoint(new Vector3f(0f, 0f, 200f)).Z,
            4);
    }

    [Fact]
    public void ANegativeNearIsAccepted()
    {
        // A cascade's box is centred on the geometry it covers, so half of it lies behind the point
        // the light's frame is built at. Refusing this would force every cascade to be pushed back and
        // spend its depth precision on empty space.
        Matrix4f projection = Matrix4f.CreateOrthographic(50f, 50f, -40f, 40f);

        Assert.Equal(-1f, projection.TransformPoint(new Vector3f(0f, 0f, -40f)).Z, 5);
        Assert.Equal(0f, projection.TransformPoint(new Vector3f(0f, 0f, 0f)).Z, 5);
        Assert.Equal(1f, projection.TransformPoint(new Vector3f(0f, 0f, 40f)).Z, 5);
    }

    [Fact]
    public void ScaleIsUniformAcrossTheBoxUnlikeAPerspective()
    {
        Matrix4f projection = Matrix4f.CreateOrthographic(20f, 20f, -50f, 50f);

        // The defining property: an object's projected size does not change with distance, which is
        // what makes a directional light's shadow map a single consistent texel scale.
        Vector3f near = projection.TransformPoint(new Vector3f(5f, 0f, -40f));
        Vector3f far = projection.TransformPoint(new Vector3f(5f, 0f, 40f));

        Assert.Equal(near.X, far.X, 5);
    }

    [Theory]
    [InlineData(0f, 0f, -1f, 1f, 0f, 10f)]
    [InlineData(-1f, 1f, 0f, 0f, 0f, 10f)]
    [InlineData(-1f, 1f, -1f, 1f, 5f, 5f)]
    public void AnEmptyBoxIsRefusedOnWhicheverAxisCollapses(
        float left,
        float right,
        float bottom,
        float top,
        float near,
        float far)
    {
        // One degenerate axis per case, the other two valid — otherwise every case would throw for the
        // same reason and the test would prove only that one of the three checks exists.
        Assert.Throws<ArgumentException>(() => Matrix4f.CreateOrthographic(left, right, bottom, top, near, far));
    }

    [Fact]
    public void AValidBoxIsAccepted() =>
        Assert.NotEqual(default, Matrix4f.CreateOrthographic(-1f, 1f, -1f, 1f, 0f, 10f));
}
