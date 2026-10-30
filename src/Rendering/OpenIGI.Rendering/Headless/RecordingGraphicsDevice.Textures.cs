using OpenIGI.Core.Graphics;

namespace OpenIGI.Rendering.Headless;

public sealed partial class RecordingGraphicsDevice
{
    private uint _nextTexture = 1;

    /// <summary>Every 2D texture created, in order, with the description it was created under.</summary>
    /// <remarks>
    /// The description rather than only the pixels, because the address mode and the mip count are
    /// decisions a caller makes and gets silently wrong: <c>TextureDescription</c>'s two-argument
    /// constructor is 2D drawing's -- one level, clamped -- and taking it for a tiling world texture
    /// smears its edge texel across everything it is drawn on.
    /// </remarks>
    public List<TextureDescription> Textures { get; } = [];

    /// <inheritdoc/>
    public TextureHandle CreateTexture(in TextureDescription description, ReadOnlySpan<Bgra32> pixels)
    {
        Record(Textures, description);
        return new(_nextTexture++);
    }

    /// <inheritdoc/>
    public void UpdateTexture(TextureHandle texture, int mipLevel, ReadOnlySpan<Bgra32> pixels)
    {
    }

    /// <inheritdoc/>
    public void DestroyTexture(TextureHandle texture)
    {
    }

    /// <inheritdoc/>
    public void SetTexture(int unit, TextureHandle texture)
    {
    }

    /// <summary>The detail map attached to each diffuse texture, in the order they were attached.</summary>
    public List<(TextureHandle Diffuse, TextureHandle Detail)> TextureDetails { get; } = [];

    /// <inheritdoc/>
    public void SetTextureDetail(TextureHandle diffuse, TextureHandle detail) =>
        Record(TextureDetails, (diffuse, detail));

    // Volumes share the texture handle space with 2D textures, so their counter starts well clear of
    // anything CreateTexture hands out -- a test that confuses the two would otherwise pass.
    private readonly Dictionary<uint, int> _cubeEdges = [];
    private uint _nextVolumeId = 10_000;
    private uint _nextCubeId = 20_000;

    /// <summary>The volume textures allocated, as name, dimensions and format.</summary>
    /// <remarks>Not to be confused with <see cref="Volumes"/>, which is the probe field.</remarks>
    public List<(string Name, int Width, int Height, int Depth, VolumeFormat Format)> VolumeTextures { get; } = [];

    /// <inheritdoc/>
    public void ReadStorageTexture(TextureHandle texture, Span<Bgra32> destination)
    {
    }

    /// <inheritdoc/>
    public TextureHandle CreateStorageTexture(
        int width, int height, VolumeFormat format, string name)
    {
        Record(VolumeTextures, (name, width, height, 1, format));
        return new TextureHandle(_nextVolumeId++);
    }

    /// <inheritdoc/>
    public TextureHandle CreateVolumeTexture(
        int width, int height, int depth, VolumeFormat format, string name, bool shaderWritten = true)
    {
        Record(VolumeTextures, (name, width, height, depth, format));
        return new TextureHandle(_nextVolumeId++);
    }

    /// <inheritdoc/>
    public void UpdateVolumeTexture<T>(TextureHandle texture, ReadOnlySpan<T> data)
        where T : unmanaged
    {
    }

    /// <inheritdoc/>
    public void ReadVolumeTexture<T>(TextureHandle texture, Span<T> destination)
        where T : unmanaged
    {
    }

    /// <inheritdoc/>
    public void SetVolumeTexture(int unit, TextureHandle texture)
    {
    }

    /// <summary>Every cube map created, by name and edge length, in order.</summary>
    public List<(string Name, int Edge)> CubeTextures { get; } = [];

    /// <summary>Each cube face written, with the texels it was written from.</summary>
    /// <remarks>
    /// The pixels are copied rather than referenced. A caller that uploads six faces out of one reused
    /// buffer -- which is the obvious way to walk an archive -- would otherwise leave six entries all
    /// showing the last face, and a test asserting the faces differ would pass on a device that had
    /// written the same face six times.
    /// </remarks>
    public List<(TextureHandle Cube, int Face, Bgra32[] Pixels)> CubeFaceUploads { get; } = [];

    /// <inheritdoc/>
    public TextureHandle CreateCubeTexture(int edge, string name)
    {
        if (edge <= 0)
        {
            throw new GraphicsDeviceException($"A cube's edge is {edge}, which is not a length.");
        }

        Record(CubeTextures, (name, edge));
        _cubeEdges[_nextCubeId] = edge;
        return new TextureHandle(_nextCubeId++);
    }

    /// <inheritdoc/>
    public void UpdateCubeFace(TextureHandle cube, int face, ReadOnlySpan<Bgra32> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(face);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(face, IGraphicsDevice.CubeFaceCount);

        if (!_cubeEdges.TryGetValue(cube.Id, out int edge))
        {
            throw new GraphicsDeviceException($"{cube} is not a cube of this device.");
        }

        if (pixels.Length != edge * edge)
        {
            throw new GraphicsDeviceException(
                $"A face of a {edge}-texel cube holds {edge * edge} texels, not {pixels.Length}.");
        }

        Record(CubeFaceUploads, (cube, face, pixels.ToArray()));
    }

    /// <summary>The texture-quality settings applied, in order.</summary>
    public List<int> TextureQuality { get; } = [];

    /// <inheritdoc/>
    public void SetTextureQuality(int maxAnisotropy) => Record(TextureQuality, maxAnisotropy);

    /// <summary>Every texture mip bias set, in order.</summary>
    public List<float> TextureLodBiases { get; } = [];

    /// <inheritdoc/>
    public void SetTextureLodBias(float bias) => Record(TextureLodBiases, bias);
}
