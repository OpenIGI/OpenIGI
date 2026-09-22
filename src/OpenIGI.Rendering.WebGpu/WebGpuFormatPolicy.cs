using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>What a texture is actually allocated as, and whether that is what was asked for.</summary>
/// <param name="Format">The format allocated.</param>
/// <param name="Widened">Whether it is wider than the one asked for.</param>
/// <param name="Reason">Why, when it was widened; empty otherwise.</param>
public readonly record struct WebGpuFormatChoice(RhiTextureFormat Format, bool Widened, string Reason);

/// <summary>
/// The one place this backend substitutes a format, and it says so when it does.
/// </summary>
/// <remarks>
/// <para>
/// WebGPU allows a storage binding on a short list of formats. The narrow ones this renderer stores
/// masks and motion in — a single byte, a pair of halves — are not on it unless the adapter offers
/// <c>texture-formats-tier1</c>, which many do not. The choice is then to refuse the texture or to
/// allocate a wider format that holds the same values, and refusing means the distance field, the
/// rejection mask and the dilated velocity simply do not exist on that machine.
/// </para>
/// <para>
/// So it widens, and reports it. The values are unchanged — a byte's worth of mask in four bytes reads
/// back the same — and what is paid is memory and bandwidth, which is a thing to know rather than a
/// thing to hide. Nothing above this has to care: the texture has the format it was given back, and the
/// shader declares a format the library compiled it with.
/// </para>
/// </remarks>
public static class WebGpuFormatPolicy
{
    /// <summary>What a texture wanting a storage binding is allocated as.</summary>
    /// <param name="format">The format asked for.</param>
    /// <param name="usage">What the texture is for.</param>
    /// <param name="tier1">Whether the adapter offers the extended storage formats.</param>
    /// <returns>The format to allocate, and whether that widened it.</returns>
    public static WebGpuFormatChoice ForStorage(RhiTextureFormat format, RhiTextureUsage usage, bool tier1)
    {
        if (!usage.HasFlag(RhiTextureUsage.Storage) || tier1 || IsCoreStorageFormat(format))
        {
            return new WebGpuFormatChoice(format, Widened: false, string.Empty);
        }

        RhiTextureFormat wider = Widen(format);
        return wider == format
            ? new WebGpuFormatChoice(format, Widened: false, string.Empty)
            : new WebGpuFormatChoice(
                wider,
                Widened: true,
                $"{format} cannot carry a storage binding without texture-formats-tier1, so {wider} is "
                + "allocated instead; the values are the same and the memory is not.");
    }

    /// <summary>Whether a format may carry a storage binding on every WebGPU adapter.</summary>
    /// <param name="format">The format asked about.</param>
    /// <returns><see langword="true"/> when it is on the core list.</returns>
    /// <remarks>
    /// The list WebGPU requires of every implementation. Anything else needs a feature, and a feature an
    /// adapter may not have.
    /// </remarks>
    public static bool IsCoreStorageFormat(RhiTextureFormat format) => format
        is RhiTextureFormat.Rgba8Unorm
        or RhiTextureFormat.Rgba16Float
        or RhiTextureFormat.R32Float
        or RhiTextureFormat.R32Uint
        or RhiTextureFormat.Rg32Float
        or RhiTextureFormat.Rgba32Float;

    /// <summary>The narrowest core storage format that holds a format's values.</summary>
    /// <param name="format">The format asked for.</param>
    /// <returns>The format to allocate, or the same one when nothing wider would help.</returns>
    private static RhiTextureFormat Widen(RhiTextureFormat format) => format switch
    {
        // A single byte and a pair of them are masks and coverage: four bytes holds them exactly.
        RhiTextureFormat.R8Unorm or RhiTextureFormat.Rg8Unorm => RhiTextureFormat.Rgba8Unorm,

        // Halves are motion and light. Four halves holds them exactly, and at half the cost of floats.
        RhiTextureFormat.R16Float or RhiTextureFormat.Rg16Float => RhiTextureFormat.Rgba16Float,

        // Packed formats have no wider partner that is also core, so they stay and fail honestly at
        // creation rather than silently becoming something with different precision.
        _ => format,
    };
}
