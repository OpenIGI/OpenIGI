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
    // mat4 + five vec4s: the view-to-world rotation, the unprojection, the resolution, the SSR knobs, the
    // wetness/frame block and the puddle block. The layout matches the shader's Params exactly.
    private const int ReflectParamSize = (16 + (5 * 4)) * sizeof(float);

    // One vec4 of resolution.
    private const int CompositeParamSize = 4 * sizeof(float);

    private static readonly ComputeBindingKind[] ReflectBindings =
    [
        ComputeBindingKind.SampledImage,   // the lit scene, the reflection's source
        ComputeBindingKind.SampledImage,   // depth
        ComputeBindingKind.SampledImage,   // world normal
        ComputeBindingKind.SampledImage,   // material
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
    /// <param name="settings">The reflection knobs: steps, intensity, roughness cutoff, thickness.</param>
    /// <param name="wetnessAmount">How wet the ground is, which is what makes it reflective.</param>
    /// <param name="puddleCoverage">How much of the ground stands under water, for the puddle mask.</param>
    /// <param name="puddleSize">The puddle lattice spacing, in metres, matching the shading pass.</param>
    /// <param name="cameraX">The camera's world X, which the puddle lattice is anchored to.</param>
    /// <param name="cameraY">The camera's world Y, which the puddle lattice is anchored to.</param>
    /// <param name="frameIndex">Which frame this is, so the march's dither moves.</param>
    /// <param name="reach">How far, in metres, a reflected ray is marched.</param>
    public void RecordReflect(
        CommandBuffer cmd,
        int frame,
        ImageView lit,
        ImageView depth,
        ImageView normal,
        ImageView material,
        ImageView reflection,
        int width,
        int height,
        in Matrix3f viewToWorld,
        in DepthUnprojection unprojection,
        in ReflectionSettings settings,
        float wetnessAmount,
        float puddleCoverage,
        float puddleSize,
        float cameraX,
        float cameraY,
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

        data[28] = MathF.Max(wetnessAmount, 0f);
        data[29] = frameIndex;
        data[30] = MathF.Max(reach, 1f);
        data[31] = 0f;

        data[32] = Math.Clamp(puddleCoverage, 0f, 1f);
        data[33] = MathF.Max(puddleSize, 0.05f);
        data[34] = cameraX;
        data[35] = cameraY;

        ReadOnlySpan<ComputeResource> resources =
        [
            new ComputeResource(lit),
            new ComputeResource(depth),
            new ComputeResource(normal),
            new ComputeResource(material),
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
