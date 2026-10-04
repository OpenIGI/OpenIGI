using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

using OpenIGI.Shaders;

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
    /// <summary>The cut-out threshold a device starts at, and returns to.</summary>
    /// <remarks>
    /// 128, which is the half this port has always tested at to within a texel value — the shader's
    /// literal was <c>0.5</c> and <c>128 / 255</c> is <c>0.502</c>. Chosen so that making the threshold
    /// settable changes no picture until somebody asks it to. It is <em>not</em> either of I.G.I. 2's
    /// numbers; see <see cref="SetAlphaReference"/>.
    /// </remarks>
    const byte DefaultAlphaReference = 128;

    /// <summary>The number of faces a cube map has.</summary>
    const int CubeFaceCount = 6;

    /// <summary>Gets what this device supports. Mirrors <c>GetGFXCaps</c>.</summary>
    GraphicsCapabilities Capabilities { get; }

    /// <summary>Gets the mode the device is currently set to.</summary>
    DisplayMode CurrentMode { get; }

    /// <summary>Gets or sets a value indicating whether presentation waits for the vertical blank. Mirrors <c>DisplayVSync</c>.</summary>
    bool VSyncEnabled { get; set; }

    /// <summary>Sets the back-buffer size and colour depth. Mirrors <c>DisplaySetMode</c>.</summary>
    /// <param name="mode">The mode to apply.</param>
    /// <exception cref="GraphicsDeviceException">The mode could not be applied.</exception>
    void SetMode(in DisplayMode mode);

    /// <summary>Clears the colour and depth buffers. Mirrors <c>DisplayClear</c>.</summary>
    /// <param name="color">The colour to fill with. The alpha channel is ignored, as it is in the original.</param>
    /// <remarks>The original always clears colour and depth together and always clears depth to <c>1.0</c>.</remarks>
    void Clear(Bgra32 color);

    /// <summary>Clears only the depth buffer to its far value.</summary>
    /// <remarks>
    /// This is for a foreground 3D layer which must retain its own depth ordering without comparing
    /// against an already-rendered scene. It does not change the colour buffer or the active depth state.
    /// </remarks>
    void ClearDepth();

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

    /// <summary>Copies the render target into a texture, without going through system memory.</summary>
    /// <param name="texture">The destination texture; it must be at least <paramref name="width"/> × <paramref name="height"/>.</param>
    /// <param name="width">The width to copy in pixels.</param>
    /// <param name="height">The height to copy in pixels.</param>
    /// <remarks>
    /// <see cref="CaptureFrame"/>'s counterpart for the case where the frame is wanted <em>as a texture</em>
    /// rather than as an image: it stays on the GPU, so it costs a copy instead of a read back plus an
    /// upload. What it is for is a cross-dissolve — holding the last frame of the outgoing picture so it can
    /// be drawn over the incoming one and faded off, which is the only way to blend two of something the
    /// renderer only keeps one of.
    /// </remarks>
    void CopyFrameToTexture(TextureHandle texture, int width, int height);

    /// <summary>Draws the loading backdrop over the whole frame: a held picture defocused and graded, or a procedural surface.</summary>
    /// <param name="texture">The held picture, as captured by <see cref="CopyFrameToTexture"/>; ignored when the settings say there is none.</param>
    /// <param name="settings">How far out of focus, how graded, how faded, and the rest.</param>
    /// <remarks>
    /// A fullscreen pass rather than sprites, because a defocus and a grade are per-pixel work no number
    /// of tinted quads can imitate. Blended over the frame, so the reveal can fade it off the live level.
    /// Drawn outside any sprite batch.
    /// </remarks>
    void DrawLoadingBackdrop(TextureHandle texture, in LoadingBackdropSettings settings);

    /// <summary>Blurs the frame drawn so far into a few levels of increasing blur, for the interface's frosted glass.</summary>
    /// <returns>The levels, valid until the next call or the end of the frame; empty on a device that cannot.</returns>
    /// <remarks>
    /// <para>
    /// Called once a frame, after the scene and before the interface: the menu shell draws its backdrop and
    /// every glass panel by sampling these in screen space. Like <see cref="CopyFrameToTexture"/> it closes
    /// the pass in flight and the next draw reopens it on top, so nothing drawn is lost.
    /// </para>
    /// <para>
    /// The levels hold linear light, the form an sRGB image's sampler hands the 2D pass, so they draw
    /// through the ordinary sprite path with no special shading.
    /// </para>
    /// </remarks>
    BackdropBlur BlurFrameForBackdrop() => BackdropBlur.None;

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
    /// the temporal resolve, and drawing <em>between</em> two
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

    /// <summary>Allocates an empty mesh with fixed capacity, to be filled by <see cref="UploadMeshRange"/>.</summary>
    /// <param name="vertexCapacity">How many vertices the mesh can ever hold.</param>
    /// <param name="indexCapacity">How many indices the mesh can ever hold.</param>
    /// <returns>A handle naming the mesh, which draws nothing until a range is uploaded.</returns>
    /// <exception cref="ArgumentException">A capacity is not positive, or the vertex capacity exceeds 16-bit indexing.</exception>
    /// <exception cref="GraphicsDeviceException">The mesh could not be created.</exception>
    /// <remarks>
    /// For meshes that grow by appending — the terrain's batch pages. A mesh from
    /// <see cref="CreateMesh"/> is immutable, so growing one means destroying it and re-uploading
    /// everything it held; done every time a streamed-in chunk joins a page, those re-uploads were the
    /// single largest source of frame spikes. A storage mesh uploads each appended range once.
    /// </remarks>
    MeshHandle CreateMeshStorage(int vertexCapacity, int indexCapacity);

    /// <summary>Writes a range of vertices and indices into a mesh made by <see cref="CreateMeshStorage"/>.</summary>
    /// <param name="mesh">A mesh from <see cref="CreateMeshStorage"/>.</param>
    /// <param name="vertexOffset">The first vertex slot written.</param>
    /// <param name="vertices">The vertices to write; may be empty.</param>
    /// <param name="indexOffset">The first index slot written.</param>
    /// <param name="indices">The indices to write, addressing the mesh's own vertex slots; may be empty.</param>
    /// <exception cref="ArgumentException">
    /// The handle is unknown or not a storage mesh, a range runs past its capacity, or an index
    /// addresses a slot past the vertex capacity.
    /// </exception>
    /// <remarks>
    /// Ranges written are drawable immediately; the mesh's drawable index count is the highest index
    /// slot ever written plus one. Callers append, so ranges beyond what the GPU has drawn are new
    /// storage and never stall the pipeline.
    /// </remarks>
    void UploadMeshRange(
        MeshHandle mesh,
        int vertexOffset,
        ReadOnlySpan<Vertex3D> vertices,
        int indexOffset,
        ReadOnlySpan<ushort> indices);

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

    /// <summary>Sets the distance fog every following world draw is blended into.</summary>
    /// <param name="fog">The fog, or <see cref="SceneFog.None"/> to draw unfogged.</param>
    /// <remarks>
    /// A device state rather than a per-draw argument, because it is a property of the frame: the level
    /// publishes one fog and every world draw in the frame wears it. The first-person rig and the sky
    /// are the exceptions the original makes too — the rig is a separate layer and the sky dome is drawn
    /// with <c>FOGENABLE</c> off (<c>sub_52D060</c>) — so the sky renderer clears this around its own
    /// draws. See <see cref="SceneFog"/> for what the numbers are and where they were read from.
    /// </remarks>
    void SetFog(in SceneFog fog);

    /// <summary>Gets the fog the last <see cref="SetFog"/> set.</summary>
    /// <remarks>So that a renderer which must draw unfogged — the sky — can put back what it found.</remarks>
    SceneFog Fog { get; }

    /// <summary>Sets the flat colour every following model with no lightmap is multiplied by.</summary>
    /// <param name="ambient">The level's ambient, or <see cref="Vector3f.One"/> to leave models alone.</param>
    /// <remarks>
    /// The original's model lighting, and nearly all of it: a model sub-mesh with no lightmap is drawn
    /// with Direct3D lighting on (<c>sub_49F1C0</c>), whose whole contribution is the global
    /// <c>D3DRENDERSTATE_AMBIENT</c> against a material that reflects it entirely — the directional
    /// lights the state block sets alongside it arrive scaled by the world's own <c>0.001</c> and are
    /// worth a quarter of one 8-bit level. A sub-mesh WITH a lightmap is drawn with lighting off and the
    /// lightmap in the second texture stage, which is why this applies to one and not the other.
    /// </remarks>
    void SetModelAmbient(Vector3f ambient);

    /// <summary>Gets the flat model light the last <see cref="SetModelAmbient"/> set.</summary>
    /// <remarks>So that a renderer which must draw untinted — the sky — can put back what it found.</remarks>
    Vector3f ModelAmbient { get; }

    /// <summary>Sets the colour the following draws fog towards, until it is set again.</summary>
    /// <param name="target">The colour: the scene's fog, half of it, or white.</param>
    /// <remarks>
    /// For I.G.I. 2's ground, whose three multiplied passes each fog to a different colour so that the
    /// product lands on the scene's fog at distance — see <see cref="FogTarget"/> for the state blocks.
    /// Device state, like <see cref="SetLightmapModulates"/>: it spans a run of draws and must be put
    /// back to <see cref="FogTarget.Scene"/> after them. A title that never calls this fogs everything
    /// to the scene's colour, which is what the forward path always did. It reaches a draw through
    /// <see cref="SceneFog.ColourFor(BlendMode, FogTarget)"/>, alongside the colour itself.
    /// </remarks>
    void SetFogTarget(FogTarget target)
    {
        _ = target;
    }

    /// <summary>Sets the fixed-function sun a title's unlightmapped objects are lit by, for every draw until it is set again.</summary>
    /// <param name="ambient">The flat light every such vertex receives, linear.</param>
    /// <param name="colour">The directional light's diffuse colour, linear.</param>
    /// <remarks>
    /// <para>
    /// The colours are a LEVEL'S CONSTANTS, like the fog: I.G.I. 2 reads them off its <c>Dirlight</c> and
    /// <c>GlobalLight</c> when the mission loads (<c>DirlightQTask_DrawCallback</c>, <c>igi2ed 0x657680</c>;
    /// <c>GlobalLightQTask_DrawCallback</c>, <c>0x6534E0</c>) and the backend is free to bake them in.
    /// The direction is not, because it is applied in each object's own space: see
    /// <see cref="SetVertexSun"/>, which is what switches the lighting on for a run of draws at all.
    /// </para>
    /// <para>
    /// A title that has no such light never calls this and both stay black, which lights nothing.
    /// </para>
    /// </remarks>
    void SetSceneSun(Vector3f ambient, Vector3f colour)
    {
        _ = ambient;
        _ = colour;
    }

    /// <summary>Lights the following draws' vertices with the scene sun, from this direction; zero switches it off.</summary>
    /// <param name="towardsSun">
    /// The unit vector towards the sun in the MODEL space of the draws that follow, or zero for draws that
    /// take no vertex lighting.
    /// </param>
    /// <remarks>
    /// <para>
    /// What Direct3D's vertex pipeline computed for an I.G.I. 2 object with no baked lightmap
    /// (<c>Direct3DRender_SetGenericStateRigid</c>, <c>igi2ed 0x56CA90</c>: <c>D3DRS_LIGHTING = 1</c> over a
    /// white material): each vertex's colour becomes <c>saturate(ambient + colour · max(N · L, 0))</c>
    /// and the texture is modulated by it. In model space so that the vertex stage needs nothing but the
    /// normal it already has; the caller knows the placement's orientation and the backend need not.
    /// </para>
    /// <para>Device state, like <see cref="SetLightmapScale"/>: it spans a run of draws and must be cleared after them.</para>
    /// </remarks>
    void SetVertexSun(Vector3f towardsSun)
    {
        _ = towardsSun;
    }

    /// <summary>Makes the following draws multiply their lightmap texture into the base colour.</summary>
    /// <param name="modulate">Whether the lightmap slot is a colour to modulate rather than a bake.</param>
    /// <remarks>
    /// <para>
    /// Two of I.G.I. 2's surfaces put a colour in that slot rather than a bake to keep apart, and both
    /// want it multiplied into what stage 0 laid down — which is what the original's stage 1 does and
    /// what the forward path here does already. The FOREST is the other one: its second texture is a
    /// single texel holding the tree's own baked colour, because the original's foliage stage 0 is
    /// <c>MODULATE2X(TEXTURE, DIFFUSE)</c> with fixed-function lighting off
    /// (<c>ForestRender_SetTextureState_AlphaTestFoliage</c>, <c>igi2ed 0x57C4B0</c>, and
    /// <c>ForestRender_SetGenericState_AlphaTestFoliage</c>, <c>0x57C770</c>, writing
    /// <c>D3DRS_LIGHTING = 0</c>) and this port has nowhere else to put a per-draw diffuse: the forward
    /// push block is full.
    /// </para>
    /// <para>
    /// For I.G.I. 2's ground, whose second texture is a DETAIL map rather than a lightmap.
    /// <c>TerrainRender_SetMaterialTextures</c> (<c>igi2ed 0x667330</c>) binds the material's diffuse to
    /// stage 0 at <c>1 / (TextureScale * 4096)</c> and its detail texture to stage 1 at
    /// <c>1 / (DetailTextureScale * 4096)</c> — the two scales it writes into vertex-shader constants
    /// 11 and 12 — and <c>TerrainRender_SetTextureState</c> (<c>0x66B810</c>) gives stage 1
    /// <c>TEXCOORDINDEX = 1</c> and <c>COLOROP = MODULATE(TEXTURE, CURRENT)</c>. The ground's colour is
    /// therefore the diffuse times the detail, at two different tiling rates, and it is where the rock
    /// and grass grain in the original's hillsides comes from: the diffuse alone repeats every few tens
    /// of metres and reads as a smooth wash.
    /// </para>
    /// <para>
    /// The forward path needs nothing said to it — it already multiplies the lightmap into the colour,
    /// which for a bound detail map is exactly the stage-1 modulate. The deferred path routes the
    /// lightmap to its own G-buffer attachment instead, which is right for an object's bake and wrong
    /// for this, so this is how it is told which of the two it has.
    /// </para>
    /// <para>Device state, like <see cref="SetLightmapScale"/>: it spans a run of draws.</para>
    /// </remarks>
    void SetLightmapModulates(bool modulate)
    {
        _ = modulate;
    }

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

    /// <summary>Sets the headroom the following draws' lightmap pages were encoded with.</summary>
    /// <param name="scale">
    /// The value each sampled lightmap is multiplied by after it is decoded. One for the shipped LDR pages;
    /// a path-traced HDR page stores the radiance divided by this before its sRGB encode, so scaling it back
    /// up recovers a lightmap that keeps its bright values above one instead of clipping.
    /// </param>
    /// <remarks>Device state, like <see cref="SetMaterialOverride"/>: it spans a run of draws.</remarks>
    void SetLightmapScale(float scale);

    /// <summary>Marks the draws that follow as geometry that exists only in front of the eye.</summary>
    /// <param name="firstPerson">Whether the following draws are the view model rather than the world.</param>
    /// <remarks>
    /// <para>
    /// The arms and the held weapon are drawn into the same lit colour as the world, and two passes read
    /// that colour back as though everything in it were world: the eye adaptation meters it, and the
    /// screen-space indirect gather traces against it. Neither is true of a view model. A white knife
    /// held up to the camera raises the metered luminance and darkens the whole frame, and bounces light
    /// off itself onto the room -- so pressing the free-camera key, which simply stops drawing it,
    /// visibly changes the lighting of a scene that has not moved.
    /// </para>
    /// <para>
    /// Device state, like <see cref="SetLightmapScale"/>: it spans a run of draws and must be cleared
    /// after them. A backend that does not separate the two may ignore it.
    /// </para>
    /// </remarks>
    void SetFirstPersonGeometry(bool firstPerson);

    /// <summary>Marks the draws that follow as moving under their own power.</summary>
    /// <param name="moving">Whether the object being drawn has its own motion this frame.</param>
    /// <param name="identity">
    /// Which object this is, the same number every frame, or nought to pair by the mesh alone. A renderer that
    /// skips some of its objects on some frames -- occlusion, a frustum -- changes which draw of a shared mesh
    /// comes n-th, and paired by the mesh alone the n-th draw of a crate is a different crate from one frame
    /// to the next.
    /// </param>
    /// <remarks>
    /// <para>
    /// The velocity the temporal passes read is derived from depth and the two cameras, so it describes only
    /// what the camera did. A car, a jet, a train or a swinging door drawn without this is reprojected as if it
    /// were the scenery behind it, and its history trails behind it as a ghost. With it set, each geometry draw
    /// is paired with its own transform from the frame before and stamps its true motion.
    /// </para>
    /// <para>
    /// Device state, like <see cref="SetFirstPersonGeometry"/>: set it around a run of draws and clear it after.
    /// A backend without per-object motion may ignore it.
    /// </para>
    /// </remarks>
    void SetObjectMotion(bool moving, long identity = 0);

    /// <summary>Says that the title draws a sky of its own, which the frame should keep.</summary>
    /// <param name="painted">Whether the sky about to be drawn is the game's own painted one.</param>
    /// <remarks>
    /// <para>
    /// A deferred frame has to decide what to put where nothing was drawn, and a remaster's answer is
    /// its own atmosphere — which is the right answer for a game whose sky is a flat painted gradient
    /// and whose missions are daylight. It is the wrong answer for a game that authors a sky per
    /// mission: an atmosphere knows night only as a sun below the horizon, and I.G.I. 2's night
    /// missions leave the sun up and paint it black, so its night skies came back as daylight blue.
    /// </para>
    /// <para>
    /// Setting this says two things at once: keep what the dome drew, and treat it as a picture rather
    /// than a radiance — divide it by the exposure the resolve will multiply back, the same
    /// compensation <see cref="SetFirstPersonGeometry"/> already earns for the view model. Defaulted to
    /// nothing, so a backend that never exposes anything ignores it and a title that has not asked
    /// keeps the picture it had.
    /// </para>
    /// </remarks>
    void SetPaintedSky(bool painted)
    {
        _ = painted;
    }

    /// <summary>Gets whether a ray traced against the world would meet the level's own geometry.</summary>
    /// <remarks>
    /// The world's acceleration structure is only built once the level has been drawn and its geometry
    /// gathered, and until then a tracing pass binds a stand-in that every ray misses: the answer an empty
    /// world gives. Anything that lights the level ONCE and keeps the result -- the surface cache's
    /// load-time convergence -- has to wait for this, or it lights every surface as if nothing stood
    /// between it and any light. Always true on a device that traces nothing, where the question does not
    /// arise.
    /// </remarks>
    bool WorldTraceReady => true;

    /// <summary>Gets whether the world's acceleration structure has stopped growing.</summary>
    /// <remarks>
    /// The structure is filled from what the frames draw and offer, a few meshes a frame, so it is ready
    /// long before it is whole. Something that lights the level once and keeps the result waits for this,
    /// and only falls back to <see cref="WorldTraceReady"/> when the level never settles. Always true on a
    /// device that traces nothing.
    /// </remarks>
    bool WorldTraceSettled => true;

    /// <summary>Lifts, or restores, the per-frame budget on geometry newly admitted to the traced world.</summary>
    /// <param name="unlimited">Whether everything offered is admitted in the frame it is offered.</param>
    /// <remarks>
    /// For the loading screen: the budget keeps structure builds out of frames the player sees, and behind
    /// a load there are none, so lifting it lets the whole level arrive in a few frames rather than a few
    /// seconds -- and lets what is lit once at load be lit against all of it.
    /// </remarks>
    void SetTraceAdmission(bool unlimited)
    {
        _ = unlimited;
    }

    /// <summary>Stops, or resumes, building the traced world from the frames' draws.</summary>
    /// <param name="paused">Whether draws are kept out of the traced world.</param>
    /// <remarks>
    /// For a picture nothing traces -- the menu's lite backdrop. Every mesh admitted there is an
    /// acceleration structure built, and then forgotten at the next swap, for a level nobody will play.
    /// Resumed, the world fills from the next frame's draws as it does at any load.
    /// </remarks>
    void SetTracePaused(bool paused)
    {
        _ = paused;
    }

    /// <summary>Sets the seed for the following draws' hashed alpha test, zero for the hard cut-out.</summary>
    /// <param name="seed">
    /// Zero draws cut-outs with a hard per-pixel test. A value above zero turns on the hashed test, whose
    /// dithered coverage the temporal resolve averages into a smooth edge; vary it each frame so successive
    /// frames dither differently and the average converges.
    /// </param>
    /// <remarks>Device state, like <see cref="SetLightmapScale"/>: it spans a run of draws.</remarks>
    void SetAlphaHash(float seed);

    /// <summary>Sets how much texture alpha a cut-out draw needs to survive, for the following draws.</summary>
    /// <param name="reference">
    /// The threshold, out of 255. A fragment whose texture alpha is <b>below</b> it is discarded;
    /// <see cref="DefaultAlphaReference"/> is the port's long-standing half and zero turns the test off.
    /// </param>
    /// <remarks>
    /// <para>
    /// A byte rather than a fraction because that is the original's own type:
    /// <c>D3DRS_ALPHAREF</c> takes an integer out of 255, and I.G.I. 2 sets it to exactly two values —
    /// <b>64</b> for cut-outs generally (<c>Direct3DRender_SetBlendStateAlphaTest</c>,
    /// <c>igi2ed 0x56C960</c>) and <b>32</b> for foliage, which lowers it so the thin edges of a leaf
    /// sprite survive (<c>ForestRender_SetStates_Foliage</c>, <c>0x57BDF0</c>). The comparison there is
    /// <c>D3DCMP_GREATEREQUAL</c>, so the test is "alpha at least the reference" and this discards below
    /// it, which is the same rule.
    /// </para>
    /// <para>
    /// It matters more than a threshold usually does. The port had it hard coded at a half, which is
    /// neither of the original's numbers: on I.G.I. 1's foliage, whose texture alpha is strictly nought
    /// or 255, any threshold in between gives the same picture and the constant was harmless. I.G.I. 2's
    /// is not — a leaf sprite's alpha ramps, and testing at a half erodes it to a fraction of its
    /// authored shape.
    /// </para>
    /// <para>
    /// Device state, like <see cref="SetAlphaHash"/>: it spans a run of draws and must be set back
    /// after them. A backend may treat it as part of the pipeline, so callers should change it in runs
    /// rather than per draw.
    /// </para>
    /// </remarks>
    void SetAlphaReference(byte reference);

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

    /// <summary>Sets how an up-facing surface's roughness differs from a wall's.</summary>
    /// <param name="gloss">
    /// A multiplier on an up-facing surface's roughness, up to two: below 1 smooths floors so they reflect,
    /// above 1 roughens them so they shine less than the walls; 1 leaves every surface as its material said.
    /// </param>
    void SetFloorGloss(float gloss);

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

    /// <summary>Sets how wet the world is, which the lighting pass applies to every surface it shades.</summary>
    /// <param name="wetness">The state of the ground, or <see cref="WetnessSettings.Dry"/> for none.</param>
    /// <remarks>
    /// <para>
    /// Applied inside the deferred lighting pass, between unpacking the G-buffer and shading it: the
    /// albedo is darkened, the roughness collapsed and the normal replaced by the water's, so the sun,
    /// the lamps, the sky and the bounce all light the wet surface rather than the dry one underneath.
    /// See <see cref="WetnessSettings"/> for why it cannot be done in the G-buffer instead.
    /// </para>
    /// <para>
    /// A backend that has not implemented it does nothing, which is why this has a body: adding standing
    /// water to one renderer should not stop the other one compiling, and a device that ignores it says
    /// so through <see cref="GraphicsCapabilities.UnimplementedSettings"/> where the shell can report it.
    /// </para>
    /// </remarks>
    void SetWetness(in WetnessSettings wetness);

    /// <summary>Sets the top-down map of the level's roofs, which decides what the rain reaches.</summary>
    /// <param name="map">
    /// The raster, packed as <see cref="ShelterMapTransform"/> describes, or
    /// <see cref="TextureHandle.None"/> for a level with no map.
    /// </param>
    /// <param name="transform">
    /// How to read it, or <see cref="ShelterMapTransform.None"/> when nothing is sheltered. Set once
    /// when a mission loads: the map is a property of the level rather than of the frame.
    /// </param>
    /// <remarks>
    /// Ground under a roof stays dry however hard it is raining outside — without this the floor of
    /// every building in the level pools with water while its own roof keeps the rain off it. Both the
    /// shading and the reflection march read it, and they must read the same one: a reflection that
    /// disagreed with the shading about where the water is would land beside it.
    /// </remarks>
    void SetShelterMap(TextureHandle map, in ShelterMapTransform transform);

    /// <summary>Sets what surfaces reflect and how far the frame is searched to find it.</summary>
    /// <param name="settings">The search, or <see cref="ReflectionSettings.Off"/> for none.</param>
    /// <remarks>
    /// Applied after the lighting and before the fog: a reflection is of the lit world, and it belongs
    /// behind the air rather than in front of it.
    /// </remarks>
    void SetReflections(in ReflectionSettings settings);

    /// <summary>Hands the backend the surface cache, so a traced ray can be turned into light.</summary>
    /// <param name="binding">
    /// The cards, the grid and the lighting atlas, or <see cref="SurfaceCacheBinding.None"/> while the
    /// level has no cache — during loading, or on a mission whose cards failed to build.
    /// </param>
    /// <remarks>
    /// Called every frame rather than once per mission, because the camera offset in it moves. Everything
    /// else is a property of the level and is simply repeated; the backend stores the struct and resolves
    /// nothing until a pass that traces actually runs.
    /// </remarks>
    void SetSurfaceCache(in SurfaceCacheBinding binding);

    /// <summary>Hands over the level's distance field, for the passes that march it.</summary>
    /// <param name="binding">The clipmaps and their boxes, or <see cref="DistanceFieldBinding.None"/>.</param>
    /// <remarks>
    /// <para>
    /// The counterpart to <see cref="SetSurfaceCache"/>, and handed over the same way and at the same
    /// point: every frame, because the boxes are camera-relative and move with the camera, with the
    /// backend storing the struct and resolving nothing until a pass that marches actually runs.
    /// </para>
    /// <para>
    /// This exists so a reflection can find a hit on an adapter that cannot trace. The field is the only
    /// description of the world a shader can walk without an acceleration structure, and it lived
    /// entirely on the game's side until now — which is why glass without ray tracing reflected the sky
    /// and nothing else. A no-op by default: a backend that marches nothing has nowhere to put it.
    /// </para>
    /// </remarks>
    void SetDistanceField(in DistanceFieldBinding binding)
    {
    }

    /// <summary>Hands over the authored colour grade, or clears it.</summary>
    /// <param name="table">The table, or <see langword="null"/> to forget the one held.</param>
    /// <remarks>
    /// Separate from <see cref="PostProcessSettings.ColourLookup"/>, and called only when the grade
    /// changes — which in practice is when the mission does. The table is a volume texture upload; how
    /// much of it to apply is a number per frame.
    /// </remarks>
    void SetColourLookup(ColourLookupTable? table);

    /// <summary>Opens the surface cache atlas for capture.</summary>
    /// <param name="width">The atlas width in texels.</param>
    /// <param name="height">The atlas height in texels.</param>
    /// <returns><see langword="true"/> when the atlas is ready and cards may be drawn into it.</returns>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="BeginSceneCapture"/>, which describes a frame. This describes an atlas
    /// that outlives every frame: each model is drawn into it a handful of times at load, through an
    /// orthographic camera per card, and read for the rest of the level.
    /// </para>
    /// <para>
    /// Clears the whole atlas, including the gutters between pages, which no card ever draws into and a
    /// linear fetch near a page edge reaches.
    /// </para>
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">A capture is already open, or a dimension is not positive.</exception>
    bool BeginCardCapture(int width, int height);

    /// <summary>Restricts the following draws to one card's page of the atlas.</summary>
    /// <param name="x">The page's left edge in texels.</param>
    /// <param name="y">The page's bottom edge in texels.</param>
    /// <param name="width">The page's width in texels.</param>
    /// <param name="height">The page's height in texels.</param>
    /// <remarks>
    /// Scissors as well as sets the viewport. Geometry outside the card's orthographic box is clipped in
    /// depth but not sideways, so without the scissor a model wider than its card bleeds into whichever
    /// page was packed beside it.
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">No card capture is open.</exception>
    void SetCardPage(int x, int y, int width, int height);

    /// <summary>Closes the card capture.</summary>
    /// <exception cref="GraphicsDeviceException">No card capture is open.</exception>
    void EndCardCapture();

    /// <summary>Reads one of the card atlas's targets back into memory.</summary>
    /// <param name="attachment">Which target: 0 albedo, 1 normal, 2 material, 3 depth.</param>
    /// <param name="destination">Receives the texels, row-major and top row first.</param>
    /// <remarks>
    /// <b>For looking at the atlas, and that is not a luxury.</b> A capture is the kind of thing only an
    /// eye can judge: wrong winding shows as models captured inside-out, a wrong card rotation as
    /// mirrored pages, a missing scissor as pages bleeding into each other — and all three produce a
    /// perfectly plausible atlas full of perfectly plausible surfaces.
    /// <para>
    /// Synchronising, so it belongs at load or behind a developer switch rather than in a frame.
    /// </para>
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">There is no atlas, or the span is too small.</exception>
    void ReadCardAtlas(int attachment, Span<Bgra32> destination);

    /// <summary>Gets the atlas texture holding each card's surface colour.</summary>
    TextureHandle CardAlbedo { get; }

    /// <summary>Gets the atlas texture holding each card's model-space normal.</summary>
    TextureHandle CardNormal { get; }

    /// <summary>Gets the atlas texture holding each card's roughness, metallic and emissive.</summary>
    TextureHandle CardMaterial { get; }

    /// <summary>Gets the atlas texture holding how far behind its card each surface sits.</summary>
    /// <remarks>
    /// A fraction of the card's own depth. Without it a card records only where on its plane a surface
    /// lies, and the plane is the model's bounding box face — nowhere near the surface for anything but
    /// a flat panel.
    /// </remarks>
    TextureHandle CardDepth { get; }

    /// <summary>Gets the current scene target's depth buffer, or <see cref="TextureHandle.None"/>.</summary>
    /// <remarks>
    /// The bridge the screen probe gather crosses: a pass in the game layer needs the frame's per-pixel
    /// depth to place probes and reconstruct where each pixel is in the world. Non-linear device depth,
    /// turned into a view distance with the same near and far the deferred lighting uses.
    /// </remarks>
    TextureHandle SceneDepth { get; }

    /// <summary>Gets the current scene target's normal buffer, or <see cref="TextureHandle.None"/>.</summary>
    /// <remarks>
    /// View-space normals, encoded to the zero-to-one range the way the G-buffer writes them. A consumer
    /// in world space has to lift them through the view-to-world rotation itself.
    /// </remarks>
    TextureHandle SceneNormal { get; }

    /// <summary>Gets the frame's motion field, or <see cref="TextureHandle.None"/> where there is none yet.</summary>
    /// <remarks>
    /// Per pixel, the unjittered screen position now less the one the same surface had last frame, in UV: the
    /// camera's motion, with every moving object's own stamped over it. Asked for once the G-buffer is drawn, the
    /// device derives it on the spot, so a temporal pass in the game layer can find where a moving car's surface
    /// really came from instead of where the camera alone would put it.
    /// </remarks>
    TextureHandle SceneVelocity { get; }

    /// <summary>Gets the current scene target's lit colour, or <see cref="TextureHandle.None"/>.</summary>
    /// <remarks>
    /// What the deferred lighting wrote, before tone mapping. The screen probe gather reads it to add its
    /// indirect term onto the frame, compositing the surface cache's bounce over the direct light.
    /// </remarks>
    TextureHandle SceneColour { get; }

    /// <summary>Copies the sky's irradiance harmonics into <paramref name="coefficients"/>.</summary>
    /// <param name="coefficients">Room for 28 floats: seven vectors of four.</param>
    /// <returns><see langword="true"/> when the backend has an atmosphere to report.</returns>
    /// <remarks>
    /// The seven coefficient vectors of the deferred sky term — third-order harmonics of the sky's light,
    /// cosine-convolved with the Lambert one-over-pi folded in, exactly as the lighting pass consumes
    /// them. A game-layer pass that traces its own rays evaluates these along each escaping ray so its
    /// sky matches the sky the lighting shows, one frame behind (the values are read back from the
    /// device). A backend with no atmosphere tables returns <see langword="false"/> and the caller keeps
    /// whatever flat approximation it has.
    /// </remarks>
    bool TryGetSkyIrradiance(Span<float> coefficients);

    /// <summary>Feeds a screen probe gather's indirect light into the deferred lighting.</summary>
    /// <param name="indirect">The probe atlas of indirect irradiance, covering the frame.</param>
    /// <param name="strength">How strongly to apply it. Zero, or an invalid handle, is a no-op.</param>
    /// <param name="fullResolution">
    /// When set, the handle is a full-resolution atlas the temporal resolve already upsampled, read straight
    /// per pixel; when clear it is the sparse probe atlas the deferred fill upsamples itself.
    /// </param>
    /// <remarks>
    /// Set while the G-buffer is live, before the geometry pass closes: the deferred lighting reads it
    /// as its diffuse fill in place of the flat ambient, where the surface's own albedo and the light
    /// scale are already applied -- so this is not a post-pass over the lit frame but part of the shade
    /// itself, in the renderer's <c>albedo * E</c> convention with metals excluded. A no-op when the
    /// target is not deferred or the strength is zero, so a preset that leaves it off pays nothing.
    /// </remarks>
    void SetScreenIndirect(TextureHandle indirect, float strength, bool fullResolution = false);

    /// <summary>Sets the level's real-time local lights for the next deferred lighting pass.</summary>
    /// <param name="lights">
    /// The lights, in VIEW space; a set longer than <see cref="DeferredLocalLight.Maximum"/> is
    /// truncated. View space because the level sits tens of kilometres from the world origin, where a
    /// float has metres of resolution left: the camera-relative subtract has to happen in double
    /// precision on the game's side, and by the time it arrives here it has already happened.
    /// </param>
    /// <remarks>
    /// <para>
    /// Held rather than acted on, like <see cref="SetScreenIndirect"/>: this records what the next
    /// lighting pass folds in, and the pass clears it after, so a frame that sets none lights from none.
    /// Called each frame while the G-buffer is live, before the geometry pass closes.
    /// </para>
    /// <para>
    /// These are the lamps a level implies and never declares — see the game layer's lamp detection.
    /// They are what lights a windowless interior: there is no daylight to gather in a sealed room, so
    /// no amount of exposure or bounce can find light that has no source, and this is the source.
    /// </para>
    /// </remarks>
    void SetLocalLights(ReadOnlySpan<DeferredLocalLight> lights);

    /// <summary>Hands over the level's distance field, for a renderer that shadows its lamps without tracing.</summary>
    /// <param name="field">The field, or <see langword="null"/> while there is none.</param>
    /// <remarks>
    /// A no-op by default: a renderer that traces its lamp shadows never reads it. See
    /// <see cref="LampOcclusionField"/>. Called every frame, so a field rebuilt or dropped is followed.
    /// </remarks>
    void SetLampOcclusionField(LampOcclusionField? field)
    {
    }

    /// <summary>Sets the player's flashlight for the next deferred lighting pass.</summary>
    /// <param name="light">The flashlight, in VIEW space as the lamps are, or <see cref="FlashlightLight.Off"/>.</param>
    /// <remarks>
    /// A no-op by default: a renderer with no deferred lighting -- the original's direct path, which vanilla
    /// draws -- has nothing to light it with. Held like <see cref="SetLocalLights"/> and cleared as the
    /// capture ends, so it is called every frame the light is on.
    /// </remarks>
    void SetFlashlight(in FlashlightLight light)
    {
    }

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

    /// <summary>Hands over the cube map reflective draws sample, or clears it.</summary>
    /// <param name="cube">The cube, or <see cref="TextureHandle.None"/> for none.</param>
    /// <remarks>
    /// <para>
    /// Device state rather than a texture unit, and that is a fact about this seam rather than about
    /// cube maps: a 3D draw here takes its diffuse and its lightmap as <see cref="DrawMesh"/> arguments,
    /// and the unit array <see cref="SetTexture"/> writes is read by the compute path alone. A
    /// reflection belongs with <see cref="SetShelterMap"/> and <see cref="SetReflections"/> — something
    /// the frame is told once and a run of draws then uses.
    /// </para>
    /// <para>
    /// A device with no cube reflects nothing; so does a draw made while none is set. That is the
    /// state every non-reflective draw is in, which is nearly all of them.
    /// </para>
    /// </remarks>
    void SetEnvironmentCube(TextureHandle cube);

    /// <summary>Creates a compute program from one of the shader library's modules.</summary>
    /// <param name="shaderModule">The compute module.</param>
    /// <param name="variant">Which compiled form to load.</param>
    /// <param name="name">A name for the program, used in diagnostics.</param>
    /// <returns>A handle to the program.</returns>
    /// <remarks>
    /// <para>
    /// A member of this seam that not every backend can honour. Compute needs GL 4.3, which desktop has
    /// and macOS -- frozen at 4.1 -- does not, so a caller must check
    /// <see cref="GraphicsCapabilities.SupportsCompute"/> and have a path that does not need it.
    /// </para>
    /// <para>
    /// The module was compiled by the build rather than at load, so a mistake in it is a failed build
    /// instead of a level that does not start. Programs are still created up front rather than lazily:
    /// a pipeline that cannot be built is a fault to find at load, not on the frame that first needed
    /// the pass.
    /// </para>
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">Compute is unsupported, or the pipeline failed.</exception>
    ComputeShaderHandle CreateComputeShader(ShaderModuleId shaderModule, ShaderVariant variant, string name);

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

    /// <summary>Replaces part of a storage buffer's contents.</summary>
    /// <typeparam name="T">The element type. Must be blittable.</typeparam>
    /// <param name="buffer">The buffer to write.</param>
    /// <param name="elementOffset">Where to start writing, in elements of <typeparamref name="T"/>.</param>
    /// <param name="data">The data to write there.</param>
    /// <exception cref="GraphicsDeviceException"><paramref name="buffer"/> is not a buffer of this device.</exception>
    void UpdateStorageBuffer<T>(StorageBufferHandle buffer, int elementOffset, ReadOnlySpan<T> data)
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

    /// <summary>Begins a GPU timing range owned by the desktop compositor.</summary>
    /// <remarks>Ranges must not overlap backend-owned rendering ranges.</remarks>
    void BeginStageTiming(RenderStage stage);

    /// <summary>Ends the desktop-owned GPU timing range.</summary>
    void EndStageTiming();

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

    /// <summary>Gets the rolling distribution of total timed GPU work per completed frame.</summary>
    GpuFrameTiming GpuTiming();

    /// <summary>Gets every labelled pass timed since timing began, the most expensive first.</summary>
    /// <returns>The breakdown within the stages; empty where nothing labels its passes.</returns>
    IReadOnlyList<RenderPassTiming> PassTimings() => [];

    /// <summary>Names the compute work recorded from here on in the timings, or stops naming it.</summary>
    /// <param name="label">The name, or <see langword="null"/> to go back to each kernel's own.</param>
    /// <remarks>
    /// One kernel can serve several callers -- the surface cache's relight, bounce and far refresh are one
    /// program -- and timed under the kernel's name they are one figure that cannot say which of them
    /// spiked. Costs nothing when nothing is being timed.
    /// </remarks>
    void SetComputeTimingLabel(string? label)
    {
        _ = label;
    }

    /// <summary>Gets what each labelled pass cost in the slowest share of the frames timed.</summary>
    /// <param name="fraction">The share: 0.01 is the frames behind the 1% low.</param>
    /// <param name="frames">How many frames that was.</param>
    /// <param name="averageMilliseconds">Their average summed GPU time.</param>
    /// <returns>The breakdown, the most expensive first; empty where nothing labels its passes.</returns>
    IReadOnlyList<RenderPassTiming> WorstFramePassTimings(double fraction, out int frames, out double averageMilliseconds)
    {
        frames = 0;
        averageMilliseconds = 0.0;
        return [];
    }

    /// <summary>Gets the whole GPU frame measured end to end, rather than summed from its stages.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="GpuTiming"/> adds up the stages, so it can only report work somebody remembered to
    /// bracket. A pass recorded outside every stage scope contributes to nothing and costs real time, and
    /// the totals stay quietly wrong -- which is how a millisecond of lamp-shadow tracing read as free
    /// here for an entire session. This is a single timestamp pair around the frame's whole command
    /// buffer, so the difference between the two is precisely the work no stage claims.
    /// </para>
    /// <para>
    /// Zero on a backend that does not measure it, which is how a caller should tell "no untimed work"
    /// (a small difference) from "not measured" (zero).
    /// </para>
    /// </remarks>
    GpuFrameTiming GpuMeasuredTiming();

    /// <summary>Marks the work recorded so far, to learn when the GPU has finished it.</summary>
    /// <returns>A marker that completes once the GPU has finished everything recorded before this call.</returns>
    /// <remarks>
    /// <para>
    /// Including the frame still being recorded. That work has not gone to the GPU yet -- it goes when the
    /// frame is presented, or flushed -- so a marker taken inside a frame is held until then and only
    /// begins to be answered once it has.
    /// </para>
    /// <para>
    /// The pacing signal for work spread across frames. The time a frame takes on the host cannot be one:
    /// the host records a frame and moves on, and until the frames in flight fill up nothing it times
    /// includes the GPU work it has queued. Sizing work by host frame time buried the GPU in the first few
    /// frames of a level's lighting convergence, and on a laptop's integrated GPU one frame's command
    /// buffer ran long enough for the system to kill it.
    /// </para>
    /// </remarks>
    IGpuWorkMarker MarkRecordedWork();

    /// <summary>Gets whether this frame's sun shadow will come from a ray trace rather than the cascades.</summary>
    /// <remarks>
    /// When it will, the cascaded shadow map is written and never read -- the deferred lighting samples
    /// the traced mask as the only sun shadow -- so the app can skip rasterising the cascades entirely
    /// and the picture cannot change. False by default, and false whenever the trace cannot actually run
    /// this frame (no ray support, no acceleration structure yet), in which case the cascades are the
    /// sun's only shadow and must keep rendering.
    /// </remarks>
    bool SunShadowTraced { get; }

    /// <summary>
    /// What the resolve is about to multiply the lit buffer by — the exposure a display-referred colour
    /// must be divided by to survive it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A forward draw that declares itself emissive writes straight into the SCENE-REFERRED lit buffer,
    /// which the resolve then exposes and tone maps. A colour authored in display terms and written raw
    /// is at the mercy of the eye adaptation: in a dark scene the exposure is high, and a saturated red
    /// sprite comes back pink because the red channel clips long before the other two stop rising.
    /// Dividing by this lands it at the colour it was asked for whatever the scene is doing — the same
    /// correction the first-person rig's lighting and the selection glow already take.
    /// </para>
    /// <para>
    /// One on a backend or a path that draws straight to a display-encoded target, where a colour
    /// already means what it says. Never zero.
    /// </para>
    /// </remarks>
    float SceneExposure { get; }

    /// <summary>Creates one asynchronous occlusion query.</summary>
    OcclusionQueryHandle CreateOcclusionQuery();

    /// <summary>Returns the latest non-blocking result; unknown queries are conservatively visible.</summary>
    bool OcclusionVisible(OcclusionQueryHandle query);

    /// <summary>Issues a bounding-box visibility test against depth already drawn this frame.</summary>
    void IssueOcclusionQuery(OcclusionQueryHandle query, in Matrix4f modelView, in OcclusionBounds bounds);

    /// <summary>Destroys an asynchronous occlusion query.</summary>
    void DestroyOcclusionQuery(OcclusionQueryHandle query);

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

    /// <summary>Binds a real-time radiance-cache volume in place of the baked field, or leaves the last one.</summary>
    /// <param name="red">The red-channel L1-SH volume, four coefficients per texel.</param>
    /// <param name="green">The green-channel volume.</param>
    /// <param name="blue">The blue-channel volume.</param>
    /// <param name="distance">The distance-moment volume, for the Chebyshev leak test.</param>
    /// <param name="origin">The world position of probe (0, 0, 0), in metres.</param>
    /// <param name="spacing">The distance between neighbouring probes, in metres.</param>
    /// <param name="countX">How many probes span X.</param>
    /// <param name="countY">How many probes span Y.</param>
    /// <param name="countZ">How many probes span Z.</param>
    /// <remarks>
    /// The volumes are owned by the pass that writes them each frame, so the device only references them --
    /// it never allocates or frees them, unlike the baked field it uploads. When bound they take precedence
    /// over the baked volume at the same sampler: the layout is identical, but the radiance is live and
    /// already coloured, so the device applies no sky tint to it. Call
    /// <see cref="ClearRealtimeProbeVolume"/> to fall back to the baked field.
    /// </remarks>
    void SetRealtimeProbeVolume(
        TextureHandle red,
        TextureHandle green,
        TextureHandle blue,
        TextureHandle distance,
        Vector3f origin,
        float spacing,
        int countX,
        int countY,
        int countZ);

    /// <summary>Stops sampling the real-time radiance cache, falling back to the baked field.</summary>
    void ClearRealtimeProbeVolume();

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

    /// <summary>Sets how the next meshes drawn blend between their two poses.</summary>
    /// <param name="currentBlend">
    /// How much of the <em>previous-pose</em> vertex stream is mixed into the rasterised position, 0
    /// to 1. Zero draws the current pose exactly — the value every static mesh is implicitly drawn
    /// with, since its previous stream is itself.
    /// </param>
    /// <param name="previousBlend">
    /// The same mix for the position reported as <em>last frame's</em>. The difference between the
    /// two blends is precisely the pose
    /// motion the frame reports: a fixed-tick simulation renders between two known poses, so last
    /// frame's vertex is a different mix of the same two streams, not a third buffer.
    /// </param>
    /// <remarks>
    /// Meaningful only for meshes from <see cref="CreateAnimatedMesh"/>; everywhere else both streams
    /// are one buffer and any blend of a value with itself is that value. Cleared by
    /// <see cref="ClearPoseInterpolation"/>.
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
    /// previous frame. Supplied in double precision: this world's coordinates run to tens of thousands
    /// of units, and the per-frame travel is often a fraction of one, so the backend must difference two
    /// positions of that magnitude to recover it. Doing that in single precision loses the travel to
    /// cancellation and smears the reprojection under translation; passing it double lets the backend
    /// keep the subtraction exact and narrow only the small result to float.
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
        Vector3d cameraPosition,
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

    /// <summary>Gets whether this frame's lamp shadows are marched without ray tracing and would read lamp maps.</summary>
    /// <remarks>
    /// Where it is <see langword="false"/> -- the lamps are traced, or not shadowed at all -- nothing need be
    /// drawn into the maps.
    /// </remarks>
    bool WantsLampShadowMaps => false;

    /// <summary>Gets how many lamps can hold a shadow map at once.</summary>
    int LampShadowMapSlots => 0;

    /// <summary>Says which lamp map each of this frame's lamps reads.</summary>
    /// <param name="slotPerLight">
    /// One per lamp, in the order <see cref="SetLocalLights"/> was given them: the slot, or -1 for none.
    /// </param>
    void SetLampShadowMapSlots(ReadOnlySpan<int> slotPerLight)
    {
        _ = slotPerLight;
    }

    /// <summary>Begins drawing the world into one lamp's shadow map, from the lamp.</summary>
    /// <param name="slot">The map, below <see cref="LampShadowMapSlots"/>.</param>
    /// <param name="position">Where the lamp's camera stands, in absolute world metres.</param>
    /// <param name="rotation">The light's rotation: its rows the right, up and forward axes.</param>
    /// <param name="projection">The perspective it looks through.</param>
    /// <returns><see langword="true"/> when the pass is open; end it with <see cref="EndShadowCascade"/>.</returns>
    /// <remarks>Like a cascade: depth only, outside a scene capture, before the frame is drawn.</remarks>
    bool BeginLampShadowMap(int slot, Vector3d position, Matrix3f rotation, in Matrix4f projection)
    {
        _ = slot;
        _ = position;
        _ = rotation;
        _ = projection;
        return false;
    }

    /// <summary>Sets the light the scene is lit by for this frame.</summary>
    /// <param name="lighting">The lighting model, sun and ambient.</param>
    void SetLighting(in SceneLighting lighting);

    /// <summary>Declares which part of the frame the following draws belong to.</summary>
    /// <param name="pass">The part of the frame.</param>
    /// <remarks>
    /// <para>
    /// The renderers themselves never call this; the shell does, around the groups of draws it already
    /// makes. That is what lets the backend shade an opaque surface into a G-buffer and a translucent
    /// one directly, without any renderer in the game learning that there are two ways to be drawn.
    /// </para>
    /// <para>
    /// The glazing's front layer is the one pass that may begin while another is open: it begins with the
    /// geometry pass still open and closes it, without the shading that ending the geometry pass would
    /// run -- the front layer has to be drawn between the two.
    /// </para>
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">
    /// No capture is in progress, or a pass is already open other than the geometry pass the front layer
    /// closes.
    /// </exception>
    void BeginPass(ScenePass pass);

    /// <summary>Ends the current pass.</summary>
    /// <exception cref="GraphicsDeviceException">No pass is open.</exception>
    void EndPass();

    /// <summary>Stops rendering into the offscreen target.</summary>
    /// <exception cref="GraphicsDeviceException">No capture is in progress, or a pass is still open.</exception>
    void EndSceneCapture();

    /// <summary>Drops everything a backend has accumulated about the level being left.</summary>
    /// <remarks>
    /// <para>
    /// Some of what a backend builds is discovered from the frames it draws rather than handed to it:
    /// the traceable copy of the world is assembled from geometry-pass draws over a level's first
    /// seconds, and it has no other way of learning that the level has gone. Destroying the meshes tells
    /// it that each one has gone, which is not the same thing -- the material records a structure's hits
    /// are looked up in are append-only by design, so a session that entered six missions carried six
    /// missions' worth of them and the seventh would have overflowed the buffer they live in.
    /// </para>
    /// <para>
    /// Called when a mission is released, before its meshes and textures are destroyed. It is not a
    /// device reset: nothing the front end is still drawing with is touched, and a backend with nothing
    /// accumulated does nothing.
    /// </para>
    /// </remarks>
    void ReleaseWorld();

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

    /// <summary>Puts part of a mesh into the traced world without drawing it.</summary>
    /// <param name="mesh">The mesh to admit.</param>
    /// <param name="indexOffset">The first index of the run.</param>
    /// <param name="indexCount">How many indices the run covers; a multiple of three.</param>
    /// <param name="modelView">The model-to-view transform, as <see cref="DrawMesh"/> takes it.</param>
    /// <param name="diffuse">The diffuse texture, which is what names the run's material to a ray.</param>
    /// <remarks>
    /// <para>
    /// For geometry that must be THERE for a ray and absent for the camera. The player's own body in
    /// first person is the case this exists for: it has to be in the acceleration structure or no
    /// reflected ray can strike it and no window can show the player standing at it, and it must not be
    /// in the G-buffer or the camera is inside its head.
    /// </para>
    /// <para>
    /// Only meaningful once a pass can shade a hit from the hit's own material. Before that, geometry the
    /// camera could not see had no colour available to it and admitting it only produced a silhouette of
    /// nothing.
    /// </para>
    /// <para>
    /// A no-op by default: a backend that traces nothing has nowhere to put this, and the callers are
    /// written so that skipping it costs only the reflection.
    /// </para>
    /// </remarks>
    /// <param name="firstPersonOnly">
    /// Whether only reflections may find it, as the first-person rig is: the player's own body, which the
    /// camera never sees. Every other ray -- the GI's, the shadows' -- passes through it, or an invisible
    /// occluder stands wherever the camera does and darkens whatever the player faces.
    /// </param>
    void SubmitTracedMesh(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        in Matrix4f modelView,
        TextureHandle diffuse,
        bool firstPersonOnly = false)
    {
    }

    /// <summary>Draws several ranges from one mesh with shared transform, texture and state.</summary>
    /// <remarks>
    /// Backends that support multi-draw submit the ranges together. Keeping this on the graphics seam
    /// lets terrain batch visible chunks without exposing OpenGL buffers or commands to the game.
    /// </remarks>
    void DrawMeshRanges(
        MeshHandle mesh,
        ReadOnlySpan<MeshDrawRange> ranges,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend);

    /// <summary>Offers a mesh to the ray-traced world for this frame only, as a shadow caster that is not drawn.</summary>
    /// <param name="mesh">A mesh from <see cref="CreateMeshStorage"/> whose first <paramref name="indexCount"/> indices are the caster.</param>
    /// <param name="modelView">The transform the mesh would be drawn with.</param>
    /// <param name="indexCount">How many indices, from the start of the mesh, make up the caster.</param>
    /// <remarks>
    /// <para>
    /// For geometry whose traceable shape is not what the rasteriser is handed: the terrain. Its pages
    /// hold every chunk ever streamed into them at every level of detail, at positions the vertex stage
    /// then slides towards the next level as the camera moves — so a structure built over a page casts
    /// from a surface that is not the one on screen, and the ground shadows itself in patches. The
    /// terrain instead writes the chunks it drew, at the positions it drew them, into a mesh of its own
    /// and offers that here each frame; the placement lasts one frame, and the structure is rebuilt
    /// whenever the mesh's contents change.
    /// </para>
    /// <para>
    /// A backend without a traced world ignores the call, which is the default implementation.
    /// </para>
    /// </remarks>
    void SubmitShadowCaster(MeshHandle mesh, in Matrix4f modelView, int indexCount);

    /// <summary>Gets whether <see cref="SubmitShadowCaster"/> would keep anything this frame.</summary>
    /// <remarks>
    /// Asked before building a caster at all, because building one is the expensive half: the terrain walks
    /// its whole octree from every direction to write it. False where nothing is traced -- a backend
    /// without a traced world, or the menu's lite backdrop, which pauses it -- and a profile of the menu
    /// found that walk taking a couple of seconds of the render thread for a caster the device then
    /// dropped.
    /// </remarks>
    bool AcceptsShadowCasters => false;

    /// <summary>Describes what the device holds in memory, for a memory report.</summary>
    /// <returns>Lines of text, or empty where there is nothing to say.</returns>
    string DescribeMemory() => string.Empty;

    /// <summary>Gives back memory held for reuse but idle, once a load is over.</summary>
    void TrimMemory()
    {
    }

    /// <summary>Draws a glowing selection outline around a mesh.</summary>
    /// <param name="mesh">The mesh to outline.</param>
    /// <param name="ranges">
    /// Every index range making up the model. All of them at once, not one call each: the interior is
    /// masked out before the glow is drawn, and a model outlined a piece at a time would show each
    /// later piece's glow lying across the pieces before it.
    /// </param>
    /// <param name="modelView">The model-to-view transform the mesh itself would be drawn with.</param>
    /// <param name="colour">The glow's colour; its alpha scales the whole effect.</param>
    /// <param name="radius">
    /// How far the glow reaches past the silhouette, <b>in pixels</b> — not in world units, so a
    /// distant object keeps a mark of the same visible size instead of collapsing into a hairline.
    /// </param>
    /// <param name="throughWalls">
    /// Whether the glow ignores the depth buffer. Set, it marks something the reader cannot otherwise
    /// see; clear, it is occluded by the world like any other geometry.
    /// </param>
    /// <exception cref="ArgumentException">The handle is unknown, or a range lies outside the mesh.</exception>
    /// <remarks>
    /// <para>
    /// An inverted hull expanded in screen space, drawn as a stack of shells that fade outwards, with
    /// the model's own silhouette masked out so only the rim survives. It costs a handful of extra
    /// draws of geometry the caller already has, and generates nothing.
    /// </para>
    /// <para>
    /// Colour is never written where the model is, and depth is never written except by the mask, so
    /// the effect cannot occlude what is drawn after it. The mask does leave the near plane written
    /// across the model's silhouette, which is why this belongs late in a frame rather than in the
    /// middle of the world pass.
    /// </para>
    /// <para>
    /// A posed mesh is outlined in the pose currently set through
    /// <see cref="SetPoseInterpolation"/>, so an animating character's glow tracks its limbs.
    /// </para>
    /// </remarks>
    void DrawOutline(
        MeshHandle mesh,
        ReadOnlySpan<MeshDrawRange> ranges,
        in Matrix4f modelView,
        Bgra32 colour,
        float radius,
        bool throughWalls);
}
