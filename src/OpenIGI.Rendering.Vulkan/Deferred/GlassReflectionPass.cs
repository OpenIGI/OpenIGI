using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Shaders;
using OpenIGI.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// What a window pane reflects, traced after the shading render pass has closed and added into the lit
/// scene.
/// </summary>
/// <remarks>
/// <para>
/// The second half of the glazing. The first half — <see cref="Pipelines.GlassPipeline"/> — draws the
/// transmission inside the render pass and records each pane pixel's normal and distance into a layer;
/// this reads that layer, fires a ray per pane pixel, and resolves what it hit out of the LIT FRAME where
/// the camera can see it and out of the surface cache's cards where it cannot.
/// </para>
/// <para>
/// <b>Running here is the whole point.</b> By this stage of the frame the lit image is finished and
/// readable, and it contains the soldiers, the weapon and the player's hands — everything the static
/// cards do not and never will. A reflection computed inside the render pass could only read the cards,
/// which is why glazing could show a wall but never show the player standing at it.
/// </para>
/// <para>
/// Two dispatches, for the reason the screen-space reflection pass has two: a pane's reflection is read
/// FROM the lit scene and written back INTO it, and one pixel's reflection is another pixel's source.
/// </para>
/// </remarks>
internal sealed class GlassReflectionPass : IDisposable
{
    // mat4 + eight vec4s: the view-to-world rotation, the unprojection, the resolution, the trace's
    // knobs, two blocks describing the surface cache, the debug flag and the camera's world position.
    // Matches the shader's Params exactly.
    private const int ReflectParamSize = (16 + (24 * 4)) * sizeof(float);

    // Where the roof map's transform starts, in floats: past everything the trace and the field use.
    private const int ShelterOffset = 16 + (21 * 4);

    // One vec4 of resolution, as the shared composite declares it.
    private const int CompositeParamSize = 4 * sizeof(float);

    private static readonly ComputeBindingKind[] ReflectBindings =
    [
        ComputeBindingKind.SampledImage,          // the finished frame, the reflection's source
        ComputeBindingKind.SampledImage,          // the scene depth, to know whether a hit is on screen

        // The glazing layer. A storage image the glass draw wrote from inside the render pass, so it is
        // read in the general layout rather than moved between layouts around it.
        ComputeBindingKind.SampledStorageImage,

        ComputeBindingKind.SampledStorageImage,   // the sky table, in the layout the atmosphere leaves it
        ComputeBindingKind.StorageImage,          // where this writes the reflection
    ];

    // What the traced half adds. Appended rather than mixed in, so the bindings above keep their numbers
    // whether or not the adapter can trace.
    private static readonly ComputeBindingKind[] TracedBindings =
    [
        ComputeBindingKind.AccelerationStructure, // the world

        ComputeBindingKind.StorageBuffer,         // the card pages
        ComputeBindingKind.StorageBuffer,         // the lookup grid's buckets
        ComputeBindingKind.StorageBuffer,         // the page indices those buckets point into

        ComputeBindingKind.SampledStorageImage,   // the cards' albedo
        ComputeBindingKind.SampledStorageImage,   // the cards' depth
        ComputeBindingKind.SampledStorageImage,   // the cards' lighting

        // Every world texture reduced to a layer, for shading a hit from its own material. A plain
        // SampledImage rather than the SampledStorageImage the cards use: nothing writes this as a
        // storage image, so it stays in the layout optimal for reading and never needs a transition
        // around a dispatch. VulkanTracedAlbedo leaves it there after each upload.
        ComputeBindingKind.SampledImage,          // the traced albedo array
    ];

    // What the SOFTWARE half adds instead. The cards are the same six -- an adapter that cannot trace
    // still has the whole surface cache -- and what stands in for the acceleration structure is the
    // level's distance field, which a shader can walk without one.
    private static readonly ComputeBindingKind[] SoftwareBindings =
    [
        ComputeBindingKind.StorageBuffer,         // the card pages
        ComputeBindingKind.StorageBuffer,         // the lookup grid's buckets
        ComputeBindingKind.StorageBuffer,         // the page indices those buckets point into

        ComputeBindingKind.SampledStorageImage,   // the cards' albedo
        ComputeBindingKind.SampledStorageImage,   // the cards' depth
        ComputeBindingKind.SampledStorageImage,   // the cards' lighting

        // The clipmaps, written by a compute pass the game drives and read here as a volume.
        ComputeBindingKind.SampledStorageImage,   // the level's distance field

        // And the level's roofs, which only the marched half needs: it is the half that can lose a
        // ceiling and hand back the sky through it. A plain sampled image, fetched and never sampled.
        ComputeBindingKind.SampledImage,          // the roof raster
    ];

    private static readonly ComputeBindingKind[] CompositeBindings =
    [
        ComputeBindingKind.StorageImage,   // the reflection, read
        ComputeBindingKind.StorageImage,   // the lit scene, added to in place
    ];

    // BOTH PERMUTATIONS, on any adapter that can trace. Which one a frame wants is not a property of the
    // hardware -- it is a setting the player flips while the game is running, and a compute pipeline
    // cannot be rebuilt in the middle of a frame. Building the marched half unconditionally is what lets
    // `r_rt_reflections 0` keep its windows: before this, turning tracing off left the pass holding a
    // shader whose only source of hits was an acceleration structure it had been told not to use, so the
    // glazing fell through to the sky and the level stopped appearing in it.
    private readonly VulkanComputePass? _traced;
    private readonly VulkanComputePass _marched;
    private readonly VulkanComputePass _composite;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="GlassReflectionPass"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the passes live on.</param>
    /// <param name="allocator">The allocator the parameter buffers come from.</param>
    /// <param name="framesInFlight">How many frames may be recording at once.</param>
    /// <param name="rayQuery">Whether the adapter can trace, which decides the shader and the layout.</param>
    public GlassReflectionPass(
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
                ShaderModuleId.GlazingReflect,
                new ShaderVariant("rayquery"),
                [.. ReflectBindings, .. TracedBindings],
                ReflectParamSize,
                framesInFlight);
        }

        _marched = new VulkanComputePass(
            vk,
            device,
            allocator,
            ShaderModuleId.GlazingReflect,
            new ShaderVariant("software"),
            [.. ReflectBindings, .. SoftwareBindings],
            ReflectParamSize,
            framesInFlight);

        // The screen-space reflection pass's own composite, verbatim: add an image to the lit scene, and
        // nothing else. Its OWN instance rather than that pass's, because a descriptor set is read when
        // the dispatch executes and not when it is recorded — two dispatches sharing one set in a frame
        // would both see whichever resources were written last.
        _composite = new VulkanComputePass(
            vk,
            device,
            allocator,
            ShaderModuleId.ReflectComposite,
            CompositeBindings,
            CompositeParamSize,
            framesInFlight);
    }

    /// <summary>Gets whether this pass was built with a traced half at all.</summary>
    /// <remarks>
    /// Asks what the ADAPTER can do, not what this frame will do. A pass that answers yes still marches
    /// the field on any frame the settings, the world or the cache leave it nothing to trace.
    /// </remarks>
    public bool Traces => _traced is not null;

    /// <summary>How many resources the pass declares, ahead of its parameter block.</summary>
    /// <param name="rayQuery">Whether the traced variant is being asked about.</param>
    /// <returns>The count.</returns>
    /// <remarks>
    /// Exposed for a test. The shader's binding numbers and this list are two hand-written descriptions
    /// of one descriptor set, and adding a resource to the shader without adding its kind here throws at
    /// the first dispatch of a level — which is a crash on the player's machine rather than a red test,
    /// because nothing at build time reads the GLSL.
    /// </remarks>
    internal static int BindingCount(bool rayQuery) =>
        ReflectBindings.Length + (rayQuery ? TracedBindings.Length : SoftwareBindings.Length);

    /// <summary>Records the trace that fills the reflection image at every pane pixel.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <param name="lit">The finished frame, read as the reflection's source.</param>
    /// <param name="depth">The scene depth.</param>
    /// <param name="glass">The glazing layer the glass draw recorded.</param>
    /// <param name="skyView">The sky table.</param>
    /// <param name="reflection">The image the reflection is written to.</param>
    /// <param name="width">The target's width in pixels.</param>
    /// <param name="height">The target's height in pixels.</param>
    /// <param name="viewToWorld">The view-to-camera-relative-world rotation.</param>
    /// <param name="unprojection">The projection's scales and planes.</param>
    /// <param name="traced">The world and the lit cards, or placeholders on a frame that traces nothing.</param>
    /// <param name="reach">How far a ray reaches, in metres.</param>
    /// <param name="clamp">The ceiling on one ray's radiance.</param>
    /// <param name="viewHeightKm">Where the camera stands, for the sky table's own parameterisation.</param>
    /// <param name="strength">How much of the reflection reaches the picture.</param>
    /// <param name="raw">Whether to write the reflection unweighted, for the glazing debug view.</param>
    /// <param name="route">
    /// Which debug read-out to write instead of the reflection: nought for none, one for the resolve
    /// route (<c>DebugView.GlassRoute</c>) and two for the raw albedo at the hit
    /// (<c>DebugView.GlassAlbedo</c>). Takes precedence over <paramref name="raw"/>, and no two are ever
    /// asked for together because a debug view is one value.
    /// </param>
    /// <param name="rigLensX">The first-person lens x scale, or zero where no rig was drawn.</param>
    /// <param name="rigLensY">
    /// The first-person lens y scale. The rig is not projected with the frame's lens, so a reflection
    /// has to look for it through this one.
    /// </param>
    /// <param name="cameraWorld">
    /// Where the camera actually stands in the world. The ray is fired in the space the acceleration
    /// structure was built in, which is the world — NOT the surface cache's space, which is what the
    /// card lookup uses. Confusing the two makes every ray miss.
    /// </param>
    /// <param name="sun">The direction towards the sun, in world space.</param>
    /// <param name="sunColour">The sun's colour, already scaled by its strength.</param>
    /// <param name="ambient">
    /// What a surface facing away from the sun still receives. Only a hit shaded from its own material
    /// reads any of these three; the frame and the cards arrive already lit.
    /// </param>
    /// <param name="albedoSampler">
    /// The wrapping sampler the albedo array must be read through, or nothing on a frame that shades no
    /// hits. See <see cref="RayTracing.VulkanTracedAlbedo.Sampler"/>.
    /// </param>
    /// <param name="shelterMap">The level's roofs seen from above, or the placeholder where there are none.</param>
    /// <param name="shelter">
    /// That map's transform. Read only by the marched half, and for one question: a reflected ray that
    /// runs off the end of the distance field has found the sky out of doors and has LOST A CEILING under
    /// a roof, and only this can tell the two apart.
    /// </param>
    public void RecordReflect(
        CommandBuffer cmd,
        int frame,
        ImageView lit,
        ImageView depth,
        ImageView glass,
        ImageView skyView,
        ImageView reflection,
        int width,
        int height,
        in Matrix3f viewToWorld,
        in DepthUnprojection unprojection,
        in TracedResources traced,
        float reach,
        float clamp,
        float viewHeightKm,
        float strength,
        bool raw,
        float route,
        in Vector3f cameraWorld,
        float rigLensX,
        float rigLensY,
        in Vector3f sun,
        in Vector3f sunColour,
        in Vector3f ambient,
        ImageView shelterMap,
        in ShelterMapTransform shelter,
        Sampler albedoSampler = default)
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
        data[22] = 1f / Math.Max(width, 1);
        data[23] = 1f / Math.Max(height, 1);

        // Which half actually runs. The traced one only where there IS one and the frame handed over a
        // world to fire at; everything else marches, including a frame on traced hardware whose settings
        // have tracing switched off.
        VulkanComputePass reflect = _traced is not null && traced.Traces ? _traced : _marched;
        bool traces = ReferenceEquals(reflect, _traced);
        data[24] = traces ? 1f : 0f;
        data[25] = reach;
        data[26] = clamp;
        data[27] = viewHeightKm;

        data[28] = traced.CameraToCache.X;
        data[29] = traced.CameraToCache.Y;
        data[30] = traced.CameraToCache.Z;
        data[31] = traced.LookupCell;

        data[32] = traced.LookupMinimum.X;
        data[33] = traced.LookupMinimum.Y;
        data[34] = traced.LookupMinimum.Z;
        data[35] = traced.LookupBuckets;

        data[36] = traced.AtlasWidth;
        data[37] = traced.AtlasHeight;
        data[38] = traced.BounceScale;
        data[39] = strength;

        data[40] = raw ? 1f : 0f;
        data[41] = rigLensX;
        data[42] = rigLensY;
        data[43] = route;

        data[44] = cameraWorld.X;
        data[45] = cameraWorld.Y;
        data[46] = cameraWorld.Z;

        // The records' device address as a bit pattern, the way the shadow pass carries the same thing:
        // a uniform block cannot hold a 64-bit integer without an extension, so the two halves ride
        // across as floats and the shader reads them back as words.
        data[48] = BitConverter.UInt32BitsToSingle((uint)traced.RecordAddress);
        data[49] = BitConverter.UInt32BitsToSingle((uint)(traced.RecordAddress >> 32));

        data[52] = sun.X;
        data[53] = sun.Y;
        data[54] = sun.Z;

        data[56] = sunColour.X;
        data[57] = sunColour.Y;
        data[58] = sunColour.Z;

        data[60] = ambient.X;
        data[61] = ambient.Y;
        data[62] = ambient.Z;

        // The distance field. Nought levels is what the software resolve reads as "no field to walk", and
        // it is also what a traced frame leaves here, which costs that frame nothing because it never
        // compiles the march at all.
        DistanceFieldBinding field = traced.Field;
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

            // A SCALAR array in the block, which the standard layout still spaces four floats apart.
            data[84 + (level * 4)] = described.Range;
        }

        // The roof map's transform, in world metres, packed exactly as the reflection pass packs its own
        // copy: both are read back into the same ShelterMap on the other side, which is what keeps the
        // two descriptions one description.
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

        ReadOnlySpan<ComputeResource> screen =
        [
            new ComputeResource(lit),
            new ComputeResource(depth),
            new ComputeResource(glass),
            new ComputeResource(skyView),
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
                new ComputeResource(shelterMap),
            ];

            reflect.Record(cmd, frame, software, data, groupsX, groupsY);
            return;
        }

        // Every descriptor the set declares is written whether or not this frame traces: a set with a
        // hole in it is undefined to BIND, not merely undefined to read. A frame with nothing to trace
        // hands over the placeholders TracedResources carries and a zero in the parameters.
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

            // Always a real view here: the array is constructed in the same branch as the world scene,
            // and a frame with no world scene never reaches this path. It brings its own WRAPPING
            // sampler: these are world textures whose UVs run past one, and the pass's own sampler
            // clamps. See VulkanTracedAlbedo.Sampler.
            new ComputeResource(traced.AlbedoArray, default, default, 0, albedoSampler),
        ];

        reflect.Record(cmd, frame, all, data, groupsX, groupsY);
    }

    /// <summary>Adds the glazing's reflection into the lit scene.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <param name="reflection">The image the trace filled.</param>
    /// <param name="lit">The lit scene, added to in place.</param>
    /// <param name="width">The target's width in pixels.</param>
    /// <param name="height">The target's height in pixels.</param>
    /// <param name="isolate">Whether to REPLACE the lit scene, for the debug view.</param>
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
