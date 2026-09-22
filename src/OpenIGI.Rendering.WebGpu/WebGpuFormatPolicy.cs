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
/// WGSL allows a storage binding on a short list of formats, and the narrow ones this renderer stores
/// masks and motion in — a single byte, a pair of halves — are not on it. The shader compiler already
/// decides what to do about that: compiling a module whose storage image is declared <c>r8</c> for the
/// WGSL target emits <c>texture_storage_2d&lt;rgba32float&gt;</c> and says so as a warning.
/// </para>
/// <para>
/// <b>So this is not a choice, it is a matching.</b> WebGPU validates a bind group layout against the
/// format the module declares, so the host has to allocate exactly what the compiler emitted. Widening
/// to anything else — even something narrower and more sensible, like <c>rgba8unorm</c> for a mask — is
/// a layout that will not bind, and the failure names neither the module nor the texture. What that
/// costs is real and worth knowing: a one-byte mask becomes sixteen bytes a texel.
/// </para>
/// <para>
/// The durable answer is for the shader library's reflection to carry the storage format it compiled
/// for each target, so this reads it rather than reproducing the compiler's rule. Until then
/// <c>WebGpuFormatPolicyTests</c> holds the rule to what the committed WGSL actually says, so a
/// compiler that starts choosing differently fails there rather than on somebody's machine.
/// </para>
/// </remarks>
public static class WebGpuFormatPolicy
{
    /// <summary>What the shader compiler widens a narrow storage format to, for the WGSL target.</summary>
    /// <remarks>
    /// Measured from the committed output of slangc, not chosen here. Every module in the library whose
    /// storage image is declared narrower than WGSL allows comes out as this one format.
    /// </remarks>
    public const RhiTextureFormat WidenedStorageFormat = RhiTextureFormat.Rgba32Float;

    /// <summary>What a texture wanting a storage binding is allocated as.</summary>
    /// <param name="format">The format asked for.</param>
    /// <param name="usage">What the texture is for.</param>
    /// <param name="tier1">Whether the adapter offers the extended storage formats.</param>
    /// <returns>The format to allocate, and whether that widened it.</returns>
    /// <remarks>
    /// The adapter's own support is not the whole question and cannot be: even where an adapter offers
    /// <c>texture-formats-tier1</c>, the module was compiled without knowing that and declares the wide
    /// format regardless. The flag is kept because a library that compiles per-adapter would use it, and
    /// because it is what the report says.
    /// </remarks>
    public static WebGpuFormatChoice ForStorage(RhiTextureFormat format, RhiTextureUsage usage, bool tier1)
    {
        if (!usage.HasFlag(RhiTextureUsage.Storage) || IsWgslStorageFormat(format))
        {
            return new WebGpuFormatChoice(format, Widened: false, string.Empty);
        }

        return new WebGpuFormatChoice(
            WidenedStorageFormat,
            Widened: true,
            $"{format} is not a WGSL storage format, and the module that writes it was compiled as "
            + $"{WidenedStorageFormat}. The texture is allocated to match, at "
            + $"{BytesPerTexel(WidenedStorageFormat)} bytes a texel instead of {BytesPerTexel(format)}."
            + (tier1 ? " The adapter would have taken the narrow one; the compiled module would not." : string.Empty));
    }

    /// <summary>Whether WGSL allows a storage binding of this format at all.</summary>
    /// <param name="format">The format asked about.</param>
    /// <returns><see langword="true"/> when a module may declare it.</returns>
    /// <remarks>
    /// The list WGSL requires of every implementation, which is what the shader compiler targets. An
    /// adapter may accept more; a module compiled without that knowledge will not use it.
    /// </remarks>
    public static bool IsWgslStorageFormat(RhiTextureFormat format) => format
        is RhiTextureFormat.Rgba8Unorm
        or RhiTextureFormat.Rgba16Float
        or RhiTextureFormat.R32Float
        or RhiTextureFormat.R32Uint
        or RhiTextureFormat.Rg32Float
        or RhiTextureFormat.Rgba32Float;

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
