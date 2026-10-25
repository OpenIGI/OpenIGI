using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>One frame's rectangle in a <see cref="SpriteStrip"/>, in normalised texture coordinates.</summary>
/// <param name="U0">The left edge.</param>
/// <param name="V0">The top edge.</param>
/// <param name="U1">The right edge.</param>
/// <param name="V1">The bottom edge.</param>
public readonly record struct SpriteStripFrame(float U0, float V0, float U1, float V1)
{
    /// <summary>Gets the whole texture, for a strip with no frames.</summary>
    public static SpriteStripFrame Full => new(0f, 0f, 1f, 1f);
}

/// <summary>A retail sprite's frames, each cropped out of its own texture and laid side by side in one strip texture.</summary>
/// <param name="Texture">The strip.</param>
/// <param name="Frames">Each frame's rectangle in it, in the sprite's order.</param>
/// <remarks>
/// One texture, so a particle system draws every frame of its sprite in one batch and picks a frame by its rectangle.
/// </remarks>
public readonly record struct SpriteStrip(TextureHandle Texture, SpriteStripFrame[] Frames)
{
    /// <summary>Gets the strip of a sprite that could not be loaded: no texture and no frames.</summary>
    public static SpriteStrip Empty => new(TextureHandle.None, []);

    /// <summary>Loads a sprite from a sprite archive into a strip.</summary>
    /// <param name="device">The device the strip is uploaded to.</param>
    /// <param name="fileSystem">The game's files, or <see langword="null"/> where there are none.</param>
    /// <param name="archive">The archive the sprite ships in.</param>
    /// <param name="spriteName">The sprite's file name, such as <c>smoke2.spr</c>.</param>
    /// <returns>The strip, or <see cref="Empty"/> when the sprite is missing, has no drawable frame or does not upload.</returns>
    public static SpriteStrip Load(IGraphicsDevice device, VirtualFileSystem? fileSystem, string archive, string spriteName)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(archive);
        try
        {
            if (fileSystem is null || !fileSystem.Exists(archive))
            {
                return Empty;
            }

            if (fileSystem.ReadArchive(archive).FindBody(spriteName) is not { } entry)
            {
                return Empty;
            }

            SpriteAsset asset = SpriteLoader.Load(entry.Data, spriteName);
            if (asset.Frames.Count == 0 || asset.Textures.Count == 0)
            {
                return Empty;
            }

            List<(Bgra32[] Pixels, int Width, int Height)> crops = [];
            foreach (SpriteFrame frame in asset.Frames)
            {
                if (frame.IsEmpty || (uint)frame.TextureIndex >= (uint)asset.Textures.Count)
                {
                    continue;
                }

                TextureImage source = asset.GetTexture(frame);
                ReadOnlySpan<Bgra32> pixels = source.GetLevel(0);
                int left = Math.Clamp((int)MathF.Floor(frame.U0 * source.Width), 0, source.Width - 1);
                int top = Math.Clamp((int)MathF.Floor(frame.V0 * source.Height), 0, source.Height - 1);
                int right = Math.Clamp((int)MathF.Ceiling(frame.U1 * source.Width), left + 1, source.Width);
                int bottom = Math.Clamp((int)MathF.Ceiling(frame.V1 * source.Height), top + 1, source.Height);
                int width = right - left;
                int height = bottom - top;
                Bgra32[] cropped = new Bgra32[width * height];
                for (int y = 0; y < height; y++)
                {
                    pixels.Slice(((top + y) * source.Width) + left, width)
                        .CopyTo(cropped.AsSpan(y * width, width));
                }

                crops.Add((cropped, width, height));
            }

            if (crops.Count == 0)
            {
                return Empty;
            }

            int atlasWidth = crops.Sum(crop => crop.Width);
            int atlasHeight = crops.Max(crop => crop.Height);
            Bgra32[] atlas = new Bgra32[checked(atlasWidth * atlasHeight)];
            SpriteStripFrame[] frames = new SpriteStripFrame[crops.Count];
            int offsetX = 0;
            for (int i = 0; i < crops.Count; i++)
            {
                (Bgra32[] pixels, int width, int height) = crops[i];
                for (int y = 0; y < height; y++)
                {
                    pixels.AsSpan(y * width, width)
                        .CopyTo(atlas.AsSpan((y * atlasWidth) + offsetX, width));
                }

                frames[i] = new SpriteStripFrame(
                    (float)offsetX / atlasWidth,
                    0f,
                    (float)(offsetX + width) / atlasWidth,
                    (float)height / atlasHeight);
                offsetX += width;
            }

            TextureHandle texture = device.CreateTexture(
                new TextureDescription(
                    atlasWidth,
                    atlasHeight,
                    MipLevels: 1,
                    TextureFilter.Linear,
                    TextureAddressMode.Clamp),
                atlas);
            return new SpriteStrip(texture, frames);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or PackFormatException or GraphicsDeviceException)
        {
            _ = ex;
            return Empty;
        }
    }

    /// <summary>Loads one of the level's <c>.tex</c> textures as it is, clamped, for an effect to draw with.</summary>
    /// <param name="device">The device it is uploaded to.</param>
    /// <param name="resources">The level's resources, or <see langword="null"/> where there are none.</param>
    /// <param name="name">The texture's name, as the effect's model or table gives it.</param>
    /// <returns>The texture, or <see cref="TextureHandle.None"/> when it is missing, unreadable or does not upload.</returns>
    public static TextureHandle LoadTexture(IGraphicsDevice device, ResourceRegistry? resources, string name)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (resources is null || !resources.TryLoad(LoadedPack.TextureResourceName(name), out ReadOnlyMemory<byte> bytes))
        {
            return TextureHandle.None;
        }

        try
        {
            TextureImage image = TexFile.Load(bytes.Span);
            return device.CreateTexture(
                new TextureDescription(
                    image.Width,
                    image.Height,
                    image.MipLevels,
                    TextureFilter.Linear,
                    TextureAddressMode.Clamp),
                image.Pixels);
        }
        catch (Exception ex) when (ex is TextureFormatException or ArgumentException or GraphicsDeviceException)
        {
            _ = ex;
            return TextureHandle.None;
        }
    }
}
