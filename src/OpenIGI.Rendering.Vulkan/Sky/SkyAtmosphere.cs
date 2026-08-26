using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Sky;

/// <summary>
/// The atmosphere's lookup tables and the passes that build them: the sky's colour in every direction,
/// how much sunlight survives to a surface, and the sky's light reduced to nine numbers a colour.
/// </summary>
/// <remarks>
/// <para>
/// Two of the tables describe the air itself and nothing else, so they are built the first time the
/// backend runs and never again. The other two describe the sky as it looks from where the camera stands
/// with the sun where it currently is, so they are rebuilt whenever the sun moves — which for a game with
/// a day cycle is every frame, and for a mission with a fixed sun is once.
/// </para>
/// <para>
/// The tables are small enough that rebuilding them costs almost nothing: the sky view is under twenty
/// thousand texels and the harmonics are eight. What they replace is a full atmospheric ray-march at
/// every pixel of the sky and at every surface asking how much sun reaches it.
/// </para>
/// </remarks>
internal sealed class SkyAtmosphere : IDisposable
{
    // Enough resolution for the transmittance to be smooth across the horizon, where it changes fastest.
    private const uint TransmittanceWidth = 256;
    private const uint TransmittanceHeight = 64;

    // Multiple scattering varies slowly with both altitude and sun angle, so it needs very little.
    private const uint MultiScatterSize = 32;

    // Wide rather than tall, because a sky changes far more around the horizon than up it — and the
    // vertical axis is already packed towards the horizon by its parameterisation.
    private const uint SkyViewWidth = 192;
    private const uint SkyViewHeight = 104;

    // Seven vectors of coefficients plus the plain average.
    private const uint IrradianceWidth = 8;

    private const int ParamSize = 4 * 16;

    private readonly Vk _vk;
    private readonly VulkanImage _transmittance;
    private readonly VulkanImage _multiScatter;
    private readonly VulkanImage _skyView;
    private readonly VulkanImage _irradiance;
    private readonly VulkanComputePass _transmittancePass;
    private readonly VulkanComputePass _multiScatterPass;
    private readonly VulkanComputePass _skyViewPass;
    private readonly VulkanComputePass _irradiancePass;

    // The harmonics copied down to the host, one buffer per frame in flight so a read never races the
    // copy still being recorded. The game-layer gather evaluates its sky rays from these, one frame old.
    private readonly VulkanBuffer[] _readback;
    private readonly bool[] _readbackWritten;

    private bool _staticTablesBuilt;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="SkyAtmosphere"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the tables live on.</param>
    /// <param name="allocator">The allocator backing the tables.</param>
    /// <param name="commands">The pool the initial layout transitions come from.</param>
    /// <param name="compiler">The shader compiler.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    public SkyAtmosphere(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        ShaderCompiler compiler,
        int framesInFlight)
    {
        ArgumentNullException.ThrowIfNull(vk);
        _vk = vk;

        const ImageUsageFlags usage = ImageUsageFlags.StorageBit | ImageUsageFlags.SampledBit;
        _transmittance = new VulkanImage(
            vk, device, allocator, commands, Format.R16G16B16A16Sfloat,
            TransmittanceWidth, TransmittanceHeight, 1, usage);
        _multiScatter = new VulkanImage(
            vk, device, allocator, commands, Format.R16G16B16A16Sfloat,
            MultiScatterSize, MultiScatterSize, 1, usage);
        _skyView = new VulkanImage(
            vk, device, allocator, commands, Format.R16G16B16A16Sfloat,
            SkyViewWidth, SkyViewHeight, 1, usage);

        // Full precision: these are the sky's light reduced to eight numbers, and every surface in the
        // frame is lit from them, so a rounding error here is a tint over everything.
        _irradiance = new VulkanImage(
            vk, device, allocator, commands, Format.R32G32B32A32Sfloat,
            IrradianceWidth, 1, 1, usage);

        _transmittancePass = new VulkanComputePass(
            vk, device, allocator, compiler, "sky.transmittance.comp", SkyAtmospherePasses.Transmittance,
            [ComputeBindingKind.StorageImage], ParamSize, framesInFlight);

        _multiScatterPass = new VulkanComputePass(
            vk, device, allocator, compiler, "sky.multiscatter.comp", SkyAtmospherePasses.MultiScatter,
            [ComputeBindingKind.SampledStorageImage, ComputeBindingKind.StorageImage],
            ParamSize, framesInFlight);

        _skyViewPass = new VulkanComputePass(
            vk, device, allocator, compiler, "sky.skyview.comp", SkyAtmospherePasses.SkyView,
            [
                ComputeBindingKind.SampledStorageImage,
                ComputeBindingKind.SampledStorageImage,
                ComputeBindingKind.StorageImage,
            ],
            ParamSize, framesInFlight);

        _irradiancePass = new VulkanComputePass(
            vk, device, allocator, compiler, "sky.irradiance.comp", SkyAtmospherePasses.SkyIrradiance,
            [ComputeBindingKind.SampledStorageImage, ComputeBindingKind.StorageImage],
            ParamSize, framesInFlight);

        _readback = new VulkanBuffer[framesInFlight];
        _readbackWritten = new bool[framesInFlight];
        for (int i = 0; i < framesInFlight; i++)
        {
            _readback[i] = new VulkanBuffer(
                vk, device, allocator,
                IrradianceWidth * 4 * sizeof(float),
                BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                persistentlyMapped: true);
        }
    }

    /// <summary>Gets the sky's luminance in every direction, for the pass that draws it.</summary>
    public ImageView SkyViewLut => _skyView.View;

    /// <summary>Gets how much sunlight survives to a point, for the pass that lights surfaces.</summary>
    public ImageView TransmittanceLut => _transmittance.View;

    /// <summary>Gets the sky's light as spherical harmonics, for the ambient term.</summary>
    public ImageView IrradianceLut => _irradiance.View;

    /// <summary>Gets the sampler the tables are read through: linear, and clamped at every edge.</summary>
    public Sampler Sampler => _skyViewPass.Sampler;

    /// <summary>
    /// The camera's height above the planet's centre, in the kilometres the atmosphere works in.
    /// </summary>
    /// <param name="altitudeMetres">How far the camera is above the ground.</param>
    /// <returns>The height to march from.</returns>
    /// <remarks>
    /// A metre above the ground and a hundred metres above it give skies that differ by far less than the
    /// eye can tell, so this being approximate costs nothing while the game is played on foot. A mission
    /// fought from an aircraft would want the real altitude.
    /// </remarks>
    public static float ViewHeightKm(float altitudeMetres) => 6360.0f + Math.Max(0.0005f, altitudeMetres * 0.001f);

    /// <summary>Records the whole update: the constant tables if they are not built, then this frame's.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <param name="sunDirection">The world direction towards the sun.</param>
    /// <param name="sunIlluminance">The sun's colour and brightness outside the atmosphere.</param>
    /// <param name="viewHeightKm">The camera's height from the planet's centre.</param>
    /// <param name="skyLightIntensity">How brightly the sky lights the world; one is its own brightness.</param>
    public void Record(
        CommandBuffer cmd,
        int frame,
        Vector3f sunDirection,
        Vector3f sunIlluminance,
        float viewHeightKm,
        float skyLightIntensity)
    {
        if (!_staticTablesBuilt)
        {
            RecordStaticTables(cmd, frame);
            _staticTablesBuilt = true;
        }

        Span<float> p = stackalloc float[ParamSize / sizeof(float)];
        WriteParameters(p, SkyViewWidth, SkyViewHeight, sunDirection, sunIlluminance, viewHeightKm, skyLightIntensity);

        _skyViewPass.Record(
            cmd,
            frame,
            [
                new ComputeResource(_transmittance.View),
                new ComputeResource(_multiScatter.View),
                new ComputeResource(_skyView.View),
            ],
            p,
            (SkyViewWidth + 7) / 8,
            (SkyViewHeight + 7) / 8);

        Barrier(cmd, _skyView.Handle);

        // One work group of sixty-four, one direction each: the whole sky reduced to eight vectors.
        _irradiancePass.Record(
            cmd,
            frame,
            [new ComputeResource(_skyView.View), new ComputeResource(_irradiance.View)],
            p,
            1,
            1);

        Barrier(cmd, _irradiance.Handle);
        RecordReadback(cmd, frame);
    }

    /// <summary>Copies this frame's harmonics down to the host, for the game layer to read next frame.</summary>
    private unsafe void RecordReadback(CommandBuffer cmd, int frame)
    {
        BufferImageCopy region = new()
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            ImageOffset = default,
            ImageExtent = new Extent3D(IrradianceWidth, 1, 1),
        };

        // The compute write above has only been made visible to shader reads; the transfer needs its own.
        ImageMemoryBarrier toTransfer = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.General,
            NewLayout = ImageLayout.General,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = _irradiance.Handle,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            SrcAccessMask = AccessFlags.ShaderWriteBit,
            DstAccessMask = AccessFlags.TransferReadBit,
        };
        _vk.CmdPipelineBarrier(
            cmd, PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.TransferBit, 0, 0, null, 0, null, 1, &toTransfer);

        _vk.CmdCopyImageToBuffer(cmd, _irradiance.Handle, ImageLayout.General, _readback[frame].Handle, 1, &region);

        // Make the copy visible to the host that reads it after the frame's fence.
        BufferMemoryBarrier toHost = new()
        {
            SType = StructureType.BufferMemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.HostReadBit,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Buffer = _readback[frame].Handle,
            Offset = 0,
            Size = Vk.WholeSize,
        };
        _vk.CmdPipelineBarrier(
            cmd, PipelineStageFlags.TransferBit, PipelineStageFlags.HostBit, 0, 0, null, 1, &toHost, 0, null);

        _readbackWritten[frame] = true;
    }

    /// <summary>Reads the harmonics the given frame slot last copied down.</summary>
    /// <param name="frame">The frame-in-flight slot whose fence has completed.</param>
    /// <param name="destination">Room for 28 floats: the seven coefficient vectors.</param>
    /// <returns><see langword="true"/> once that slot has been written at least once.</returns>
    public unsafe bool TryReadIrradiance(int frame, Span<float> destination)
    {
        if (!_readbackWritten[frame] || destination.Length < 28)
        {
            return false;
        }

        new ReadOnlySpan<float>(_readback[frame].MappedPointer, 28).CopyTo(destination);
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _irradiancePass.Dispose();
        _skyViewPass.Dispose();
        _multiScatterPass.Dispose();
        _transmittancePass.Dispose();
        _irradiance.Dispose();
        _skyView.Dispose();
        _multiScatter.Dispose();
        _transmittance.Dispose();
        foreach (VulkanBuffer buffer in _readback)
        {
            buffer.Dispose();
        }
    }

    private static void WriteParameters(
        Span<float> p,
        uint width,
        uint height,
        Vector3f sunDirection,
        Vector3f sunIlluminance,
        float viewHeightKm,
        float skyLightIntensity)
    {
        p[0] = width;
        p[1] = height;
        p[2] = 1f / width;
        p[3] = 1f / height;
        p[4] = sunDirection.X;
        p[5] = sunDirection.Y;
        p[6] = sunDirection.Z;
        p[7] = 0f;
        p[8] = sunIlluminance.X;
        p[9] = sunIlluminance.Y;
        p[10] = sunIlluminance.Z;
        p[11] = 0f;
        p[12] = viewHeightKm;
        p[13] = skyLightIntensity;
        p[14] = 0f;
        p[15] = 0f;
    }

    private void RecordStaticTables(CommandBuffer cmd, int frame)
    {
        Span<float> p = stackalloc float[ParamSize / sizeof(float)];

        WriteParameters(p, TransmittanceWidth, TransmittanceHeight, default, default, 0f, 1f);
        _transmittancePass.Record(
            cmd,
            frame,
            [new ComputeResource(_transmittance.View)],
            p,
            (TransmittanceWidth + 7) / 8,
            (TransmittanceHeight + 7) / 8);

        // The multiple-scattering march reads the table the pass above just wrote.
        Barrier(cmd, _transmittance.Handle);

        WriteParameters(p, MultiScatterSize, MultiScatterSize, default, default, 0f, 1f);
        _multiScatterPass.Record(
            cmd,
            frame,
            [new ComputeResource(_transmittance.View), new ComputeResource(_multiScatter.View)],
            p,
            (MultiScatterSize + 7) / 8,
            (MultiScatterSize + 7) / 8);

        Barrier(cmd, _multiScatter.Handle);
    }

    /// <summary>Makes one pass's writes visible to the next pass's reads.</summary>
    private unsafe void Barrier(CommandBuffer cmd, Image image)
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
            SrcAccessMask = AccessFlags.ShaderWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
        };

        _vk.CmdPipelineBarrier(
            cmd,
            PipelineStageFlags.ComputeShaderBit,
            PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.FragmentShaderBit,
            0,
            0,
            null,
            0,
            null,
            1,
            &barrier);
    }
}
