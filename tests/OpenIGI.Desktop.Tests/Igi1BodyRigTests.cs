using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>A body model posed for a viewer as the level wears it, on the skeleton and in the stand clip the game uses.</summary>
public sealed class Igi1BodyRigTests
{
    /// <summary>
    /// A soldier's body is on its Bone Heirachy and the player's on a soldier's rig (its own clip is a still frame made to be
    /// seen from inside), each opening on stand clip 2, standing upright and moving; a level of detail nobody names takes its family's rig; the arms keep their own rig with no borrowed clip.
    /// </summary>
    [RequiresGameDataFact]
    public void Bodies_are_posed_as_the_level_wears_them()
    {
        (LoadedMission mission, LevelTask root) = Load();
        IReadOnlyDictionary<string, Igi1BodyRig> rigs = Igi1ModelAnimator.Rigs(root);
        Assert.Equal(new Igi1BodyRig(1, 2), rigs["003_01_1"]);

        foreach ((string name, int skeleton) in (ValueTuple<string, int>[])[("000_01_1", 1), ("000_01_4", 1), ("003_01_1", 1), ("001_01_2", 1)])
        {
            Assert.True(mission.TryGetModel(name, out MefModel? model), name);
            Igi1ModelAnimator animator = Igi1ModelAnimator.For(model!, mission, name, rigs)!;
            Assert.Equal(skeleton, animator.Skeleton);
            Assert.Equal("Clip 2", animator.Clips[animator.StandClip].Split(" (")[0]);

            Vertex3D[] vertices = [.. model!.Vertices.ToArray().Select(static v => new Vertex3D(v.Position, v.Normal, v.DiffuseU, v.DiffuseV))];
            Vertex3D[] later = (Vertex3D[])vertices.Clone();
            animator.Pose(animator.StandClip, 0f, vertices);
            animator.Pose(animator.StandClip, animator.Seconds(animator.StandClip) * 0.5f, later);
            Assert.True(vertices.Zip(later).Any(static p => (p.First.Position - p.Second.Position).Length > 1f), name + " does not move");
            float Extent(Func<Vertex3D, float> axis) => vertices.Max(axis) - vertices.Min(axis);
            Assert.True(Extent(static v => v.Position.Z) > 2f * Extent(static v => v.Position.Y), name);
        }

        Assert.True(mission.TryGetModel("005_01_1", out MefModel? arms));
        Assert.Equal(-1, Igi1ModelAnimator.For(arms!, mission, "005_01_1", rigs)!.StandClip);
    }

    /// <summary>
    /// An animation set file plays on the body that wears its skeleton: the soldiers' set on a soldier, opening on his stand
    /// clip; the hands' 56-bone set on the arms.
    /// </summary>
    [RequiresGameDataFact]
    public void Set_files_play_on_the_body_that_wears_them()
    {
        (LoadedMission mission, LevelTask root) = Load();
        IReadOnlyDictionary<string, Igi1BodyRig> rigs = Igi1ModelAnimator.Rigs(root);

        AnimationFilePreview soldiers = Igi1ModelAnimator.PreviewFile(File.ReadAllBytes(GameData.Resolve("common/anims/001.iff")), mission, rigs)!;
        Assert.Equal(1, rigs[soldiers.BodyName].Skeleton);
        Assert.Equal("Clip 2", soldiers.Animator.Clips[soldiers.OpenClip].Split(" (")[0]);

        AnimationFilePreview hands = Igi1ModelAnimator.PreviewFile(File.ReadAllBytes(GameData.Resolve("common/anims/005.iff")), mission, rigs)!;
        Assert.Equal("005_01_1", hands.BodyName);
        Assert.Equal(-1, hands.OpenClip);
    }

    private static (LoadedMission Mission, LevelTask Root) Load()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        OpenIGI.Game.Flow.LevelTaskTypes.Register(loop.Types);
        OpenIGI.Game.Igi1.Igi1TaskTypes.Register(loop.Types);
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        LoadedMission mission = new MissionLoader(
            missions, fileSystem, resources, new LevelLoader(root, loop.TaskCommands, loop.Natives, resources), terrainLoader: new TerrainLoader(resources, loop.Natives))
            .Load(1);
        return (mission, root);
    }
}
