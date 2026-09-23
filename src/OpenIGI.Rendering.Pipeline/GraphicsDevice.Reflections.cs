using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Pipeline.Passes;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Sky;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>What a window pane reflects: traced from its front layer and laid over the lit scene.</summary>
/// <remarks>
/// <para>
/// The second half of the glazing. The front layer (see <see cref="GraphicsDevice"/>'s glazing) records
/// each pane pixel's normal and distance; after the frame is lit, one ray per pane pixel is marched
/// through the level's distance field and resolved out of the LIT FRAME where the camera can see what it
/// hit and out of the surface cache's cards where it cannot, and the result is added to the lit scene
/// before the temporal resolve. The software form of the Vulkan original's glass reflection pass: this
/// device traces no rays, so the march is the only half there is.
/// </para>
/// <para>
/// It reads what the game hands over for its own GI: the surface cache, the distance field and the roof
/// map. A frame missing one of them still runs, over stand-ins and a field of nought levels, and the
/// panes then reflect the screen and the sky -- as the original does in the same state.
/// </para>
/// <para>
/// The composite differs from the original's in form and not in answer: that one adds in place from a
/// compute stage, which WGSL cannot do to an rgba16float image, so here the blend unit adds (see
/// <c>Reflection/CompositeDraw.slang</c>).
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    // mat4 + twenty-four vec4s, the trace's Params exactly; see Glazing/Reflect.slang.
    private const int ReflectFloats = 16 + (24 * 4);

    // Where the roof map's transform starts in that block: past everything the trace and the field use.
    private const int ShelterOffset = 16 + (21 * 4);

    // How far a ray reaches, in metres. The cards describe the level's built area and a kilometre covers
    // every level's with room to spare; the original's figure.
    private const float ReflectionReachMetres = 1000f;

    private SurfaceCacheBinding _surfaceCacheBinding = SurfaceCacheBinding.None;
    private DistanceFieldBinding _distanceFieldBinding = DistanceFieldBinding.None;
    private TextureHandle _shelterMap = TextureHandle.None;
    private ShelterMapTransform _shelterTransform = ShelterMapTransform.None;

    private ComputePass? _glassReflect;
    private ComputeParameters? _glassReflectBlock;
    private IRhiBindGroupLayout? _compositeLayout;
    private FullscreenPipeline? _compositeAdd;
    private FullscreenPipeline? _compositeReplace;
    private ShaderBindings? _compositeBindings;
    private StandInTextures? _reflectStandIns;
    private IRhiBuffer? _standInRecords;
    private IRhiSampler? _reflectSampler;

    // How many panes the front layer drew this frame; the trace has nothing to do without one.
    private int _glassPanes;

    // The lens the first-person rig was drawn with this frame, or nought where none was: it is not the
    // frame's, so a reflection has to look for the rig through this one.
    private float _viewModelLensX;
    private float _viewModelLensY;

    /// <inheritdoc/>
    public void SetShelterMap(TextureHandle map, in ShelterMapTransform transform)
    {
        ThrowIfDisposed();
        _shelterMap = map;
        _shelterTransform = transform;
    }

    /// <inheritdoc/>
    public void SetSurfaceCache(in SurfaceCacheBinding binding)
    {
        ThrowIfDisposed();
        _surfaceCacheBinding = binding;
    }

    /// <inheritdoc/>
    public void SetDistanceField(in DistanceFieldBinding binding)
    {
        ThrowIfDisposed();
        _distanceFieldBinding = binding;
    }

    /// <summary>Remembers the lens a first-person draw uses, for the reflection to find the rig through.</summary>
    private void NoteViewModelLens()
    {
        if (_firstPerson)
        {
            _viewModelLensX = _projection.M11;
            _viewModelLensY = _projection.M22;
        }
    }

    /// <summary>Starts a frame's glazing over: no panes yet, and no rig drawn.</summary>
    private void ResetGlazingFrame()
    {
        _glassPanes = 0;
        _viewModelLensX = 0f;
        _viewModelLensY = 0f;
    }

    /// <summary>Traces what the panes reflect and adds it to the lit scene, where there are panes.</summary>
    /// <remarks>
    /// After the frame is lit and its translucency drawn, before the temporal resolve -- where the
    /// original runs it, so a pane reflects the finished scene and the resolve then steadies what a single
    /// ray per pixel finds. Stood down while a debug view is up, except the glazing's own views, which
    /// isolate: each shows something that is not light, so it replaces the lit scene outright.
    /// </remarks>
    private void RecordGlazingReflection()
    {
        if (_scene is not { } scene || _sky is not { } sky || _glassPanes == 0)
        {
            return;
        }

        float route = _debugView switch
        {
            DebugView.GlassRoute => 1f,
            DebugView.GlassAlbedo => 2f,
            DebugView.GlassRay => 3f,
            DebugView.GlassNormal => 4f,
            _ => 0f,
        };

        bool isolate = _debugView is DebugView.Glass || route > 0f;
        if (_debugView is not DebugView.None && !isolate)
        {
            return;
        }

        EnsureGlazingReflection();

        ComputePass reflect = _glassReflect!;
        bool marches = BindReflectionWorld(reflect);

        Span<float> block = stackalloc float[ReflectFloats];
        WriteReflectBlock(block, scene, marches ? _distanceFieldBinding : DistanceFieldBinding.None, isolate && route <= 0f, route);

        reflect.Bind("uLit", scene.View(SceneTargetId.Lit), _reflectSampler!);
        reflect.Bind("uSceneDepth", scene.View(SceneTargetId.Depth), _reflectSampler!);
        reflect.Bind("uGlass", scene.View(SceneTargetId.GlassLayer), _reflectSampler!);
        reflect.Bind("uSkyView", sky.SkyViewTable, _reflectSampler!);
        reflect.Bind("uOut", scene.View(SceneTargetId.Reflection));
        reflect.Bind("p", _glassReflectBlock!.Write<float>(FrameSlot, block));

        using (IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.Reflections),
            "glazing.reflect")))
        {
            reflect.Dispatch(pass, scene.Width, scene.Height);
        }

        RecordReflectionComposite(scene, isolate);
    }

    /// <summary>Binds what the march reads of the level, or stand-ins where the game has handed nothing over.</summary>
    /// <returns><see langword="true"/> when the cards are all there, and a field too for a form that marches one.</returns>
    private bool BindReflectionWorld(ComputePass reflect)
    {
        SurfaceCacheBinding cache = _surfaceCacheBinding;
        DistanceFieldBinding field = _distanceFieldBinding;

        IRhiBuffer? pages = BufferOf(cache.Pages);
        IRhiBuffer? cells = BufferOf(cache.Cells);
        IRhiBuffer? cellPages = BufferOf(cache.CellPages);
        IRhiTextureView? albedo = ViewOf(cache.Albedo);
        IRhiTextureView? depth = ViewOf(cache.Depth);
        IRhiTextureView? lighting = ViewOf(cache.Lighting);
        IRhiTextureView? clipmaps = field.IsPresent ? ViewOf(field.Clipmaps) : null;

        bool complete = cache.IsPresent
            && pages is not null && cells is not null && cellPages is not null
            && albedo is not null && depth is not null && lighting is not null
            && (clipmaps is not null || !reflect.Declares("uClipmaps"));

        IRhiBuffer standIn = _standInRecords!;
        StandInTextures standIns = _reflectStandIns!;
        ShaderReflection reflection = reflect.Program.Reflection;

        reflect.Bind("uPages", complete ? pages! : standIn);
        reflect.Bind("uCells", complete ? cells! : standIn);
        reflect.Bind("uCellPages", complete ? cellPages! : standIn);
        reflect.Bind("uAlbedo", complete ? albedo! : standIns.For(Slot(reflection, "uAlbedo")), _reflectSampler!);
        reflect.Bind("uDepth", complete ? depth! : standIns.For(Slot(reflection, "uDepth")), _reflectSampler!);
        reflect.Bind("uFinal", complete ? lighting! : standIns.For(Slot(reflection, "uFinal")), _reflectSampler!);
        // Only the marching forms walk a field; a traced form declares none.
        if (reflect.Declares("uClipmaps"))
        {
            reflect.Bind("uClipmaps", complete ? clipmaps! : standIns.For(Slot(reflection, "uClipmaps")), _reflectSampler!);
        }

        // The roofs, which only the march reads: whether a ray that left the field found the sky or lost
        // a ceiling. Fetched rather than sampled.
        IRhiTextureView? shelter = _shelterTransform.IsPresent ? ViewOf(_shelterMap) : null;
        reflect.Bind("uShelterMap", shelter ?? standIns.For(Slot(reflection, "uShelterMap")), _reflectSampler!);

        return complete;
    }

    /// <summary>Fills the trace's block, laid out as the original lays it out.</summary>
    private void WriteReflectBlock(Span<float> data, SceneTargetSet scene, in DistanceFieldBinding field, bool raw, float route)
    {
        data.Clear();

        Matrix3f m = _viewToWorld;
        data[0] = m.M11; data[1] = m.M21; data[2] = m.M31;
        data[4] = m.M12; data[5] = m.M22; data[6] = m.M32;
        data[8] = m.M13; data[9] = m.M23; data[10] = m.M33;
        data[15] = 1f;

        data[16] = _unprojection.XScale;
        data[17] = _unprojection.YScale;
        data[18] = _unprojection.Near;
        data[19] = _unprojection.Far;

        data[20] = scene.Width;
        data[21] = scene.Height;
        data[22] = 1f / scene.Width;
        data[23] = 1f / scene.Height;

        // Never traced: this device has no acceleration structure, so every frame marches.
        data[24] = 0f;
        data[25] = ReflectionReachMetres;
        data[26] = RenderCVars.TracedReflectionClamp.Value;
        data[27] = SkyConditions.ViewHeightFor(altitudeMetres: 2f);

        SurfaceCacheBinding cache = _surfaceCacheBinding;
        data[28] = cache.CameraToCache.X;
        data[29] = cache.CameraToCache.Y;
        data[30] = cache.CameraToCache.Z;
        data[31] = cache.LookupCell;

        data[32] = cache.LookupMinimum.X;
        data[33] = cache.LookupMinimum.Y;
        data[34] = cache.LookupMinimum.Z;
        data[35] = cache.LookupBuckets;

        data[36] = cache.AtlasWidth;
        data[37] = cache.AtlasHeight;
        data[38] = cache.BounceScale;
        data[39] = RenderCVars.GlassReflection.Value;

        data[40] = raw ? 1f : 0f;
        data[41] = _viewModelLensX;
        data[42] = _viewModelLensY;
        data[43] = route;

        data[44] = _cameraPosition.X;
        data[45] = _cameraPosition.Y;
        data[46] = _cameraPosition.Z;

        // The material records' address stays nought: only a traced hit reads them.
        Vector3f sun = _lighting.WorldSunDirection(_viewToWorld);
        data[52] = sun.X;
        data[53] = sun.Y;
        data[54] = sun.Z;

        Vector3f sunColour = _lighting.SunColour * _lighting.Scale;
        data[56] = sunColour.X;
        data[57] = sunColour.Y;
        data[58] = sunColour.Z;

        data[60] = _lighting.AmbientColour.X;
        data[61] = _lighting.AmbientColour.Y;
        data[62] = _lighting.AmbientColour.Z;

        // Nought levels is what the march reads as no field to walk.
        data[64] = field.Levels;
        data[65] = field.Resolution;

        for (int level = 0; level < DistanceFieldBinding.MaxLevels && level < field.Levels; level++)
        {
            DistanceFieldLevel described = field[level];
            int origin = 68 + (level * 4);
            data[origin + 0] = described.Origin.X;
            data[origin + 1] = described.Origin.Y;
            data[origin + 2] = described.Origin.Z;
            data[origin + 3] = described.VoxelSize;

            // A scalar array, which the block's layout still spaces four floats apart.
            data[84 + (level * 4)] = described.Range;
        }

        ShelterMapTransform shelter = _shelterTransform;
        data[ShelterOffset] = shelter.OriginX;
        data[ShelterOffset + 1] = shelter.OriginY;
        data[ShelterOffset + 2] = shelter.CellMetres > 0f ? 1f / shelter.CellMetres : 0f;
        data[ShelterOffset + 3] = shelter.LowestZ;
        data[ShelterOffset + 4] = shelter.HeightScale;
        data[ShelterOffset + 5] = shelter.HeadroomMetres;
        data[ShelterOffset + 6] = shelter.ReachMetres;
        data[ShelterOffset + 7] = shelter.IsPresent ? 1f : 0f;
        data[ShelterOffset + 8] = shelter.Width;
        data[ShelterOffset + 9] = shelter.Height;
    }

    /// <summary>Adds the reflection to the lit scene, or puts it there alone for a view that isolates it.</summary>
    private void RecordReflectionComposite(SceneTargetSet scene, bool isolate)
    {
        ShaderBindings bindings = _compositeBindings!;
        bindings.Bind("uReflection", scene.View(SceneTargetId.Reflection));

        using IRhiRenderPassEncoder pass = Encoder().BeginRenderPass(new RhiRenderPassDescriptor(
            [new RhiColorAttachment(scene.View(SceneTargetId.Lit), RhiLoadOp.Load, RhiStoreOp.Store, RhiClearColor.Transparent)],
            Label: "glazing.composite"));

        pass.SetViewport(0, 0, scene.Width, scene.Height, 0f, 1f);
        pass.SetScissor(0, 0, scene.Width, scene.Height);
        (isolate ? _compositeReplace! : _compositeAdd!).Draw(pass, bindings.GroupFor(0), []);
    }

    /// <summary>Builds the trace and the composite, once.</summary>
    private void EnsureGlazingReflection()
    {
        if (_glassReflect is not null)
        {
            return;
        }

        _glassReflect = new ComputePass(_device, ShaderModuleId.GlazingReflect, "glazing.reflect", new ShaderVariant("software"));
        _glassReflectBlock = new ComputeParameters(_device, ReflectFloats * sizeof(float), FramesInFlight, "glazing.reflect");
        _reflectStandIns = new StandInTextures(_device, "glazing.standin");
        _standInRecords = _device.CreateBuffer(new RhiBufferDescriptor(16, RhiBufferUsage.Storage, "glazing.records"));
        _reflectSampler = _device.CreateSampler(new RhiSamplerDescriptor(Label: "glazing.linear"));

        ShaderReflection composite = ShaderCatalog.GetReflection(ShaderModuleId.ReflectCompositeDraw);
        _compositeLayout = _device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            ShaderBindingLayout.EntriesFor(composite, RhiShaderStages.Fragment),
            "glazing.composite"));

        RhiTextureFormat lit = SceneTargetSet.FormatOf(SceneTargetId.Lit);

        // Colour added and the scene's own alpha kept; or the reflection alone, for the isolating views.
        RhiBlendState add = new(
            new RhiBlendComponent(RhiBlendFactor.One, RhiBlendFactor.One, RhiBlendOperation.Add),
            new RhiBlendComponent(RhiBlendFactor.Zero, RhiBlendFactor.One, RhiBlendOperation.Add));

        _compositeAdd = new FullscreenPipeline(_device, ShaderModuleId.ReflectCompositeDraw, _compositeLayout, lit, add);
        _compositeReplace = new FullscreenPipeline(_device, ShaderModuleId.ReflectCompositeDraw, _compositeLayout, lit);
        _compositeBindings = new ShaderBindings(_device, composite, [_compositeLayout], RhiShaderStages.Fragment, "glazing.composite");
    }

    /// <summary>A storage buffer the game made, or null for none.</summary>
    private IRhiBuffer? BufferOf(StorageBufferHandle handle) =>
        handle.IsValid && _storageBuffers.TryGetValue(handle.Id, out IRhiBuffer? buffer) ? buffer : null;

    /// <summary>One slot of a module, by name.</summary>
    private static ShaderBinding Slot(ShaderReflection reflection, string name) =>
        reflection.Bindings.First(binding => string.Equals(binding.Name, name, StringComparison.Ordinal));

    private void DisposeGlazingReflection()
    {
        _compositeBindings?.Dispose();
        _compositeReplace?.Dispose();
        _compositeAdd?.Dispose();
        _compositeLayout?.Dispose();
        _reflectSampler?.Dispose();
        _standInRecords?.Dispose();
        _reflectStandIns?.Dispose();
        _glassReflectBlock?.Dispose();
        _glassReflect?.Dispose();
    }
}
