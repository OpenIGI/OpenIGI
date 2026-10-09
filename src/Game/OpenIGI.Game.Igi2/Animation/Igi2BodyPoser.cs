using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Animation;

/// <summary>A human body's pose as its clips leave it, and the clip whose attach objects hang on it.</summary>
/// <param name="Pose">The bones as they stand, in the model's frame.</param>
/// <param name="AttachClip">The clip whose attach objects are hung: the upper body's while it plays, else the main one.</param>
/// <param name="AttachTime">That clip's time.</param>
public sealed record Igi2BodyPose(BonePose Pose, Igi2AnimationClip AttachClip, int AttachTime);

/// <summary>Poses a human body from its animation layer: the main clip on the centre branch, the upper-body clip outside the lower body.</summary>
/// <remarks>
/// <para>
/// The main clip poses the bones under <c>center</c>; while an upper-body clip plays it poses every bone outside the
/// <c>lower body</c> branch (<c>HumanAnim_Init</c>, <c>igi2 0x58F650</c>, marking the sub-hierarchies with
/// <c>Hierarchy_MarkSubHierarchy</c>, <c>0x57F3A0</c>). Bones are named by the model's <c>BNAM</c> chunk, as
/// <c>Hierarchy_GetBone</c> (<c>0x57F2F0</c>) finds them.
/// </para>
/// <para>
/// Not yet: the blends between clips, the turn of the upper body and head toward the aim (<c>HumanSoldier_ShouldersCallback</c>
/// and its kin), and the lying body's tilt; the pose is the clips a blend is heading for, sampled in double precision rather
/// than as <c>BoneDynCubeObj_UpdateHierarchy</c> (<c>0x564B50</c>) computes it.
/// </para>
/// </remarks>
public sealed class Igi2BodyPoser
{
    /// <summary>The bone the main clip poses from.</summary>
    public const string CenterBone = "center";

    /// <summary>The bone whose branch the upper-body clip leaves to the main clip.</summary>
    public const string LowerBodyBone = "lower body";

    private readonly Dictionary<Igi2BoneHierarchy, (BoneSkeleton Skeleton, bool[] Main, bool[] Upper)> _rigs = new(ReferenceEqualityComparer.Instance);
    private Quaternion32[] _main = [];
    private Quaternion32[] _upper = [];

    /// <summary>Poses a body.</summary>
    /// <param name="animation">The body's animation layer.</param>
    /// <param name="boneNames">The model's bone names, in bone order.</param>
    /// <returns>The pose, or <see langword="null"/> when the body has no rig or no clip on.</returns>
    public Igi2BodyPose? Pose(Igi2HumanAnimation animation, IReadOnlyList<string> boneNames)
    {
        ArgumentNullException.ThrowIfNull(animation);
        ArgumentNullException.ThrowIfNull(boneNames);
        if (animation.Rig is not { } rig || animation.Main.Clip is not { } main || main.BoneCount != rig.BoneCount)
        {
            return null;
        }

        if (!_rigs.TryGetValue(rig, out (BoneSkeleton Skeleton, bool[] Main, bool[] Upper) masks))
        {
            masks = (rig.CreateSkeleton(), Mark(rig, IndexOf(boneNames, CenterBone), include: true), Mark(rig, IndexOf(boneNames, LowerBodyBone), include: false));
            _rigs[rig] = masks;
        }

        if (_main.Length != rig.BoneCount)
        {
            _main = new Quaternion32[rig.BoneCount];
            _upper = new Quaternion32[rig.BoneCount];
        }

        Vector3f root = Igi2ClipSampler.Sample(main, animation.Main.Time, squad: false, _main);
        Igi2AnimationClip attachSource = main;
        int attachTime = animation.Main.Time;
        if (animation.UpperBody is { Clip: { } upper } upperController && upper.BoneCount == rig.BoneCount)
        {
            Igi2ClipSampler.Sample(upper, upperController.Time, squad: false, _upper);
            for (int bone = 0; bone < rig.BoneCount; bone++)
            {
                if (masks.Upper[bone])
                {
                    _main[bone] = _upper[bone];
                }
            }

            attachSource = upper;
            attachTime = upperController.Time;
        }

        for (int bone = 0; bone < rig.BoneCount; bone++)
        {
            if (!masks.Main[bone] && !masks.Upper[bone])
            {
                _main[bone] = Quaternion32.Identity;
            }
        }

        return new Igi2BodyPose(BonePose.Evaluate(masks.Skeleton, _main, root), attachSource, attachTime);
    }

    /// <summary>Marks a branch of a hierarchy, or everything but it, as <c>Hierarchy_MarkSubHierarchy</c> (<c>igi2 0x57F3A0</c>) does.</summary>
    /// <param name="rig">The hierarchy.</param>
    /// <param name="branch">The branch's bone, or <c>-1</c> for none.</param>
    /// <param name="include">Whether the branch is marked; otherwise everything else is.</param>
    /// <returns>One mark per bone.</returns>
    public static bool[] Mark(Igi2BoneHierarchy rig, int branch, bool include)
    {
        ArgumentNullException.ThrowIfNull(rig);
        bool[] marks = new bool[rig.BoneCount];
        for (int bone = 0; bone < marks.Length; bone++)
        {
            int parent = rig.Parents[bone];
            marks[bone] = bone == branch ? include : parent < 0 ? !include : marks[parent];
        }

        return marks;
    }

    /// <summary>A bone's index by its name, as <c>Hierarchy_GetBone</c> (<c>igi2 0x57F2F0</c>) finds it.</summary>
    /// <param name="names">The bones' names, in bone order.</param>
    /// <param name="name">The name.</param>
    /// <returns>The index, or <c>-1</c>.</returns>
    public static int IndexOf(IReadOnlyList<string> names, string name)
    {
        ArgumentNullException.ThrowIfNull(names);
        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
