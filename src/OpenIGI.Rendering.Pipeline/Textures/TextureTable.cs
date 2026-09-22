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
    private readonly Dictionary<uint, Entry> _textures = [];
    private readonly Dictionary<RhiSamplerDescriptor, IRhiSampler> _samplers = [];
    private uint _next = 1;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="TextureTable"/> class.</summary>
    /// <param name="device">The device the textures live on.</param>
    public TextureTable(IRhiDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        White = Create(new TextureDescription(1, 1), [new Bgra32(255, 255, 255, 255)], "white");
    }

    /// <summary>Gets the handle of the white texel every unbound slot resolves to.</summary>
    public TextureHandle White { get; }

    /// <summary>Gets how many textures are loaded, the white one included.</summary>
    public int Count => _textures.Count;

    /// <summary>Gets or sets how many samples an anisotropic lookup may take.</summary>
    /// <remarks>
    /// Applied to textures created afterwards. Changing it does not re-make what is already loaded,
    /// which is what the original did too: the setting takes effect on the next level.
    /// </remarks>
    public int MaxAnisotropy { get; set; } = 1;

    /// <summary>Creates a texture and uploads its first mip level.</summary>
    /// <param name="description">Its size and how it is filtered.</param>
    /// <param name="pixels">The texels of mip zero.</param>
    /// <param name="label">A name for captures and diagnostics.</param>
    /// <returns>Its handle.</returns>
    public TextureHandle Create(in TextureDescription description, ReadOnlySpan<Bgra32> pixels, string label = "")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        uint id = _next++;

        IRhiTexture texture = _device.CreateTexture(new RhiTextureDescriptor(
            Math.Max(1, description.Width),
            Math.Max(1, description.Height),
            RhiTextureFormat.Bgra8UnormSrgb,
            RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination,
            MipLevelCount: Math.Max(1, description.MipLevels),
            Label: label.Length > 0 ? label : $"texture-{id}"));

        _textures[id] = new Entry(texture, SamplerFor(description));

        if (!pixels.IsEmpty)
        {
            Upload(texture, 0, pixels, description);
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
        if (handle.Id == White.Id || !_textures.Remove(handle.Id, out Entry entry))
        {
            return false;
        }

        entry.Texture.Dispose();
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
        _textures[id] = new Entry(texture, SamplerFor(description));

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
            $"{filter}-{address}");

        if (_samplers.TryGetValue(wanted, out IRhiSampler? existing))
        {
            return existing;
        }

        IRhiSampler made = _device.CreateSampler(wanted);
        _samplers[wanted] = made;
        return made;
    }

    private readonly record struct Entry(IRhiTexture Texture, IRhiSampler Sampler);
}
