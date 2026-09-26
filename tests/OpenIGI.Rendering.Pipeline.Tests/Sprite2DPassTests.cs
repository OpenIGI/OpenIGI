using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Pipeline.Draw2D;
using OpenIGI.Rendering.Rhi;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// That the 2D path records the frame it used to record, with no graphics API underneath it.
/// </summary>
public sealed class Sprite2DPassTests
{
    private static readonly Vertex2D[] Quad =
    [
        new(0f, 0f, 0f, 0f, new Bgra32(255, 255, 255, 255)),
        new(8f, 0f, 1f, 0f, new Bgra32(255, 255, 255, 255)),
        new(8f, 8f, 1f, 1f, new Bgra32(255, 255, 255, 255)),
        new(0f, 8f, 0f, 1f, new Bgra32(255, 255, 255, 255)),
    ];

    private static readonly ushort[] QuadIndices = [0, 1, 2, 0, 2, 3];

    [Fact]
    public void A_sprite_binds_its_blend_pipeline_its_texture_and_the_projection()
    {
        using Harness harness = new();
        TextureHandle texture = harness.Textures.Add(7);

        harness.Record(pass => harness.Pass.DrawTriangles(
            pass, Quad, QuadIndices, texture, BlendMode.AlphaBlend));

        RecordedRhiCommand.SetPipeline pipeline =
            Assert.Single(harness.Device.CommandsOf<RecordedRhiCommand.SetPipeline>());
        Assert.Equal("Sprite2D-AlphaBlend", pipeline.Label);

        RecordedRhiCommand.SetBindGroup group =
            Assert.Single(harness.Device.CommandsOf<RecordedRhiCommand.SetBindGroup>());
        Assert.Equal(0, group.Index);
        Assert.Equal("texture-7", group.Label);

        RecordedRhiCommand.SetPushData push =
            Assert.Single(harness.Device.CommandsOf<RecordedRhiCommand.SetPushData>());
        Assert.Equal(8, push.Bytes.Count);

        RecordedRhiCommand.Draw draw = Assert.Single(harness.Device.CommandsOf<RecordedRhiCommand.Draw>());
        Assert.Equal(new RecordedRhiCommand.Draw(6, 1, Indexed: true), draw);
    }

    [Fact]
    public void Each_blend_mode_has_its_own_pipeline()
    {
        using Harness harness = new();

        harness.Record(pass =>
        {
            harness.Pass.DrawTriangles(pass, Quad, QuadIndices, TextureHandle.None, BlendMode.Opaque);
            harness.Pass.DrawTriangles(pass, Quad, QuadIndices, TextureHandle.None, BlendMode.Additive);
        });

        Assert.Equal(
            ["Sprite2D-Opaque", "Sprite2D-Additive"],
            harness.Device.CommandsOf<RecordedRhiCommand.SetPipeline>().Select(p => p.Label));
    }

    [Fact]
    public void A_second_draw_appends_behind_the_first_rather_than_overwriting_it()
    {
        using Harness harness = new();

        harness.Record(pass =>
        {
            harness.Pass.DrawTriangles(pass, Quad, QuadIndices, TextureHandle.None, BlendMode.AlphaBlend);
            harness.Pass.DrawTriangles(pass, Quad, QuadIndices, TextureHandle.None, BlendMode.AlphaBlend);
        });

        // Both drew six indices; the harness proves they are different six by reading the ring back.
        Assert.Equal(2, harness.Device.CommandsOf<RecordedRhiCommand.Draw>().Count);

        harness.Pass.EndFrame();

        RecordedBuffer vertices = harness.Buffer("sprite2d-vertices-0");
        Assert.Equal(Quad[1].X, BitConverter.ToSingle(vertices.Contents, Vertex2D.Size));
        Assert.Equal(Quad[0].X, BitConverter.ToSingle(vertices.Contents, Vertex2D.Size * 4));
    }

    [Fact]
    public void The_same_texture_drawn_twice_is_bound_through_one_group()
    {
        using Harness harness = new();
        TextureHandle texture = harness.Textures.Add(3);

        harness.Record(pass =>
        {
            harness.Pass.DrawTriangles(pass, Quad, QuadIndices, texture, BlendMode.Opaque);
            harness.Pass.DrawTriangles(pass, Quad, QuadIndices, texture, BlendMode.Opaque);
        });

        Assert.Single(harness.Device.Resources.OfType<RecordedBindGroup>());
    }

    [Fact]
    public void A_forgotten_texture_gives_its_group_back()
    {
        using Harness harness = new();
        TextureHandle texture = harness.Textures.Add(5);

        harness.Record(pass => harness.Pass.DrawTriangles(
            pass, Quad, QuadIndices, texture, BlendMode.Opaque));

        RecordedBindGroup group = Assert.Single(harness.Device.Resources.OfType<RecordedBindGroup>());
        Assert.False(group.IsDisposed);

        harness.Pass.ForgetTexture(5);

        Assert.True(group.IsDisposed);
    }

    [Fact]
    public void A_handle_that_names_nothing_falls_back_to_white_rather_than_failing()
    {
        using Harness harness = new();

        harness.Record(pass => harness.Pass.DrawTriangles(
            pass, Quad, QuadIndices, new TextureHandle(999), BlendMode.Opaque));

        RecordedRhiCommand.SetBindGroup group =
            Assert.Single(harness.Device.CommandsOf<RecordedRhiCommand.SetBindGroup>());

        Assert.Equal("texture-0", group.Label);
    }

    [Fact]
    public void Text_pushes_the_whole_block_the_module_declared()
    {
        using Harness harness = new();
        TextureHandle atlas = harness.Textures.Add(11);

        harness.Record(pass => harness.Pass.DrawText(
            pass,
            Quad,
            QuadIndices,
            atlas,
            SdfTextParams.Plain(4f),
            BlendMode.AlphaBlend));

        RecordedRhiCommand.SetPipeline pipeline =
            Assert.Single(harness.Device.CommandsOf<RecordedRhiCommand.SetPipeline>());
        Assert.Equal("SdfText-AlphaBlend", pipeline.Label);

        RecordedRhiCommand.SetPushData push =
            Assert.Single(harness.Device.CommandsOf<RecordedRhiCommand.SetPushData>());

        // The projection first, the text parameters past it -- one block, two disjoint readers.
        Assert.True(push.Bytes.Count >= 80);
        Assert.Equal(4f, BitConverter.ToSingle([.. push.Bytes], 16));
    }

    [Fact]
    public void The_loading_backdrop_is_a_triangle_with_no_geometry_behind_it()
    {
        using Harness harness = new();

        harness.Record(pass => harness.Pass.DrawLoadingBackdrop(
            pass,
            TextureHandle.None,
            new LoadingBackdropSettings(
                TimeSeconds: 2f,
                Blur: 0f,
                Desaturation: 0f,
                Vignette: 0f,
                Zoom: 1f,
                Grain: 0f,
                Scanlines: 0f,
                Opacity: 1f,
                Letterbox: 0f,
                HasPicture: false,
                AspectRatio: 16f / 9f,
                Sweep: 0f)));

        RecordedRhiCommand.Draw draw = Assert.Single(harness.Device.CommandsOf<RecordedRhiCommand.Draw>());
        Assert.Equal(new RecordedRhiCommand.Draw(3, 1, Indexed: false), draw);

        RecordedRhiCommand.SetPushData push =
            Assert.Single(harness.Device.CommandsOf<RecordedRhiCommand.SetPushData>());
        Assert.Equal(2f, BitConverter.ToSingle([.. push.Bytes], 0));
    }

    [Fact]
    public void Nothing_is_recorded_for_an_empty_draw()
    {
        using Harness harness = new();

        harness.Record(pass => harness.Pass.DrawTriangles(
            pass, [], [], TextureHandle.None, BlendMode.Opaque));

        Assert.Empty(harness.Device.CommandsOf<RecordedRhiCommand.Draw>());
    }

    [Fact]
    public void An_index_count_that_is_not_whole_triangles_is_refused()
    {
        using Harness harness = new();
        using IRhiCommandEncoder encoder = harness.Device.CreateCommandEncoder();
        using IRhiRenderPassEncoder pass = harness.BeginPass(encoder);

        Assert.Throws<ArgumentException>(() => harness.Pass.DrawTriangles(
            pass, Quad, [0, 1], TextureHandle.None, BlendMode.Opaque));
    }

    [Fact]
    public void Each_frame_in_flight_appends_into_its_own_ring()
    {
        using Harness harness = new();

        harness.Pass.BeginFrame(1, 1280f, 720f);
        harness.Record(
            pass => harness.Pass.DrawTriangles(pass, Quad, QuadIndices, TextureHandle.None, BlendMode.Opaque),
            beginFrame: false);

        harness.Pass.EndFrame();

        Assert.Equal(Quad[1].X, BitConverter.ToSingle(harness.Buffer("sprite2d-vertices-1").Contents, Vertex2D.Size));
        Assert.All(harness.Buffer("sprite2d-vertices-0").Contents, b => Assert.Equal(0, b));
    }

    /// <summary>A device, a texture table and a 2D pass, wired the way a frame wires them.</summary>
    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            Device = new RecordingRhiDevice();
            Textures = new FakeTextureBindingSource(Device);
            Pass = new Sprite2DPass(Device, Textures, RhiTextureFormat.Bgra8UnormSrgb, framesInFlight: 2);
        }

        public RecordingRhiDevice Device { get; }

        public FakeTextureBindingSource Textures { get; }

        public Sprite2DPass Pass { get; }

        /// <summary>Runs one frame's worth of 2D drawing and submits it.</summary>
        public void Record(Action<IRhiRenderPassEncoder> draw, bool beginFrame = true)
        {
            if (beginFrame)
            {
                Pass.BeginFrame(0, 1280f, 720f);
            }

            using IRhiCommandEncoder encoder = Device.CreateCommandEncoder("frame");

            using (IRhiRenderPassEncoder pass = BeginPass(encoder))
            {
                draw(pass);
            }

            Device.Submit([encoder.Finish()]);
        }

        public IRhiRenderPassEncoder BeginPass(IRhiCommandEncoder encoder)
        {
            IRhiTexture target = Device.CreateTexture(new RhiTextureDescriptor(
                1280,
                720,
                RhiTextureFormat.Bgra8UnormSrgb,
                RhiTextureUsage.RenderTarget,
                Label: "present"));

            return encoder.BeginRenderPass(
                new RhiRenderPassDescriptor([new RhiColorAttachment(target.View)], Label: "present"));
        }

        /// <summary>Finds a buffer the pass created, by the label it gave it.</summary>
        public RecordedBuffer Buffer(string label) =>
            Device.Resources.OfType<RecordedBuffer>().Single(b => b.Label == label);

        public void Dispose()
        {
            Pass.Dispose();
            Device.Dispose();
        }
    }
}
