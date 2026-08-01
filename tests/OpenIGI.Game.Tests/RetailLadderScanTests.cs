using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Checks that the scene collects the ladders a retail level attaches to its buildings, with plausible
/// climb geometry.
/// </summary>
public sealed class RetailLadderScanTests
{
    [RequiresGameDataTheory]
    [InlineData(1)]
    public void ALevelPlacesClimbableLadders(int missionId)
    {
        WorldScene scene = LoadScene(missionId);

        // magicobj.qvm defines a dozen TASKTYPE_LADDER models and level 1's buildings attach several.
        Assert.NotEmpty(scene.Ladders);

        foreach (LadderPlacement ladder in scene.Ladders)
        {
            // The head is above the foot, and the climb is a sensible height — between half a metre and
            // forty metres — and divides into at least one rung.
            double height = ladder.Top.Z - ladder.Bottom.Z;
            Assert.True(height > 0.0, "the head should sit above the foot");
            Assert.InRange(height, 0.5 * 4096.0, 40.0 * 4096.0);
            Assert.True(ladder.ClimbLine.StepCount >= 1);
        }
    }

    [RequiresGameDataFact]
    public void AMountedLadderIsClimbedFootToHead()
    {
        WorldScene scene = LoadScene(1);
        LadderPlacement ladder = scene.Ladders.First(static l => l.ClimbLine.StepCount >= 4);

        // Stand just off the foot, looking toward the ladder, and it offers a bottom mount.
        Vector3d beside = new(ladder.Bottom.X - 2048, ladder.Bottom.Y, ladder.Bottom.Z);
        float approach = (float)Math.Atan2(ladder.Bottom.X - beside.X, ladder.Bottom.Y - beside.Y);
        Assert.True(ladder.CanActivate(beside, approach, out bool atTop));
        Assert.False(atTop);
    }

    private static WorldScene LoadScene(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);

        GameLoop loop = new(new ManualTimeSource());
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);

        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);

        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(registry, fileSystem, resources, levelLoader).Load(missionId);

        return WorldScene.Build(mission, levelRoot, magic);
    }
}
