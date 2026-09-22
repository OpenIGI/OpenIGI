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

    // The level's distance fog. A frame's property rather than a draw's: every world draw wears it, and
    // the sky clears it around its own dome exactly as sub_52D060 turns FOGENABLE off around the
    // original's. Its density is zero -- no fog -- for everything but the vanilla picture.
    private SceneFog _fog = SceneFog.None;

    // The flat light a model with no lightmap wears -- the original's D3DRENDERSTATE_AMBIENT, which is
    // the whole of its model lighting. One for every draw in the frame, like the fog; white leaves the
    // texture alone, which is what every path but the baked one wants.
    private Vector3f _modelAmbient = Vector3f.One;

    // The sequel's ground: its lightmap slot holds a detail colour to multiply into the base rather
    // than a bake to keep apart. Mode 3 in the first normal row's fourth component, which the G-buffer
    // shader reads beside the two the level-of-detail fade uses. See IGraphicsDevice.SetGroundDetail.
    private bool _groundDetail;

    // How much of the previous-pose vertex stream is in the position drawn now, and how much was in the
    // one drawn last frame. Zero for everything that does not animate -- and for those two streams are
    // one buffer, so any blend of a value with itself is that value and the picture is unchanged.
    private float _poseBlend;
    private float _previousPoseBlend;

    // Moving meshes drawn into this frame, and each occurrence's transform this frame and last. The
    // velocity compute derives motion from the camera alone, so animated G-buffer objects and the
    // separately transformed first-person layer must stamp their true screen delta afterwards — see
    // ObjectVelocityPass. The view-space
    // translation rides along for the mispair guard below: the composed clip transform has no column
    // that means metres.
    private readonly record struct ObjectVelocityState(Matrix4f MotionTransform, Vector3f ViewTranslation);

    private readonly List<Deferred.ObjectVelocityDraw> _objectVelocityDraws = [];
    private readonly Dictionary<long, ObjectVelocityState> _objectVelocityPrevious = [];
    private readonly Dictionary<long, ObjectVelocityState> _objectVelocityCurrent = [];
    private readonly Dictionary<uint, int> _objectVelocityOccurrence = [];

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Every mesh pipeline takes the mesh's previous-pose stream as a second vertex binding, so the
    /// blend is one number per draw and the vertex stage slides each vertex along its own delta. A
    /// simulation that ticks at thirty and a frame that presents at whatever the display asks for meet
    /// here: the frame draws a soldier <em>between</em> the two poses either side of it, rather than
    /// snapping onto the last one that landed.
    /// </para>
    /// <para>
    /// A mesh that is not animated reports its own vertices as its previous pose, so its delta is zero
    /// to the bit and no blend of it can move a vertex. That is what lets this be state the whole draw
    /// path carries rather than a branch: the terrain, the world and every static prop are drawn through
    /// the same shader and come out identical whatever is set here.
    /// </para>
    /// </remarks>
    public void SetPoseInterpolation(float currentBlend, float previousBlend)
    {
        ThrowIfDisposed();
        _poseBlend = Math.Clamp(currentBlend, 0f, 1f);
        _previousPoseBlend = Math.Clamp(previousBlend, 0f, 1f);
    }

    /// <inheritdoc/>
    public void ClearPoseInterpolation()
    {
        ThrowIfDisposed();
        _poseBlend = 0f;
        _previousPoseBlend = 0f;
    }

    /// <summary>Records one moving draw for the object-velocity stamp.</summary>
    /// <remarks>
    /// The key is the mesh plus which occurrence of it this is within the frame, so a mesh drawn several
    /// times pairs each draw with its own transform from the previous frame — as long as the draw order
    /// is stable, which a renderer iterating its own lists gives for free.
    /// </remarks>
    private void RecordObjectVelocity(
        uint meshId,
        VulkanMesh resource,
        int indexOffset,
        int indexCount,
        in Matrix4f modelView,
        bool firstPerson)
    {
        // Raster with the exact current projection so the depth test covers the same fragments as the
        // original draw. Motion itself is defined between UNJITTERED positions, matching the static
        // velocity field: applying this frame's jitter to both ends below preserves raster coverage while
        // cancelling the sampling offset out of the vector.
        Matrix4f rasterNow = _projection * modelView;
        Matrix4f motionNow = (firstPerson ? _projection : _temporalProjection) * modelView;
        Vector3f translation = new(modelView.M14, modelView.M24, modelView.M34);
        _objectVelocityOccurrence.TryGetValue(meshId, out int occurrence);
        _objectVelocityOccurrence[meshId] = occurrence + 1;

        long key = ((long)meshId << 16) | (uint)(occurrence & 0xFFFF);
        Matrix4f previous = rasterNow;

        // The pose half of the motion, which no transform can express. Both frames' vertices are mixes
        // of the same two streams, so the difference between the two blends -- applied to one delta --
        // is the whole of it. A frame that reports no previous transform reports no pose motion either:
        // the two would otherwise disagree about which frame they are measuring from.
        float poseBlend = _poseBlend;
        float previousPoseBlend = _poseBlend;
        if (_objectVelocityPrevious.TryGetValue(key, out ObjectVelocityState prev))
        {
            // A plausibility guard on the pairing. The key is draw order, and draw order can shift when
            // culling admits or drops an occurrence — pairing this draw with some OTHER occurrence's old
            // transform would stamp a huge false velocity and the temporal filter would smear a ghost
            // from it. Nothing this pass serves moves metres in a frame, so a jump that large is a
            // mispair, and the honest fallback is "no motion of its own this frame".
            Vector3f jump = translation - prev.ViewTranslation;
            if (jump.LengthSquared <= 4f)
            {
                Matrix4f currentJitter = firstPerson
                    ? Matrix4f.Identity
                    : Matrix4f.CreateTranslation(
                        _taaSettings.JitterX * 2f / Math.Max(1, _captureWidth),
                        _taaSettings.JitterY * 2f / Math.Max(1, _captureHeight),
                        0f);
                previous = currentJitter * prev.MotionTransform;
                previousPoseBlend = _previousPoseBlend;
            }
        }

        _objectVelocityCurrent[key] = new ObjectVelocityState(motionNow, translation);
        _objectVelocityDraws.Add(
            new Deferred.ObjectVelocityDraw(
                resource,
                indexOffset,
                indexCount,
                rasterNow,
                previous,
                firstPerson,
                poseBlend,
                previousPoseBlend));
    }

    /// <summary>Rolls the object-velocity bookkeeping over to the next frame.</summary>
    private void RollObjectVelocityFrame()
    {
        _objectVelocityPrevious.Clear();
        foreach (KeyValuePair<long, ObjectVelocityState> entry in _objectVelocityCurrent)
        {
            _objectVelocityPrevious[entry.Key] = entry.Value;
        }

        _objectVelocityCurrent.Clear();
        _objectVelocityOccurrence.Clear();
        _objectVelocityDraws.Clear();
    }
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
    private Buffer _boundPreviousVertexBuffer;
    private Buffer _boundIndexBuffer;
    private DescriptorSet _boundDescriptorSet;
    private DescriptorSet _boundSurfaceSet;
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
        _boundPreviousVertexBuffer = default;
        _boundIndexBuffer = default;
        _boundDescriptorSet = default;
        _boundDescriptorLayout = default;
        _boundSurfaceSet = default;
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
        Resolve(mesh).UpdateVertices(vertices, _frameCounter);
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
    public void SubmitTracedMesh(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        in Matrix4f modelView,
        TextureHandle diffuse)
    {
        ThrowIfDisposed();

        if (_worldScene is null)
        {
            return;
        }

        // Everything the drawn path does for the traced world and nothing it does for the picture. The
        // stage and capture checks live in ObserveForRayTracing, which turns this away unless the caller
        // is where a world transform can be composed -- the same restriction a drawn submission is under,
        // for the same reason.
        ObserveForRayTracing(Resolve(mesh), modelView, diffuse, indexOffset, indexCount);
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

        // Tell the traceable world this run is here, with the range it covered: a mesh is not a material,
        // so what the structure is built from is the run rather than the whole mesh. The transform it
        // wants is object to absolute world, which ObserveForRayTracing composes.
        // The first-person rig's opaque, depth-writing draws. Read here for two things: the velocity
        // stamp below, and whether this draw belongs in the traceable world.
        bool firstPersonForward = _firstPersonGeometry
            && _deferredStage == DeferredStage.Forward
            && blend == BlendMode.Opaque
            && _depthState == DepthState.TestAndWrite;

        // The lens the rig is drawn with, kept for the reflections. See _viewModelLens.
        if (firstPersonForward)
        {
            _viewModelLensX = _projection.M11;
            _viewModelLensY = _projection.M22;
        }

        ObserveForRayTracing(resource, modelView, diffuse, indexOffset, indexCount, firstPersonForward);

        // An animated mesh in the G-buffer is a thing that moves under its own power — a soldier, a
        // door — and the camera-derived velocity is wrong for it. The first-person layer needs the same
        // treatment for a different reason: its forward-drawn weapon parts are immutable meshes whose
        // per-frame transforms carry recoil, and the foreground depth path deliberately gives them zero
        // camera velocity. Without this stamp temporal upscaler accumulates the old weapon pose over the new one. Only
        // opaque, depth-writing view-model draws participate; the additive muzzle flame neither owns the
        // stored depth nor wants history.
        // Only a draw that WRITES depth may stamp: the stamp re-rasterises the mesh against the stored
        // depth, and a mesh that never wrote it cannot own it. The sky's cloud layers are animated
        // meshes drawn with depth disabled; stamped, their far-plane depth plus the stamp's bias toward
        // the eye beat every surface beyond some twenty metres, and their near-zero drift was written
        // over the true motion of everything above that depth contour -- the tank, the wire, the far
        // hills. A strafe then reprojected none of it and the whole upper picture ghosted, along a line
        // that followed the terrain, while the near ground below stayed sharp.
        bool animatedGeometry = resource.Animated
            && _deferredStage == DeferredStage.Geometry
            && _depthState == DepthState.TestAndWrite;
        if (_capturing && (animatedGeometry || firstPersonForward))
        {
            RecordObjectVelocity(
                mesh.Id,
                resource,
                indexOffset,
                indexCount,
                modelView,
                firstPerson: firstPersonForward);
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

            ObserveForRayTracing(resource, modelView, diffuse, range.IndexOffset, range.IndexCount);
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
            BindGeometry(mesh, modelView, diffuse, lightmap, blend);
        }
        else if (_capturing
            && _deferredStage is DeferredStage.Forward or DeferredStage.GlassLayer
            && _material.Glass)
        {
            // Glazing, which is the one forward draw that does not go through the world pipeline. It
            // needs the acceleration structure and the lit cards bound beside its textures, and a view
            // position its vertex stage does not pass -- see GlassPipeline for why that is a second
            // pipeline rather than another state of the first.
            BindGlass(mesh, modelView, diffuse, lightmap);
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

        // The same threshold the G-buffer will be asked for, which is what keeps the two tests agreeing.
        BindGraphicsPipeline(_deferred.ShadowPipeline.ForAlphaReference(_alphaReference));
        BindMeshBuffers(mesh);

        // The colour texture, for its alpha alone: a cut-out must cast the shadow of the shape that is
        // there, not of the quad it was cut from.
        BindMeshTextures(_deferred.ShadowPipeline.PipelineLayout, diffuse, TextureHandle.None, split: true);

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
            data[20] = offset.X; data[21] = offset.Y; data[22] = offset.Z;
        }
        else
        {
            for (int i = 16; i < 23; i++)
            {
                data[i] = 0f;
            }
        }

        // A body casts the shadow of the pose it is drawn in, or its shadow walks at the tick rate while
        // it does not. The terrain never animates, so this slot is free in both branches above.
        data[23] = _poseBlend;

        vk.CmdPushConstants(cmd, _deferred.ShadowPipeline.PipelineLayout, ShaderStageFlags.VertexBit, 0, sizeof(float) * 24, data);
    }

    private unsafe void BindGeometry(
        VulkanMesh mesh,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend)
    {
        Vk vk = _instance.Vk;
        CommandBuffer cmd = _frameCommandBuffer;

        BindGraphicsPipeline(_deferred.Geometry.ForState(_depthState, _cullMode, blend, _alphaReference));
        BindMeshBuffers(mesh);
        BindMeshTextures(_deferred.Geometry.PipelineLayout, diffuse, lightmap, split: true);
        BindSurfaceSet();

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
            data[20] = offset.X; data[21] = offset.Y; data[22] = offset.Z;
            data[24] = 0f; data[25] = 0f; data[26] = 0f; data[27] = _alphaHashSeed;
        }
        else
        {
            data[16] = normalMatrix.M11; data[17] = normalMatrix.M21; data[18] = normalMatrix.M31;
            // Three says the lightmap slot is the ground's DETAIL map; four says this draw is the
            // ground's BAKE and belongs in the baked attachment rather than in the colour. Nothing
            // but I.G.I. 2's ground asks for either -- Modulate2X has no other producer in the port.
            data[19] = blend == BlendMode.Modulate2X ? 4f : (_groundDetail ? 3f : 0f);
            data[20] = normalMatrix.M12; data[21] = normalMatrix.M22; data[22] = normalMatrix.M32;
            data[24] = normalMatrix.M13; data[25] = normalMatrix.M23; data[26] = normalMatrix.M33; data[27] = _alphaHashSeed;
        }

        // How much of the previous pose is in the position drawn. The second normal row's fourth
        // component is spare whichever branch wrote it — a terrain page puts its offset in the first
        // three and a rotated mesh a matrix column, and neither has a use for the fourth.
        data[23] = _poseBlend;

        // The pane's roughness is NOT the frame's, and this is where the two part company. A glass
        // texture holds both, and everything of it that reaches the G-buffer is by definition the opaque
        // remainder -- the mullions, the surround, the putty -- because the pane's own texels fail the
        // alpha cut a few lines into the fragment stage and are drawn by the glazing pass instead. A
        // window's authored 0.08 describes the glass; applied to what is left here it makes painted
        // metal read as polished chrome, which is exactly what it did while the pane was being discarded
        // and the frame was all that survived.
        data[28] = _material.Glass ? SurfaceMaterial.Default.Roughness : _material.Roughness;
        data[29] = _material.Metallic;

        // A painted sky is written as emission -- it is not lit, it IS light -- but what it carries is
        // the colour the artist wanted on the screen rather than a radiance. Divided here by what the
        // resolve will multiply back, so it comes out of the tone map at the colour it was painted. The
        // lighting pass reads the strength back out of the G-buffer and applies it.
        data[30] = _paintedSky && _material.Emissive > 0f
            ? _material.Emissive / ResolveExposure()
            : _material.Emissive;
        data[31] = _lightmapScale;
        vk.CmdPushConstants(
            cmd,
            _deferred.Geometry.PipelineLayout,
            ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            0,
            128,
            data);
    }

    /// <summary>Binds the glazing pipeline for one pane draw.</summary>
    /// <param name="mesh">The mesh being drawn.</param>
    /// <param name="modelView">Its model-to-view transform.</param>
    /// <param name="diffuse">The window texture, whose sub-half alpha is the glass.</param>
    /// <param name="lightmap">The baked lightmap, which tints what comes through.</param>
    /// <remarks>
    /// <para>
    /// Set 1 is bound here and written once a frame in <c>BeginPass</c>, so a level with a hundred panes
    /// updates no descriptors at all while it draws them. It is bound on every glass draw regardless,
    /// because the texture set bound between two panes is set 0 under the same layout and binding set 0
    /// does not disturb set 1 — but a draw that followed a world draw would otherwise inherit whatever
    /// that pipeline's layout left in slot 1.
    /// </para>
    /// <para>
    /// The push block is the glazing's own: the transform, and the view-to-world rotation the fragment
    /// stage needs to fire a ray in the space the acceleration structure was built in.
    /// </para>
    /// </remarks>
    private unsafe void BindGlass(
        VulkanMesh mesh,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap)
    {
        Vk vk = _instance.Vk;
        CommandBuffer cmd = _frameCommandBuffer;
        Pipelines.GlassPipeline glass = _deferred.Glass;

        // The FRONT LAYER variant writes depth into a buffer of its own and does not blend; the
        // transmission variant is the reverse of both. Same vertex stage, same descriptor sets, same
        // push block -- only the target and those two states differ.
        bool frontLayer = _deferredStage == DeferredStage.GlassLayer;

        // Counted once, on the layer pass, which sees every pane the transmission pass will.
        if (frontLayer)
        {
            _glassDraws++;
        }

        BindGraphicsPipeline(glass.ForCull(_cullMode, frontLayer));
        BindMeshBuffers(mesh);
        BindMeshTextures(glass.PipelineLayout, diffuse, lightmap);

        DescriptorSet layer = glass.GlassSet(_frameLoop.CurrentFrame);
        vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, glass.PipelineLayout, 1, 1, &layer, 0, null);

        Matrix4f mvp = _projection * modelView;
        float* data = stackalloc float[32];
        mvp.CopyColumnMajorTo(new Span<float>(data, 16));

        // The OBJECT-TO-WORLD rotation, composed and laid out exactly as the G-buffer pass does it: the
        // model-view's own rotation followed by the view-to-world one, pushed a column at a time so the
        // shader can build a mat3 straight from the three rows.
        //
        // The vertex stage is handed a MODEL-space normal. It used to be given the view-to-world rotation
        // instead, which is the wrong matrix for that normal by exactly the model's own orientation -- so
        // every pane reflected the room about the wrong plane and the floor came out at the top of the
        // glass.
        Matrix3f modelViewRotation = new(
            modelView.M11, modelView.M12, modelView.M13,
            modelView.M21, modelView.M22, modelView.M23,
            modelView.M31, modelView.M32, modelView.M33);
        Matrix3f normalMatrix = _viewToWorld * modelViewRotation;

        data[16] = normalMatrix.M11; data[17] = normalMatrix.M21; data[18] = normalMatrix.M31; data[19] = 0f;
        data[20] = normalMatrix.M12; data[21] = normalMatrix.M22; data[22] = normalMatrix.M32; data[23] = 0f;
        data[24] = normalMatrix.M13; data[25] = normalMatrix.M23; data[26] = normalMatrix.M33; data[27] = 0f;
        data[28] = 0f; data[29] = 0f; data[30] = 0f; data[31] = 0f;

        vk.CmdPushConstants(
            cmd,
            glass.PipelineLayout,
            ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            0,
            sizeof(float) * 32,
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

        // A draw reflects when the frame has handed over a cube, which is what SetEnvironmentCube means.
        bool reflective = TryGetEnvironmentCube(out Textures.VulkanCubeTexture? cube);

        BindGraphicsPipeline(pipeline.ForState(_depthState, _cullMode, blend, _alphaReference, reflective));
        BindMeshBuffers(mesh);
        BindMeshTextures(pipeline.PipelineLayout, diffuse, lightmap, split: true);
        BindEnvironmentCube(pipeline, cube);

        // The forward and direct pipelines take the combined model-view-projection plus the lit
        // view-model block; their shader applies the OpenGL-to-Vulkan clip correction.
        Matrix4f mvp = _projection * modelView;
        float* data = stackalloc float[32];
        mvp.CopyColumnMajorTo(new Span<float>(data, 16));

        // What this draw is: the world, the rig, or something translucent — and, for the rig, whether
        // there is a dynamic light to key it with. The studio key is a remaster light and the baked model
        // has none, which is what used to draw the vanilla rig as a black silhouette. See
        // ForwardDrawMarker.
        data[16] = ForwardDrawMarker.For(blend, _firstPersonGeometry, _lighting.Model);
        bool litViewModel = ForwardDrawMarker.IsStudioRig(data[16]);

        // The lit view-model path, for the studio-lit rig only: its lighting is designed in display terms
        // (the retail look), so it is divided by the exposure the resolve will apply — written raw, the
        // eye adaptation of a dim interior multiplied the arms into pure white.
        float exposure = MathF.Max(RenderCVars.EffectiveExposure * _lastAdaptedExposure, 1e-3f);

        // A world draw has no exposure to carry and puts the lightmap scale there instead -- the same
        // scale the G-buffer path pushes in its material row, which this block has no room for
        // otherwise. One unless a title's bakes are MODULATE2X or an HDR bake was encoded with headroom.
        data[17] = litViewModel ? 1f / exposure : _lightmapScale;

        // How much of the previous pose is in the position drawn. This block is the guaranteed 128 bytes
        // to the last float, and the slot it takes used to carry "light this draw with the rig's key",
        // which was the marker above written twice: a lit draw is exactly a first-person opaque one, and
        // that is precisely the marker's own zero. The fragment stage reads the condition off the marker
        // now, which frees this for the one number the vertex stage was missing.
        data[18] = _poseBlend;

        // The lamps standing around the player, added to the rig's key and fill. Without this the arms
        // are lit by the SUN and nothing else: at noon that is right and at night it is a black key over
        // a nearly black ambient, so the weapon stayed a silhouette while the player walked under a lit
        // fitting with the wall behind them brightly lit by it. The rig is a studio rig by design and
        // stays one — this only tells it how much light there is where it is standing.
        Vector3f lamps = litViewModel ? LampLightAtEye() : default;
        float lampFill = (lamps.X * 0.212639f) + (lamps.Y * 0.715169f) + (lamps.Z * 0.072192f);

        // The rig's ambient level; for a world draw, the first component of the model-space direction
        // towards the fixed-function sun, which the vertex stage reads with the two rows' spare w's
        // below and which is zero unless SetVertexSun switched the lighting on for this run of draws.
        data[19] = litViewModel ? _lighting.AmbientColour.Length + lampFill : _vertexSun.X;

        if (_detailFade.Mode > 0f && !litViewModel)
        {
            // Terrain slides each vertex between levels of detail, and the slide is measured along the
            // world axes from the eye, so the vertex stage needs the ramp and where the chunk's origin
            // sits relative to the camera. The rows below carry the rotation for the lit rig's normal
            // and the key colour, and a terrain chunk is never the rig, so they carry these instead;
            // the first row's w -- zero for every other draw below -- is what says so.
            //
            // A chunk is drawn unrotated, so the model-view's rotation is the view's own and its
            // transpose undoes it: the offset is that transpose applied to the model-view's translation,
            // which is exactly what the OpenGL shader computed per vertex as the view position times
            // the model-view's 3x3.
            data[20] = _detailFade.Scale; data[21] = _detailFade.Offset; data[22] = _detailFade.Slope;
            data[23] = _detailFade.Mode;
            data[24] = (modelView.M11 * modelView.M14) + (modelView.M21 * modelView.M24) + (modelView.M31 * modelView.M34);
            data[25] = (modelView.M12 * modelView.M14) + (modelView.M22 * modelView.M24) + (modelView.M32 * modelView.M34);
            data[26] = (modelView.M13 * modelView.M14) + (modelView.M23 * modelView.M24) + (modelView.M33 * modelView.M34);
            data[27] = 0f;
            WriteFog(data, blend);
        }
        else
        {
            // The model-to-view rotation, row by row, each w carrying one channel of the key colour — the
            // level's own sun tint plus whatever the lamps are contributing, so a weapon picks up the mood
            // of the mission it is in and the colour of the light it is standing under. The key is read
            // by the lit rig alone, so every other draw leaves the first w at zero: that is the terrain
            // flag the vertex stage reads, and a sun tint there would morph the world. The other two
            // w's are a world draw's remaining components of the direction towards the fixed-function
            // sun, zero when it is not lit that way.
            data[20] = modelView.M11; data[21] = modelView.M12; data[22] = modelView.M13;
            data[23] = litViewModel ? _lighting.SunColour.X + lamps.X : 0f;

            if (litViewModel)
            {
                data[24] = modelView.M21; data[25] = modelView.M22; data[26] = modelView.M23;
                data[27] = _lighting.SunColour.Y + lamps.Y;
                data[28] = modelView.M31; data[29] = modelView.M32; data[30] = modelView.M33;
                data[31] = _lighting.SunColour.Z + lamps.Z;
            }
            else
            {
                // The second normal row carries the level's flat model light instead. A sub-mesh with a
                // lightmap does not take it -- the original draws that one with Direct3D lighting off and
                // the lightmap in the second stage, and this one with lighting on and the ambient as its
                // only term (sub_49F1C0). White is the identity either way, so the shader multiplies
                // unconditionally and nothing branches.
                Vector3f tint = lightmap.IsValid ? Vector3f.One : _modelAmbient;
                data[24] = tint.X; data[25] = tint.Y; data[26] = tint.Z;
                data[27] = 0f;
                // The third normal row and the key's blue are read by the lit rig and by nothing else —
                // every other draw returns out of the fragment stage before it touches either — so this
                // is where the frame's fog rides in. The rig itself is never fogged, which is the
                // original's arrangement too: it is drawn on the shallow first-person layer, a hand's
                // length from the eye, where the exponential has not begun.
                WriteFog(data, blend);
            }
        }

        vk.CmdPushConstants(
            cmd,
            pipeline.PipelineLayout,
            ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            0,
            sizeof(float) * 32,
            data);
    }

    /// <summary>Writes the frame's fog into the last row of the forward push block.</summary>
    /// <param name="data">The block being filled.</param>
    /// <param name="blend">The draw's blend, which decides what colour it fogs towards.</param>
    /// <remarks>
    /// <para>
    /// The colour follows the blend the way <c>sub_5144E0</c> sets <c>FOGCOLOR</c> per blend mode: an
    /// additive pass fogs to black so that a surface drawn as several passes is fogged once and not once
    /// per pass. The density is per metre of view depth, and zero means the vertex stage leaves the
    /// draw alone.
    /// </para>
    /// <para>
    /// <b>And the fog TARGET arrives here rather than in the pipeline.</b> It was built as a
    /// specialization constant -- id 12, a dimension of the pipeline key, three variants of every
    /// pipeline in the world -- at a time when the fog colour was a constant too. The colour has since
    /// become push data, which is the row this writes, and the target was left behind: the game set it,
    /// the device stored it, the key carried it, and no shader ever declared <c>constant_id = 12</c> to
    /// read it. So I.G.I. 2's ground fogged all three of its passes to the scene's colour, and the
    /// lightmap pass -- which should fade to white, the identity of what it multiplies -- darkened the
    /// far hills instead of letting them go to haze. It costs nothing here: the row is written per draw
    /// already.
    /// </para>
    /// </remarks>
    private unsafe void WriteFog(float* data, BlendMode blend)
    {
        Vector3f colour = _fog.ColourFor(blend, _fogTarget);
        data[28] = colour.X;
        data[29] = colour.Y;
        data[30] = colour.Z;
        data[31] = _fog.DensityPerMetre;
    }

    /// <summary>How much light the level's lamps put on the first-person rig, in the same units as the sun.</summary>
    /// <remarks>
    /// <para>
    /// The lamps already reach this side of the seam in VIEW space — that is how the deferred pass wants
    /// them — and the rig sits at the eye, so the sample point is very nearly the view-space origin. A
    /// hand's length in front of it, because the arms are, and because a fitting directly overhead
    /// otherwise reads as being exactly at the sample point.
    /// </para>
    /// <para>
    /// The falloff is the same windowed inverse square the lighting shader applies, deliberately: the
    /// arms and the wall behind them should agree about how bright the room is, and two different
    /// curves would have the weapon brighten as the wall dimmed. What it does NOT do is trace — the rig
    /// is lit by a two-light studio setup that has never cast a shadow, and a shadow ray per lamp per
    /// frame to shade two hands would cost more than the whole rest of the rig.
    /// </para>
    /// </remarks>
    private Vector3f LampLightAtEye()
    {
        // View space runs +Z into the scene, so this is a little way ahead of the camera.
        const float Ahead = 0.5f;

        Vector3f total = default;
        for (int i = 0; i < _localLightCount; i++)
        {
            DeferredLocalLight lamp = _localLights[i];
            float dx = lamp.ViewPosition.X;
            float dy = lamp.ViewPosition.Y;
            float dz = lamp.ViewPosition.Z - Ahead;
            float distanceSq = (dx * dx) + (dy * dy) + (dz * dz);
            float reach = lamp.Radius;
            if (reach <= 0f || distanceSq >= reach * reach)
            {
                continue;
            }

            float ratio = distanceSq / (reach * reach);
            float window = Math.Clamp(1f - (ratio * ratio), 0f, 1f);
            window *= window;

            // The same soft core the shader uses, for the same reason: the position is a fixture's
            // centroid rather than a point, so standing at one must not divide by nothing.
            float attenuation = window / (distanceSq + 1f);
            total = new Vector3f(
                total.X + (lamp.Colour.X * lamp.Intensity * attenuation),
                total.Y + (lamp.Colour.Y * lamp.Intensity * attenuation),
                total.Z + (lamp.Colour.Z * lamp.Intensity * attenuation));
        }

        return total;
    }

    /// <summary>Binds a mesh's two vertex streams and its indices, skipping what is already bound.</summary>
    /// <remarks>
    /// Two streams, always: the pose the mesh holds now and the pose it held before. An animated body
    /// keeps the two apart and every mesh pipeline blends between them (see
    /// <see cref="SetPoseInterpolation"/>); everything else reports its own buffer as both, which costs
    /// one more binding and nothing else — the delta the shader forms is then zero to the bit.
    /// </remarks>
    private unsafe void BindMeshBuffers(VulkanMesh mesh)
    {
        Vk vk = _instance.Vk;

        // Before the already-bound test below, not after it: a mesh drawn twice in a row skips the
        // rebind, and a slot that went unstamped would read as retired and be overwritten under the
        // frame that is still fetching it.
        mesh.MarkPoseSlotsBound(_frameCounter);

        Buffer vertexBuffer = mesh.VertexBuffer;
        Buffer previousVertexBuffer = mesh.PreviousVertexBuffer;
        Buffer indexBuffer = mesh.IndexBuffer;

        // A terrain page is one mesh holding every chunk batched into it, drawn as a run of ranges, and a
        // model is drawn once per cascade and again for the frame. Both arrive here with the buffers they
        // asked for last time still bound. An animated mesh flips its two streams on every re-skin, so
        // the pair is compared rather than the first of it.
        if (_boundVertexBuffer.Handle != vertexBuffer.Handle
            || _boundPreviousVertexBuffer.Handle != previousVertexBuffer.Handle)
        {
            Buffer* buffers = stackalloc Buffer[2];
            buffers[0] = vertexBuffer;
            buffers[1] = previousVertexBuffer;
            ulong* vertexOffsets = stackalloc ulong[2];
            vertexOffsets[0] = 0;
            vertexOffsets[1] = 0;
            vk.CmdBindVertexBuffers(_frameCommandBuffer, 0, 2, buffers, vertexOffsets);
            _boundVertexBuffer = vertexBuffer;
            _boundPreviousVertexBuffer = previousVertexBuffer;
        }

        if (_boundIndexBuffer.Handle != indexBuffer.Handle)
        {
            vk.CmdBindIndexBuffer(_frameCommandBuffer, indexBuffer, 0, IndexType.Uint16);
            _boundIndexBuffer = indexBuffer;
        }
    }

    /// <summary>Binds the geometry pass's per-frame surface block as set one, unless it is already bound.</summary>
    /// <remarks>
    /// Bound beside the textures rather than once when the pass opens, because a pipeline whose layout
    /// is incompatible for a set unbinds it — and the occlusion queries bind a pipeline of their own in
    /// the middle of the geometry pass. Restating it costs a handle comparison on every draw but one.
    /// </remarks>
    private unsafe void BindSurfaceSet()
    {
        DescriptorSet set = _deferred.SurfaceSet(_frameLoop.CurrentFrame);
        if (_boundSurfaceSet.Handle == set.Handle)
        {
            return;
        }

        _instance.Vk.CmdBindDescriptorSets(
            _frameCommandBuffer,
            PipelineBindPoint.Graphics,
            _deferred.Geometry.PipelineLayout,
            1,
            1,
            &set,
            0,
            null);
        _boundSurfaceSet = set;
    }

    /// <summary>Binds the descriptor set holding this draw's diffuse, lightmap and detail textures.</summary>
    /// <param name="layout">The pipeline layout the set is bound under.</param>
    /// <param name="diffuse">The diffuse texture.</param>
    /// <param name="lightmap">The baked lightmap, or none.</param>
    /// <param name="split">
    /// Whether the pipeline about to be bound reads a sampled image and a sampler apart, which is
    /// what a shader from the library does, rather than one combined object per texture.
    /// </param>
    private unsafe void BindMeshTextures(
        PipelineLayout layout,
        TextureHandle diffuse,
        TextureHandle lightmap,
        bool split = false)
    {
        uint diffuseId = TextureKey(diffuse);
        uint lightmapId = TextureKey(lightmap);

        // The detail map is a property of the diffuse texture rather than of the draw, so it is looked up
        // here and nothing upstream has to carry it. A diffuse with none registered takes the neutral
        // map, whose value is the identity of every term the detail block applies -- which is what lets
        // the shader run the block unconditionally instead of branching on whether one exists.
        uint detailId = _textureDetail.TryGetValue(diffuseId, out uint mapped) && _textures.ContainsKey(mapped)
            ? mapped
            : TextureKey(_neutralDetailTexture);

        MeshTextureDescriptorCache cache = split ? _meshTextureDescriptorsSplit : _meshTextureDescriptors;
        DescriptorSet descriptorSet = cache.Get(
            diffuseId,
            _textures[diffuseId],
            lightmapId,
            _textures[lightmapId],
            detailId,
            _textures[detailId]);

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
    /// <param name="diffuse">Its colour texture, whose alpha decides how the run occludes.</param>
    /// <param name="indexOffset">Where in the index buffer this draw started.</param>
    /// <param name="indexCount">How many indices it covered.</param>
    /// <param name="firstPerson">
    /// Whether this is an opaque, depth-writing draw of the first-person rig. Such a draw is admitted
    /// although it belongs to the forward pass rather than the G-buffer, and is marked so that only a
    /// reflection can find it — see <see cref="RayTracing.VulkanRayTracingInstance.FirstPersonOnly"/>.
    /// </param>
    private void ObserveForRayTracing(
        VulkanMesh mesh,
        in Matrix4f modelView,
        TextureHandle diffuse,
        int indexOffset,
        int indexCount,
        bool firstPerson = false)
    {
        if (_worldScene is null)
        {
            return;
        }

        // What the G-buffer pass draws, and the first-person rig besides.
        //
        // The restriction is about the CAMERA, not the pass: the same mesh goes through the shadow
        // cascades and the card atlas with a camera that is not the frame's, and the transform composed
        // below is the world transform only for the one that is. The forward pass draws with the frame's
        // own camera, so the rig's transform composes correctly there too -- which is why it can be
        // admitted at all, and why the shadow and card passes still cannot.
        //
        // The rig is otherwise exactly what this structure is for: rigid geometry at a transform that
        // changes every frame. The scene's own mover detection picks it up within a few frames and stops
        // it accumulating a placement per frame, which is the same treatment a door or a carried gun gets.
        if (!_capturing || (_deferredStage != DeferredStage.Geometry && !firstPerson))
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
        //
        // "Emits AND is not an occluder", not merely "emits": a lamp emits too, and a lamp is ordinary
        // solid geometry that has to stay in here -- it hangs off a ceiling and shadows the sun exactly
        // as the ceiling does, and a fixture the rays fly straight through is a hole in the level for
        // every traced shadow and every gather ray. The sky is the one surface that is both.
        if (_material.Emissive > 0f && !_material.CastsOcclusion)
        {
            return;
        }

        // What this run is made of, and it is the TEXTURE that decides -- not the material library. A
        // cut-out texture is traced as geometry the ray shader tests the alpha of, so the sun reaches the
        // floor between the wires; anything else is traced as solid.
        //
        // The rule the two shadow systems have to share is this: whatever the rasteriser would cast, the
        // trace casts, and whatever it would punch through, the trace punches through. The rasterised
        // shadow map tests `alpha < 0.5` against this same texture, so measuring the same texture the same
        // way is what makes the two agree.
        //
        // It used to defer to the material library's castsOcclusion flag instead, and that is what made
        // them disagree. The flag is set from a FILENAME -- everything ending _argb8888 -- which marks the
        // textures the packs store with an alpha channel, not the textures that have holes in them.
        // Measured on mission one, 27 of them carry an alpha channel with no cut-out in it at all:
        // window bars, railings, signs, some 1.9 million triangles' worth. Every one of them was dropped
        // from the traceable world while the shadow map went on casting them, which is exactly why the
        // office window's glazing bars had shadows on F6 and none on the trace. The flag existed to stand
        // in for a per-texel test the trace did not have; it has one now, so it no longer gates this. It
        // still gates the ambient occlusion, where the reason it was written -- a depth buffer cannot say
        // where a thin surface ends -- is as true as ever.
        uint textureId = TextureKey(diffuse);
        bool cutout = _textures.TryGetValue(textureId, out VulkanTexture? diffuseTexture)
            && diffuseTexture.HasCutoutAlpha;

        int maskLayer = -1;
        if (cutout && (_opacityMasks is null || !_opacityMasks.TryGetLayer(textureId, out maskLayer)))
        {
            // A cut-out surface with no mask to test against would be traced as a solid board, which is
            // the artefact this whole path exists to remove. Keep it out rather than get it wrong.
            return;
        }

        // The colour a reflection shades this run with when it strikes it somewhere the camera cannot
        // see. Absent is fine and common -- the surface then resolves the way it did before -- so unlike
        // the mask above, a miss here is not a reason to keep the run out of the traceable world.
        // Assigned only on a hit: TryGetValue writes the default on a miss, and nought is a real layer,
        // so taking its out parameter unconditionally would shade every unregistered texture with
        // whatever happens to be in layer zero.
        int albedoLayer = -1;
        if (_tracedAlbedo is null || !_tracedAlbedo.TryGetLayer(textureId, out albedoLayer))
        {
            albedoLayer = -1;
        }

        _worldScene.Observe(
            meshId,
            mesh,
            WorldTransform(modelView),
            new RayTracing.RayTracedSurface(
                indexOffset,
                indexCount,
                !cutout,
                maskLayer,
                albedoLayer,

                // Skinned, so no card can describe it. See RayTracedSurface.Dynamic.
                mesh.Animated),
            firstPerson
                ? RayTracing.VulkanRayTracingInstance.FirstPersonOnly
                : RayTracing.VulkanRayTracingInstance.VisibleToAll);
    }

    /// <inheritdoc/>
    public void SubmitShadowCaster(MeshHandle mesh, in Matrix4f modelView, int indexCount)
    {
        ThrowIfDisposed();

        // Only from the G-buffer pass, for the same reason the observed draws are: the transform below
        // is the world transform only for the frame's own camera.
        if (_worldScene is null || !_capturing || _deferredStage != DeferredStage.Geometry)
        {
            return;
        }

        VulkanMesh resource = Resolve(mesh);
        if (!_meshIds.TryGetValue(resource, out uint meshId))
        {
            return;
        }

        _worldScene.ObserveCaster(meshId, resource, WorldTransform(modelView), indexCount);
    }

    /// <summary>Recovers a draw's absolute world transform from its model-view, for the traced world.</summary>
    private Matrix4f WorldTransform(in Matrix4f modelView)
    {
        Matrix3f modelViewRotation = new(
            modelView.M11, modelView.M12, modelView.M13,
            modelView.M21, modelView.M22, modelView.M23,
            modelView.M31, modelView.M32, modelView.M33);
        Matrix3f normalMatrix = _viewToWorld * modelViewRotation;

        Matrix3f m = _viewToWorld;
        float tx = (m.M11 * modelView.M14) + (m.M12 * modelView.M24) + (m.M13 * modelView.M34);
        float ty = (m.M21 * modelView.M14) + (m.M22 * modelView.M24) + (m.M23 * modelView.M34);
        float tz = (m.M31 * modelView.M14) + (m.M32 * modelView.M24) + (m.M33 * modelView.M34);

        return new Matrix4f(
            normalMatrix.M11, normalMatrix.M12, normalMatrix.M13, tx + _cameraPosition.X,
            normalMatrix.M21, normalMatrix.M22, normalMatrix.M23, ty + _cameraPosition.Y,
            normalMatrix.M31, normalMatrix.M32, normalMatrix.M33, tz + _cameraPosition.Z,
            0f, 0f, 0f, 1f);
    }
}
