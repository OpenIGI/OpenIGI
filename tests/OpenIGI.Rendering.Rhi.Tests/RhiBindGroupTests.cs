using OpenIGI.Rendering.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>
/// What a bind group entry holds, and the defaults a descriptor takes when it is not told.
/// </summary>
/// <remarks>
/// One entry carries exactly one of a buffer, a texture view or a sampler, matched against the layout's
/// type. The factories are what keep that true at the call site: an entry built by hand can set two, and
/// a backend that reads whichever one it expected then binds silently past the other.
/// </remarks>
public sealed class RhiBindGroupTests
{
    [Fact]
    public void A_buffer_entry_holds_a_buffer_alone()
    {
        RhiBindGroupEntry entry = RhiBindGroupEntry.Of(3, FakeBuffer.Instance, offset: 256, size: 64);

        Assert.Equal(3, entry.Binding);
        Assert.Same(FakeBuffer.Instance, entry.Buffer);
        Assert.Equal(256ul, entry.Offset);
        Assert.Equal(64ul, entry.Size);
        Assert.Null(entry.TextureView);
        Assert.Null(entry.Sampler);
    }

    [Fact]
    public void A_buffer_entry_covers_the_rest_of_the_buffer_by_default()
    {
        RhiBindGroupEntry entry = RhiBindGroupEntry.Of(0, FakeBuffer.Instance);

        Assert.Equal(0ul, entry.Offset);
        Assert.Equal(0ul, entry.Size);
    }

    [Fact]
    public void A_texture_entry_holds_a_view_alone()
    {
        RhiBindGroupEntry entry = RhiBindGroupEntry.Of(1, FakeTextureView.Instance);

        Assert.Same(FakeTextureView.Instance, entry.TextureView);
        Assert.Null(entry.Buffer);
        Assert.Null(entry.Sampler);
    }

    [Fact]
    public void A_sampler_entry_holds_a_sampler_alone()
    {
        RhiBindGroupEntry entry = RhiBindGroupEntry.Of(2, FakeSampler.Instance);

        Assert.Same(FakeSampler.Instance, entry.Sampler);
        Assert.Null(entry.Buffer);
        Assert.Null(entry.TextureView);
    }

    [Fact]
    public void A_layout_entry_declares_a_storage_texture_it_only_writes()
    {
        // Write-only is the default because it is the access every backend allows for every storage
        // format. Read-write is the one WebGPU refuses for most of them, so it has to be asked for.
        RhiBindGroupLayoutEntry entry = new(0, RhiBindingType.StorageTexture, RhiShaderStages.Compute);

        Assert.Equal(RhiStorageAccess.WriteOnly, entry.StorageAccess);
        Assert.Equal(RhiTextureFormat.Undefined, entry.StorageFormat);
        Assert.False(entry.HasDynamicOffset);
        Assert.False(entry.MultisampledTexture);
    }

    [Fact]
    public void A_pipeline_layout_takes_no_push_data_unless_it_says_so()
    {
        RhiPipelineLayoutDescriptor layout = new([]);

        Assert.Equal(0, layout.PushDataBytes);
        Assert.Empty(layout.BindGroupLayouts);
    }

    private sealed class FakeBuffer : IRhiBuffer
    {
        public static readonly FakeBuffer Instance = new();

        public string Label => nameof(FakeBuffer);

        public ulong SizeInBytes => 1024;

        public RhiBufferUsage Usage => RhiBufferUsage.Uniform;

        public void Dispose()
        {
        }
    }

    private sealed class FakeTextureView : IRhiTextureView
    {
        public static readonly FakeTextureView Instance = new();

        public string Label => nameof(FakeTextureView);

        public IRhiTexture Texture => throw new NotSupportedException();

        public RhiTextureFormat Format => RhiTextureFormat.Rgba16Float;

        public void Dispose()
        {
        }
    }

    private sealed class FakeSampler : IRhiSampler
    {
        public static readonly FakeSampler Instance = new();

        public string Label => nameof(FakeSampler);

        public void Dispose()
        {
        }
    }
}
