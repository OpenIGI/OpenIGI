using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>
/// The resource seams the direct path does not use: the compute seam, volumes, cubes and storage.
/// </summary>
/// <remarks>
/// Every one of these belongs to the remaster's stack. The device reports
/// <see cref="GraphicsCapabilities.SupportsCompute"/> false and
/// <see cref="GraphicsCapabilities.SupportsSceneCapture"/> false, which is what the game asks before it
/// reaches for any of them — so these exist to satisfy the interface, and each refuses rather than
/// pretending, because a handle handed back from here would be one nothing could ever honour.
/// </remarks>
public sealed partial class GraphicsDevice
{
    /// <inheritdoc/>
    public void SetTexture(int unit, TextureHandle texture)
    {
        // Textures reach a draw through its own call on this path; there is no bound-unit state.
    }

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
    /// The cascades themselves are not drawn here — there is no shadow map on this path, and
    /// <c>r_shadows</c> says so. What is kept is the two things that arrive with them and that the
    /// shading cannot work without: how a depth becomes a view position, and how a view direction
    /// becomes a world one.
    /// </remarks>
    public void SetShadows(
        in ShadowSettings settings,
        ReadOnlySpan<ShadowCascade> cascades,
        in DepthUnprojection unprojection,
        Matrix3f viewToWorld)
    {
        _unprojection = unprojection;
        _viewToWorld = viewToWorld;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The resolve itself has not moved onto this path and <c>r_taa</c> says so. The camera position
    /// arrives with it, and the shading needs that to turn a view-space position into a world one.
    /// </remarks>
    public void SetTemporalAntiAliasing(
        in TemporalAaSettings settings,
        in Matrix4f projection,
        Vector3d cameraPosition,
        Matrix3f viewToWorld)
    {
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
    public void SetRealtimeProbeVolume(
        TextureHandle red,
        TextureHandle green,
        TextureHandle blue,
        TextureHandle distance,
        Vector3f origin,
        float spacing,
        int countX,
        int countY,
        int countZ)
    {
    }

    /// <inheritdoc/>
    public TextureHandle CreateStorageTexture(int width, int height, VolumeFormat format, string name) =>
        throw Unsupported(nameof(CreateStorageTexture));

    /// <inheritdoc/>
    public TextureHandle CreateVolumeTexture(int width, int height, int depth, VolumeFormat format, string name) =>
        throw Unsupported(nameof(CreateVolumeTexture));

    /// <inheritdoc/>
    public TextureHandle CreateCubeTexture(int edge, string name) => throw Unsupported(nameof(CreateCubeTexture));

    /// <inheritdoc/>
    public void UpdateCubeFace(TextureHandle cube, int face, ReadOnlySpan<Bgra32> pixels) =>
        throw Unsupported(nameof(UpdateCubeFace));

    /// <inheritdoc/>
    public void UpdateVolumeTexture<T>(TextureHandle texture, ReadOnlySpan<T> data)
        where T : unmanaged => throw Unsupported(nameof(UpdateVolumeTexture));

    /// <inheritdoc/>
    public void ReadVolumeTexture<T>(TextureHandle texture, Span<T> destination)
        where T : unmanaged => throw Unsupported(nameof(ReadVolumeTexture));

    /// <inheritdoc/>
    public void SetVolumeTexture(int unit, TextureHandle texture)
    {
    }

    /// <inheritdoc/>
    public void ReadStorageTexture(TextureHandle texture, Span<Bgra32> destination) => destination.Clear();

    /// <inheritdoc/>
    public ComputeShaderHandle CreateComputeShader(ShaderModuleId shaderModule, ShaderVariant variant, string name) =>
        throw Unsupported(nameof(CreateComputeShader));

    /// <inheritdoc/>
    public void DestroyComputeShader(ComputeShaderHandle shader)
    {
    }

    /// <inheritdoc/>
    public StorageBufferHandle CreateStorageBuffer(int sizeInBytes, string name) =>
        throw Unsupported(nameof(CreateStorageBuffer));

    /// <inheritdoc/>
    public void UpdateStorageBuffer<T>(StorageBufferHandle buffer, ReadOnlySpan<T> data)
        where T : unmanaged => throw Unsupported(nameof(UpdateStorageBuffer));

    /// <inheritdoc/>
    public void ReadStorageBuffer<T>(StorageBufferHandle buffer, Span<T> destination)
        where T : unmanaged => throw Unsupported(nameof(ReadStorageBuffer));

    /// <inheritdoc/>
    public void DestroyStorageBuffer(StorageBufferHandle buffer)
    {
    }

    /// <inheritdoc/>
    public void BindStorageBuffer(int slot, StorageBufferHandle buffer)
    {
    }

    /// <inheritdoc/>
    public void BindImage(int slot, TextureHandle texture, ImageAccess access, int level = 0)
    {
    }

    /// <inheritdoc/>
    public void Dispatch(ComputeShaderHandle shader, int groupsX, int groupsY = 1, int groupsZ = 1) =>
        throw Unsupported(nameof(Dispatch));

    /// <inheritdoc/>
    public void DispatchIndirect(ComputeShaderHandle shader, StorageBufferHandle arguments, int offsetInBytes = 0) =>
        throw Unsupported(nameof(DispatchIndirect));

    /// <inheritdoc/>
    public void MemoryBarrier(MemoryBarrierScope scope)
    {
        // The RHI has no barriers: what a resource is ready for is the implementation's business.
    }

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, float value)
    {
    }

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, int value)
    {
    }

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, in Vector3f value)
    {
    }

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, float x, float y, float z, float w)
    {
    }

    private readonly List<TextureHandle> _staged = [];

    /// <summary>Says which member a caller reached that this device does not offer.</summary>
    /// <remarks>
    /// Named rather than silent, because every one of these is guarded by a capability the device
    /// reports false: reaching one means something asked without asking first, and a quiet no-op there
    /// would be a frame missing a feature nobody could account for.
    /// </remarks>
    private static GraphicsDeviceException Unsupported(string member) => new(
        $"{member} belongs to the remaster's stack, which this device does not offer. It reports "
        + "SupportsSceneCapture and SupportsCompute false; ask before reaching for it.");
}
