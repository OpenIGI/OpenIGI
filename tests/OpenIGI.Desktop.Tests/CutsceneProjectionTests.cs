using Xunit;

namespace OpenIGI.Desktop.Tests;

public sealed class CutsceneProjectionTests
{
    [Fact]
    public void AuthoredVerticalFramingStaysAtTheRetailFourByThreeAperture()
    {
        float actual = DesktopGame.CutsceneVerticalFieldOfView(1f);
        float retail = 2f * MathF.Atan(1f / (4f / 3f));
        float croppedWidescreen = 2f * MathF.Atan(1f / (16f / 9f));

        Assert.Equal(retail, actual, 6);
        Assert.True(actual > croppedWidescreen);
    }
}
