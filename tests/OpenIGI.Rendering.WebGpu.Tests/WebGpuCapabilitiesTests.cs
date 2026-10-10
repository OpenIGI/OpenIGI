using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu;
using OpenIGI.Rendering.WebGpu.Native;
using Xunit;

namespace OpenIGI.Rendering.WebGpu.Tests;

/// <summary>
/// What an adapter reports, in the renderer's terms.
/// </summary>
/// <remarks>
/// Tested with features and limits filled in by hand, because the cases that matter are adapters this
/// machine does not have: the one without float32-filterable, the one with 16 KiB of workgroup storage,
/// the one that cannot trace a ray.
/// </remarks>
public sealed class WebGpuCapabilitiesTests
{
    [Fact]
    public void An_adapter_that_reports_nothing_offers_nothing()
    {
        RhiFeatures features = WebGpuCapabilities.Features([], rayQuery: false);

        // Strict sample types are the API's rule, not an adapter feature: WebGPU always has them.
        Assert.Equal(RhiFeatures.None with { StrictTextureSampleTypes = true }, features);
    }

    [Fact]
    public void Each_reported_feature_reaches_the_one_it_means()
    {
        WGPUFeatureName[] supported =
        [
            WGPUFeatureName.TimestampQuery,
            WGPUFeatureName.Float32Filterable,
            WGPUFeatureName.DepthClipControl,
            WGPUFeatureName.IndirectFirstInstance,
            WGPUFeatureName.TextureCompressionBC,
        ];

        RhiFeatures features = WebGpuCapabilities.Features(supported, rayQuery: true);

        Assert.True(features.TimestampQuery);
        Assert.True(features.Float32Filterable);
        Assert.True(features.DepthClamp);
        Assert.True(features.IndirectFirstInstance);
        Assert.True(features.TextureCompressionBc);
        Assert.True(features.RayQuery);
    }

    [Fact]
    public void A_feature_the_renderer_has_no_use_for_changes_nothing()
    {
        RhiFeatures features = WebGpuCapabilities.Features(
            [WGPUFeatureName.ShaderF16, WGPUFeatureName.Subgroups, WGPUFeatureName.ClipDistances],
            rayQuery: false);

        Assert.Equal(RhiFeatures.None with { StrictTextureSampleTypes = true }, features);
    }

    [Fact]
    public void Ray_query_is_reported_separately_because_it_is_not_a_webgpu_feature()
    {
        // WebGPU has no ray tracing at all; wgpu-native offers it as an extension of its own. Reading it
        // from the standard list would find nothing, which is why it is passed in.
        Assert.False(WebGpuCapabilities.Features([], rayQuery: false).RayQuery);
        Assert.True(WebGpuCapabilities.Features([], rayQuery: true).RayQuery);
    }

    [Theory]
    [InlineData(WGPUFeatureName.TextureFormatsTier1, true)]
    [InlineData(WGPUFeatureName.TextureFormatsTier2, true)]
    [InlineData(WGPUFeatureName.TextureCompressionBC, false)]
    public void The_extended_storage_formats_are_their_own_question(WGPUFeatureName feature, bool expected) =>
        Assert.Equal(expected, WebGpuCapabilities.HasExtendedStorageFormats([feature]));

    [Fact]
    public void The_limits_are_read_across()
    {
        WGPULimits reported = new()
        {
            maxTextureDimension2D = 16384,
            maxTextureDimension3D = 2048,
            maxTextureArrayLayers = 256,
            maxBindGroups = 4,
            maxSamplersPerShaderStage = 16,
            maxSampledTexturesPerShaderStage = 128,
            maxStorageBuffersPerShaderStage = 10,
            maxStorageTexturesPerShaderStage = 8,
            maxUniformBufferBindingSize = 65536,
            maxStorageBufferBindingSize = 2147483648,
            maxComputeWorkgroupStorageSize = 32768,
            maxComputeInvocationsPerWorkgroup = 1024,
            maxComputeWorkgroupsPerDimension = 65535,
            maxColorAttachments = 8,
        };

        RhiLimits limits = WebGpuCapabilities.Limits(reported);

        Assert.Equal(16384, limits.MaxTextureDimension2D);
        Assert.Equal(32768, limits.MaxComputeWorkgroupStorageBytes);
        Assert.Equal(2147483648UL, limits.MaxStorageBufferBindingSize);
        Assert.Equal(WebGpuCapabilities.PushDataBytes, limits.MaxPushDataBytes);
        Assert.Equal(4, limits.MaxSampleCount);
    }

    [Fact]
    public void An_adapter_at_the_floor_meets_the_floor()
    {
        // The guaranteed minimums, as the WebGPU specification states them. Anything written against
        // RhiLimits.WebGpuDefaults has to fit on an adapter that offers exactly this and no more, or the
        // floor is not a floor.
        WGPULimits reported = new()
        {
            maxTextureDimension2D = 8192,
            maxTextureDimension3D = 2048,
            maxTextureArrayLayers = 256,
            maxBindGroups = 4,
            maxSamplersPerShaderStage = 16,
            maxSampledTexturesPerShaderStage = 16,
            maxStorageBuffersPerShaderStage = 8,
            maxStorageTexturesPerShaderStage = 4,
            maxUniformBufferBindingSize = 65536,
            maxStorageBufferBindingSize = 134217728,
            maxComputeWorkgroupStorageSize = 16384,
            maxComputeInvocationsPerWorkgroup = 256,
            maxComputeWorkgroupsPerDimension = 65535,
            maxColorAttachments = 8,
        };

        Assert.True(WebGpuCapabilities.Limits(reported).Meets(RhiLimits.WebGpuDefaults));
    }
}
