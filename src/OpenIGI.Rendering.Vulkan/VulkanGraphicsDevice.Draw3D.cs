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
    private DepthState _depthState = DepthState.Disabled;
    private CullMode _cullMode = CullMode.None;

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
        BindMeshState(resource, modelView, diffuse, lightmap, blend);
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
        BindMeshState(resource, modelView, diffuse, lightmap, blend);

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

    private void BindMeshState(
        VulkanMesh mesh,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend)
    {
        // A shadow cascade is being rendered: the mesh casts depth into the shadow map and nothing else.
        if (_inShadowPass)
        {
            BindShadow(mesh, modelView);
            return;
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
    }

    private unsafe void BindShadow(VulkanMesh mesh, in Matrix4f modelView)
    {
        Vk vk = _instance.Vk;
        CommandBuffer cmd = _frameCommandBuffer;

        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _deferred.ShadowPipeline.Pipeline);
        BindMeshBuffers(mesh);

        // The cascade's model-view-projection: the frame's projection is the cascade's orthographic one and
        // the model-view is the light-camera view, both set by the shadow pass before it draws.
        Matrix4f mvp = _projection * modelView;
        float* data = stackalloc float[16];
        mvp.CopyColumnMajorTo(new Span<float>(data, 16));
        vk.CmdPushConstants(cmd, _deferred.ShadowPipeline.PipelineLayout, ShaderStageFlags.VertexBit, 0, sizeof(float) * 16, data);
    }

    private unsafe void BindGeometry(
        VulkanMesh mesh,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap)
    {
        Vk vk = _instance.Vk;
        CommandBuffer cmd = _frameCommandBuffer;

        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _deferred.Geometry.ForState(_depthState, _cullMode));
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
        ObserveForRayTracing(mesh, modelView, normalMatrix);

        float* data = stackalloc float[32];
        mvp.CopyColumnMajorTo(new Span<float>(data, 16));
        data[16] = normalMatrix.M11; data[17] = normalMatrix.M21; data[18] = normalMatrix.M31; data[19] = 0f;
        data[20] = normalMatrix.M12; data[21] = normalMatrix.M22; data[22] = normalMatrix.M32; data[23] = 0f;
        data[24] = normalMatrix.M13; data[25] = normalMatrix.M23; data[26] = normalMatrix.M33; data[27] = 0f;
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

        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, pipeline.ForState(_depthState, _cullMode, blend));
        BindMeshBuffers(mesh);
        BindMeshTextures(pipeline.PipelineLayout, diffuse, lightmap);

        // The forward and direct pipelines take only the combined model-view-projection; their shader
        // applies the OpenGL-to-Vulkan clip correction.
        Matrix4f mvp = _projection * modelView;
        float* data = stackalloc float[16];
        mvp.CopyColumnMajorTo(new Span<float>(data, 16));
        vk.CmdPushConstants(cmd, pipeline.PipelineLayout, ShaderStageFlags.VertexBit, 0, sizeof(float) * 16, data);
    }

    private unsafe void BindMeshBuffers(VulkanMesh mesh)
    {
        Vk vk = _instance.Vk;
        Buffer vertexBuffer = mesh.VertexBuffer;
        ulong vertexOffset = 0;
        vk.CmdBindVertexBuffers(_frameCommandBuffer, 0, 1, &vertexBuffer, &vertexOffset);
        vk.CmdBindIndexBuffer(_frameCommandBuffer, mesh.IndexBuffer, 0, IndexType.Uint16);
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
        _instance.Vk.CmdBindDescriptorSets(_frameCommandBuffer, PipelineBindPoint.Graphics, layout, 0, 1, &descriptorSet, 0, null);
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
    private void ObserveForRayTracing(VulkanMesh mesh, in Matrix4f modelView, in Matrix3f normalMatrix)
    {
        if (_worldScene is null)
        {
            return;
        }

        if (!_meshIds.TryGetValue(mesh, out uint meshId))
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
