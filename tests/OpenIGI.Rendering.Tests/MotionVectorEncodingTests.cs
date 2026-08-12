using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

public sealed class MotionVectorEncodingTests
{
    [Fact]
    public void NoHistorySentinelIsOutsideValidScreenMotion()
    {
        Assert.True(MotionVectorEncoding.NoHistorySentinel > 1f);
        Assert.True(MotionVectorEncoding.IsNoHistory(MotionVectorEncoding.NoHistorySentinel, 0f));
        Assert.False(MotionVectorEncoding.IsNoHistory(1f, -1f));
    }
}
