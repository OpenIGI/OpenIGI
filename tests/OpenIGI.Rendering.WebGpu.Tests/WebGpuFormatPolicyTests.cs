using System.Text.RegularExpressions;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu;
using OpenIGI.Shaders;
using Xunit;

namespace OpenIGI.Rendering.WebGpu.Tests;

/// <summary>
/// The storage format a texture is allocated as, held to what the compiled modules actually declare.
/// </summary>
/// <remarks>
/// WebGPU validates a bind group layout against the format the module declares, so this is a matching
/// rather than a choice: allocate anything else and the layout will not bind, and the failure names
/// neither the module nor the texture. The last test here reads the committed WGSL and holds the rule
/// to it, so a shader compiler that starts widening differently fails in this file rather than on
/// somebody's machine.
/// </remarks>
public sealed partial class WebGpuFormatPolicyTests
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

    [Theory]
    [InlineData(RhiTextureFormat.R8Unorm)]
    [InlineData(RhiTextureFormat.Rg8Unorm)]
    [InlineData(RhiTextureFormat.R16Float)]
    [InlineData(RhiTextureFormat.Rg16Float)]
    public void A_narrow_storage_format_matches_what_the_module_was_compiled_as(RhiTextureFormat asked)
    {
        WebGpuFormatChoice choice = WebGpuFormatPolicy.ForStorage(asked, RhiTextureUsage.Storage, tier1: false);

        Assert.Equal(WebGpuFormatPolicy.WidenedStorageFormat, choice.Format);
        Assert.True(choice.Widened);
        Assert.Contains(asked.ToString(), choice.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_adapter_with_the_feature_still_gets_the_wide_one()
    {
        // The module was compiled without knowing which adapter would run it, so it declares the wide
        // format whatever this machine could have taken. Allocating the narrow one here because the
        // adapter allows it produces a layout the module will not bind to.
        WebGpuFormatChoice choice = WebGpuFormatPolicy.ForStorage(
            RhiTextureFormat.Rg16Float,
            RhiTextureUsage.Storage,
            tier1: true);

        Assert.Equal(WebGpuFormatPolicy.WidenedStorageFormat, choice.Format);
        Assert.True(choice.Widened);
    }

    [Theory]
    [InlineData(RhiTextureFormat.Rgba8Unorm)]
    [InlineData(RhiTextureFormat.Rgba16Float)]
    [InlineData(RhiTextureFormat.R32Float)]
    [InlineData(RhiTextureFormat.R32Uint)]
    [InlineData(RhiTextureFormat.Rg32Float)]
    [InlineData(RhiTextureFormat.Rgba32Float)]
    public void A_format_wgsl_allows_is_left_alone(RhiTextureFormat format)
    {
        Assert.True(WebGpuFormatPolicy.IsWgslStorageFormat(format));

        WebGpuFormatChoice choice = WebGpuFormatPolicy.ForStorage(format, RhiTextureUsage.Storage, tier1: false);

        Assert.Equal(format, choice.Format);
        Assert.False(choice.Widened);
    }

    [Fact]
    public void The_widening_says_what_it_costs()
    {
        WebGpuFormatChoice choice = WebGpuFormatPolicy.ForStorage(
            RhiTextureFormat.R8Unorm,
            RhiTextureUsage.Storage,
            tier1: false);

        Assert.Equal(1, WebGpuFormatPolicy.BytesPerTexel(RhiTextureFormat.R8Unorm));
        Assert.Equal(16, WebGpuFormatPolicy.BytesPerTexel(WebGpuFormatPolicy.WidenedStorageFormat));
        Assert.Contains("16 bytes a texel instead of 1", choice.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_storage_format_the_library_compiled_is_one_this_would_allocate()
    {
        // The check that matters, and the reason the constant above is not a guess: every storage
        // texture declared in every committed WGSL module is read back, and each must be a format this
        // policy would produce. A compiler that starts widening r8 to rgba8unorm instead fails here.
        HashSet<string> declared = [];

        foreach (ShaderModuleEntry module in ShaderCatalog.Manifest.Modules)
        {
            foreach (ShaderVariantEntry variant in module.Variants)
            {
                if (!variant.Compiles(ShaderTarget.Wgsl))
                {
                    continue;
                }

                string wgsl = ShaderCatalog.GetWgsl(module.Id, new ShaderVariant(variant.Name));
                foreach (Match match in StorageTexturePattern().Matches(wgsl))
                {
                    declared.Add(match.Groups["format"].Value);
                }
            }
        }

        Assert.NotEmpty(declared);

        HashSet<string> allowed = [];
        foreach (RhiTextureFormat format in Enum.GetValues<RhiTextureFormat>())
        {
            if (format != RhiTextureFormat.Undefined && WebGpuFormatPolicy.IsWgslStorageFormat(format))
            {
                allowed.Add(WgslName(format));
            }
        }

        Assert.Empty(declared.Except(allowed, StringComparer.Ordinal));
        Assert.Contains(WgslName(WebGpuFormatPolicy.WidenedStorageFormat), declared);
    }

    /// <summary>The name WGSL spells a format with.</summary>
    private static string WgslName(RhiTextureFormat format) => format switch
    {
        RhiTextureFormat.Rgba8Unorm => "rgba8unorm",
        RhiTextureFormat.Rgba16Float => "rgba16float",
        RhiTextureFormat.R32Float => "r32float",
        RhiTextureFormat.R32Uint => "r32uint",
        RhiTextureFormat.Rg32Float => "rg32float",
        RhiTextureFormat.Rgba32Float => "rgba32float",
        _ => format.ToString().ToLowerInvariant(),
    };

    [GeneratedRegex(@"texture_storage_\dd<(?<format>[a-z0-9_]+)")]
    private static partial Regex StorageTexturePattern();
}
