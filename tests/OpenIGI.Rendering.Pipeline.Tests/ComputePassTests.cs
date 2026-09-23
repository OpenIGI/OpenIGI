using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// That a compute pass binds what it was told to, refuses what it was not, and does not churn.
/// </summary>
public sealed class ComputePassTests
{
    [Fact]
    public void A_dispatch_binds_the_pipeline_the_group_and_the_workgroups_that_cover_the_region()
    {
        using RecordingRhiDevice device = new();
        using Fixture fixture = new(device);

        fixture.Record(pass => fixture.Pass.Dispatch(pass, 16, 16));

        Assert.Single(device.CommandsOf<RecordedRhiCommand.SetPipeline>());
        Assert.Single(device.CommandsOf<RecordedRhiCommand.SetBindGroup>());

        // Eight-by-eight workgroups over sixteen pixels.
        Assert.Equal(
            new RecordedRhiCommand.Dispatch(2, 2, 1),
            Assert.Single(device.CommandsOf<RecordedRhiCommand.Dispatch>()));
    }

    [Fact]
    public void The_group_is_built_once_while_nothing_it_binds_changes()
    {
        using RecordingRhiDevice device = new();
        using Fixture fixture = new(device);

        fixture.Record(pass =>
        {
            fixture.Pass.Dispatch(pass, 16, 16);
            fixture.Pass.Dispatch(pass, 16, 16);
        });

        Assert.Equal(1, fixture.Pass.GroupsBuilt);
    }

    [Fact]
    public void Binding_the_same_resource_again_does_not_rebuild_the_group()
    {
        using RecordingRhiDevice device = new();
        using Fixture fixture = new(device);

        fixture.Record(pass => fixture.Pass.Dispatch(pass, 16, 16));

        fixture.BindAll();

        fixture.Record(pass => fixture.Pass.Dispatch(pass, 16, 16));

        Assert.Equal(1, fixture.Pass.GroupsBuilt);
    }

    [Fact]
    public void Binding_something_different_builds_another_group_and_binding_back_takes_the_first()
    {
        // What a frame slot does every frame: a parameter buffer that alternates, and back. Built afresh
        // each time, that was a new group per pass per frame, and a Vulkan descriptor set is never handed
        // back while a frame could still read it -- the pools grew for as long as the game ran.
        using RecordingRhiDevice device = new();
        using Fixture fixture = new(device);

        fixture.Record(pass => fixture.Pass.Dispatch(pass, 16, 16));

        RecordedBindGroup first = Assert.Single(device.Resources.OfType<RecordedBindGroup>());

        fixture.Pass.Bind("uRejection", fixture.Other.View);
        fixture.Record(pass => fixture.Pass.Dispatch(pass, 16, 16));

        fixture.BindAll();
        fixture.Record(pass => fixture.Pass.Dispatch(pass, 16, 16));

        Assert.Equal(2, fixture.Pass.GroupsBuilt);
        Assert.False(first.IsDisposed);
    }

    [Fact]
    public void A_group_long_unused_is_released()
    {
        // What a resize leaves behind: groups naming targets that are gone, which are let go once enough
        // others have been built since, rather than kept for as long as the pass lives.
        using RecordingRhiDevice device = new();
        using Fixture fixture = new(device);

        fixture.Record(pass => fixture.Pass.Dispatch(pass, 16, 16));
        RecordedBindGroup first = Assert.Single(device.Resources.OfType<RecordedBindGroup>());

        for (int i = 0; i < 8; i++)
        {
            IRhiTexture replacement = device.CreateTexture(new RhiTextureDescriptor(
                16,
                16,
                RhiTextureFormat.R8Unorm,
                RhiTextureUsage.Sampled,
                Label: $"replacement{i}"));

            fixture.Pass.Bind("uRejection", replacement.View);
            fixture.Record(pass => fixture.Pass.Dispatch(pass, 16, 16));
        }

        Assert.True(first.IsDisposed);
    }

    [Fact]
    public void A_slot_the_module_declares_and_nothing_bound_is_refused_by_name()
    {
        using RecordingRhiDevice device = new();
        using ComputePass pass = new(device, ShaderModuleId.UpscaleRejectionDilate, "dilate");

        using IRhiCommandEncoder encoder = device.CreateCommandEncoder("frame");
        using IRhiComputePassEncoder encoded = encoder.BeginComputePass();

        InvalidOperationException refused =
            Assert.Throws<InvalidOperationException>(() => pass.Dispatch(encoded, 16, 16));

        Assert.Contains("dilate", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_the_module_does_not_declare_is_refused_when_it_is_bound()
    {
        // At the point of the mistake, not at the dispatch, which is where it can still be read.
        using RecordingRhiDevice device = new();
        using Fixture fixture = new(device);

        Assert.Throws<ArgumentException>(() => fixture.Pass.Bind("uNotAThing", fixture.Other.View));
    }

    [Fact]
    public void Everything_it_made_goes_when_it_does()
    {
        using RecordingRhiDevice device = new();
        Fixture fixture = new(device);

        fixture.Record(pass => fixture.Pass.Dispatch(pass, 16, 16));

        RecordedBindGroup group = Assert.Single(device.Resources.OfType<RecordedBindGroup>());

        fixture.Dispose();

        Assert.True(group.IsDisposed);
    }

    /// <summary>A pass with every slot of its module filled, the way a frame fills one.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly RecordingRhiDevice _device;
        private readonly IRhiTexture _rejection;
        private readonly IRhiTexture _blend;
        private readonly IRhiBuffer _parameters;

        public Fixture(RecordingRhiDevice device)
        {
            _device = device;

            Pass = new ComputePass(device, ShaderModuleId.UpscaleRejectionDilate, "dilate");

            _rejection = Texture("rejection", RhiTextureUsage.Sampled);
            _blend = Texture("blend", RhiTextureUsage.Storage);
            Other = Texture("other", RhiTextureUsage.Sampled);

            _parameters = device.CreateBuffer(new RhiBufferDescriptor(
                64,
                RhiBufferUsage.Uniform,
                "params"));

            BindAll();
        }

        public ComputePass Pass { get; }

        public IRhiTexture Other { get; }

        public void BindAll()
        {
            Pass.Bind("uRejection", _rejection.View);
            Pass.Bind("uBlend", _blend.View);
            Pass.Bind("p", _parameters);
        }

        public void Record(Action<IRhiComputePassEncoder> run)
        {
            using IRhiCommandEncoder encoder = _device.CreateCommandEncoder("frame");

            using (IRhiComputePassEncoder pass = encoder.BeginComputePass())
            {
                run(pass);
            }

            _device.Submit([encoder.Finish()]);
        }

        public void Dispose()
        {
            Pass.Dispose();
            _parameters.Dispose();
            Other.Dispose();
            _blend.Dispose();
            _rejection.Dispose();
        }

        private IRhiTexture Texture(string label, RhiTextureUsage usage) =>
            _device.CreateTexture(new RhiTextureDescriptor(16, 16, RhiTextureFormat.R8Unorm, usage, Label: label));
    }
}
