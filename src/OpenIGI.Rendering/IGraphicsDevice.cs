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
