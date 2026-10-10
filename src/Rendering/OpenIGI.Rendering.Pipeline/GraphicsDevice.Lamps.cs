using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Temporal;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The level's lamps: what the shading, the fog and the first-person rig are lit by besides the sun.</summary>
/// <remarks>
/// Held for one capture: the shading reads them, the fog scatters them, and the forward pass lights the rig
/// from them after the shading has run, so they are cleared only as the capture ends -- a frame that submits
/// none still lights from none.
/// </remarks>
public sealed partial class GraphicsDevice
{
    private readonly DeferredLocalLight[] _localLights = new DeferredLocalLight[DeferredLocalLight.Maximum];
    private readonly float[] _lampBlock = new float[DeferredLampBlock.Floats];
    private int _localLightCount;
    private TracedLampShadow? _tracedLamps;

    /// <summary>
    /// The exposed lamp light, as a fraction of the display's full scale, below which a lamp's shadow is not
    /// traced: a quarter of one 8-bit step, divided by four for the specular lobe.
    /// </summary>
    private const float MinimumVisibleLampFraction = 0.25f / 255f / 4f;

    // Whether this frame's lamp visibility was traced, and so whether the shading reads it.
    private bool _lampsTraced;

    // The level's distance field, which the lamps' shadows are marched through when nothing can trace.
    private LampOcclusionField? _lampField;

    // The field's corners and ranges as the software trace reads them; see LampFieldFloats.
    private readonly float[] _lampFieldBlock = new float[TracedLampShadow.FieldFloats];

    /// <summary>Gets this frame's lamps.</summary>
    private ReadOnlySpan<DeferredLocalLight> LocalLights => _localLights.AsSpan(0, _localLightCount);

    /// <summary>The lamp trace's frame number: a short cycle for its sequences, the whole count for raw white noise.</summary>
    private int LampFrameIndex => RawLighting ? (int)(_frameCounter & 0xFFFFFFUL) : (int)(_frameCounter % 64UL);

    /// <summary>Whether the lamp shadows' filters run; the raw trace skips them whatever this says.</summary>
    private static bool LampFilters => RenderCVars.LampDenoise.Value != 0;

    /// <inheritdoc/>
    public void SetLampOcclusionField(LampOcclusionField? field) => _lampField = field;

    /// <inheritdoc/>
    public void SetLocalLights(ReadOnlySpan<DeferredLocalLight> lights)
    {
        ThrowIfDisposed();

        _localLightCount = Math.Min(lights.Length, DeferredLocalLight.Maximum);
        lights[.._localLightCount].CopyTo(_localLights);
    }

    /// <summary>What the frame's lamps do.</summary>
    /// <remarks>
    /// The screen march is on wherever lamp shadows are asked for at all: it needs no ray query, and it is
    /// what keeps a lamp from lighting through a wall on an adapter that cannot trace. The traced
    /// visibility needs the adapter, the RT switches and a built world, as it does there.
    /// </remarks>
    private DeferredLampState LampState() => new(
        _frameCounter,
        ScreenMarch: RenderCVars.LocalLightShadows.Value != 0,
        Traced: WillTraceLamps() || WillMarchLamps(),
        MaxSourceRadius: RenderCVars.LocalLightSource.Value);

    /// <summary>Whether this frame's lamp shadows are traced.</summary>
    private bool WillTraceLamps() =>
        RenderCVars.LocalLightShadows.Value != 0
        && RenderCVars.HardwareRayTracing.Value
        && RenderCVars.TracedLampShadows.Value
        && _shading?.Variant == DeferredLighting.TracedVariant
        && TracedWorld is { IsWorldReady: true, WorldScene: not null };

    /// <summary>Whether this frame's lamp shadows are marched through the distance field instead.</summary>
    /// <remarks>
    /// Wherever lamp shadows are asked for and the rays cannot be traced -- an adapter without ray tracing,
    /// which is every WebGPU one, or the RT switch off -- and the level has a field to march. It had no
    /// fallback at all: the shading read a full visibility, and every lamp lit through every wall in reach.
    /// </remarks>
    private bool WillMarchLamps() =>
        RenderCVars.LocalLightShadows.Value != 0
        && !WillTraceLamps()
        && _lampField is { Levels: > 0 } field
        && _textures.Find(field.Clipmaps) is not null;

    /// <summary>Traces how much of each pixel's chosen lamp reaches it, for the shading to read this frame.</summary>
    /// <remarks>
    /// One ray per pixel at half resolution, whatever the lamp count; timed as a shadow, as on the Vulkan
    /// device. After the lamps are known and before the fog, which scatters them.
    /// </remarks>
    private void RecordLampShadow(SceneTargetSet scene)
    {
        _lampsTraced = false;

        bool traced = WillTraceLamps() && TracedWorld is { WorldScene: not null };
        bool marched = !traced && WillMarchLamps();
        if (marched)
        {
            EnsureLampMaps();
        }
        if (_localLightCount <= 0 || (!traced && !marched))
        {
            return;
        }

        _tracedLamps ??= new TracedLampShadow(_device, FramesInFlight, _retired);
        _tracedLamps.Ensure(scene.Width, scene.Height);
        _tracedLamps.ClearNew(Encoder());

        DeferredLampBlock.Write(_lampBlock, LampState(), LocalLights);
        TemporalFrameState state = TemporalState(scene);

        // What the lamps must bring to a pixel for any of their shadow to be seen there, from the exposure the
        // last frame was shown at. Their summed light is the same quantity the lighting pass adds -- colour times
        // strength times the windowed falloff times n.l, which the trace already sums to choose its lamps --
        // and a shadow can take away at most all of it. A quarter of the smallest step the display shows, with
        // four times that again for a glossy highlight above the diffuse term: in daylight outdoors the lamps
        // add nothing the eye can see, and their rays were a millisecond and a half of every frame.
        float minimumVisibleLamp = MinimumVisibleLampFraction / MathF.Max(_sceneExposure, 1e-4f);

        // And the sun the lighting pass will add, where this frame traced it: against a sunlit surface the
        // lamps' share decides whether their shadow could be seen at all. Traced just before this.
        IRhiTextureView? sunMask = _sunTraced ? _tracedSun?.Mask : null;
        Vector3f sunDirection = _lighting.WorldSunDirection(_viewToWorld);
        Vector3f sunColour = _lighting.SunColour * _lighting.Scale;
        float sunLuminance = (0.2126f * sunColour.X) + (0.7152f * sunColour.Y) + (0.0722f * sunColour.Z);

        // Held short a few frames after a lamp switched (see NoteLampLevelsChanged).
        bool lightingChanged = _lampLevelChangeFrames > 0;
        if (lightingChanged)
        {
            _lampLevelChangeFrames--;
        }

        TracedLampFrame lampFrame =
            traced
                ? new TracedLampFrame(
                    TracedWorld!.WorldScene,
                    _cameraPosition,
                    _lampBlock,
                    LampFrameIndex,
                    LampFilters,
                    TracedWorld.CutoutMasks,
                    TracedWorld.HitRecordHandle,
                    MinimumVisible: minimumVisibleLamp,
                    SunVisibility: sunMask,
                    SunDirection: sunDirection,
                    SunLuminance: sunLuminance,
                    LightingChanged: lightingChanged,
                    Raw: RawLighting)
                : new TracedLampFrame(
                    null,
                    _cameraPosition,
                    _lampBlock,
                    LampFrameIndex,
                    LampFilters,
                    null,
                    0UL,
                    _textures.Resolve(_lampField!.Value.Clipmaps).View,
                    WriteLampField(_lampField.Value),
                    minimumVisibleLamp,
                    sunMask,
                    sunDirection,
                    sunLuminance,
                    _lampMapArray,
                    _lampMapSampler,
                    lightingChanged,
                    _lampBounceMapArray,
                    WriteLampCoarseBlock(),
                    RawLighting);
        IRhiTextureView? lampVelocity = _velocityRecorded ? scene.View(SceneTargetId.Velocity) : null;

        // Step by step while measuring, so the trace and the two filters are told apart in the breakdown.
        if (_profiler.Enabled)
        {
            IRhiComputePassEncoder? open = null;
            try
            {
                _tracedLamps.Record(
                    null,
                    FrameSlot,
                    scene.View(SceneTargetId.Depth),
                    scene.View(SceneTargetId.Normal),
                    lampFrame,
                    state,
                    lampVelocity,
                    label =>
                    {
                        open?.Dispose();
                        open = Encoder().BeginComputePass(new RhiComputePassDescriptor(
                            _profiler.Time(RenderStage.Shadows, label),
                            label));
                        return open;
                    });
            }
            finally
            {
                open?.Dispose();
            }
        }
        else
        {
            using IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
                Label: "lampshadow"));
            _tracedLamps.Record(
                pass,
                FrameSlot,
                scene.View(SceneTargetId.Depth),
                scene.View(SceneTargetId.Normal),
                lampFrame,
                state,
                lampVelocity);
        }

        _lampsTraced = true;
    }

    /// <summary>The field's corners made relative to the camera, and the rest of its block.</summary>
    private float[] WriteLampField(in LampOcclusionField field)
    {
        int levels = Math.Clamp(field.Levels, 0, 4);
        for (int level = 0; level < 4; level++)
        {
            Vector3d corner = field.CornerOf(level);
            int at = level * 4;
            _lampFieldBlock[at] = (float)(corner.X - _cameraPosition.X);
            _lampFieldBlock[at + 1] = (float)(corner.Y - _cameraPosition.Y);
            _lampFieldBlock[at + 2] = (float)(corner.Z - _cameraPosition.Z);
            _lampFieldBlock[at + 3] = level < field.VoxelSizes.Count ? field.VoxelSizes[level] : 1f;
            _lampFieldBlock[16 + level] = level < field.Ranges.Count ? field.Ranges[level] : 1f;
        }

        _lampFieldBlock[20] = BitConverter.Int32BitsToSingle(levels);
        _lampFieldBlock[21] = BitConverter.Int32BitsToSingle(field.Resolution);
        _lampFieldBlock[22] = BitConverter.Int32BitsToSingle(
            _debugView switch
            {
                DebugView.LampMarch => 1,
                DebugView.LampMaps => 2,
                _ => 0,
            });
        _lampFieldBlock[23] = 0f;
        WriteLampMaps(_lampFieldBlock);
        return _lampFieldBlock;
    }

    /// <summary>Points the shading at the traced lamp visibility and the world, or at stand-ins.</summary>
    private void BindLampShadow() =>
        _shading?.BindLampVisibility(_lampsTraced ? _tracedLamps?.Filtered : null, TracedWorld?.TraceableScene);

    private void DisposeLampShadow()
    {
        _tracedLamps?.Dispose();
        _tracedLamps = null;
    }
}
