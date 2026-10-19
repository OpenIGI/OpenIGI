using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.Meshes;
using OpenIGI.Rendering.Rhi;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// That a mesh draw binds everything it needs and nothing it does not, and refuses what would be
/// unsafe.
/// </summary>
public sealed class Mesh3DPassTests
{
    [Fact]
    public void A_draw_binds_its_pipeline_its_material_its_two_streams_and_its_indices()
    {
        using Harness harness = new();
        using RhiMesh mesh = RhiMesh.CreateStatic(harness.Device, Pose(0f), [0, 1, 2], "wall");

        harness.Record(pass => harness.Pass.Draw(
            pass,
            harness.Pipeline,
            Call(mesh),
            Push,
            frameCounter: 7));

        Assert.Equal("Mesh3D-TestAndWrite-CounterClockwise-Opaque-128", Single<RecordedRhiCommand.SetPipeline>(harness).Label);
        Assert.Equal("material-1-2-0", Single<RecordedRhiCommand.SetBindGroup>(harness).Label);
        Assert.Equal(new RecordedRhiCommand.Draw(3, 1, Indexed: true), Single<RecordedRhiCommand.Draw>(harness));
    }

    [Fact]
    public void A_static_mesh_still_binds_both_streams()
    {
        // A stream left unbound is a pipeline drawing from whatever the last draw left there. A static
        // mesh reports one buffer as both, which is what makes the delta zero without anyone deciding.
        using Harness harness = new();
        using RhiMesh mesh = RhiMesh.CreateStatic(harness.Device, Pose(0f), [0, 1, 2], "wall");

        Assert.Same(mesh.VertexBuffer, mesh.PreviousVertexBuffer);
    }

    [Fact]
    public void A_draw_stamps_the_pose_slots_the_frame_has_bound()
    {
        // Without the stamp the next pose write lands on vertices this frame is still fetching.
        using Harness harness = new();
        using RhiMesh mesh = RhiMesh.CreateAnimated(harness.Device, Pose(0f), [0, 1, 2], "body");

        IRhiBuffer drawn = mesh.VertexBuffer;

        harness.Record(pass => harness.Pass.Draw(pass, harness.Pipeline, Call(mesh), Push, frameCounter: 4));

        mesh.UpdateVertices(Pose(1f), frameCounter: 4);

        Assert.NotSame(drawn, mesh.VertexBuffer);
    }

    [Fact]
    public void A_range_past_the_index_buffer_is_dropped_rather_than_issued()
    {
        // Bounded buffer access does not cover an out-of-bounds index read on either backend: the draw
        // would not read garbage, it would lose the device.
        using Harness harness = new();
        using RhiMesh mesh = RhiMesh.CreateStorage(harness.Device, 64, 8, "terrain");

        bool drawn = true;

        harness.Record(pass => drawn = harness.Pass.Draw(
            pass,
            harness.Pipeline,
            Call(mesh) with { IndexOffset = 6, IndexCount = 6 },
            Push,
            frameCounter: 1));

        Assert.False(drawn);
        Assert.Equal(1, harness.Pass.DroppedRanges);
        Assert.Empty(harness.Device.CommandsOf<RecordedRhiCommand.Draw>());
    }

    [Fact]
    public void An_empty_draw_is_neither_issued_nor_counted_as_dropped()
    {
        using Harness harness = new();
        using RhiMesh mesh = RhiMesh.CreateStatic(harness.Device, Pose(0f), [0, 1, 2], "wall");

        harness.Record(pass => harness.Pass.Draw(
            pass,
            harness.Pipeline,
            Call(mesh) with { IndexCount = 0 },
            Push,
            frameCounter: 1));

        Assert.Equal(0, harness.Pass.DroppedRanges);
        Assert.Empty(harness.Device.CommandsOf<RecordedRhiCommand.Draw>());
    }

    [Fact]
    public void Several_ranges_of_one_mesh_bind_its_state_once()
    {
        // The terrain's shape: one mesh, one material, many runs.
        using Harness harness = new();
        using RhiMesh mesh = RhiMesh.CreateStorage(harness.Device, 64, 64, "terrain");

        int drawn = 0;

        harness.Record(pass => drawn = harness.Pass.DrawRanges(
            pass,
            harness.Pipeline,
            Call(mesh),
            [new MeshDrawRange(0, 3), new MeshDrawRange(3, 3), new MeshDrawRange(6, 3)],
            Push,
            frameCounter: 1));

        Assert.Equal(3, drawn);
        Assert.Single(harness.Device.CommandsOf<RecordedRhiCommand.SetPipeline>());
        Assert.Single(harness.Device.CommandsOf<RecordedRhiCommand.SetBindGroup>());
        Assert.Equal(3, harness.Device.CommandsOf<RecordedRhiCommand.Draw>().Count);
    }

    [Fact]
    public void One_bad_range_does_not_take_the_others_with_it()
    {
        using Harness harness = new();
        using RhiMesh mesh = RhiMesh.CreateStorage(harness.Device, 64, 8, "terrain");

        int drawn = 0;

        harness.Record(pass => drawn = harness.Pass.DrawRanges(
            pass,
            harness.Pipeline,
            Call(mesh),
            [new MeshDrawRange(0, 3), new MeshDrawRange(6, 6), new MeshDrawRange(3, 3)],
            Push,
            frameCounter: 1));

        Assert.Equal(2, drawn);
        Assert.Equal(1, harness.Pass.DroppedRanges);
    }

    [Fact]
    public void The_same_material_across_draws_is_bound_through_one_group()
    {
        using Harness harness = new();
        using RhiMesh first = RhiMesh.CreateStatic(harness.Device, Pose(0f), [0, 1, 2], "a");
        using RhiMesh second = RhiMesh.CreateStatic(harness.Device, Pose(1f), [0, 1, 2], "b");

        harness.Record(pass =>
        {
            harness.Pass.Draw(pass, harness.Pipeline, Call(first), Push, 1);
            harness.Pass.Draw(pass, harness.Pipeline, Call(second), Push, 1);
        });

        Assert.Equal(2, harness.Device.CommandsOf<RecordedRhiCommand.SetBindGroup>().Count);
        Assert.Single(harness.Device.Resources.OfType<RecordedBindGroup>());
    }

    private static readonly byte[] Push = new byte[Mesh3DPipelineCache.PushDataBytes];

    private static T Single<T>(Harness harness)
        where T : RecordedRhiCommand => Assert.Single(harness.Device.CommandsOf<T>());

    private static MeshDrawCall Call(RhiMesh mesh) => new(
        mesh,
        IndexOffset: 0,
        IndexCount: 3,
        new TextureHandle(1),
        new TextureHandle(2),
        TextureHandle.None);

    private static Vertex3D[] Pose(float x) =>
    [
        new(new Vector3f(x, 0f, 0f), Vector3f.UnitY, 0f, 0f),
        new(new Vector3f(x, 1f, 0f), Vector3f.UnitY, 1f, 0f),
        new(new Vector3f(x, 1f, 1f), Vector3f.UnitY, 1f, 1f),
    ];

    /// <summary>A device, a material cache, a world pipeline and the pass that draws through them.</summary>
    private sealed class Harness : IDisposable
    {
        private readonly IRhiBindGroupLayout _material;
        private readonly IRhiBindGroupLayout _cube;
        private readonly Mesh3DPipelineCache _pipelines;
        private readonly MaterialBindGroupCache _materials;

        public Harness()
        {
            Device = new RecordingRhiDevice();
            Textures = new FakeTextureBindingSource(Device);

            Textures.Add(1);
            Textures.Add(2);

            _material = Mesh3DPipelineCache.CreateMaterialLayout(Device);
            _cube = Mesh3DPipelineCache.CreateCubeLayout(Device);
            _materials = new MaterialBindGroupCache(Device, _material);

            _pipelines = new Mesh3DPipelineCache(
                Device,
                _material,
                _cube,
                RhiTextureFormat.Rgba16Float,
                RhiTextureFormat.Depth32Float);

            Pipeline = _pipelines.ForState(DepthState.TestAndWrite, CullMode.CounterClockwise, BlendMode.Opaque);
            Pass = new Mesh3DPass(Textures, _materials);

            Device.ClearCommands();
        }

        public RecordingRhiDevice Device { get; }

        public FakeTextureBindingSource Textures { get; }

        public IRhiRenderPipeline Pipeline { get; }

        public Mesh3DPass Pass { get; }

        public void Record(Action<IRhiRenderPassEncoder> draw)
        {
            using IRhiCommandEncoder encoder = Device.CreateCommandEncoder("frame");

            IRhiTexture target = Device.CreateTexture(new RhiTextureDescriptor(
                64,
                64,
                RhiTextureFormat.Rgba16Float,
                RhiTextureUsage.RenderTarget,
                Label: "lit"));

            using (IRhiRenderPassEncoder pass = encoder.BeginRenderPass(
                new RhiRenderPassDescriptor([new RhiColorAttachment(target.View)], Label: "forward")))
            {
                draw(pass);
            }

            Device.Submit([encoder.Finish()]);
        }

        public void Dispose()
        {
            _pipelines.Dispose();
            _materials.Dispose();
            _cube.Dispose();
            _material.Dispose();
            Device.Dispose();
        }
    }
}
