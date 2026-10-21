using OpenIGI.Core.Graphics;

namespace OpenIGI.Rendering;

/// <summary>The textures a device holds: two-dimensional, block-compressed, storage, volume and cube, and how they are sampled.</summary>
/// <remarks>The two-dimensional members are the original's twelve-method texture set. Part of <see cref="IGraphicsDevice"/>.</remarks>
public interface ITextureDevice
{
    /// <summary>The number of faces a cube map has.</summary>
    const int CubeFaceCount = 6;

    /// <summary>Allocates a texture and uploads its pixels. Mirrors <c>AllocTexture</c> + <c>DownloadTexture</c>.</summary>
    /// <param name="description">The texture's size, mip count and sampling.</param>
    /// <param name="pixels">The whole mip chain, largest level first, each level tightly packed.</param>
    /// <returns>A handle to the new texture.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixels"/> does not hold every mip level described.</exception>
    /// <exception cref="GraphicsDeviceException">The texture could not be created.</exception>
    TextureHandle CreateTexture(in TextureDescription description, ReadOnlySpan<Bgra32> pixels);

    /// <summary>Creates a texture from a stored block-compressed chain, uploading it as it is.</summary>
    /// <param name="description">Its size, how many levels it has, and how it is filtered.</param>
    /// <param name="format">The block format.</param>
    /// <param name="chain">Every level's blocks, back to back, largest first.</param>
    /// <param name="proxy">
    /// A small uncompressed copy, for what reads a texture's pixels as it is created -- the traced world's
    /// albedo and cut-out mask -- or empty for none.
    /// </param>
    /// <param name="proxyWidth">The proxy's width.</param>
    /// <param name="proxyHeight">The proxy's height.</param>
    /// <returns>The texture.</returns>
    /// <exception cref="NotSupportedException">The device cannot sample block formats; see <see cref="GraphicsCapabilities.SupportsBlockCompression"/>.</exception>
    TextureHandle CreateCompressedTexture(
        in TextureDescription description,
        BlockFormat format,
        ReadOnlySpan<byte> chain,
        ReadOnlySpan<Bgra32> proxy,
        int proxyWidth,
        int proxyHeight) =>
        throw new NotSupportedException("This device does not create block-compressed textures.");

    /// <summary>Replaces the pixels of one mip level. Mirrors <c>ReloadTexture</c>.</summary>
    /// <param name="texture">The texture to update.</param>
    /// <param name="mipLevel">The zero-based mip level.</param>
    /// <param name="pixels">The replacement pixels for that level, tightly packed.</param>
    /// <exception cref="ArgumentException">The handle is unknown, or the pixel count does not match the level.</exception>
    void UpdateTexture(TextureHandle texture, int mipLevel, ReadOnlySpan<Bgra32> pixels);

    /// <summary>Releases a texture. Mirrors <c>DeAllocTexture</c>.</summary>
    /// <param name="texture">The texture to release. <see cref="TextureHandle.None"/> is ignored.</param>
    void DestroyTexture(TextureHandle texture);

    /// <summary>Binds a texture to a sampling unit. Mirrors <c>SetActiveTMU</c> followed by the D3D <c>SetTexture</c>.</summary>
    /// <param name="unit">The zero-based texture unit.</param>
    /// <param name="texture">The texture to bind, or <see cref="TextureHandle.None"/> to unbind.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="unit"/> is outside <see cref="GraphicsCapabilities.TextureUnits"/>.</exception>
    void SetTexture(int unit, TextureHandle texture);

    /// <summary>Sets how textures are sampled.</summary>
    /// <param name="maxAnisotropy">
    /// The anisotropic filtering ratio, 1 for none. A ground plane seen at a grazing angle is the
    /// worst case in the frame for trilinear filtering: the footprint of a pixel is a long thin
    /// sliver, mipmapping has to pick a level for its <em>widest</em> axis, and everything along the
    /// other axis is thrown away. That is what turns ground into a smear towards the horizon.
    /// </param>
    /// <remarks>
    /// Applies to textures created afterwards, and re-applies the sampling to those already uploaded.
    /// </remarks>
    void SetTextureQuality(int maxAnisotropy);

    /// <summary>Reads a two-dimensional texture back into memory.</summary>
    /// <param name="texture">The texture to read.</param>
    /// <param name="destination">Receives the texels, row-major and top row first.</param>
    /// <remarks>
    /// For looking at what a compute pass produced. Synchronising, so it belongs at load or behind a
    /// developer switch. Float formats are tone-mapped into eight bits on the way out, because the
    /// point is to see the shape of the result rather than to recover its values.
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">The handle is not a texture, or the span is too small.</exception>
    void ReadStorageTexture(TextureHandle texture, Span<Bgra32> destination);

    /// <summary>Allocates a two-dimensional texture a compute pass can write through an image.</summary>
    /// <param name="width">The width in texels.</param>
    /// <param name="height">The height in texels.</param>
    /// <param name="format">What one texel holds.</param>
    /// <param name="name">A name for the texture, used only in diagnostics.</param>
    /// <returns>A handle to the texture, its contents undefined until written.</returns>
    /// <remarks>
    /// Distinct from <see cref="CreateTexture"/>, which uploads eight-bit colour from memory and builds
    /// a mip chain. This allocates storage of a chosen precision with no mips and nothing in it, for a
    /// pass that will fill it on the GPU — the surface cache's lighting, which is radiance rather than
    /// colour and is not bounded by one.
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">Compute is unsupported, or a dimension is not positive.</exception>
    TextureHandle CreateStorageTexture(int width, int height, VolumeFormat format, string name);

    /// <summary>Allocates a three-dimensional texture.</summary>
    /// <param name="width">Voxels along x.</param>
    /// <param name="height">Voxels along y.</param>
    /// <param name="depth">Voxels along z.</param>
    /// <param name="format">What one voxel holds.</param>
    /// <param name="name">A name for the texture, used only in diagnostics.</param>
    /// <param name="shaderWritten">
    /// Whether a compute pass writes the volume. False for one that is only ever uploaded and then
    /// sampled, which a backend may then store in its own format rather than a storage one: WGSL has no
    /// storage form of a one-byte texel, and a volume allocated for storage there is sixteen bytes a
    /// voxel and widened on the way in.
    /// </param>
    /// <returns>A handle to the texture, its contents undefined until written.</returns>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="CreateTexture"/> rather than an overload of it, because almost nothing
    /// about the two is shared: a volume has no mip chain, no anisotropy, and is as likely to be written
    /// by a compute pass as uploaded from memory.
    /// </para>
    /// <para>
    /// Filtered linearly and clamped at every edge. Linear because the hardware's trilinear filter is
    /// exactly the blend between eight neighbouring voxels that a field lookup wants, and it is free;
    /// clamped because a volume does not tile — the space past its edge is more of the same emptiness
    /// rather than the far side of the level.
    /// </para>
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">A dimension is not positive, or the device refused it.</exception>
    TextureHandle CreateVolumeTexture(
        int width,
        int height,
        int depth,
        VolumeFormat format,
        string name,
        bool shaderWritten = true);

    /// <summary>Replaces a volume texture's contents.</summary>
    /// <typeparam name="T">The element type. Must match the texture's format.</typeparam>
    /// <param name="texture">The volume to write.</param>
    /// <param name="data">The voxels, x fastest then y then z.</param>
    /// <exception cref="GraphicsDeviceException">The handle is not a volume of this device, or the data is the wrong size.</exception>
    void UpdateVolumeTexture<T>(TextureHandle texture, ReadOnlySpan<T> data)
        where T : unmanaged;

    /// <summary>Reads a volume texture back into memory.</summary>
    /// <typeparam name="T">The element type. Must match the texture's format.</typeparam>
    /// <param name="texture">The volume to read.</param>
    /// <param name="destination">Receives the voxels, x fastest then y then z.</param>
    /// <remarks>
    /// <b>Synchronising, and therefore for diagnostics rather than for a frame.</b> It exists because a
    /// volume built by a compute pass cannot be judged by looking at it — a wrong distance field
    /// produces a picture that is merely subtly wrong, several stages downstream. Reading it back and
    /// comparing against an answer computed another way is the only check that distinguishes a correct
    /// volume from a plausible one.
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">The handle is not a volume, or the span is the wrong size.</exception>
    void ReadVolumeTexture<T>(TextureHandle texture, Span<T> destination)
        where T : unmanaged;

    /// <summary>Binds a volume texture to a sampling unit.</summary>
    /// <param name="unit">The texture unit.</param>
    /// <param name="texture">The volume, or the null handle to unbind.</param>
    /// <remarks>
    /// Separate from <see cref="SetTexture"/> because the two bind different targets to the same unit,
    /// and a unit holding both is a unit whose meaning depends on which sampler reads it.
    /// </remarks>
    void SetVolumeTexture(int unit, TextureHandle texture);

    /// <summary>Creates an empty cube map.</summary>
    /// <param name="edge">The edge length of one face, in texels.</param>
    /// <param name="name">A name for the texture, used only in diagnostics.</param>
    /// <returns>A handle to the cube, its faces undefined until written.</returns>
    /// <exception cref="GraphicsDeviceException">The edge is not positive, or the device refused it.</exception>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="CreateTexture"/> for the reason <see cref="CreateVolumeTexture"/> is:
    /// the two bind different targets, and a unit holding both is a unit whose meaning depends on which
    /// sampler reads it.
    /// </para>
    /// <para>
    /// Square faces and no mip chain, which is what the game's own bakes are — I.G.I. 2 writes one
    /// <c>A8R8G8B8</c> single-mip cube at face zero's width and loads the six files into it in order
    /// (<c>Envmap_LoadFromMemory</c>, <c>igi2ed 0x581670</c>). Filtered linearly and clamped at every
    /// edge, so the seams between faces blend rather than repeat.
    /// </para>
    /// </remarks>
    TextureHandle CreateCubeTexture(int edge, string name);

    /// <summary>Replaces one face of a cube map.</summary>
    /// <param name="cube">The cube to write.</param>
    /// <param name="face">Which face, <c>0</c> to <c>5</c>.</param>
    /// <param name="pixels">The face's texels, row by row, <c>edge × edge</c> of them.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="face"/> is not a face.</exception>
    /// <exception cref="GraphicsDeviceException">The handle is not a cube of this device, or the span is the wrong size.</exception>
    /// <remarks>
    /// The face order is the one every cube map API agrees on and the one the game's files are numbered
    /// in: <c>+X, −X, +Y, −Y, +Z, −Z</c>, in the <em>sampler's</em> axes. Those are not the engine's —
    /// I.G.I. 2 bakes its faces in engine axes and the water shader swizzles <c>.xzy</c> on the way to
    /// the lookup, which exchanges Y and Z and lines the two up. A caller holding engine-axis faces
    /// therefore has a mapping to do, and it belongs on the caller's side of this seam.
    /// </remarks>
    void UpdateCubeFace(TextureHandle cube, int face, ReadOnlySpan<Bgra32> pixels);

    /// <summary>Sets the mip bias every mipmapped texture is sampled with.</summary>
    /// <param name="bias">The bias, in mip levels; negative is sharper.</param>
    /// <remarks>
    /// Changing it re-resolves the samplers of textures already loaded, so it takes effect on the next frame
    /// rather than the next level. Cheap when it has not changed.
    /// </remarks>
    void SetTextureLodBias(float bias);
}
