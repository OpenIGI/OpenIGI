using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Shaders;
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
/// and writing the same image would race. See <see cref="ReflectionShaderSource"/> for the march.
/// </remarks>
internal sealed class ReflectionPass : IDisposable
{
    // mat4 + ten vec4s: the view-to-world rotation, the unprojection, the resolution, the SSR knobs,
    // the wetness/frame block, two blocks of puddle and ripple, and then three of the shelter map's own
    // transform. The layout matches the shader's Params exactly.
    private const int ReflectParamSize = (16 + (10 * 4)) * sizeof(float);

    // Where the shelter map's transform starts, in floats: past the mat4 and the seven vec4s above it.
    private const int ShelterOffset = 16 + (7 * 4);

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

        ComputeBindingKind.StorageImage,   // the reflection, written
    ];

    private static readonly ComputeBindingKind[] CompositeBindings =
    [
        ComputeBindingKind.StorageImage,   // the reflection, read
        ComputeBindingKind.StorageImage,   // the lit scene, added to in place
    ];

    private readonly VulkanComputePass _reflect;
    private readonly VulkanComputePass _composite;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ReflectionPass"/> class.</summary>
    public ReflectionPass(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        ShaderCompiler compiler,
        int framesInFlight)
    {
        _reflect = new VulkanComputePass(
            vk, device, allocator, compiler,
            "reflect.comp", ReflectionShaderSource.Reflect,
            ReflectBindings, ReflectParamSize, framesInFlight);

        _composite = new VulkanComputePass(
            vk, device, allocator, compiler,
            "reflectcomposite.comp", ReflectionShaderSource.Composite,
            CompositeBindings, CompositeParamSize, framesInFlight);
    }

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
    public void RecordReflect(
        CommandBuffer cmd,
        int frame,
        ImageView lit,
        ImageView depth,
        ImageView normal,
        ImageView material,
        ImageView skyView,
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
        float reach)
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

        ReadOnlySpan<ComputeResource> resources =
        [
            new ComputeResource(lit),
            new ComputeResource(depth),
            new ComputeResource(normal),
            new ComputeResource(material),
            new ComputeResource(skyView),
            new ComputeResource(shelterMap),
            new ComputeResource(reflection),
        ];

        _reflect.Record(cmd, frame, resources, data, (uint)((width + 7) / 8), (uint)((height + 7) / 8));
    }

    /// <summary>Records the add of the reflection back into the lit scene.</summary>
    public void RecordComposite(
        CommandBuffer cmd,
        int frame,
        ImageView reflection,
        ImageView lit,
        int width,
        int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Span<float> data = stackalloc float[CompositeParamSize / sizeof(float)];
        data.Clear();
        data[0] = width;
        data[1] = height;

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
        _reflect.Dispose();
        _composite.Dispose();
    }
}
