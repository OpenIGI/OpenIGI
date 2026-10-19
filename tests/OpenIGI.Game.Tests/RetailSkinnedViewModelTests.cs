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
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Skins the player's own arms against the first-person rig — the other half of what <c>0x4644A0</c>
/// draws, and the part that needs the CPU skinner.
/// </summary>
public sealed class RetailSkinnedViewModelTests
{
    [RequiresGameDataFact]
    public void TheArmsAreASkinnedModelThatNeedsTheFiftySixBoneRig()
    {
        MefModel arms = ViewModel();

        Assert.Equal(MefMeshFormat.Skinned, arms.Format);
        Assert.True(MefSkinner.IsSkinned(arms));

        // 005_01_1 is one of exactly two shipped models reaching past bone 32, which is what ties it to
        // 005.iff -- and 0x464660 names both as literals, so the two halves agree.
        Assert.Equal(48, MefSkinner.HighestBone(arms));
        Assert.True(arms.BaseVertexCount <= MefSkinner.MaximumVertices);
        Assert.True(arms.ExtraInfluenceCount > 0);
    }

    [RequiresGameDataFact]
    public void TheInfluencesOfEveryVertexSumToOne()
    {
        MefModel arms = ViewModel();
        double[] totals = new double[arms.BaseVertexCount];

        for (int i = 0; i < arms.BaseVertexCount; i++)
        {
            totals[i] = arms.SkinInfluences[i].Weight;
        }

        for (int i = arms.BaseVertexCount; i < arms.SkinInfluences.Length; i++)
        {
            MefSkinInfluence extra = arms.SkinInfluences[i];
            totals[extra.TargetVertex] += extra.Weight;
        }

        // This is the arithmetic that proves the last eight bytes of a format-1 vertex are a rig and
        // not a lightmap UV: it means nothing under the other reading and comes out exact under this
        // one. It is also what makes the base vertex's own float a weight rather than a scale.
        Assert.All(totals, t => Assert.Equal(1d, t, 3));
    }

    [RequiresGameDataFact]
    public void TheSkinnedArmsSurroundTheWeaponBone()
    {
        MefModel arms = ViewModel();
        HumanView view = new(ViewSet());
        Assert.True(RetailWeaponRoster.Value.TryGetByGunModel("133_01_1", out WeaponType knife));
        view.PlayWeaponAnimation(knife.StandAnimation, loops: true);

        Vertex3D[] skinned = new Vertex3D[arms.Vertices.Length];
        int written = MefSkinner.Skin(arms, view.Bones.Pose(), 1f, skinned);
        Assert.Equal(arms.BaseVertexCount, written);

        // The hand has to be where the weapon is. Bone 3 is the attachment, so the closest skinned
        // vertex to it must be within a hand's width -- which is what says the skinner and the
        // attachment agree, rather than each being separately plausible.
        Vector3f bone = view.Bones.Pose().Positions[HumanAttachPoints.FirstPersonWeapon];
        double nearest = double.MaxValue;
        for (int i = 0; i < written; i++)
        {
            Vector3f p = skinned[i].Position;
            double dx = p.X - bone.X;
            double dy = p.Y - bone.Y;
            double dz = p.Z - bone.Z;
            nearest = Math.Min(nearest, Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)));
        }

        // One metre is 4096 engine units, so this is 5 cm.
        Assert.True(nearest < 205d, $"The nearest skinned vertex is {nearest / 4096d:0.000} m from the hand bone.");
    }

    [RequiresGameDataFact]
    public void PosingTheArmsMovesThemAwayFromTheRestPose()
    {
        MefModel arms = ViewModel();
        HumanView view = new(ViewSet());
        Assert.True(RetailWeaponRoster.Value.TryGetByGunModel("133_01_1", out WeaponType knife));
        view.PlayWeaponAnimation(knife.StandAnimation, loops: true);

        Vertex3D[] skinned = new Vertex3D[arms.Vertices.Length];
        MefSkinner.Skin(arms, view.Bones.Pose(), 1f, skinned);

        // The unskinned model is the artist's bind pose, and skinning it must not be the identity --
        // a skinner that quietly did nothing would still draw arms, just the wrong ones.
        int moved = 0;
        for (int i = 0; i < arms.BaseVertexCount; i++)
        {
            Vector3f before = arms.Vertices[i].Position;
            Vector3f after = skinned[i].Position;
            if (Math.Abs(before.X - after.X) + Math.Abs(before.Y - after.Y) + Math.Abs(before.Z - after.Z) > 1f)
            {
                moved++;
            }
        }

        Assert.True(moved > arms.BaseVertexCount / 2, $"Only {moved} of {arms.BaseVertexCount} vertices moved.");
    }

    [RequiresGameDataFact]
    public void SkinningAgainstTheWrongSkeletonIsRefused()
    {
        MefModel arms = ViewModel();
        BoneAnimationSet body = BoneAnimationSet.Read(File.ReadAllBytes(
            Path.Combine(GameData.RequireRoot(), "common", "anims", "000.iff")));

        BoneObject bones = new(body);
        Vertex3D[] destination = new Vertex3D[arms.Vertices.Length];

        // Bone 48 does not exist in the 33-bone rig, and reading past the palette would be silent
        // corruption rather than an error.
        Assert.Throws<ArgumentException>(() => MefSkinner.Skin(arms, bones.Pose(), 1f, destination));
    }

    private static BoneAnimationSet ViewSet() =>
        BoneAnimationSet.Read(File.ReadAllBytes(
            Path.Combine(GameData.RequireRoot(), "common", "anims", "005.iff")));

    private static MefModel ViewModel()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);

        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        MissionLoader loader = new(registry, fileSystem, resources, levelLoader);
        LoadedMission mission = loader.Load(1);

        Assert.True(mission.TryGetModel(HumanView.ModelName, out MefModel? model) && model is not null);
        return model!;
    }
}
