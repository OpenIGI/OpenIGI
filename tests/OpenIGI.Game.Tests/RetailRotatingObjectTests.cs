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

namespace OpenIGI.Game.Tests;

/// <summary>
/// Verifies the rotating objects in retail levels, specifically Level 5's radar tower rotor
/// (model 436_02_1 on 436_01_1) and radar dome rotating tasks.
/// </summary>
public sealed class RetailRotatingObjectTests
{
    [RequiresGameDataFact]
    public void LevelFiveDiscoversRadarTowerRotorAndRadarDome()
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
        LoadedMission mission = new MissionLoader(missionRegistry, fileSystem, resources, levelLoader).Load(5);
        new ConditionalContainerGate(loop.Variables).Prepare(levelRoot);

        WorldScene scene = WorldScene.Build(mission, levelRoot, magic);
        RotatingObjectRegistry registry = RotatingObjectRegistry.Build(levelRoot, scene.Placements, loop.Variables, magic);

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
}
