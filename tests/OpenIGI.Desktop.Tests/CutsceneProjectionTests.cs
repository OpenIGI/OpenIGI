using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
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

    [Fact]
    public void PlayerBodyKeepsCountingWhileTheCutsceneCameraDrivesActivation()
    {
        Vector3d playerUnits = new(100.0, 200.0, 300.0);
        Vector3d cameraMetres = new(4.0, 5.0, 6.0);

        // The camera stays the primary occupant (Mission 14 streams off it); the body rides along
        // so a latch-less gate such as Mission 4's outro volume does not flap with the camera.
        Assert.Equal(
            playerUnits,
            DesktopGame.AreaActivationExtraOccupant(playerUnits, cameraMetres));
        Assert.Null(DesktopGame.AreaActivationExtraOccupant(playerUnits, cutsceneViewMetres: null));
        Assert.Null(DesktopGame.AreaActivationExtraOccupant(playerUnits: null, cameraMetres));
    }

    [Fact]
    public void DesktopHostPassesBodyProjectionAndLivenessTogether()
    {
        (GameLoop loop, MissionObjectiveRuntime runtime) = BuildPlayerAreaRuntime();
        Vector3d bodyInside = new(0.0, 0.0, 0.0);
        Vector3d cameraOutsideMetres = new(100.0, 0.0, 0.0);

        DesktopGame.TickAreaActivations(
            runtime,
            bodyInside,
            playerTeam: 0,
            cameraOutsideMetres,
            freeViewMetres: null,
            playerAlive: true);
        Assert.Equal(1, ReadPlayerAreaActive(loop));

        DesktopGame.TickAreaActivations(
            runtime,
            bodyInside,
            playerTeam: 0,
            cameraOutsideMetres,
            freeViewMetres: null,
            playerAlive: false);
        Assert.Equal(0, ReadPlayerAreaActive(loop));
    }

    private static (GameLoop Loop, MissionObjectiveRuntime Runtime) BuildPlayerAreaRuntime()
    {
        GameLoop loop = new();
        QvmProgram level = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("AreaActivate"),
                QvmExpression.Text("Position"), QvmExpression.Text("ObjectPos"),
                QvmExpression.Text("Orientation"), QvmExpression.Text("Real32x9"),
                QvmExpression.Text("Dimensions"), QvmExpression.Text("Real32x3"),
                QvmExpression.Text("Criteria"), QvmExpression.Text("String32")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(900),
                QvmExpression.Text("AreaActivate"),
                QvmExpression.Text("Player activation volume"),
                QvmExpression.Real(0f), QvmExpression.Real(0f), QvmExpression.Real(0f),
                QvmExpression.Real(0f), QvmExpression.Real(0f), QvmExpression.Real(0f),
                QvmExpression.Real(20f), QvmExpression.Real(20f), QvmExpression.Real(20f),
                QvmExpression.Text("PLAYER"))));
        Assert.Equal(QvmError.None, new QvmInterpreter(level, loop.Natives).Run());

        return (loop, new MissionObjectiveRuntime(loop.Tasks.Root, loop.Variables));
    }

    private static int ReadPlayerAreaActive(GameLoop loop)
    {
        Assert.True(loop.Variables.TryRead("AreaActivate_900.nActive", out QvmValue active));
        return active.ToInt32();
    }
}
