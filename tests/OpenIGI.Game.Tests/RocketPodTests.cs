using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The Hind's rocket pods and sliding door (<c>0x427A70</c>, <c>0x4273B0</c>).</summary>
[Collection(RetailMissionWorld.Name)]
public sealed class RocketPodTests
{
    private static readonly Func<Vector3d, Vector3d, bool> Clear = static (_, _) => true;

    [Fact]
    public void APodMadeDrawsItsFirstGapBelowItsDelay()
    {
        IgiRandom expected = IgiRandom.Seeded();
        IgiRandom random = IgiRandom.Seeded();
        RocketPod pod = new(Vector3d.Zero, Matrix3f.Identity, 450);

        pod.Create(random);

        Assert.Equal((int)expected.NextBounded(450), pod.Countdown);
    }

    [Fact]
    public void APodFiresOnlyOnceItsCountdownIsBelowNought()
    {
        IgiRandom random = IgiRandom.Seeded();
        RocketPod pod = new(Vector3d.Zero, Matrix3f.Identity, 450) { Countdown = 1 };
        List<RocketPodLaunch> fired = [];
        Vector3d[] ahead = [new(0d, 40960d, 0d)];

        Assert.Equal(0, pod.Run(random, Vector3d.Zero, Matrix3f.Identity, 0f, ahead, Clear, fired.Add));
        Assert.Equal(0, pod.Countdown);
        Assert.Equal(0, pod.Run(random, Vector3d.Zero, Matrix3f.Identity, 0f, ahead, Clear, fired.Add));
        Assert.Equal(-1, pod.Countdown);
        Assert.Empty(fired);

        IgiRandom expected = IgiRandom.Seeded();
        Assert.Equal(1, pod.Run(random, Vector3d.Zero, Matrix3f.Identity, 0f, ahead, Clear, fired.Add));

        // The gap is the delay and a draw below it, less this run's step.
        Assert.Equal((int)expected.NextBounded(450) + 450 - 1, pod.Countdown);
    }

    [Fact]
    public void ARocketLeavesHalfAMetreAheadAtTheVehiclesSpeedAndFiveMore()
    {
        RocketPod pod = new(Vector3d.Zero, Matrix3f.Identity, 450) { Countdown = -1 };
        List<RocketPodLaunch> fired = [];
        Vector3d enemy = new(1000d, 40960d, -500d);

        pod.Run(IgiRandom.Seeded(), new Vector3d(10d, 20d, 30d), Matrix3f.Identity, 4096f / 30f, [enemy], Clear, fired.Add);

        RocketPodLaunch launch = Assert.Single(fired);
        Assert.Equal(new Vector3d(10d, 20d + 2048d, 30d), launch.Origin);
        Assert.Equal(6f, launch.SpeedMetresPerSecond, 5);
        Assert.Equal(enemy, launch.Target);
    }

    [Theory]
    [InlineData(0d, -4096d)]
    [InlineData(40960d, 4096d)]
    [InlineData(-40960d, 4096d)]
    public void APodHoldsItsFireAtWhatIsBehindItOrOutsideItsCone(double x, double y)
    {
        RocketPod pod = new(Vector3d.Zero, Matrix3f.Identity, 450) { Countdown = -1 };
        List<RocketPodLaunch> fired = [];

        pod.Run(IgiRandom.Seeded(), Vector3d.Zero, Matrix3f.Identity, 0f, [new Vector3d(x, y, 0d)], Clear, fired.Add);

        Assert.Empty(fired);
        Assert.Equal(-2, pod.Countdown);
    }

    [Fact]
    public void APodHoldsItsFireAtWhatItCannotSee()
    {
        RocketPod pod = new(Vector3d.Zero, Matrix3f.Identity, 450) { Countdown = -1 };
        List<RocketPodLaunch> fired = [];

        pod.Run(IgiRandom.Seeded(), Vector3d.Zero, Matrix3f.Identity, 0f, [new Vector3d(0d, 40960d, 0d)], static (_, _) => false, fired.Add);

        Assert.Empty(fired);
    }

    [Fact]
    public void ADoorSlidesToItsStopAndBack()
    {
        SlidingDoor door = new(new MagicHeliDoor(1, 81.920006f, 200f)) { IsOpen = true };

        door.Run();
        door.Run();
        Assert.Equal(163.84001f, door.Slide);
        door.Run();
        Assert.Equal(200f, door.Slide);
        Assert.Equal(new Vector3d(0d, 200d, 0d), door.Displacement);

        door.IsOpen = false;
        door.Run();
        door.Run();
        door.Run();
        Assert.Equal(0f, door.Slide);
    }

    [RequiresGameDataFact]
    public void TheHindsPodsAndDoorAreReadFromTheMagicObjectScript()
    {
        MagicObjectRegistry magic = RetailMissionData.Get(7).Magic;

        Assert.True(magic.TryGet("700_09_1", out MagicObjectDefinition pod));
        Assert.Equal(450, pod.RocketPodDelayTicks);
        Assert.True(magic.TryGet("700_05_1", out MagicObjectDefinition door));
        Assert.Equal(new MagicHeliDoor(1, 81.920006f, 10854.4f), door.HeliDoor);
    }

    [RequiresGameDataFact]
    public void EachOfMissionSevensHindsDrawsAGapForEachOfItsFourPodsAsTheLevelIsMade()
    {
        (LevelTask root, WorldScene world, ScriptVarRegistry variables, MagicObjectRegistry magic) = Load(7);
        CutsceneRuntime runtime = new(root, variables);
        IgiRandom random = IgiRandom.Seeded();
        int draws = 0;
        random.Drew = _ => draws++;
        runtime.VehicleRandom = random;
        runtime.ConfigureVehicleParts(
            world,
            name => magic.TryGet(name, out MagicObjectDefinition definition) ? definition : null,
            definition => magic.TaskTypeName(definition.TaskType));

        foreach (WorldPlacement placement in world.Placements)
        {
            runtime.VehiclePartMade(placement);
        }

        // Heli_353 and Heli_356 are the mission's two Hinds.
        Assert.Equal(8, draws);
    }

    private static (LevelTask Root, WorldScene World, ScriptVarRegistry Variables, MagicObjectRegistry Magic) Load(int missionId)
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        VehiclePhysicsRegistry physics = new();
        physics.Load(resources);
        LoadedMission mission = new MissionLoader(
            missions,
            fs,
            resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(missionId);
        return (root, WorldScene.Build(mission, root, magic, vehiclePhysics: physics), loop.Variables, magic);
    }
}
