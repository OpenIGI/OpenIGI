using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>The skeleton a body model is posed on in its level, and the clip it stands in.</summary>
/// <param name="Skeleton">The animation set's skeleton id: a soldier's <c>Bone Heirachy</c>, or 0 for the player.</param>
/// <param name="StandAnimation">The clip it stands in, by id.</param>
public readonly record struct Igi1BodyRig(int Skeleton, int StandAnimation);

/// <summary>
/// A Project I.G.I. bone model played in its level's animation sets, for a viewer, posed as the game poses it: on the set
/// the level gives the soldier that wears it (its <c>Bone Heirachy</c>) and opening on that soldier's stand clip, its clips
/// sampled as a soldier samples them and skinned by <see cref="MefSkinner"/>.
/// </summary>
public sealed class Igi1ModelAnimator : IModelAnimator
{
    // A clip's clock runs a frame step a 30 Hz tick (SoldierView sets Rate to it).
    private const float TimePerSecond = BoneAnimation.FrameStep * 30f;

    private readonly MefModel _model;
    private readonly BoneSkeleton _skeleton;
    private readonly BoneAnimation[] _clips;
    private readonly Quaternion32[] _rotations;

    private Igi1ModelAnimator(MefModel model, BoneAnimationSet set, int standAnimation)
    {
        _model = model;
        _skeleton = set.Skeleton;
        _clips = [.. set.Animations.Where(a => a.BoneCount == set.Skeleton.BoneCount).OrderBy(static a => a.Id)];
        _rotations = new Quaternion32[_skeleton.BoneCount];
        Clips = [.. _clips.Select(static a => string.Create(CultureInfo.InvariantCulture, $"Clip {a.Id}{(a.IsCyclic ? " (loops)" : string.Empty)}"))];
        StandClip = Array.FindIndex(_clips, a => a.Id == standAnimation);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Clips { get; }

    /// <inheritdoc/>
    public int StandClip { get; }

    /// <summary>
    /// Gets the rig a body is shown in when the level gives it none worth looking at: a plain soldier's, standing in his clip.
    /// </summary>
    /// <remarks>
    /// The player's own body is posed in the game on skeleton 0 in its clip 2 (<see cref="HumanPlayerBody"/>), but that clip
    /// is one still frame made for a body seen from inside it: shown whole it leans back with its chin up. The player's
    /// models share the soldiers' hierarchy, so whatever wears skeleton 0 (the player, and a cut-scene's Jones, whose
    /// scenes drive him) is shown on the soldier's rig, which stands it as a soldier stands.
    /// </remarks>
    public static Igi1BodyRig Shown { get; } = new(HumanSoldierClass.Soldier.DefaultBoneHierarchy, HumanSoldierClass.Soldier.FallbackStandAnimation);

    /// <summary>Gets the skeleton id of the set the model is posed on.</summary>
    public int Skeleton => _skeleton.Id;

    /// <summary>
    /// Reads which skeleton and stand clip each body model wears in a level: every soldier's model with its
    /// <c>Bone Heirachy</c> and stand clip (its declared one, else its class's fallback, as <c>SoldierView</c> chooses), and
    /// the player's with a soldier's (<see cref="Shown"/>).
    /// </summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <returns>The rigs, by model name.</returns>
    public static IReadOnlyDictionary<string, Igi1BodyRig> Rigs(GameTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        Dictionary<string, Igi1BodyRig> rigs = new(StringComparer.OrdinalIgnoreCase);
        foreach (GameTask task in levelRoot.Descendants())
        {
            switch (task)
            {
                case HumanSoldierTask soldier when soldier.Model.Length > 0:
                    rigs.TryAdd(soldier.Model, new Igi1BodyRig(soldier.BoneHierarchy, soldier.StandAnimation >= 0 ? soldier.StandAnimation : soldier.Class.FallbackStandAnimation));
                    break;
                case HumanPlayerTask player when player.PlayerModel.Length > 0:
                    rigs.TryAdd(player.PlayerModel, Shown);
                    break;
            }
        }

        return rigs;
    }

    /// <summary>Finds the clips a model can play in a level.</summary>
    /// <param name="model">The model.</param>
    /// <param name="mission">The level, whose packs hold the animation sets.</param>
    /// <param name="name">The model's name (<c>003_01_1</c>), to find who wears it; null when not known.</param>
    /// <param name="rigs">Who wears what in the level (<see cref="Rigs"/>); null for none.</param>
    /// <returns>An animator, or <see langword="null"/> for a model that is not skinned or a level with no set that fits it.</returns>
    /// <remarks>
    /// A model nobody in the level wears, or a level of detail of one that is (<c>003_01_2</c> for <c>003_01_1</c>), takes
    /// the rig of the model it is a version of, else <see cref="Shown"/>; a set with too few bones for the model's rig is
    /// passed over for the smallest that has enough, with no stand clip known.
    /// </remarks>
    public static Igi1ModelAnimator? For(MefModel model, LoadedMission mission, string? name = null, IReadOnlyDictionary<string, Igi1BodyRig>? rigs = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(mission);
        if (!MefSkinner.IsSkinned(model))
        {
            return null;
        }

        int highest = MefSkinner.HighestBone(model);
        Igi1BodyRig rig = Find(name, rigs) is { } own && own.Skeleton != HumanPlayerBody.Skeleton ? own : Shown;
        BoneAnimationSet? set = mission.TryGetAnimationSet(rig.Skeleton, out BoneAnimationSet? worn) && worn is not null
            && worn.Skeleton.BoneCount > highest && worn.Animations.Count > 0
                ? worn
                : mission.PacksInLoadOrder.SelectMany(static p => p.AnimationSets)
                    .Where(s => s.Skeleton.BoneCount > highest && s.Animations.Count > 0)
                    .OrderBy(static s => s.Skeleton.BoneCount)
                    .FirstOrDefault();
        // A stand clip is an id in its own skeleton's set; in another set the same id is some other clip.
        return set is null ? null : new Igi1ModelAnimator(model, set, set.Skeleton.Id == rig.Skeleton ? rig.StandAnimation : -1);
    }

    /// <summary>Plays an animation set file (<c>anims/001.iff</c>) on a body of the level, for a viewer.</summary>
    /// <param name="file">The file's bytes.</param>
    /// <param name="mission">The level, whose models are the bodies.</param>
    /// <param name="rigs">Who wears what in the level (<see cref="Rigs"/>).</param>
    /// <returns>The preview, or null for a level with no body the set's skeleton fits.</returns>
    /// <exception cref="Formats.Iff.IffFormatException">The bytes are not an animation set.</exception>
    /// <remarks>
    /// The body is one the level puts on that skeleton, else the skinned model with the most bones the skeleton still has
    /// (the hands' 56-bone rig takes the arms, a soldier's the bodies). It opens on the wearer's stand clip.
    /// </remarks>
    public static AnimationFilePreview? PreviewFile(ReadOnlyMemory<byte> file, LoadedMission mission, IReadOnlyDictionary<string, Igi1BodyRig> rigs)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(rigs);
        BoneAnimationSet set = BoneAnimationSet.Read(file);
        int skeleton = set.Skeleton.Id;
        (string Name, MefModel Model, int Highest, Igi1BodyRig? Rig)? best = null;
        foreach (string name in mission.LevelPack.Instances.Concat(mission.CommonPack.Instances).Select(static i => i.Name).Distinct().Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!mission.TryGetModel(name, out MefModel? model) || model is null || !MefSkinner.IsSkinned(model))
            {
                continue;
            }

            int highest = MefSkinner.HighestBone(model);
            if (highest >= set.Skeleton.BoneCount)
            {
                continue;
            }

            Igi1BodyRig? rig = rigs.TryGetValue(name, out Igi1BodyRig found) && found.Skeleton == skeleton ? found : null;
            bool better = best is not { } current
                || (rig is not null && current.Rig is null)
                || ((rig is null) == (current.Rig is null) && highest > current.Highest);
            if (better)
            {
                best = (name, model, highest, rig);
            }
        }

        if (best is not { } body || set.Animations.Count == 0)
        {
            return null;
        }

        Igi1ModelAnimator animator = new(body.Model, set, body.Rig?.StandAnimation ?? -1);
        string facts = string.Create(CultureInfo.InvariantCulture, $"skeleton {skeleton}, {set.Skeleton.BoneCount} bones, {animator.Clips.Count} clips  \u2022  played on {body.Name}");
        return new AnimationFilePreview(body.Model, body.Name, animator, animator.StandClip, facts);
    }

    /// <inheritdoc/>
    public float Seconds(int clip) => _clips[clip].Duration / TimePerSecond;

    /// <inheritdoc/>
    public int Pose(int clip, float seconds, Span<Vertex3D> destination)
    {
        BoneAnimation animation = _clips[clip];
        float time = seconds * TimePerSecond;
        for (int bone = 0; bone < _rotations.Length; bone++)
        {
            _rotations[bone] = BoneRotationSampler.Sample(animation.Rotations(bone), time, animation.Duration, animation.IsCyclic);
        }

        // No root motion, as a soldier poses its rig (BoneObject.Pose): the clip's travel moves the object, not the mesh.
        BonePose pose = BonePose.Evaluate(_skeleton, _rotations);
        return MefSkinner.Skin(_model, pose, 1f, destination);
    }

    // The rig a model is worn with: its own, else that of the model it is another level of detail of (same NNN_NN).
    private static Igi1BodyRig? Find(string? name, IReadOnlyDictionary<string, Igi1BodyRig>? rigs)
    {
        if (name is null || rigs is null)
        {
            return null;
        }

        if (rigs.TryGetValue(name, out Igi1BodyRig rig))
        {
            return rig;
        }

        int last = name.LastIndexOf('_');
        if (last <= 0)
        {
            return null;
        }

        string family = name[..(last + 1)];
        foreach ((string worn, Igi1BodyRig found) in rigs.OrderBy(static r => r.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (worn.StartsWith(family, StringComparison.OrdinalIgnoreCase))
            {
                return found;
            }
        }

        return null;
    }
}
