using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Animation;

/// <summary>
/// An I.G.I. 2 bone mesh played in the clips its level's library holds for its rig, for a viewer: sampled as the first-person
/// frame samples a clip (<see cref="Igi2ClipSampler"/>) and skinned by <see cref="Igi2BoneSkin"/>.
/// </summary>
public sealed class Igi2ModelAnimator : IModelAnimator
{
    // A clip's clock runs a frame's ticks every 30 Hz frame.
    private const float TimePerSecond = Igi2AnimController.TicksPerFrame * 30f;

    private readonly Igi2BoneSkin _skin;
    private readonly BoneSkeleton _skeleton;
    private readonly Igi2AnimationClip[] _clips;
    private readonly Quaternion32[] _rotations;

    private Igi2ModelAnimator(Igi2BoneSkin skin, Igi2BoneHierarchy rig, Igi2AnimationClip[] clips)
    {
        _skin = skin;
        _skeleton = rig.CreateSkeleton();
        _clips = clips;
        _rotations = new Quaternion32[rig.BoneCount];
        Clips = [.. clips.Select(static c => c.Loops ? c.Name + " (loops)" : c.Name)];
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Clips { get; }

    /// <summary>Finds the clips a model can play.</summary>
    /// <param name="model">The model.</param>
    /// <param name="library">The level's clips.</param>
    /// <returns>An animator, or <see langword="null"/> for a model with no rig or a rig with no clips.</returns>
    public static Igi2ModelAnimator? For(MefModel model, Igi2AnimationLibrary library)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(library);
        if (model.BoneHierarchy is not { } rig || Igi2BoneSkin.Create(model) is not { } skin)
        {
            return null;
        }

        Igi2AnimationClip[] clips = [.. library.ClipsOf(rig).Where(c => c.BoneCount == rig.BoneCount)];
        return clips.Length == 0 ? null : new Igi2ModelAnimator(skin, rig, clips);
    }

    /// <summary>Plays a clip file (<c>anims/005_02.iff</c>) on a body of the level whose rig it was authored on, for a viewer.</summary>
    /// <param name="file">The file's bytes.</param>
    /// <param name="packs">The level's packs in load order, whose models are the bodies.</param>
    /// <param name="library">The level's clips, offered after the file's own.</param>
    /// <returns>The preview, or null for a level with no model of the clip's rig.</returns>
    /// <exception cref="Formats.Ilff.IlffFormatException">The bytes are not a clip.</exception>
    /// <remarks>A rig is matched by the shape of its tree, as the game's hierarchy table matches one (<c>Hierarchy_Compare</c>).</remarks>
    public static AnimationFilePreview? PreviewFile(ReadOnlyMemory<byte> file, IEnumerable<LoadedPack> packs, Igi2AnimationLibrary library)
    {
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(library);
        Igi2AnimationClip clip = Igi2AnimationClip.Read(file);
        IReadOnlyList<byte> shape = clip.Hierarchy.ChildCounts;
        foreach (LoadedPack pack in packs)
        {
            foreach (string name in pack.Instances.Select(static i => i.Name).Distinct().Order(StringComparer.OrdinalIgnoreCase))
            {
                if (!pack.TryGetModel(name, out MefModel? model) || model?.BoneHierarchy is not { } rig
                    || !rig.ChildCounts.SequenceEqual(shape) || Igi2BoneSkin.Create(model) is not { } skin)
                {
                    continue;
                }

                Igi2AnimationClip[] clips = [clip, .. library.ClipsOf(rig).Where(c => c.Name != clip.Name && c.BoneCount == rig.BoneCount)];
                Igi2ModelAnimator animator = new(skin, rig, clips);
                string facts = FormattableString.Invariant($"clip {clip.Name}, {clip.BoneCount} bones, {clip.TotalTime / TimePerSecond:0.##} s  \u2022  played on {name}");
                return new AnimationFilePreview(model, name, animator, 0, facts);
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public float Seconds(int clip) => _clips[clip].TotalTime / TimePerSecond;

    /// <inheritdoc/>
    public int Pose(int clip, float seconds, Span<Vertex3D> destination)
    {
        Vector3f root = Igi2ClipSampler.Sample(_clips[clip], (int)(seconds * TimePerSecond), squad: true, _rotations);

        // The root keeps its height but not its travel, so a walk plays on the spot in the viewer.
        BonePose pose = BonePose.Evaluate(_skeleton, _rotations, new Vector3f(0f, 0f, root.Z));
        return _skin.Skin(pose, 1f, destination);
    }
}
