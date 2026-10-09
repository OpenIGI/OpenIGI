using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.ViewModel;

/// <summary>A human body as one tick left it, for drawing: its skinned model posed by its clips, and what its clips hang on it.</summary>
/// <remarks>
/// <para>
/// The main clip poses the bones under <c>center</c>; while an upper-body clip plays it poses every bone outside the
/// <c>lower body</c> branch (<c>HumanAnim_Init</c>, <c>igi2 0x58F650</c>, marking the sub-hierarchies with
/// <c>Hierarchy_MarkSubHierarchy</c>, <c>0x57F3A0</c>). Bones are named by the model's <c>BNAM</c> chunk, as
/// <c>Hierarchy_GetBone</c> (<c>0x57F2F0</c>) finds them.
/// </para>
/// <para>
/// Not yet: the blends between clips, the turn of the upper body and head toward the aim (<c>HumanSoldier_ShouldersCallback</c>
/// and its kin), and the lying body's tilt; the frame shows the clips a blend is heading for.
/// </para>
/// </remarks>
public sealed class Igi2BodyFrame
{
    /// <summary>The bone the main clip poses from.</summary>
    public const string CenterBone = "center";

    /// <summary>The bone whose branch the upper-body clip leaves to the main clip.</summary>
    public const string LowerBodyBone = "lower body";

    private readonly Dictionary<Igi2BoneHierarchy, (BoneSkeleton Skeleton, bool[] Main, bool[] Upper)> _rigs = new(ReferenceEqualityComparer.Instance);
    private Quaternion32[] _main = [];
    private Quaternion32[] _upper = [];

    /// <summary>Builds the models a body draws as this tick.</summary>
    /// <param name="human">The body.</param>
    /// <param name="animation">The body's animation layer.</param>
    /// <param name="model">The body's model name.</param>
    /// <param name="boneNames">The model's bone names, in bone order.</param>
    /// <param name="heldModel">The weapon in hand's fixed model, or <see langword="null"/>.</param>
    /// <param name="tick">The tick the frame is for.</param>
    /// <returns>The body and what hangs on it; empty when the body has no rig or no clip on.</returns>
    public IReadOnlyList<TitleWorldModel> Build(
        Igi2Human human,
        Igi2HumanAnimation animation,
        string model,
        IReadOnlyList<string> boneNames,
        string? heldModel,
        long tick)
    {
        ArgumentNullException.ThrowIfNull(human);
        ArgumentNullException.ThrowIfNull(animation);
        ArgumentNullException.ThrowIfNull(boneNames);
        if (animation.Rig is not { } rig || animation.Main.Clip is not { } main || main.BoneCount != rig.BoneCount)
        {
            return [];
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

        BonePose pose = BonePose.Evaluate(masks.Skeleton, _main, root);
        Matrix3f orientation = human.Orientation;
        List<TitleWorldModel> models = [new TitleWorldModel(model, human.Position, orientation, pose, human, tick)];
        foreach (Igi2AttachedPart part in Igi2AttachedParts.Of(attachSource, attachTime, pose, heldModel))
        {
            Vector3f local = orientation.Transposed().Transform(part.Position);
            Vector3d at = new(human.Position.X + local.X, human.Position.Y + local.Y, human.Position.Z + local.Z);
            models.Add(new TitleWorldModel(part.Model, at, orientation * part.Orientation));
        }

        return models;
    }

    /// <summary>Marks a branch of a hierarchy, or everything but it, as <c>Hierarchy_MarkSubHierarchy</c> (<c>igi2 0x57F3A0</c>) does.</summary>
    private static bool[] Mark(Igi2BoneHierarchy rig, int branch, bool include)
    {
        bool[] marks = new bool[rig.BoneCount];
        for (int bone = 0; bone < marks.Length; bone++)
        {
            int parent = rig.Parents[bone];
            marks[bone] = bone == branch ? include : parent < 0 ? !include : marks[parent];
        }

        return marks;
    }

    private static int IndexOf(IReadOnlyList<string> names, string name)
    {
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
