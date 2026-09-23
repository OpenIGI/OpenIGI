using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Shaders;
using OpenIGI.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// What the wet ground and the glossy surfaces reflect of the scene standing over them, marched in
/// screen space after the frame is lit.
/// </summary>
/// <remarks>
/// Two dispatches: the first reflects the view ray about each reflective surface and walks the depth
/// buffer to find what the camera drew along it, writing the result to its own image; the second adds
/// that image back into the lit scene. They are split because the reflection is READ from the lit scene
/// and WRITTEN back into it, and one pixel's reflection is another's source — so a single pass reading
/// and writing the same image would race. The march itself is <c>Source/Reflection/Reflect.slang</c>.
/// </remarks>
internal sealed class ReflectionPass : IDisposable
{
    private const int ReflectParamSize = ReflectionParameters.Floats * sizeof(float);

    // One vec4 of resolution.
    private const int CompositeParamSize = ReflectionParameters.CompositeFloats * sizeof(float);

    private static readonly ComputeBindingKind[] ReflectBindings =
    [
        ComputeBindingKind.SampledImage,   // the lit scene, the reflection's source
        ComputeBindingKind.SampledImage,   // depth
        ComputeBindingKind.SampledImage,   // world normal
        ComputeBindingKind.SampledImage,   // material

        // The sky table, in the general layout the atmosphere passes leave it in. Read so that a marched
        // reflection can take the place of the sky the shading already laid in the water rather than
        // being piled on top of it.
        ComputeBindingKind.SampledStorageImage,

        // The level's roofs, seen from above. Read for exactly the reason the shading reads it: no water
        // stands under a roof, so none reflects, and the march has to agree about that with the pass
        // that decided where the water was.
        ComputeBindingKind.SampledImage,

        // The albedo, for the one thing a reflection needs it for: a metal reflects its own colour.
        ComputeBindingKind.SampledImage,

        ComputeBindingKind.StorageImage,   // the reflection, written
    ];

    // What the traced half adds, on an adapter that can trace: the world to fire a ray at, and the
    // surface cache to turn where it landed into light. Appended to the list above rather than mixed into
    // it, because the shader's binding numbers are the list's own indices -- so the screen march's
    // bindings keep the numbers they have whether or not this is present.
    private static readonly ComputeBindingKind[] TracedBindings =
    [
        ComputeBindingKind.AccelerationStructure,  // the world

        ComputeBindingKind.StorageBuffer,          // the card pages
        ComputeBindingKind.StorageBuffer,          // the lookup grid's buckets
        ComputeBindingKind.StorageBuffer,          // the page indices those buckets point into

        // The three card atlases. All storage images the engine's own lighting passes write, so they are
        // read in the general layout rather than moved between layouts around every frame -- the same
        // arrangement the sky table above has.
        ComputeBindingKind.SampledStorageImage,    // the cards' albedo
        ComputeBindingKind.SampledStorageImage,    // the cards' depth
        ComputeBindingKind.SampledStorageImage,    // the cards' lighting
    ];

    // What the SOFTWARE half adds instead, on an adapter that cannot trace -- and the cards are the
    // same six, which is the point. Such an adapter is not missing the surface cache, only the
    // acceleration structure, so the one substitution is the level's distance field: the only description
    // of the world a shader can walk without a structure to trace. Seven either way, so the parameter
    // block keeps its binding whichever variant is compiled.
    private static readonly ComputeBindingKind[] SoftwareBindings =
    [
        ComputeBindingKind.StorageBuffer,           // the card pages
        ComputeBindingKind.StorageBuffer,           // the lookup grid's buckets
        ComputeBindingKind.StorageBuffer,           // the page indices those buckets point into

        ComputeBindingKind.SampledStorageImage,     // the cards' albedo
        ComputeBindingKind.SampledStorageImage,     // the cards' depth
        ComputeBindingKind.SampledStorageImage,     // the cards' lighting

        // The clipmaps, written by a compute pass the game drives and read here as a volume.
        ComputeBindingKind.SampledStorageImage,     // the level's distance field
    ];

    private static readonly ComputeBindingKind[] CompositeBindings =
    [
        ComputeBindingKind.StorageImage,   // the reflection, read
        ComputeBindingKind.StorageImage,   // the lit scene, added to in place
    ];

    // BOTH PERMUTATIONS, on any adapter that can trace. Which one a frame wants is not a property of
    // the hardware: r_rt and r_rt_reflections are flipped while the game runs, and a compute pipeline
    // cannot be rebuilt mid-frame. The marched half is therefore built unconditionally, and it is what
    // stands behind the screen march on every frame that is not tracing.
    private readonly VulkanComputePass? _traced;
    private readonly VulkanComputePass _marched;
    private readonly VulkanComputePass _composite;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ReflectionPass"/> class.</summary>
    public ReflectionPass(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        int framesInFlight,
        bool rayQuery)
    {
        if (rayQuery)
        {
            _traced = new VulkanComputePass(
                vk,
                device,
                allocator,
                ShaderModuleId.Reflect,
                new ShaderVariant("rayquery"),
                [.. ReflectBindings, .. TracedBindings],
                ReflectParamSize, framesInFlight);
        }

        _marched = new VulkanComputePass(
            vk,
            device,
            allocator,
            ShaderModuleId.Reflect,
            new ShaderVariant("software"),
            [.. ReflectBindings, .. SoftwareBindings],
            ReflectParamSize, framesInFlight);

        _composite = new VulkanComputePass(
            vk,
            device,
            allocator,
            ShaderModuleId.ReflectComposite,
            CompositeBindings, CompositeParamSize, framesInFlight);
    }

    /// <summary>Gets whether this pass was built with a traced half at all.</summary>
    /// <remarks>
    /// A compile-time property of the pass rather than a setting: the ray-query extension has to be
    /// declared for the shader to parse, so an adapter that cannot trace gets a shader with no trace in
    /// it. Asked by the caller so that it can say so rather than silently handing over resources nothing
    /// will read.
    /// </remarks>
    public bool Traces => _traced is not null;

    /// <summary>How many resources the pass declares, ahead of its parameter block.</summary>
    /// <param name="rayQuery">Whether the traced variant is being asked about.</param>
    /// <returns>The count.</returns>
    /// <remarks>
    /// Exposed for a test, for the reason the glazing pass exposes the same thing: the shader's binding
    /// numbers and this list are two hand-written descriptions of one descriptor set, and a resource
    /// added to one alone throws at the first dispatch of a level rather than at build time.
    /// </remarks>
    internal static int BindingCount(bool rayQuery) =>
        ReflectBindings.Length + (rayQuery ? TracedBindings.Length : SoftwareBindings.Length);

    /// <summary>Records the screen-space march that fills the reflection image.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <param name="lit">The lit scene, read as the reflection's source.</param>
    /// <param name="depth">The scene depth.</param>
    /// <param name="normal">The world normals.</param>
    /// <param name="material">The packed material.</param>
    /// <param name="reflection">The image the reflection is written to.</param>
    /// <param name="width">The full-resolution width.</param>
    /// <param name="height">The full-resolution height.</param>
    /// <param name="viewToWorld">The view-space to camera-relative-world rotation.</param>
    /// <param name="unprojection">How a depth sample becomes a view position.</param>
    /// <param name="skyView">The sky table, for the term a marched reflection stands in place of.</param>
    /// <param name="settings">The reflection knobs: steps, intensity, roughness cutoff, thickness.</param>
    /// <param name="wetness">
    /// The whole of the weather, not a few fields of it. The march has to arrive at the same water the
    /// shading drew — the same puddles, rippled the same way at the same instant — which takes the ripple
    /// lattice and its clock as much as it takes the coverage.
    /// </param>
    /// <param name="shelterMap">The level's roofs, seen from above: no water stands under one.</param>
    /// <param name="shelter">How to read that map, or <see cref="ShelterMapTransform.None"/> for none.</param>
    /// <param name="cameraX">The camera's world X, which the puddle lattice is anchored to.</param>
    /// <param name="cameraY">The camera's world Y, which the puddle lattice is anchored to.</param>
    /// <param name="cameraZ">The camera's world Z, which the shelter map is addressed in.</param>
    /// <param name="viewHeightKm">The camera's height, for reading the sky table the same way.</param>
    /// <param name="frameIndex">Which frame this is, so the march's dither moves.</param>
    /// <param name="reach">How far, in metres, a reflected ray is marched.</param>
    /// <param name="baseColour">The albedo, which is what a metal's reflection is tinted by.</param>
    /// <param name="traced">
    /// The world and the lit cards, or <see cref="TracedResources.None"/> for a frame that is not to
    /// trace. Ignored outright on a pass compiled without the traced half.
    /// </param>
    /// <param name="maxRayIntensity">The ceiling on one traced ray's radiance, which stops a firefly.</param>
    /// <param name="tracedReach">How far, in metres, a traced ray reaches before it counts as having left.</param>
    /// <param name="rigLensX">The first-person lens x scale, or zero where no rig was drawn.</param>
    /// <param name="rigLensY">
    /// The first-person lens y scale. The rig is not projected with the frame's lens, so a reflection
    /// has to look for it through this one.
    /// </param>
    /// <param name="tracedMaxRoughness">
    /// The roughest surface a traced ray is fired for. Deliberately not the march's own ceiling — see
    /// <c>r_rt_reflection_roughness</c> for why one number cannot serve both.
    /// </param>
    public void RecordReflect(
        CommandBuffer cmd,
        int frame,
        ImageView lit,
        ImageView depth,
        ImageView normal,
        ImageView material,
        ImageView skyView,
        ImageView baseColour,
        ImageView reflection,
        int width,
        int height,
        in Matrix3f viewToWorld,
        in DepthUnprojection unprojection,
        in ReflectionSettings settings,
        in WetnessSettings wetness,
        ImageView shelterMap,
        in ShelterMapTransform shelter,
        float cameraX,
        float cameraY,
        float cameraZ,
        float viewHeightKm,
        int frameIndex,
        float reach,
        in TracedResources traced,
        float maxRayIntensity,
        float tracedReach,
        float tracedMaxRoughness,
        float rigLensX,
        float rigLensY)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Which half actually runs. The traced one only where there IS one and the frame handed over a
        // world to fire at; everything else marches, including a frame on traced hardware whose settings
        // have tracing switched off.
        VulkanComputePass reflect = _traced is not null && traced.Traces ? _traced : _marched;
        bool traces = ReferenceEquals(reflect, _traced);

        Span<float> data = stackalloc float[ReflectParamSize / sizeof(float)];
        ReflectionParameters.Write(data, new ReflectionFrame(
            width,
            height,
            viewToWorld,
            unprojection,
            settings,
            wetness,
            shelter,
            new Vector3f(cameraX, cameraY, cameraZ),
            viewHeightKm,
            frameIndex,
            reach,
            traces,
            tracedReach,
            maxRayIntensity,
            tracedMaxRoughness,
            new ReflectionCacheLookup(
                traced.CameraToCache,
                traced.LookupCell,
                traced.LookupMinimum,
                traced.LookupBuckets,
                traced.AtlasWidth,
                traced.AtlasHeight,
                traced.BounceScale),
            rigLensX,
            rigLensY,

            // Nought levels on a traced frame, which never compiles the march at all.
            traced.Field));

        ReadOnlySpan<ComputeResource> screen =
        [
            new ComputeResource(lit),
            new ComputeResource(depth),
            new ComputeResource(normal),
            new ComputeResource(material),
            new ComputeResource(skyView),
            new ComputeResource(shelterMap),
            new ComputeResource(baseColour),
            new ComputeResource(reflection),
        ];

        uint groupsX = (uint)((width + 7) / 8);
        uint groupsY = (uint)((height + 7) / 8);

        if (!traces)
        {
            // The software set: the same cards a traced frame resolves against, plus the field this one
            // marches instead of tracing.
            ReadOnlySpan<ComputeResource> software =
            [
                .. screen,
                ComputeResource.Records(traced.Pages, traced.PageBytes),
                ComputeResource.Records(traced.Cells, traced.CellBytes),
                ComputeResource.Records(traced.CellPages, traced.CellPageBytes),
                new ComputeResource(traced.CardAlbedo),
                new ComputeResource(traced.CardDepth),
                new ComputeResource(traced.CardLighting),
                new ComputeResource(traced.DistanceFieldVolume),
            ];

            reflect.Record(cmd, frame, software, data, groupsX, groupsY);
            return;
        }

        // Every descriptor the layout declares has to be written whether or not this frame traces: a
        // descriptor set with a hole in it is undefined to bind, not merely unread. So a frame with no
        // world and no cards still binds the placeholders the caller hands over, and is told by the
        // parameter above to fire no rays at them -- the same arrangement the lamps' traced shadow has.
        ReadOnlySpan<ComputeResource> all =
        [
            .. screen,
            ComputeResource.Scene(traced.Scene),
            ComputeResource.Records(traced.Pages, traced.PageBytes),
            ComputeResource.Records(traced.Cells, traced.CellBytes),
            ComputeResource.Records(traced.CellPages, traced.CellPageBytes),
            new ComputeResource(traced.CardAlbedo),
            new ComputeResource(traced.CardDepth),
            new ComputeResource(traced.CardLighting),
        ];

        reflect.Record(cmd, frame, all, data, groupsX, groupsY);
    }

    /// <summary>Records the add of the reflection back into the lit scene.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <param name="reflection">The reflection image the march and the trace filled.</param>
    /// <param name="lit">The lit scene to add it to.</param>
    /// <param name="width">The full-resolution width.</param>
    /// <param name="height">The full-resolution height.</param>
    /// <param name="isolate">
    /// Whether to REPLACE the lit scene with the reflection rather than add to it — the debug view that
    /// shows what surfaces reflect on its own.
    /// </param>
    public void RecordComposite(
        CommandBuffer cmd,
        int frame,
        ImageView reflection,
        ImageView lit,
        int width,
        int height,
        bool isolate = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Span<float> data = stackalloc float[CompositeParamSize / sizeof(float)];
        data.Clear();
        data[0] = width;
        data[1] = height;
        data[2] = isolate ? 1f : 0f;

        ReadOnlySpan<ComputeResource> resources =
        [
            new ComputeResource(reflection),
            new ComputeResource(lit),
        ];

        _composite.Record(cmd, frame, resources, data, (uint)((width + 7) / 8), (uint)((height + 7) / 8));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _traced?.Dispose();
        _marched.Dispose();
        _composite.Dispose();
    }
}
