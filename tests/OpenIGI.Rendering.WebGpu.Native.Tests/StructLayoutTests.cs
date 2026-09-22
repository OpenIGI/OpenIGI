using System.Runtime.InteropServices;
using OpenIGI.Rendering.WebGpu.Native;
using Xunit;

namespace OpenIGI.Rendering.WebGpu.Native.Tests;

/// <summary>
/// The sizes of a few structures, worked out from the C header by hand.
/// </summary>
/// <remarks>
/// Not generated from the same model the bindings were, on purpose: a size derived from the generator
/// only proves the generator agrees with itself. These are read off the header and written down, so a
/// scalar mapped a word too narrow — <c>size_t</c> as a 32-bit integer is the classic — fails here
/// rather than in the middle of a frame, where it presents as a descriptor mysteriously rejected or, far
/// worse, accepted with the wrong values.
/// </remarks>
public sealed class StructLayoutTests
{
    [Fact]
    public void A_string_view_is_a_pointer_and_a_length() =>
        Assert.Equal(nint.Size + nuint.Size, Marshal.SizeOf<WGPUStringView>());

    [Fact]
    public void An_extent_is_three_unsigned_integers() =>
        Assert.Equal(3 * sizeof(uint), Marshal.SizeOf<WGPUExtent3D>());

    [Fact]
    public void An_origin_is_three_unsigned_integers() =>
        Assert.Equal(3 * sizeof(uint), Marshal.SizeOf<WGPUOrigin3D>());

    [Fact]
    public void A_colour_is_four_doubles() =>
        Assert.Equal(4 * sizeof(double), Marshal.SizeOf<WGPUColor>());

    [Fact]
    public void A_chained_struct_is_a_pointer_and_a_type()
    {
        // WGPUChainedStruct is { WGPUChainedStruct const * next; WGPUSType sType; } and the enumeration
        // is 32 bits, so on a 64-bit target the whole is padded to sixteen. That padding is the point of
        // checking: an enumeration bound as long would make it twenty-four and every chained descriptor
        // past the first would be read from the wrong offset.
        Assert.Equal(nint.Size + (nint.Size == 8 ? 8 : 4), Marshal.SizeOf<WGPUChainedStruct>());
        Assert.Equal(sizeof(int), Marshal.SizeOf(Enum.GetUnderlyingType(typeof(WGPUSType))));
    }

    [Fact]
    public void A_buffer_descriptor_is_its_five_fields()
    {
        // { chain pointer, WGPUStringView label, WGPUBufferUsage usage, uint64 size, WGPUBool mapped }
        // with usage a 64-bit flag set and the trailing bool padded out to eight.
        int expected = nint.Size          // nextInChain
            + nint.Size + nuint.Size      // label
            + sizeof(ulong)               // usage
            + sizeof(ulong)               // size
            + 8;                          // mappedAtCreation, padded

        Assert.Equal(expected, Marshal.SizeOf<WGPUBufferDescriptor>());
    }

    [Fact]
    public void A_flag_set_is_sixty_four_bits_wide()
    {
        // typedef uint64_t WGPUFlags. Binding one as a 32-bit enumeration puts every field after it in a
        // descriptor at the wrong offset, which is the kind of mistake that still runs.
        Assert.Equal(typeof(ulong), Enum.GetUnderlyingType(typeof(WGPUBufferUsage)));
        Assert.Equal(typeof(ulong), Enum.GetUnderlyingType(typeof(WGPUTextureUsage)));
        Assert.Equal(typeof(ulong), Enum.GetUnderlyingType(typeof(WGPUShaderStage)));
    }

    [Fact]
    public void A_plain_enumeration_is_thirty_two_bits_wide()
    {
        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(WGPUTextureFormat)));
        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(WGPULoadOp)));
        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(WGPUBackendType)));
    }

    [Fact]
    public void The_values_this_renderer_binds_by_are_what_the_header_says()
    {
        // A handful of values spelled out, so a whole enumeration silently shifting by one -- a member
        // added upstream in the middle rather than at the end -- is caught. Every one of these is read
        // off the committed header.
        Assert.Equal(0, (int)WGPUTextureFormat.Undefined);
        Assert.Equal(0x00000001, (int)WGPULoadOp.Load);
        Assert.Equal(0x00000002, (int)WGPULoadOp.Clear);
        Assert.Equal(0x00000001, (int)WGPUStoreOp.Store);
        Assert.Equal(0x00000002, (int)WGPUStoreOp.Discard);
        Assert.Equal(0x0000000000000001UL, (ulong)WGPUBufferUsage.MapRead);
        Assert.Equal(0x0000000000000002UL, (ulong)WGPUBufferUsage.MapWrite);
    }
}
