using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>Retail <c>SCamera</c>/<c>SCameraLens</c> behaviour transcribed from 0x441770..0x44229B.</summary>
public sealed class SecurityCameraRegistryTests
{
    /// <summary>
    /// SCameraControl is a Terminal (0x440C20 registers it under word_53C680): it exports the terminal's isOn, and
    /// the cameras of missions 5 to 9 are powered by exactly that. Without it every one of them stayed dark.
    /// </summary>
    [RequiresGameDataTheory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void Cameras_powered_by_a_camera_control_terminal_switch_on(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LevelLoader level = new(root, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(missions, fileSystem, resources, level).Load(missionId);
        DoorRegistry doors = DoorRegistry.Build(root);
        ElevatorRegistry elevators = ElevatorRegistry.Build(root, loop.Variables, doors);
        _ = WorldInteractionRegistry.Build(root, mission, doors, loop.Variables, elevators);
        SecurityCameraRegistry cameras = SecurityCameraRegistry.Build(root, loop.Variables);

        cameras.Tick(default, targetCrouching: false, targetAlive: true);

        SecurityCamera[] controlled = [.. cameras.Cameras.Where(static c => c.OnExpression.Contains("SCameraControl_", StringComparison.Ordinal))];
        Assert.NotEmpty(controlled);
        Assert.All(controlled, static camera => Assert.True(camera.IsActive, $"{camera.Task.Name} ({camera.OnExpression}) is dark."));
    }

    [RequiresGameDataFact]
    public void MissionOneCameraConfirmsAVisiblePlayerAfterExactlyNinetyTicks()
    {
        Fixture fixture = Build();
        SecurityCamera camera = fixture.Registry.Cameras[0];
        Vector3d targetFeet = TargetStraightAhead(camera, 5.0);

        fixture.Registry.Tick(targetFeet, targetCrouching: false, targetAlive: true);
        Assert.Equal(89, camera.DetectionTime);
        Assert.Equal(SecurityCameraBeep.Suspicious, camera.Beep);
        Assert.False(camera.IsDetection);

        for (int tick = 2; tick <= 89; tick++)
        {
            fixture.Registry.Tick(targetFeet, targetCrouching: false, targetAlive: true);
        }
        Assert.Equal(1, camera.DetectionTime);
        Assert.False(camera.IsDetection);

        fixture.Registry.Tick(targetFeet, targetCrouching: false, targetAlive: true);
        Assert.Equal(0, camera.DetectionTime);
        Assert.True(camera.IsDetection);
        Assert.Equal(SecurityCameraBeep.Detected, camera.Beep);
        AssertScript(fixture.Variables, "SCamera_1301.isDetection", 1);
        AssertScript(fixture.Variables, "SCameraControl_90.isDetection", 1);

        fixture.Registry.Tick(targetFeet, targetCrouching: false, targetAlive: false);
        Assert.False(camera.IsDetection);
        Assert.True(camera.IsLastDetection);
        Assert.Equal(SecurityCameraBeep.None, camera.Beep);
        AssertScript(fixture.Variables, "SCameraControl_90.isDetection", 0);
    }

    [RequiresGameDataFact]
    public void WorldOcclusionPreventsCameraDetection()
    {
        Fixture fixture = Build((in WorldTraceRequest request, out WorldTraceHit hit) =>
        {
            hit = new WorldTraceHit(
                request.Origin,
                default,
                1.0,
                0.1,
                0,
                WorldTraceHitKind.StaticObject);
            return true;
        });
        SecurityCamera camera = fixture.Registry.Cameras[0];
        Vector3d targetFeet = TargetStraightAhead(camera, 5.0);

        for (int tick = 0; tick < 120; tick++)
        {
            fixture.Registry.Tick(targetFeet, targetCrouching: false, targetAlive: true);
        }

        Assert.False(camera.IsDetection);
        Assert.Equal(0, camera.DetectionTime);
        Assert.Equal(SecurityCameraBeep.None, camera.Beep);
    }

    [RequiresGameDataFact]
    public void CameraSweepsAtAuthoredSpeedAndPausesAtItsAuthoredLimit()
    {
        Fixture fixture = Build();
        SecurityCamera camera = fixture.Registry.Cameras[0];

        fixture.Registry.Tick(default, targetCrouching: false, targetAlive: false);
        Assert.InRange(camera.CurrentGamma, Degrees(0.499f), Degrees(0.501f));
        Assert.True(camera.IsMoving);

        fixture.Registry.Tick(default, targetCrouching: false, targetAlive: false);
        Assert.InRange(camera.CurrentGamma, Degrees(0.999f), Degrees(1.001f));
        Assert.True(camera.IsMoving);

        // Camera 1301 authors a one-degree left limit and a four-second (120-tick) end delay.
        for (int tick = 0; tick < 120; tick++)
        {
            fixture.Registry.Tick(default, targetCrouching: false, targetAlive: false);
            Assert.InRange(camera.CurrentGamma, Degrees(0.999f), Degrees(1.001f));
            Assert.False(camera.IsMoving);
        }

        fixture.Registry.Tick(default, targetCrouching: false, targetAlive: false);
        Assert.True(camera.CurrentGamma < Degrees(1.0f));
        Assert.True(camera.IsMoving);
    }

    [RequiresGameDataFact]
    public void CameraHeadCanBePresentedBetweenSimulationTicks()
    {
        Fixture fixture = Build();
        SecurityCamera camera = fixture.Registry.Cameras[0];
        Matrix3f before = camera.Orientation;

        fixture.Registry.Tick(default, targetCrouching: false, targetAlive: false);

        Matrix3f halfway = camera.PresentedOrientation(0.5f, smooth: true);
        Matrix3f current = camera.PresentedOrientation(1f, smooth: true);
        Assert.NotEqual(before, halfway);
        Assert.NotEqual(halfway, current);
        Assert.Equal(camera.Orientation, current);
    }

    [RequiresGameDataFact]
    public void CameraWaitsExactlySixtyTicksAfterLosingItsTargetBeforeResumingSweep()
    {
        Fixture fixture = Build();
        SecurityCamera camera = fixture.Registry.Cameras[0];
        Vector3d targetFeet = TargetStraightAhead(camera, 5.0);

        fixture.Registry.Tick(targetFeet, targetCrouching: false, targetAlive: true);
        float trackedGamma = camera.CurrentGamma;

        for (int tick = 1; tick < 60; tick++)
        {
            fixture.Registry.Tick(default, targetCrouching: false, targetAlive: false);
            Assert.Equal(trackedGamma, camera.CurrentGamma);
            Assert.False(camera.IsMoving);
        }

        fixture.Registry.Tick(default, targetCrouching: false, targetAlive: false);
        Assert.NotEqual(trackedGamma, camera.CurrentGamma);
        Assert.True(camera.IsMoving);
    }

    [RequiresGameDataFact]
    public void CameraLampUsesRetailGreenRedStateAndEightTickPulsePhases()
    {
        Fixture fixture = Build();
        SecurityCamera camera = fixture.Registry.Cameras[0];
        Assert.Equal(SecurityCameraLamp.Off, camera.Lamp);

        for (int tick = 1; tick <= 8; tick++)
        {
            fixture.Registry.Tick(default, targetCrouching: false, targetAlive: false);
            Assert.Equal(SecurityCameraLamp.Green, camera.Lamp);
            Assert.True(camera.IsLargeLampPulse);
        }

        fixture.Registry.Tick(default, targetCrouching: false, targetAlive: false);
        Assert.Equal(SecurityCameraLamp.Green, camera.Lamp);
        Assert.False(camera.IsLargeLampPulse);

        Vector3d targetFeet = TargetStraightAhead(camera, 5.0);
        fixture.Registry.Tick(targetFeet, targetCrouching: false, targetAlive: true);
        Assert.Equal(SecurityCameraLamp.Red, camera.Lamp);

        Assert.True(fixture.Registry.Destroy(camera.Task));
        Assert.Equal(SecurityCameraLamp.Off, camera.Lamp);
    }

    [RequiresGameDataFact]
    public void DestroyingTheLensExportsExplodedAndStopsDetectionAndMotion()
    {
        Fixture fixture = Build();
        SecurityCamera camera = fixture.Registry.Cameras[0];
        Vector3d targetFeet = TargetStraightAhead(camera, 5.0);

        fixture.Registry.Tick(targetFeet, targetCrouching: false, targetAlive: true);
        float pitch = camera.CurrentAlpha;
        Assert.True(fixture.Registry.Destroy(camera.Task));
        fixture.Registry.Tick(targetFeet, targetCrouching: false, targetAlive: true);

        Assert.True(camera.IsExploded);
        Assert.False(camera.IsActive);
        Assert.False(camera.IsDetection);
        Assert.False(camera.IsMoving);
        Assert.Equal(SecurityCameraBeep.None, camera.Beep);
        Assert.Equal(pitch - Degrees(2.5f), camera.CurrentAlpha, 5);
        AssertScript(fixture.Variables, "SCamera_1301.isExploded", 1);
    }

    [RequiresGameDataFact]
    public void CameraRaisesItsAlarmAtOnceOnAGuardsBodyInView()
    {
        Fixture fixture = Build();
        SecurityCamera camera = fixture.Registry.Cameras[0];
        SecurityCameraSubject body = Soldier(TargetStraightAhead(camera, 5.0), alive: false);

        // 0x441BF0: a +428 target (own side, dead) starts with no countdown, so it is detected this tick.
        fixture.Registry.Tick(default, false, targetAlive: false, playerOnCamerasSide: false, [body]);

        Assert.Equal(0, camera.DetectionTime);
        Assert.True(camera.IsDetection);
        Assert.True(camera.IsBodyTarget);
        Assert.Equal(SecurityCameraBeep.BodyDetected, camera.Beep);
        AssertScript(fixture.Variables, "SCamera_1301.isDetection", 1);
    }

    [RequiresGameDataFact]
    public void CameraIgnoresALivingGuardOfItsOwnSide()
    {
        Fixture fixture = Build();
        SecurityCamera camera = fixture.Registry.Cameras[0];
        SecurityCameraSubject guard = Soldier(TargetStraightAhead(camera, 5.0), alive: true);

        // Scan flags 6 (0x4415C0) register the own side only when dead.
        for (int tick = 0; tick < 120; tick++)
        {
            fixture.Registry.Tick(default, false, targetAlive: false, playerOnCamerasSide: false, [guard]);
        }

        Assert.False(camera.IsDetection);
        Assert.Equal(SecurityCameraBeep.None, camera.Beep);
    }

    [RequiresGameDataFact]
    public void AVisiblePlayerOutranksABodyAndTheBodyIsTakenAtOnceWhenHeLeaves()
    {
        Fixture fixture = Build();
        SecurityCamera camera = fixture.Registry.Cameras[0];
        Vector3d playerFeet = TargetStraightAhead(camera, 5.0);
        SecurityCameraSubject body = Soldier(TargetStraightAhead(camera, 4.0), alive: false);

        // The +416 list (other side, alive) is read second and replaces the +428 pick.
        fixture.Registry.Tick(playerFeet, false, targetAlive: true, playerOnCamerasSide: false, [body]);
        Assert.Equal(89, camera.DetectionTime);
        Assert.False(camera.IsDetection);
        Assert.False(camera.IsBodyTarget);
        Assert.Equal(SecurityCameraBeep.Suspicious, camera.Beep);

        // The target changes to the body: its countdown restarts at zero.
        fixture.Registry.Tick(playerFeet, false, targetAlive: false, playerOnCamerasSide: false, [body]);
        Assert.Equal(0, camera.DetectionTime);
        Assert.True(camera.IsDetection);
        Assert.Equal(SecurityCameraBeep.BodyDetected, camera.Beep);
    }

    [RequiresGameDataFact]
    public void EveryMissionOneLensMeasuresBlastsFromItsHeadModelsBound()
    {
        Fixture fixture = Build();

        // The live game reports 1.0 x 2863.5869 for every intact 313_02_1 head: 0x4422F0 multiplies the
        // lens's extent by 0x4D0950's MESH+0x78.
        AlarmSpeaker[] cameras = [.. fixture.Speakers.Speakers.Where(speaker => speaker.AccumulatesDamage)];
        Assert.Equal(5, cameras.Length);
        Assert.All(cameras, camera => Assert.Equal(2863.5869f, camera.ExplosionContactRadiusUnits, 2));
    }

    private static SecurityCameraSubject Soldier(Vector3d feet, bool alive) =>
        new(new object(), new Vector3d(feet.X, feet.Y, feet.Z + 2828.8), CamerasSide: true, alive);

    private static Fixture Build(WorldTraceCast? sight = null)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LevelLoader levels = new(root, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(missions, fileSystem, resources, levels).Load(1);
        WorldScene scene = WorldScene.Build(mission, root);

        SecurityCameraRegistry registry = SecurityCameraRegistry.Build(root, loop.Variables, sight);
        registry.BindScene(scene.Placements);
        Assert.Equal(5, registry.Cameras.Count);
        Assert.Equal(2, registry.ControlCount);
        AlarmSpeakerRegistry speakers = AlarmSpeakerRegistry.Build(root, loop.Variables);
        speakers.BindScene(scene.Placements);
        return new Fixture(registry, loop.Variables, speakers);
    }

    private static Vector3d TargetStraightAhead(SecurityCamera camera, double metres)
    {
        double distance = metres * WorldScene.UnitsPerMetre;
        Vector3d target = new(
            camera.ViewPositionUnits.X + (camera.Orientation.M12 * distance),
            camera.ViewPositionUnits.Y + (camera.Orientation.M22 * distance),
            camera.ViewPositionUnits.Z + (camera.Orientation.M32 * distance));
        return new Vector3d(target.X, target.Y, target.Z - 2828.8);
    }

    private static float Degrees(float degrees) => degrees * MathF.PI / 180f;

    private static void AssertScript(ScriptVarRegistry variables, string name, int expected)
    {
        Assert.True(variables.TryRead(name, out QvmValue value), $"missing script variable {name}");
        Assert.Equal(expected, value.Integer);
    }

    private sealed record Fixture(
        SecurityCameraRegistry Registry,
        ScriptVarRegistry Variables,
        AlarmSpeakerRegistry Speakers);
}
