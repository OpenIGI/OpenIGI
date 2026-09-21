using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.RayTracing;

/// <summary>
/// The colour a traced ray needs at the point it struck something, in one array a ray shader can reach
/// without knowing which texture it hit.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is for.</b> A reflection had exactly two ways to put a colour on a hit: read the finished
/// frame where the camera can see the place the ray struck, or look that place up in the surface cache's
/// baked cards. Both fail for the same thing — a body. It is off screen, because a window in front of you
/// reflects what is behind you, and it is not in the cards, because the cards are a bake of the static
/// level. So a ray would strike a soldier or the player and have nothing to shade them with, which the
/// glazing route view shows as a body-shaped hole. This is the third route: shade the hit from its own
/// texture.
/// </para>
/// <para>
/// <b>Why an array of reduced textures rather than the textures themselves.</b> The same reasoning as
/// <see cref="VulkanOpacityMasks"/>, which this deliberately mirrors: a bindless heap is the general
/// answer and this is the narrow one. A reflection is weighted by a Fresnel term that is four per cent
/// head-on, and where it is not, it is stretched across a surface seen almost edge-on — so it does not
/// need the source resolution or a mip chain. Every world texture is reduced once, at load, to a layer of
/// a fixed-size array, and a hit carries the layer number.
/// </para>
/// <para>
/// <b>Filtered, where the mask is not.</b> A mask answers a binary question, and averaging its neighbours
/// would invent values on the wrong side of its test. This answers a continuous one, so the reduction is
/// a box filter over the whole source region a layer texel covers.
/// </para>
/// <para>
/// <b>Layers are returned when a texture is destroyed</b>, which the mask array never had to do. Masks
/// are a handful per level and the array outlived them all; this holds hundreds, so a second mission
/// would exhaust it during its load if the first mission's textures never let go.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanTracedAlbedo : IDisposable
{
    /// <summary>The narrowest and widest a layer may be, whatever was asked for.</summary>
    /// <remarks>
    /// The request comes from a cvar a player can type anything into. Below the floor a character's face
    /// is a handful of texels; above the ceiling the array is a third of a gigabyte.
    /// </remarks>
    private const int MinimumResolution = 32;
    private const int MaximumResolution = 512;

    /// <summary>How many distinct textures the array can hold.</summary>
    /// <remarks>
    /// <para>
    /// <b>This was 512 and that was not enough, in a way that looked like anything but a limit.</b>
    /// Mission one's pack alone is 393 textures; the interface, the weapons and the soldiers' own sets
    /// are all on top of that, and the SOLDIERS LOAD LAST — the mission's load sequence does the world,
    /// then the terrain, then the characters. So the array filled with scenery and every human in the
    /// level fell off the end of it. The player's reflection came out as a flat silhouette while the
    /// building behind them reflected perfectly, which reads as a bug in how bodies are shaded rather
    /// than as an array that ran out of room.
    /// </para>
    /// <para>
    /// A thousand covers a level's pack, the interface and every character with room to spare. At
    /// <see cref="Resolution"/> squared in four bytes that is about 67 MB, which is half what the surface
    /// cache's own atlas already costs.
    /// </para>
    /// <para>
    /// A texture that still arrives past the limit has no layer, and its hits fall back to the two routes
    /// that existed before. That is the old behaviour rather than a wrong colour — but it is now SAID,
    /// through the renderer's log rather than the console, because the silence is what made this cost a
    /// day.
    /// </para>
    /// </remarks>
    public const int MaxLayers = 1024;

    /// <summary>How much video memory the whole array is allowed to take, in bytes.</summary>
    /// <remarks>
    /// <b>A budget, because the cap alone was the wrong control.</b> A thousand and twenty-four layers is
    /// a sensible number at one resolution and an arbitrary one at every other: a level here holds well
    /// over a thousand textures, so the array ran out on mission one at any setting, and the log said so
    /// on every frame — geometry admitted after that can only be shaded from the frame and the cards, and
    /// WHICH textures win the slots depends on load order, so two sessions of the same level reflected
    /// different surfaces. What actually has to be held constant is the memory, so that is what is held:
    /// the count follows from it and from the resolution the player chose. At a quarter-megabyte a layer
    /// nothing changes; halve the resolution and four times as many textures fit, which covers the level.
    /// </remarks>
    public const long LayerBudgetBytes = 256L * 1024L * 1024L;

    /// <summary>How many layers a given layer resolution affords inside that budget.</summary>
    /// <param name="resolution">How many texels square one layer is.</param>
    /// <returns>The layer count, never below a floor that keeps the array useful.</returns>
    public static int LayersFor(int resolution)
    {
        long bytes = (long)resolution * resolution * 4L;
        long affordable = bytes > 0 ? LayerBudgetBytes / bytes : MaxLayers;

        // Never fewer than enough to be worth having, and never so many that the array stops being a
        // fixed cost -- a level with tens of thousands of textures is not a case this is for.
        return (int)Math.Clamp(affordable, 256L, 4096L);
    }

    /// <summary>The smallest source a layer is spent on, in texels.</summary>
    /// <remarks>
    /// <para>
    /// Only the one-texel placeholders and the smallest scraps. It was thirty-two square, chosen to keep
    /// interface art from spending the array before the world loaded — but the array holds a thousand
    /// layers now and hands them back when a texture dies, so the scarcity that justified the floor is
    /// gone, and what the floor was actually excluding was the level's SMALL textures: trim, pipework,
    /// ladders, the narrow strips on a tower. Those reflected as unshaded black slivers against properly
    /// shaded walls, which reads as streaking rather than as a size limit.
    /// </para>
    /// <para>
    /// If it ever does run out it says so now, in the renderer's own log, rather than in a console line
    /// nobody sees.
    /// </para>
    /// </remarks>
    private const int MinimumTexels = 8 * 8;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly VulkanAllocator _allocator;
    private readonly VulkanCommands _commands;
    private readonly VulkanDevice _owner;
    private readonly DeviceMemory _memory;
    private readonly Dictionary<uint, int> _layers = [];
    private readonly Stack<int> _free = [];
    private int _next;
    private bool _overflow;
    private bool _disposed;

    // How many layers this array actually has, which follows from the budget and the chosen resolution
    // rather than from a constant. See LayerBudgetBytes.
    private readonly int _capacity;

    /// <summary>Initialises a new instance of the <see cref="VulkanTracedAlbedo"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the array lives on.</param>
    /// <param name="allocator">The allocator backing the image.</param>
    /// <param name="commands">The pool the one-off upload commands come from.</param>
    /// <param name="resolution">
    /// How many texels square to store each texture at. The cost is quadratic — see
    /// <c>r_reflection_texture</c>, which is where this comes from — and it is fixed for the life of the
    /// array, because the image is allocated here and nothing resizes it.
    /// </param>
    public VulkanTracedAlbedo(
        Vk vk, VulkanDevice device, VulkanAllocator allocator, VulkanCommands commands, int resolution)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(commands);

        Resolution = Math.Clamp(resolution, MinimumResolution, MaximumResolution);

        // How many layers that resolution affords inside the array's memory budget -- and never more
        // than the adapter will put in one array image, which is a hard limit and commonly two thousand
        // and forty-eight. Asking for more is not a soft failure: the create call is invalid.
        PhysicalDeviceProperties properties;
        vk.GetPhysicalDeviceProperties(device.PhysicalDevice, &properties);
        _capacity = Math.Min(LayersFor(Resolution), (int)properties.Limits.MaxImageArrayLayers);
        _vk = vk;
        _owner = device;
        _device = device.Device;
        _allocator = allocator;
        _commands = commands;

        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,

            // SRGB, so the hardware decodes on read and the shader is handed linear light without a curve
            // of its own. The raster path decodes the same textures in the shader; doing it in the format
            // here keeps the two from disagreeing by a gamma, which reads as a reflection that is too
            // dark rather than as a bug.
            Format = Format.R8G8B8A8Srgb,
            Extent = new Extent3D((uint)Resolution, (uint)Resolution, 1),
            MipLevels = 1,
            ArrayLayers = (uint)_capacity,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };

        Image image;
        VkCheck.Ok(_vk.CreateImage(_device, &imageInfo, null, &image), "vkCreateImage (traced albedo)");
        Image = image;

        _vk.GetImageMemoryRequirements(_device, image, out MemoryRequirements requirements);
        _memory = _allocator.Allocate(requirements, MemoryPropertyFlags.DeviceLocalBit);
        VkCheck.Ok(_vk.BindImageMemory(_device, image, _memory, 0), "vkBindImageMemory (traced albedo)");

        // A mid grey, so a layer nothing was ever written into reflects as an unremarkable surface rather
        // than as a black hole -- which is the failure this whole array exists to remove.
        ClearToGrey();

        View = CreateView();
        Sampler = CreateSampler();
    }

    /// <summary>Gets how many texels square each layer is.</summary>
    /// <remarks>
    /// Nothing in the shader needs this. The hit shading reads the array in NORMALISED coordinates
    /// through its sampler, so the size is the array's own business and can be a setting rather than a
    /// constant two languages have to agree about.
    /// </remarks>
    public int Resolution { get; }

    /// <summary>Gets the array image.</summary>
    public Image Image { get; }

    /// <summary>Gets the view a ray shader reads the array through.</summary>
    public ImageView View { get; }

    /// <summary>Gets the sampler this array must be read through.</summary>
    /// <remarks>
    /// <b>Wrapping, and that is the whole point of it existing.</b> These are world textures: a wall's UV
    /// runs to forty along its length and a cylindrical tank's runs several times around its
    /// circumference. The compute passes share one clamping sampler — right for the sky and depth tables
    /// they mostly read — so the wrap used to be done in the shader with <c>fract</c>, which is not the
    /// same thing at all. <c>fract</c> moves the sample but cannot move the FILTER: at every point where
    /// the texture repeats, the bilinear tap set straddles a discontinuity and clamps to the edge texel
    /// instead of reaching round to the far side. At the reduced resolution these layers are stored at,
    /// one texel is a wide stripe, so every repeat drew a hard line — a row of vertical marks up the side
    /// of every storage tank and along every façade in the glazing's reflections.
    /// </remarks>
    public Sampler Sampler { get; }

    /// <summary>Gets how many layers are currently held.</summary>
    public int Count => _layers.Count;

    /// <summary>Gets whether the array ever turned a texture away for want of a layer.</summary>
    /// <remarks>
    /// Reported in the renderer's traced-world line. The failure it describes is invisible in the
    /// picture — some geometry shades from its own texture and some does not — so it has to be said
    /// somewhere a player or a bug report would actually look.
    /// </remarks>
    public bool Overflowed => _overflow;

    /// <summary>Reduces a texture to an albedo layer, or returns the layer it already has.</summary>
    /// <param name="textureId">The texture's identifier, which is what the mapping is keyed by.</param>
    /// <param name="pixels">The texture's whole mip chain; only the largest level is read.</param>
    /// <param name="width">The width of the largest level.</param>
    /// <param name="height">The height of the largest level.</param>
    /// <returns>The layer, or <c>-1</c> when the texture is too small or the array is full.</returns>
    public int Register(uint textureId, ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_layers.TryGetValue(textureId, out int existing))
        {
            return existing;
        }

        if (width <= 0 || height <= 0 || width * height > pixels.Length || width * height < MinimumTexels)
        {
            return -1;
        }

        int layer;
        if (_free.Count > 0)
        {
            layer = _free.Pop();
        }
        else if (_next < _capacity)
        {
            layer = _next++;
        }
        else
        {
            // Said once. A ray that finds no layer falls back to the frame and the cards, which is what
            // it did before this existed, so the cost is a reflection that is missing rather than wrong.
            _overflow = true;
            return -1;
        }

        _layers[textureId] = layer;
        Upload(layer, Reduce(pixels, width, height, Resolution));
        return layer;
    }

    /// <summary>Gives a destroyed texture's layer back to the array.</summary>
    /// <param name="textureId">The texture's identifier.</param>
    /// <remarks>
    /// The layer's contents are left as they are rather than cleared. Nothing reads a layer that no
    /// record names, and the next registration overwrites it outright.
    /// </remarks>
    public void Release(uint textureId)
    {
        if (_disposed || !_layers.Remove(textureId, out int layer))
        {
            return;
        }

        _free.Push(layer);
    }

    /// <summary>Finds the layer a texture was reduced to.</summary>
    /// <param name="textureId">The texture's identifier.</param>
    /// <param name="layer">Receives the layer.</param>
    /// <returns><see langword="true"/> when the texture has an albedo layer.</returns>
    public bool TryGetLayer(uint textureId, out int layer) => _layers.TryGetValue(textureId, out layer);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroySampler(_device, Sampler, null);
        _vk.DestroyImageView(_device, View, null);
        _vk.DestroyImage(_device, Image, null);
        _allocator.Free(_memory);
    }

    /// <summary>Box-filters a texture down to one layer-sized plane.</summary>
    /// <param name="pixels">The source pixels, largest level first.</param>
    /// <param name="width">The source width.</param>
    /// <param name="height">The source height.</param>
    /// <param name="resolution">How many texels square the result is.</param>
    /// <returns>The reduced texture, row-major RGBA.</returns>
    /// <remarks>
    /// Averaged over the whole source region a layer texel covers, not point sampled. These are 1998
    /// textures at 256 or 512 square and many of them are strongly patterned — brick, chain-link,
    /// camouflage — and dropping texels out of a pattern turns a uniform into noise. Noise in a
    /// reflection does not read as detail; it reads as sparkle that crawls when the camera moves.
    /// </remarks>
    private static byte[] Reduce(ReadOnlySpan<Bgra32> pixels, int width, int height, int resolution)
    {
        byte[] reduced = new byte[resolution * resolution * 4];

        for (int y = 0; y < resolution; y++)
        {
            int y0 = (int)(((float)y / resolution) * height);
            int y1 = Math.Min(height, Math.Max(y0 + 1, (int)(((y + 1.0f) / resolution) * height)));

            for (int x = 0; x < resolution; x++)
            {
                int x0 = (int)(((float)x / resolution) * width);
                int x1 = Math.Min(width, Math.Max(x0 + 1, (int)(((x + 1.0f) / resolution) * width)));

                int r = 0;
                int g = 0;
                int b = 0;
                int a = 0;
                int n = 0;

                for (int sy = y0; sy < y1; sy++)
                {
                    int row = sy * width;
                    for (int sx = x0; sx < x1; sx++)
                    {
                        Bgra32 texel = pixels[row + sx];
                        r += texel.R;
                        g += texel.G;
                        b += texel.B;
                        a += texel.A;
                        n++;
                    }
                }

                if (n == 0)
                {
                    continue;
                }

                int at = ((y * resolution) + x) * 4;
                reduced[at + 0] = (byte)(r / n);
                reduced[at + 1] = (byte)(g / n);
                reduced[at + 2] = (byte)(b / n);
                reduced[at + 3] = (byte)(a / n);
            }
        }

        return reduced;
    }

    /// <summary>Fills every layer with a mid grey, before any texture has been written.</summary>
    private void ClearToGrey()
    {
        Image image = Image;
        _commands.SingleTime(cmd =>
        {
            Transition(
                cmd,
                image,
                ImageLayout.Undefined,
                ImageLayout.TransferDstOptimal,
                PipelineStageFlags.TopOfPipeBit,
                PipelineStageFlags.TransferBit,
                0,
                AccessFlags.TransferWriteBit,
                0,
                (uint)_capacity);

            ClearColorValue clear = default;
            clear.Float32_0 = 0.5f;
            clear.Float32_1 = 0.5f;
            clear.Float32_2 = 0.5f;
            clear.Float32_3 = 1f;

            ImageSubresourceRange range = new()
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = (uint)_capacity,
            };

            _vk.CmdClearColorImage(cmd, image, ImageLayout.TransferDstOptimal, &clear, 1, &range);

            Transition(
                cmd,
                image,
                ImageLayout.TransferDstOptimal,
                ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.ComputeShaderBit,
                AccessFlags.TransferWriteBit,
                AccessFlags.ShaderReadBit,
                0,
                MaxLayers);
        });
    }

    /// <summary>Writes one reduced texture into its layer.</summary>
    /// <param name="layer">The layer to write.</param>
    /// <param name="texels">The reduced texture, <see cref="Resolution"/> squared RGBA bytes.</param>
    private void Upload(int layer, byte[] texels)
    {
        using VulkanBuffer staging = new(
            _vk,
            _owner,
            _allocator,
            (ulong)texels.Length,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        staging.Write<byte>(texels);

        Image image = Image;
        Buffer stagingHandle = staging.Handle;
        _commands.SingleTime(cmd =>
        {
            Transition(
                cmd,
                image,
                ImageLayout.ShaderReadOnlyOptimal,
                ImageLayout.TransferDstOptimal,
                PipelineStageFlags.ComputeShaderBit,
                PipelineStageFlags.TransferBit,
                AccessFlags.ShaderReadBit,
                AccessFlags.TransferWriteBit,
                (uint)layer,
                1);

            BufferImageCopy region = new()
            {
                BufferOffset = 0,
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = (uint)layer,
                    LayerCount = 1,
                },
                ImageExtent = new Extent3D((uint)Resolution, (uint)Resolution, 1),
            };

            _vk.CmdCopyBufferToImage(cmd, stagingHandle, image, ImageLayout.TransferDstOptimal, 1, &region);

            Transition(
                cmd,
                image,
                ImageLayout.TransferDstOptimal,
                ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.ComputeShaderBit,
                AccessFlags.TransferWriteBit,
                AccessFlags.ShaderReadBit,
                (uint)layer,
                1);
        });
    }

    private void Transition(
        CommandBuffer cmd,
        Image image,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        PipelineStageFlags sourceStage,
        PipelineStageFlags destinationStage,
        AccessFlags sourceAccess,
        AccessFlags destinationAccess,
        uint baseLayer,
        uint layerCount)
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
                BaseArrayLayer = baseLayer,
                LayerCount = layerCount,
            },
            SrcAccessMask = sourceAccess,
            DstAccessMask = destinationAccess,
        };

        _vk.CmdPipelineBarrier(cmd, sourceStage, destinationStage, 0, 0, null, 0, null, 1, &barrier);
    }

    /// <summary>Builds the wrapping sampler these layers are read through. See <see cref="Sampler"/>.</summary>
    private Sampler CreateSampler()
    {
        SamplerCreateInfo createInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,

            // No mips are stored, so the mode only has to be valid.
            MipmapMode = SamplerMipmapMode.Nearest,

            // REPEAT on the two texture axes and clamp on the third, which is the layer and must never
            // wrap: a UV a hair past the last layer would otherwise read the first one, painting some
            // other object's texture onto this one.
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            BorderColor = BorderColor.FloatOpaqueBlack,
        };

        Sampler sampler;
        VkCheck.Ok(
            _vk.CreateSampler(_device, &createInfo, null, &sampler), "vkCreateSampler (traced albedo)");
        return sampler;
    }

    private ImageView CreateView()
    {
        ImageViewCreateInfo createInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = Image,
            ViewType = ImageViewType.Type2DArray,
            Format = Format.R8G8B8A8Srgb,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = MaxLayers,
            },
        };

        ImageView view;
        VkCheck.Ok(_vk.CreateImageView(_device, &createInfo, null, &view), "vkCreateImageView (traced albedo)");
        return view;
    }
}
