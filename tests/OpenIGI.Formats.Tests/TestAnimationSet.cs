using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;

namespace OpenIGI.Formats.Tests;

/// <summary>Builds small hand-made animation sets, so the runtime can be tested without game data.</summary>
internal static class TestAnimationSet
{
    /// <summary>Wraps <see cref="BoneAnimationSet.Create"/> so the call sites read as one line.</summary>
    internal static BoneAnimationSet Create(BoneSkeleton skeleton, params BoneAnimation[] animations) =>
        BoneAnimationSet.Create(skeleton, animations);

    /// <summary>
    /// A clip that turns every bone about Z by <paramref name="spin"/> over its duration.
    /// </summary>
    /// <param name="id">The animation id.</param>
    /// <param name="duration">The duration, in the authoring unit.</param>
    /// <param name="spin">The total turn in radians; zero gives a still clip.</param>
    /// <param name="bones">How many rotation tracks to carry.</param>
    /// <param name="travel">The root's travel over the clip, along +Y.</param>
    internal static BoneAnimation Clip(int id, float duration, float spin, int bones = 4, float travel = 0f)
    {
        BoneRotationKey[][] tracks = new BoneRotationKey[bones][];
        for (int bone = 0; bone < bones; bone++)
        {
            tracks[bone] =
            [
                Key(0f, 0f),
                Key(duration, spin),
            ];
        }

        BoneTranslationKey[] translation = travel == 0f
            ? []
            :
            [
                new BoneTranslationKey(default, 0f, default, default),
                new BoneTranslationKey(new Vector3f(0f, travel, 0f), duration, default, default),
            ];

        return BoneAnimation.Create(id, duration, tracks, translation);
    }

    private static BoneRotationKey Key(float time, float angle)
    {
        Quaternion32 q = new(0f, 0f, MathF.Sin(angle / 2f), MathF.Cos(angle / 2f));
        return new BoneRotationKey(q, time, q, q);
    }
}
