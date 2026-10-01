namespace OpenIGI.Formats.World;

/// <summary>
/// One entry of a model's <c>PMTL</c> (<c>LTMP</c>) table: the size of a baked object-lightmap page.
/// </summary>
/// <remarks>
/// <para>
/// The table has one entry per page and the render header's lightmap count agrees with it exactly for all
/// 5628 lightmapped models in the retail corpus. A model's draw records each name the page they sample
/// through <see cref="MefDrawRecord.LightmapPage"/>.
/// </para>
/// <para>
/// The size is what the pixels in the matching <c>.olm</c> entry are: the original's lightmap builder
/// (<c>0x523D40</c>) copies these two shorts straight into the image descriptor it writes, so a page and
/// its baked image always agree. The remaining four bytes of the entry are zero in every retail model.
/// </para>
/// </remarks>
/// <param name="Width">The page width in texels.</param>
/// <param name="Height">The page height in texels.</param>
public readonly record struct MefLightmapPage(int Width, int Height)
{
    /// <summary>The size in bytes of one table entry.</summary>
    public const int Stride = 8;

    /// <summary>Gets the number of texels in the page.</summary>
    public int TexelCount => Width * Height;

    /// <inheritdoc/>
    public override string ToString() => $"{Width}x{Height}";
}
