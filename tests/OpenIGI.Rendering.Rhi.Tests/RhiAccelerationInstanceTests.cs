using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>
/// Pins the one placement struct both APIs read: <c>VkAccelerationStructureInstanceKHR</c> and
/// <c>D3D12_RAYTRACING_INSTANCE_DESC</c> are the same 64 bytes, and a backend copies this into its instance
/// buffer without translating a field.
/// </summary>
public sealed class RhiAccelerationInstanceTests
{
    [Fact]
    public void A_placement_is_the_64_bytes_both_apis_read()
    {
        Assert.Equal(64, Unsafe.SizeOf<RhiAccelerationInstance>());
        Assert.Equal(48, Unsafe.SizeOf<RhiTransform3x4>());
        Assert.Equal(0, (int)Marshal.OffsetOf<RhiAccelerationInstance>(nameof(RhiAccelerationInstance.Transform)));
        Assert.Equal(48, (int)Marshal.OffsetOf<RhiAccelerationInstance>(nameof(RhiAccelerationInstance.CustomIndexAndMask)));
        Assert.Equal(52, (int)Marshal.OffsetOf<RhiAccelerationInstance>(nameof(RhiAccelerationInstance.HitGroupOffsetAndFlags)));
        Assert.Equal(56, (int)Marshal.OffsetOf<RhiAccelerationInstance>(nameof(RhiAccelerationInstance.StructureReference)));
    }

    [Fact]
    public void A_placement_packs_its_index_mask_and_flags_into_the_high_and_low_bits()
    {
        Matrix4f transform = Matrix4f.CreateTranslation(5f, 0f, 0f);

        RhiAccelerationInstance instance = RhiAccelerationInstance.Create(
            transform, customIndex: 0x01ABCDEF, mask: 0x80, RhiAccelerationInstanceOptions.TriangleCullDisable, structure: 0x1234);

        // The index keeps 24 bits; the mask rides above it, the flags above the hit-group offset.
        Assert.Equal(0x80ABCDEFu, instance.CustomIndexAndMask);
        Assert.Equal(0x01000000u, instance.HitGroupOffsetAndFlags);
        Assert.Equal(0x1234ul, instance.StructureReference);

        // Row-major, translation in the fourth column of each row.
        Assert.Equal(1f, instance.Transform[0]);
        Assert.Equal(5f, instance.Transform[3]);
        Assert.Equal(1f, instance.Transform[10]);

        RhiAccelerationInstance repointed = instance.Repointed(0x5678, 42);
        Assert.Equal(0x8000002Au, repointed.CustomIndexAndMask);
        Assert.Equal(0x5678ul, repointed.StructureReference);
    }
}
