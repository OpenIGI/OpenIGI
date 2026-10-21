using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Meshes;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Pipeline.RayTracing;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Temporal;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The traced sun shadow: the world fed to the backend's ray tracing, and the mask traced against it.</summary>
/// <remarks>
/// <para>
/// Where the adapter traces, the sun's shadow is asked of the geometry rather than of cascades. The world is
/// built from what the frame draws: every G-buffer draw is observed with the transform it was drawn at, the
/// first-person rig's opaque forward draws are observed where only a reflection can find them, the terrain
/// arrives through <see cref="SubmitShadowCaster"/>, and each cut-out texture's alpha is kept as a mask the
/// moment its pixels are in hand.
/// </para>
/// <para>
/// The structure is updated as the capture opens, from what was observed up to then, and traced between the
/// G-buffer and the shading.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    private TracedSunShadow? _tracedSun;

    // Whether this frame's sun was traced, and so whether the shading reads the mask.
    private bool _sunTraced;
    private bool _worldReleasePending;

    // The traced world: the device's own where it offers acceleration structures, or a test's stand-in.
    private readonly ITracedWorld? _tracedWorld;
    private readonly RayTracing.TracedWorld? _ownedTracedWorld;

    /// <summary>Gets the traced world, or null where the device does not trace.</summary>
    private ITracedWorld? TracedWorld => _tracedWorld;

    /// <inheritdoc/>
    public bool WorldTraceReady => TracedWorld is not { IsWorldReady: false };

    /// <inheritdoc/>
    public bool WorldTraceSettled => TracedWorld is not { IsWorldSettled: false };

    /// <inheritdoc/>
    public void SetTraceAdmission(bool unlimited) => TracedWorld?.SetUnlimitedAdmission(unlimited);

    // Whether draws are being kept out of the traced world. See SetTracePaused.
    private bool _tracePaused;

    /// <inheritdoc/>
    public void SetTracePaused(bool paused) => _tracePaused = paused;

    /// <inheritdoc/>
    public bool AcceptsShadowCasters => TracedWorld is not null && !_tracePaused;

    /// <inheritdoc/>
    public string DescribeMemory() =>
        FormattableString.Invariant($"Graphics device: {_textures.Count} texture(s), {_materials.Count} material group(s).\n")
        + _device.DescribeMemory();

    /// <inheritdoc/>
    public void TrimMemory() => _device.TrimMemory();

    /// <inheritdoc/>
    /// <remarks>
    /// Asked before the capture opens, which is when the game decides whether the cascades are worth
    /// drawing: they are not, on a frame whose sun is traced.
    /// </remarks>
    public bool SunShadowTraced => WillTraceSun();

    /// <inheritdoc/>
    /// <remarks>
    /// Acted on as the next frame begins, with the device idle; see <see cref="ReleaseWorldIfAsked"/>.
    /// </remarks>
    public void ReleaseWorld()
    {
        _worldReleasePending = TracedWorld is not null;

        // The next level's sky is not this one's; its harmonics are asked for afresh.
        _skyIrradianceKnown = false;
    }

    /// <inheritdoc/>
    /// <remarks>Only from the G-buffer pass: the transform is the world transform only for the frame's camera.</remarks>
    public void SubmitShadowCaster(MeshHandle mesh, in Matrix4f modelView, int indexCount)
    {
        ThrowIfDisposed();

        if (TracedWorld is not { } world || _tracePaused || !_capturing || CurrentPass is not ScenePass.Geometry)
        {
            return;
        }

        world.ObserveCaster(mesh.Id, Traced(Resolve(mesh)), TracedWorldTransform.From(modelView, _viewToWorld, _cameraPosition), indexCount);
    }

    /// <inheritdoc/>
    public void SubmitTracedMesh(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        in Matrix4f modelView,
        TextureHandle diffuse,
        bool firstPersonOnly = false,
        SurfaceMaterial? material = null)
    {
        ThrowIfDisposed();
        ObserveForTracing(
            mesh, modelView, diffuse, indexOffset, indexCount, material ?? SurfaceMaterial.Default, firstPersonOnly);
    }

    /// <summary>Whether this frame's sun is traced: the settings ask for it and the world can answer.</summary>
    private bool WillTraceSun()
        => _shadowSettings.Enabled
            && _shadowSettings.TracedSun
            && _shadowSettings.SunLit
            && TracedWorld is { IsWorldReady: true, WorldScene: not null };

    /// <summary>Keeps a new texture's cut-out shape, while its pixels are in hand.</summary>
    private void ObserveTexture(TextureHandle texture, in TextureDescription description, ReadOnlySpan<Bgra32> pixels) =>
        TracedWorld?.ObserveTexture(texture.Id, pixels, description.Width, description.Height);

    /// <summary>Records a draw into the traced world, if it is one the world is built from.</summary>
    /// <param name="mesh">The mesh being drawn.</param>
    /// <param name="modelView">Its object-to-view transform.</param>
    /// <param name="diffuse">Its colour texture, whose alpha decides how the run occludes.</param>
    /// <param name="indexOffset">Where in the index buffer this draw started.</param>
    /// <param name="indexCount">How many indices it covered.</param>
    /// <param name="material">What the run is made of: whether it glows, and whether it occludes.</param>
    /// <param name="firstPerson">Whether this is an opaque, depth-writing draw of the first-person rig.</param>
    /// <remarks>
    /// The sky stays out: it is drawn as geometry, but it is light rather than a lit surface, and built into
    /// the world it becomes a closed shell every ray at the sun hits. "Emits and does not occlude", because
    /// a lamp emits too and is a solid fixture that has to cast.
    /// </remarks>
    private void ObserveForTracing(
        MeshHandle mesh,
        in Matrix4f modelView,
        TextureHandle diffuse,
        int indexOffset,
        int indexCount,
        in SurfaceMaterial material,
        bool firstPerson = false)
    {
        if (TracedWorld is not { } world
            || _tracePaused
            || !_capturing
            || (CurrentPass is not ScenePass.Geometry && !firstPerson)
            || (material.Emissive > 0f && !material.CastsOcclusion))
        {
            return;
        }

        world.Observe(
            mesh.Id,
            Traced(Resolve(mesh)),
            TracedWorldTransform.From(modelView, _viewToWorld, _cameraPosition),
            // A lamp's glow goes with it, so a reflection that stops on the tube can show it lit.
            new TracedSurface(indexOffset, indexCount, diffuse.Id, Math.Max(material.Emissive, 0f)),
            firstPerson ? RayMask.ViewModel : RayMask.All);
    }

    /// <summary>Updates the traced world from this frame's G-buffer draws, once, before anything this frame traces.</summary>
    /// <remarks>
    /// <para>
    /// Called where the geometry ends -- the first of the screen GI's motion request or the shading -- so every
    /// ray the frame fires meets the world the frame drew. It used to run as the capture opened, from the
    /// previous frame's sightings: anything that moved or changed its level of detail was traced where it had
    /// been, and a surface just in front of its old self shadowed itself. With the shadow history clamped to
    /// the current frame that was a flicker -- the back of a moving truck, a far mountainside at every level
    /// of detail change.
    /// </para>
    /// <para>
    /// What is observed after this -- the first-person rig in the forward pass -- reaches the next frame's
    /// update, as everything did before.
    /// </para>
    /// </remarks>
    private void SettleTracedWorld()
    {
        if (_worldSettled || !_capturing)
        {
            return;
        }

        _worldSettled = true;

        if (TracedWorld is not { } world)
        {
            return;
        }

        // Outside any pass, because a structure cannot be built inside one.
        ClosePass();
        CloseScenePass();
        world.RecordSceneUpdate(Encoder(), _profiler.Time(RenderStage.AccelerationStructure, "scene-update"));
    }

    /// <summary>Drops the traced world a level change asked to be released, before the frame records anything.</summary>
    /// <remarks>
    /// <para>
    /// Here, at the top of a frame, and nowhere later. Resetting frees every structure at once, so it needs
    /// the device idle -- and idle covers only what has been SUBMITTED. It was done as a capture opened,
    /// which may be well into a frame: whatever that frame had already recorded, a scene build naming the
    /// old level's structures among it, sat in pieces not yet submitted, ran after the reset had freed
    /// them, and the device was lost on the read (an invalid read inside a top-level build, on the way
    /// from the menu into a mission). At the top of the frame there is nothing recorded to be caught out.
    /// </para>
    /// <para>
    /// A release asked for mid-frame waits for the next one, so the frame that asked goes out tracing a
    /// world that is still whole.
    /// </para>
    /// </remarks>
    private void ReleaseWorldIfAsked()
    {
        if (!_worldReleasePending)
        {
            return;
        }

        _worldReleasePending = false;

        if (TracedWorld is not { } world)
        {
            return;
        }

        _device.WaitIdle();
        world.Reset();
        _tracedSun?.Invalidate();
    }

    /// <summary>Traces the sun's shadow against the world, for the shading to read this same frame.</summary>
    private void RecordSunShadow(SceneTargetSet scene)
    {
        if (!WillTraceSun() || TracedWorld is not { WorldScene: { } structure } world)
        {
            return;
        }

        _tracedSun ??= new TracedSunShadow(_device, FramesInFlight, _retired);
        _tracedSun.Ensure(scene.Width, scene.Height);
        _tracedSun.ClearNew(Encoder());

        TemporalFrameState state = TemporalState(scene);

        // A shadow, so it reports under the shadows' stage.
        using (IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.Shadows, "sun.shadow"),
            "sunshadow")))
        {
            _tracedSun.Record(
                pass,
                FrameSlot,
                scene.View(SceneTargetId.Depth),
                scene.View(SceneTargetId.Normal),
                new TracedSunShadowFrame(
                    structure,
                    world.CutoutMasks,
                    world.HitRecordHandle,
                    _cameraPosition,
                    _lighting.WorldSunDirection(_viewToWorld),
                    RenderCVars.SunAngle.Value,

                    // Cycled over a short period: the ray direction is built from this through a fract(),
                    // and a float as large as a frame count has lost the low bits that vary it. Raw, for
                    // ray reconstruction, the white noise is hashed from the whole count instead.
                    RawLighting ? _frameCounter & 0xFFFFFFUL : _frameCounter % 64UL,
                    RenderCVars.ShadowAccumulate.Value,
                    RawLighting),
                state,
                _velocityRecorded ? scene.View(SceneTargetId.Velocity) : null);
        }

        _sunTraced = true;
    }

    /// <summary>What the shading is told about the sun's shadow: traced this frame, or from the cascades.</summary>
    private DeferredLightingState WithTracedSun(in DeferredLightingState state) => state with
    {
        SunShadowTraced = _sunTraced,
    };

    /// <summary>Points the shading at the traced mask, or at nothing for a frame that did not trace.</summary>
    private void BindSunShadow() =>
        _shading?.BindSunShadow(_sunTraced ? _tracedSun?.Mask : null);

    private static TracedMeshBuffers Traced(RhiMesh mesh) => new(
        mesh.VertexBuffer,
        mesh.IndexBuffer,
        mesh.VertexCapacity,
        mesh.IndexCapacity,
        mesh.DrawableIndexCount,
        mesh.Animated,
        mesh.Storage,
        mesh.ContentVersion);

    private bool _worldSettled;

    private void DisposeSunShadow()
    {
        _tracedSun?.Dispose();
        _tracedSun = null;
    }
}
