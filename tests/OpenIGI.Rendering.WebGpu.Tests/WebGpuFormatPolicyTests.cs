using System.Text.RegularExpressions;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu;
using OpenIGI.Shaders;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.WebGpu.Tests;

/// <summary>
/// The storage format a texture is allocated as, which is read from the compiled modules.
/// </summary>
/// <remarks>
/// WebGPU validates a bind group layout against the format the module declares, so this is a matching
/// rather than a choice: allocate anything else and the layout binds nothing, and the failure names
/// neither the module nor the texture. What makes it a matching rather than a guess is that the library
/// records what each module came out as for each target, and these hold that record to the artefacts it
/// describes.
/// </remarks>
public sealed partial class WebGpuFormatPolicyTests(ITestOutputHelper output)
{
    [Fact]
    public void A_texture_that_is_not_storage_is_never_widened()
    {
        WebGpuFormatChoice choice = WebGpuFormatPolicy.ForStorage(
            RhiTextureFormat.R8Unorm,
            RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination,
            tier1: false);

        Assert.Equal(RhiTextureFormat.R8Unorm, choice.Format);
        Assert.False(choice.Widened);
    }

    [Fact]
    public void What_each_format_became_is_read_from_the_library()
    {
        // Not asserted against a constant: the widening belongs to the shader compiler, and this is the
        // record of what it actually did, module by module.
        foreach ((RhiTextureFormat authored, RhiTextureFormat compiled) in WebGpuStorageFormats.Widenings)
        {
            output.WriteLine($"{authored} -> {compiled}");
        }

        Assert.NotEmpty(WebGpuStorageFormats.Widenings);
        Assert.All(
            WebGpuStorageFormats.Widenings,
            pair => Assert.True(
                pair.Value.BytesPerTexel() >= pair.Key.BytesPerTexel(),
                $"{pair.Key} was compiled as {pair.Value}, which is narrower."));
    }

    [Fact]
    public void The_reflection_and_the_emitted_wgsl_say_the_same_thing()
    {
        // The check that makes the record trustworthy. Every storage binding of every WGSL module is read
        // out of the emitted text and compared against what the reflection beside it claims, so a build
        // that recorded the wrong format -- or recorded nothing -- fails here rather than as a layout
        // that binds nothing on somebody's machine.
        int checkedBindings = 0;

        foreach (ShaderModuleEntry module in ShaderCatalog.Manifest.Modules)
        {
            foreach (ShaderVariantEntry variant in module.Variants)
            {
                if (!variant.Compiles(ShaderTarget.Wgsl))
                {
                    continue;
                }

                ShaderVariant which = new(variant.Name);
                string wgsl = ShaderCatalog.GetWgsl(module.Id, which);
                ShaderReflection reflection = ShaderCatalog.GetReflection(module.Id, which);

                Dictionary<(int, int), string> declared = [];
                foreach (Match match in StorageTexturePattern().Matches(wgsl))
                {
                    declared[(
                        int.Parse(match.Groups["group"].ValueSpan, provider: null),
                        int.Parse(match.Groups["binding"].ValueSpan, provider: null))] =
                        match.Groups["format"].Value;
                }

                foreach (ShaderBinding binding in reflection.Bindings)
                {
                    if (binding.Kind != ShaderBindingKind.StorageTexture)
                    {
                        continue;
                    }

                    Assert.True(
                        declared.TryGetValue((binding.Group, binding.Binding), out string? format),
                        $"{module.Id}.{variant.Name}: the reflection says '{binding.Name}' is a storage "
                        + $"texture at group {binding.Group} binding {binding.Binding} and the WGSL has none.");

                    Assert.Equal(
                        WgslName(WebGpuStorageFormats.Translate(binding.WgslStorageFormat)),
                        format);

                    checkedBindings++;
                }

                Assert.Equal(declared.Count, reflection.Bindings.Count(
                    b => b.Kind == ShaderBindingKind.StorageTexture));
            }
        }

        output.WriteLine($"{checkedBindings} storage binding(s) checked against their own WGSL.");
        Assert.True(checkedBindings > 0, "No storage bindings were checked, so this proved nothing.");
    }

    [Fact]
    public void A_module_with_no_wgsl_form_records_no_wgsl_format()
    {
        // The SPIR-V-only modules still record what they declared, and record nothing for a target they
        // were never compiled for. A format invented for one of those would be a format nothing agrees to.
        bool sawOne = false;

        foreach (ShaderModuleEntry module in ShaderCatalog.Manifest.Modules)
        {
            foreach (ShaderVariantEntry variant in module.Variants)
            {
                if (variant.Compiles(ShaderTarget.Wgsl))
                {
                    continue;
                }

                ShaderReflection reflection = ShaderCatalog.GetReflection(module.Id, new ShaderVariant(variant.Name));
                foreach (ShaderBinding binding in reflection.Bindings)
                {
                    if (binding.Kind != ShaderBindingKind.StorageTexture)
                    {
                        continue;
                    }

                    Assert.NotEqual(ShaderStorageFormat.None, binding.StorageFormat);
                    Assert.Equal(ShaderStorageFormat.None, binding.WgslStorageFormat);
                    sawOne = true;
                }
            }
        }

        Assert.True(sawOne, "No SPIR-V-only storage binding was found, so this proved nothing.");
    }

    [Fact]
    public void A_narrow_storage_format_matches_what_the_module_was_compiled_as()
    {
        // R8Unorm is what the distance field, the rejection mask and the shadow accumulation all declare,
        // so the library certainly has a record for it.
        Assert.True(WebGpuStorageFormats.Widenings.ContainsKey(RhiTextureFormat.R8Unorm));

        WebGpuFormatChoice choice = WebGpuFormatPolicy.ForStorage(
            RhiTextureFormat.R8Unorm,
            RhiTextureUsage.Storage,
            tier1: false);

        Assert.Equal(WebGpuStorageFormats.ForStorage(RhiTextureFormat.R8Unorm), choice.Format);
        Assert.True(choice.Widened);
        Assert.Contains("R8Unorm", choice.Reason, StringComparison.Ordinal);
        Assert.Contains("bytes a texel", choice.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_adapter_with_the_feature_still_gets_what_the_module_was_compiled_as()
    {
        // The module was compiled without knowing which adapter would run it, so it declares the wide
        // format whatever this machine could have taken.
        WebGpuFormatChoice choice = WebGpuFormatPolicy.ForStorage(
            RhiTextureFormat.R8Unorm,
            RhiTextureUsage.Storage,
            tier1: true);

        Assert.Equal(WebGpuStorageFormats.ForStorage(RhiTextureFormat.R8Unorm), choice.Format);
        Assert.True(choice.Widened);
    }

    [Fact]
    public void A_format_wgsl_takes_as_it_is_is_left_alone()
    {
        // Rgba16Float is declared by more modules than anything else and needs no widening, so the
        // library records it as itself.
        Assert.Equal(
            RhiTextureFormat.Rgba16Float,
            WebGpuStorageFormats.ForStorage(RhiTextureFormat.Rgba16Float));

        WebGpuFormatChoice choice = WebGpuFormatPolicy.ForStorage(
            RhiTextureFormat.Rgba16Float,
            RhiTextureUsage.Storage,
            tier1: false);

        Assert.Equal(RhiTextureFormat.Rgba16Float, choice.Format);
        Assert.False(choice.Widened);
    }

    [Fact]
    public void A_format_no_module_declares_comes_back_unchanged()
    {
        // Nothing was compiled against it, so there is nothing to match and nothing to invent.
        Assert.Equal(
            RhiTextureFormat.Bgra8Unorm,
            WebGpuStorageFormats.ForStorage(RhiTextureFormat.Bgra8Unorm));
    }

    /// <summary>The name WGSL spells a format with.</summary>
    private static string WgslName(RhiTextureFormat format) => format switch
    {
        RhiTextureFormat.R8Unorm => "r8unorm",
        RhiTextureFormat.Rg8Unorm => "rg8unorm",
        RhiTextureFormat.Rgba8Unorm => "rgba8unorm",
        RhiTextureFormat.R16Float => "r16float",
        RhiTextureFormat.Rg16Float => "rg16float",
        RhiTextureFormat.Rgba16Float => "rgba16float",
        RhiTextureFormat.R32Float => "r32float",
        RhiTextureFormat.R32Uint => "r32uint",
        RhiTextureFormat.Rg32Float => "rg32float",
        RhiTextureFormat.Rgba32Float => "rgba32float",
        _ => format.ToString().ToLowerInvariant(),
    };

    [GeneratedRegex(
        @"@binding\((?<binding>\d+)\)\s*@group\((?<group>\d+)\)\s*var\s+\w+\s*:\s*texture_storage_\dd<(?<format>[a-z0-9_]+)")]
    private static partial Regex StorageTexturePattern();
}
