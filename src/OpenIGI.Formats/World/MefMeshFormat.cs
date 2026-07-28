namespace OpenIGI.Formats.World;

/// <summary>
/// The mesh format at <c>MESH+32</c>, which decides the vertex stride, the render-header layout and the
/// size of a draw record.
/// </summary>
/// <remarks>
/// The original accepts exactly these three values and aborts with <c>Mesh format not supported</c> on
/// anything else (<c>0x49FB10</c>). The retail corpus is 630 / 537 / 5628 models respectively.
/// </remarks>
public enum MefMeshFormat
{
    /// <summary>
    /// 32-byte vertices (position, normal, one UV set). Draw-record indices are absolute. Batched into a
    /// shared vertex buffer by the original.
    /// </summary>
    Diffuse = 0,

    /// <summary>
    /// 40-byte vertices, same layout as <see cref="Lightmapped"/>, but the original never batches these
    /// meshes — they are the large ones, and each owns its vertex buffer. Draw-record indices are absolute.
    /// </summary>
    Unbatched = 1,

    /// <summary>
    /// 40-byte vertices (position, normal, diffuse UV, lightmap UV) with a <c>LTMP</c> descriptor.
    /// Draw records carry a vertex span and their indices are relative to the record's base vertex.
    /// </summary>
    Lightmapped = 3,
}
