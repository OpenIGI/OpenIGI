namespace OpenIGI.Formats.World;

/// <summary>
/// The <c>PMTL</c> (<c>LTMP</c>) descriptor that marks a model as carrying baked object lightmaps.
/// </summary>
/// <remarks>
/// Only <see cref="MefMeshFormat.Lightmapped"/> models have this chunk — all 344 of them in level 1. The
/// pixels live in <c>lightmaps/lightmaps.res</c> as <c>.olm</c> entries named
/// <c>&lt;objectName&gt;_&lt;draw record index&gt;.olm</c>; the object name comes from the level's object
/// placement, which is still an open item.
/// </remarks>
/// <param name="Size">A power-of-two size between 2 and 64, observed but not yet explained.</param>
/// <param name="Count">The number of lightmap sets, 1 or 2 across the retail corpus.</param>
public readonly record struct MefLightmapInfo(int Size, int Count)
{
    /// <inheritdoc/>
    public override string ToString() => $"lightmap size={Size} count={Count}";
}
