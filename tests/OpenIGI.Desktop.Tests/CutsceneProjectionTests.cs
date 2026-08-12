using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
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

    [Fact]
    public void ActiveViewDrivesAreaStreamedBuildingsDuringACutscene()
    {
        Vector3d playerUnits = new(100.0, 200.0, 300.0);
        Vector3d cameraMetres = new(4.0, 5.0, 6.0);

        Assert.Equal(
            cameraMetres * WorldScene.UnitsPerMetre,
            DesktopGame.AreaActivationOccupant(playerUnits, cameraMetres));
        Assert.Equal(
            playerUnits,
            DesktopGame.AreaActivationOccupant(playerUnits, activeViewMetres: null));
    }
}
