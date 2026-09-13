using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Desktop.Tests;

public sealed class ViewModelProjectionTests
{
    [Fact]
    public void UsesRetailForegroundNearPlane()
    {
        Matrix4f projection = ViewModelProjection.Create(0.57735026f, 4000f);

        Assert.Equal(0.05f, ViewModelProjection.NearPlane);
        Assert.Equal(-1f, projection.TransformPoint(new Vector3f(0f, 0f, ViewModelProjection.NearPlane)).Z, 5);
    }

    [Fact]
    public void KeepsRetailHorizontalHalfTangentOnWidescreen()
    {
        // IGI.EXE 0x497D70 publishes independent x and y pixel scales for the HumanView's
        // tan(30 degrees) view-model aperture. A regular aspect-correct projection widens the
        // horizontal frustum at 16:9; retail does not.
        const float horizontalHalfTangent = 0.57735026f;
        Matrix4f projection = ViewModelProjection.Create(horizontalHalfTangent, 4000f);

        Vector3f rightEdge = projection.TransformPoint(new Vector3f(horizontalHalfTangent, 0f, 1f));
        Vector3f topEdge = projection.TransformPoint(new Vector3f(0f, 0.75f * horizontalHalfTangent, 1f));
        Assert.Equal(1f, rightEdge.X, 5);
        Assert.Equal(1f, topEdge.Y, 5);
    }
}
