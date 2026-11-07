using OpenIGI.Formats;
using OpenIGI.Formats.Animation;

namespace OpenIGI.Game.World;

/// <summary>
/// Animations a title adds to the sets its packs load: moves the retail sets do not have (the bomb modes' plant and defuse),
/// put in each set they fit as it is read, so every reader of a pack's sets -- the soldiers, the partners, the previews --
/// finds them by id like the set's own.
/// </summary>
public static class AnimationSetExtensions
{
    private static readonly Lock Gate = new();
    private static readonly List<(AssetFormatProfile Profile, Func<BoneAnimationSet, IReadOnlyList<BoneAnimation>> Extra)> Extensions = [];

    /// <summary>Adds animations to every set the packs of a format read.</summary>
    /// <param name="profile">The format whose packs' sets they go in.</param>
    /// <param name="extra">For a set, the animations to add to it: none where they do not fit it; ids clear of the set's own.</param>
    public static void Register(AssetFormatProfile profile, Func<BoneAnimationSet, IReadOnlyList<BoneAnimation>> extra)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(extra);
        lock (Gate)
        {
            Extensions.Add((profile, extra));
        }
    }

    /// <summary>Reads one set file, with every animation registered for its format added, as a pack's sets have them.</summary>
    /// <param name="profile">The file's format.</param>
    /// <param name="bytes">The file.</param>
    /// <returns>The set.</returns>
    /// <remarks>For a set read outside a pack -- the player's own body and arms, which the host reads from the install.</remarks>
    public static BoneAnimationSet Read(AssetFormatProfile profile, byte[] bytes) => Apply(profile, [BoneAnimationSet.Read(bytes)])[0];

    /// <summary>Gives the sets a pack read with every animation registered for its format added.</summary>
    /// <param name="profile">The pack's format.</param>
    /// <param name="sets">The sets as read.</param>
    /// <returns>The sets, each with its additions; a set with none is returned as it was.</returns>
    /// <remarks>An addition whose id the set already uses is left out: the set's own animation wins.</remarks>
    public static List<BoneAnimationSet> Apply(AssetFormatProfile profile, IReadOnlyList<BoneAnimationSet> sets)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(sets);
        (AssetFormatProfile Profile, Func<BoneAnimationSet, IReadOnlyList<BoneAnimation>> Extra)[] extensions;
        lock (Gate)
        {
            extensions = [.. Extensions.Where(e => e.Profile == profile)];
        }

        List<BoneAnimationSet> extended = new(sets.Count);
        foreach (BoneAnimationSet set in sets)
        {
            List<BoneAnimation> added = [.. extensions.SelectMany(e => e.Extra(set)).Where(a => !set.TryGet(a.Id, out _))];
            extended.Add(added.Count == 0 ? set : BoneAnimationSet.Create(set.Skeleton, [.. set.Animations, .. added]));
        }

        return extended;
    }
}
