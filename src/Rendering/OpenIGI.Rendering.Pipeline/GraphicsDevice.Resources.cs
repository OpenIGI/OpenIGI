using System.Runtime.InteropServices;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.Meshes;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>
/// The resource seams that are not the frame's own: blits, the frame copy, occlusion queries, outlines
/// and the temporal settings.
/// </summary>
public sealed partial class GraphicsDevice
{
    /// <inheritdoc/>
    public void BlitPixels(int x, int y, int width, int height, ReadOnlySpan<Bgra32> pixels)
    {
        ThrowIfDisposed();

        if (pixels.IsEmpty || width <= 0 || height <= 0)
        {
            return;
        }

        // Uploaded as a texture and drawn as a quad, because neither backend can write the back buffer
        // directly and the sprite path already knows how to put a texture on the screen.
        TextureHandle staged = _textures.Create(new TextureDescription(width, height), pixels, "blit");

        try
        {
            _sprites.SetProjection(CurrentMode.Width, CurrentMode.Height);

            Bgra32 white = new(255, 255, 255, 255);

            Span<Vertex2D> quad =
            [
                new(x, y, 0f, 0f, white),
                new(x + width, y, 1f, 0f, white),
                new(x + width, y + height, 1f, 1f, white),
                new(x, y + height, 0f, 1f, white),
            ];

            _sprites.DrawTriangles(Pass(), quad, [0, 1, 2, 0, 2, 3], staged, BlendMode.Opaque);
        }
        finally
        {
            // Kept until the frame it was drawn in has gone out, which SwapBuffers guarantees by then.
            _staged.Add(staged);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The held picture the menu dissolves from, copied inside the frame: whatever has drawn so far -- the
    /// outgoing level, resolved, before the interface goes over it -- is copied out, and the frame's pass
    /// reopens on top of it so nothing drawn is lost and the draws that follow can sample the copy.
    /// </para>
    /// <para>
    /// The texture takes the frame's channel order, in its sRGB form. A copy moves bytes and cannot
    /// swizzle, and the bytes in the frame are already encoded -- by the shaders into an unsigned-normal
    /// frame, by the hardware into an sRGB one -- so sampling them through an sRGB view decodes the same
    /// light either way. A browser canvas is red-first where the desktop is blue-first, so the texture is
    /// remade in the frame's order the first time the two differ.
    /// </para>
    /// <para>
    /// Between frames there is nothing this device may copy: the last frame's image belongs to the
    /// presentation engine once it has gone out. Nothing asks there -- the menu holds its picture
    /// mid-frame -- so it is left as it was.
    /// </para>
    /// </remarks>
    public void CopyFrameToTexture(TextureHandle texture, int width, int height)
    {
        ThrowIfDisposed();

        if (_textures.Find(texture) is null)
        {
            throw new ArgumentException($"Unknown texture {texture}.", nameof(texture));
        }

        if (!_inFrame || width <= 0 || height <= 0)
        {
            return;
        }

        // Opened and closed, so a clear still pending lands before the copy reads the image.
        Pass();
        ClosePass();

        IRhiTexture frameColour = FrameColourSource;
        IRhiTexture destination = _textures.Reformat(texture, FrameCopyFormat(frameColour.Format))!;
        int columns = Math.Min(width, Math.Min(destination.Width, CurrentMode.Width));
        int rows = Math.Min(height, Math.Min(destination.Height, CurrentMode.Height));

        Encoder().CopyTextureToTexture(
            new RhiTextureRegion(frameColour),
            new RhiTextureRegion(destination),
            new RhiExtent3D(columns, rows));
    }

    /// <summary>The format a texture holding a copy of the frame is made in.</summary>
    /// <param name="frame">The frame's format.</param>
    /// <returns>The frame's channel order, sRGB.</returns>
    internal static RhiTextureFormat FrameCopyFormat(RhiTextureFormat frame) =>
        frame is RhiTextureFormat.Rgba8Unorm or RhiTextureFormat.Rgba8UnormSrgb
            ? RhiTextureFormat.Rgba8UnormSrgb
            : RhiTextureFormat.Bgra8UnormSrgb;

    /// <inheritdoc/>
    public OcclusionQueryHandle CreateOcclusionQuery()
    {
        ThrowIfDisposed();
        return _occlusion.Create();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Visible until proven hidden: a query nothing has answered yet -- the direct path draws no
    /// geometry pass to probe in -- keeps its object drawn.
    /// </remarks>
    public bool OcclusionVisible(OcclusionQueryHandle query)
    {
        ThrowIfDisposed();
        return _occlusion.Visible(query);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A box is drawn against the geometry pass's depth, so a query is only probed while that pass is
    /// open. Anywhere else the object is left as it was rather than probed. The box takes the projection the
    /// geometry is drawn with, which on this path already is the target's clip space.
    /// </remarks>
    public void IssueOcclusionQuery(OcclusionQueryHandle query, in Matrix4f modelView, in OcclusionBounds bounds)
    {
        ThrowIfDisposed();

        if (!_capturing || CurrentPass is not ScenePass.Geometry || _scenePass is not { } pass)
        {
            return;
        }

        _occlusion.Issue(pass, query, _projection * modelView, bounds);
    }

    /// <inheritdoc/>
    public void DestroyOcclusionQuery(OcclusionQueryHandle query)
    {
        ThrowIfDisposed();
        _occlusion.Destroy(query);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The selection glow, drawn into whichever pass is open: the forward pass over the lit image when a
    /// scene is being captured, the frame itself on the direct path. Anywhere else -- the geometry pass, a
    /// card capture -- there is nothing to mark and the call does nothing.
    /// </para>
    /// <para>
    /// A mask lays the model's silhouette into depth just inside the near plane; then a stack of shells,
    /// widest and faintest first, draws the model pushed outwards in screen space, each asked only for the
    /// opacity the rings outside it have not already given, so the stack adds up to a falloff rather than
    /// a band.
    /// </para>
    /// </remarks>
    public void DrawOutline(
        MeshHandle mesh,
        ReadOnlySpan<MeshDrawRange> ranges,
        in Matrix4f modelView,
        Bgra32 colour,
        float radius,
        bool throughWalls)
    {
        ThrowIfDisposed();

        RhiMesh resource = Resolve(mesh);

        if (ranges.IsEmpty || radius <= 0f || colour.A == 0 || CapturingCards)
        {
            return;
        }

        foreach (MeshDrawRange range in ranges)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(range.IndexOffset);
            ArgumentOutOfRangeException.ThrowIfNegative(range.IndexCount);

            if (range.IndexOffset + range.IndexCount > resource.IndexCapacity)
            {
                throw new ArgumentException(
                    $"Indices {range.IndexOffset}..{range.IndexOffset + range.IndexCount} lie outside the mesh.",
                    nameof(ranges));
            }
        }

        IRhiRenderPassEncoder pass;
        RhiTextureFormat colourFormat;
        RhiTextureFormat depthFormat;
        float viewportWidth;
        float viewportHeight;

        if (_capturing)
        {
            if (CurrentPass is not ScenePass.Forward || _scenePass is not { } forward || _scene is not { } scene)
            {
                return;
            }

            pass = forward;
            colourFormat = SceneTargetSet.FormatOf(SceneTargetId.Lit);
            depthFormat = SceneTargetSet.FormatOf(SceneTargetId.Depth);

            // Measured against the scene's own size, under a render scale, so a rim is the pixels it says.
            viewportWidth = scene.Width;
            viewportHeight = scene.Height;
        }
        else
        {
            pass = Pass();
            colourFormat = PresentFormat;
            depthFormat = DepthFormat;
            viewportWidth = _viewport.Width > 0 ? _viewport.Width : CurrentMode.Width;
            viewportHeight = _viewport.Height > 0 ? _viewport.Height : CurrentMode.Height;
        }

        viewportWidth = MathF.Max(1f, viewportWidth);
        viewportHeight = MathF.Max(1f, viewportHeight);

        // A fixed-pixel rim, capped against the subject's own scale -- a third of a metre at its distance --
        // so a soldier sixty metres off is outlined rather than drowned in a lozenge of glow.
        float viewDistance = MathF.Max(MathF.Abs(modelView.M34), 0.1f);
        float metrePixels = MathF.Abs(_projection.M22) * viewportHeight * 0.5f / viewDistance;
        float width = MathF.Min(radius, MathF.Max(metrePixels * 0.35f, 1f));

        // Written into the scene-referred lit image, which the resolve then exposes: divided by that
        // exposure so the marker lands at the colour it was asked for, as the lit rig does. One on the
        // direct path, whose target already means what it says.
        float exposure = SceneExposure;

        Span<float> push = stackalloc float[OutlinePipelineCache.PushDataBytes / sizeof(float)];
        (_projection * modelView).CopyColumnMajorTo(push[..16]);
        push[24] = viewportWidth;
        push[25] = viewportHeight;

        OutlinePipelineCache pipelines = _outlines ??= new OutlinePipelineCache(_device);

        pass.SetVertexBuffer(0, resource.VertexBuffer);
        pass.SetVertexBuffer(1, resource.PreviousVertexBuffer);
        pass.SetIndexBuffer(resource.IndexBuffer, RhiIndexFormat.Uint16);
        resource.MarkPoseSlotsBound(_frameCounter);

        // The mask: the model where it really is, pinned just inside the near plane.
        pass.SetPipeline(pipelines.For(colourFormat, depthFormat, OutlinePass.Mask));
        push[21] = _poseBlend;
        push[22] = 1f;
        push[23] = OutlineMaskDepth;
        pass.SetPushData(MemoryMarshal.AsBytes(push));
        DrawOutlineRanges(pass, ranges);

        // Cull whichever facing the frame is KEEPING, so each shell adds one layer and not two.
        OutlinePass shell = _cullMode == CullMode.Clockwise ? OutlinePass.ShellCullBack : OutlinePass.ShellCullFront;
        pass.SetPipeline(pipelines.For(colourFormat, depthFormat, shell));

        // Pinned, the shells clear any wall in the way; at their own depth the world occludes them.
        push[22] = throughWalls ? 1f : 0f;
        push[23] = OutlineGlowDepth;

        float covered = 0f;

        for (int ring = GlowShells; ring >= 1; ring--)
        {
            float fraction = ring / (float)GlowShells;
            float wanted = MathF.Pow(1f - fraction + (1f / GlowShells), GlowFalloff);
            float step = Math.Clamp((wanted - covered) / MathF.Max(1e-3f, 1f - covered), 0f, 1f);
            covered = wanted;

            push[16] = colour.R / 255f / exposure;
            push[17] = colour.G / 255f / exposure;
            push[18] = colour.B / 255f / exposure;
            push[19] = step * (colour.A / 255f);
            push[20] = width * fraction;

            pass.SetPushData(MemoryMarshal.AsBytes(push));
            DrawOutlineRanges(pass, ranges);
        }
    }

    /// <summary>How many shells the glow is built from.</summary>
    private const int GlowShells = 8;

    /// <summary>How sharply the glow fades from the silhouette outwards; above one it hugs the model.</summary>
    private const float GlowFalloff = 2.5f;

    /// <summary>The depth the mask is pinned to: just inside the near plane, never on it.</summary>
    private const float OutlineMaskDepth = 1f - 1e-5f;

    /// <summary>The depth a see-through shell is pinned to: a hair behind the mask, so it loses to it.</summary>
    private const float OutlineGlowDepth = 1f - 1e-4f;

    private OutlinePipelineCache? _outlines;

    private static void DrawOutlineRanges(IRhiRenderPassEncoder pass, ReadOnlySpan<MeshDrawRange> ranges)
    {
        foreach (MeshDrawRange range in ranges)
        {
            if (range.IndexCount > 0)
            {
                pass.DrawIndexed(range.IndexCount, 1, range.IndexOffset);
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Kept for the temporal resolve, which measures this frame against the last, and for the shading,
    /// which needs the camera to turn a view-space position into a world one. The position is kept in
    /// double precision as well, because the camera's motion is the difference of two large numbers.
    /// </remarks>
    public void SetTemporalAntiAliasing(
        in TemporalAaSettings settings,
        in Matrix4f projection,
        Vector3d cameraPosition,
        Matrix3f viewToWorld)
    {
        _taa = settings;
        _temporalProjection = projection;
        _cameraPositionPrecise = cameraPosition;
        _cameraPosition = new Vector3f(
            (float)cameraPosition.X,
            (float)cameraPosition.Y,
            (float)cameraPosition.Z);

        _viewToWorld = viewToWorld;
    }

    private readonly List<TextureHandle> _staged = [];
}
