using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Sprites;
using OpenIGI.Formats.Textures;
using OpenIGI.Rendering;

namespace OpenIGI.Engine.Graphics;

/// <summary>
/// A <see cref="SpriteAsset"/> uploaded to a graphics device: the decoded pixels become device
/// textures, and the frame table becomes something drawable.
/// </summary>
/// <remarks>
/// This is the bridge between <c>OpenIGI.Formats</c>, which knows the file layouts, and
/// <c>OpenIGI.Rendering</c>, which knows nothing about them. Disposing the resource releases its
/// textures; the underlying <see cref="SpriteAsset"/> stays valid and can be uploaded again.
/// </remarks>
public sealed class SpriteResource : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly TextureHandle[] _textures;
    private bool _disposed;

    private SpriteResource(IGraphicsDevice device, SpriteAsset asset, TextureHandle[] textures)
    {
        _device = device;
        Asset = asset;
        _textures = textures;
    }

    /// <summary>Gets the asset this resource was uploaded from.</summary>
    public SpriteAsset Asset { get; }

    /// <summary>Gets the device textures, one per asset texture.</summary>
    public IReadOnlyList<TextureHandle> Textures => _textures;

    /// <summary>Gets a value indicating whether this sprite can render text.</summary>
    public bool IsFont => Asset.IsFont;

    /// <summary>Uploads a sprite asset to a device.</summary>
    /// <param name="device">The device to upload to.</param>
    /// <param name="asset">The decoded asset.</param>
    /// <param name="filter">How the textures are sampled. UI art is authored at 1:1 and looks best unfiltered.</param>
    /// <returns>The uploaded resource.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="GraphicsDeviceException">A texture could not be created.</exception>
    public static SpriteResource Upload(
        IGraphicsDevice device,
        SpriteAsset asset,
        TextureFilter filter = TextureFilter.Point)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(asset);

        TextureHandle[] handles = new TextureHandle[asset.Textures.Count];
        int created = 0;
        try
        {
            for (; created < handles.Length; created++)
            {
                TextureImage image = asset.Textures[created];
                TextureDescription description = new(
                    image.Width,
                    image.Height,
                    image.MipLevels,
                    filter,
                    TextureAddressMode.Clamp);

                handles[created] = device.CreateTexture(description, image.Pixels);
            }
        }
        catch
        {
            for (int i = 0; i < created; i++)
            {
                device.DestroyTexture(handles[i]);
            }

            throw;
        }

        return new SpriteResource(device, asset, handles);
    }

    /// <summary>Queues one frame at a position, at its natural size.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="frameIndex">The frame to draw.</param>
    /// <param name="x">The destination left edge in pixels, before the frame's own offset.</param>
    /// <param name="y">The destination top edge in pixels, before the frame's own offset.</param>
    /// <param name="color">The colour modulated with the texture.</param>
    /// <param name="blend">How the frame combines with what is behind it.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="frameIndex"/> is not a frame of this sprite.</exception>
    public void Draw(
        SpriteBatch batch,
        int frameIndex,
        float x,
        float y,
        Bgra32 color,
        BlendMode blend = BlendMode.AlphaBlend)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentOutOfRangeException.ThrowIfNegative(frameIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frameIndex, Asset.Frames.Count);

        DrawFrame(batch, Asset.Frames[frameIndex], x, y, 1.0f, 1.0f, color, blend);
    }

    /// <summary>Queues one frame stretched into a destination rectangle.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="frameIndex">The frame to draw.</param>
    /// <param name="destination">The destination rectangle in pixels.</param>
    /// <param name="color">The colour modulated with the texture.</param>
    /// <param name="blend">How the frame combines with what is behind it.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="frameIndex"/> is not a frame of this sprite.</exception>
    public void DrawStretched(
        SpriteBatch batch,
        int frameIndex,
        in RectF destination,
        Bgra32 color,
        BlendMode blend = BlendMode.AlphaBlend)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentOutOfRangeException.ThrowIfNegative(frameIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frameIndex, Asset.Frames.Count);

        SpriteFrame frame = Asset.Frames[frameIndex];
        if (frame.IsEmpty)
        {
            return;
        }

        batch.Draw(
            _textures[frame.TextureIndex],
            destination,
            SourceRect(frame),
            color,
            blend);
    }

    /// <summary>Queues a tiled picture, reassembled from its frames and scaled into a rectangle.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="destination">The destination rectangle in pixels. The assembled picture is scaled to fill it.</param>
    /// <param name="color">The colour modulated with the texture.</param>
    /// <param name="blend">How the picture combines with what is behind it.</param>
    /// <exception cref="InvalidOperationException">This sprite has no tile grid.</exception>
    /// <remarks>
    /// <para>
    /// Tiles are placed by their grid cell, at <c>(column * tileWidth, row * tileHeight)</c>, which is
    /// how the original derives them — the positions stored alongside each cell are redundant. Empty
    /// cells are skipped.
    /// </para>
    /// <para>
    /// Every tile edge is rounded to a whole pixel. Adjacent tiles share an edge, so rounding both
    /// sides of it the same way keeps them exactly adjacent; leaving them on fractional pixels instead
    /// makes each boundary a half-covered column that both neighbours blend into, which draws a visible
    /// grid over any picture whose left edge is not on an integer.
    /// </para>
    /// </remarks>
    public void DrawTiled(
        SpriteBatch batch,
        in RectF destination,
        Bgra32 color,
        BlendMode blend = BlendMode.Opaque)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ThrowIfDisposed();

        SpriteTileLayout layout = Asset.TileLayout
            ?? throw new InvalidOperationException($"Sprite '{Asset.Name}' has no tile grid.");

        int tileWidth = Asset.DefaultWidth;
        int tileHeight = Asset.DefaultHeight;
        float scaleX = destination.Width / (layout.Columns * (float)tileWidth);
        float scaleY = destination.Height / (layout.Rows * (float)tileHeight);

        for (int row = 0; row < layout.Rows; row++)
        {
            for (int column = 0; column < layout.Columns; column++)
            {
                int frameIndex = layout.GetFrameIndex(column, row);
                if (frameIndex < 0 || frameIndex >= Asset.Frames.Count)
                {
                    continue;
                }

                SpriteFrame frame = Asset.Frames[frameIndex];

                float left = MathF.Round(destination.X + (column * tileWidth * scaleX));
                float top = MathF.Round(destination.Y + (row * tileHeight * scaleY));
                float right = MathF.Round(destination.X + (((column * tileWidth) + frame.Width) * scaleX));
                float bottom = MathF.Round(destination.Y + (((row * tileHeight) + frame.Height) * scaleY));

                batch.Draw(
                    _textures[frame.TextureIndex],
                    RectF.FromEdges(left, top, right, bottom),
                    SourceRect(frame),
                    color,
                    blend);
            }
        }
    }

    /// <summary>Queues one frame, applying its placement offset and a scale.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="frame">The frame to draw.</param>
    /// <param name="x">The cursor's horizontal position in pixels.</param>
    /// <param name="y">The cursor's vertical position in pixels.</param>
    /// <param name="scaleX">The horizontal scale.</param>
    /// <param name="scaleY">The vertical scale.</param>
    /// <param name="color">The colour modulated with the texture.</param>
    /// <param name="blend">How the frame combines with what is behind it.</param>
    public void DrawFrame(
        SpriteBatch batch,
        in SpriteFrame frame,
        float x,
        float y,
        float scaleX,
        float scaleY,
        Bgra32 color,
        BlendMode blend = BlendMode.AlphaBlend)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ThrowIfDisposed();

        // A zero-area frame is the format's "draws nothing" cell — the trailing defaults record uses
        // it as the missing glyph. Skipping keeps a degenerate quad out of the batch.
        if (frame.IsEmpty || frame.U0 == frame.U1 || frame.V0 == frame.V1)
        {
            return;
        }

        RectF destination = new(
            x + (frame.OffsetX * scaleX),
            y + (frame.OffsetY * scaleY),
            frame.Width * scaleX,
            frame.Height * scaleY);

        batch.Draw(_textures[frame.TextureIndex], destination, SourceRect(frame), color, blend);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (TextureHandle handle in _textures)
        {
            _device.DestroyTexture(handle);
        }
    }

    private static RectF SourceRect(in SpriteFrame frame) =>
        RectF.FromEdges(frame.U0, frame.V0, frame.U1, frame.V1);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
