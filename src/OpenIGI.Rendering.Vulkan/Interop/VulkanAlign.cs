namespace OpenIGI.Rendering.Vulkan.Interop;

/// <summary>Rounding to the alignments Vulkan requires of offsets and sizes.</summary>
/// <remarks>
/// Alignment shows up everywhere in an explicit API — descriptor heap offsets, build scratch, shader
/// binding table records, uniform buffer suballocations — and every one of them is the same arithmetic
/// against a power of two. Writing it once means a call site names the alignment and nothing else.
/// </remarks>
internal static class VulkanAlign
{
    /// <summary>Rounds a value up to the next multiple of an alignment.</summary>
    /// <param name="value">The value to round.</param>
    /// <param name="alignment">The alignment, which must be a power of two. Zero and one leave the value alone.</param>
    /// <returns>The rounded value.</returns>
    public static ulong Up(ulong value, ulong alignment) =>
        alignment <= 1 ? value : (value + alignment - 1) & ~(alignment - 1);
}
