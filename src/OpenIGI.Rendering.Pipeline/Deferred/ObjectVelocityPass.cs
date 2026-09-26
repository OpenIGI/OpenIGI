using System.Runtime.InteropServices;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>One moving draw whose screen motion must be stamped into the velocity image.</summary>
/// <param name="Vertices">The pose the G-buffer drew from.</param>
/// <param name="PreviousVertices">The pose before it, which the blends mix with the first.</param>
/// <param name="Indices">The mesh's indices.</param>
/// <param name="IndexOffset">The first index of the drawn range.</param>
/// <param name="IndexCount">How many indices the range drew.</param>
/// <param name="Current">This frame's raster model-view-projection, including its jitter.</param>
/// <param name="Previous">
/// The preceding frame's unjittered transform with the current jitter applied, so subtracting the two
/// clip positions removes jitter while raster coverage still matches this frame.
/// </param>
/// <param name="FirstPerson">Whether the draw used the foreground depth-range sliver.</param>
/// <param name="PoseBlend">
/// How much of the previous-pose stream is in the position the G-buffer drew — the same blend, or this
/// pass rasterises a shape the depth test will reject.
/// </param>
/// <param name="PreviousPoseBlend">
/// The blend that reconstructs LAST frame's position from the same two streams. The difference between
/// the two is the whole of the pose motion; equal blends report rigid motion alone.
/// </param>
/// <remarks>
/// The buffers are taken when the mesh is drawn rather than when the stamp is recorded, so the stamp
/// rasterises exactly the pose the G-buffer did whatever the pose ring does in between.
/// </remarks>
internal readonly record struct ObjectVelocityDraw(
    IRhiBuffer Vertices,
    IRhiBuffer PreviousVertices,
    IRhiBuffer Indices,
    int IndexOffset,
    int IndexCount,
    Matrix4f Current,
    Matrix4f Previous,
    bool FirstPerson,
    float PoseBlend,
    float PreviousPoseBlend);

/// <summary>
/// A raster pass that overwrites the computed velocity with each MOVING object's own screen motion.
/// </summary>
/// <remarks>
/// <para>
/// The velocity compute derives motion from depth and the two cameras, which is exact for everything
/// that stands still — and silently wrong for everything that does not. A running soldier reprojects as
/// though he were part of the wall behind him, so the temporal accumulation blends his history from
/// where he was not, and draws a trail behind him. This pass redraws each animated mesh over the
/// velocity image with this frame's and the previous frame's transforms, writing the true screen delta
/// wherever the mesh actually won the depth test.
/// </para>
/// <para>
/// Every mesh binds its preceding pose as a second vertex stream, so limb and hand motion is measured
/// as well as rigid translation. What each frame reports is the part of a tick's motion that fell
/// between the two frames: the drawn position is a blend of the two poses and the caller says which
/// blend named last frame's. A mesh that does not animate reports one buffer as both streams and its
/// pose motion is zero by construction.
/// </para>
/// <para>
/// It rasterises with the exact transform the G-buffer draw used, jitter and all, so the depths match,
/// and tests GREATER-OR-EQUAL against the stored depth with a whisker of bias towards the eye, writing
/// none. Occluded parts of the mesh fail the test and leave the background's motion alone. No culling:
/// the G-buffer's cull state varies per draw, and a face the depth test rejects costs nothing, while a
/// face wrongly culled here leaves a hole in the object's motion.
/// </para>
/// </remarks>
internal sealed class ObjectVelocityPass : IDisposable
{
    /// <summary>How many bytes of push data the stamp reads: a matrix, three rows and the blends.</summary>
    public const int PushDataBytes = 32 * sizeof(float);

    // Towards the eye in reverse-Z, where greater is nearer, so the stamp cannot lose its own surface to
    // a rounding difference between the two rasterisations.
    private const float DepthBias = 4f;

    private readonly IRhiDevice _device;
    private readonly ShaderReflection _reflection;
    private readonly IRhiShaderModule _module;
    private readonly IRhiPipelineLayout _layout;
    private readonly Dictionary<RhiTextureFormat, IRhiRenderPipeline> _pipelines = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ObjectVelocityPass"/> class.</summary>
    /// <param name="device">The device.</param>
    public ObjectVelocityPass(IRhiDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        _reflection = ShaderCatalog.GetReflection(ShaderModuleId.ObjectVelocity);
        _module = device.CreateShaderModule(
            new RhiShaderModuleDescriptor(ShaderModuleId.ObjectVelocity, Label: "ObjectVelocity"));
        _layout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [],
            PushDataBytes,
            RhiShaderStages.Vertex | RhiShaderStages.Fragment,
            "ObjectVelocity"));
    }

    /// <summary>The pipeline that draws into a velocity image of this format, built on first use.</summary>
    /// <param name="velocity">The format the velocity image actually has.</param>
    /// <remarks>
    /// Taken from the image rather than from the target table: a backend that cannot write the table's
    /// format as storage -- WebGPU, for a two-channel half float -- widens it, and a pipeline built for the
    /// narrower format is one the pass refuses.
    /// </remarks>
    private IRhiRenderPipeline PipelineFor(RhiTextureFormat velocity)
    {
        if (_pipelines.TryGetValue(velocity, out IRhiRenderPipeline? existing))
        {
            return existing;
        }

        IRhiRenderPipeline pipeline = _device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            _layout,
            new RhiShaderStage(
                _module,
                _reflection.EntryPointFor(ShaderStage.Vertex).Name,
                ShaderConstants.Declared(_reflection, Mesh3DConstants.TargetConstants)),
            new RhiShaderStage(_module, _reflection.EntryPointFor(ShaderStage.Fragment).Name),

            // Both frames' positions come from whole-pose streams; the rest of each vertex is stepped over.
            [
                new RhiVertexBufferLayout(
                    Vertex3D.Size,
                    RhiVertexStepMode.Vertex,
                    [new RhiVertexElement(0, RhiVertexFormat.Float32x3, 0)]),
                new RhiVertexBufferLayout(
                    Vertex3D.Size,
                    RhiVertexStepMode.Vertex,
                    [new RhiVertexElement(1, RhiVertexFormat.Float32x3, 0)]),
            ],
            [new RhiColorTargetState(velocity, null, RhiColorWrite.Red | RhiColorWrite.Green)],
            new RhiDepthStencilState(
                SceneTargetSet.FormatOf(SceneTargetId.Depth),
                DepthWrite: false,
                RhiCompare.GreaterEqual,
                DepthBiasConstant: DepthBias),
            new RhiPrimitiveState(RhiPrimitiveTopology.TriangleList, RhiCullMode.None),
            SampleCount: 1,
            $"ObjectVelocity-{velocity}"));

        _pipelines[velocity] = pipeline;
        return pipeline;
    }

    /// <summary>Records the pass: one draw per moving mesh, over the velocity image.</summary>
    /// <param name="encoder">The frame's encoder, outside any pass.</param>
    /// <param name="scene">The frame's targets; its velocity has already been derived from the depth.</param>
    /// <param name="draws">The moving draws recorded this frame.</param>
    /// <param name="timestamps">Where to time the pass, or <see langword="null"/>.</param>
    public void Record(
        IRhiCommandEncoder encoder,
        SceneTargetSet scene,
        IReadOnlyList<ObjectVelocityDraw> draws,
        RhiTimestampWrites? timestamps)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(draws);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using IRhiRenderPassEncoder pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor(
            [new RhiColorAttachment(scene.View(SceneTargetId.Velocity), RhiLoadOp.Load, RhiStoreOp.Store)],
            new RhiDepthAttachment(
                scene.View(SceneTargetId.Depth),
                RhiLoadOp.Load,
                RhiStoreOp.Store,
                DepthReadOnly: true),
            timestamps,
            Label: "object-velocity"));

        pass.SetPipeline(PipelineFor(scene.View(SceneTargetId.Velocity).Texture.Format));
        pass.SetScissor(0, 0, scene.Width, scene.Height);

        Span<float> push = stackalloc float[PushDataBytes / sizeof(float)];
        bool? viewportIsFirstPerson = null;

        foreach (ObjectVelocityDraw draw in draws)
        {
            // The view model was rasterised into the nearest sliver of the depth range, so it keeps its
            // self-occlusion without a wall cutting through it. Its stamp has to land in the same sliver,
            // or its depth cannot match the stored one and every fragment fails the test.
            if (viewportIsFirstPerson != draw.FirstPerson)
            {
                pass.SetViewport(
                    0,
                    0,
                    scene.Width,
                    scene.Height,
                    draw.FirstPerson ? Viewport.ForegroundDepthFloor : 0f,
                    1f);
                viewportIsFirstPerson = draw.FirstPerson;
            }

            draw.Current.CopyColumnMajorTo(push[..16]);

            // The previous transform's x, y and w rows; its z row is the one the vector never asks for.
            Matrix4f was = draw.Previous;
            push[16] = was.M11; push[17] = was.M12; push[18] = was.M13; push[19] = was.M14;
            push[20] = was.M21; push[21] = was.M22; push[22] = was.M23; push[23] = was.M24;
            push[24] = was.M41; push[25] = was.M42; push[26] = was.M43; push[27] = was.M44;

            push[28] = draw.PoseBlend;
            push[29] = draw.PreviousPoseBlend;
            push[30] = 0f;
            push[31] = 0f;
            pass.SetPushData(MemoryMarshal.AsBytes(push));

            pass.SetVertexBuffer(0, draw.Vertices);
            pass.SetVertexBuffer(1, draw.PreviousVertices);
            pass.SetIndexBuffer(draw.Indices, RhiIndexFormat.Uint16);
            pass.DrawIndexed(draw.IndexCount, 1, draw.IndexOffset);
        }

        pass.EndPass();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IRhiRenderPipeline pipeline in _pipelines.Values)
        {
            pipeline.Dispose();
        }

        _pipelines.Clear();
        _layout.Dispose();
        _module.Dispose();
    }
}
