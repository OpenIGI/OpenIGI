using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>Guards the menu backdrop's expensive derived-ground-detail policy.</summary>
public sealed class MissionPreviewLoaderTests
{
    [Theory]
    [InlineData(0f, false)]
    [InlineData(-1f, false)]
    [InlineData(0.0001f, true)]
    [InlineData(0.25f, true)]
    public void GroundDetailIsOnlyDerivedWhenTheRendererCanUseIt(float strength, bool expected)
        => Assert.Equal(expected, MissionPreviewLoader.ShouldDeriveGroundDetail(strength));
}
