using OpenIGI.Presentation.Rendering;
using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Presentation.Tests;

public sealed class ViewModelProjectionTests
{
    private const float HorizontalHalfTangent = 0.57735026f;

    [Fact]
    public void UsesRetailForegroundNearPlane()
    {
        Matrix4f projection = ViewModelProjection.Create(HorizontalHalfTangent, 4000f, 16f / 9f);

        Assert.Equal(0.05f, ViewModelProjection.NearPlane);
        Assert.Equal(-1f, projection.TransformPoint(new Vector3f(0f, 0f, ViewModelProjection.NearPlane)).Z, 5);
    }

    [Fact]
    public void KeepsRetailApertureAtFourByThree()
    {
        // IGI.EXE 0x497D70 publishes independent x and y pixel scales for the HumanView's
        // tan(30 degrees) view-model aperture: the vertical one is three quarters of the horizontal.
        Matrix4f projection = ViewModelProjection.Create(HorizontalHalfTangent, 4000f, 4f / 3f);

        Vector3f rightEdge = projection.TransformPoint(new Vector3f(HorizontalHalfTangent, 0f, 1f));
        Vector3f topEdge = projection.TransformPoint(new Vector3f(0f, 0.75f * HorizontalHalfTangent, 1f));
        Assert.Equal(1f, rightEdge.X, 5);
        Assert.Equal(1f, topEdge.Y, 5);
    }

    [Fact]
    public void WidensOnlyTheSidesOnWidescreen()
    {
        // Hor+ keeps retail's vertical aperture, so the glove still sits at the bottom edge, and the
        // sides open up with the screen instead of the rig being stretched across it.
        Matrix4f projection = ViewModelProjection.Create(HorizontalHalfTangent, 4000f, 16f / 9f);

        Vector3f rightEdge = projection.TransformPoint(new Vector3f(HorizontalHalfTangent * (16f / 9f) / (4f / 3f), 0f, 1f));
        Vector3f topEdge = projection.TransformPoint(new Vector3f(0f, 0.75f * HorizontalHalfTangent, 1f));
        Assert.Equal(1f, rightEdge.X, 5);
        Assert.Equal(1f, topEdge.Y, 5);
    }
}
