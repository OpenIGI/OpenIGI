using System.Buffers.Binary;
using OpenIGI.Core;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Animation;

/// <summary>
/// An I.G.I. 2 bone hierarchy: the <c>HIER</c> chunk both a bone mesh and an animation clip carry.
/// </summary>
/// <remarks>
/// <para>
/// One child-count byte per bone, padded to four bytes, then one <c>real32x3</c> translation per bone.
/// The bytes are the hierarchy's identity: <c>Hierarchy_Register</c> (<c>igi2 0x57EDF0</c>) keys its
/// table on them, which is how a clip is matched to a model without either naming the other — a clip
/// can drive any model whose hierarchy has the same shape (<see cref="HasShapeOf"/>).
/// </para>
/// <para>
/// The two copies are not interchangeable. <c>BoneDynCubeObj_SetLODVirModel</c> (<c>0x567BE0</c>) takes
/// the hierarchy, the bone count and the translations a posed bone is offset by from the <b>mesh</b>;
/// a clip contributes only rotations and the root's motion. A clip's own translations are those of the
/// rig it was authored on.
/// </para>
/// </remarks>
public sealed class Igi2BoneHierarchy
{
    /// <summary>The chunk, <c>HIER</c>, as its bytes appear on disk in both meshes and clips.</summary>
    public static readonly FourCc ChunkTag = FourCc.FromString("REIH");

    private readonly byte[] _counts;
    private readonly int[] _parents;
    private readonly Vector3f[] _translations;

    private Igi2BoneHierarchy(byte[] counts, int[] parents, Vector3f[] translations)
    {
        _counts = counts;
        _parents = parents;
        _translations = translations;
    }

    /// <summary>Gets the number of bones.</summary>
    public int BoneCount => _counts.Length;

    /// <summary>Gets the child-count bytes, one per bone.</summary>
    public IReadOnlyList<byte> ChildCounts => _counts;

    /// <summary>Gets each bone's parent, <see cref="BoneSkeleton.NoParent"/> for the root.</summary>
    /// <remarks>
    /// Breadth-first: <c>BoneDynCubeObj_UpdateHierarchy</c> (<c>igi2 0x564B50</c>) walks the bones in
    /// order as parents, and each takes its child count of bones from one shared cursor that starts at
    /// bone 1. So bone 0's children are bones <c>1..c₀</c>, bone 1's are the next <c>c₁</c>, and so on.
    /// </remarks>
    public IReadOnlyList<int> Parents => _parents;

    /// <summary>Gets each bone's translation from its parent.</summary>
    public IReadOnlyList<Vector3f> Translations => _translations;

    /// <summary>Gets the size of a hierarchy chunk for a bone count.</summary>
    /// <param name="boneCount">The number of bones.</param>
    /// <returns>The bytes: the counts padded to four, then twelve per bone.</returns>
    public static int ChunkSize(int boneCount) => ((boneCount + 3) & ~3) + (boneCount * 12);

    /// <summary>Counts the bones a child-count string describes, as <c>Hierarchy_GetNumBones</c> does.</summary>
    /// <param name="counts">The child counts, at least as long as the hierarchy.</param>
    /// <returns>One plus the counts read until every placed bone has been visited.</returns>
    /// <remarks>
    /// <c>Hierarchy_GetNumBones</c> (<c>igi2 0x57F2C0</c>) reads counts until the bones still owed a
    /// visit run out, so it needs no length — which is how the engine sizes a hierarchy it was handed as
    /// a bare pointer.
    /// </remarks>
    /// <exception cref="ArgumentException">The counts run out before the hierarchy closes.</exception>
    public static int CountBones(ReadOnlySpan<byte> counts)
    {
        int bones = 1;
        int owed = 0;
        for (int i = 0; ; i++)
        {
            if (i >= counts.Length)
            {
                throw new ArgumentException("The child counts end before the hierarchy closes.", nameof(counts));
            }

            bones += counts[i];
            owed += counts[i] - 1;
            if (owed + 1 <= 0)
            {
                return bones;
            }
        }
    }

    /// <summary>Gets each bone's parent from a breadth-first child-count string.</summary>
    /// <param name="counts">One child count per bone.</param>
    /// <returns>The parents, <see cref="BoneSkeleton.NoParent"/> for the root.</returns>
    /// <exception cref="ArgumentException">The counts do not describe exactly one tree over the bones.</exception>
    public static int[] ParentsOf(ReadOnlySpan<byte> counts)
    {
        int[] parents = new int[counts.Length];
        if (parents.Length == 0)
        {
            return parents;
        }

        parents[0] = BoneSkeleton.NoParent;
        int next = 1;
        for (int parent = 0; parent < counts.Length; parent++)
        {
            for (int child = 0; child < counts[parent]; child++)
            {
                if (next >= parents.Length)
                {
                    throw new ArgumentException(
                        $"Bone {parent} claims more children than the {parents.Length} bones hold.",
                        nameof(counts));
                }

                parents[next++] = parent;
            }
        }

        if (next != parents.Length)
        {
            throw new ArgumentException($"The child counts place {next} bones, not {parents.Length}.", nameof(counts));
        }

        return parents;
    }

    /// <summary>Reads a hierarchy chunk.</summary>
    /// <param name="chunk">The chunk payload.</param>
    /// <param name="boneCount">The bone count its owner's header declares.</param>
    /// <returns>The hierarchy.</returns>
    /// <exception cref="Ilff.IlffFormatException">The chunk is short or its counts make no single tree.</exception>
    public static Igi2BoneHierarchy Read(ReadOnlySpan<byte> chunk, int boneCount)
    {
        int padded = (boneCount + 3) & ~3;
        if (boneCount <= 0 || chunk.Length < ChunkSize(boneCount))
        {
            throw new Ilff.IlffFormatException(
                $"A {boneCount}-bone hierarchy needs {ChunkSize(boneCount)} bytes but the chunk holds {chunk.Length}.");
        }

        byte[] counts = chunk[..boneCount].ToArray();
        int[] parents;
        try
        {
            parents = ParentsOf(counts);
        }
        catch (ArgumentException ex)
        {
            throw new Ilff.IlffFormatException($"A bone hierarchy is malformed: {ex.Message}", ex);
        }

        var translations = new Vector3f[boneCount];
        for (int bone = 0; bone < boneCount; bone++)
        {
            int offset = padded + (bone * 12);
            translations[bone] = new Vector3f(
                BinaryPrimitives.ReadSingleLittleEndian(chunk[offset..]),
                BinaryPrimitives.ReadSingleLittleEndian(chunk[(offset + 4)..]),
                BinaryPrimitives.ReadSingleLittleEndian(chunk[(offset + 8)..]));
        }

        return new Igi2BoneHierarchy(counts, parents, translations);
    }

    /// <summary>Gets a value indicating whether another hierarchy has this one's shape.</summary>
    /// <param name="other">The other hierarchy.</param>
    /// <returns><see langword="true"/> when the child-count bytes are identical.</returns>
    /// <remarks>
    /// The engine's own test: <c>Hierarchy_Register</c> finds an existing record by the bytes alone,
    /// translations aside.
    /// </remarks>
    public bool HasShapeOf(Igi2BoneHierarchy other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return _counts.AsSpan().SequenceEqual(other._counts);
    }

    /// <summary>Builds the shared skeleton this hierarchy describes.</summary>
    /// <returns>The skeleton, with these translations as its rest pose and the root's zeroed.</returns>
    /// <remarks>
    /// The root's rest translation is zero whatever the chunk stores.
    /// <c>BoneDynCubeObj_UpdateHierarchy</c> (<c>igi2 0x564B50</c>) puts the root at the sampled root
    /// translation and starts applying hierarchy translations at bone 1, so the first entry is never
    /// used; zeroing it lets <see cref="BonePose.Evaluate"/> place the root exactly where the clip says.
    /// </remarks>
    public BoneSkeleton CreateSkeleton()
    {
        Vector3f[] rest = [.. _translations];
        rest[0] = default;
        return BoneSkeleton.Create(_parents, rest);
    }
}
