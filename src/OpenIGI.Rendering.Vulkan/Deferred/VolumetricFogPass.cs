using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Shaders;
using OpenIGI.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>How the fog volume's depth slices are spread through the range it covers.</summary>
/// <remarks>
/// The reference's parameterisation, which both the shaders that build the volume and the shading pass
/// that samples it must agree on: <c>slice = log2(z·B + O)·Scale</c>. A <see cref="Scale"/> of zero is
/// how a frame with no fog says so.
/// </remarks>
/// <param name="B">The linear term.</param>
/// <param name="O">The offset that puts slice zero at the near plane.</param>
/// <param name="Scale">How hard the slices bunch towards the camera.</param>
/// <param name="InverseSlices">One over the slice count, which normalises a slice to a lookup.</param>
internal readonly record struct FogSliceParams(float B, float O, float Scale, float InverseSlices);

/// <summary>Everything one frame's fog needs to know, gathered at the point it is dispatched.</summary>
/// <param name="Settings">The medium itself.</param>
/// <param name="Volumes">The level's local volumes, already rebased against the camera.</param>
/// <param name="Lamps">The frame's local lights, in view space.</param>
/// <param name="LampCount">How many of <paramref name="Lamps"/> are live.</param>
/// <param name="SunDirection">The view-space direction towards the sun.</param>
/// <param name="SunColour">The sun's colour, already scaled by the frame's light scale.</param>
/// <param name="Ambient">The flat ambient, for a frame whose sky has no harmonics.</param>
/// <param name="SkyValid">Whether the sky's harmonics are worth reading.</param>
/// <param name="BaseHeight">The medium's base height as an offset from the camera, in metres.</param>
internal readonly record struct VolumetricFogFrame(
    VolumetricFogSettings Settings,
    ReadOnlyMemory<LocalFogVolume> Volumes,
    DeferredLocalLight[] Lamps,
    int LampCount,
    Vector3f SunDirection,
    Vector3f SunColour,
    Vector3f Ambient,
    bool SkyValid,
    float BaseHeight);

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
    /// <summary>How many screen pixels one froxel spans, across and down.</summary>
    public const int TilePixels = 16;

    /// <summary>The fewest and most depth slices the quality dial can ask for.</summary>
    private const int MinSlices = 16;
    private const int MaxSlices = 128;

    /// <summary>
    /// How the slices are spread through the depth range: the reference's
    /// <c>r.VolumetricFog.DepthDistributionScale</c>, and its default.
    /// </summary>
    private const float DepthDistributionScale = 32f;

    /// <summary>
    /// The near offset the reference folds into the distribution, in metres. Without it the first slices
    /// are packed into the few centimetres in front of the near plane, where there is nothing to resolve.
    /// </summary>
    private const float NearOffsetMetres = 0.095f;

    /// <summary>How much of the previous frame's answer a froxel keeps — the reference's history weight.</summary>
    private const float HistoryWeight = 0.9f;

    /// <summary>
    /// How far the camera may travel in one frame before the accumulated volume is thrown away, in metres.
    /// </summary>
    /// <remarks>
    /// A walk or a sprint moves a fraction of this, and the reprojection handles that: the history lands
    /// where the air was and the blend is correct. A jump this large is not movement, it is a teleport —
    /// a level change, a spawn, a camera cut — and reprojecting through it drags the fog of one place
    /// over another for the ten or so frames the blend takes to forget it.
    /// </remarks>
    private const float TeleportMetres = 8f;

    /// <summary>A depth bias on the shadow lookup, in the map's own normalised depth.</summary>
    /// <remarks>
    /// Small, and constant across cascades. A surface needs a bias scaled to its own slope because it
    /// shadows itself; a point of air does not, and the only thing this has to clear is the depth
    /// quantisation of the map itself.
    /// </remarks>
    private const float ShadowBias = 0.0008f;

    // The parameter block both shaders declare. Two rotations and four cascades of matrix, seventeen
    // vectors of scalars, then the volumes and the lamps.
    private const int MatrixFloats = 16 * (2 + 4);
    private const int VectorFloats = 17 * 4;
    private const int VolumeFloats = LocalFogVolume.MaxPerLevel * 4 * 2;
    private const int LampFloats = DeferredLocalLight.Maximum * 4 * 2;
    private const int ParamFloats = MatrixFloats + VectorFloats + VolumeFloats + LampFloats;
    private const int ParamSize = ParamFloats * sizeof(float);

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

    // The offsets each frame samples its froxels at, a Halton (2, 3, 5) sequence. Low discrepancy in all
    // three axes at once, so eight frames of it cover a cell evenly rather than clustering along one.
    private static readonly Vector3f[] JitterOffsets = BuildJitterOffsets(8);

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
    public FogSliceParams ZParams { get; private set; }

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
        int gridX = Math.Max(1, (width + TilePixels - 1) / TilePixels);
        int gridY = Math.Max(1, (height + TilePixels - 1) / TilePixels);
        int gridZ = Math.Clamp(slices, MinSlices, MaxSlices);

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
        if (cameraDelta.LengthSquared > TeleportMetres * TeleportMetres)
        {
            _hasHistory = false;
        }

        VulkanStorageImage target = _scattering[_current]!;
        VulkanStorageImage history = _scattering[_current ^ 1]!;
        VulkanStorageImage integrated = _integrated;

        Span<float> data = stackalloc float[ParamFloats];
        WriteParameters(
            data, fog, viewToWorld, previousViewToWorld, cameraDelta,
            unprojection, previousUnprojection, cascadeMatrices, cascadeSplits, cascadeCount,
            shelter, cameraWorld);

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

    /// <summary>Fills the parameter block both dispatches read.</summary>
    private void WriteParameters(
        Span<float> data,
        in VolumetricFogFrame fog,
        in Matrix3f viewToWorld,
        in Matrix3f previousViewToWorld,
        in Vector3f cameraDelta,
        in DepthUnprojection unprojection,
        in DepthUnprojection previousUnprojection,
        Matrix4f[] cascadeMatrices,
        float[] cascadeSplits,
        int cascadeCount,
        in ShelterMapTransform shelter,
        in Vector3f cameraWorld)
    {
        data.Clear();

        VolumetricFogSettings settings = fog.Settings;
        float distance = MathF.Max(settings.Distance, 1f);
        FogSliceParams z = ComputeZParams(unprojection.Near, distance, _gridZ);
        ZParams = z;

        WriteRotation(data, 0, viewToWorld);
        WriteRotation(data, 16, previousViewToWorld.Transposed());

        for (int i = 0; i < 4; i++)
        {
            Matrix4f cascade = i < cascadeCount ? cascadeMatrices[i] : Matrix4f.Identity;
            cascade.CopyColumnMajorTo(data.Slice(32 + (i * 16), 16));
        }

        int at = 96;
        for (int i = 0; i < 4; i++)
        {
            data[at + i] = i < cascadeCount ? cascadeSplits[i] : 0f;
        }

        at = 100;
        data[at + 0] = _gridX;
        data[at + 1] = _gridY;
        data[at + 2] = _gridZ;
        data[at + 3] = HistoryWeight;

        at = 104;
        data[at + 0] = unprojection.XScale;
        data[at + 1] = unprojection.YScale;
        data[at + 2] = unprojection.Near;
        data[at + 3] = unprojection.Far;

        at = 108;
        data[at + 0] = previousUnprojection.XScale;
        data[at + 1] = previousUnprojection.YScale;
        data[at + 2] = previousUnprojection.Near;
        data[at + 3] = previousUnprojection.Far;

        at = 112;
        data[at + 0] = z.B;
        data[at + 1] = z.O;
        data[at + 2] = z.Scale;
        data[at + 3] = z.InverseSlices;

        at = 116;
        data[at + 0] = MathF.Max(settings.Density, 0f);
        data[at + 1] = 1f / MathF.Max(settings.HeightFalloff, 0.01f);
        data[at + 2] = fog.BaseHeight;
        data[at + 3] = Math.Clamp(settings.Anisotropy, -0.95f, 0.95f);

        at = 120;
        data[at + 0] = Math.Clamp(settings.UniformFraction, 0f, 1f);
        data[at + 1] = Math.Clamp(settings.MultipleScattering, 0f, 1f);
        data[at + 2] = MathF.Max(settings.SunIntensity, 0f);
        data[at + 3] = MathF.Max(settings.AmbientIntensity, 0f);

        Vector3f albedo = settings.ScatteringAlbedo;
        at = 124;
        data[at + 0] = albedo.X;
        data[at + 1] = albedo.Y;
        data[at + 2] = albedo.Z;
        data[at + 3] = cascadeCount;

        at = 128;
        data[at + 0] = fog.SunDirection.X;
        data[at + 1] = fog.SunDirection.Y;
        data[at + 2] = fog.SunDirection.Z;
        data[at + 3] = 0f;

        at = 132;
        data[at + 0] = fog.SunColour.X;
        data[at + 1] = fog.SunColour.Y;
        data[at + 2] = fog.SunColour.Z;
        data[at + 3] = ShadowBias;

        at = 136;
        data[at + 0] = fog.Ambient.X;
        data[at + 1] = fog.Ambient.Y;
        data[at + 2] = fog.Ambient.Z;
        data[at + 3] = fog.SkyValid ? 1f : 0f;

        at = 140;
        data[at + 0] = cameraDelta.X;
        data[at + 1] = cameraDelta.Y;
        data[at + 2] = cameraDelta.Z;
        data[at + 3] = Math.Clamp(fog.LampCount, 0, DeferredLocalLight.Maximum);

        Vector3f jitter = JitterOffsets[(int)(_frameIndex % (ulong)JitterOffsets.Length)];
        at = 144;
        data[at + 0] = jitter.X;
        data[at + 1] = jitter.Y;
        data[at + 2] = jitter.Z;
        data[at + 3] = _hasHistory ? 1f : 0f;

        // Where the camera stands in the world, and the roof map's own transform. In world metres and
        // not camera-relative, unlike everything around it: the map is a fixed thing in the level, and
        // a froxel has to put the camera back on to address it.
        at = 148;
        data[at + 0] = cameraWorld.X;
        data[at + 1] = cameraWorld.Y;
        data[at + 2] = cameraWorld.Z;
        data[at + 3] = 0f;

        at = 152;
        data[at + 0] = shelter.OriginX;
        data[at + 1] = shelter.OriginY;
        data[at + 2] = shelter.CellMetres > 0f ? 1f / shelter.CellMetres : 0f;
        data[at + 3] = shelter.LowestZ;

        at = 156;
        data[at + 0] = shelter.HeightScale;
        data[at + 1] = shelter.HeadroomMetres;
        data[at + 2] = shelter.ReachMetres;
        data[at + 3] = shelter.IsPresent ? 1f : 0f;

        at = 160;
        data[at + 0] = shelter.Width;
        data[at + 1] = shelter.Height;

        int centres = 164;
        int extents = centres + (LocalFogVolume.MaxPerLevel * 4);
        ReadOnlySpan<LocalFogVolume> volumes = fog.Volumes.Span;
        for (int i = 0; i < LocalFogVolume.MaxPerLevel && i < volumes.Length; i++)
        {
            LocalFogVolume volume = volumes[i];
            if (!volume.IsMeaningful)
            {
                continue;
            }

            data[centres + (i * 4) + 0] = volume.Centre.X;
            data[centres + (i * 4) + 1] = volume.Centre.Y;
            data[centres + (i * 4) + 2] = volume.Centre.Z;
            data[centres + (i * 4) + 3] = volume.Shape == FogVolumeShape.Sphere ? 1f : 0f;

            data[extents + (i * 4) + 0] = volume.Extent.X;
            data[extents + (i * 4) + 1] = volume.Extent.Y;
            data[extents + (i * 4) + 2] = volume.Extent.Z;
            data[extents + (i * 4) + 3] = volume.DensityScale;
        }

        int positions = extents + (LocalFogVolume.MaxPerLevel * 4);
        int colours = positions + (DeferredLocalLight.Maximum * 4);
        int lamps = Math.Clamp(fog.LampCount, 0, DeferredLocalLight.Maximum);
        for (int i = 0; i < lamps; i++)
        {
            DeferredLocalLight lamp = fog.Lamps[i];
            data[positions + (i * 4) + 0] = lamp.ViewPosition.X;
            data[positions + (i * 4) + 1] = lamp.ViewPosition.Y;
            data[positions + (i * 4) + 2] = lamp.ViewPosition.Z;
            data[positions + (i * 4) + 3] = lamp.Radius;

            data[colours + (i * 4) + 0] = lamp.Colour.X;
            data[colours + (i * 4) + 1] = lamp.Colour.Y;
            data[colours + (i * 4) + 2] = lamp.Colour.Z;
            data[colours + (i * 4) + 3] = lamp.Intensity;
        }
    }

    /// <summary>
    /// Solves the reference's slice distribution: <c>slice = log2(z·B + O)·S</c>, with B and O placing
    /// slice zero at the near plane and the last slice at the fog's far distance.
    /// </summary>
    /// <param name="near">The near plane, in metres.</param>
    /// <param name="far">How far the fog reaches, in metres.</param>
    /// <param name="slices">How many slices the grid is deep.</param>
    /// <returns>B, O and S, with one over the slice count in the fourth component.</returns>
    private static FogSliceParams ComputeZParams(float near, float far, int slices)
    {
        double n = MathF.Max(near, 0.01f) + NearOffsetMetres;
        double f = Math.Max(far, n + 1.0);
        double s = DepthDistributionScale;

        double o = (f - (n * Math.Pow(2.0, slices / s))) / (f - n);
        double b = (1.0 - o) / n;

        return new FogSliceParams((float)b, (float)o, (float)s, 1f / Math.Max(1, slices));
    }

    /// <summary>Builds the per-frame offsets the froxels are sampled at.</summary>
    /// <param name="count">How many frames the sequence covers before it repeats.</param>
    /// <returns>The offsets, each within the unit cell.</returns>
    private static Vector3f[] BuildJitterOffsets(int count)
    {
        Vector3f[] offsets = new Vector3f[count];
        for (int i = 0; i < count; i++)
        {
            offsets[i] = new Vector3f(Halton(i + 1, 2), Halton(i + 1, 3), Halton(i + 1, 5));
        }

        return offsets;
    }

    /// <summary>One term of a Halton sequence — a low-discrepancy point in [0, 1).</summary>
    /// <param name="index">Which term, from one.</param>
    /// <param name="radix">The base, which must differ per axis or the axes correlate.</param>
    /// <returns>The term.</returns>
    private static float Halton(int index, int radix)
    {
        float result = 0f;
        float fraction = 1f / radix;
        while (index > 0)
        {
            result += (index % radix) * fraction;
            index /= radix;
            fraction /= radix;
        }

        return result;
    }

    /// <summary>Writes a three-by-three rotation into the four-by-four a shader reads, column by column.</summary>
    private static void WriteRotation(Span<float> data, int offset, in Matrix3f m)
    {
        data[offset + 0] = m.M11; data[offset + 1] = m.M21; data[offset + 2] = m.M31; data[offset + 3] = 0f;
        data[offset + 4] = m.M12; data[offset + 5] = m.M22; data[offset + 6] = m.M32; data[offset + 7] = 0f;
        data[offset + 8] = m.M13; data[offset + 9] = m.M23; data[offset + 10] = m.M33; data[offset + 11] = 0f;
        data[offset + 12] = 0f; data[offset + 13] = 0f; data[offset + 14] = 0f; data[offset + 15] = 1f;
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
