using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu;
using Xunit;

namespace OpenIGI.Rendering.WebGpu.Tests;

/// <summary>
/// The one substitution this backend makes, and the conditions it makes it under.
/// </summary>
/// <remarks>
/// A narrow format cannot carry a storage binding on a WebGPU adapter without an optional feature. The
/// choice is to refuse the texture or to allocate a wider one, and refusing means the distance field,
/// the rejection mask and the dilated velocity do not exist on that machine. It widens and reports it,
/// and these are the cases it must and must not do that in.
/// </remarks>
public sealed class WebGpuFormatPolicyTests
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
    public void An_adapter_with_the_feature_gets_what_was_asked_for()
    {
        WebGpuFormatChoice choice = WebGpuFormatPolicy.ForStorage(
            RhiTextureFormat.Rg16Float,
            RhiTextureUsage.Storage,
            tier1: true);

        Assert.Equal(RhiTextureFormat.Rg16Float, choice.Format);
        Assert.False(choice.Widened);
        Assert.Empty(choice.Reason);
    }

    [Theory]
    [InlineData(RhiTextureFormat.R8Unorm, RhiTextureFormat.Rgba8Unorm)]
    [InlineData(RhiTextureFormat.Rg8Unorm, RhiTextureFormat.Rgba8Unorm)]
    [InlineData(RhiTextureFormat.R16Float, RhiTextureFormat.Rgba16Float)]
    [InlineData(RhiTextureFormat.Rg16Float, RhiTextureFormat.Rgba16Float)]
    public void A_narrow_storage_format_is_widened_and_said_so(RhiTextureFormat asked, RhiTextureFormat allocated)
    {
        WebGpuFormatChoice choice = WebGpuFormatPolicy.ForStorage(asked, RhiTextureUsage.Storage, tier1: false);

        Assert.Equal(allocated, choice.Format);
        Assert.True(choice.Widened);
        Assert.Contains(asked.ToString(), choice.Reason, StringComparison.Ordinal);
        Assert.Contains(allocated.ToString(), choice.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RhiTextureFormat.Rgba8Unorm)]
    [InlineData(RhiTextureFormat.Rgba16Float)]
    [InlineData(RhiTextureFormat.R32Float)]
    [InlineData(RhiTextureFormat.R32Uint)]
    [InlineData(RhiTextureFormat.Rg32Float)]
    [InlineData(RhiTextureFormat.Rgba32Float)]
    public void A_core_storage_format_is_left_alone(RhiTextureFormat format)
    {
        Assert.True(WebGpuFormatPolicy.IsCoreStorageFormat(format));

        WebGpuFormatChoice choice = WebGpuFormatPolicy.ForStorage(format, RhiTextureUsage.Storage, tier1: false);

        Assert.Equal(format, choice.Format);
        Assert.False(choice.Widened);
    }

    [Fact]
    public void A_packed_format_is_not_quietly_turned_into_something_else()
    {
        // There is no wider core format holding the same values, so it stays and fails at creation. A
        // substitution here would change precision rather than only cost memory, and the picture would
        // differ from the other backend's for a reason nothing reported.
        WebGpuFormatChoice choice = WebGpuFormatPolicy.ForStorage(
            RhiTextureFormat.Rg11B10Ufloat,
            RhiTextureUsage.Storage,
            tier1: false);

        Assert.Equal(RhiTextureFormat.Rg11B10Ufloat, choice.Format);
        Assert.False(choice.Widened);
    }

    [Fact]
    public void A_widened_format_holds_at_least_as_much_as_the_one_asked_for()
    {
        foreach (RhiTextureFormat format in Enum.GetValues<RhiTextureFormat>())
        {
            if (format == RhiTextureFormat.Undefined || format.IsDepth())
            {
                continue;
            }

            WebGpuFormatChoice choice = WebGpuFormatPolicy.ForStorage(format, RhiTextureUsage.Storage, tier1: false);

            if (choice.Widened)
            {
                Assert.True(WebGpuFormatPolicy.IsCoreStorageFormat(choice.Format));
            }
        }
    }
}
