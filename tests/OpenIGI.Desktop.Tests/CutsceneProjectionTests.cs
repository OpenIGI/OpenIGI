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
    public void AuthoredCutsceneViewDrivesAreaStreamedBuildings()
    {
        Vector3d playerUnits = new(100.0, 200.0, 300.0);
        Vector3d cameraMetres = new(4.0, 5.0, 6.0);

        Assert.Equal(
            cameraMetres * WorldScene.UnitsPerMetre,
            DesktopGame.AreaActivationOccupant(playerUnits, cameraMetres));
        Assert.Equal(
            playerUnits,
            DesktopGame.AreaActivationOccupant(playerUnits, cutsceneViewMetres: null));
    }

    [Fact]
    public void FreeCameraCannotTriggerAreaVolumesWhileAPlayerBodyExists()
    {
        Vector3d playerUnits = new(100.0, 200.0, 300.0);
        Vector3d extractionCameraMetres = new(27481.8, -13622.7, 42795.7);

        Assert.Equal(
            playerUnits,
            DesktopGame.AreaActivationOccupant(
                playerUnits,
                cutsceneViewMetres: null,
                freeViewMetres: extractionCameraMetres));

        Assert.Equal(
            extractionCameraMetres * WorldScene.UnitsPerMetre,
            DesktopGame.AreaActivationOccupant(
                playerUnits: null,
                cutsceneViewMetres: null,
                freeViewMetres: extractionCameraMetres));
    }
}
