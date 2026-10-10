using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Checks the soldier spawn path — <c>0x456E50</c> + <c>0x45F890</c> — against the retail levels that
/// exercise it.
/// </summary>
public sealed class RetailSoldierViewTests
{
    private static readonly string[] ShippedSetNames = ["000", "001", "002", "003", "005", "006"];
    private static readonly int[] ShippedSkeletonIds = [0, 1, 2, 3, 5, 6];

    [RequiresGameDataFact]
    public void ThePacksLoadTheAnimationSetsTheirBanmChunkNames()
    {
        // Pack_Bind (0x4CF080) loads the BANM names before either archive binds. location0 names the
        // six shipped sets and no retail level pack names any; the ids come from each file's own BOSH.
        LoadedMission mission = Load(1, out _);

        Assert.Equal(ShippedSetNames, mission.CommonPack.Pack.AnimationSetNames);
        Assert.Empty(mission.LevelPack.Pack.AnimationSetNames);

        Assert.Equal(
            ShippedSkeletonIds,
            mission.CommonPack.AnimationSets.Select(s => s.Skeleton.Id).ToArray());

        // The fallback stand animation must resolve in every set, or 0x456E50's default would fatal.
        foreach (BoneAnimationSet set in mission.CommonPack.AnimationSets)
        {
            Assert.True(
                set.TryGet(HumanSoldierClass.Soldier.FallbackStandAnimation, out _),
                $"set {set.Skeleton.Id} lacks animation {HumanSoldierClass.Soldier.FallbackStandAnimation}");
        }
    }

    [RequiresGameDataFact]
    public void EveryLevelOneSoldierResolvesEndToEnd()
    {
        LoadedMission mission = Load(1, out LevelTask root);
        List<SoldierView> views = [];

        foreach (HumanSoldierTask soldier in FindAll<HumanSoldierTask>(root))
        {
            Assert.True(
                mission.TryGetAnimationSet(soldier.BoneHierarchy, out BoneAnimationSet? set),
                $"'{soldier.Name}' declares skeleton {soldier.BoneHierarchy}, which did not load");
            Assert.True(
                mission.TryGetModel(soldier.Model, out MefModel? model),
                $"'{soldier.Name}' names model '{soldier.Model}', which no pack holds");
            Assert.True(MefSkinner.IsSkinned(model!), $"'{soldier.Model}' is not a skinned model");

            views.Add(new SoldierView(soldier, set!));
        }

        Assert.Equal(29, views.Count);

        // Every declared stand animation in level 1 resolves - the "Animation #%d not found" fallback
        // exists in the binary but the shipped data never trips it here.
        Assert.All(
            views.Where(v => v.Soldier.StandAnimation >= 0),
            v => Assert.True(v.DeclaredStandAnimationFound, $"'{v.Soldier.Name}' stand {v.Soldier.StandAnimation}"));
    }

    [RequiresGameDataFact]
    public void MissionOneGameplayGuardsAreNotClassifiedAsCinematicActors()
    {
        _ = Load(1, out LevelTask root);
        HumanSoldierTask[] soldiers = [.. FindAll<HumanSoldierTask>(root)];
        HumanSoldierTask[] staged = [.. soldiers.Where(CinematicActor.IsStaged)];

        Assert.Contains(staged, soldier => soldier.Id == 1505);
        Assert.Contains(staged, soldier => soldier.Id == 2021);
        Assert.Contains(staged, soldier => soldier.Id == 4023);
        Assert.Equal(8, staged.Length);
        Assert.DoesNotContain(staged, soldier => soldier.Id is >= 2000 and <= 2025 && soldier.Id != 2021);
        Assert.True(soldiers.Length - staged.Length >= 20,
            $"classified {staged.Length} of {soldiers.Length} soldiers as cinematic");
    }

    [RequiresGameDataFact]
    public void APosedSoldierIsHumanSized()
    {
        // The skin and the pose come from different functions (0x49B700 and 0x4D3A90); a soldier
        // coming out person-shaped is the cheap end-to-end check that they compose. The bounds are in
        // metres after the skinner's scale.
        LoadedMission mission = Load(1, out LevelTask root);
        HumanSoldierTask soldier = FindAll<HumanSoldierTask>(root).First();

        Assert.True(mission.TryGetAnimationSet(soldier.BoneHierarchy, out BoneAnimationSet? set));
        Assert.True(mission.TryGetModel(soldier.Model, out MefModel? model));

        SoldierView view = new(soldier, set!);
        MeshGeometry geometry = MefMeshBuilder.Build(model!, 1f)!;
        Vertex3D[] skinned = [.. geometry.Vertices];
        int written = MefSkinner.Skin(model!, view.UnrotatedPose(), (float)(1.0 / WorldScene.UnitsPerMetre), skinned);

        // Only the base vertices are skinned output; the array's tail is the extra-influence records,
        // which no draw record indexes.
        Vector3f min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3f max = new(float.MinValue, float.MinValue, float.MinValue);
        foreach (Vertex3D vertex in skinned.Take(written))
        {
            min = new Vector3f(
                Math.Min(min.X, vertex.Position.X),
                Math.Min(min.Y, vertex.Position.Y),
                Math.Min(min.Z, vertex.Position.Z));
            max = new Vector3f(
                Math.Max(max.X, vertex.Position.X),
                Math.Max(max.Y, vertex.Position.Y),
                Math.Max(max.Z, vertex.Position.Z));
        }

        float height = max.Z - min.Z;
        Assert.InRange(height, 1.0f, 2.5f);
        Assert.InRange(max.X - min.X, 0.1f, 3f);
        Assert.InRange(max.Y - min.Y, 0.1f, 3f);
    }

    [RequiresGameDataFact]
    public void EverySoldierInTheCampaignResolves()
    {
        // The campaign-wide sweep: every soldier's skeleton loaded from BANM, every declared stand
        // animation is in its declared skeleton's set, and every model resolves as a skinned mesh -
        // except the ones retail itself cannot resolve. 0x4CEC10 falls back to a model literally named
        // "missing", no retail install ships one, and 0x45F890 skips the bind on a zero id, so those
        // soldiers are invisible in the original too. The list is pinned so a regression cannot hide
        // in it.
        int soldiers = 0;
        List<string> missingStands = [];
        List<string> unresolved = [];

        for (int level = 1; level <= 14; level++)
        {
            LoadedMission mission = Load(level, out LevelTask root);
            foreach (HumanSoldierTask soldier in FindAll<HumanSoldierTask>(root))
            {
                Assert.True(
                    mission.TryGetAnimationSet(soldier.BoneHierarchy, out BoneAnimationSet? set),
                    $"level {level} '{soldier.Name}' skeleton {soldier.BoneHierarchy} did not load");

                if (!mission.TryGetModel(soldier.Model, out MefModel? model))
                {
                    unresolved.Add($"level {level}: {soldier.Model}");
                }
                else
                {
                    Assert.True(MefSkinner.IsSkinned(model!), $"level {level} '{soldier.Model}' is not skinned");
                }

                SoldierView view = new(soldier, set!);
                if (soldier.StandAnimation >= 0 && !view.DeclaredStandAnimationFound)
                {
                    missingStands.Add($"level {level} '{soldier.Name}' stand {soldier.StandAnimation}");
                }

                soldiers++;
            }
        }

        Assert.True(soldiers > 300, $"only {soldiers} soldiers across the campaign");
        Assert.Empty(missingStands);
        Assert.Equal(["level 7: 003_01_1"], unresolved);
    }

    [RequiresGameDataFact]
    public void TheRunAndFireClipHoldsTheGunLevelAndTheTenseRunHoldsItDown()
    {
        // State 17 (the tense run) plays clip 48 and state 18 (firing on the run, 0x45AAD0) clip 52, from 0x53ED10.
        // The barrel's axis in the weapon bone's frame is taken from the standing fire clip (state 6, clip 56),
        // which aims level along the body: then each clip's pitch of that axis says where its gun points. A palette
        // rotation turns a bone-frame vector as M * v -- Transposed().Transform, Transform being the row-vector
        // form -- the way the held weapon is drawn from it.
        LoadedMission mission = Load(1, out LevelTask root);
        HumanSoldierTask soldier = FindAll<HumanSoldierTask>(root)
            .First(s => s.Class == HumanSoldierClass.Soldier && !CinematicActor.IsStaged(s));
        Assert.True(mission.TryGetAnimationSet(soldier.BoneHierarchy, out BoneAnimationSet? set));

        HumanBodyStateTable table = HumanSoldierStates.Table;
        int fire = table.AnimationOf(HumanSoldierStates.FiringState);
        int run = table.AnimationOf(HumanSoldierStates.RunState);
        int runFire = table.AnimationOf(HumanSoldierStates.RunFiringState);
        Assert.Equal((56, 48, 52), (fire, run, runFire));

        Vector3f forward = new(0f, 1f, 0f);
        // The inverse of M * v is the transpose's, M^T * v -- which is Transform.
        Vector3f barrel = GunRotations(soldier, set!, fire)[0].Transform(forward);
        float[] runPitch = [.. GunRotations(soldier, set!, run).Select(r => PitchDegrees(r.Transposed().Transform(barrel)))];
        float[] runFirePitch = [.. GunRotations(soldier, set!, runFire)
            .Select(r => PitchDegrees(r.Transposed().Transform(barrel)))];

        // Measured: clip 48 holds the muzzle 51-58 degrees below level, clip 52 within 5-7 degrees of it.
        string report = $"clip 48 pitch {runPitch.Min():0.0}..{runPitch.Max():0.0}; "
            + $"clip 52 pitch {runFirePitch.Min():0.0}..{runFirePitch.Max():0.0}";
        Assert.True(runFirePitch.All(pitch => Math.Abs(pitch) < 10f), report);
        Assert.True(runPitch.All(pitch => pitch < -45f), report);
    }

    // The weapon bone's object-space rotation at every tick of one pass through a clip.
    private static List<Matrix3f> GunRotations(HumanSoldierTask soldier, BoneAnimationSet set, int animation)
    {
        SoldierView view = new(soldier, set);
        Assert.True(view.PlayStateAnimation(animation, loops: true));
        List<Matrix3f> rotations = [];
        for (int tick = 0; tick < 30; tick++)
        {
            rotations.Add(view.UnrotatedPose().Rotations[HumanAttachPoints.ThirdPersonWeapon]);
            view.Tick();
        }

        return rotations;
    }

    private static float PitchDegrees(Vector3f direction)
    {
        float length = MathF.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        return MathF.Asin(direction.Z / length) * 180f / MathF.PI;
    }

    private static IEnumerable<T> FindAll<T>(GameTask task)
        where T : GameTask
    {
        foreach (GameTask child in task.Children)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (T deeper in FindAll<T>(child))
            {
                yield return deeper;
            }
        }
    }

    private static LoadedMission Load(int mission, out LevelTask root)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());

        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);

        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);

        PlayerTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);

        root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        LevelLoader loader = new(root, loop.TaskCommands, loop.Natives, resources);
        return new MissionLoader(registry, fileSystem, resources, loader, null, new TerrainLoader(resources, loop.Natives))
            .Load(mission);
    }
}
