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

/// <summary>Each level's own "Mapcomputer on expression" decides whether its player has the map (0x45DCF0).</summary>
public sealed class MapComputerGateTests
{
    /// <summary>Levels that never write the slot have the map; 7 and 14 write 0 and never do.</summary>
    /// <remarks>The probe's computer hook set sees mission 7 open on its objectives page (mode 2) with the flag clear.</remarks>
    [RequiresGameDataTheory]
    [InlineData(1, true)]
    [InlineData(7, false)]
    [InlineData(14, false)]
    public void A_level_without_an_expression_has_the_map_and_one_that_writes_zero_does_not(int mission, bool expected)
    {
        (LevelTask root, GameLoop loop) = Load(mission);
        Assert.Equal(expected, new MapComputerGate(root, loop.Variables).MapAvailable);
    }

    /// <summary>Mission 8 starts without the map and gains it with GenericPickup 860.</summary>
    [RequiresGameDataFact]
    public void Mission_eight_gains_the_map_with_its_pickup()
    {
        (LevelTask root, GameLoop loop) = Load(8);
        _ = GenericPickupRegistry.Build(root, loop.Variables);
        MapComputerGate gate = new(root, loop.Variables);
        Assert.False(gate.MapAvailable);

        new LevelExpressionRuntime(loop.Variables).Execute("GenericPickup_860.isPickedUp = 1");
        Assert.True(gate.MapAvailable);
    }

    /// <summary>Mission 13 has the map except while the player is inside AreaActivate 3999.</summary>
    [RequiresGameDataFact]
    public void Mission_thirteen_loses_the_map_inside_its_area()
    {
        (LevelTask root, GameLoop loop) = Load(13);
        _ = new MissionObjectiveRuntime(root, loop.Variables);
        MapComputerGate gate = new(root, loop.Variables);
        Assert.True(gate.MapAvailable);

        new LevelExpressionRuntime(loop.Variables).Execute("AreaActivate_3999.nActive = 1");
        Assert.False(gate.MapAvailable);
    }

    private static (LevelTask Root, GameLoop Loop) Load(int mission)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        _ = new MissionLoader(missions, fileSystem, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(mission);
        return (root, loop);
    }
}
