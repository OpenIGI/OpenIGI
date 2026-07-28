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
    /// 40-byte vertices carrying a <b>skin binding</b> in place of the lightmap UV, drawn by the bone
    /// path. These are the animated models: the render header splits the array into base vertices and
    /// extra weighted influences, and each vertex names the bone that moves it.
    /// </summary>
    /// <remarks>
    /// Previously read as "the same layout as <see cref="Lightmapped"/>, just never batched". That was
    /// wrong: <c>0x49F700</c> takes the bone path on exactly this format, and the last eight bytes are
    /// <c>{ f32 weight, u16 targetVertex, u16 boneIndex }</c>.
    /// </remarks>
    Skinned = 1,

    /// <summary>
    /// 40-byte vertices (position, normal, diffuse UV, lightmap UV) with a <c>LTMP</c> descriptor.
    /// Draw records carry a vertex span and their indices are relative to the record's base vertex.
    /// </summary>
    Lightmapped = 3,
}
