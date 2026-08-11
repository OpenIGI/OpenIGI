using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Desktop.Tests;

public sealed class ViewModelProjectionTests
{
    [Fact]
    public void UsesRetailForegroundNearPlane()
    {
        Matrix4f projection = ViewModelProjection.Create(MathF.PI / 3f, 4f / 3f, 4000f);

        Assert.Equal(0.05f, ViewModelProjection.NearPlane);
        Assert.Equal(-1f, projection.TransformPoint(new Vector3f(0f, 0f, ViewModelProjection.NearPlane)).Z, 5);
    }
}
