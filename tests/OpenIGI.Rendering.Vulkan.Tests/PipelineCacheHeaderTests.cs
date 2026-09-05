using OpenIGI.Rendering.Vulkan.Pipelines;
using Silk.NET.Vulkan;
using Xunit;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// Pins which pipeline-cache blobs are accepted and which are refused.
/// </summary>
/// <remarks>
/// This is the half of the cache that fails silently. A blob wrongly rejected turns the cache into a
/// permanent miss — every pipeline recompiled on every run, and nothing said about it. A blob wrongly
/// accepted is worse: Vulkan says feeding a foreign cache to a driver is undefined, so it has to be
/// the driver's own output, from this device, from this driver build.
/// </remarks>
public sealed unsafe class PipelineCacheHeaderTests
{
    private const uint VendorId = 0x10DE;
    private const uint DeviceId = 0x2216;

    [Fact]
    public void A_blob_this_device_wrote_is_accepted()
    {
        PhysicalDeviceProperties properties = Properties(uuidSeed: 1);
        Assert.True(VulkanPipelineCache.Matches(Blob(in properties), in properties));
    }

    [Fact]
    public void A_blob_from_another_driver_build_is_refused()
    {
        // The pipeline cache UUID is what moves when a driver updates, and it is the whole reason the
        // header exists: everything else about the machine is unchanged.
        PhysicalDeviceProperties written = Properties(uuidSeed: 1);
        PhysicalDeviceProperties now = Properties(uuidSeed: 2);
        Assert.False(VulkanPipelineCache.Matches(Blob(in written), in now));
    }

    [Fact]
    public void A_blob_from_another_device_is_refused()
    {
        PhysicalDeviceProperties written = Properties(uuidSeed: 1);
        PhysicalDeviceProperties now = Properties(uuidSeed: 1, deviceId: DeviceId + 1);
        Assert.False(VulkanPipelineCache.Matches(Blob(in written), in now));
    }

    [Fact]
    public void A_blob_from_another_vendor_is_refused()
    {
        PhysicalDeviceProperties written = Properties(uuidSeed: 1);
        PhysicalDeviceProperties now = Properties(uuidSeed: 1, vendorId: 0x1002);
        Assert.False(VulkanPipelineCache.Matches(Blob(in written), in now));
    }

    [Fact]
    public void A_truncated_or_empty_blob_is_refused()
    {
        PhysicalDeviceProperties properties = Properties(uuidSeed: 1);
        Assert.False(VulkanPipelineCache.Matches([], in properties));
        Assert.False(VulkanPipelineCache.Matches(Blob(in properties).AsSpan(0, 31), in properties));
    }

    [Fact]
    public void A_blob_with_a_foreign_header_shape_is_refused()
    {
        PhysicalDeviceProperties properties = Properties(uuidSeed: 1);

        byte[] wrongLength = Blob(in properties);
        BitConverter.GetBytes(64u).CopyTo(wrongLength, 0);
        Assert.False(VulkanPipelineCache.Matches(wrongLength, in properties));

        byte[] wrongVersion = Blob(in properties);
        BitConverter.GetBytes(2u).CopyTo(wrongVersion, 4);
        Assert.False(VulkanPipelineCache.Matches(wrongVersion, in properties));
    }

    /// <summary>Builds the 32-byte header Vulkan puts at the front of every cache blob, plus a payload.</summary>
    private static byte[] Blob(in PhysicalDeviceProperties properties)
    {
        byte[] blob = new byte[48];
        BitConverter.GetBytes(32u).CopyTo(blob, 0);
        BitConverter.GetBytes(1u).CopyTo(blob, 4);
        BitConverter.GetBytes(properties.VendorID).CopyTo(blob, 8);
        BitConverter.GetBytes(properties.DeviceID).CopyTo(blob, 12);

        fixed (PhysicalDeviceProperties* source = &properties)
        {
            for (int i = 0; i < 16; i++)
            {
                blob[16 + i] = source->PipelineCacheUuid[i];
            }
        }

        return blob;
    }

    private static PhysicalDeviceProperties Properties(
        byte uuidSeed,
        uint vendorId = VendorId,
        uint deviceId = DeviceId)
    {
        PhysicalDeviceProperties properties = default;
        properties.VendorID = vendorId;
        properties.DeviceID = deviceId;
        for (int i = 0; i < 16; i++)
        {
            properties.PipelineCacheUuid[i] = (byte)(uuidSeed + i);
        }

        return properties;
    }
}
