using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>
/// The renderer seam: everything the engine may ask of a graphics backend.
/// </summary>
/// <remarks>
/// <para>
/// The member names deliberately mirror the original's <c>RenderMode</c> method names so the mapping
/// stays checkable — <see cref="IDisplayDevice.SetMode"/> is <c>DisplaySetMode</c>, <see cref="IDisplayDevice.SwapBuffers"/> is
/// <c>DisplaySwapBuffers</c>, <see cref="IDisplayDevice.SetViewport"/> is <c>ClipWindowPrimitive</c>,
/// <see cref="IDisplayDevice.BlitPixels"/> is <c>BlitPrimitive</c>, and the texture members are the twelve-method
/// texture set.
/// </para>
/// <para>
/// <b>Frame order.</b> The original presents at the <em>start</em> of <c>DisplaySwapBuffers</c> and
/// begins the new scene at its end, so a frame is <c>SwapBuffers</c> → <c>Clear</c> → draw. Backends
/// must keep that order: it is what makes the game loop's one-render-per-tick interleave land on the
/// same frame boundaries.
/// </para>
/// <para>
/// <b>Threading.</b> Every member must be called from the thread that created the device.
/// </para>
/// </remarks>
public interface IGraphicsDevice :
    IDisplayDevice,
    ITextureDevice,
    IDraw2DDevice,
    IMeshDevice,
    IDrawStateDevice,
    ISceneEnvironmentDevice,
    ISceneLightingDevice,
    ITracedWorldDevice,
    ICardCaptureDevice,
    IScenePassDevice,
    IMeshDrawDevice,
    IComputeDevice,
    IGpuTimingDevice,
    IOcclusionQueryDevice,
    ITemporalDevice,
    ILatencyDevice,
    IFrameGenerationDevice,
    IShadowDevice,
    IDisposable
{
    /// <summary>Replaces an animated mesh's vertices; the ones replaced become its previous pose.</summary>
    /// <param name="mesh">A mesh from <see cref="IMeshDevice.CreateAnimatedMesh"/>.</param>
    /// <param name="vertices">The new pose. Must be exactly as many vertices as the mesh was created with.</param>
    /// <exception cref="ArgumentException">
    /// The handle is unknown, was not created animated, or the count differs.
    /// </exception>
    void UpdateAnimatedMesh(MeshHandle mesh, ReadOnlySpan<Vertex3D> vertices);

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
    /// <para>Device state, like <see cref="IDrawStateDevice.SetLightmapScale"/>: it spans a run of draws and must be cleared after them.</para>
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
    /// <para>Device state, like <see cref="IDrawStateDevice.SetLightmapScale"/>: it spans a run of draws.</para>
    /// </remarks>
    void SetLightmapModulates(bool modulate)
    {
        _ = modulate;
    }

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
    /// Only the deferred path reads it, and only when <see cref="IDrawStateDevice.SetMaterialDetail"/> asks for a
    /// non-zero strength. Nothing under the original's path is affected.
    /// </para>
    /// </remarks>
    /// <exception cref="GraphicsDeviceException"><paramref name="diffuse"/> is not a texture of this device.</exception>
    void SetTextureDetail(TextureHandle diffuse, TextureHandle detail);

    /// <summary>Gets the frame's motion field, or <see cref="TextureHandle.None"/> where there is none yet.</summary>
    /// <remarks>
    /// Per pixel, the unjittered screen position now less the one the same surface had last frame, in UV: the
    /// camera's motion, with every moving object's own stamped over it. Asked for once the G-buffer is drawn, the
    /// device derives it on the spot, so a temporal pass in the game layer can find where a moving car's surface
    /// really came from instead of where the camera alone would put it.
    /// </remarks>
    TextureHandle SceneVelocity { get; }

    /// <summary>Binds a storage buffer to a numbered slot for the next dispatch.</summary>
    /// <param name="slot">The binding point, matching the shader's <c>binding =</c> layout qualifier.</param>
    /// <param name="buffer">The buffer, or the null handle to unbind.</param>
    void BindStorageBuffer(int slot, StorageBufferHandle buffer);

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

    /// <summary>Gets whether this frame's lamp shadows are marched without ray tracing and would read lamp maps.</summary>
    /// <remarks>
    /// Where it is <see langword="false"/> -- the lamps are traced, or not shadowed at all -- nothing need be
    /// drawn into the maps.
    /// </remarks>
    bool WantsLampShadowMaps => false;

    /// <summary>Gets how many coarse lamp maps the surface cache's relight can hold: six to a lamp.</summary>
    /// <remarks>
    /// A second, small bank beside the lamp shadow maps, for every lamp in the level rather than the frame's
    /// nearest: what the relight asks of a lamp is whether its light reaches a patch of wall at all, which a
    /// coarse map answers, and it asks it of lamps far from the camera that the shadow maps never cover.
    /// </remarks>
    int LampBounceMapSlots => 0;

    /// <summary>Gets the coarse lamp maps as one array, for a compute pass to read through <see cref="ITextureDevice.SetTexture"/>.</summary>
    /// <remarks><see cref="TextureHandle.None"/> on a device that draws none.</remarks>
    TextureHandle LampBounceMapArray => TextureHandle.None;

    /// <summary>Brings the captured scene to the back buffer.</summary>
    /// <param name="post">What to do to it on the way.</param>
    /// <remarks>
    /// With <see cref="PostProcessSettings.Identity"/> and a non-HDR target this is a straight copy,
    /// which is what keeps the vanilla preset identical to the build that had no offscreen path.
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">Nothing has been captured this frame.</exception>
    void ResolveScene(in PostProcessSettings post);

    /// <summary>Draws camera-facing tracer strips through the tracer shader.</summary>
    /// <param name="mesh">The strips: u along each one (0 tail, 1 head), v across it, the vertex colour the tracer's
    /// colour at the display encoding and its alpha the brightness at that end.</param>
    /// <param name="indexOffset">The first index to draw.</param>
    /// <param name="indexCount">How many indices to draw.</param>
    /// <param name="modelView">The strips' transform into view space.</param>
    /// <param name="style">Which look: the original's line or the remaster's streak.</param>
    /// <param name="radiance">The remastered streak's radiance at full brightness; the retail line ignores it.</param>
    /// <remarks>
    /// Tested against the scene's depth and never writing it, never fogged, and always adding its coverage to the
    /// target's alpha, which is the marker the temporal resolve reads to keep a one-tick effect out of its history.
    /// A device without the shader draws the strips as an ordinary blended mesh.
    /// </remarks>
    void DrawTracers(MeshHandle mesh, int indexOffset, int indexCount, in Matrix4f modelView, TracerStyle style, float radiance) =>
        DrawMesh(
            mesh,
            indexOffset,
            indexCount,
            modelView,
            TextureHandle.None,
            TextureHandle.None,
            style == TracerStyle.Retail ? BlendMode.AlphaBlend : BlendMode.Additive);

    /// <summary>Draws part of a sheet of I.G.I. 2's water, as its own vertex shader lights it.</summary>
    /// <param name="mesh">The sheet's mesh: a position and a normal a vertex, with how shallow the water is there in the diffuse U.</param>
    /// <param name="indexOffset">The first index to draw.</param>
    /// <param name="indexCount">How many indices to draw; a multiple of three.</param>
    /// <param name="modelView">The model-to-view transform, from <see cref="Camera3D.CreateModelView(Vector3d, Matrix3f)"/>.</param>
    /// <param name="cube">The baked reflection, from <see cref="ITextureDevice.CreateCubeTexture"/>.</param>
    /// <param name="shading">What the sheet is lit by.</param>
    /// <remarks>
    /// Its own draw rather than <see cref="IMeshDrawDevice.DrawMesh"/> with a cube set: the water's colour is a Fresnel blend of
    /// the reflection and its own light, and its alpha how much of the scene behind shows, which no texture of
    /// the world pipeline carries. Tested and written into depth, blended <c>ONE</c> and <c>SRCALPHA</c>, and
    /// fogged against the frame's fog. A device that cannot draw it draws nothing, and so does a draw without a
    /// cube: the original returns before drawing a sheet whose reflection is not valid.
    /// </remarks>
    void DrawWater(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        in Matrix4f modelView,
        TextureHandle cube,
        in WaterShading shading)
    {
    }

    /// <summary>Describes what the device holds in memory, for a memory report.</summary>
    /// <returns>Lines of text, or empty where there is nothing to say.</returns>
    string DescribeMemory() => string.Empty;

    /// <summary>Gives back memory held for reuse but idle, once a load is over.</summary>
    void TrimMemory()
    {
    }
}
