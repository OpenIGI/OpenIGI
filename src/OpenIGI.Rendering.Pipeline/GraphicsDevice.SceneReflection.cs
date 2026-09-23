using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Sky;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>What the wet ground and the glossy surfaces reflect: the Vulkan renderer's reflection pass.</summary>
/// <remarks>
/// <para>
/// One of two answers a frame, as there: a ray against the world where the adapter traces, the switches
/// allow and the surface cache is lit; a march through the depth buffer and then the level's distance field
/// everywhere else. The same Reflect module and parameter block (<see cref="ReflectionParameters"/>), after
/// the frame is lit and its translucency drawn and before the glazing and the temporal resolve -- so a pane
/// reflects a road with its reflections in, and the resolve steadies what one ray a pixel finds.
/// </para>
/// <para>
/// The composite is the glazing's, the blend unit adding the reflection to the lit scene where the Vulkan
/// renderer adds in place from compute; the answer is the same.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    // How far a traced reflected ray reaches before it counts as having left: the Vulkan renderer's figure.
    private const float TracedReflectionReachMetres = 1000f;

    private ReflectionSettings _reflectionSettings = ReflectionSettings.Off;
    private ComputePass? _sceneReflectMarched;
    private ComputePass? _sceneReflectTraced;
    private ComputeParameters? _sceneReflectBlock;

    /// <inheritdoc/>
    public void SetReflections(in ReflectionSettings settings)
    {
        ThrowIfDisposed();
        _reflectionSettings = settings;
    }

    /// <summary>Whether this frame's reflection traces: the adapter, the switches and a built world.</summary>
    private bool WillTraceReflections() =>
        RenderCVars.HardwareRayTracing.Value
        && RenderCVars.TracedReflections.Value
        && _surfaceCacheBinding.IsPresent
        && TracedWorld is { IsWorldReady: true, WorldScene: not null };

    /// <summary>Reflects the scene into the surfaces that reflect, and adds it to the lit colour.</summary>
    /// <remarks>
    /// Stood down under a debug view, which isolates one term of the lighting; except the reflection's own
    /// view, under which the composite replaces the lit scene so the reflection is the picture.
    /// </remarks>
    private void RecordSceneReflection()
    {
        bool isolate = _debugView is DebugView.Ssr;

        if (_scene is not { } scene || _sky is not { } sky || (_debugView is not DebugView.None && !isolate))
        {
            return;
        }

        EnsureGlazingReflection();
        _sceneReflectBlock ??= new ComputeParameters(
            _device, ReflectionParameters.Floats * sizeof(float), FramesInFlight, "reflect");

        bool traces = WillTraceReflections();
        ComputePass reflect = traces
            ? _sceneReflectTraced ??= new ComputePass(_device, ShaderModuleId.Reflect, "reflect", new ShaderVariant("rayquery"))
            : _sceneReflectMarched ??= new ComputePass(_device, ShaderModuleId.Reflect, "reflect", new ShaderVariant("software"));

        // The cards, and the field where this form marches one; stand-ins where the game handed nothing
        // over, and then the march has only the screen to follow.
        bool complete = BindReflectionWorld(reflect);
        traces &= complete;
        bool marchesField = !traces && complete;

        // Worth running where either answer is there: the screen march, or the world beyond it.
        if (!_reflectionSettings.Enabled || (!_reflectionSettings.Marches && !traces && !marchesField))
        {
            return;
        }

        if (traces)
        {
            reflect.Bind("uScene", TracedWorld!.TraceableScene);
        }

        bool roofs = HasShelterMap();
        Span<float> block = stackalloc float[ReflectionParameters.Floats];
        ReflectionParameters.Write(block, new ReflectionFrame(
            scene.Width,
            scene.Height,
            _viewToWorld,
            _unprojection,
            _reflectionSettings,
            _wetness,
            roofs ? _shelterTransform : ShelterMapTransform.None,
            _cameraPosition,
            SkyConditions.ViewHeightFor(altitudeMetres: 2f),
            (int)(_frameCounter % 64UL),
            ReflectionReachMetres,
            traces,
            TracedReflectionReachMetres,
            RenderCVars.TracedReflectionClamp.Value,
            RenderCVars.TracedReflectionMaxRoughness.Value,
            complete ? ReflectionCacheLookup.Of(_surfaceCacheBinding) : default,
            _viewModelLensX,
            _viewModelLensY,
            marchesField ? _distanceFieldBinding : DistanceFieldBinding.None));

        reflect.Bind("uLit", scene.View(SceneTargetId.Lit), _reflectSampler!);
        reflect.Bind("uSceneDepth", scene.View(SceneTargetId.Depth), _reflectSampler!);
        reflect.Bind("uNormal", scene.View(SceneTargetId.Normal), _reflectSampler!);
        reflect.Bind("uMaterial", scene.View(SceneTargetId.Material), _reflectSampler!);
        reflect.Bind("uBaseColour", scene.View(SceneTargetId.BaseColour), _reflectSampler!);
        reflect.Bind("uSkyView", sky.SkyViewTable, _reflectSampler!);
        reflect.Bind("uReflection", scene.View(SceneTargetId.Reflection));
        reflect.Bind("p", _sceneReflectBlock.Write<float>(FrameSlot, block));

        using (IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.Reflections),
            "reflect")))
        {
            reflect.Dispatch(pass, scene.Width, scene.Height);
        }

        RecordReflectionComposite(scene, isolate);
    }

    private void DisposeSceneReflection()
    {
        _sceneReflectBlock?.Dispose();
        _sceneReflectTraced?.Dispose();
        _sceneReflectMarched?.Dispose();
    }
}
