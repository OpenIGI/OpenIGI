using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>What a texture is actually allocated as, and whether that is what was asked for.</summary>
/// <param name="Format">The format allocated.</param>
/// <param name="Widened">Whether it differs from the one asked for.</param>
/// <param name="Reason">Why, when it does; empty otherwise.</param>
public readonly record struct WebGpuFormatChoice(RhiTextureFormat Format, bool Widened, string Reason);

/// <summary>
/// The one place this backend substitutes a format, and it says so when it does.
/// </summary>
/// <remarks>
/// <para>
/// WGSL allows a storage binding on a short list of formats, and the narrow ones this renderer stores
/// masks and motion in are not on it. The shader compiler widens them when it emits WGSL, the library
/// records what each module came out as, and a WebGPU bind group layout is validated against exactly
/// that. So this is a matching rather than a choice: allocate anything else and the layout binds
/// nothing, while reporting neither the module nor the texture.
/// </para>
/// <para>
/// What it matches to is read from the compiled modules — see <see cref="WebGpuStorageFormats"/> — not
/// stated here. The widening rule belongs to whichever compiler produced the output and is free to
/// change between releases; a copy of it in a backend would be a second place for it to live.
/// </para>
/// </remarks>
public static class WebGpuFormatPolicy
{
    /// <summary>What a texture wanting a storage binding is allocated as.</summary>
    /// <param name="format">The format asked for.</param>
    /// <param name="usage">What the texture is for.</param>
    /// <param name="tier1">Whether the adapter offers the extended storage formats.</param>
    /// <returns>The format to allocate, and whether that differs from what was asked for.</returns>
    /// <remarks>
    /// The adapter's own support is not the question and cannot be: even where an adapter offers
    /// <c>texture-formats-tier1</c>, the module was compiled without knowing that and declares the wide
    /// format regardless. The flag is taken because it belongs in the explanation when the two differ.
    /// </remarks>
    public static WebGpuFormatChoice ForStorage(RhiTextureFormat format, RhiTextureUsage usage, bool tier1)
    {
        if (!usage.HasFlag(RhiTextureUsage.Storage))
        {
            return new WebGpuFormatChoice(format, Widened: false, string.Empty);
        }

        RhiTextureFormat compiled = WebGpuStorageFormats.ForStorage(format);
        if (compiled == format)
        {
            return new WebGpuFormatChoice(format, Widened: false, string.Empty);
        }

        return new WebGpuFormatChoice(
            compiled,
            Widened: true,
            $"{format} is not a WGSL storage format, and every module that binds one was compiled as "
            + $"{compiled}. The texture is allocated to match, at {BytesPerTexel(compiled)} bytes a texel "
            + $"instead of {BytesPerTexel(format)}."
            + (tier1 ? " The adapter would have taken the narrow one; the compiled module would not." : string.Empty));
    }

    /// <summary>How many bytes one texel of a format occupies, for saying what a widening costs.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The size in bytes, or zero for one with no fixed size.</returns>
    public static int BytesPerTexel(RhiTextureFormat format) => format switch
    {
        RhiTextureFormat.R8Unorm => 1,
        RhiTextureFormat.Rg8Unorm => 2,
        RhiTextureFormat.R16Float => 2,
        RhiTextureFormat.Rg16Float => 4,
        RhiTextureFormat.Rgba8Unorm or RhiTextureFormat.Rgba8UnormSrgb => 4,
        RhiTextureFormat.Bgra8Unorm or RhiTextureFormat.Bgra8UnormSrgb => 4,
        RhiTextureFormat.R32Float or RhiTextureFormat.R32Uint => 4,
        RhiTextureFormat.Rgb10A2Unorm or RhiTextureFormat.Rg11B10Ufloat => 4,
        RhiTextureFormat.Rgba16Float or RhiTextureFormat.Rg32Float => 8,
        RhiTextureFormat.Rgba32Float => 16,
        RhiTextureFormat.Depth16Unorm => 2,
        RhiTextureFormat.Depth32Float or RhiTextureFormat.Depth24PlusStencil8 => 4,
        _ => 0,
    };
}
