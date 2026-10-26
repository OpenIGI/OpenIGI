using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Presentation.Tests;

public sealed class SoldierLevelOfDetailTests
{
    // Mission 1's LODSettings for bone objects: a first switch at 65% and a cutoff at 1600% of the radius, an ideal radius of 15 m.
    private static readonly ModelLodSettings Bones = new(65f * ModelLodSettings.Percent, 1600f * ModelLodSettings.Percent, 0.25f, 15f * 4096f);

    [RequiresGameDataFact]
    public void A_soldier_models_lower_levels_skin_through_its_skeleton()
    {
        LoadedMission mission = LoadRetailMission();
        Assert.True(mission.TryGetModel("000_01_1", out MefModel? first));

        IReadOnlyList<SoldierLevel> lower = SoldierLevelOfDetail.FindLower(mission, "000_01_1", first!, out int[] triangles);

        // 0x4CED50 counts the name's last character on: _2 to _5, each fewer triangles than the last.
        Assert.Equal([978, 796, 622, 165, 91], triangles);
        Assert.Equal(4, lower.Count);

        int bones = MefSkinner.HighestBone(first!) + 1;
        BonePose pose = BonePose.FromBones(
            Enumerable.Repeat(Matrix3f.Identity, bones).ToArray(),
            new Vector3f[bones],
            Enumerable.Repeat(BoneSkeleton.NoParent, bones).ToArray());
        foreach (SoldierLevel level in lower)
        {
            Assert.True(MefSkinner.HighestBone(level.Model) < bones);
            Vertex3D[] skinned = [.. level.Geometry.Vertices];
            Assert.True(MefSkinner.Skin(level.Model, pose, 1f / 4096f, skinned) > 0);
        }
    }

    [RequiresGameDataFact]
    public void A_soldier_gives_up_detail_with_distance_and_is_not_drawn_past_its_cutoff()
    {
        ModelLodPolicy lod = new(default, new Dictionary<string, float[]>(), bones: Bones);
        Assert.True(LoadRetailMission().TryGetModel("000_01_1", out MefModel? body));
        int[] triangles = [978, 796, 622, 165, 91];

        int Level(double metresAhead) =>
            lod.SelectBone("000_01_1", body!, triangles, new Vector3d(0d, metresAhead, 0d), new Camera3D(Vector3d.Zero, Ahead), 1f);

        Assert.Equal(0, Level(5d));
        Assert.True(Level(60d) > 0);
        Assert.True(Level(60d) <= Level(150d));
        Assert.Equal(-1, Level(2000d));

        // Without the level's settings for bone objects every body is drawn whole.
        ModelLodPolicy rigidOnly = new(default, new Dictionary<string, float[]>());
        Assert.Equal(0, rigidOnly.SelectBone("000_01_1", body!, triangles, new Vector3d(0d, 2000d, 0d), new Camera3D(Vector3d.Zero, Ahead), 1f));
    }

    // A view whose depth axis (its third row) is the world's +Y.
    private static readonly Matrix3f Ahead = Matrix3f.FromRows(new Vector3f(1f, 0f, 0f), new Vector3f(0f, 0f, 1f), new Vector3f(0f, 1f, 0f));

    private static LoadedMission LoadRetailMission()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LevelLoader level = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        return new MissionLoader(missions, fileSystem, resources, level).Load(1);
    }
}
