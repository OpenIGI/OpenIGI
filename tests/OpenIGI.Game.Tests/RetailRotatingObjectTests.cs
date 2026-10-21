using System.Linq;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Verifies the rotating objects in retail levels, specifically Level 5's radar tower rotor
/// (model 436_02_1 on 436_01_1) and radar dome rotating tasks.
/// </summary>
public sealed class RetailRotatingObjectTests
{
    [RequiresGameDataFact]
    public void LevelThreeReadsDeclaredRotationAccelerationAndCondition()
    {
        RotatingObjectRegistry registry = LoadRegistry(3);
        PlacedRotatingObject rotor = Assert.Single(
            registry.Objects,
            obj => !obj.Placement.IsAttached && obj.Placement.Instance.Name == "436_02_1");

        Assert.Equal(0f, rotor.TargetSpeedX);
        Assert.Equal(0f, rotor.TargetSpeedY);
        Assert.Equal(90f, rotor.TargetSpeedZ);
        Assert.Equal(0f, rotor.AccelX);
        Assert.Equal(0f, rotor.AccelY);
        Assert.Equal(1f, rotor.AccelZ);
        Assert.Equal("1", rotor.OnExpression);
        Assert.Equal(string.Empty, rotor.Sound);

        registry.Tick();

        Assert.True(rotor.IsActive);
        Assert.Equal(3f, rotor.AngleZ, precision: 5);
    }

    [RequiresGameDataFact]
    public void LevelFiveDiscoversRadarTowerRotorAndRadarDome()
    {
        RotatingObjectRegistry registry = LoadRegistry(5);

        Assert.True(registry.Count >= 2, $"Expected at least 2 rotating objects in Level 5, found {registry.Count}");

        // Find the attached radar tower rotor and the radar dome rotor
        PlacedRotatingObject? towerRotor = null;
        PlacedRotatingObject? domeRotor = null;
        foreach (PlacedRotatingObject obj in registry.Objects)
        {
            if (obj.Placement.IsAttached && obj.Placement.Instance.Name == "436_02_1")
            {
                towerRotor = obj;
            }
            else if (!obj.Placement.IsAttached && obj.Placement.Instance.Name == "436_02_1")
            {
                domeRotor = obj;
            }
        }

        Assert.NotNull(towerRotor);
        Assert.NotNull(domeRotor);

        // Tower rotor parameters (from TASKTYPE_RADARDISH: speed=90, accel=15)
        Assert.Equal(90f, towerRotor.TargetSpeedZ);
        Assert.Equal(15f, towerRotor.AccelZ);

        // The separate dome is a declared RotatingObject even though its model also appears in
        // magicobj.qvm. Its task-authored behavior must win over the model-level radar defaults.
        Assert.Equal(0f, domeRotor.TargetSpeedX);
        Assert.Equal(0f, domeRotor.TargetSpeedY);
        Assert.Equal(25f, domeRotor.TargetSpeedZ);
        Assert.Equal(0f, domeRotor.AccelX);
        Assert.Equal(0f, domeRotor.AccelY);
        Assert.Equal(0f, domeRotor.AccelZ);
        Assert.Equal("0", domeRotor.OnExpression);
        Assert.Equal("generator_run", domeRotor.Sound);

        // Initial orientation transform check
        Assert.True(registry.TryGetDrawTransform(
            towerRotor.Placement,
            tickFraction: 0f,
            smooth: false,
            out Vector3d pos0,
            out Matrix3f ori0));

        Assert.Equal(towerRotor.Placement.Position, pos0);
        Assert.Equal(towerRotor.Placement.Orientation, ori0);

        // Tick 30 times (1 second of game time at 30 Hz)
        for (int i = 0; i < 30; i++)
        {
            registry.Tick();
        }

        // Rotor has accelerated and rotated
        Assert.True(towerRotor.CurrentSpeedZ > 0f);
        Assert.True(towerRotor.AngleZ > 0f);

        // Retail authors this separate RotatingObject with the false expression "0", which zeroes its target speed
        // (0x439090) -- but the post-load (0x4395D0) starts it at its authored 25 degrees a second and it has no
        // acceleration to slow by, so it never stops.
        Assert.False(domeRotor.IsActive);
        Assert.Equal(0f, domeRotor.TargetSpeedZ);
        Assert.Equal(25f, domeRotor.CurrentSpeedZ);
        Assert.InRange(domeRotor.AngleZ, 24.999f, 25.001f);

        Assert.True(registry.TryGetDrawTransform(
            towerRotor.Placement,
            tickFraction: 1f,
            smooth: false,
            out Vector3d pos1,
            out Matrix3f ori1));

        Assert.Equal(towerRotor.Placement.Position, pos1);
        // Orientation should have changed from original orientation
        Assert.NotEqual(towerRotor.Placement.Orientation, ori1);

        // Presented orientation with smooth interpolation should differ between fraction 0 and 1
        Matrix3f oriInterp0 = towerRotor.PresentedOrientation(0f, true);
        Matrix3f oriInterp1 = towerRotor.PresentedOrientation(1f, true);
        Assert.NotEqual(oriInterp0, oriInterp1);
    }

    [RequiresGameDataFact]
    public void SwitchingOffForOneTickRunsTheRotorDownForGood()
    {
        // 0x439090 zeroes the target speeds themselves; nothing restores them, so an expression false for a moment (Mission
        // 8's fan, on while !Switch_260.isPressed) stops the object for the rest of the level.
        RotatingObjectRegistry registry = LoadRegistry(3);
        PlacedRotatingObject rotor = Assert.Single(
            registry.Objects,
            obj => !obj.Placement.IsAttached && obj.Placement.Instance.Name == "436_02_1");
        Assert.Equal(90f, rotor.CurrentSpeedZ);

        rotor.TickRotatingObject(switchedOff: true);
        for (int tick = 0; tick < 30; tick++)
        {
            rotor.TickRotatingObject(switchedOff: false);
        }

        Assert.Equal(0f, rotor.TargetSpeedZ);
        Assert.False(rotor.IsActive);

        // A degree a second squared: 31 ticks take a thirtieth of a degree a second each off 90.
        Assert.InRange(rotor.CurrentSpeedZ, 88.96f, 88.97f);
    }

    [RequiresGameDataFact]
    public void AShotRadarDishRunsDownAndStaysStill()
    {
        // RadarDish's WeaponHit (0x449260) zeroes the speed it wants; its update (0x448E30) then takes its acceleration off
        // a thirtieth at a time. Level 5's tower dish turns at 90 degrees a second and slows by 15.
        RotatingObjectRegistry registry = LoadRegistry(5);
        PlacedRotatingObject dish = Assert.Single(
            registry.Objects,
            obj => obj.Placement.IsAttached && obj.Placement.Instance.Name == "436_02_1");

        registry.WeaponHit(dish.Owner, dish.Placement);
        registry.Tick();

        Assert.Equal(0f, dish.TargetSpeedZ);
        Assert.InRange(dish.CurrentSpeedZ, 89.49f, 89.51f);

        for (int tick = 0; tick < 180; tick++)
        {
            registry.Tick();
        }

        float angle = dish.AngleZ;
        registry.Tick();
        Assert.Equal(0f, dish.CurrentSpeedZ);
        Assert.Equal(angle, dish.AngleZ);
    }

    [RequiresGameDataFact]
    public void ARoundLeavesARotorThatIsNotDestructableTurning()
    {
        // RotatingObject's WeaponHit (0x439750) breaks it only when it is Destructable, which no shipped one is.
        RotatingObjectRegistry registry = LoadRegistry(3);
        PlacedRotatingObject rotor = Assert.Single(
            registry.Objects,
            obj => !obj.Placement.IsAttached && obj.Placement.Instance.Name == "436_02_1");

        registry.WeaponHit(rotor.Owner, rotor.Placement);
        rotor.TickRotatingObject(switchedOff: false);

        Assert.False(rotor.IsDestructable);
        Assert.False(rotor.IsBroken);
        Assert.Equal(90f, rotor.TargetSpeedZ);
        Assert.True(rotor.IsActive);
    }

    [RequiresGameDataFact]
    public void ARotorsAngleWrapsOnlyFromAbove()
    {
        RotatingObjectRegistry registry = LoadRegistry(3);
        PlacedRotatingObject rotor = Assert.Single(
            registry.Objects,
            obj => !obj.Placement.IsAttached && obj.Placement.Instance.Name == "436_02_1");

        // 90 degrees a second: three degrees a tick, a full turn in 120 ticks and back under 360 on the next.
        for (int tick = 0; tick < 121; tick++)
        {
            rotor.TickRotatingObject(switchedOff: false);
        }

        Assert.InRange(rotor.AngleZ, 2.99f, 3.01f);
    }

    private static RotatingObjectRegistry LoadRegistry(int levelNumber)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);

        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

        MissionRegistry missionRegistry = new();
        missionRegistry.Register(loop.Natives);
        missionRegistry.Scan(fileSystem, resources, loop.Natives);

        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);

        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(missionRegistry, fileSystem, resources, levelLoader).Load(levelNumber);
        new ConditionalContainerGate(loop.Variables).Prepare(levelRoot);

        WorldScene scene = WorldScene.Build(mission, levelRoot, magic);
        return RotatingObjectRegistry.Build(levelRoot, scene.Placements, loop.Variables, magic);
    }
}
