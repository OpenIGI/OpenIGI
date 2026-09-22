using OpenIGI.Rendering.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>
/// The sizes a device reports, and what it means for one set of them to meet another.
/// </summary>
/// <remarks>
/// A pass written against the WebGPU floor runs everywhere; one written against a desktop driver's
/// numbers does not, and the way it fails is a pipeline that will not create on the machine that has
/// the smaller limit. <see cref="RhiLimits.Meets"/> is what a device is asked before it is trusted with
/// something, so it has to be right in both directions.
/// </remarks>
public sealed class RhiLimitsTests
{
    [Fact]
    public void A_device_meets_its_own_limits() =>
        Assert.True(RhiLimits.WebGpuDefaults.Meets(RhiLimits.WebGpuDefaults));

    [Fact]
    public void A_larger_device_meets_a_smaller_one()
    {
        RhiLimits desktop = RhiLimits.WebGpuDefaults with
        {
            MaxComputeWorkgroupStorageBytes = 32768,
            MaxTextureDimension2D = 16384,
            MaxPushDataBytes = 128,
        };

        Assert.True(desktop.Meets(RhiLimits.WebGpuDefaults with { MaxPushDataBytes = 128 }));
    }

    [Fact]
    public void One_limit_short_is_not_met()
    {
        // The one that actually bites: WebGPU guarantees 16 KiB of workgroup storage where a desktop
        // card offers twice that, so a pass tiled for the larger number simply does not create.
        RhiLimits browser = RhiLimits.WebGpuDefaults;
        RhiLimits wanted = RhiLimits.WebGpuDefaults with { MaxComputeWorkgroupStorageBytes = 32768 };

        Assert.False(browser.Meets(wanted));
        Assert.True(wanted.Meets(browser));
    }

    [Fact]
    public void Every_limit_is_compared()
    {
        // A limit left out of Meets is a limit nothing checks, which is invisible until the machine that
        // has less than it fails to create a pipeline. Each is dropped below the floor in turn.
        RhiLimits floor = RhiLimits.WebGpuDefaults;
        RhiLimits[] short_ =
        [
            floor with { MaxTextureDimension2D = floor.MaxTextureDimension2D - 1 },
            floor with { MaxTextureDimension3D = floor.MaxTextureDimension3D - 1 },
            floor with { MaxTextureArrayLayers = floor.MaxTextureArrayLayers - 1 },
            floor with { MaxBindGroups = floor.MaxBindGroups - 1 },
            floor with { MaxSamplersPerStage = floor.MaxSamplersPerStage - 1 },
            floor with { MaxSampledTexturesPerStage = floor.MaxSampledTexturesPerStage - 1 },
            floor with { MaxStorageBuffersPerStage = floor.MaxStorageBuffersPerStage - 1 },
            floor with { MaxStorageTexturesPerStage = floor.MaxStorageTexturesPerStage - 1 },
            floor with { MaxUniformBufferBindingSize = floor.MaxUniformBufferBindingSize - 1 },
            floor with { MaxStorageBufferBindingSize = floor.MaxStorageBufferBindingSize - 1 },
            floor with { MaxPushDataBytes = floor.MaxPushDataBytes - 1 },
            floor with { MaxComputeWorkgroupStorageBytes = floor.MaxComputeWorkgroupStorageBytes - 1 },
            floor with { MaxComputeInvocationsPerWorkgroup = floor.MaxComputeInvocationsPerWorkgroup - 1 },
            floor with { MaxComputeWorkgroupsPerDimension = floor.MaxComputeWorkgroupsPerDimension - 1 },
            floor with { MaxColorAttachments = floor.MaxColorAttachments - 1 },
            floor with { MaxSampleCount = floor.MaxSampleCount - 1 },
        ];

        Assert.All(short_, one => Assert.False(one.Meets(floor)));

        // And the count is the number of limits there are, so a limit added without being compared fails
        // here rather than silently.
        Assert.Equal(typeof(RhiLimits).GetConstructors()[0].GetParameters().Length, short_.Length);
    }

    [Fact]
    public void A_device_offers_nothing_until_it_says_otherwise()
    {
        RhiFeatures none = RhiFeatures.None;

        Assert.False(none.RayQuery);
        Assert.False(none.TimestampQuery);
        Assert.False(none.Float32Filterable);
        Assert.False(none.DepthClamp);
        Assert.False(none.IndirectFirstInstance);
        Assert.False(none.TextureCompressionBc);
    }
}
