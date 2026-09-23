using OpenIGI.Rendering.Rhi;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>That a volume no shader writes is not allocated as a storage image.</summary>
public sealed class VolumeUsageTests
{
    [Fact]
    public void A_volume_only_uploaded_and_sampled_carries_no_storage_usage()
    {
        // As a storage image, WebGPU widened the distance field's one-byte voxels to sixteen bytes each,
        // on the main thread, on every mission swap.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = new(rhi, new DisplayMode(640, 480, 32, 60));

        device.CreateVolumeTexture(4, 4, 4, VolumeFormat.R8, "uploaded", shaderWritten: false);
        device.CreateVolumeTexture(4, 4, 4, VolumeFormat.R8, "written");

        RecordedTexture uploaded = rhi.Resources.OfType<RecordedTexture>().Single(t => t.Label == "uploaded");
        RecordedTexture written = rhi.Resources.OfType<RecordedTexture>().Single(t => t.Label == "written");

        Assert.False(uploaded.Usage.HasFlag(RhiTextureUsage.Storage));
        Assert.True(uploaded.Usage.HasFlag(RhiTextureUsage.Sampled));
        Assert.True(written.Usage.HasFlag(RhiTextureUsage.Storage));
    }
}
