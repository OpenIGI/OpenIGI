using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>
/// Covers <see cref="Matrix4f"/>, and in particular that the projection reproduces the arithmetic the
/// original applies per vertex.
/// </summary>
public sealed class Matrix4fTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void IdentityLeavesAPointAlone()
    {
        Vector3f point = new(3f, -4f, 5f);
        Assert.Equal(point, Matrix4f.Identity.TransformPoint(point));
    }

    [Fact]
    public void MultiplicationAppliesTheRightOperandFirst()
    {
        Matrix4f translate = Matrix4f.CreateTranslation(1f, 2f, 3f);
        Matrix4f scale = new(
            2f, 0f, 0f, 0f,
            0f, 2f, 0f, 0f,
            0f, 0f, 2f, 0f,
            0f, 0f, 0f, 1f);

        // scale * translate scales the already-translated point; translate * scale does not.
        Vector3f scaledThenMoved = (translate * scale).TransformPoint(new Vector3f(1f, 1f, 1f));
        Vector3f movedThenScaled = (scale * translate).TransformPoint(new Vector3f(1f, 1f, 1f));

        Assert.Equal(new Vector3f(3f, 4f, 5f), scaledThenMoved);
        Assert.Equal(new Vector3f(4f, 6f, 8f), movedThenScaled);
    }

    [Fact]
    public void CreateRigidRotatesThenTranslates()
    {
        // A quarter turn about Z: +X becomes +Y.
        Matrix3f rotate = Matrix3f.FromRows(
            new Vector3f(0f, -1f, 0f),
            new Vector3f(1f, 0f, 0f),
            new Vector3f(0f, 0f, 1f));

        Matrix4f transform = Matrix4f.CreateRigid(rotate, new Vector3f(10f, 0f, 0f));
        Vector3f result = transform.TransformPoint(new Vector3f(1f, 0f, 0f));

        Assert.Equal(10f, result.X, Tolerance);
        Assert.Equal(1f, result.Y, Tolerance);
        Assert.Equal(0f, result.Z, Tolerance);
    }

    [Fact]
    public void ProjectionPutsTheNearAndFarPlanesAtTheEndsOfTheDepthRange()
    {
        Matrix4f projection = Matrix4f.CreateProjection(320f, 320f, 320f, 240f, 640, 480, 1f, 1000f);

        Assert.Equal(-1f, projection.TransformPoint(new Vector3f(0f, 0f, 1f)).Z, Tolerance);
        Assert.Equal(1f, projection.TransformPoint(new Vector3f(0f, 0f, 1000f)).Z, Tolerance);
    }

    [Fact]
    public void ProjectionReproducesTheOriginalsPixelArithmetic()
    {
        // The original computes sx = x/z * xScale + cx and sy = y/z * yScale + cy in pixels
        // (sub_512C30), with y measured downwards from the top of the viewport.
        const float xScale = 400f;
        const float yScale = 300f;
        const float centreX = 320f;
        const float centreY = 240f;
        const int width = 640;
        const int height = 480;

        Matrix4f projection = Matrix4f.CreateProjection(xScale, yScale, centreX, centreY, width, height, 1f, 1000f);

        foreach (Vector3f view in new[] { new Vector3f(5f, 3f, 20f), new(-12f, 7f, 60f), new(0f, 0f, 2f) })
        {
            float expectedPixelX = (view.X / view.Z * xScale) + centreX;
            float expectedPixelY = (view.Y / view.Z * yScale) + centreY;

            Vector3f ndc = projection.TransformPoint(view);
            float actualPixelX = (ndc.X + 1f) * 0.5f * width;
            float actualPixelY = (1f - ndc.Y) * 0.5f * height;

            Assert.Equal(expectedPixelX, actualPixelX, 0.001);
            Assert.Equal(expectedPixelY, actualPixelY, 0.001);
        }
    }

    [Fact]
    public void PerspectiveKeepsTheVerticalFieldOfView()
    {
        const float fov = MathF.PI / 3f;
        Matrix4f projection = Matrix4f.CreatePerspective(fov, 4f / 3f, 1f, 500f);

        // A point exactly on the top edge of the frustum lands on the top of the NDC cube.
        float z = 10f;
        float y = z * MathF.Tan(fov * 0.5f);
        Assert.Equal(1f, projection.TransformPoint(new Vector3f(0f, y, z)).Y, Tolerance);
    }

    [Fact]
    public void CopyColumnMajorWritesTheTranspose()
    {
        Matrix4f matrix = new(
            1f, 2f, 3f, 4f,
            5f, 6f, 7f, 8f,
            9f, 10f, 11f, 12f,
            13f, 14f, 15f, 16f);

        Span<float> elements = stackalloc float[Matrix4f.ComponentCount];
        matrix.CopyColumnMajorTo(elements);

        Assert.Equal([1f, 5f, 9f, 13f, 2f, 6f, 10f, 14f, 3f, 7f, 11f, 15f, 4f, 8f, 12f, 16f], elements.ToArray());
    }

    [Fact]
    public void CopyColumnMajorRejectsAShortDestination()
    {
        float[] tooSmall = new float[15];
        Assert.Throws<ArgumentException>(() => Matrix4f.Identity.CopyColumnMajorTo(tooSmall));
    }

    [Theory]
    [InlineData(0, 480, 1, 1000)]
    [InlineData(640, 0, 1, 1000)]
    [InlineData(640, 480, 0, 1000)]
    [InlineData(640, 480, 10, 10)]
    public void ProjectionRejectsDegenerateArguments(int width, int height, float near, float far) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Matrix4f.CreateProjection(1f, 1f, 0f, 0f, width, height, near, far));
}
