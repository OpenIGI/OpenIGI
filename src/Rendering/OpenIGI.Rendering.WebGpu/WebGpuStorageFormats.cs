using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>
/// What the compiled modules say a storage texture of each format came out as.
/// </summary>
/// <remarks>
/// <para>
/// WGSL allows a storage binding on a short list of formats, and the shader compiler widens anything
/// narrower when it emits WGSL. A WebGPU bind group layout is validated against the format the module
/// declares, so the host has to allocate exactly that one — allocate the format that was asked for and
/// the layout binds nothing, and the failure names neither the module nor the texture.
/// </para>
/// <para>
/// <b>Read, not reproduced.</b> The widening belongs to whichever compiler produced the output and is
/// free to change between releases, so the library records what each module actually declared for each
/// target and this reads it back. A backend that instead re-stated the rule would be a second place for
/// it to live, and the two would agree right up until the day they did not.
/// </para>
/// </remarks>
public static class WebGpuStorageFormats
{
    private static readonly Lazy<IReadOnlyDictionary<RhiTextureFormat, RhiTextureFormat>> Mapping =
        new(Build, isThreadSafe: true);

    /// <summary>Gets what each authored storage format was compiled as for WGSL.</summary>
    /// <remarks>Only the formats the library's own modules declare; anything else is not in it.</remarks>
    public static IReadOnlyDictionary<RhiTextureFormat, RhiTextureFormat> Widenings => Mapping.Value;

    /// <summary>What a storage texture of a format has to be allocated as.</summary>
    /// <param name="format">The format the renderer asked for.</param>
    /// <returns>The format a module reading or writing it was compiled with.</returns>
    /// <remarks>
    /// A format no module declares comes back unchanged. That is the honest answer: nothing was compiled
    /// against it, so there is nothing to match, and creating the texture will either work or fail on its
    /// own terms rather than on a guess made here.
    /// </remarks>
    public static RhiTextureFormat ForStorage(RhiTextureFormat format) =>
        Widenings.TryGetValue(format, out RhiTextureFormat compiled) ? compiled : format;

    /// <summary>The renderer's form of a storage format the library records.</summary>
    /// <param name="format">The library's format.</param>
    /// <returns>The renderer's format.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The format is not one the renderer has.</exception>
    public static RhiTextureFormat Translate(ShaderStorageFormat format) => format switch
    {
        ShaderStorageFormat.R8Unorm => RhiTextureFormat.R8Unorm,
        ShaderStorageFormat.Rg8Unorm => RhiTextureFormat.Rg8Unorm,
        ShaderStorageFormat.Rgba8Unorm => RhiTextureFormat.Rgba8Unorm,
        ShaderStorageFormat.R16Float => RhiTextureFormat.R16Float,
        ShaderStorageFormat.Rg16Float => RhiTextureFormat.Rg16Float,
        ShaderStorageFormat.Rgba16Float => RhiTextureFormat.Rgba16Float,
        ShaderStorageFormat.R32Float => RhiTextureFormat.R32Float,
        ShaderStorageFormat.R32Uint => RhiTextureFormat.R32Uint,
        ShaderStorageFormat.Rg32Float => RhiTextureFormat.Rg32Float,
        ShaderStorageFormat.Rgba32Float => RhiTextureFormat.Rgba32Float,
        ShaderStorageFormat.Rgb10A2Unorm => RhiTextureFormat.Rgb10A2Unorm,
        ShaderStorageFormat.Rg11B10Ufloat => RhiTextureFormat.Rg11B10Ufloat,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a storage format."),
    };

    /// <summary>Reads every module's storage bindings and works out what each format became.</summary>
    /// <exception cref="InvalidOperationException">Two modules disagree about the same format.</exception>
    private static Dictionary<RhiTextureFormat, RhiTextureFormat> Build()
    {
        Dictionary<RhiTextureFormat, RhiTextureFormat> widenings = [];
        Dictionary<RhiTextureFormat, string> declaredBy = [];

        foreach (ShaderModuleEntry module in ShaderCatalog.Manifest.Modules)
        {
            foreach (ShaderVariantEntry variant in module.Variants)
            {
                if (!variant.Compiles(ShaderTarget.Wgsl))
                {
                    continue;
                }

                ShaderReflection reflection = ShaderCatalog.GetReflection(
                    module.Id, new ShaderVariant(variant.Name));

                foreach (ShaderBinding binding in reflection.Bindings)
                {
                    if (binding.Kind != ShaderBindingKind.StorageTexture
                        || binding.StorageFormat == ShaderStorageFormat.None
                        || binding.WgslStorageFormat == ShaderStorageFormat.None)
                    {
                        continue;
                    }

                    RhiTextureFormat authored = Translate(binding.StorageFormat);
                    RhiTextureFormat compiled = Translate(binding.WgslStorageFormat);
                    string where = $"{module.Id}.{variant.Name}:{binding.Name}";

                    if (widenings.TryGetValue(authored, out RhiTextureFormat already) && already != compiled)
                    {
                        // Two modules compiled the same declared format into different WGSL ones. One
                        // texture cannot satisfy both layouts, so this has to be decided in the library
                        // rather than papered over here.
                        throw new InvalidOperationException(
                            $"The shader library compiled {authored} as {already} in "
                            + $"{declaredBy[authored]} and as {compiled} in {where}. A storage texture "
                            + "cannot be allocated to match both.");
                    }

                    widenings[authored] = compiled;
                    declaredBy[authored] = where;
                }
            }
        }

        return widenings;
    }
}
