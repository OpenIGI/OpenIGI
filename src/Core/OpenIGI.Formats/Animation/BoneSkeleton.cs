using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Animation;

/// <summary>
/// The bone hierarchy an animation set animates: a parent per bone and a rest translation per bone.
/// </summary>
/// <remarks>
/// From the <c>BOBH</c> group — <c>BOSH</c>, <c>PLST</c> and <c>TLST</c>. The parent list is
/// <b>topologically sorted</b>: every parent index is strictly less than its own bone's index (measured
/// over all six retail sets), so a pose can be resolved to world space in one forward pass with no
/// sorting and no recursion.
/// </remarks>
public sealed class BoneSkeleton
{
    /// <summary>The parent index that marks the root.</summary>
    public const int NoParent = -1;

    private readonly int[] _parents;
    private readonly Vector3f[] _restTranslations;

    internal BoneSkeleton(int id, int[] parents, Vector3f[] restTranslations)
    {
        Id = id;
        _parents = parents;
        _restTranslations = restTranslations;
    }

    /// <summary>Builds a skeleton directly, for tools and tests.</summary>
    /// <param name="parents">One parent index per bone, <see cref="NoParent"/> for a root.</param>
    /// <param name="restTranslations">One rest translation per bone.</param>
    /// <param name="id">The skeleton id.</param>
    /// <returns>The skeleton.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The two lists differ in length, or either is empty.</exception>
    public static BoneSkeleton Create(
        IReadOnlyList<int> parents,
        IReadOnlyList<Vector3f> restTranslations,
        int id = 0)
    {
        ArgumentNullException.ThrowIfNull(parents);
        ArgumentNullException.ThrowIfNull(restTranslations);
        if (parents.Count == 0)
        {
            throw new ArgumentException("A skeleton needs at least one bone.", nameof(parents));
        }

        if (parents.Count != restTranslations.Count)
        {
            throw new ArgumentException(
                $"{parents.Count} parents against {restTranslations.Count} rest translations.",
                nameof(restTranslations));
        }

        return new BoneSkeleton(id, [.. parents], [.. restTranslations]);
    }

    /// <summary>Gets the skeleton id the file declares, which matches the file's name.</summary>
    public int Id { get; }

    /// <summary>Gets the number of bones.</summary>
    public int BoneCount => _parents.Length;

    /// <summary>Gets each bone's parent, or <see cref="NoParent"/> for a root.</summary>
    public ReadOnlySpan<int> Parents => _parents;

    /// <summary>Gets each bone's rest translation relative to its parent, in engine units.</summary>
    public ReadOnlySpan<Vector3f> RestTranslations => _restTranslations;

    /// <summary>
    /// Reports whether every parent index is a root or refers to an earlier bone.
    /// </summary>
    /// <returns><see langword="true"/> when the hierarchy can be evaluated in index order.</returns>
    public bool IsTopologicallySorted()
    {
        for (int i = 0; i < _parents.Length; i++)
        {
            int parent = _parents[i];
            if (parent != NoParent && (parent < 0 || parent >= i))
            {
                return false;
            }
        }

        return true;
    }
}
