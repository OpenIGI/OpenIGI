using System.Runtime.InteropServices;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Mesh3D;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.Sky;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Sky;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The deferred frame: the G-buffer, the shading over it, and the resolve onto the screen.</summary>
/// <remarks>
/// <para>
/// Three passes rather than one. The geometry pass writes what every surface IS — its colour, which way
/// it faces, what it is made of and what light was baked onto it — into four targets and a depth buffer.
/// The shading pass reads all five back as textures and turns them into lit colour. The resolve turns
/// that half-float colour into the eight-bit frame a display takes.
/// </para>
/// <para>
/// The shading reads them as TEXTURES rather than as subpass inputs, which is the one structural
/// difference from the Vulkan original: WebGPU has no subpasses at all, so the same shader is compiled
/// once for each and this path takes the sampled form. What they produce is identical.
/// </para>
/// <para>
/// What is not here yet is named honestly rather than drawn wrongly. There are no cascades, no ambient
/// occlusion, no temporal resolve, no bloom and no translucency pass, and every one of those is either
/// a console variable this device reports as unimplemented or a pass whose draws are dropped. The
/// alternative — drawing translucency as opaque geometry, say — would be a frame that looks finished
/// and is not.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    /// <summary>The four G-buffer targets, in the order the geometry pass declares them.</summary>
    private static readonly SceneTargetId[] GBufferTargets =
    [
        SceneTargetId.BaseColour,
        SceneTargetId.Normal,
        SceneTargetId.Material,
        SceneTargetId.Baked,
    ];

    private SceneTargetSet? _scene;
    private GBufferPipelineCache? _geometryPipelines;
    private DeferredLighting? _shading;
    private DeferredResolve? _sceneResolve;
    private SkyAtmosphere? _sky;
    private IRhiBindGroupLayout? _surfaceLayout;
    private IRhiBuffer? _surfaceUniform;
    private IRhiBindGroup? _surfaceGroup;
    private IRhiRenderPassEncoder? _geometryPass;

    private bool _capturing;
    private DebugView _debugView;
    private bool _groundDetail;
    private bool _paintedSky;
    private bool _frameHasPaintedSky;
    private float _floorGloss = 1f;
    private float _writtenFloorGloss = float.NaN;
    private float _sceneExposure = 1f;
    private int _grainFrame;
    private Matrix3f _viewToWorld = Matrix3f.Identity;
    private Vector3f _cameraPosition;
    private DepthUnprojection _unprojection = new(1f, 1f, 0.1f, 1000f);

    /// <inheritdoc/>
    /// <remarks>
    /// Only the deferred, high-dynamic-range form. A description asking for anything else is declined,
    /// which is how the game falls back to the direct path rather than being handed a frame this cannot
    /// finish.
    /// </remarks>
    public bool BeginSceneCapture(in SceneTargetDescription description)
    {
        ThrowIfDisposed();

        if (_capturing)
        {
            throw new GraphicsDeviceException("A scene capture is already in progress.");
        }

        // Declined where it was not advertised, so the two answers cannot disagree: something that asks
        // without reading the capability gets the same no.
        if (!Capabilities.SupportsSceneCapture || !description.Deferred || CapturingCards)
        {
            return false;
        }

        // Whatever the caller drew before the capture belongs to the frame, not to the G-buffer.
        ClosePass();

        EnsureDeferred(Math.Max(1, description.Width), Math.Max(1, description.Height));
        WriteSurfaceBlock();

        _frameHasPaintedSky = false;
        _capturing = true;
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A bracket around the dome's own draws: what it turns on is the division by the exposure, which
    /// belongs to them and to nothing drawn after. The frame remembers that a sky was painted at all, so
    /// the shading keeps it rather than putting the atmosphere where there is no depth.
    /// </remarks>
    public void SetPaintedSky(bool painted)
    {
        ThrowIfDisposed();
        _paintedSky = painted;
        _frameHasPaintedSky |= painted;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The ground's lightmap slot then carries its detail map, which the geometry pass multiplies into
    /// the colour rather than writing as baked light.
    /// </remarks>
    public void SetLightmapModulates(bool modulate)
    {
        ThrowIfDisposed();
        _groundDetail = modulate;
    }

    /// <inheritdoc/>
    public void SetFloorGloss(float gloss)
    {
        ThrowIfDisposed();
        _floorGloss = Math.Clamp(gloss, 0f, 1f);
    }

    /// <inheritdoc/>
    public void BeginPass(ScenePass pass)
    {
        ThrowIfDisposed();
        CurrentPass = pass;

        // A pass ends when the next one begins, whether or not the caller said so: the game moves from
        // geometry to glazing to translucency by declaring the next one, and a second render pass begun
        // while the first is still open is undefined rather than nested.
        CloseGeometryPass();

        if (!_capturing || pass != ScenePass.Geometry || _scene is not { } scene)
        {
            return;
        }

        // The whole G-buffer cleared at once: coverage, normal, material and baked light all read as
        // nothing where no surface reached, and the shading tests the depth rather than any of them.
        ClosePass();

        RhiColorAttachment[] attachments = new RhiColorAttachment[GBufferTargets.Length];

        for (int i = 0; i < GBufferTargets.Length; i++)
        {
            attachments[i] = new RhiColorAttachment(
                scene.View(GBufferTargets[i]),
                RhiLoadOp.Clear,
                RhiStoreOp.Store,
                RhiClearColor.Transparent);
        }

        _geometryPass = Encoder().BeginRenderPass(new RhiRenderPassDescriptor(
            attachments,
            new RhiDepthAttachment(
                scene.View(SceneTargetId.Depth),

                // Reverse-Z: the far plane is nought, so a pass that clears starts with everything
                // already further away than anything it will draw.
                RhiLoadOp.Clear,
                RhiStoreOp.Store,
                DepthClearValue: 0f),
            _profiler.Time(RenderStage.Geometry),
            Label: "geometry"));

        _geometryPass.SetViewport(0, 0, scene.Width, scene.Height, 0f, 1f);
        _geometryPass.SetScissor(0, 0, scene.Width, scene.Height);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The shading draws every view it knows from the G-buffer it is reading, and the resolve shows the
    /// code values of those that are masks rather than tone mapping them. A view whose pass has not moved
    /// onto this path shows what the shading draws for it, which for most of them is black.
    /// </remarks>
    public void SetDebugView(DebugView view)
    {
        ThrowIfDisposed();
        _debugView = view;
    }

    /// <inheritdoc/>
    public void EndPass()
    {
        ThrowIfDisposed();
        CurrentPass = ScenePass.None;
        CloseGeometryPass();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Where the G-buffer becomes light. The shading is recorded here rather than by the resolve so that
    /// the capture is finished when it says it is, whatever the caller does between the two.
    /// </remarks>
    public void EndSceneCapture()
    {
        ThrowIfDisposed();

        if (!_capturing)
        {
            return;
        }

        CloseGeometryPass();
        RecordShading();

        _capturing = false;
    }

    /// <inheritdoc/>
    public void ResolveScene(in PostProcessSettings post)
    {
        ThrowIfDisposed();

        if (_scene is not { } scene || _sceneResolve is not { } resolve)
        {
            return;
        }

        ClosePass();

        _grainFrame = (_grainFrame + 1) % ResolvePushBlock.GrainCycle;

        Span<float> push = stackalloc float[ResolvePushBlock.Floats];

        ResolvePushBlock.Write(push, post, new ResolveFrameState(
            Aspect: scene.Height / (float)Math.Max(1, scene.Width),
            GrainPhase: _grainFrame * ResolvePushBlock.GrainStep,
            PresentWidth: CurrentMode.Width,
            PresentHeight: CurrentMode.Height,
            MaskView: _debugView.IsMask()));

        // What a painted sky is divided by as it is drawn, so that it leaves the tone map at the colour
        // it was painted. Last frame's, as it is on the Vulkan device: the sky is drawn before the frame
        // says what it will be exposed by. There is no metering here, so it is the setting itself.
        _sceneExposure = MathF.Max(post.Exposure, 1e-3f);

        resolve.BindFrame(scene.View(SceneTargetId.Lit));

        using (IRhiRenderPassEncoder pass = Encoder().BeginRenderPass(new RhiRenderPassDescriptor(
            [new RhiColorAttachment(_frame.Target, RhiLoadOp.Clear, RhiStoreOp.Store, _clearColour)],
            Timestamps: _profiler.Time(RenderStage.ToneMapGrade),
            Label: "resolve")))
        {
            pass.SetViewport(0, 0, CurrentMode.Width, CurrentMode.Height, 0f, 1f);
            pass.SetScissor(0, 0, CurrentMode.Width, CurrentMode.Height);
            resolve.Draw(pass, MemoryMarshal.AsBytes(push));
            pass.EndPass();
        }

        // The resolve cleared the frame and then covered all of it, so a clear the caller asked for is
        // done. Left armed, it is honoured by whatever opens the frame's pass next — the letterbox, the
        // HUD — which clears away the whole picture a moment after it was drawn.
        _clearPending = false;

        scene.AdvanceFrame();
    }

    /// <summary>Records the shading: the whole G-buffer read back as textures, over one triangle.</summary>
    private void RecordShading()
    {
        if (_scene is not { } scene || _shading is not { } shading)
        {
            return;
        }

        // Nothing else may be open: a render pass begun inside another is not nesting, it is undefined,
        // and the encoder that ends the outer one afterwards ends something that is no longer there.
        ClosePass();
        CloseGeometryPass();

        RecordSky();

        Span<float> gi = stackalloc float[DeferredLightingParameters.GiFloats];
        Span<float> lamps = stackalloc float[DeferredLampBlock.Floats];
        Span<float> light = stackalloc float[DeferredLightingParameters.LightFloats];

        DeferredLightingParameters.WriteGi(gi, LightingState(), [], []);
        DeferredLampBlock.Write(lamps, new DeferredLampState(_frameCounter), []);

        DeferredLightingParameters.WriteLight(
            light,
            _lighting.SunDirection,
            (int)_lighting.Model,
            _lighting.SunColour,
            _lighting.Scale,
            _lighting.AmbientColour,
            (int)_debugView,
            _frameHasPaintedSky,
            _lighting.CompleteBake);

        shading.BindFrame(scene, FrameSlot, gi, lamps);

        using IRhiRenderPassEncoder pass = Encoder().BeginRenderPass(new RhiRenderPassDescriptor(
            [new RhiColorAttachment(scene.View(SceneTargetId.Lit), RhiLoadOp.Clear, RhiStoreOp.Store, RhiClearColor.Transparent)],
            Timestamps: _profiler.Time(RenderStage.Lighting),
            Label: "shading"));

        pass.SetViewport(0, 0, scene.Width, scene.Height, 0f, 1f);
        pass.SetScissor(0, 0, scene.Width, scene.Height);
        shading.Draw(pass, MemoryMarshal.AsBytes(light));
        pass.EndPass();
    }

    /// <summary>Marches the atmosphere for this frame's sun, and copies its light down for the game.</summary>
    /// <remarks>
    /// Here rather than as the capture opens, because the game states the frame's lighting after it has
    /// opened one. The march itself is skipped while nothing it depends on has moved, which for a sun
    /// fixed for the mission is every frame after the first; the copy is not, so every frame slot holds
    /// the answer rather than one of them.
    /// </remarks>
    private void RecordSky()
    {
        if (_sky is not { } sky)
        {
            return;
        }

        IRhiCommandEncoder encoder = Encoder();

        using (IRhiComputePassEncoder pass = encoder.BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.Setup),
            "sky")))
        {
            sky.Record(pass, FrameSlot, SkyConditions.ForScene(_lighting, _viewToWorld, altitudeMetres: 2f));
        }

        sky.RecordReadback(encoder, FrameSlot);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Read from the harmonics the atmosphere copies down every frame. Nothing is waited for: a slot
    /// whose copy has not arrived answers no, and the caller keeps what it had — which, for a sun fixed
    /// for the mission, is the same answer.
    /// </remarks>
    public bool TryGetSkyIrradiance(Span<float> coefficients)
    {
        ThrowIfDisposed();
        return _sky is { } sky && sky.TryReadIrradiance(FrameSlot, coefficients);
    }

    /// <summary>What the shading is told about this frame.</summary>
    /// <remarks>
    /// Most of it is switched off, because most of what it describes has not moved onto this path: there
    /// is no probe field, no screen atlas, no occlusion, no cascades, no wetness and no fog volume. Each
    /// becomes a line here on the day its pass lands, and until then the shading reads the value that
    /// says "not this frame" rather than a stand-in's contents.
    /// </remarks>
    private DeferredLightingState LightingState() => new(
        _unprojection,
        LightScale: _lighting.Scale,
        CameraPosition: _cameraPosition,
        ViewToWorld: _viewToWorld,
        ViewHeightKm: SkyConditions.ViewHeightFor(altitudeMetres: 2f));

    /// <summary>Gets whether a scene capture is open, which routes every draw through it.</summary>
    private bool Capturing => _capturing;

    /// <summary>Records one mesh's ranges into the G-buffer.</summary>
    /// <param name="mesh">The mesh being drawn.</param>
    /// <param name="ranges">Which parts of it, and what each is textured with.</param>
    /// <param name="modelView">Its transform into view space.</param>
    /// <param name="diffuse">Its colour texture.</param>
    /// <param name="lightmap">Its lightmap.</param>
    /// <param name="blend">How it is blended.</param>
    private void DrawGeometryRanges(
        MeshHandle mesh,
        ReadOnlySpan<MeshDrawRange> ranges,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend)
    {
        if (_geometryPass is not { } pass || _geometryPipelines is not { } pipelines)
        {
            return;
        }

        Span<float> push = stackalloc float[GBufferPushBlock.Floats];
        GBufferPushBlock.Write(push, GeometryState(modelView), modelView, blend);

        _meshes.DrawRanges(
            pass,
            pipelines.ForState(_depthState, _cullMode, blend, _alphaReference),
            Call(mesh, 0, 0, diffuse, lightmap, blend),
            ranges,
            MemoryMarshal.AsBytes(push),
            _frameCounter,
            _surfaceGroup);
    }

    /// <summary>What a geometry draw is told about the frame it is in.</summary>
    private GBufferDrawState GeometryState(in Matrix4f modelView) => new(
        _projection,
        _viewToWorld,
        _material,
        _lightmapScale,
        _poseBlend,
        _alphaHashSeed,
        _detailFade,
        GBufferPushBlock.TerrainCameraOffset(modelView, _viewToWorld),
        _groundDetail,
        _paintedSky,
        _sceneExposure);

    /// <summary>Records one mesh into the G-buffer.</summary>
    /// <param name="call">What to draw.</param>
    /// <param name="modelView">Its transform into view space.</param>
    /// <param name="blend">How it is blended, which is what names the ground's bake.</param>
    /// <returns><see langword="true"/> when it was drawn.</returns>
    private bool DrawGeometry(in MeshDrawCall call, in Matrix4f modelView, BlendMode blend)
    {
        if (_geometryPass is not { } pass || _geometryPipelines is not { } pipelines)
        {
            return false;
        }

        Span<float> push = stackalloc float[GBufferPushBlock.Floats];
        GBufferPushBlock.Write(push, GeometryState(modelView), modelView, blend);

        return _meshes.Draw(
            pass,
            pipelines.ForState(_depthState, _cullMode, blend, _alphaReference),
            call,
            MemoryMarshal.AsBytes(push),
            _frameCounter,
            _surfaceGroup);
    }

    private void CloseGeometryPass()
    {
        if (_geometryPass is not { } pass)
        {
            return;
        }

        pass.EndPass();
        pass.Dispose();
        _geometryPass = null;
    }

    /// <summary>Builds everything the deferred frame needs, at the size it is asked for.</summary>
    private void EnsureDeferred(int width, int height)
    {
        if (_scene is { } existing && existing.Width == width && existing.Height == height)
        {
            return;
        }

        if (_scene is { } resized)
        {
            resized.Resize(width, height);
            return;
        }

        _scene = new SceneTargetSet(_device, width, height);

        _surfaceLayout = _device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            [new RhiBindGroupLayoutEntry(0, RhiBindingType.UniformBuffer, RhiShaderStages.Graphics)],
            "surface"));

        // One vector, per FRAME rather than per draw: how strongly the derived detail maps are trusted,
        // and how far an up-facing surface's roughness is smoothed towards a floor's.
        _surfaceUniform = _device.CreateBuffer(new RhiBufferDescriptor(
            SurfaceBlockBytes,
            RhiBufferUsage.Uniform | RhiBufferUsage.CopyDestination,
            "surface"));

        _writtenFloorGloss = float.NaN;
        WriteSurfaceBlock();

        _surfaceGroup = _device.CreateBindGroup(new RhiBindGroupDescriptor(
            _surfaceLayout,
            [RhiBindGroupEntry.Of(0, _surfaceUniform)],
            "surface"));

        _geometryPipelines = new GBufferPipelineCache(
            _device,
            _materialLayout,
            _surfaceLayout,
            [.. GBufferTargets.Select(id => SceneTargetSet.FormatOf(id))],
            SceneTargetSet.FormatOf(SceneTargetId.Depth));

        _shading = new DeferredLighting(_device, FramesInFlight, SceneTargetSet.FormatOf(SceneTargetId.Lit));

        // The atmosphere's tables never change place, so the shading is pointed at them once. Until they
        // were, it read the stand-ins: a sky of flat white and an ambient of nothing.
        _sky = new SkyAtmosphere(_device, FramesInFlight);
        _shading.BindSky(_sky.SkyViewTable, _sky.TransmittanceTable, _sky.IrradianceTable, _sky.Sampler);
        _sceneResolve = new DeferredResolve(_device, PresentFormat);
    }

    /// <summary>Writes the geometry pass's surface block, when what it says has changed.</summary>
    /// <remarks>
    /// <para>
    /// Not a block of zeros. The floor gloss is a MULTIPLY on an up-facing surface's roughness, and one
    /// is its identity: nought makes every floor a mirror. The detail strength is nought on purpose —
    /// the detail maps it trusts are not bound on this path, and a strength over a stand-in would bend
    /// every normal in the world by the stand-in's colour.
    /// </para>
    /// <para>
    /// Written only when it changes, because the buffer is one for every frame in flight and the gloss
    /// is a setting that moves when a player moves it.
    /// </para>
    /// </remarks>
    private void WriteSurfaceBlock()
    {
        if (_surfaceUniform is not { } uniform || _floorGloss.Equals(_writtenFloorGloss))
        {
            return;
        }

        Span<float> block = stackalloc float[SurfaceBlockFloats];
        block[0] = 0f;
        block[1] = _floorGloss;

        _device.WriteBuffer<float>(uniform, 0, block);
        _writtenFloorGloss = _floorGloss;
    }

    /// <summary>How many floats the geometry pass's per-frame surface block holds.</summary>
    private const int SurfaceBlockFloats = 4;

    /// <summary>How many bytes it occupies.</summary>
    private const ulong SurfaceBlockBytes = SurfaceBlockFloats * sizeof(float);

    private void DisposeDeferred()
    {
        _geometryPass?.Dispose();
        _geometryPass = null;

        _sceneResolve?.Dispose();
        _sceneResolve = null;
        _sky?.Dispose();
        _sky = null;
        _shading?.Dispose();
        _shading = null;
        _geometryPipelines?.Dispose();
        _geometryPipelines = null;

        _surfaceGroup?.Dispose();
        _surfaceGroup = null;
        _surfaceUniform?.Dispose();
        _surfaceUniform = null;
        _surfaceLayout?.Dispose();
        _surfaceLayout = null;

        _scene?.Dispose();
        _scene = null;
    }
}
