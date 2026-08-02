using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>
/// The renderer seam: everything the engine may ask of a graphics backend.
/// </summary>
/// <remarks>
/// <para>
/// The member names deliberately mirror the original's <c>RenderMode</c> method names so the mapping
/// stays checkable — <see cref="SetMode"/> is <c>DisplaySetMode</c>, <see cref="SwapBuffers"/> is
/// <c>DisplaySwapBuffers</c>, <see cref="SetViewport"/> is <c>ClipWindowPrimitive</c>,
/// <see cref="BlitPixels"/> is <c>BlitPrimitive</c>, and the texture members are the twelve-method
/// texture set. See the original and §8.
/// </para>
/// <para>
/// <b>Frame order.</b> The original presents at the <em>start</em> of <c>DisplaySwapBuffers</c> and
/// begins the new scene at its end, so a frame is <c>SwapBuffers</c> → <c>Clear</c> → draw. Backends
/// must keep that order: it is what makes the one-render-per-tick interleave in
/// the original land on the same frame boundaries.
/// </para>
/// <para>
/// <b>Threading.</b> Every member must be called from the thread that created the device.
/// </para>
/// </remarks>
public interface IGraphicsDevice : IDisposable
{
    /// <summary>Occurs after the device has recreated resources following a device loss.</summary>
    /// <remarks>
    /// The original handles this inline in <c>DisplaySwapBuffers</c>: it evicts managed textures,
    /// restores all surfaces and re-applies render state. Backends raise this once recovery has
    /// completed so that callers holding cached device state can re-apply it.
    /// </remarks>
    event EventHandler? DeviceRestored;

    /// <summary>Gets what this device supports. Mirrors <c>GetGFXCaps</c>.</summary>
    GraphicsCapabilities Capabilities { get; }

    /// <summary>Gets the mode the device is currently set to.</summary>
    DisplayMode CurrentMode { get; }

    /// <summary>Gets or sets a value indicating whether presentation waits for the vertical blank. Mirrors <c>DisplayVSync</c>.</summary>
    bool VSyncEnabled { get; set; }

    /// <summary>Enumerates the display modes the adapter reports. Mirrors the <c>Enum</c> method.</summary>
    /// <returns>The available modes. May be empty when the backend cannot enumerate, which is not an error.</returns>
    IReadOnlyList<DisplayMode> EnumerateDisplayModes();

    /// <summary>Sets the back-buffer size and colour depth. Mirrors <c>DisplaySetMode</c>.</summary>
    /// <param name="mode">The mode to apply.</param>
    /// <exception cref="GraphicsDeviceException">The mode could not be applied.</exception>
    void SetMode(in DisplayMode mode);

    /// <summary>Releases the current mode's resources without destroying the device. Mirrors <c>DisplayUnsetMode</c>.</summary>
    void UnsetMode();

    /// <summary>Recreates resources lost to a device reset. Mirrors <c>DisplayRestore</c>.</summary>
    /// <remarks>Backends that cannot lose their device may implement this as a no-op.</remarks>
    void Restore();

    /// <summary>Clears the colour and depth buffers. Mirrors <c>DisplayClear</c>.</summary>
    /// <param name="color">The colour to fill with. The alpha channel is ignored, as it is in the original.</param>
    /// <remarks>The original always clears colour and depth together and always clears depth to <c>1.0</c>.</remarks>
    void Clear(Bgra32 color);

    /// <summary>Presents the back buffer and opens the next scene. Mirrors <c>DisplaySwapBuffers</c>.</summary>
    /// <remarks>See the frame-order note on <see cref="IGraphicsDevice"/>: this presents first, then begins.</remarks>
    void SwapBuffers();

    /// <summary>Restricts rendering to a sub-rectangle of the render target. Mirrors <c>ClipWindowPrimitive</c>.</summary>
    /// <param name="viewport">The region and depth range.</param>
    void SetViewport(in Viewport viewport);

    /// <summary>Copies a CPU pixel buffer straight to the render target. Mirrors <c>BlitPrimitive</c>.</summary>
    /// <param name="x">The destination left edge in pixels.</param>
    /// <param name="y">The destination top edge in pixels.</param>
    /// <param name="width">The source width in pixels.</param>
    /// <param name="height">The source height in pixels.</param>
    /// <param name="pixels">The source pixels, row-major, <paramref name="width"/> per row.</param>
    /// <exception cref="ArgumentException"><paramref name="pixels"/> is shorter than <paramref name="width"/> × <paramref name="height"/>.</exception>
    /// <remarks>Used for splash screens and movie frames, where the source is produced on the CPU.</remarks>
    void BlitPixels(int x, int y, int width, int height, ReadOnlySpan<Bgra32> pixels);

    /// <summary>Reads the render target back into a pixel buffer. Mirrors <c>SnapshotPrimitive</c>.</summary>
    /// <param name="width">The width to read in pixels.</param>
    /// <param name="height">The height to read in pixels.</param>
    /// <param name="destination">Receives the pixels, row-major and top row first.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than <paramref name="width"/> × <paramref name="height"/>.</exception>
    /// <remarks>
    /// Rows arrive top-first regardless of what the backend's own framebuffer orientation is, so a
    /// capture can be written straight out as an image.
    /// </remarks>
    void CaptureFrame(int width, int height, Span<Bgra32> destination);

    /// <summary>Allocates a texture and uploads its pixels. Mirrors <c>AllocTexture</c> + <c>DownloadTexture</c>.</summary>
    /// <param name="description">The texture's size, mip count and sampling.</param>
    /// <param name="pixels">The whole mip chain, largest level first, each level tightly packed.</param>
    /// <returns>A handle to the new texture.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixels"/> does not hold every mip level described.</exception>
    /// <exception cref="GraphicsDeviceException">The texture could not be created.</exception>
    TextureHandle CreateTexture(in TextureDescription description, ReadOnlySpan<Bgra32> pixels);

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

    /// <summary>Draws indexed screen-space triangles.</summary>
    /// <param name="vertices">The vertex buffer.</param>
    /// <param name="indices">Indices into <paramref name="vertices"/>, three per triangle.</param>
    /// <param name="texture">The texture to sample, or <see cref="TextureHandle.None"/> for untextured geometry.</param>
    /// <param name="blend">How fragments combine with the render target.</param>
    /// <exception cref="ArgumentException"><paramref name="indices"/> is not a multiple of three, or an index is out of range.</exception>
    /// <remarks>
    /// This is the primitive every 2D path funnels through: <see cref="SpriteBatch"/> builds the two
    /// buffers and calls this once per texture and blend-mode run.
    /// </remarks>
    void DrawTriangles2D(
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices,
        TextureHandle texture,
        BlendMode blend);

    /// <summary>Sets the pixel size of the coordinate system <see cref="Vertex2D"/> positions are expressed in.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <remarks>
    /// The original's UI is authored against the back-buffer resolution and needs no projection of its
    /// own; this is the equivalent of that implicit mapping, made explicit so that a UI authored at
    /// 640×480 can be presented at any window size.
    /// </remarks>
    void Set2DProjection(int width, int height);

    /// <summary>Uploads a mesh and returns a handle to it.</summary>
    /// <param name="vertices">The vertices.</param>
    /// <param name="indices">Indices into <paramref name="vertices"/>, three per triangle.</param>
    /// <returns>A handle to the new mesh.</returns>
    /// <exception cref="ArgumentException"><paramref name="indices"/> is not a multiple of three, or an index is out of range.</exception>
    /// <exception cref="GraphicsDeviceException">The mesh could not be created.</exception>
    /// <remarks>
    /// Meshes are immutable once uploaded. The original packs many models into a shared vertex buffer
    /// and flushes it when the running vertex count would pass 65535 (<c>0x49B430</c>/<c>0x49B4B0</c>) —
    /// a constraint of the hardware it targeted, not of the data, so OpenIGI gives each model its own
    /// buffer and keeps the 16-bit indices the models already use.
    /// </remarks>
    MeshHandle CreateMesh(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices);

    /// <summary>Uploads a mesh whose vertices will be replaced every pose, keeping the pose before.</summary>
    /// <param name="vertices">The rest pose, replaced by the first <see cref="UpdateAnimatedMesh"/>.</param>
    /// <param name="indices">The triangle list, three indices a triangle. Fixed for the mesh's life.</param>
    /// <returns>A handle naming the mesh.</returns>
    /// <remarks>
    /// <para>
    /// For CPU-skinned bodies. An ordinary mesh is immutable; an animated one is double-buffered, and
    /// each <see cref="UpdateAnimatedMesh"/> makes the vertices it replaces the mesh's <em>previous
    /// pose</em> — a second vertex stream the shaders can reach. That stream is what makes two things
    /// possible for a skinned body that a rigid transform cannot express: exact per-vertex motion for
    /// the temporal resolve (see <see cref="SetPreviousPose"/>), and drawing <em>between</em> two
    /// simulation poses (see <see cref="SetPoseInterpolation"/>).
    /// </para>
    /// <para>
    /// A mesh made by <see cref="CreateMesh"/> reports its own vertices as its previous pose, so every
    /// static mesh has exactly zero pose motion without paying for a second buffer.
    /// </para>
    /// </remarks>
    MeshHandle CreateAnimatedMesh(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices);

    /// <summary>Replaces an animated mesh's vertices; the ones replaced become its previous pose.</summary>
    /// <param name="mesh">A mesh from <see cref="CreateAnimatedMesh"/>.</param>
    /// <param name="vertices">The new pose. Must be exactly as many vertices as the mesh was created with.</param>
    /// <exception cref="ArgumentException">
    /// The handle is unknown, was not created animated, or the count differs.
    /// </exception>
    void UpdateAnimatedMesh(MeshHandle mesh, ReadOnlySpan<Vertex3D> vertices);

    /// <summary>Releases a mesh.</summary>
    /// <param name="mesh">The mesh to release. <see cref="MeshHandle.None"/> is ignored.</param>
    void DestroyMesh(MeshHandle mesh);

    /// <summary>Sets how subsequent draws interact with the depth buffer.</summary>
    /// <param name="state">The depth behaviour.</param>
    void SetDepthState(DepthState state);

    /// <summary>Sets which triangle facings subsequent draws discard.</summary>
    /// <param name="mode">The culling rule.</param>
    void SetCullMode(CullMode mode);

    /// <summary>Sets the projection applied to subsequent <see cref="DrawMesh"/> calls.</summary>
    /// <param name="projection">The view-to-clip transform.</param>
    /// <remarks>Set once per frame; the per-object transform travels with each draw instead.</remarks>
    void SetProjection(in Matrix4f projection);

    /// <summary>Applies the terrain LOD fade to subsequent <see cref="DrawMesh"/> calls.</summary>
    /// <param name="scale">What one unit of Chebyshev camera distance is worth.</param>
    /// <param name="offset">Subtracted from the scaled distance.</param>
    /// <param name="slope">Scales the offset distance into the fade's <c>[0, 1]</c>.</param>
    /// <param name="fadeAlpha">
    /// Whether the draw's alpha also fades out with the factor — the detail pass does, the base pass
    /// only morphs.
    /// </param>
    /// <remarks>
    /// The fade is <c>t = clamp((chebyshev * scale - offset) * slope, 0, 1)</c> per vertex, the distance
    /// taken along the world axes, exactly as <c>sub_512C30</c> computes it. While set, a vertex slides
    /// onto the morph target its normal channel carries as <c>t</c> rises — the seam morph — and with
    /// <paramref name="fadeAlpha"/> the alpha is additionally multiplied by <c>1 - t</c>. Cleared by
    /// <see cref="ClearDetailFade"/>; draws between the two calls are affected, everything else is not.
    /// </remarks>
    void SetDetailFade(float scale, float offset, float slope, bool fadeAlpha);

    /// <summary>Stops the terrain fade; subsequent <see cref="DrawMesh"/> calls draw unmodified.</summary>
    void ClearDetailFade();

    /// <summary>Gets the part of the frame draws currently belong to.</summary>
    /// <remarks><see cref="ScenePass.None"/> outside a scene capture, which is the original's own path.</remarks>
    ScenePass CurrentPass { get; }

    /// <summary>Starts rendering the scene into an offscreen target instead of the back buffer.</summary>
    /// <param name="description">The target to render into.</param>
    /// <returns>
    /// <see langword="true"/> when the capture began. <see langword="false"/> means the backend cannot
    /// offer one — the caller then draws exactly as the original does, which is a supported outcome
    /// rather than an error.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the one addition the whole remaster track rests on. The original throws each frame away
    /// as it presents it; shadows, ambient occlusion, reflections, volumetric fog and temporal
    /// anti-aliasing all need the frame and its depth to still be there afterwards.
    /// </para>
    /// <para>
    /// Pair with <see cref="EndSceneCapture"/> and then <see cref="ResolveScene"/>. Two-dimensional
    /// drawing — the HUD, the menus, the console — belongs <em>after</em> the resolve, on the back
    /// buffer, so that it is never tone mapped.
    /// </para>
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">A capture is already in progress.</exception>
    bool BeginSceneCapture(in SceneTargetDescription description);

    /// <summary>Sets how the following draws respond to light.</summary>
    /// <param name="material">The surface properties, from the port's own material table.</param>
    /// <remarks>
    /// Only the deferred path reads this; on the original's path nothing does. It is device state
    /// rather than a draw argument because a material spans a run of sub-meshes.
    /// </remarks>
    void SetMaterialOverride(in SurfaceMaterial material);

    /// <summary>Attaches a derived surface-detail map to a diffuse texture.</summary>
    /// <param name="diffuse">The diffuse texture the map belongs to.</param>
    /// <param name="detail">
    /// The packed map — the tangent-space normal's x and y, a roughness multiplier, and metallic — or
    /// <see cref="TextureHandle.None"/> to detach whatever was attached.
    /// </param>
    /// <remarks>
    /// <para>
    /// I.G.I. ships no normal maps and no roughness maps, so a physically-based shading model has three
    /// inputs it cannot answer from the data. This is how the answers get in: a map derived from the
    /// diffuse texture, or authored, or generated, registered once when the texture is loaded.
    /// </para>
    /// <para>
    /// <b>A property of the texture rather than of the draw</b>, and that is what makes it cheap to
    /// adopt. A detail map belongs to a diffuse texture wherever that texture is used, so registering
    /// the pairing here means no renderer has to carry it, no draw call grows an argument, and a
    /// renderer that has no detail maps needs no change at all.
    /// </para>
    /// <para>
    /// Only the deferred path reads it, and only when <see cref="SetMaterialDetail"/> asks for a
    /// non-zero strength. Nothing under the original's path is affected.
    /// </para>
    /// </remarks>
    /// <exception cref="GraphicsDeviceException"><paramref name="diffuse"/> is not a texture of this device.</exception>
    void SetTextureDetail(TextureHandle diffuse, TextureHandle detail);

    /// <summary>Sets how strongly the derived detail maps are applied.</summary>
    /// <param name="strength">
    /// Zero ignores them entirely — which is what every preset below <c>high</c> asks for — one is the
    /// amount they were derived at, and above that exaggerates.
    /// </param>
    void SetMaterialDetail(float strength);

    /// <summary>Sets how textures are sampled.</summary>
    /// <param name="maxAnisotropy">
    /// The anisotropic filtering ratio, 1 for none. A ground plane seen at a grazing angle is the
    /// worst case in the frame for trilinear filtering: the footprint of a pixel is a long thin
    /// sliver, mipmapping has to pick a level for its <em>widest</em> axis, and everything along the
    /// other axis is thrown away. That is what turns ground into a smear towards the horizon.
    /// </param>
    /// <param name="generateMissingMipmaps">
    /// Whether to build a mip chain for textures that arrive without one. The terrain's ground tiles
    /// do: the atlas's layer descriptors carry no mip count, so every one is a single 128x128 level,
    /// and minifying that without mips aliases into a crawling sparkle.
    /// </param>
    /// <remarks>
    /// Applies to textures created afterwards, and re-applies the sampling to those already uploaded.
    /// </remarks>
    void SetTextureQuality(int maxAnisotropy, bool generateMissingMipmaps);

    /// <summary>Sets the participating medium the scene is seen through.</summary>
    /// <param name="fog">The medium, or <see cref="VolumetricFogSettings.Off"/> for none.</param>
    /// <param name="cameraPosition">
    /// The camera's world position in metres. The march works in camera-relative world space, so the
    /// medium's base height and every volume's centre are rebased against this; passing it explicitly
    /// is clearer than having the shell pre-subtract it and hand over coordinates that are no longer
    /// world coordinates.
    /// </param>
    /// <param name="volumes">
    /// Regions where the medium differs from the level's base fog — rooms, basements, smoke. Retail
    /// has no such entities, so these are authored for the port.
    /// </param>
    /// <remarks>
    /// Applied after lighting and before the forward pass, so translucent surfaces and the view model
    /// sit in front of the fog rather than being fogged twice.
    /// </remarks>
    void SetVolumetricFog(
        in VolumetricFogSettings fog,
        Vector3f cameraPosition,
        ReadOnlySpan<LocalFogVolume> volumes);

    /// <summary>Sets what surfaces reflect and how far the frame is searched to find it.</summary>
    /// <param name="settings">The search, or <see cref="ReflectionSettings.Off"/> for none.</param>
    /// <remarks>
    /// Applied after the lighting and before the fog: a reflection is of the lit world, and it belongs
    /// behind the air rather than in front of it.
    /// </remarks>
    void SetReflections(in ReflectionSettings settings);

    /// <summary>Hands over the authored colour grade, or clears it.</summary>
    /// <param name="table">The table, or <see langword="null"/> to forget the one held.</param>
    /// <remarks>
    /// Separate from <see cref="PostProcessSettings.ColourLookup"/>, and called only when the grade
    /// changes — which in practice is when the mission does. The table is a volume texture upload; how
    /// much of it to apply is a number per frame.
    /// </remarks>
    void SetColourLookup(ColourLookupTable? table);

    /// <summary>Allocates a three-dimensional texture.</summary>
    /// <param name="width">Voxels along x.</param>
    /// <param name="height">Voxels along y.</param>
    /// <param name="depth">Voxels along z.</param>
    /// <param name="format">What one voxel holds.</param>
    /// <param name="name">A name for the texture, used only in diagnostics.</param>
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
    TextureHandle CreateVolumeTexture(int width, int height, int depth, VolumeFormat format, string name);

    /// <summary>Replaces a volume texture's contents.</summary>
    /// <typeparam name="T">The element type. Must match the texture's format.</typeparam>
    /// <param name="texture">The volume to write.</param>
    /// <param name="data">The voxels, x fastest then y then z.</param>
    /// <exception cref="GraphicsDeviceException">The handle is not a volume of this device, or the data is the wrong size.</exception>
    void UpdateVolumeTexture<T>(TextureHandle texture, ReadOnlySpan<T> data)
        where T : unmanaged;

    /// <summary>Binds a volume texture to a sampling unit.</summary>
    /// <param name="unit">The texture unit.</param>
    /// <param name="texture">The volume, or the null handle to unbind.</param>
    /// <remarks>
    /// Separate from <see cref="SetTexture"/> because the two bind different targets to the same unit,
    /// and a unit holding both is a unit whose meaning depends on which sampler reads it.
    /// </remarks>
    void SetVolumeTexture(int unit, TextureHandle texture);

    /// <summary>Compiles a compute program.</summary>
    /// <param name="source">The GLSL compute shader.</param>
    /// <param name="name">A name for the program, used only in diagnostics.</param>
    /// <returns>A handle to the program.</returns>
    /// <remarks>
    /// <para>
    /// The first member of this seam that not every backend can honour. Compute needs GL 4.3, which
    /// desktop has and macOS — frozen at 4.1 — does not, so a caller must check
    /// <see cref="GraphicsCapabilities.SupportsCompute"/> and have a path that does not need it.
    /// </para>
    /// <para>
    /// Programs are compiled up front rather than lazily, because a compile failure is a fault in the
    /// build rather than a condition to recover from, and finding it at load is worth more than finding
    /// it on the frame that first needed the pass.
    /// </para>
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">Compute is unsupported, or the program did not compile.</exception>
    ComputeShaderHandle CreateComputeShader(string source, string name);

    /// <summary>Releases a compute program.</summary>
    /// <param name="shader">The program to release. The null handle is ignored.</param>
    void DestroyComputeShader(ComputeShaderHandle shader);

    /// <summary>Allocates a shader-writable buffer.</summary>
    /// <param name="sizeInBytes">How large the buffer is.</param>
    /// <param name="name">A name for the buffer, used only in diagnostics.</param>
    /// <returns>A handle to the buffer, zero-filled.</returns>
    /// <remarks>
    /// Zero-filled on creation, and that is part of the contract rather than an accident of the driver:
    /// almost every buffer here is an append list with a count at the front, and a count that starts at
    /// whatever was in memory is a pass that reads garbage on its first frame.
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">Compute is unsupported, or the size is not positive.</exception>
    StorageBufferHandle CreateStorageBuffer(int sizeInBytes, string name);

    /// <summary>Replaces the contents of a storage buffer.</summary>
    /// <typeparam name="T">The element type. Must be blittable.</typeparam>
    /// <param name="buffer">The buffer to write.</param>
    /// <param name="data">The data to write, from the start of the buffer.</param>
    /// <exception cref="GraphicsDeviceException"><paramref name="buffer"/> is not a buffer of this device.</exception>
    void UpdateStorageBuffer<T>(StorageBufferHandle buffer, ReadOnlySpan<T> data)
        where T : unmanaged;

    /// <summary>Reads a storage buffer back into memory.</summary>
    /// <typeparam name="T">The element type. Must be blittable.</typeparam>
    /// <param name="buffer">The buffer to read.</param>
    /// <param name="destination">Receives the contents, from the start of the buffer.</param>
    /// <remarks>
    /// <b>Synchronising, and therefore for diagnostics and load-time work rather than for a frame.</b>
    /// Reading a buffer the GPU may still be writing means waiting for it, which costs whatever
    /// pipelining the driver had built up. Every per-frame consumer of a compute result should be
    /// another shader.
    /// </remarks>
    /// <exception cref="GraphicsDeviceException"><paramref name="buffer"/> is not a buffer of this device.</exception>
    void ReadStorageBuffer<T>(StorageBufferHandle buffer, Span<T> destination)
        where T : unmanaged;

    /// <summary>Releases a storage buffer.</summary>
    /// <param name="buffer">The buffer to release. The null handle is ignored.</param>
    void DestroyStorageBuffer(StorageBufferHandle buffer);

    /// <summary>Binds a storage buffer to a numbered slot for the next dispatch.</summary>
    /// <param name="slot">The binding point, matching the shader's <c>binding =</c> layout qualifier.</param>
    /// <param name="buffer">The buffer, or the null handle to unbind.</param>
    void BindStorageBuffer(int slot, StorageBufferHandle buffer);

    /// <summary>Binds a texture as a readable or writable image for the next dispatch.</summary>
    /// <param name="slot">The image unit, matching the shader's <c>binding =</c> layout qualifier.</param>
    /// <param name="texture">The texture, or the null handle to unbind.</param>
    /// <param name="access">How the pass intends to touch it.</param>
    /// <param name="level">Which mip level to bind.</param>
    /// <remarks>
    /// Distinct from <see cref="SetTexture"/>, and not interchangeable with it: a sampler reads through
    /// filtering and normalised coordinates, while an image binding is unfiltered, integer-addressed,
    /// and writable. A compute pass that builds a volume wants the second; one that traces through a
    /// finished volume wants the first.
    /// </remarks>
    void BindImage(int slot, TextureHandle texture, ImageAccess access, int level = 0);

    /// <summary>Runs a compute program over a grid of work groups.</summary>
    /// <param name="shader">The program to run.</param>
    /// <param name="groupsX">Work groups in x. Must be positive.</param>
    /// <param name="groupsY">Work groups in y. Must be positive.</param>
    /// <param name="groupsZ">Work groups in z. Must be positive.</param>
    /// <remarks>
    /// Counts are <em>groups</em>, not threads: the group's size is declared by the shader, so a pass
    /// over a 1920×1080 image with an 8×8 group dispatches 240×135.
    /// </remarks>
    /// <exception cref="GraphicsDeviceException"><paramref name="shader"/> is not a program of this device.</exception>
    void Dispatch(ComputeShaderHandle shader, int groupsX, int groupsY = 1, int groupsZ = 1);

    /// <summary>Runs a compute program over a grid a buffer decides.</summary>
    /// <param name="shader">The program to run.</param>
    /// <param name="arguments">A buffer holding three consecutive unsigned integers: the group counts.</param>
    /// <param name="offsetInBytes">Where in that buffer the three counts start.</param>
    /// <remarks>
    /// What lets a pass size the next one. The culling stage cannot know how many meshes fall inside a
    /// clipmap until it has looked, and reading its count back to the CPU to launch the next dispatch
    /// would cost a full synchronisation every frame — so it writes the count into a buffer and the
    /// GPU reads it without anybody waiting.
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">A handle does not belong to this device.</exception>
    void DispatchIndirect(ComputeShaderHandle shader, StorageBufferHandle arguments, int offsetInBytes = 0);

    /// <summary>Makes a previous dispatch's writes visible to what follows.</summary>
    /// <param name="scope">What kind of access has to become visible.</param>
    /// <remarks>
    /// Dispatches are not ordered against each other without this. Omitting it does not usually fail
    /// on the machine it was written on — the timing simply happens to work — which is what makes a
    /// missing barrier one of the harder faults to find later.
    /// </remarks>
    void MemoryBarrier(MemoryBarrierScope scope);

    /// <summary>Sets a scalar uniform on a compute program.</summary>
    /// <param name="shader">The program.</param>
    /// <param name="name">The uniform's name.</param>
    /// <param name="value">The value.</param>
    /// <remarks>
    /// Deliberately small. Anything with real structure belongs in a storage buffer, where it can be
    /// indexed and written by another pass; this is for the handful of scalars that decide what a
    /// dispatch is doing, and a uniform block would be more machinery than that is worth.
    /// </remarks>
    void SetComputeUniform(ComputeShaderHandle shader, string name, float value);

    /// <summary>Sets an integer uniform on a compute program.</summary>
    /// <param name="shader">The program.</param>
    /// <param name="name">The uniform's name.</param>
    /// <param name="value">The value.</param>
    void SetComputeUniform(ComputeShaderHandle shader, string name, int value);

    /// <summary>Sets a three-component uniform on a compute program.</summary>
    /// <param name="shader">The program.</param>
    /// <param name="name">The uniform's name.</param>
    /// <param name="value">The value.</param>
    void SetComputeUniform(ComputeShaderHandle shader, string name, in Vector3f value);

    /// <summary>Sets a four-component uniform on a compute program.</summary>
    /// <param name="shader">The program.</param>
    /// <param name="name">The uniform's name.</param>
    /// <param name="x">The first component.</param>
    /// <param name="y">The second component.</param>
    /// <param name="z">The third component.</param>
    /// <param name="w">The fourth component.</param>
    void SetComputeUniform(ComputeShaderHandle shader, string name, float x, float y, float z, float w);

    /// <summary>Turns per-stage GPU timing on or off.</summary>
    /// <param name="enabled">Whether the backend should time each <see cref="RenderStage"/>.</param>
    /// <remarks>
    /// <para>
    /// Off by default and worth keeping that way when nobody is reading it. A timer query is a
    /// synchronisation point: the driver is free to reorder and batch work between draw calls, and
    /// asking when a stage began and ended narrows what it may do. The measurement is close enough to
    /// judge a budget by and is not free.
    /// </para>
    /// <para>
    /// Switching it off discards the history, so the peaks reported after switching it back on belong
    /// to the run being watched rather than to some earlier camera angle.
    /// </para>
    /// </remarks>
    void SetStageTiming(bool enabled);

    /// <summary>Gets what each stage of the frame last cost the GPU.</summary>
    /// <returns>
    /// One entry per stage that ran, in frame order; empty when timing is off or when no result has
    /// come back yet.
    /// </returns>
    /// <remarks>
    /// The results lag the frame being drawn by a frame or two, deliberately. A query's result is not
    /// ready when its stage ends, and waiting for it would stall the very pipeline being measured — so
    /// the answers are collected whenever the driver has them and reported a little late instead.
    /// </remarks>
    IReadOnlyList<RenderStageTiming> StageTimings();

    /// <summary>Sets which buffer the frame shows instead of the scene.</summary>
    /// <param name="view">The buffer, or <see cref="DebugView.None"/> for the scene itself.</param>
    /// <remarks>
    /// Its own member rather than a field of <see cref="PostProcessSettings"/> because the passes that
    /// can answer it are not all the resolve: the probe field's contribution exists only inside the
    /// lighting pass, and by the time the frame resolves it has been multiplied into the picture.
    /// </remarks>
    void SetDebugView(DebugView view);

    /// <summary>Hands over the level's baked probe field, or clears it.</summary>
    /// <param name="volume">The baked volume, or <see langword="null"/> to forget the one held.</param>
    /// <remarks>
    /// Separate from <see cref="SetIrradianceVolumeSettings"/> and called only when the level changes:
    /// the volume is megabytes of baked transfer that does not vary per frame, while the colours
    /// applied to it do.
    /// </remarks>
    void SetIrradianceVolume(IrradianceVolume? volume);

    /// <summary>Sets how this frame lights itself from the probe field.</summary>
    /// <param name="settings">
    /// The application, or <see cref="IrradianceVolumeSettings.Off"/> for none. With none, or with no
    /// volume handed over, surfaces fall back to the flat ambient term.
    /// </param>
    void SetIrradianceVolumeSettings(in IrradianceVolumeSettings settings);

    /// <summary>Sets how much of the sky each surface can see, and what its neighbours bounce onto it.</summary>
    /// <param name="settings">
    /// The search, or <see cref="AmbientOcclusionSettings.Off"/> for none. With none, the lighting pass
    /// uses full visibility and no bounce, which is the flat ambient the stage before this one had.
    /// </param>
    /// <remarks>
    /// Applied to the ambient and bounced terms only, never to the sun. Occluding direct light is what
    /// shadow maps are for, and doing it here as well would darken the same geometry twice — visibly so
    /// in a corner that is both shadowed and enclosed, which is most corners.
    /// </remarks>
    void SetAmbientOcclusion(in AmbientOcclusionSettings settings);

    /// <summary>Says where the next meshes drawn stood in the previous frame.</summary>
    /// <param name="previousModelView">
    /// The object's previous pose seen through the <em>current</em> camera — the same matrix a draw
    /// would have used last frame had the camera not moved since.
    /// </param>
    /// <remarks>
    /// <para>
    /// Only for geometry that moves of its own accord: soldiers, doors, trains, the player's body.
    /// Everything static can ignore this entirely, because the temporal resolve already reprojects the
    /// camera exactly and a static object's previous pose is its current one — which this reports as
    /// zero motion without a renderer having to say anything.
    /// </para>
    /// <para>
    /// The current camera rather than the previous one is not an approximation made for convenience: it
    /// is what isolates the object's own movement from the camera's, so the two are recovered by
    /// different means and neither is counted twice.
    /// </para>
    /// </remarks>
    void SetPreviousPose(in Matrix4f previousModelView);

    /// <summary>Stops reporting a previous pose; subsequent meshes are treated as not having moved.</summary>
    void ClearPreviousPose();

    /// <summary>Sets how the next meshes drawn blend between their two poses.</summary>
    /// <param name="currentBlend">
    /// How much of the <em>previous-pose</em> vertex stream is mixed into the rasterised position, 0
    /// to 1. Zero draws the current pose exactly — the value every static mesh is implicitly drawn
    /// with, since its previous stream is itself.
    /// </param>
    /// <param name="previousBlend">
    /// The same mix for the position reported as <em>last frame's</em>, alongside
    /// <see cref="SetPreviousPose"/>. The difference between the two blends is precisely the pose
    /// motion the frame reports: a fixed-tick simulation renders between two known poses, so last
    /// frame's vertex is a different mix of the same two streams, not a third buffer.
    /// </param>
    /// <remarks>
    /// Meaningful only for meshes from <see cref="CreateAnimatedMesh"/>; everywhere else both streams
    /// are one buffer and any blend of a value with itself is that value. Cleared by
    /// <see cref="ClearPoseInterpolation"/>, and by <see cref="ClearPreviousPose"/> callers should
    /// clear both together — a pose blend without its matching previous pose reports half a motion.
    /// </remarks>
    void SetPoseInterpolation(float currentBlend, float previousBlend);

    /// <summary>Stops blending poses; subsequent meshes draw their current vertices exactly.</summary>
    void ClearPoseInterpolation();

    /// <summary>Sets how this frame accumulates into the ones before it.</summary>
    /// <param name="settings">
    /// The accumulation, or <see cref="TemporalAaSettings.Off"/> for none. Its jitter is the offset the
    /// caller has <em>already</em> applied to the projection it draws with; it is reported here so the
    /// backend can account for it, not so the backend can apply it.
    /// </param>
    /// <param name="projection">
    /// The scene's projection <em>without</em> the jitter. Reprojection has to compare two frames in one
    /// frame of reference, and the jitter is precisely what differs between them; the unjittered matrix
    /// is that shared reference. This is passed rather than read from
    /// <see cref="SetProjection"/> because the view model draws with a projection of its own, and by the
    /// time the frame resolves that is the one the device last saw.
    /// </param>
    /// <param name="cameraPosition">
    /// The camera's world position in metres, used to work out how far it has travelled since the
    /// previous frame.
    /// </param>
    /// <param name="viewToWorld">
    /// The rotation taking a view-space direction back into camera-relative world space.
    /// </param>
    /// <remarks>
    /// The backend keeps the previous frame's values itself, so a caller supplies only what is true
    /// now. A frame in which this is not called is a frame with no history, which is the correct
    /// behaviour on a camera cut.
    /// </remarks>
    void SetTemporalAntiAliasing(
        in TemporalAaSettings settings,
        in Matrix4f projection,
        Vector3f cameraPosition,
        Matrix3f viewToWorld);

    /// <summary>Sets the sun's shadow map for this frame.</summary>
    /// <param name="settings">How the shadows are drawn, or <see cref="ShadowSettings.Off"/> for none.</param>
    /// <param name="cascades">The fitted cascades, from <see cref="ShadowCascades.Fit"/>.</param>
    /// <param name="unprojection">
    /// What the lighting pass needs to rebuild a view-space position from the depth buffer: the
    /// projection.s x and y scales, then its near and far planes.
    /// </param>
    /// <param name="viewToWorld">
    /// The rotation taking a view-space direction back into <em>camera-relative</em> world space,
    /// which is the frame the cascades are fitted in.
    /// </param>
    void SetShadows(
        in ShadowSettings settings,
        ReadOnlySpan<ShadowCascade> cascades,
        in DepthUnprojection unprojection,
        Matrix3f viewToWorld);

    /// <summary>Starts rendering depth into one cascade.</summary>
    /// <param name="cascade">The zero-based cascade index.</param>
    /// <returns><see langword="false"/> when there is no shadow map to render into.</returns>
    /// <remarks>
    /// Draws between this and <see cref="EndShadowCascade"/> write depth only; their textures are read
    /// solely for the cut-out test. Called outside a scene capture, before the frame is drawn.
    /// </remarks>
    bool BeginShadowCascade(int cascade);

    /// <summary>Ends the current cascade.</summary>
    void EndShadowCascade();

    /// <summary>Sets the light the scene is lit by for this frame.</summary>
    /// <param name="lighting">The lighting model, sun and ambient.</param>
    void SetLighting(in SceneLighting lighting);

    /// <summary>Declares which part of the frame the following draws belong to.</summary>
    /// <param name="pass">The part of the frame.</param>
    /// <remarks>
    /// The renderers themselves never call this; the shell does, around the groups of draws it already
    /// makes. That is what lets the backend shade an opaque surface into a G-buffer and a translucent
    /// one directly, without any renderer in the game learning that there are two ways to be drawn.
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">No capture is in progress, or a pass is already open.</exception>
    void BeginPass(ScenePass pass);

    /// <summary>Ends the current pass.</summary>
    /// <exception cref="GraphicsDeviceException">No pass is open.</exception>
    void EndPass();

    /// <summary>Stops rendering into the offscreen target.</summary>
    /// <exception cref="GraphicsDeviceException">No capture is in progress, or a pass is still open.</exception>
    void EndSceneCapture();

    /// <summary>Brings the captured scene to the back buffer.</summary>
    /// <param name="post">What to do to it on the way.</param>
    /// <remarks>
    /// With <see cref="PostProcessSettings.Identity"/> and a non-HDR target this is a straight copy,
    /// which is what keeps the vanilla preset identical to the build that had no offscreen path.
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">Nothing has been captured this frame.</exception>
    void ResolveScene(in PostProcessSettings post);

    /// <summary>Draws part of a mesh.</summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="indexOffset">The first index to draw.</param>
    /// <param name="indexCount">How many indices to draw; a multiple of three.</param>
    /// <param name="modelView">The model-to-view transform, from <see cref="Camera3D.CreateModelView(Vector3d, Matrix3f)"/>.</param>
    /// <param name="diffuse">The diffuse texture, or <see cref="TextureHandle.None"/> for untextured geometry.</param>
    /// <param name="lightmap">The lightmap texture, or <see cref="TextureHandle.None"/> to skip the lightmap term.</param>
    /// <param name="blend">How fragments combine with the render target.</param>
    /// <exception cref="ArgumentException">The handle is unknown, or the range lies outside the mesh.</exception>
    /// <remarks>
    /// One call per sub-mesh: a model's draw records each name their own texture, so a model with four
    /// records is four calls into the same mesh at different index ranges.
    /// </remarks>
    void DrawMesh(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend);
}
