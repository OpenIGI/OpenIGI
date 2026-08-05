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

        for (int i = 0; i < scene.Ladders.Count; i++)
        {
            LadderPlacement ladder = scene.Ladders[i];
            // The head is above the foot, and the climb is a sensible height — between half a metre and
            // forty metres — and divides into at least one rung.
            double height = ladder.Top.Z - ladder.Bottom.Z;
            Assert.True(height > 0.0, "the head should sit above the foot");
            Assert.InRange(height, 0.5 * 4096.0, 40.0 * 4096.0);
            Assert.True(ladder.ClimbLine.StepCount >= 1);

            // sub_4113E0 reads raw Real32x9 columns one/two. In Matrix3f's one-based API those are
            // Column2/Column3: the former is the wall-normal stand-off, the latter is the vertical lift.
            Vector3d mountOffset = new(
                ladder.BottomMount.X - ladder.Bottom.X,
                ladder.BottomMount.Y - ladder.Bottom.Y,
                ladder.BottomMount.Z - ladder.Bottom.Z);
            Assert.Equal(LadderPlacement.BottomStandoffUnits, Dot(mountOffset, ladder.Orientation.Column2), 2);
            Assert.Equal(LadderPlacement.BottomVerticalOffsetUnits, Dot(mountOffset, ladder.Orientation.Column3), 2);
            Assert.Equal(0.0, Dot(mountOffset, ladder.Orientation.Column1), 2);
        }
    }

    [RequiresGameDataFact]
    public void AMountedLadderIsClimbedFootToHead()
    {
        WorldScene scene = LoadScene(1);
        LadderPlacement ladder = scene.Ladders.First(static l => l.ClimbLine.StepCount >= 4);

        // Stand on the positive side of the ladder's normal, facing back into it, within the authored
        // vertical interval between the task origin and magic vertex 1.
        Vector3f normal = ladder.Orientation.Column2;
        double z = ladder.Origin.Z + ((ladder.ActivationTop.Z - ladder.Origin.Z) * 0.5);
        Vector3d beside = new(
            ladder.Origin.X + (normal.X * 2048.0),
            ladder.Origin.Y + (normal.Y * 2048.0),
            z);
        Assert.True(ladder.CanActivate(beside, ladder.PlayerYaw, out bool atTop));
        Assert.False(atTop);
    }

    [RequiresGameDataFact]
    public void MissionOneLaddersActivateFromAGroundedViewWithoutJumping()
    {
        WorldScene scene = LoadScene(1);
        int checkedLadders = 0;

        foreach (LadderPlacement ladder in scene.Ladders)
        {
            double viewZ = ladder.Bottom.Z + PlayerBody.StandingEyeHeight;
            if (viewZ <= ladder.Origin.Z || viewZ >= ladder.ActivationTop.Z)
            {
                continue;
            }

            Vector3f outside = ladder.Orientation.Column2;
            Vector3d standingView = new(
                ladder.Origin.X + (outside.X * (LadderPlacement.ActivateRadiusUnits - 1.0)),
                ladder.Origin.Y + (outside.Y * (LadderPlacement.ActivateRadiusUnits - 1.0)),
                viewZ);

            Assert.True(ladder.CanActivate(standingView, ladder.PlayerYaw, out bool atTop));
            Assert.False(atTop);
            checkedLadders++;
        }

        Assert.True(checkedLadders > 0);
    }

    [RequiresGameDataFact]
    public void MissionOneTaskNormalsMatchTheExteriorWallNormals()
    {
        WorldScene scene = LoadScene(1);
        ObjectCollisionIndex collision = ObjectCollisionIndex.Build(scene.Placements);

        for (int i = 0; i < scene.Ladders.Count; i++)
        {
            LadderPlacement ladder = scene.Ladders[i];
            Vector3f outside = ladder.Orientation.Column2;
            Vector3d centre = new(ladder.Bottom.X, ladder.Bottom.Y, ladder.BottomMount.Z);
            Vector3d inside = Offset(centre, outside, -8192.0);
            Vector3d exterior = Offset(centre, outside, 8192.0);

            Assert.True(collision.TryCast(exterior, inside, out WorldRayHit wall));
            Assert.True(Dot(wall.Normal, outside) > 0.99, "the task normal must point out of the wall");
        }
    }

    [RequiresGameDataFact]
    public void TheWaterTowerLadderMountsOnItsVisibleNorthSide()
    {
        WorldScene scene = LoadScene(1);

        // WaterTower's 435_02_1 is mission one's tallest ladder. Its parent and attachment are both
        // turned through 180 degrees, so the spawned task frame is identity and its outward normal is
        // +Y. Applying another blanket half-turn put the human south, inside the tower.
        LadderPlacement waterTower = scene.Ladders.MaxBy(l => l.Top.Z - l.Bottom.Z)!;

        Assert.True(waterTower.Orientation.Column2.Y > 0.999f);
        Assert.True(waterTower.BottomMount.Y > waterTower.Bottom.Y);
        Assert.Equal(MathF.PI, waterTower.Facing, 4);
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

    private static double Dot(Vector3d value, Vector3f axis) =>
        (value.X * axis.X) + (value.Y * axis.Y) + (value.Z * axis.Z);

    private static double Dot(Vector3f value, Vector3f axis) =>
        (value.X * axis.X) + (value.Y * axis.Y) + (value.Z * axis.Z);

    private static Vector3d Offset(Vector3d origin, Vector3f axis, double distance) => new(
        origin.X + (axis.X * distance),
        origin.Y + (axis.Y * distance),
        origin.Z + (axis.Z * distance));

}
