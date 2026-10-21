namespace OpenIGI.Rendering.Rhi;

/// <summary>
/// Records work for the GPU: passes, copies and queries.
/// </summary>
/// <remarks>
/// <para>
/// One encoder records one submission. It is not thread-safe and is not meant to be: the frame is
/// recorded from one thread, and an implementation is free to write straight into a command buffer.
/// </para>
/// <para>
/// <b>There are no barriers here.</b> What a resource is ready for is the implementation's business:
/// Vulkan tracks it and inserts the transitions, WebGPU has no such notion at all. A pass that reads
/// what another pass wrote simply reads it, and an implementation that needs a barrier to make that true
/// puts one in. This is the single largest difference from the interface it replaces, and the reason a
/// browser backend is possible.
/// </para>
/// </remarks>
public interface IRhiCommandEncoder : IDisposable
{
    /// <summary>Gets the name this encoder was created with.</summary>
    string Label { get; }

    /// <summary>Begins a render pass.</summary>
    /// <param name="descriptor">What it draws into.</param>
    /// <returns>The pass, which must be ended before anything else is recorded.</returns>
    IRhiRenderPassEncoder BeginRenderPass(RhiRenderPassDescriptor descriptor);

    /// <summary>Begins a compute pass.</summary>
    /// <param name="descriptor">How it is recorded.</param>
    /// <returns>The pass, which must be ended before anything else is recorded.</returns>
    IRhiComputePassEncoder BeginComputePass(in RhiComputePassDescriptor descriptor = default);

    /// <summary>Copies between two buffers.</summary>
    /// <param name="source">The buffer read.</param>
    /// <param name="sourceOffset">Where the read starts.</param>
    /// <param name="destination">The buffer written.</param>
    /// <param name="destinationOffset">Where the write starts.</param>
    /// <param name="sizeInBytes">How many bytes to copy.</param>
    void CopyBufferToBuffer(
        IRhiBuffer source,
        ulong sourceOffset,
        IRhiBuffer destination,
        ulong destinationOffset,
        ulong sizeInBytes);

    /// <summary>Copies a buffer into a texture.</summary>
    /// <param name="source">Where the texels are laid out.</param>
    /// <param name="destination">The texture region written.</param>
    /// <param name="size">How much to copy.</param>
    void CopyBufferToTexture(
        in RhiBufferTextureCopy source,
        in RhiTextureRegion destination,
        in RhiExtent3D size);

    /// <summary>Copies a texture into a buffer, which is how a readback leaves the GPU.</summary>
    /// <param name="source">The texture region read.</param>
    /// <param name="destination">Where the texels are laid out.</param>
    /// <param name="size">How much to copy.</param>
    void CopyTextureToBuffer(
        in RhiTextureRegion source,
        in RhiBufferTextureCopy destination,
        in RhiExtent3D size);

    /// <summary>Copies between two textures.</summary>
    /// <param name="source">The region read.</param>
    /// <param name="destination">The region written.</param>
    /// <param name="size">How much to copy.</param>
    void CopyTextureToTexture(
        in RhiTextureRegion source,
        in RhiTextureRegion destination,
        in RhiExtent3D size);

    /// <summary>Fills a buffer range with zeroes.</summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="offset">Where to start.</param>
    /// <param name="sizeInBytes">How many bytes, or zero for the rest of it.</param>
    void ClearBuffer(IRhiBuffer buffer, ulong offset = 0, ulong sizeInBytes = 0);

    /// <summary>Copies a query set's results into a buffer.</summary>
    /// <param name="querySet">The set.</param>
    /// <param name="firstQuery">The first slot.</param>
    /// <param name="queryCount">How many slots.</param>
    /// <param name="destination">The buffer written.</param>
    /// <param name="destinationOffset">Where the write starts.</param>
    void ResolveQuerySet(
        IRhiQuerySet querySet,
        int firstQuery,
        int queryCount,
        IRhiBuffer destination,
        ulong destinationOffset);

    /// <summary>Records a frame's acceleration-structure builds: the meshes, then the scene over them.</summary>
    /// <param name="bottomLevel">The bottom-level builds and refits, in any order among themselves.</param>
    /// <param name="topLevel">The top-level build that reads them, or <see langword="null"/> for none.</param>
    /// <param name="timestamps">Where to record when the builds began and ended, or <see langword="null"/>.</param>
    /// <remarks>
    /// <para>
    /// Outside any pass. One call for the batch, because the ordering inside it is fixed and is the whole of
    /// what makes it safe, and the backend owns it as it owns every other barrier: whatever traced or wrote
    /// geometry before is finished before a build writes; every bottom-level build is finished before the
    /// top level reads them; and the top level is finished before anything after this traces against it.
    /// Coarse memory barriers, not per-resource ones, because a structure is reached by address.
    /// </para>
    /// <para>
    /// Only on a device with <see cref="IRhiDevice.AccelerationStructures"/>.
    /// </para>
    /// </remarks>
    void BuildAccelerationStructures(
        ReadOnlySpan<RhiBottomLevelBuild> bottomLevel,
        RhiTopLevelBuild? topLevel,
        RhiTimestampWrites? timestamps = null) =>
        throw new NotSupportedException("This device builds no acceleration structures.");

    /// <summary>Finishes recording.</summary>
    /// <param name="label">A name for the recorded work.</param>
    /// <returns>The recorded commands, ready to submit.</returns>
    IRhiCommandBuffer Finish(string label = "");
}

/// <summary>What is common to recording inside a pass.</summary>
public interface IRhiPassEncoder : IDisposable
{
    /// <summary>Binds a group of resources.</summary>
    /// <param name="index">Which group of the pipeline's layout.</param>
    /// <param name="group">The resources.</param>
    /// <param name="dynamicOffsets">
    /// One offset per dynamic entry of the group's layout, in binding order. Empty for a group with none.
    /// </param>
    void SetBindGroup(int index, IRhiBindGroup group, ReadOnlySpan<uint> dynamicOffsets = default);

    /// <summary>Sets the push data the pipeline's layout declared.</summary>
    /// <param name="data">The bytes, which must not exceed the layout's declared size.</param>
    /// <param name="offset">Where in the push block to write them.</param>
    /// <remarks>
    /// Vulkan writes push constants; a browser writes into a per-frame uniform ring and binds it by
    /// dynamic offset. The cost differs and the meaning does not.
    /// </remarks>
    void SetPushData(ReadOnlySpan<byte> data, int offset = 0);

    /// <summary>Marks the start of a labelled region, which a capture shows as a group.</summary>
    /// <param name="label">The region's name.</param>
    void PushDebugGroup(string label);

    /// <summary>Marks the end of the innermost labelled region.</summary>
    void PopDebugGroup();

    /// <summary>Ends the pass. Nothing more may be recorded into it.</summary>
    void EndPass();
}

/// <summary>Records draws.</summary>
public interface IRhiRenderPassEncoder : IRhiPassEncoder
{
    /// <summary>Binds the pipeline the following draws use.</summary>
    /// <param name="pipeline">The pipeline.</param>
    void SetPipeline(IRhiRenderPipeline pipeline);

    /// <summary>Binds a vertex buffer.</summary>
    /// <param name="slot">Which of the pipeline's vertex buffers.</param>
    /// <param name="buffer">The buffer.</param>
    /// <param name="offset">Where the data starts.</param>
    void SetVertexBuffer(int slot, IRhiBuffer buffer, ulong offset = 0);

    /// <summary>Binds the index buffer.</summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="format">How wide one index is.</param>
    /// <param name="offset">Where the data starts.</param>
    void SetIndexBuffer(IRhiBuffer buffer, RhiIndexFormat format, ulong offset = 0);

    /// <summary>Sets the region of the target the following draws write to.</summary>
    /// <param name="x">The left edge in pixels.</param>
    /// <param name="y">The top edge in pixels.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="minDepth">The near end of the depth range.</param>
    /// <param name="maxDepth">The far end of the depth range.</param>
    void SetViewport(float x, float y, float width, float height, float minDepth = 0f, float maxDepth = 1f);

    /// <summary>Sets the rectangle outside which fragments are discarded.</summary>
    /// <param name="x">The left edge in pixels.</param>
    /// <param name="y">The top edge in pixels.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    void SetScissor(int x, int y, int width, int height);

    /// <summary>Sets the constant a blend factor referring to it uses.</summary>
    /// <param name="color">The constant.</param>
    void SetBlendConstant(in RhiClearColor color);

    /// <summary>Draws without an index buffer.</summary>
    /// <param name="vertexCount">How many vertices.</param>
    /// <param name="instanceCount">How many instances.</param>
    /// <param name="firstVertex">The first vertex.</param>
    /// <param name="firstInstance">The first instance.</param>
    void Draw(int vertexCount, int instanceCount = 1, int firstVertex = 0, int firstInstance = 0);

    /// <summary>Draws through the index buffer.</summary>
    /// <param name="indexCount">How many indices.</param>
    /// <param name="instanceCount">How many instances.</param>
    /// <param name="firstIndex">The first index.</param>
    /// <param name="baseVertex">A value added to every index.</param>
    /// <param name="firstInstance">The first instance.</param>
    void DrawIndexed(
        int indexCount,
        int instanceCount = 1,
        int firstIndex = 0,
        int baseVertex = 0,
        int firstInstance = 0);

    /// <summary>Draws with the arguments taken from a buffer.</summary>
    /// <param name="arguments">The buffer holding them.</param>
    /// <param name="offset">Where they start.</param>
    void DrawIndirect(IRhiBuffer arguments, ulong offset);

    /// <summary>Draws indexed with the arguments taken from a buffer.</summary>
    /// <param name="arguments">The buffer holding them.</param>
    /// <param name="offset">Where they start.</param>
    void DrawIndexedIndirect(IRhiBuffer arguments, ulong offset);

    /// <summary>Begins counting the samples the following draws write.</summary>
    /// <param name="queryIndex">The slot of the pass's occlusion query set.</param>
    void BeginOcclusionQuery(int queryIndex);

    /// <summary>Ends the occlusion query.</summary>
    void EndOcclusionQuery();
}

/// <summary>Records dispatches.</summary>
public interface IRhiComputePassEncoder : IRhiPassEncoder
{
    /// <summary>Binds the pipeline the following dispatches use.</summary>
    /// <param name="pipeline">The pipeline.</param>
    void SetPipeline(IRhiComputePipeline pipeline);

    /// <summary>Runs a grid of workgroups.</summary>
    /// <param name="x">How many along x.</param>
    /// <param name="y">How many along y.</param>
    /// <param name="z">How many along z.</param>
    void Dispatch(int x, int y = 1, int z = 1);

    /// <summary>Runs a grid of workgroups whose size is taken from a buffer.</summary>
    /// <param name="arguments">The buffer holding it.</param>
    /// <param name="offset">Where it starts.</param>
    void DispatchIndirect(IRhiBuffer arguments, ulong offset);
}
