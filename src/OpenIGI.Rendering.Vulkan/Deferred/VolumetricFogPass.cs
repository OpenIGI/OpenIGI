using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Shaders;
using OpenIGI.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// The volumetric fog: a froxel volume lit once and integrated into what each pixel sees through it.
/// </summary>
/// <remarks>
/// <para>
/// The grid is a frustum in disguise — screen tiles across and down, depth slices deep, so a cell is a
/// truncated pyramid of air rather than a cube. That is what makes a lookup from the shading pass a
/// single texture fetch: the pixel's own screen position and depth ARE the coordinate. See
/// the FogScatter and FogIntegrate modules for the two dispatches and what each is ported from.
/// </para>
/// <para>
/// <b>Sizing.</b> Sixteen pixels to a froxel across, against the reference's eight. The reference culls
/// lights into a grid before it lights a froxel; this pass tests every lamp against every cell, so its
/// cost grows with the cell count in a way the reference's does not, and sixteen quarters that count for
/// a medium whose whole nature is low frequency. The depth slices come from the quality dial, which is
/// where the fog's cost actually lives.
/// </para>
/// <para>
/// <b>Three volumes, not one.</b> The scattering volume is written this frame and read as history the
/// next, so it ping-pongs; the integrated volume is what the shading pass samples. Half floats
/// throughout — the values are radiance and a transmittance, neither of which wants more than that, and
/// the whole set at 1600×900 with 96 slices is under fifteen megabytes.
/// </para>
/// </remarks>
internal sealed class VolumetricFogPass : IDisposable
{
    private const int ParamSize = VolumetricFogParameters.Floats * sizeof(float);

    private static readonly ComputeBindingKind[] ScatterBindings =
    [
        ComputeBindingKind.SampledImage,          // the cascaded shadow map

        // The sky's harmonics are WRITTEN by a compute pass of their own and so live in the general
        // layout for good, which is how the lighting binds them too. Declaring them as an ordinary
        // sampled image bound them as shader-read-only instead, and reading an image through a
        // descriptor whose layout is not the one it is in is undefined -- the validation layer says so
        // on every dispatch, and a driver is entitled to return anything for the ambient term.
        ComputeBindingKind.SampledStorageImage,   // the sky's harmonics
        ComputeBindingKind.SampledStorageImage,   // the previous frame's scattering, as history
        ComputeBindingKind.StorageVolume,         // this frame's scattering, written

        // The level's roofs, seen from above. What makes the medium stop at a wall instead of the whole
        // march being switched off indoors -- which took the yard's fog with it, so a compound seen
        // from a doorway was drawn in clear air.
        ComputeBindingKind.SampledImage,
    ];

    private static readonly ComputeBindingKind[] IntegrateBindings =
    [
        ComputeBindingKind.SampledStorageImage,   // this frame's scattering, read back
        ComputeBindingKind.StorageVolume,         // the integrated result the shading pass samples
    ];

    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanAllocator _allocator;
    private readonly VulkanCommands _commands;
    private readonly VulkanComputePass _scatter;
    private readonly VulkanComputePass _integrate;

    private readonly VulkanStorageImage?[] _scattering = new VulkanStorageImage?[2];
    private VulkanStorageImage? _integrated;
    private int _gridX;
    private int _gridY;
    private int _gridZ;
    private int _current;
    private bool _hasHistory;
    private ulong _frameIndex;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VolumetricFogPass"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the volumes live on.</param>
    /// <param name="allocator">The allocator backing them.</param>
    /// <param name="commands">The pool their first layout transition comes from.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    public VolumetricFogPass(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        int framesInFlight)
    {
        _vk = vk;
        _device = device;
        _allocator = allocator;
        _commands = commands;

        _scatter = new VulkanComputePass(
            vk, device, allocator, ShaderModuleId.FogScatter,
            ScatterBindings, ParamSize, framesInFlight);

        _integrate = new VulkanComputePass(
            vk, device, allocator, ShaderModuleId.FogIntegrate,
            IntegrateBindings, ParamSize, framesInFlight);
    }

    /// <summary>Gets the integrated volume the shading pass samples, or null before one is built.</summary>
    public VulkanStorageImage? Integrated => _integrated;

    /// <summary>Gets the depth distribution the shading pass needs to find a pixel's slice.</summary>
    /// <remarks>
    /// The same numbers the shaders use, so the lookup lands on the slice the integration wrote.
    /// </remarks>
    public FogSliceDistribution ZParams { get; private set; }

    /// <summary>Forgets the accumulated history, for a camera that has moved somewhere unrelated.</summary>
    public void Invalidate() => _hasHistory = false;

    /// <summary>Sizes the volumes for a render resolution and a quality, rebuilding them if either moved.</summary>
    /// <param name="width">The render width in pixels.</param>
    /// <param name="height">The render height in pixels.</param>
    /// <param name="slices">How many depth slices the quality dial asks for.</param>
    /// <param name="retire">
    /// Where the volumes being replaced are handed for release once the frames still reading them have
    /// finished. Passing nothing frees them AT ONCE, which is only safe with no frame in flight.
    /// </param>
    /// <returns><see langword="true"/> when a volume of some size exists.</returns>
    /// <remarks>
    /// The rebuild is what a quality change costs: the grid's depth comes from the dial, so moving it
    /// resizes three volumes. They cannot simply be destroyed. A frame or two is still executing against
    /// the old ones -- their dispatches name those images, and their descriptor sets name those views --
    /// and freeing an image the GPU is reading is not a leak but a lost device, which is exactly how
    /// this first showed itself: changing the fog quality from the menu killed the adapter mid-frame.
    /// </remarks>
    public bool Ensure(int width, int height, int slices, Action<IDisposable>? retire = null)
    {
        (int gridX, int gridY, int gridZ) = VolumetricFogParameters.Grid(width, height, slices);

        if (_integrated is not null && gridX == _gridX && gridY == _gridY && gridZ == _gridZ)
        {
            return true;
        }

        Release(retire);

        _gridX = gridX;
        _gridY = gridY;
        _gridZ = gridZ;
        _hasHistory = false;

        for (int i = 0; i < _scattering.Length; i++)
        {
            _scattering[i] = new VulkanStorageImage(
                _vk, _device, _allocator, _commands, gridX, gridY, gridZ, VolumeFormat.Rgba16F);
        }

        _integrated = new VulkanStorageImage(
            _vk, _device, _allocator, _commands, gridX, gridY, gridZ, VolumeFormat.Rgba16F);

        return true;
    }

    /// <summary>Lights the grid and integrates it, for one frame.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <param name="fog">What this frame's medium and lights are.</param>
    /// <param name="shadowMap">The cascaded shadow map's array view.</param>
    /// <param name="skyIrradiance">The sky's harmonics, as the lighting pass reads them.</param>
    /// <param name="viewToWorld">This frame's view rotation.</param>
    /// <param name="previousViewToWorld">The previous frame's, for the history lookup.</param>
    /// <param name="cameraDelta">This frame's camera position minus the previous frame's.</param>
    /// <param name="unprojection">This frame's projection scales and planes.</param>
    /// <param name="previousUnprojection">The previous frame's, unjittered.</param>
    /// <param name="cascadeMatrices">Each cascade's camera-relative world to light clip.</param>
    /// <param name="cascadeSplits">The view depth each cascade stops at.</param>
    /// <param name="cascadeCount">How many cascades are live.</param>
    /// <param name="shelterMap">The level's roofs seen from above; no fog stands under one.</param>
    /// <param name="shelter">How to read that map, or <see cref="ShelterMapTransform.None"/> for none.</param>
    /// <param name="cameraWorld">Where the camera stands in the world, which the map is addressed in.</param>
    public unsafe void Record(
        CommandBuffer cmd,
        int frame,
        in VolumetricFogFrame fog,
        ImageView shadowMap,
        ImageView skyIrradiance,
        Matrix3f viewToWorld,
        Matrix3f previousViewToWorld,
        Vector3f cameraDelta,
        DepthUnprojection unprojection,
        DepthUnprojection previousUnprojection,
        Matrix4f[] cascadeMatrices,
        float[] cascadeSplits,
        int cascadeCount,
        ImageView shelterMap,
        in ShelterMapTransform shelter,
        Vector3f cameraWorld)
    {
        if (_integrated is null || _scattering[0] is null || _scattering[1] is null)
        {
            return;
        }

        // A camera that jumped did not travel; nothing it accumulated describes where it is now.
        if (cameraDelta.LengthSquared > VolumetricFogParameters.TeleportMetres * VolumetricFogParameters.TeleportMetres)
        {
            _hasHistory = false;
        }

        VulkanStorageImage target = _scattering[_current]!;
        VulkanStorageImage history = _scattering[_current ^ 1]!;
        VulkanStorageImage integrated = _integrated;

        Span<float> data = stackalloc float[VolumetricFogParameters.Floats];
        ZParams = VolumetricFogParameters.Write(
            data,
            fog,
            new VolumetricFogCamera(viewToWorld, previousViewToWorld, cameraDelta, unprojection, previousUnprojection, cameraWorld),
            (_gridX, _gridY, _gridZ),
            cascadeMatrices,
            cascadeSplits,
            cascadeCount,
            shelter,
            (long)_frameIndex,
            _hasHistory);

        ReadOnlySpan<ComputeResource> scatterResources =
        [
            new ComputeResource(shadowMap),
            new ComputeResource(skyIrradiance),
            new ComputeResource(history.View),
            new ComputeResource(target.View),
            new ComputeResource(shelterMap),
        ];

        _scatter.Record(
            cmd,
            frame,
            scatterResources,
            data,
            (uint)((_gridX + 3) / 4),
            (uint)((_gridY + 3) / 4),
            (uint)((_gridZ + 3) / 4));

        // The scattering written above, before the integration reads the same voxels back.
        VolumeBarrier(cmd, target.Image, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit);

        ReadOnlySpan<ComputeResource> integrateResources =
        [
            new ComputeResource(target.View),
            new ComputeResource(integrated.View),
        ];

        _integrate.Record(
            cmd,
            frame,
            integrateResources,
            data,
            (uint)((_gridX + 7) / 8),
            (uint)((_gridY + 7) / 8));

        // The integration's write, before the shading pass samples it from a fragment shader.
        VolumeBarrier(
            cmd,
            integrated.Image,
            AccessFlags.ShaderWriteBit,
            AccessFlags.ShaderReadBit,
            PipelineStageFlags.FragmentShaderBit);

        _current ^= 1;
        _hasHistory = true;
        _frameIndex++;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Release(retire: null);
        _scatter.Dispose();
        _integrate.Dispose();
    }

    /// <summary>Orders one dispatch's access to a volume against the next's.</summary>
    /// <remarks>
    /// Every volume here lives in the general layout for its whole life — written as a storage image and
    /// read back through a sampler, and the general layout is the one that is legal for both. So these
    /// are memory barriers with a layout that does not change, which is exactly what is needed: the
    /// hazard is the write landing after the read, not the image being in the wrong form.
    /// </remarks>
    private unsafe void VolumeBarrier(
        CommandBuffer cmd,
        Image image,
        AccessFlags source,
        AccessFlags destination,
        PipelineStageFlags destinationStage = PipelineStageFlags.ComputeShaderBit)
    {
        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.General,
            NewLayout = ImageLayout.General,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            SrcAccessMask = source,
            DstAccessMask = destination,
        };

        _vk.CmdPipelineBarrier(
            cmd,
            PipelineStageFlags.ComputeShaderBit,
            destinationStage,
            0,
            0,
            null,
            0,
            null,
            1,
            &barrier);
    }

    /// <summary>Frees the volumes, for a resize or a shutdown.</summary>
    /// <summary>Gives up the volumes, either to the retirement queue or on the spot.</summary>
    /// <param name="retire">
    /// Where to hand each volume so it is freed once the frames that used it have cycled, or
    /// <see langword="null"/> to free it immediately — which shutdown may do, having gone idle first.
    /// </param>
    private void Release(Action<IDisposable>? retire)
    {
        for (int i = 0; i < _scattering.Length; i++)
        {
            Give(_scattering[i], retire);
            _scattering[i] = null;
        }

        Give(_integrated, retire);
        _integrated = null;
        _hasHistory = false;

        // The grid is gone, so nothing matches it: an Ensure asking for the size just released has to
        // build again rather than believe the volumes are still there.
        _gridX = 0;
        _gridY = 0;
        _gridZ = 0;
    }

    /// <summary>Hands one volume to the retirement queue, or frees it where there is none.</summary>
    private static void Give(VulkanStorageImage? volume, Action<IDisposable>? retire)
    {
        if (volume is null)
        {
            return;
        }

        if (retire is null)
        {
            volume.Dispose();
        }
        else
        {
            retire(volume);
        }
    }
}
