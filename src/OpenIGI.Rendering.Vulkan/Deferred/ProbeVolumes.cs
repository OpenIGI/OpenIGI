using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// The irradiance probe field as four 3D textures — red, green and blue first-order spherical-harmonic
/// coefficients and a distance-moment volume — that the lighting pass samples for bounced light.
/// </summary>
/// <remarks>
/// <para>
/// The engine hands the field over as flat float arrays (<see cref="IrradianceVolume"/>): twelve
/// coefficients a probe, four per colour channel, plus two distance moments. They are repacked into four
/// four-component volumes and uploaded, so the hardware's trilinear filter does the blend between eight
/// neighbouring probes for free and one <c>texture()</c> a channel evaluates the SH. The volumes are
/// <c>R32G32B32A32</c> float to carry the coefficients without the precision loss a half would add to
/// low-frequency light that is then multiplied across a whole surface.
/// </para>
/// <para>
/// Rebuilt only when the field's dimensions change; an interactive bake re-uploads the same shape each
/// frame for a second as the light eases in, and reusing the images across those uploads avoids
/// reallocating a volume a frame.
/// </para>
/// </remarks>
internal sealed unsafe class ProbeVolumes : IDisposable
{
    private const int Channels = 4;

    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanAllocator _allocator;
    private readonly VulkanCommands _commands;
    private readonly Image[] _images = new Image[Channels];
    private readonly DeviceMemory[] _memory = new DeviceMemory[Channels];
    private readonly ImageView[] _views = new ImageView[Channels];

    // Volumes replaced by a re-upload, each dated with the queue value that has to be reached before the
    // frames still sampling them can possibly have finished.
    private readonly List<Retired> _retiring = [];
    private Sampler _sampler;
    private int _countX;
    private int _countY;
    private int _countZ;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ProbeVolumes"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the volumes live on.</param>
    /// <param name="allocator">The allocator backing the images.</param>
    /// <param name="commands">The pool the uploads come from.</param>
    public ProbeVolumes(Vk vk, VulkanDevice device, VulkanAllocator allocator, VulkanCommands commands)
    {
        _vk = vk;
        _device = device;
        _allocator = allocator;
        _commands = commands;
        _sampler = CreateSampler();

        // A 1x1x1 placeholder so the four views are always valid to bind, even before any field is
        // uploaded; the lighting pass ignores their contents while HasData is false.
        _countX = 1;
        _countY = 1;
        _countZ = 1;
        for (int channel = 0; channel < Channels; channel++)
        {
            CreateImage(channel);
            Upload(channel, new float[4], default, null);
        }
    }

    /// <summary>Gets a value indicating whether a field has been uploaded and can be sampled.</summary>
    public bool HasData { get; private set; }

    /// <summary>Gets the field's origin in world space.</summary>
    public Vector3f Origin { get; private set; }

    /// <summary>Gets the probe spacing in metres.</summary>
    public float Spacing { get; private set; }

    /// <summary>Gets one over the probe count on each axis, for the grid-to-texture-coordinate map.</summary>
    public Vector3f InverseCount { get; private set; }

    /// <summary>Gets the sampler the four volumes are read through.</summary>
    public Sampler Sampler => _sampler;

    /// <summary>Gets the image view for one channel: 0 red, 1 green, 2 blue, 3 distance.</summary>
    /// <param name="channel">The channel index.</param>
    /// <returns>The view.</returns>
    public ImageView View(int channel) => _views[channel];

    /// <summary>Uploads a field, rebuilding the volumes if its dimensions changed.</summary>
    /// <param name="volume">The field.</param>
    /// <param name="frameCommandBuffer">The open frame's command buffer to record the upload into.</param>
    /// <param name="deferCleanup">
    /// Takes ownership of the staging buffer to free once the frame has cycled, or <see langword="null"/>
    /// to upload on a private submit-and-wait instead (before any frame is open).
    /// </param>
    public void Update(IrradianceVolume volume, CommandBuffer frameCommandBuffer, Action<IDisposable>? deferCleanup)
    {
        ArgumentNullException.ThrowIfNull(volume);
        if (volume.ProbeCount == 0)
        {
            HasData = false;
            return;
        }

        RetireCompleted();

        if (!HasData || _countX != volume.CountX || _countY != volume.CountY || _countZ != volume.CountZ)
        {
            RetireImages();
            _countX = volume.CountX;
            _countY = volume.CountY;
            _countZ = volume.CountZ;
            for (int channel = 0; channel < Channels; channel++)
            {
                CreateImage(channel);
            }
        }

        int count = volume.ProbeCount;
        float[] red = new float[count * 4];
        float[] green = new float[count * 4];
        float[] blue = new float[count * 4];
        float[] distance = new float[count * 4];

        ReadOnlySpan<float> source = volume.Coefficients;
        ReadOnlySpan<float> moments = volume.Distances;
        for (int probe = 0; probe < count; probe++)
        {
            int from = probe * IrradianceVolume.CoefficientsPerProbe;
            int to = probe * 4;
            for (int i = 0; i < 4; i++)
            {
                red[to + i] = source[from + i];
                green[to + i] = source[from + 4 + i];
                blue[to + i] = source[from + 8 + i];
            }

            distance[to] = moments[probe * 2];
            distance[to + 1] = moments[(probe * 2) + 1];
        }

        Upload(0, red, frameCommandBuffer, deferCleanup);
        Upload(1, green, frameCommandBuffer, deferCleanup);
        Upload(2, blue, frameCommandBuffer, deferCleanup);
        Upload(3, distance, frameCommandBuffer, deferCleanup);

        Origin = volume.Origin;
        Spacing = volume.Spacing;
        InverseCount = new Vector3f(1f / volume.CountX, 1f / volume.CountY, 1f / volume.CountZ);
        HasData = true;
    }

    /// <summary>Forgets the current field, so the lighting falls back to the flat ambient.</summary>
    public void Clear() => HasData = false;

    private void CreateImage(int channel)
    {
        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type3D,
            Format = Format.R32G32B32A32Sfloat,
            Extent = new Extent3D((uint)_countX, (uint)_countY, (uint)_countZ),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };

        Image image;
        VkCheck.Ok(_vk.CreateImage(_device.Device, &imageInfo, null, &image), "vkCreateImage (probe)");
        _images[channel] = image;

        _vk.GetImageMemoryRequirements(_device.Device, image, out MemoryRequirements requirements);
        _memory[channel] = _allocator.Allocate(requirements, MemoryPropertyFlags.DeviceLocalBit);
        VkCheck.Ok(_vk.BindImageMemory(_device.Device, image, _memory[channel], 0), "vkBindImageMemory (probe)");

        ImageViewCreateInfo viewInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type3D,
            Format = Format.R32G32B32A32Sfloat,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };

        ImageView view;
        VkCheck.Ok(_vk.CreateImageView(_device.Device, &viewInfo, null, &view), "vkCreateImageView (probe)");
        _views[channel] = view;
    }

    // Records one channel's copy into the frame command buffer (deferring the staging buffer) when a
    // deferral is given, or onto a private submit-and-wait when it is not.
    private void Upload(int channel, float[] data, CommandBuffer frameCommandBuffer, Action<IDisposable>? deferCleanup)
    {
        VulkanBuffer staging = new(
            _vk,
            _device,
            _allocator,
            (ulong)(data.Length * sizeof(float)),
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        staging.Write<float>(data);

        Image image = _images[channel];
        Buffer source = staging.Handle;
        int x = _countX;
        int y = _countY;
        int z = _countZ;

        void Record(CommandBuffer cmd)
        {
            Transition(cmd, image, ImageLayout.Undefined, ImageLayout.TransferDstOptimal,
                PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit, 0, AccessFlags.TransferWriteBit);

            BufferImageCopy region = new()
            {
                ImageSubresource = new ImageSubresourceLayers { AspectMask = ImageAspectFlags.ColorBit, LayerCount = 1 },
                ImageExtent = new Extent3D((uint)x, (uint)y, (uint)z),
            };
            _vk.CmdCopyBufferToImage(cmd, source, image, ImageLayout.TransferDstOptimal, 1, &region);

            Transition(cmd, image, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit);
        }

        // The frame's own command buffer when there is one, so this upload lands in the same submission --
        // and so the same in-order timeline -- as the passes that read the field, rather than on its own
        // submission racing the frame in flight (which is what lost the device under a moving camera). The
        // staging buffer has to outlive the frame it feeds, so it is handed to the deferred deletion the
        // caller owns rather than disposed here. Outside a frame -- a load-time first upload -- there is
        // nothing in flight and its own submit-and-wait is both safe and simplest.
        if (deferCleanup is not null)
        {
            Record(frameCommandBuffer);
            deferCleanup(staging);
        }
        else
        {
            _commands.SingleTime(Record);
            staging.Dispose();
        }
    }

    private void Transition(
        CommandBuffer cmd, Image image, ImageLayout oldLayout, ImageLayout newLayout,
        PipelineStageFlags src, PipelineStageFlags dst, AccessFlags srcAccess, AccessFlags dstAccess)
    {
        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
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
            SrcAccessMask = srcAccess,
            DstAccessMask = dstAccess,
        };
        _vk.CmdPipelineBarrier(cmd, src, dst, 0, 0, null, 0, null, 1, &barrier);
    }

    private Sampler CreateSampler()
    {
        SamplerCreateInfo createInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            BorderColor = BorderColor.FloatOpaqueBlack,
        };

        Sampler sampler;
        VkCheck.Ok(_vk.CreateSampler(_device.Device, &createInfo, null, &sampler), "vkCreateSampler (probe)");
        return sampler;
    }

    /// <summary>
    /// Hands the current volumes over to be freed later, rather than destroying them where they stand.
    /// </summary>
    /// <remarks>
    /// A field is re-uploaded during play, while frames that sample the volumes it replaces are still in
    /// flight — their command buffers carry descriptors naming these very views. Destroying them here
    /// would free memory the GPU is about to read. They are instead dated against the queue's timeline
    /// and freed once it has passed the frame being recorded now, which is the first point at which
    /// nothing can still be reading them.
    /// </remarks>
    private void RetireImages()
    {
        // The frame currently being recorded will signal this value, so everything recorded up to and
        // including it has finished once the queue reaches it.
        ulong retireAfter = _device.GraphicsQueue.LastSubmittedValue + 1;

        for (int channel = 0; channel < Channels; channel++)
        {
            if (_images[channel].Handle == 0 && _views[channel].Handle == 0)
            {
                continue;
            }

            _retiring.Add(new Retired(retireAfter, _images[channel], _views[channel], _memory[channel]));
            _images[channel] = default;
            _views[channel] = default;
            _memory[channel] = default;
        }
    }

    /// <summary>Frees the retired volumes the queue has moved past.</summary>
    private void RetireCompleted()
    {
        if (_retiring.Count == 0)
        {
            return;
        }

        ulong completed = _device.GraphicsQueue.CompletedValue;
        for (int i = _retiring.Count - 1; i >= 0; i--)
        {
            Retired retired = _retiring[i];
            if (retired.RetireAfter > completed)
            {
                continue;
            }

            Free(retired);
            _retiring.RemoveAt(i);
        }
    }

    private void Free(in Retired retired)
    {
        if (retired.View.Handle != 0)
        {
            _vk.DestroyImageView(_device.Device, retired.View, null);
        }

        if (retired.Image.Handle != 0)
        {
            _vk.DestroyImage(_device.Device, retired.Image, null);
        }

        if (retired.Memory.Handle != 0)
        {
            _allocator.Free(retired.Memory);
        }
    }

    private void DestroyImages()
    {
        for (int channel = 0; channel < Channels; channel++)
        {
            if (_views[channel].Handle != 0)
            {
                _vk.DestroyImageView(_device.Device, _views[channel], null);
                _views[channel] = default;
            }

            if (_images[channel].Handle != 0)
            {
                _vk.DestroyImage(_device.Device, _images[channel], null);
                _images[channel] = default;
            }

            if (_memory[channel].Handle != 0)
            {
                _allocator.Free(_memory[channel]);
                _memory[channel] = default;
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // Teardown waits for the device, so anything still on the retirement list is certainly idle.
        foreach (Retired retired in _retiring)
        {
            Free(retired);
        }

        _retiring.Clear();
        DestroyImages();
        _vk.DestroySampler(_device.Device, _sampler, null);
    }

    /// <summary>A volume waiting for the GPU to finish with it.</summary>
    /// <param name="RetireAfter">The queue timeline value past which nothing can still be sampling it.</param>
    /// <param name="Image">The image.</param>
    /// <param name="View">Its view.</param>
    /// <param name="Memory">The memory backing it.</param>
    private readonly record struct Retired(ulong RetireAfter, Image Image, ImageView View, DeviceMemory Memory);
}
