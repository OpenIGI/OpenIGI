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
    // mat4 + fifteen vec4s: the view-to-world rotation, the unprojection, the resolution, the march's
    // knobs, the wetness/frame block, two blocks of puddle and ripple, three of the shelter map's own
    // transform, and then five of the trace. The layout matches the shader's Params exactly.
    private const int ReflectParamSize = (16 + (24 * 4)) * sizeof(float);

    // Where the shelter map's transform starts, in floats: past the mat4 and the seven vec4s above it.
    private const int ShelterOffset = 16 + (7 * 4);

    // Where the traced half's own block starts: past the mat4 and the ten vec4s the screen march uses.
    private const int TraceOffset = 16 + (10 * 4);

    // And where the distance field's own block starts: past everything above it. Declared in both
    // variants of the shader although only the marched one reads it, so there is one layout rather than
    // two that can drift.
    private const int FieldOffset = 16 + (15 * 4);

    // Its levels, which the standard layout spaces four floats apart in both arrays -- the boxes because
    // they are vec4s, and the ranges although they are scalars.
    private const int FieldOriginOffset = FieldOffset + 4;
    private const int FieldRangeOffset = FieldOriginOffset + (DistanceFieldBinding.MaxLevels * 4);

    // One vec4 of resolution.
    private const int CompositeParamSize = 4 * sizeof(float);

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

        Span<float> data = stackalloc float[ReflectParamSize / sizeof(float)];
        data.Clear();

        Matrix3f m = viewToWorld;
        data[0] = m.M11; data[1] = m.M21; data[2] = m.M31; data[3] = 0f;
        data[4] = m.M12; data[5] = m.M22; data[6] = m.M32; data[7] = 0f;
        data[8] = m.M13; data[9] = m.M23; data[10] = m.M33; data[11] = 0f;
        data[12] = 0f; data[13] = 0f; data[14] = 0f; data[15] = 1f;

        data[16] = unprojection.XScale;
        data[17] = unprojection.YScale;
        data[18] = unprojection.Near;
        data[19] = unprojection.Far;

        data[20] = width;
        data[21] = height;
        data[22] = width > 0 ? 1f / width : 0f;
        data[23] = height > 0 ? 1f / height : 0f;

        data[24] = MathF.Max(settings.Quality, 1);
        data[25] = MathF.Max(settings.Intensity, 0f);
        data[26] = Math.Clamp(settings.MaxRoughness, 0f, 1f);
        data[27] = MathF.Max(settings.Thickness, 0.02f);

        data[28] = MathF.Max(wetness.Amount, 0f);
        data[29] = frameIndex;
        data[30] = MathF.Max(reach, 1f);
        data[31] = viewHeightKm;

        // The wetness, packed exactly as the shading pass packs it into its own uniform. Both are read
        // back into the same WetParams on the other side, which is what keeps the two descriptions of the
        // water one description.
        data[32] = Math.Clamp(wetness.PuddleCoverage, 0f, 1f);
        data[33] = MathF.Max(wetness.PuddleSizeMetres, 0.05f);
        data[34] = Math.Clamp(wetness.RippleStrength, 0f, 1f);
        data[35] = wetness.Seconds;

        data[36] = MathF.Max(wetness.RippleSizeMetres, 0.02f);
        data[37] = Math.Clamp(wetness.WindRippleStrength, 0f, 1f);
        data[38] = cameraX;
        data[39] = cameraY;

        // The shelter map's transform, in world metres. The camera's own Z rides along with it because
        // this pass has the surface only as a camera-relative vector and the map is addressed in the
        // world; the shading pass gets the same number from its uCamera.
        data[40] = shelter.IsPresent ? 1f : 0f;
        data[41] = cameraZ;

        data[ShelterOffset] = shelter.OriginX;
        data[ShelterOffset + 1] = shelter.OriginY;
        data[ShelterOffset + 2] = shelter.CellMetres > 0f ? 1f / shelter.CellMetres : 0f;
        data[ShelterOffset + 3] = shelter.LowestZ;

        data[ShelterOffset + 4] = shelter.HeightScale;
        data[ShelterOffset + 5] = shelter.HeadroomMetres;
        data[ShelterOffset + 6] = shelter.ReachMetres;

        data[ShelterOffset + 8] = shelter.Width;
        data[ShelterOffset + 9] = shelter.Height;

        // Whether this frame traces at all. False on a pass compiled without the traced half, and false
        // whenever the world or the cards are missing -- which is one test rather than several, because
        // a lookup with a grid and no atlas does not degrade into a coarser answer, it reads whatever the
        // descriptor happens to hold. See TracedResources.Traces.
        // Which half actually runs. The traced one only where there IS one and the frame handed over a
        // world to fire at; everything else marches, including a frame on traced hardware whose settings
        // have tracing switched off.
        VulkanComputePass reflect = _traced is not null && traced.Traces ? _traced : _marched;
        bool traces = ReferenceEquals(reflect, _traced);

        data[TraceOffset] = traces ? 1f : 0f;
        data[TraceOffset + 1] = MathF.Max(tracedReach, 1f);
        data[TraceOffset + 2] = MathF.Max(maxRayIntensity, 0f);
        data[TraceOffset + 3] = MathF.Max(traced.BounceScale, 0f);

        data[TraceOffset + 4] = traced.CameraToCache.X;
        data[TraceOffset + 5] = traced.CameraToCache.Y;
        data[TraceOffset + 6] = traced.CameraToCache.Z;
        data[TraceOffset + 7] = traced.LookupCell;

        data[TraceOffset + 8] = traced.LookupMinimum.X;
        data[TraceOffset + 9] = traced.LookupMinimum.Y;
        data[TraceOffset + 10] = traced.LookupMinimum.Z;
        data[TraceOffset + 11] = traced.LookupBuckets;

        data[TraceOffset + 12] = traced.AtlasWidth;
        data[TraceOffset + 13] = traced.AtlasHeight;

        data[TraceOffset + 16] = Math.Clamp(tracedMaxRoughness, 0f, 1f);
        data[TraceOffset + 17] = rigLensX;
        data[TraceOffset + 18] = rigLensY;

        // The distance field. Nought levels is what the marched resolve reads as "no field to walk", and
        // it is also what a traced frame leaves here, which costs that frame nothing because it never
        // compiles the march at all.
        DistanceFieldBinding field = traced.Field;
        data[FieldOffset] = field.Levels;
        data[FieldOffset + 1] = field.Resolution;

        for (int level = 0; level < DistanceFieldBinding.MaxLevels && level < field.Levels; level++)
        {
            DistanceFieldLevel described = field[level];

            int origin = FieldOriginOffset + (level * 4);
            data[origin + 0] = described.Origin.X;
            data[origin + 1] = described.Origin.Y;
            data[origin + 2] = described.Origin.Z;
            data[origin + 3] = described.VoxelSize;

            // A SCALAR array in the block, which the standard layout still spaces four floats apart.
            data[FieldRangeOffset + (level * 4)] = described.Range;
        }

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
