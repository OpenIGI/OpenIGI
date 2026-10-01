namespace OpenIGI.Formats.World;

/// <summary>
/// One bone influence of a <see cref="MefMeshFormat.Skinned"/> model — the last eight bytes of a vertex,
/// which carry a skin binding rather than the lightmap UV the other 40-byte format puts there.
/// </summary>
/// <param name="Weight">
/// How much of the vertex this influence owns. Measured to sum to exactly 1.0 per vertex over all 537
/// retail skinned models.
/// </param>
/// <param name="TargetVertex">
/// For an extra influence, the base vertex it accumulates into. Meaningless — and always read as part
/// of the same eight bytes — for a base vertex.
/// </param>
/// <param name="BoneIndex">The bone that moves this influence.</param>
/// <remarks>
/// <para>
/// The skinner (<c>0x49B700</c>) walks the vertex array as two runs: the first
/// <see cref="MefModel.BaseVertexCount"/> entries are placed by their one bone, and the
/// <see cref="MefModel.ExtraInfluenceCount"/> entries that follow **accumulate**
/// <c>weight * boneMatrix * position</c> into the base vertex named by <see cref="TargetVertex"/>.
/// </para>
/// </remarks>
public readonly record struct MefSkinInfluence(float Weight, int TargetVertex, int BoneIndex)
{
    /// <summary>The byte offset of the binding within a 40-byte vertex.</summary>
    public const int OffsetInVertex = 32;

    /// <summary>The size of the binding in bytes.</summary>
    public const int SizeInBytes = 8;
}
