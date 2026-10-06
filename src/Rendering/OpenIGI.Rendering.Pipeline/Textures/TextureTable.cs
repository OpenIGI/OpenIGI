using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Pipeline.Draw2D;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.Textures;

/// <summary>
/// The textures a level has loaded, by handle, and the samplers they are read through.
/// </summary>
/// <remarks>
/// <para>
/// One white texel is made up front and handed back for every handle that names nothing. That is not a
/// convenience: white is the identity of every multiply the shaders do, so a draw whose texture was
/// destroyed a frame early loses its picture rather than the frame, and no shader needs a branch
/// saying which case it is in.
/// </para>
/// <para>
/// Samplers are shared by description rather than made per texture. A level has thousands of textures
/// and perhaps four distinct ways of filtering them, and a sampler per texture is thousands of objects
/// where a handful would do — which on some drivers is a hard limit rather than a waste.
/// </para>
/// </remarks>
public sealed class TextureTable : ITextureBindingSource, IDisposable
{
    private readonly IRhiDevice _device;
    private readonly RetiredResources? _retired;
    private readonly Dictionary<uint, Entry> _textures = [];
    private readonly Dictionary<RhiSamplerDescriptor, IRhiSampler> _samplers = [];
    private uint _next = 1;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="TextureTable"/> class.</summary>
    /// <param name="device">The device the textures live on.</param>
    /// <param name="retired">
    /// Where a destroyed texture goes. A texture destroyed where it is released invalidates the command
    /// buffer of any frame still naming it, which voids every command recorded afterwards; null frees at
    /// once, which is only right where nothing is in flight.
    /// </param>
    public TextureTable(IRhiDevice device, RetiredResources? retired = null)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        _retired = retired;
        White = Create(new TextureDescription(1, 1), [new Bgra32(255, 255, 255, 255)], "white");
        NeutralDetail = Create(
            new TextureDescription(1, 1) { HoldsData = true },
            [new Bgra32(128, 128, 128, 255)],
            "neutral-detail");
    }

    /// <summary>Gets the handle of the white texel every unbound slot resolves to.</summary>
    public TextureHandle White { get; }

    /// <summary>Gets the surface-detail texel a diffuse with no detail map of its own is given.</summary>
    /// <remarks>
    /// Not white, and the exact values are the point. A flat tangent normal is a half in x and y, since
    /// the channel stores the range minus one to one biased into nought to one; a roughness multiplier of
    /// one is stored halved, so a half again; and the alpha holds how dielectric a surface is, so opaque
    /// leaves the material's own metallic alone. Every term of the detail block folds to what it would
    /// have been, so no draw needs a flag saying whether it has a map. Stored as data, not colour, so a
    /// read returns the half it was given rather than a decoded fifth.
    /// </remarks>
    public TextureHandle NeutralDetail { get; }

    /// <summary>Gets the mip bias every mipmapped texture is sampled with.</summary>
    /// <remarks>
    /// The render-to-output ratio decides it, so it moves whenever the reconstruction or its quality does;
    /// <see cref="Rebias"/> is how a change reaches textures already loaded.
    /// </remarks>
    public float MipLodBias { get; private set; } = TextureLodBias.FromSettings();

    /// <summary>Re-resolves every mipmapped texture's sampler at a new mip bias.</summary>
    /// <param name="bias">The bias, in mip levels; negative is sharper.</param>
    /// <returns>The handles whose sampler changed, so the bind groups that captured the old one can go.</returns>
    /// <remarks>
    /// Samplers are shared by description and the bias is part of the description, so this costs a handful
    /// of new samplers, not one per texture. The old ones stay cached: switching back is free.
    /// </remarks>
    public IReadOnlyList<uint> Rebias(float bias)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (MathF.Abs(bias - MipLodBias) < 1e-4f)
        {
            return [];
        }

        MipLodBias = bias;
        List<uint> changed = [];
        foreach ((uint id, Entry entry) in _textures.ToArray())
        {
            if (entry.Description.MipLevels <= 1)
            {
                continue;
            }

            IRhiSampler sampler = SamplerFor(entry.Description);
            if (!ReferenceEquals(sampler, entry.Sampler))
            {
                _textures[id] = entry with { Sampler = sampler };
                changed.Add(id);
            }
        }

        return changed;
    }

    /// <summary>Gets how many textures are loaded, the white one included.</summary>
    public int Count => _textures.Count;

    /// <summary>Gets or sets how many samples an anisotropic lookup may take.</summary>
    /// <remarks>
    /// Applied to textures created afterwards. Changing it does not re-make what is already loaded,
    /// which is what the original did too: the setting takes effect on the next level.
    /// </remarks>
    public int MaxAnisotropy { get; set; } = 1;

    /// <summary>Creates a texture and uploads every level the caller handed over.</summary>
    /// <param name="description">Its size, how many levels it has, and how it is filtered.</param>
    /// <param name="pixels">
    /// The whole chain, tightly packed, largest level first — which is how the engine lays a texture out
    /// and what every backend here is given. Uploading only the first level leaves the rest of the chain
    /// undefined, and nothing says so: the texture looks right up close and reads as BLACK the moment it
    /// is minified enough to pick a lower level, which is most of a level most of the time.
    /// </param>
    /// <param name="label">A name for captures and diagnostics.</param>
    /// <returns>Its handle.</returns>
    public TextureHandle Create(in TextureDescription description, ReadOnlySpan<Bgra32> pixels, string label = "")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        uint id = _next++;

        IRhiTexture texture = _device.CreateTexture(new RhiTextureDescriptor(
            Math.Max(1, description.Width),
            Math.Max(1, description.Height),
            description.HoldsData ? RhiTextureFormat.Bgra8Unorm : RhiTextureFormat.Bgra8UnormSrgb,
            RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination,
            MipLevelCount: Math.Max(1, description.MipLevels),
            Label: label.Length > 0 ? label : $"texture-{id}"));

        _textures[id] = new Entry(texture, SamplerFor(description), description);

        UploadChain(texture, description, pixels);

        return new TextureHandle(id);
    }

    /// <summary>Creates a block-compressed texture and uploads its stored chain as it is.</summary>
    /// <param name="description">Its size, how many levels it has, and how it is filtered.</param>
    /// <param name="format">The block format.</param>
    /// <param name="chain">Every level's blocks, back to back, largest first.</param>
    /// <param name="label">A name for captures and diagnostics.</param>
    /// <returns>Its handle.</returns>
    /// <remarks>
    /// Stops where the chain runs out, as <see cref="Create"/> does with pixels. Each level is a row of
    /// whole blocks per four texels across, partial blocks counted whole.
    /// </remarks>
    public TextureHandle CreateCompressed(
        in TextureDescription description, BlockFormat format, ReadOnlySpan<byte> chain, string label = "")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        uint id = _next++;
        RhiTextureFormat rhiFormat = (format, description.HoldsData) switch
        {
            (BlockFormat.Bc1, false) => RhiTextureFormat.Bc1RgbaUnormSrgb,
            (BlockFormat.Bc1, true) => RhiTextureFormat.Bc1RgbaUnorm,
            (BlockFormat.Bc7, false) => RhiTextureFormat.Bc7RgbaUnormSrgb,
            (BlockFormat.Bc7, true) => RhiTextureFormat.Bc7RgbaUnorm,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a block format."),
        };

        IRhiTexture texture = _device.CreateTexture(new RhiTextureDescriptor(
            Math.Max(1, description.Width),
            Math.Max(1, description.Height),
            rhiFormat,
            RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination,
            MipLevelCount: Math.Max(1, description.MipLevels),
            Label: label.Length > 0 ? label : $"texture-{id}"));

        _textures[id] = new Entry(texture, SamplerFor(description), description);

        int levels = Math.Max(1, description.MipLevels);
        int at = 0;
        for (int level = 0; level < levels; level++)
        {
            (int width, int height) = MipSize(texture, level);
            int bytes = BlockCompression.LevelBytes(format, width, height);
            if (at + bytes > chain.Length)
            {
                break;
            }

            _device.WriteTexture(
                new RhiTextureRegion(texture, level),
                chain.Slice(at, bytes),
                BlockCompression.RowBytes(format, width),
                new RhiExtent3D(width, height));
            at += bytes;
        }

        return new TextureHandle(id);
    }

    /// <summary>Replaces one mip level of a texture.</summary>
    /// <param name="handle">The texture.</param>
    /// <param name="mipLevel">Which level.</param>
    /// <param name="pixels">Its texels.</param>
    public void Update(TextureHandle handle, int mipLevel, ReadOnlySpan<Bgra32> pixels)
    {
        if (pixels.IsEmpty || !_textures.TryGetValue(handle.Id, out Entry entry))
        {
            return;
        }

        (int width, int height) = MipSize(entry.Texture, mipLevel);
        Upload(entry.Texture, mipLevel, pixels, new TextureDescription(width, height));
    }

    /// <summary>Destroys a texture.</summary>
    /// <param name="handle">The texture.</param>
    /// <returns><see langword="true"/> when one was destroyed.</returns>
    public bool Destroy(TextureHandle handle)
    {
        if (handle.Id == White.Id || handle.Id == NeutralDetail.Id || !_textures.Remove(handle.Id, out Entry entry))
        {
            return false;
        }

        if (_retired is { } queue)
        {
            queue.Retire(entry.Texture);
        }
        else
        {
            entry.Texture.Dispose();
        }

        return true;
    }

    /// <inheritdoc/>
    public RhiTextureBinding Resolve(TextureHandle handle)
    {
        if (!_textures.TryGetValue(handle.Id, out Entry entry))
        {
            entry = _textures[White.Id];
            return new RhiTextureBinding(White.Id, entry.Texture.View, entry.Sampler);
        }

        return new RhiTextureBinding(handle.Id, entry.Texture.View, entry.Sampler);
    }

    /// <summary>Gets the texture behind a handle, or null when it names nothing.</summary>
    /// <param name="handle">The handle.</param>
    /// <returns>The texture.</returns>
    public IRhiTexture? Find(TextureHandle handle) =>
        _textures.TryGetValue(handle.Id, out Entry entry) ? entry.Texture : null;

    /// <summary>Remakes a texture in another format under the same handle, if it is not in it already.</summary>
    /// <param name="handle">The texture.</param>
    /// <param name="format">The format it has to be in.</param>
    /// <returns>The texture in that format, or null when the handle names nothing.</returns>
    /// <remarks>
    /// For a texture something is about to copy into, which has to match the source's channel order: a
    /// copy moves bytes and cannot swizzle. The contents are not carried over -- the copy replaces them --
    /// and the old texture is retired rather than freed, since frames in flight may still sample it.
    /// </remarks>
    public IRhiTexture? Reformat(TextureHandle handle, RhiTextureFormat format)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_textures.TryGetValue(handle.Id, out Entry entry))
        {
            return null;
        }

        if (entry.Texture.Format == format)
        {
            return entry.Texture;
        }

        IRhiTexture old = entry.Texture;
        IRhiTexture remade = _device.CreateTexture(new RhiTextureDescriptor(
            old.Width,
            old.Height,
            format,
            old.Usage,
            MipLevelCount: old.MipLevelCount,
            Label: old.Label));

        _textures[handle.Id] = entry with { Texture = remade };

        if (_retired is { } queue)
        {
            queue.Retire(old);
        }
        else
        {
            old.Dispose();
        }

        return remade;
    }

    /// <summary>Takes a handle from the table's own sequence that names none of its textures.</summary>
    /// <returns>The handle, which no texture of the table will ever be given.</returns>
    /// <remarks>
    /// For an image the device resolves itself — a scene target, a card page — which is remade under the
    /// handle the game keeps. Taking it from the same sequence is what keeps it from colliding with one.
    /// </remarks>
    public TextureHandle Reserve()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new TextureHandle(_next++);
    }

    /// <summary>Takes a texture the table did not create under its own handle.</summary>
    /// <param name="texture">The texture, which the table then owns.</param>
    /// <param name="description">How it is to be filtered.</param>
    /// <returns>Its handle.</returns>
    /// <remarks>
    /// For the frames the renderer captures for itself — a held backdrop, a card page — which are made
    /// as targets rather than uploaded and still have to be bindable by handle afterwards.
    /// </remarks>
    public TextureHandle Adopt(IRhiTexture texture, in TextureDescription description)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ObjectDisposedException.ThrowIf(_disposed, this);

        uint id = _next++;
        _textures[id] = new Entry(texture, SamplerFor(description), description);

        return new TextureHandle(id);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (Entry entry in _textures.Values)
        {
            entry.Texture.Dispose();
        }

        foreach (IRhiSampler sampler in _samplers.Values)
        {
            sampler.Dispose();
        }

        _textures.Clear();
        _samplers.Clear();
    }

    /// <summary>Uploads as many levels of a packed chain as the caller actually provided.</summary>
    /// <param name="texture">The texture being filled.</param>
    /// <param name="description">Its size and level count.</param>
    /// <param name="pixels">The packed chain, largest level first.</param>
    /// <remarks>
    /// Stops where the span runs out rather than reading past it: a caller that hands over one level of
    /// a texture declared with several is uploading what it has, and the levels it did not give are
    /// filled by a later <see cref="Update"/> or left as they are.
    /// </remarks>
    private void UploadChain(IRhiTexture texture, in TextureDescription description, ReadOnlySpan<Bgra32> pixels)
    {
        int levels = Math.Max(1, description.MipLevels);
        int at = 0;

        for (int level = 0; level < levels; level++)
        {
            (int width, int height) = MipSize(texture, level);
            int texels = width * height;

            if (at + texels > pixels.Length)
            {
                return;
            }

            Upload(texture, level, pixels.Slice(at, texels), new TextureDescription(width, height));
            at += texels;
        }
    }

    private static (int Width, int Height) MipSize(IRhiTexture texture, int level) => (
        Math.Max(1, texture.Width >> level),
        Math.Max(1, texture.Height >> level));

    private void Upload(IRhiTexture texture, int level, ReadOnlySpan<Bgra32> pixels, in TextureDescription size)
    {
        _device.WriteTexture(
            new RhiTextureRegion(texture, level),
            pixels,
            Math.Max(1, size.Width) * 4,
            new RhiExtent3D(Math.Max(1, size.Width), Math.Max(1, size.Height)));
    }

    private IRhiSampler SamplerFor(in TextureDescription description)
    {
        RhiFilter filter = description.Filter is TextureFilter.Point ? RhiFilter.Nearest : RhiFilter.Linear;

        RhiAddressMode address = description.AddressMode is TextureAddressMode.Repeat
            ? RhiAddressMode.Repeat
            : RhiAddressMode.ClampToEdge;

        RhiSamplerDescriptor wanted = new(
            filter,
            filter,
            filter,
            address,
            address,
            address,

            // Anisotropy only makes sense where there are mips to choose between; asking for it on a
            // single-level texture costs a sampler and changes nothing.
            description.MipLevels > 1 ? Math.Max(1, MaxAnisotropy) : 1,
            Compare: null,
            LodMin: 0f,
            LodMax: 1000f,
            $"{filter}-{address}",

            // The frame's mip bias, only where there are levels to choose.
            MipLodBias: description.MipLevels > 1 ? MipLodBias : 0f);

        if (_samplers.TryGetValue(wanted, out IRhiSampler? existing))
        {
            return existing;
        }

        IRhiSampler made = _device.CreateSampler(wanted);
        _samplers[wanted] = made;
        return made;
    }

    private readonly record struct Entry(IRhiTexture Texture, IRhiSampler Sampler, TextureDescription Description);
}
