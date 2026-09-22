using System.Text;
using OpenIGI.Rendering.WebGpu;
using OpenIGI.Rendering.WebGpu.Native;
using Xunit;

namespace OpenIGI.Rendering.WebGpu.Tests;

/// <summary>
/// Strings across the boundary, which WebGPU takes as a pointer and a length in UTF-8 bytes.
/// </summary>
/// <remarks>
/// The reason this exists rather than a pinned array: the collector may move a managed array between the
/// call being made and the driver reading it, and what arrives is whatever now sits at the old address.
/// A label read from the wrong address is a garbled name in a capture; an entry point read from one is a
/// pipeline that does not build.
/// </remarks>
public sealed unsafe class WebGpuStringTests
{
    [Fact]
    public void A_string_crosses_as_its_utf8_bytes()
    {
        using WebGpuString text = new("cards.light");

        WGPUStringView view = text.View;

        Assert.Equal(11, text.ByteCount);
        Assert.Equal((nuint)11, view.length);
        Assert.False(view.data is null);
        Assert.Equal("cards.light", Encoding.UTF8.GetString(view.data, (int)view.length));
    }

    [Fact]
    public void A_string_outside_ascii_crosses_by_bytes_and_not_by_characters()
    {
        // Length is bytes, not characters. Getting that wrong truncates every label with an accent in it
        // and, where the truncation lands mid-sequence, hands the driver an invalid string.
        const string Text = "café";

        using WebGpuString crossed = new(Text);

        Assert.Equal(5, crossed.ByteCount);
        Assert.Equal(Text, Encoding.UTF8.GetString(crossed.View.data, (int)crossed.View.length));
    }

    [Fact]
    public void An_empty_string_is_no_string_at_all()
    {
        // A null pointer with a zero length reads as "not provided" rather than as the empty string,
        // which is what a resource with no label wants.
        using WebGpuString empty = new(string.Empty);
        using WebGpuString missing = new(null);

        Assert.True(empty.View.data is null);
        Assert.Equal((nuint)0, empty.View.length);
        Assert.True(missing.View.data is null);
    }

    [Fact]
    public void A_string_the_api_returns_reads_back()
    {
        using WebGpuString text = new("NVIDIA GeForce RTX 3080");

        Assert.Equal("NVIDIA GeForce RTX 3080", WebGpuString.Read(text.View));
    }

    [Fact]
    public void An_absent_string_reads_back_as_empty()
    {
        Assert.Equal(string.Empty, WebGpuString.Read(default));
        Assert.Equal(string.Empty, WebGpuString.Read(new WGPUStringView { data = null, length = 12 }));
    }

    [Fact]
    public void Disposing_twice_frees_once()
    {
        // The reason this is a class and not a struct: a struct holding unmanaged memory is copied by
        // assignment, and each copy's disposal frees the same allocation. A double free corrupts the heap
        // and reports itself somewhere else entirely, which is the worst kind of bug to go looking for.
        WebGpuString text = new("label");

        text.Dispose();
        text.Dispose();

        Assert.Equal(0, text.ByteCount);
    }
}
