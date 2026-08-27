using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Meshes;
using OpenIGI.Rendering.Vulkan.Textures;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>The 3D forward draw path for <see cref="VulkanGraphicsDevice"/> — meshes, world state, DrawMesh.</summary>
/// <remarks>
/// This is the original's direct world path: a mesh transformed by its model-view and the frame's
/// projection, shaded as diffuse × lightmap × colour. The projection is set once per frame, the
/// model-view travels with each draw as a push constant, and depth, culling and blend select which
/// cached pipeline the draw binds.
/// </remarks>
public sealed partial class VulkanGraphicsDevice
{
    private readonly Dictionary<uint, VulkanMesh> _meshes = [];

    // The same registry the other way round. A draw is handed the mesh itself, but the traceable world
    // keys its geometry by the identifier the seam knows it as, so one has to be found from the other.
    private readonly Dictionary<VulkanMesh, uint> _meshIds = [];
    private uint _nextMeshId = 1;
    private Matrix4f _projection = Matrix4f.Identity;

    // The terrain's level-of-detail fade: scale, offset, slope, and which kind of terrain page this is.
    // All zero for everything that is not terrain, which is how the vertex stage tells them apart.
    private (float Scale, float Offset, float Slope, float Mode) _detailFade;
    private DepthState _depthState = DepthState.Disabled;
    private CullMode _cullMode = CullMode.None;

    // What the frame's command buffer already has bound, so a draw asking for what is there records
    // nothing. The shadow pass binds one pipeline for every mesh in every cascade, and the terrain draws
    // range after range out of a single page mesh, so most of these calls were restating the state they
    // found. The buffer is remembered with them: Cards swaps _frameCommandBuffer for one of its own, and
    // state recorded into one buffer says nothing about another.
    private CommandBuffer _boundCommandBuffer;
    private Pipeline _boundPipeline;
    private Buffer _boundVertexBuffer;
    private Buffer _boundIndexBuffer;
    private DescriptorSet _boundDescriptorSet;
    private PipelineLayout _boundDescriptorLayout;

    /// <summary>Forgets what is bound, so the next draw restates all of it.</summary>
    /// <remarks>
    /// Every path that records a graphics bind without going through the helpers below must call this.
    /// Forgetting costs a redundant bind; failing to forget skips one that was needed, which draws the
    /// last mesh's geometry or the last pass's textures.
    /// </remarks>
    internal void InvalidateBoundState()
    {
        _boundPipeline = default;
        _boundVertexBuffer = default;
        _boundIndexBuffer = default;
        _boundDescriptorSet = default;
        _boundDescriptorLayout = default;
    }

    /// <summary>Binds a graphics pipeline unless it is the one already bound.</summary>
    private void BindGraphicsPipeline(Pipeline pipeline)
    {
        if (_boundCommandBuffer.Handle != _frameCommandBuffer.Handle)
        {
            _boundCommandBuffer = _frameCommandBuffer;
            InvalidateBoundState();
        }

        if (_boundPipeline.Handle == pipeline.Handle)
        {
            return;
        }

        _instance.Vk.CmdBindPipeline(_frameCommandBuffer, PipelineBindPoint.Graphics, pipeline);
        _boundPipeline = pipeline;

        // A pipeline built against a different layout unbinds the descriptor sets, so what was bound for
        // the last one cannot be assumed to have survived this.
        _boundDescriptorSet = default;
        _boundDescriptorLayout = default;
    }

    /// <inheritdoc/>
    public MeshHandle CreateMesh(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices)
    {
        ThrowIfDisposed();
        ValidateIndices(vertices.Length, indices);
        VulkanMesh mesh = VulkanMesh.CreateStatic(_instance.Vk, _device, _allocator, _commands, vertices, indices);
        return Register(mesh);
    }

    /// <inheritdoc/>
    public MeshHandle CreateAnimatedMesh(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices)
    {
        ThrowIfDisposed();
        ValidateIndices(vertices.Length, indices);
        VulkanMesh mesh = VulkanMesh.CreateAnimated(_instance.Vk, _device, _allocator, _commands, vertices, indices);
        return Register(mesh);
    }

    /// <inheritdoc/>
    public void UpdateAnimatedMesh(MeshHandle mesh, ReadOnlySpan<Vertex3D> vertices)
    {
        ThrowIfDisposed();
        Resolve(mesh).UpdateVertices(vertices);
    }

    /// <inheritdoc/>
    public MeshHandle CreateMeshStorage(int vertexCapacity, int indexCapacity)
    {
        ThrowIfDisposed();
        if (vertexCapacity <= 0 || indexCapacity <= 0)
        {
            throw new ArgumentException("A storage mesh needs positive capacities.");
        }

        if (vertexCapacity > ushort.MaxValue + 1)
        {
            throw new ArgumentException("A storage mesh cannot exceed 16-bit vertex indexing.", nameof(vertexCapacity));
        }

        VulkanMesh mesh = VulkanMesh.CreateStorage(_instance.Vk, _device, _allocator, _commands, vertexCapacity, indexCapacity);
        return Register(mesh);
    }

    /// <inheritdoc/>
    public void UploadMeshRange(
        MeshHandle mesh,
        int vertexOffset,
        ReadOnlySpan<Vertex3D> vertices,
        int indexOffset,
        ReadOnlySpan<ushort> indices)
    {
        ThrowIfDisposed();
        Resolve(mesh).UploadRange(vertexOffset, vertices, indexOffset, indices);
    }

    /// <inheritdoc/>
    public void DestroyMesh(MeshHandle mesh)
    {
        ThrowIfDisposed();
        if (mesh == MeshHandle.None || !_meshes.Remove(mesh.Id, out VulkanMesh? resource))
        {
            return;
        }

        _meshIds.Remove(resource);
        _worldScene?.Forget(mesh.Id);

        // The mesh may still be referenced by a frame in flight, so it is not freed now; it is queued and
        // disposed once those frames have cycled — no device stall.
        QueueDeletion(resource);
    }

    /// <inheritdoc/>
    public void SetProjection(in Matrix4f projection)
    {
        ThrowIfDisposed();
        _projection = projection;
    }

    /// <inheritdoc/>
    public void SetDepthState(DepthState state)
    {
        ThrowIfDisposed();
        _depthState = state;
    }

    /// <inheritdoc/>
    public void SetCullMode(CullMode mode)
    {
        ThrowIfDisposed();
        _cullMode = mode;
    }

    /// <inheritdoc/>
    public unsafe void DrawMesh(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend)
    {
        ThrowIfDisposed();
        if (indexCount <= 0)
        {
            return;
        }

        VulkanMesh resource = Resolve(mesh);
        if (!BindMeshState(resource, modelView, diffuse, lightmap, blend))
        {
            return;
        }

        _instance.Vk.CmdDrawIndexed(_frameCommandBuffer, (uint)indexCount, 1, (uint)indexOffset, 0, 0);
    }

    /// <inheritdoc/>
    public unsafe void DrawMeshRanges(
        MeshHandle mesh,
        ReadOnlySpan<MeshDrawRange> ranges,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend)
    {
        ThrowIfDisposed();
        if (ranges.IsEmpty)
        {
            return;
        }

        VulkanMesh resource = Resolve(mesh);
        if (!BindMeshState(resource, modelView, diffuse, lightmap, blend))
        {
            return;
        }

        // State is shared across the ranges, so it is bound once and each range is a draw into the same
        // buffers — the multi-draw the seam documents, done as a run of indexed draws.
        Vk vk = _instance.Vk;
        int limit = resource.IndexCapacity;
        foreach (MeshDrawRange range in ranges)
        {
            // Plain robustBufferAccess does not cover out-of-bounds index-buffer reads, so a range that
            // runs past the index buffer must be dropped rather than issued — otherwise it loses the
            // device instead of merely reading garbage.
            if (range.IndexCount <= 0 || range.IndexOffset < 0 || range.IndexOffset + range.IndexCount > limit)
            {
                if (range.IndexCount > 0)
                {
                    WarnOnce("DrawMeshRangesOob", $"Dropped a terrain range [{range.IndexOffset}, +{range.IndexCount}) past index capacity {limit}.");
                }

                continue;
            }

            vk.CmdDrawIndexed(_frameCommandBuffer, (uint)range.IndexCount, 1, (uint)range.IndexOffset, 0, 0);
        }
    }

    private static void ValidateIndices(int vertexCount, ReadOnlySpan<ushort> indices)
    {
        if (indices.Length % 3 != 0)
        {
            throw new ArgumentException("The index count must be a multiple of three.", nameof(indices));
        }

        foreach (ushort index in indices)
        {
            if (index >= vertexCount)
            {
                throw new ArgumentException("An index addresses a vertex outside the mesh.", nameof(indices));
            }
        }
    }

    /// <summary>Binds everything a draw needs, and says whether the draw should happen at all.</summary>
    /// <param name="mesh">The mesh being drawn.</param>
    /// <param name="modelView">Its transform.</param>
    /// <param name="diffuse">Its colour texture.</param>
    /// <param name="lightmap">Its lightmap.</param>
    /// <param name="blend">How it blends.</param>
    /// <returns><see langword="false"/> when this draw has no business in the pass that is open.</returns>
    private bool BindMeshState(
        VulkanMesh mesh,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend)
    {
        // A shadow cascade is being rendered: the mesh casts depth into the shadow map and nothing else.
        if (_inShadowPass)
        {
            // Only what is solid casts. A blended draw is a decal over something that has already cast --
            // the terrain's detail layers sit on a base that is itself opaque -- and its alpha is a smooth
            // blend, not a cut-out. Putting it through the cut-out test turns that gradient into a hard
            // half-and-half stipple, and every detail tile then casts the shadow of a chequerboard.
            if (blend != BlendMode.Opaque)
            {
                return false;
            }

            BindShadow(mesh, modelView, diffuse);
            return true;
        }

        // The card atlas is open: this draw is a model being photographed flat into one of its pages,
        // not part of a frame at all.
        if (_cardCaptureActive)
        {
            BindCard(mesh, modelView, diffuse, lightmap);
            return true;
        }

        // A draw goes to one of three pipelines by where it is: the G-buffer during the geometry
        // subpass, the forward pipeline into the lit scene during the forward subpass, or the direct
        // mesh pipeline on the back buffer when there is no capture.
        if (_capturing && _deferredStage == DeferredStage.Geometry)
        {
            BindGeometry(mesh, modelView, diffuse, lightmap);
        }
        else if (_capturing && _deferredStage == DeferredStage.Forward)
        {
            BindForward(_deferred.Forward, mesh, modelView, diffuse, lightmap, blend);
        }
        else
        {
            EnsurePresentPass();
            BindForward(_mesh3D, mesh, modelView, diffuse, lightmap, blend);
        }

        return true;
    }

    /// <summary>Whether a rotation is the identity, within what single precision leaves of one.</summary>
    private static bool IsIdentity(in Matrix3f m)
    {
        const float slack = 1e-4f;
        return MathF.Abs(m.M11 - 1f) < slack && MathF.Abs(m.M22 - 1f) < slack && MathF.Abs(m.M33 - 1f) < slack
            && MathF.Abs(m.M12) < slack && MathF.Abs(m.M13) < slack
            && MathF.Abs(m.M21) < slack && MathF.Abs(m.M23) < slack
            && MathF.Abs(m.M31) < slack && MathF.Abs(m.M32) < slack;
    }

    /// <summary>Where a terrain page's origin sits relative to the camera, in world axes.</summary>
    /// <param name="modelView">The page's object-to-view transform.</param>
    /// <param name="viewToWorld">The rotation that undoes that view's, whichever view it is.</param>
    /// <param name="offset">The page origin minus the camera position.</param>
    /// <returns><see langword="true"/> when the offset could be worked out.</returns>
    /// <remarks>
    /// The model-view carries the camera subtraction already, folded in while it was still in double
    /// precision. Undoing its rotation recovers the offset in world axes, which is the frame the page's
    /// own vertices are in.
    /// </remarks>
    private static bool TerrainCameraOffset(in Matrix4f modelView, in Matrix3f viewToWorld, out Vector3f offset)
    {
        Matrix3f m = viewToWorld;
        offset = new Vector3f(
            (m.M11 * modelView.M14) + (m.M12 * modelView.M24) + (m.M13 * modelView.M34),
            (m.M21 * modelView.M14) + (m.M22 * modelView.M24) + (m.M23 * modelView.M34),
            (m.M31 * modelView.M14) + (m.M32 * modelView.M24) + (m.M33 * modelView.M34));
        return true;
    }

    private unsafe void BindShadow(VulkanMesh mesh, in Matrix4f modelView, TextureHandle diffuse)
    {
        Vk vk = _instance.Vk;
        CommandBuffer cmd = _frameCommandBuffer;

        BindGraphicsPipeline(_deferred.ShadowPipeline.Pipeline);
        BindMeshBuffers(mesh);

        // The colour texture, for its alpha alone: a cut-out must cast the shadow of the shape that is
        // there, not of the quad it was cut from.
        BindMeshTextures(_deferred.ShadowPipeline.PipelineLayout, diffuse, TextureHandle.None);

        // The cascade's model-view-projection: the frame's projection is the cascade's orthographic one and
        // the model-view is the light-camera view, both set by the shadow pass before it draws.
        Matrix4f mvp = _projection * modelView;
        float* data = stackalloc float[24];
        mvp.CopyColumnMajorTo(new Span<float>(data, 16));

        // Terrain slides between levels of detail, and it has to cast from where it ended up rather than
        // where it started, or the ground shadows itself along a shape it no longer has. The slide is
        // measured from the eye, not from the light — it has to be the same slide the frame drew — and
        // this pass looks along the light from where the eye stands, so the light's rotation is what
        // undoes its own view.
        Matrix3f lightToWorld = _cascadeLightRotations[Math.Clamp(_shadowCascade, 0, ShadowSettings.MaxCascades - 1)]
            .Transposed();

        if (_detailFade.Mode > 0f && TerrainCameraOffset(modelView, lightToWorld, out Vector3f offset))
        {
            data[16] = _detailFade.Scale; data[17] = _detailFade.Offset; data[18] = _detailFade.Slope;
            data[19] = _detailFade.Mode;
            data[20] = offset.X; data[21] = offset.Y; data[22] = offset.Z; data[23] = 0f;
        }
        else
        {
            for (int i = 16; i < 24; i++)
            {
                data[i] = 0f;
            }
        }

        vk.CmdPushConstants(cmd, _deferred.ShadowPipeline.PipelineLayout, ShaderStageFlags.VertexBit, 0, sizeof(float) * 24, data);
    }

    private unsafe void BindGeometry(
        VulkanMesh mesh,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap)
    {
        Vk vk = _instance.Vk;
        CommandBuffer cmd = _frameCommandBuffer;

        BindGraphicsPipeline(_deferred.Geometry.ForState(_depthState, _cullMode));
        BindMeshBuffers(mesh);
        BindMeshTextures(_deferred.Geometry.PipelineLayout, diffuse, lightmap);

        // The 128-byte push: the model-view-projection for position, a rotation for the normal, and the
        // material with the lightmap scale.
        Matrix4f mvp = _projection * modelView;

        // The G-buffer stores world-space normals, so the rotation pushed here is object-to-world: the
        // model-view's own rotation followed by the view-to-world one. Composing the two on the processor
        // costs nothing per vertex and saves the shader a second matrix and a second multiply.
        Matrix3f modelViewRotation = new(
            modelView.M11, modelView.M12, modelView.M13,
            modelView.M21, modelView.M22, modelView.M23,
            modelView.M31, modelView.M32, modelView.M33);
        Matrix3f normalMatrix = _viewToWorld * modelViewRotation;

        // Tell the traceable world this mesh is here. The transform it wants is object to absolute world:
        // the same rotation as the normal's, and the model-view's own translation rotated into world and
        // offset by where the camera is. Absolute rather than camera-relative because a placement recorded
        // now has to still be right in a frame whose camera has moved.
        ObserveForRayTracing(mesh, modelView, normalMatrix, diffuse);

        float* data = stackalloc float[32];
        mvp.CopyColumnMajorTo(new Span<float>(data, 16));

        // Terrain slides each vertex along the offset in its normal channel as the ground changes level of
        // detail, and the distance that slide is measured against is the vertex's own, so it has to happen
        // per vertex. That needs three numbers for the ramp and three for where the page sits relative to
        // the camera, and the push block is full.
        //
        // It has room all the same, because a terrain page is drawn with no rotation of its own: its
        // normal matrix is the view rotation undone by its own inverse, which is the identity, and nine
        // floats of identity carry nothing. So a terrain draw puts the ramp and the offset there instead,
        // and the fourth component of the first row — zero for everything else — is what says it has. The
        // identity is checked rather than assumed; a page that ever came in rotated keeps its matrix and
        // simply does not morph.
        if (_detailFade.Mode > 0f && IsIdentity(normalMatrix) && TerrainCameraOffset(modelView, _viewToWorld, out Vector3f offset))
        {
            data[16] = _detailFade.Scale; data[17] = _detailFade.Offset; data[18] = _detailFade.Slope;
            data[19] = _detailFade.Mode;
            data[20] = offset.X; data[21] = offset.Y; data[22] = offset.Z; data[23] = 0f;
            data[24] = 0f; data[25] = 0f; data[26] = 0f; data[27] = _alphaHashSeed;
        }
        else
        {
            data[16] = normalMatrix.M11; data[17] = normalMatrix.M21; data[18] = normalMatrix.M31; data[19] = 0f;
            data[20] = normalMatrix.M12; data[21] = normalMatrix.M22; data[22] = normalMatrix.M32; data[23] = 0f;
            data[24] = normalMatrix.M13; data[25] = normalMatrix.M23; data[26] = normalMatrix.M33; data[27] = _alphaHashSeed;
        }

        data[28] = _material.Roughness;
        data[29] = _material.Metallic;
        data[30] = _material.Emissive;
        data[31] = _lightmapScale;
        vk.CmdPushConstants(
            cmd,
            _deferred.Geometry.PipelineLayout,
            ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            0,
            128,
            data);
    }

    private unsafe void BindForward(
        Pipelines.Mesh3DPipeline pipeline,
        VulkanMesh mesh,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend)
    {
        Vk vk = _instance.Vk;
        CommandBuffer cmd = _frameCommandBuffer;

        BindGraphicsPipeline(pipeline.ForState(_depthState, _cullMode, blend));
        BindMeshBuffers(mesh);
        BindMeshTextures(pipeline.PipelineLayout, diffuse, lightmap);

        // The forward and direct pipelines take only the combined model-view-projection; their shader
        // applies the OpenGL-to-Vulkan clip correction.
        Matrix4f mvp = _projection * modelView;
        float* data = stackalloc float[17];
        mvp.CopyColumnMajorTo(new Span<float>(data, 16));

        // What this draw leaves in the lit target's alpha. A translucent draw keeps its own alpha,
        // because that is the blend's source; an opaque one has no use for it and carries the marker
        // instead, so a pass reading the lit colour back can tell the world from what is only in front
        // of the eye.
        data[16] = blend != BlendMode.Opaque ? -1f : (_firstPersonGeometry ? 0f : 1f);

        vk.CmdPushConstants(
            cmd,
            pipeline.PipelineLayout,
            ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            0,
            sizeof(float) * 17,
            data);
    }

    private unsafe void BindMeshBuffers(VulkanMesh mesh)
    {
        Vk vk = _instance.Vk;
        Buffer vertexBuffer = mesh.VertexBuffer;
        Buffer indexBuffer = mesh.IndexBuffer;

        // A terrain page is one mesh holding every chunk batched into it, drawn as a run of ranges, and a
        // model is drawn once per cascade and again for the frame. Both arrive here with the buffers they
        // asked for last time still bound.
        if (_boundVertexBuffer.Handle != vertexBuffer.Handle)
        {
            ulong vertexOffset = 0;
            vk.CmdBindVertexBuffers(_frameCommandBuffer, 0, 1, &vertexBuffer, &vertexOffset);
            _boundVertexBuffer = vertexBuffer;
        }

        if (_boundIndexBuffer.Handle != indexBuffer.Handle)
        {
            vk.CmdBindIndexBuffer(_frameCommandBuffer, indexBuffer, 0, IndexType.Uint16);
            _boundIndexBuffer = indexBuffer;
        }
    }

    private unsafe void BindMeshTextures(PipelineLayout layout, TextureHandle diffuse, TextureHandle lightmap)
    {
        uint diffuseId = TextureKey(diffuse);
        uint lightmapId = TextureKey(lightmap);
        DescriptorSet descriptorSet = _meshTextureDescriptors.Get(
            diffuseId,
            _textures[diffuseId],
            lightmapId,
            _textures[lightmapId]);

        // The layout travels with the set: the same set bound under a different pipeline layout is a
        // different binding, and the shadow, geometry and forward pipelines do not share one.
        if (_boundDescriptorSet.Handle == descriptorSet.Handle && _boundDescriptorLayout.Handle == layout.Handle)
        {
            return;
        }

        _instance.Vk.CmdBindDescriptorSets(_frameCommandBuffer, PipelineBindPoint.Graphics, layout, 0, 1, &descriptorSet, 0, null);
        _boundDescriptorSet = descriptorSet;
        _boundDescriptorLayout = layout;
    }

    private uint TextureKey(TextureHandle handle) =>
        handle != TextureHandle.None && _textures.ContainsKey(handle.Id) ? handle.Id : _whiteTexture.Id;

    private VulkanMesh Resolve(MeshHandle mesh)
    {
        if (!_meshes.TryGetValue(mesh.Id, out VulkanMesh? resource))
        {
            throw new ArgumentException($"Unknown mesh {mesh}.", nameof(mesh));
        }

        return resource;
    }

    private MeshHandle Register(VulkanMesh mesh)
    {
        uint id = _nextMeshId++;
        _meshes[id] = mesh;
        _meshIds[mesh] = id;
        return new MeshHandle(id);
    }

    /// <summary>Records a geometry-pass draw into the traceable copy of the world.</summary>
    /// <param name="mesh">The mesh being drawn.</param>
    /// <param name="modelView">Its object-to-view transform.</param>
    /// <param name="normalMatrix">Its object-to-world rotation, already composed for the normal.</param>
    /// <param name="diffuse">Its colour texture, whose alpha decides whether it is a solid occluder.</param>
    private void ObserveForRayTracing(
        VulkanMesh mesh,
        in Matrix4f modelView,
        in Matrix3f normalMatrix,
        TextureHandle diffuse)
    {
        if (_worldScene is null)
        {
            return;
        }

        if (!_meshIds.TryGetValue(mesh, out uint meshId))
        {
            return;
        }

        // The sky stays out of it. The dome is drawn as geometry like everything else, but it is light
        // rather than a lit surface -- which is what this flag says, and what the G-buffer turns into an
        // unlit shading model a few lines further on. Built into the structure it becomes a closed shell
        // around the level, and every ray fired at the sun from anywhere inside it hits: the traced mask
        // came back shadowed for every sun-facing pixel in the frame, indoors and out, which reads as a
        // level with no sunlight at all rather than as a bug in the sky. The reference keeps its sky out
        // of the ray-tracing scene for the same reason.
        if (_material.Emissive > 0.5f)
        {
            return;
        }

        // The cut-out surfaces stay out of the traceable world too. Glass, wire fences and foliage carry
        // an alpha channel the raster shadow path tests per texel -- the sun reaches the floor through a
        // clear pane and between the wires. The trace has no such per-texel test: every triangle it holds
        // is a solid occluder. So a glass pane built into the structure blocks the sun ray from every
        // floor point that sees the sky through a window, and the traced mask comes back shadowed over the
        // whole sunlit pattern the cascade correctly leaves lit. The same flag that keeps these surfaces
        // out of the ambient occlusion -- a depth buffer cannot describe a thin cut-out either -- keeps
        // them out of the trace, which is the honest reading of it until an any-hit shader can sample the
        // alpha at the hit.
        if (!_material.CastsOcclusion)
        {
            return;
        }

        // And the same exclusion decided from the TEXELS as well as from the material library. The
        // library's only rule for a cut-out is a name match on *_argb8888, which is a property of a file
        // name rather than of the surface: a masked texture that is not named that way is handed to the
        // trace as a solid occluder, and a ray query has no per-texel test -- every triangle it holds
        // blocks the sun completely, so such a surface would cast the shadow of a wall.
        //
        // Measured with the shaders' own alpha cut, so the two paths cannot disagree about what a surface
        // is: anything the rasteriser would punch holes in is something the trace must not call solid.
        //
        // Honest about its reach: this was written while chasing a fence that appeared to cast a wall's
        // shadow, and it did NOT fix it -- that level's chain-link is modelled geometry with an opaque
        // texture, so it was never mis-classified and the trace was casting it correctly all along. It
        // catches 171 draws a frame here and changes no pixel of either compare shot. It is kept because
        // the filename rule is the fragile half of a correctness decision, not because it fixed that.
        if (_textures.TryGetValue(diffuse.Id, out VulkanTexture? diffuseTexture) && diffuseTexture.HasCutoutAlpha)
        {
            return;
        }

        Matrix3f m = _viewToWorld;
        float tx = (m.M11 * modelView.M14) + (m.M12 * modelView.M24) + (m.M13 * modelView.M34);
        float ty = (m.M21 * modelView.M14) + (m.M22 * modelView.M24) + (m.M23 * modelView.M34);
        float tz = (m.M31 * modelView.M14) + (m.M32 * modelView.M24) + (m.M33 * modelView.M34);

        Matrix4f world = new(
            normalMatrix.M11, normalMatrix.M12, normalMatrix.M13, tx + _cameraPosition.X,
            normalMatrix.M21, normalMatrix.M22, normalMatrix.M23, ty + _cameraPosition.Y,
            normalMatrix.M31, normalMatrix.M32, normalMatrix.M33, tz + _cameraPosition.Z,
            0f, 0f, 0f, 1f);

        _worldScene.Observe(meshId, mesh, world);
    }
}
