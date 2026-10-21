using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.Shaders;

namespace OpenIGI.Presentation.Lighting;

/// <summary>
/// The on-screen gather that carries the surface cache to the frame: screen probes traced and
/// accumulated over frames, the one-ray-a-pixel form ray reconstruction denoises, and the spatial
/// filter and full-resolution resolve that follow them.
/// </summary>
/// <remarks>
/// Its programs are compiled on first use, so a level that never turns the gather on never pays for
/// them. <see cref="SurfaceCacheLighting"/> exposes every pass.
/// </remarks>
internal sealed class ScreenProbeGather
{
    private readonly IGraphicsDevice _device;
    private readonly SurfaceCacheBindings _bindings;
    private readonly Action _ensureTraceMode;
    private readonly StorageBufferHandle _pages;
    private readonly TextureHandle _final;
    private readonly int _width;
    private readonly int _height;

    // The screen probe gather's shaders, compiled on first use rather than at construction: a level that
    // never turns the on-screen gather on never pays for it. The plain one produces a raw estimate for
    // the diagnostic dump; the temporal one reprojects and accumulates it for the live frame.
    private ComputeShaderHandle _screenGather;
    private ComputeShaderHandle _screenGatherTemporal;

    // The one-ray-a-pixel form DLSS ray reconstruction denoises; traced only, so it is never built for the march.
    private ComputeShaderHandle _pixelGather;
    private ComputeShaderHandle _screenGatherFilter;
    private ComputeShaderHandle _screenResolve;

    /// <summary>Initializes the gather over the surface cache it reads.</summary>
    /// <param name="device">The device to dispatch on.</param>
    /// <param name="bindings">The bindings every dispatch shares.</param>
    /// <param name="ensureTraceMode">Brings every tracing pass up to the current trace mode.</param>
    /// <param name="pages">The page records.</param>
    /// <param name="final">The final lighting atlas a hit reads.</param>
    /// <param name="width">The atlas width in texels.</param>
    /// <param name="height">The atlas height in texels.</param>
    internal ScreenProbeGather(
        IGraphicsDevice device,
        SurfaceCacheBindings bindings,
        Action ensureTraceMode,
        StorageBufferHandle pages,
        TextureHandle final,
        int width,
        int height)
    {
        _device = device;
        _bindings = bindings;
        _ensureTraceMode = ensureTraceMode;
        _pages = pages;
        _final = final;
        _width = width;
        _height = height;
    }

    /// <summary>Gathers indirect light for a grid of screen probes and writes it to a probe atlas.</summary>
    /// <param name="shadows">The clipmaps to trace against. Without them there is nothing to gather.</param>
    /// <param name="sceneDepth">The frame's device depth, from the G-buffer.</param>
    /// <param name="sceneNormal">The frame's view-space normals, from the G-buffer.</param>
    /// <param name="unproject">The projection's scales and planes, for turning depth into a view position.</param>
    /// <param name="viewToWorld">The view-to-world rotation, matching what the deferred pass draws with.</param>
    /// <param name="cameraToCache">The camera's world position relative to this pass's origin.</param>
    /// <param name="screenWidth">The frame's width in pixels.</param>
    /// <param name="screenHeight">The frame's height in pixels.</param>
    /// <param name="probeStep">How many pixels lie between probes each way.</param>
    /// <param name="output">The probe atlas to write, sized to the screen divided by the step.</param>
    /// <remarks>
    /// <para>
    /// L6's screen probe gather, the pass that finally carries the surface cache to the screen. Each
    /// probe reads its pixel's depth and normal from the G-buffer, rebuilds where that pixel sits in the
    /// world -- rebased onto this pass's origin so it lands in the same space the trace works in -- and
    /// gathers a hemisphere of rays through the shared software trace, reading the surface cache's final
    /// lighting at each hit. What comes back is the pixel's indirect irradiance, which a later pass
    /// interpolates across the probes and folds into the frame.
    /// </para>
    /// <para>
    /// A probe grid rather than per pixel, because indirect light varies slowly and tracing every pixel
    /// would trace sixteen rays a pixel over the whole screen. Probes could be refined adaptively where
    /// depth or normal vary too fast for the grid to interpolate; that refinement is a later slice.
    /// </para>
    /// </remarks>
    internal void GatherScreen(
        GlobalDistanceField? shadows,
        TextureHandle sceneDepth,
        TextureHandle sceneNormal,
        in DepthUnprojection unproject,
        Matrix3f viewToWorld,
        Vector3f cameraToCache,
        int screenWidth,
        int screenHeight,
        int probeStep,
        TextureHandle output)
    {
        if (shadows is null || _bindings.Lookup is null || screenWidth <= 0 || screenHeight <= 0)
        {
            return;
        }

        int step = Math.Max(probeStep, 1);

        _ensureTraceMode();
        if (!_screenGather.IsValid)
        {
            _screenGather = _device.CreateComputeShader(
                ShaderModuleId.CardsScreenGather,
                new ShaderVariant(_bindings.TracesRays ? "rayquery" : "software"),
                "cards.screen");
        }

        _device.BindStorageBuffer(0, _pages);
        _device.BindStorageBuffer(3, _bindings.Lookup.Cells);
        _device.BindStorageBuffer(4, _bindings.Lookup.Pages);
        _device.BindImage(0, output, ImageAccess.Write);

        _device.SetTexture(1, _device.CardAlbedo);
        _device.SetTexture(4, _device.CardDepth);
        _device.SetTexture(6, _final);
        _device.SetTexture(7, sceneDepth);
        _device.SetTexture(8, sceneNormal);

        // Bound so the sampler is valid, but the trace is disabled below: the dump stays a deterministic
        // single-frame surface-cache gather, with no dependence on a previous frame's colour.
        _device.SetTexture(11, _device.SceneColour);

        _device.SetComputeUniform(_screenGather, "uMaxRayIntensity", _bindings.ExposedRayCap());
        _device.SetComputeUniform(_screenGather, "uScreenTraceSteps", 0);
        _device.SetComputeUniform(_screenGather, "uScreenTraceDistance", 0f);
        _device.SetComputeUniform(_screenGather, "uScreenThickness", 0f);
        _device.SetComputeUniform(_screenGather, "uSkyColour", default(Vector3f));
        _device.SetComputeUniform(_screenGather, "uGroundSun", default(Vector3f));

        // The dump shows traced bounce alone, so the harmonics are forced off along with the flat sky,
        // and the bounce is read at its physical strength whatever the live calibration says.
        _device.SetComputeUniform(_screenGather, "uSkyShValid", 0);
        _device.SetComputeUniform(_screenGather, "uBounceScale", 1f);
        _device.SetComputeUniform(_screenGather, "uDebugSource", RenderCVars.GiSource.Value);
        _bindings.BindTerrainHeights(_screenGather);

        _device.SetComputeUniform(_screenGather, "uAtlasSize", new Vector3f(_width, _height, 0f));
        _device.SetComputeUniform(_screenGather, "uLookupBuckets", SurfaceCacheLookup.Buckets);
        _device.SetComputeUniform(_screenGather, "uLookupMin", _bindings.Lookup.Minimum);
        _device.SetComputeUniform(_screenGather, "uLookupCell", SurfaceCacheLookup.CellMetres);

        _device.SetComputeUniform(
            _screenGather, "uUnproject", unproject.XScale, unproject.YScale, unproject.Near, unproject.Far);
        _device.SetComputeUniform(_screenGather, "uViewToWorld0", viewToWorld.Column1);
        _device.SetComputeUniform(_screenGather, "uViewToWorld1", viewToWorld.Column2);
        _device.SetComputeUniform(_screenGather, "uViewToWorld2", viewToWorld.Column3);
        _device.SetComputeUniform(_screenGather, "uCameraToCache", cameraToCache);
        _device.SetComputeUniform(_screenGather, "uScreenWidth", screenWidth);
        _device.SetComputeUniform(_screenGather, "uScreenHeight", screenHeight);
        _device.SetComputeUniform(_screenGather, "uProbeStep", step);

        _bindings.BindShadows(_screenGather, shadows);

        int gridX = (screenWidth + step - 1) / step;
        int gridY = (screenHeight + step - 1) / step;

        _device.Dispatch(_screenGather, (gridX + 7) / 8, (gridY + 7) / 8);
        _device.MemoryBarrier(MemoryBarrierScope.Image | MemoryBarrierScope.TextureFetch);
    }

    /// <summary>Gathers one ray of indirect light at every pixel, unfiltered, for DLSS ray reconstruction.</summary>
    /// <param name="shadows">The clipmaps the ray falls back to past the traced world.</param>
    /// <param name="sceneDepth">The frame's device depth, from the G-buffer.</param>
    /// <param name="sceneNormal">The frame's normals, from the G-buffer.</param>
    /// <param name="unproject">The projection's scales and planes, for turning depth into a view position.</param>
    /// <param name="viewToWorld">The view-to-world rotation, matching what the deferred pass draws with.</param>
    /// <param name="cameraToCache">The camera's world position relative to this pass's origin.</param>
    /// <param name="screenWidth">The frame's width in pixels.</param>
    /// <param name="screenHeight">The frame's height in pixels.</param>
    /// <param name="skyColour">The sky's radiance, returned for a ray that escapes without a hit.</param>
    /// <param name="frame">The presented-frame count, which the white noise is keyed on.</param>
    /// <param name="output">The irradiance image to write, the frame's size.</param>
    /// <param name="sceneColour">The previous frame's lit scene, read at an on-screen hit.</param>
    /// <param name="screenTraceSteps">The screen trace's step cap, and its on/off (zero disables it).</param>
    /// <param name="screenTraceDistance">How far a screen-traced ray reaches, in metres.</param>
    /// <param name="screenThickness">The assumed surface thickness for the screen trace's hit test, in metres.</param>
    /// <param name="colourWorldToView">The world-to-view rotation <paramref name="sceneColour"/> was rendered with.</param>
    /// <param name="colourCameraToCache">The camera <paramref name="sceneColour"/> was rendered from, relative to this pass's origin.</param>
    /// <returns><see langword="true"/> when the gather ran; <see langword="false"/> where it cannot trace.</returns>
    /// <remarks>
    /// <para>
    /// The same gather as the probes' -- the traced world first, then the cache's light where it struck, the
    /// sky or the open ground on a miss -- with a probe at every pixel and a single ray from it. The probes'
    /// own denoising, their pooling, cadence, accumulation and resolve, is exactly what ray reconstruction
    /// replaces, and handed their output it would see a smooth, lagging signal it was not trained on.
    /// </para>
    /// <para>
    /// The screen comes first, as for the probes: a ray that lands on something in last frame's lit picture
    /// takes that light, read where the hit stood last frame. It is what carries a lamp's pool on the floor
    /// up onto the ceiling and a lit machine's glow onto the wall beside it, which the cards hold coarsely
    /// or not at all. Without it the interiors under ray reconstruction lost their bounce light.
    /// </para>
    /// </remarks>
    internal bool GatherPixels(
        GlobalDistanceField? shadows,
        TextureHandle sceneDepth,
        TextureHandle sceneNormal,
        in DepthUnprojection unproject,
        Matrix3f viewToWorld,
        Vector3f cameraToCache,
        int screenWidth,
        int screenHeight,
        Vector3f skyColour,
        uint frame,
        TextureHandle output,
        TextureHandle sceneColour,
        int screenTraceSteps,
        float screenTraceDistance,
        float screenThickness,
        Matrix3f colourWorldToView,
        Vector3f colourCameraToCache)
    {
        if (shadows is null || _bindings.Lookup is null || screenWidth <= 0 || screenHeight <= 0)
        {
            return false;
        }

        _ensureTraceMode();
        if (!_bindings.TracesRays)
        {
            return false;
        }

        if (!_pixelGather.IsValid)
        {
            _pixelGather = _device.CreateComputeShader(
                ShaderModuleId.CardsScreenGather, new ShaderVariant("pixel"), "cards.pixel");
        }

        _device.BindStorageBuffer(0, _pages);
        _device.BindStorageBuffer(3, _bindings.Lookup.Cells);
        _device.BindStorageBuffer(4, _bindings.Lookup.Pages);
        _device.BindImage(0, output, ImageAccess.Write);

        _device.SetTexture(1, _device.CardAlbedo);
        _device.SetTexture(4, _device.CardDepth);
        _device.SetTexture(6, _final);
        _device.SetTexture(7, sceneDepth);
        _device.SetTexture(8, sceneNormal);
        _device.SetTexture(11, sceneColour);

        _device.SetComputeUniform(_pixelGather, "uMaxRayIntensity", _bindings.ExposedRayCap());
        _device.SetComputeUniform(_pixelGather, "uScreenTraceSteps", Math.Max(screenTraceSteps, 0));
        _device.SetComputeUniform(_pixelGather, "uScreenTraceDistance", MathF.Max(screenTraceDistance, 0f));
        _device.SetComputeUniform(_pixelGather, "uScreenThickness", MathF.Max(screenThickness, 0f));
        _device.SetComputeUniform(_pixelGather, "uColourWorldToView0", colourWorldToView.Column1);
        _device.SetComputeUniform(_pixelGather, "uColourWorldToView1", colourWorldToView.Column2);
        _device.SetComputeUniform(_pixelGather, "uColourWorldToView2", colourWorldToView.Column3);
        _device.SetComputeUniform(_pixelGather, "uColourCameraToCache", colourCameraToCache);
        _device.SetComputeUniform(_pixelGather, "uSkyColour", skyColour);
        _device.SetComputeUniform(_pixelGather, "uGroundSun", _bindings.GroundSun());
        _device.SetComputeUniform(_pixelGather, "uBounceScale", RenderCVars.GiBounce.Value);
        _device.SetComputeUniform(_pixelGather, "uDebugSource", RenderCVars.GiSource.Value);

        // Wrapped, as the traces' counts are: the noise only needs it to differ from one frame to the next.
        _device.SetComputeUniform(_pixelGather, "uFrame", (int)(frame & 0xFFFFFFu));
        _bindings.BindTerrainHeights(_pixelGather);
        _bindings.WriteSkyHarmonics(_pixelGather);

        _device.SetComputeUniform(_pixelGather, "uAtlasSize", new Vector3f(_width, _height, 0f));
        _device.SetComputeUniform(_pixelGather, "uLookupBuckets", SurfaceCacheLookup.Buckets);
        _device.SetComputeUniform(_pixelGather, "uLookupMin", _bindings.Lookup.Minimum);
        _device.SetComputeUniform(_pixelGather, "uLookupCell", SurfaceCacheLookup.CellMetres);

        _device.SetComputeUniform(
            _pixelGather, "uUnproject", unproject.XScale, unproject.YScale, unproject.Near, unproject.Far);
        _device.SetComputeUniform(_pixelGather, "uViewToWorld0", viewToWorld.Column1);
        _device.SetComputeUniform(_pixelGather, "uViewToWorld1", viewToWorld.Column2);
        _device.SetComputeUniform(_pixelGather, "uViewToWorld2", viewToWorld.Column3);
        _device.SetComputeUniform(_pixelGather, "uCameraToCache", cameraToCache);
        _device.SetComputeUniform(_pixelGather, "uScreenWidth", screenWidth);
        _device.SetComputeUniform(_pixelGather, "uScreenHeight", screenHeight);
        _device.SetComputeUniform(_pixelGather, "uProbeStep", 1);

        _bindings.BindShadows(_pixelGather, shadows);

        _device.SetComputeTimingLabel("cards.pixel");
        _device.Dispatch(_pixelGather, (screenWidth + 7) / 8, (screenHeight + 7) / 8);
        _device.SetComputeTimingLabel(null);
        _device.MemoryBarrier(MemoryBarrierScope.Image | MemoryBarrierScope.TextureFetch);
        return true;
    }

    /// <summary>Gathers indirect light per screen probe and accumulates it over frames.</summary>
    /// <param name="shadows">The clipmaps to trace against. Without them there is nothing to gather.</param>
    /// <param name="sceneDepth">The frame's device depth, from the G-buffer.</param>
    /// <param name="sceneNormal">The frame's view-space normals, from the G-buffer.</param>
    /// <param name="unproject">The projection's scales and planes, for turning depth into a view position.</param>
    /// <param name="viewToWorld">The view-to-world rotation, matching what the deferred pass draws with.</param>
    /// <param name="cameraToCache">The camera's world position relative to this pass's origin.</param>
    /// <param name="screenWidth">The frame's width in pixels.</param>
    /// <param name="screenHeight">The frame's height in pixels.</param>
    /// <param name="probeStep">How many pixels lie between probes each way.</param>
    /// <param name="accumulated">The atlas of accumulated irradiance to write and, next frame, read.</param>
    /// <param name="positions">The atlas of probe world positions, written beside the irradiance.</param>
    /// <param name="history">Last frame's accumulated irradiance, reprojected onto this frame.</param>
    /// <param name="positionHistory">Last frame's probe world positions, for the disocclusion test.</param>
    /// <param name="prevWorldToView">The previous frame's world-to-view rotation, for reprojection.</param>
    /// <param name="prevCameraToCache">The previous frame's camera relative to this pass's origin.</param>
    /// <param name="frameIndex">The running frame index, for the jitter.</param>
    /// <param name="maxAccumulated">The ceiling on how many frames accumulate into a probe.</param>
    /// <param name="hasHistory">Whether there is a previous frame to reproject onto.</param>
    /// <param name="sceneColour">The previous frame's lit scene, read at an on-screen trace hit.</param>
    /// <param name="screenTraceSteps">The screen trace's step cap, and its on/off (zero disables it).</param>
    /// <param name="screenTraceDistance">How far a screen-traced ray reaches, in metres.</param>
    /// <param name="screenThickness">The assumed surface thickness for the screen trace's hit test, in metres.</param>
    /// <param name="skyColour">The sky's radiance, returned for a ray that escapes without a hit.</param>
    /// <param name="colourWorldToView">The world-to-view rotation <paramref name="sceneColour"/> was rendered with.</param>
    /// <param name="colourCameraToCache">The camera <paramref name="sceneColour"/> was rendered from, relative to this pass's origin.</param>
    /// <param name="raw">Scratch for the raw per-probe estimates, the size of <paramref name="accumulated"/>.</param>
    /// <param name="sceneVelocity">The frame's motion field (<see cref="IGraphicsDevice.SceneVelocity"/>), or none.</param>
    /// <param name="previousUnproject">
    /// The projection the history was drawn through, or <see langword="null"/> for this frame's. A cinematic
    /// zoom changes it a little every frame, and reprojecting through this frame's instead misses.
    /// </param>
    /// <remarks>
    /// L6c's temporal accumulation of the gather, with L5b's screen trace tier. The raw estimate is noisy
    /// -- sixteen jittered rays a frame -- so reading it straight ghosts and shimmers. Each probe
    /// reprojects its surface onto last frame's screen, reads the irradiance that accumulated there, and
    /// blends this frame's estimate in with a weight that falls as the frame count climbs, a running
    /// average. A reprojection that lands on a different surface, or off screen, drops the history rather
    /// than smearing it. Kept separate from the frame's own temporal anti-aliasing: this stabilises the
    /// gather in probe space before it is read, and the two filters compose.
    /// </remarks>
    internal void GatherScreenTemporal(
        GlobalDistanceField? shadows,
        TextureHandle sceneDepth,
        TextureHandle sceneNormal,
        in DepthUnprojection unproject,
        Matrix3f viewToWorld,
        Vector3f cameraToCache,
        int screenWidth,
        int screenHeight,
        int probeStep,
        TextureHandle accumulated,
        TextureHandle positions,
        TextureHandle history,
        TextureHandle positionHistory,
        Matrix3f prevWorldToView,
        Vector3f prevCameraToCache,
        int frameIndex,
        int maxAccumulated,
        bool hasHistory,
        TextureHandle sceneColour,
        int screenTraceSteps,
        float screenTraceDistance,
        float screenThickness,
        Vector3f skyColour,
        Matrix3f colourWorldToView,
        Vector3f colourCameraToCache,
        TextureHandle raw,
        TextureHandle sceneVelocity = default,
        DepthUnprojection? previousUnproject = null)
    {
        if (shadows is null || _bindings.Lookup is null || screenWidth <= 0 || screenHeight <= 0)
        {
            return;
        }

        int step = Math.Max(probeStep, 1);

        _ensureTraceMode();
        if (!_screenGatherTemporal.IsValid)
        {
            _screenGatherTemporal = _device.CreateComputeShader(
                ShaderModuleId.CardsScreenGatherTemporal,
                new ShaderVariant(_bindings.TracesRays ? "rayquery" : "software"),
                "cards.screen.temporal");
        }

        _device.BindStorageBuffer(0, _pages);
        _device.BindStorageBuffer(3, _bindings.Lookup.Cells);
        _device.BindStorageBuffer(4, _bindings.Lookup.Pages);
        _device.BindImage(0, accumulated, ImageAccess.Write);
        _device.BindImage(1, positions, ImageAccess.Write);

        _device.SetTexture(1, _device.CardAlbedo);
        _device.SetTexture(4, _device.CardDepth);
        _device.SetTexture(6, _final);
        _device.SetTexture(7, sceneDepth);
        _device.SetTexture(8, sceneNormal);
        _device.SetTexture(9, history);
        _device.SetTexture(10, positionHistory);
        _device.SetTexture(11, sceneColour);

        // Where a moving object's surface came from (IGraphicsDevice.SceneVelocity). Without a field the flag is
        // off and the normals, never read, fill the unit the program declares either way.
        _device.SetTexture(13, sceneVelocity.IsValid ? sceneVelocity : sceneNormal);
        _device.SetComputeUniform(_screenGatherTemporal, "uHasVelocity", sceneVelocity.IsValid ? 1 : 0);

        _device.SetComputeUniform(_screenGatherTemporal, "uMaxRayIntensity", _bindings.ExposedRayCap());
        _device.SetComputeUniform(_screenGatherTemporal, "uScreenTraceSteps", Math.Max(screenTraceSteps, 0));
        _device.SetComputeUniform(_screenGatherTemporal, "uScreenTraceDistance", MathF.Max(screenTraceDistance, 0f));
        _device.SetComputeUniform(_screenGatherTemporal, "uScreenThickness", MathF.Max(screenThickness, 0f));
        _device.SetComputeUniform(_screenGatherTemporal, "uSkyColour", skyColour);
        _device.SetComputeUniform(_screenGatherTemporal, "uGroundSun", _bindings.GroundSun());
        _device.SetComputeUniform(_screenGatherTemporal, "uBounceScale", RenderCVars.GiBounce.Value);
        _device.SetComputeUniform(_screenGatherTemporal, "uDebugSource", RenderCVars.GiSource.Value);
        _bindings.BindTerrainHeights(_screenGatherTemporal);
        _bindings.WriteSkyHarmonics(_screenGatherTemporal);

        _device.SetComputeUniform(_screenGatherTemporal, "uAtlasSize", new Vector3f(_width, _height, 0f));
        _device.SetComputeUniform(_screenGatherTemporal, "uLookupBuckets", SurfaceCacheLookup.Buckets);
        _device.SetComputeUniform(_screenGatherTemporal, "uLookupMin", _bindings.Lookup.Minimum);
        _device.SetComputeUniform(_screenGatherTemporal, "uLookupCell", SurfaceCacheLookup.CellMetres);

        _device.SetComputeUniform(
            _screenGatherTemporal, "uUnproject", unproject.XScale, unproject.YScale, unproject.Near, unproject.Far);
        DepthUnprojection gatherPrevious = previousUnproject ?? unproject;
        _device.SetComputeUniform(
            _screenGatherTemporal,
            "uPrevUnproject",
            gatherPrevious.XScale,
            gatherPrevious.YScale,
            gatherPrevious.Near,
            gatherPrevious.Far);
        _device.SetComputeUniform(_screenGatherTemporal, "uViewToWorld0", viewToWorld.Column1);
        _device.SetComputeUniform(_screenGatherTemporal, "uViewToWorld1", viewToWorld.Column2);
        _device.SetComputeUniform(_screenGatherTemporal, "uViewToWorld2", viewToWorld.Column3);
        _device.SetComputeUniform(_screenGatherTemporal, "uCameraToCache", cameraToCache);
        _device.SetComputeUniform(_screenGatherTemporal, "uScreenWidth", screenWidth);
        _device.SetComputeUniform(_screenGatherTemporal, "uScreenHeight", screenHeight);
        _device.SetComputeUniform(_screenGatherTemporal, "uProbeStep", step);

        _device.SetComputeUniform(_screenGatherTemporal, "uHasHistory", hasHistory ? 1 : 0);
        _device.SetComputeUniform(_screenGatherTemporal, "uPrevWorldToView0", prevWorldToView.Column1);
        _device.SetComputeUniform(_screenGatherTemporal, "uPrevWorldToView1", prevWorldToView.Column2);
        _device.SetComputeUniform(_screenGatherTemporal, "uPrevWorldToView2", prevWorldToView.Column3);
        _device.SetComputeUniform(_screenGatherTemporal, "uPrevCameraToCache", prevCameraToCache);
        _device.SetComputeUniform(_screenGatherTemporal, "uColourWorldToView0", colourWorldToView.Column1);
        _device.SetComputeUniform(_screenGatherTemporal, "uColourWorldToView1", colourWorldToView.Column2);
        _device.SetComputeUniform(_screenGatherTemporal, "uColourWorldToView2", colourWorldToView.Column3);
        _device.SetComputeUniform(_screenGatherTemporal, "uColourCameraToCache", colourCameraToCache);
        _device.SetComputeUniform(_screenGatherTemporal, "uFrameIndex", frameIndex);
        _device.SetComputeUniform(_screenGatherTemporal, "uMaxAccumulated", Math.Max(maxAccumulated, 1));

        _bindings.BindShadows(_screenGatherTemporal, shadows);

        int gridX = (screenWidth + step - 1) / step;
        int gridY = (screenHeight + step - 1) / step;

        // Two passes over the same program: the first traces every probe into the raw atlas, the second
        // pools each probe's whole neighbourhood from it and folds the result into the history. See
        // uGatherPass for why the pool cannot share the trace's dispatch.
        _device.BindImage(0, raw, ImageAccess.Write);
        _device.SetComputeUniform(_screenGatherTemporal, "uGatherPass", 0);
        _device.SetComputeTimingLabel("cards.screen.trace");
        _device.Dispatch(_screenGatherTemporal, (gridX + 7) / 8, (gridY + 7) / 8);
        _device.SetComputeTimingLabel(null);
        _device.MemoryBarrier(MemoryBarrierScope.Image | MemoryBarrierScope.TextureFetch);

        _device.BindImage(0, accumulated, ImageAccess.Write);
        _device.SetTexture(12, raw);
        _device.SetComputeUniform(_screenGatherTemporal, "uGatherPass", 1);
        _device.SetComputeTimingLabel("cards.screen.pool");
        _device.Dispatch(_screenGatherTemporal, (gridX + 7) / 8, (gridY + 7) / 8);
        _device.SetComputeTimingLabel(null);
        _device.MemoryBarrier(MemoryBarrierScope.Image | MemoryBarrierScope.TextureFetch);
    }

    /// <summary>Upsamples the sparse probe atlas to full resolution and accumulates it there over time.</summary>
    /// <param name="probe">The gather's filtered sparse probe atlas, to upsample.</param>
    /// <param name="sceneDepth">The scene depth, for the reconstruction and the upsample's depth weight.</param>
    /// <param name="sceneNormal">The scene normals.</param>
    /// <param name="unproject">The projection scales and planes.</param>
    /// <param name="viewToWorld">The view-to-world rotation.</param>
    /// <param name="cameraToCache">This frame's camera, relative to the pass origin.</param>
    /// <param name="prevWorldToView">Last frame's world-to-view rotation.</param>
    /// <param name="prevCameraToCache">Last frame's camera, relative to the pass origin.</param>
    /// <param name="accum">The full-resolution atlas to write this frame's accumulation into.</param>
    /// <param name="accumPosition">The full-resolution atlas to write each pixel's world position into.</param>
    /// <param name="history">Last frame's <paramref name="accum"/>.</param>
    /// <param name="historyPosition">Last frame's <paramref name="accumPosition"/>.</param>
    /// <param name="screenWidth">The full render width.</param>
    /// <param name="screenHeight">The full render height.</param>
    /// <param name="hasHistory">Whether there is a previous frame to reproject onto.</param>
    /// <param name="maxFrames">The ceiling on the running average.</param>
    /// <param name="probePosition">Where each probe of <paramref name="probe"/> stood when traced, relative to that frame's camera.</param>
    /// <param name="probeWorldToView">The world-to-view rotation of the frame the probes were traced in.</param>
    /// <param name="probeCameraToCache">That frame's camera, relative to this pass's origin.</param>
    /// <param name="probeStep">Pixels between probes.</param>
    /// <param name="sceneVelocity">The frame's motion field (<see cref="IGraphicsDevice.SceneVelocity"/>), or none.</param>
    /// <param name="probeFrame">The gather frame the probes were traced in, which places them in their tiles.</param>
    /// <param name="previousUnproject">
    /// The projection the history was drawn through, or <see langword="null"/> for this frame's. A cinematic
    /// zoom changes it a little every frame, and reprojecting through this frame's instead misses.
    /// </param>
    /// <remarks>
    /// The gather accumulates at the sparse probe grid, whose reprojection is coarse, so its upsampled indirect
    /// slides across a surface as the camera moves. This resolves it to full resolution and reprojects THERE,
    /// per pixel, against the surface's own world position -- pinning the indirect to the world. The deferred
    /// fill reads the result directly.
    /// </remarks>
    internal void ResolveScreenTemporal(
        TextureHandle probe,
        TextureHandle sceneDepth,
        TextureHandle sceneNormal,
        DepthUnprojection unproject,
        Matrix3f viewToWorld,
        Vector3f cameraToCache,
        Matrix3f prevWorldToView,
        Vector3f prevCameraToCache,
        TextureHandle accum,
        TextureHandle accumPosition,
        TextureHandle history,
        TextureHandle historyPosition,
        int screenWidth,
        int screenHeight,
        bool hasHistory,
        int maxFrames,
        TextureHandle probePosition,
        Matrix3f probeWorldToView,
        Vector3f probeCameraToCache,
        int probeStep,
        TextureHandle sceneVelocity = default,
        DepthUnprojection? previousUnproject = null,
        int probeFrame = 0)
    {
        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return;
        }

        if (!_screenResolve.IsValid)
        {
            _screenResolve = _device.CreateComputeShader(
                ShaderModuleId.CardsScreenResolve, ShaderVariant.Default, "cards.screen.resolve");
        }

        _device.BindImage(0, accum, ImageAccess.Write);
        _device.BindImage(1, accumPosition, ImageAccess.Write);

        _device.SetTexture(1, probe);
        _device.SetTexture(2, sceneDepth);
        _device.SetTexture(3, sceneNormal);
        _device.SetTexture(4, history);
        _device.SetTexture(5, historyPosition);
        _device.SetTexture(6, probePosition);
        _device.SetTexture(7, sceneVelocity.IsValid ? sceneVelocity : sceneNormal);

        _device.SetComputeUniform(_screenResolve, "uProbe", 1);
        _device.SetComputeUniform(_screenResolve, "uSceneDepth", 2);
        _device.SetComputeUniform(_screenResolve, "uSceneNormal", 3);
        _device.SetComputeUniform(_screenResolve, "uHistory", 4);
        _device.SetComputeUniform(_screenResolve, "uHistoryPosition", 5);
        _device.SetComputeUniform(_screenResolve, "uProbePosition", 6);
        _device.SetComputeUniform(_screenResolve, "uVelocity", 7);
        _device.SetComputeUniform(_screenResolve, "uHasVelocity", sceneVelocity.IsValid ? 1 : 0);
        _device.SetComputeUniform(_screenResolve, "uProbeWorldToView0", probeWorldToView.Column1);
        _device.SetComputeUniform(_screenResolve, "uProbeWorldToView1", probeWorldToView.Column2);
        _device.SetComputeUniform(_screenResolve, "uProbeWorldToView2", probeWorldToView.Column3);
        _device.SetComputeUniform(_screenResolve, "uProbeCameraToCache", probeCameraToCache);
        _device.SetComputeUniform(_screenResolve, "uProbeStep", Math.Max(probeStep, 1));
        _device.SetComputeUniform(_screenResolve, "uProbeFrame", probeFrame);

        _device.SetComputeUniform(
            _screenResolve, "uUnproject", unproject.XScale, unproject.YScale, unproject.Near, unproject.Far);
        DepthUnprojection resolvePrevious = previousUnproject ?? unproject;
        _device.SetComputeUniform(
            _screenResolve,
            "uPrevUnproject",
            resolvePrevious.XScale,
            resolvePrevious.YScale,
            resolvePrevious.Near,
            resolvePrevious.Far);
        _device.SetComputeUniform(_screenResolve, "uViewToWorld0", viewToWorld.Column1);
        _device.SetComputeUniform(_screenResolve, "uViewToWorld1", viewToWorld.Column2);
        _device.SetComputeUniform(_screenResolve, "uViewToWorld2", viewToWorld.Column3);
        _device.SetComputeUniform(_screenResolve, "uCameraToCache", cameraToCache);
        _device.SetComputeUniform(_screenResolve, "uPrevWorldToView0", prevWorldToView.Column1);
        _device.SetComputeUniform(_screenResolve, "uPrevWorldToView1", prevWorldToView.Column2);
        _device.SetComputeUniform(_screenResolve, "uPrevWorldToView2", prevWorldToView.Column3);
        _device.SetComputeUniform(_screenResolve, "uPrevCameraToCache", prevCameraToCache);
        _device.SetComputeUniform(_screenResolve, "uScreenWidth", screenWidth);
        _device.SetComputeUniform(_screenResolve, "uScreenHeight", screenHeight);
        _device.SetComputeUniform(_screenResolve, "uHasHistory", hasHistory ? 1 : 0);
        _device.SetComputeUniform(_screenResolve, "uMaxFrames", Math.Max(maxFrames, 1));

        _device.Dispatch(_screenResolve, (screenWidth + 7) / 8, (screenHeight + 7) / 8);
        _device.MemoryBarrier(MemoryBarrierScope.Image | MemoryBarrierScope.TextureFetch);
    }

    /// <summary>Spatially denoises the accumulated probe atlas, plane-weighted, into a separate atlas.</summary>
    /// <param name="accumulated">The accumulated irradiance to filter.</param>
    /// <param name="positions">The probe world positions, for the plane weight.</param>
    /// <param name="normals">The scene's G-buffer normals, for the facing weight.</param>
    /// <param name="filtered">The atlas to write the denoised result into, read by the deferred fill.</param>
    /// <param name="gridWidth">The probe grid's width.</param>
    /// <param name="gridHeight">The probe grid's height.</param>
    /// <param name="radius">How many probes each way the blur reaches.</param>
    /// <remarks>
    /// L6c's spatial half of the denoise: the per-frame gather is noisy and the temporal accumulation
    /// resets along every depth edge, so the probes freshest near a wall carry the raw estimate, which
    /// reads as a shimmer. Blending each probe with its neighbours -- weighted by how near their world
    /// positions sit, so light does not cross an edge -- smooths that. Writes a separate atlas rather
    /// than the accumulation itself, so the history the next frame reprojects onto stays unfiltered and
    /// the blur does not compound.
    /// </remarks>
    internal void FilterScreen(
        TextureHandle accumulated,
        TextureHandle positions,
        TextureHandle normals,
        TextureHandle filtered,
        int gridWidth,
        int gridHeight,
        int radius)
    {
        if (gridWidth <= 0 || gridHeight <= 0 || radius <= 0)
        {
            return;
        }

        if (!_screenGatherFilter.IsValid)
        {
            _screenGatherFilter = _device.CreateComputeShader(
                ShaderModuleId.CardsScreenFilter, ShaderVariant.Default, "cards.screen.filter");
        }

        _device.BindImage(0, filtered, ImageAccess.Write);
        _device.SetTexture(1, accumulated);
        _device.SetTexture(2, positions);
        _device.SetTexture(3, normals);

        _device.SetComputeUniform(_screenGatherFilter, "uAccum", 1);
        _device.SetComputeUniform(_screenGatherFilter, "uPosition", 2);
        _device.SetComputeUniform(_screenGatherFilter, "uSceneNormal", 3);
        _device.SetComputeUniform(_screenGatherFilter, "uWidth", gridWidth);
        _device.SetComputeUniform(_screenGatherFilter, "uHeight", gridHeight);
        _device.SetComputeUniform(_screenGatherFilter, "uRadius", radius);

        _device.Dispatch(_screenGatherFilter, (gridWidth + 7) / 8, (gridHeight + 7) / 8);
        _device.MemoryBarrier(MemoryBarrierScope.Image | MemoryBarrierScope.TextureFetch);
    }

    /// <summary>Drops the tracing programs after a trace-mode switch, so each pass rebuilds its own.</summary>
    internal void DropTracingShaders()
    {
        if (_screenGather.IsValid)
        {
            _device.DestroyComputeShader(_screenGather);
            _screenGather = ComputeShaderHandle.None;
        }

        if (_screenGatherTemporal.IsValid)
        {
            _device.DestroyComputeShader(_screenGatherTemporal);
            _screenGatherTemporal = ComputeShaderHandle.None;
        }

        if (_pixelGather.IsValid)
        {
            _device.DestroyComputeShader(_pixelGather);
            _pixelGather = ComputeShaderHandle.None;
        }
    }

    /// <summary>Destroys every program built, as disposing the pass does.</summary>
    internal void DestroyShaders()
    {
        if (_screenGather.IsValid)
        {
            _device.DestroyComputeShader(_screenGather);
        }

        if (_screenGatherTemporal.IsValid)
        {
            _device.DestroyComputeShader(_screenGatherTemporal);
        }

        if (_pixelGather.IsValid)
        {
            _device.DestroyComputeShader(_pixelGather);
        }

        if (_screenGatherFilter.IsValid)
        {
            _device.DestroyComputeShader(_screenGatherFilter);
        }

        if (_screenResolve.IsValid)
        {
            _device.DestroyComputeShader(_screenResolve);
        }
    }
}
