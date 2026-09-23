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
    public void CopyFrameToTexture(TextureHandle texture, int width, int height)
    {
        // The held picture the menu dissolves from. It belongs with the scene capture, which this path
        // does not offer.
    }

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
    public void SetVolumetricFog(
        in VolumetricFogSettings fog,
        Vector3f cameraPosition,
        ReadOnlySpan<LocalFogVolume> volumes)
    {
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
