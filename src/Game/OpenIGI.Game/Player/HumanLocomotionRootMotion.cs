using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;

namespace OpenIGI.Game.Player;

/// <summary>
/// The part of Project I.G.I.'s player animations its walk is made of: each locomotion clip's root track, length, flags and events,
/// carried with the code so the player's movement runs without the game's files.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="HumanLocomotionAnimator"/> reads nothing else of a clip. The step it hands <see cref="PlayerBody"/> is bone 0's
/// translation sampled across the tick (<see cref="BoneObject.BlendedRootMotionDelta"/>), the origin height is bone 0's place
/// (<see cref="BoneObject.BlendedRootPosition"/>), a one-shot ends at the clip's duration and a footstep is an event crossed;
/// the rotation tracks only pose a skeleton, which the walk never does. So a set of one bone holding just those is the walk,
/// to the bit, and <see cref="Retail"/> is the same code walking on the same numbers as <c>common/anims/000.iff</c> gives it.
/// </para>
/// <para>
/// The numbers are <c>000.iff</c>'s, for the clips <see cref="HumanLocomotionStates"/> binds, written out by
/// <c>HumanLocomotionRootMotionTests.WriteTheBakedClips</c> through <see cref="Bake"/>; the same tests hold the baked set
/// equal to the retail one and a body walking on each to the same place every tick.
/// </para>
/// </remarks>
public static partial class HumanLocomotionRootMotion
{
    private static readonly Lazy<BoneAnimationSet> RetailSet = new(() => Build(BakedClips));

    /// <summary>Gets the animations the locomotion states play, each once, in id order.</summary>
    public static IReadOnlyList<int> AnimationIds { get; } =
        [.. HumanLocomotionStates.Bindings
            .Select(b => b.AnimationId)
            .Where(id => id != HumanLocomotionStates.NoAnimation)
            .Distinct()
            .Order()];

    /// <summary>Gets the player's walk as <c>000.iff</c> has it, from the numbers carried with the code.</summary>
    public static BoneAnimationSet Retail => RetailSet.Value;

    /// <summary>The clips of a full animation set the walk is made of, cut down to what it reads.</summary>
    /// <param name="set">The player's set, normally <c>000.iff</c>.</param>
    /// <returns>One clip per animation in <see cref="AnimationIds"/> the set defines.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="set"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<RootMotionClip> Bake(BoneAnimationSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        List<RootMotionClip> clips = [];
        foreach (int id in AnimationIds)
        {
            if (set.TryGet(id, out BoneAnimation animation))
            {
                clips.Add(new RootMotionClip(id, animation.Duration, animation.Flags, [.. animation.Translation], [.. animation.Events]));
            }
        }

        return clips;
    }

    /// <summary>A one-bone set of the clips: what <see cref="HumanLocomotionAnimator"/> walks on.</summary>
    /// <param name="clips">The clips, as <see cref="Bake"/> cuts them.</param>
    /// <returns>The set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clips"/> is <see langword="null"/>.</exception>
    public static BoneAnimationSet Build(IReadOnlyList<RootMotionClip> clips)
    {
        ArgumentNullException.ThrowIfNull(clips);
        BoneSkeleton root = BoneSkeleton.Create([BoneSkeleton.NoParent], [default(Vector3f)]);
        IReadOnlyList<BoneRotationKey>[] noRotation = [Array.Empty<BoneRotationKey>()];
        BoneAnimation[] animations = new BoneAnimation[clips.Count];
        for (int i = 0; i < clips.Count; i++)
        {
            RootMotionClip clip = clips[i];
            animations[i] = BoneAnimation.Create(clip.Id, clip.Duration, noRotation, clip.Translation, clip.Events, clip.Flags);
        }

        return BoneAnimationSet.Create(root, animations);
    }
}

/// <summary>One locomotion clip as the walk reads it.</summary>
/// <param name="Id">The animation id.</param>
/// <param name="Duration">The clip's length, in the set's units.</param>
/// <param name="Flags">The clip's flag word.</param>
/// <param name="Translation">Bone 0's translation keys.</param>
/// <param name="Events">The clip's events.</param>
public sealed record RootMotionClip(
    int Id,
    float Duration,
    uint Flags,
    IReadOnlyList<BoneTranslationKey> Translation,
    IReadOnlyList<BoneAnimationEvent> Events);
