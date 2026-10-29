using OpenIGI.Rendering.Pipeline.Draw2D;
using OpenIGI.Rendering.Pipeline.Meshes;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.Mesh3D;

/// <summary>
/// How a mesh reaches each of the four pipelines that draw one: the pipeline, the material group, both
/// vertex streams, the index buffer, the push data, the draw.
/// </summary>
/// <remarks>
/// <para>
/// Thin on purpose. What is easy to get wrong here is not arithmetic but bookkeeping — binding one
/// vertex stream and forgetting the other, or forgetting to stamp the pose slots a frame has bound, or
/// issuing a range that runs past the index buffer — and each of those is a defect that appears as
/// judder, or as a lost device, rather than as a failure. Gathered in one place they can be stated
/// once and checked.
/// </para>
/// <para>
/// What to push is the caller's: the block a draw carries depends on which pass is running and is
/// written by its own function, which is what makes those testable at all. This binds it and no more.
/// </para>
/// </remarks>
/// <param name="textures">Where a handle is turned into something bindable.</param>
/// <param name="materials">The groups a draw's textures are bound through.</param>
public sealed class Mesh3DPass(ITextureBindingSource textures, MaterialBindGroupCache materials)
{
    private readonly ITextureBindingSource _textures = textures
        ?? throw new ArgumentNullException(nameof(textures));

    private readonly MaterialBindGroupCache _materials = materials
        ?? throw new ArgumentNullException(nameof(materials));

    /// <summary>Gets how many draws were dropped because their range ran past the index buffer.</summary>
    /// <remarks>
    /// Counted rather than merely refused, because it is the tell for a terrain page whose upload and
    /// whose draw disagree, and because zero is what a correct run reports.
    /// </remarks>
    public int DroppedRanges { get; private set; }

    /// <summary>Records one draw.</summary>
    /// <param name="pass">The pass to record into.</param>
    /// <param name="pipeline">The pipeline the draw goes through.</param>
    /// <param name="call">The mesh, the range and the state.</param>
    /// <param name="push">The block the draw carries.</param>
    /// <param name="frameCounter">
    /// The frame being built, which is stamped on the mesh's live pose slots so that a later pose write
    /// does not land on vertices this frame is still fetching.
    /// </param>
    /// <param name="frameGroup">
    /// The pass's own group, bound at index one, for a pipeline that takes one — the geometry path's
    /// per-frame surface block. Bound here rather than by the caller because a group may only be bound
    /// once a pipeline is, and that ordering is easy to get wrong and silent when it is right by
    /// accident.
    /// </param>
    /// <returns><see langword="false"/> when the range was dropped.</returns>
    public bool Draw(
        IRhiRenderPassEncoder pass,
        IRhiRenderPipeline pipeline,
        in MeshDrawCall call,
        ReadOnlySpan<byte> push,
        ulong frameCounter,
        IRhiBindGroup? frameGroup = null)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(pipeline);

        if (!Bind(pass, pipeline, call, push, frameCounter, frameGroup: frameGroup))
        {
            return false;
        }

        pass.DrawIndexed(call.IndexCount, 1, call.IndexOffset);
        return true;
    }

    /// <summary>Records several ranges of one mesh, binding its state once.</summary>
    /// <param name="pass">The pass to record into.</param>
    /// <param name="pipeline">The pipeline the draws go through.</param>
    /// <param name="call">The mesh and the state; its own range is ignored.</param>
    /// <param name="ranges">The ranges to draw.</param>
    /// <param name="push">The block the draws carry.</param>
    /// <param name="frameCounter">The frame being built.</param>
    /// <param name="frameGroup">The pass's own group, bound at index one, for a pipeline that takes one.</param>
    /// <returns>How many ranges were drawn.</returns>
    /// <remarks>
    /// The terrain's shape: one mesh, one material, many runs. State is shared, so it is bound once and
    /// each range is a draw into the same buffers.
    /// </remarks>
    public int DrawRanges(
        IRhiRenderPassEncoder pass,
        IRhiRenderPipeline pipeline,
        in MeshDrawCall call,
        ReadOnlySpan<MeshDrawRange> ranges,
        ReadOnlySpan<byte> push,
        ulong frameCounter,
        IRhiBindGroup? frameGroup = null)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(pipeline);

        if (ranges.IsEmpty)
        {
            return 0;
        }

        MeshDrawCall whole = call with { IndexOffset = 0, IndexCount = 0 };

        if (!Bind(pass, pipeline, whole, push, frameCounter, checkRange: false, frameGroup: frameGroup))
        {
            return 0;
        }

        int drawn = 0;

        foreach (MeshDrawRange range in ranges)
        {
            if (!InBounds(call.Mesh, range.IndexOffset, range.IndexCount))
            {
                if (range.IndexCount > 0)
                {
                    DroppedRanges++;
                }

                continue;
            }

            pass.DrawIndexed(range.IndexCount, 1, range.IndexOffset);
            drawn++;
        }

        return drawn;
    }

    /// <summary>Whether a range is one the index buffer actually holds.</summary>
    /// <param name="mesh">The mesh.</param>
    /// <param name="indexOffset">The first index.</param>
    /// <param name="indexCount">How many.</param>
    /// <returns><see langword="true"/> when the range is safe to issue.</returns>
    /// <remarks>
    /// A range past the end must be dropped rather than issued. Bounded buffer access does not cover
    /// out-of-bounds index-buffer reads on either backend, so the draw would not read garbage — it would
    /// lose the device.
    /// </remarks>
    public static bool InBounds(RhiMesh mesh, int indexOffset, int indexCount)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        return indexCount > 0
            && indexOffset >= 0
            && indexOffset + indexCount <= mesh.IndexCapacity;
    }

    // The materials lately bound, by the handles that named them, in a small table indexed by those handles: three
    // texture lookups and a group lookup a draw were a tenth of a browser's frame, and a frame binds a few dozen
    // materials thousands of times. Emptied whenever the cache lets a group go.
    private const int RecentMaterials = 64;
    private readonly RecentMaterial[] _recent = new RecentMaterial[RecentMaterials];
    private int _recentGeneration;

    private IRhiBindGroup Material(TextureHandle diffuse, TextureHandle lightmap, TextureHandle detail)
    {
        if (_materials.Generation != _recentGeneration)
        {
            Array.Clear(_recent);
            _recentGeneration = _materials.Generation;
        }

        uint hash = (diffuse.Id * 0x9E3779B1u) ^ (lightmap.Id * 0x85EBCA77u) ^ (detail.Id * 0xC2B2AE3Du);
        ref RecentMaterial slot = ref _recent[(int)(hash >> 26)];
        if (slot.Group is { } known && slot.Diffuse == diffuse && slot.Lightmap == lightmap && slot.Detail == detail)
        {
            return known;
        }

        IRhiBindGroup group = _materials.Get(_textures.Resolve(diffuse), _textures.Resolve(lightmap), _textures.Resolve(detail));
        slot = new RecentMaterial(diffuse, lightmap, detail, group);
        return group;
    }

    private readonly record struct RecentMaterial(
        TextureHandle Diffuse, TextureHandle Lightmap, TextureHandle Detail, IRhiBindGroup? Group);

    private bool Bind(
        IRhiRenderPassEncoder pass,
        IRhiRenderPipeline pipeline,
        in MeshDrawCall call,
        ReadOnlySpan<byte> push,
        ulong frameCounter,
        bool checkRange = true,
        IRhiBindGroup? frameGroup = null)
    {
        if (checkRange && !InBounds(call.Mesh, call.IndexOffset, call.IndexCount))
        {
            if (call.IndexCount > 0)
            {
                DroppedRanges++;
            }

            return false;
        }

        pass.SetPipeline(pipeline);

        pass.SetBindGroup(0, Material(call.Diffuse, call.Lightmap, call.Detail));

        if (frameGroup is not null)
        {
            pass.SetBindGroup(1, frameGroup);
        }

        if (!push.IsEmpty)
        {
            pass.SetPushData(push);
        }

        // Both streams, always. A static mesh reports one buffer as both, so the delta the vertex stage
        // forms is zero and nothing has to say which kind of mesh it is -- but a stream left unbound is
        // a pipeline drawing from whatever the last draw left there.
        pass.SetVertexBuffer(0, call.Mesh.VertexBuffer);
        pass.SetVertexBuffer(1, call.Mesh.PreviousVertexBuffer);
        pass.SetIndexBuffer(call.Mesh.IndexBuffer, RhiIndexFormat.Uint16);

        // This frame has now bound both live poses, which is what keeps the next pose write off memory
        // it is still reading.
        call.Mesh.MarkPoseSlotsBound(frameCounter);

        return true;
    }
}
