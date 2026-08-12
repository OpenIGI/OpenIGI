using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

public sealed class GBufferLayoutTests
{
    [Fact]
    public void OptionalAttachmentsFollowPresetLayout()
    {
        SceneTargetDescription enhanced = new(2560, 1440, Deferred: true, GBuffer: GBufferLayout.BakedLighting);
        SceneTargetDescription high = enhanced with { GBuffer = GBufferLayout.Full };
        SceneTargetDescription ultra = enhanced with { GBuffer = GBufferLayout.Full };
        SceneTargetDescription vanilla = new(2560, 1440, Deferred: false, GBuffer: GBufferLayout.Full);

        Assert.True(enhanced.HasBakedLighting);
        Assert.False(enhanced.HasMotionVectors);
        Assert.True(high.HasBakedLighting && high.HasMotionVectors);
        Assert.True(ultra.HasBakedLighting && ultra.HasMotionVectors);
        Assert.False(vanilla.HasBakedLighting || vanilla.HasMotionVectors);
    }

    [Theory]
    [InlineData(2560, 1440, 1280, 720)]
    [InlineData(2559, 1439, 1280, 720)]
    [InlineData(1, 1, 1, 1)]
    public void HalfResolutionRoundsOddDimensionsUp(int width, int height, int halfWidth, int halfHeight)
    {
        SceneTargetDescription target = new(width, height);
        Assert.Equal(halfWidth, target.HalfWidth);
        Assert.Equal(halfHeight, target.HalfHeight);
    }
}
