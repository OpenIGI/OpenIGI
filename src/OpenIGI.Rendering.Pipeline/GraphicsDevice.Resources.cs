using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>
/// The resource seams that are not the frame's own: blits, occlusion queries, outlines, and the members
/// of the remaster's stack this device has not taken over — cubes, the fog volume, the probe field.
/// </summary>
/// <remarks>
/// What has not moved over refuses rather than pretending, because a handle handed back from here would
/// be one nothing could ever honour.
/// </remarks>
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
    /// The held picture the menu dissolves from, copied inside the frame as the Vulkan renderer copies
    /// it: whatever has drawn so far -- the outgoing level, resolved, before the interface goes over it --
    /// is copied out, and the frame's pass reopens on top of it so nothing drawn is lost and the draws
    /// that follow can sample the copy.
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

        IRhiTexture destination = _textures.Reformat(texture, FrameCopyFormat(PresentFormat))!;
        int columns = Math.Min(width, Math.Min(destination.Width, CurrentMode.Width));
        int rows = Math.Min(height, Math.Min(destination.Height, CurrentMode.Height));

        Encoder().CopyTextureToTexture(
            new RhiTextureRegion(_frame.Target.Texture),
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
    public OcclusionQueryHandle CreateOcclusionQuery() => new(_nextQuery++);

    /// <inheritdoc/>
    /// <remarks>
    /// True, always. A query nothing answers must read as visible: the caller draws what a query says
    /// is visible, so a false would hide geometry that is there, while a true costs a draw that would
    /// have happened anyway.
    /// </remarks>
    public bool OcclusionVisible(OcclusionQueryHandle query) => true;

    /// <inheritdoc/>
    public void IssueOcclusionQuery(OcclusionQueryHandle query, in Matrix4f modelView, in OcclusionBounds bounds)
    {
    }

    /// <inheritdoc/>
    public void DestroyOcclusionQuery(OcclusionQueryHandle query)
    {
    }

    /// <inheritdoc/>
    public void DrawOutline(
        MeshHandle mesh,
        ReadOnlySpan<MeshDrawRange> ranges,
        in Matrix4f modelView,
        Bgra32 colour,
        float radius,
        bool throughWalls)
    {
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
        _cameraPositionPrecise = cameraPosition;
        _cameraPosition = new Vector3f(
            (float)cameraPosition.X,
            (float)cameraPosition.Y,
            (float)cameraPosition.Z);

        _viewToWorld = viewToWorld;
    }

    /// <inheritdoc/>
    public TextureHandle CreateCubeTexture(int edge, string name) => throw Unsupported(nameof(CreateCubeTexture));

    /// <inheritdoc/>
    public void UpdateCubeFace(TextureHandle cube, int face, ReadOnlySpan<Bgra32> pixels) =>
        throw Unsupported(nameof(UpdateCubeFace));

    private readonly List<TextureHandle> _staged = [];

    /// <summary>Says which member a caller reached that this device does not offer.</summary>
    /// <remarks>
    /// Named rather than silent, because every one of these is guarded by a capability the device
    /// reports false: reaching one means something asked without asking first, and a quiet no-op there
    /// would be a frame missing a feature nobody could account for.
    /// </remarks>
    private static GraphicsDeviceException Unsupported(string member) => new(
        $"{member} belongs to a part of the remaster's stack this device has not taken over yet.");
}
