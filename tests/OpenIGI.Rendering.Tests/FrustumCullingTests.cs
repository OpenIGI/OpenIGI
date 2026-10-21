using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

public sealed class FrustumCullingTests
{
    [Fact]
    public void SphereTouchingAPlaneRemainsVisible()
    {
        BoundingSphere touching = new(new Vector3f(2f, 0f, 0f), 1f);
        Assert.True(FrustumCulling.SphereVisible(Matrix4f.Identity, touching));
    }

    [Fact]
    public void SphereWhollyOutsideAPlaneIsRejected()
    {
        BoundingSphere outside = new(new Vector3f(2.01f, 0f, 0f), 1f);
        Assert.False(FrustumCulling.SphereVisible(Matrix4f.Identity, outside));
    }
}
