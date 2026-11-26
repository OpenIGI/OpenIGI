using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Sprites;
using OpenIGI.Formats.Textures;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <summary>
/// The titles' pictures as one-colour silhouettes: each texel's colour made white and its coverage kept, so a picture is drawn
/// in whatever colour it is given -- a kill feed's weapons all one clean ink, whatever the title painted them.
/// </summary>
/// <remarks>
/// A copy is made the first time a picture is drawn, mipmapped and sampled smoothly, since the feed draws a weapon at a fraction
/// of its art's size; it is kept until the display is let go.
/// </remarks>
public sealed class HudSilhouettes : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly Dictionary<SpriteResource, TextureHandle[]?> _copies = new(ReferenceEqualityComparer.Instance);

    /// <summary>Initializes a new instance of the <see cref="HudSilhouettes"/> class.</summary>
    /// <param name="device">The device the copies are uploaded to.</param>
    public HudSilhouettes(IGraphicsDevice device) => _device = device ?? throw new ArgumentNullException(nameof(device));

    /// <summary>Draws a picture's first frame as a silhouette, its top left at a place.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="source">The title's picture.</param>
    /// <param name="x">Its left edge, in pixels.</param>
    /// <param name="y">Its top edge, in pixels.</param>
    /// <param name="scale">How many pixels each of its own is drawn as.</param>
    /// <param name="colour">The colour it is drawn in.</param>
    /// <param name="mirror">Whether it is turned left to right.</param>
    /// <returns>Whether it was drawn; <see langword="false"/> when no copy could be made.</returns>
    public bool Draw(SpriteBatch batch, SpriteResource source, float x, float y, float scale, Bgra32 colour, bool mirror)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(source);
        if (source.Asset.Frames.Count == 0 || Get(source) is not { } textures)
        {
            return false;
        }

        SpriteFrame frame = source.Asset.Frames[0];
        if (frame.IsEmpty || frame.U0 == frame.U1 || frame.V0 == frame.V1 || frame.TextureIndex >= textures.Length)
        {
            return false;
        }

        RectF destination = new(x, y, frame.Width * scale, frame.Height * scale);
        RectF from = mirror ? RectF.FromEdges(frame.U1, frame.V0, frame.U0, frame.V1) : RectF.FromEdges(frame.U0, frame.V0, frame.U1, frame.V1);
        batch.Draw(textures[frame.TextureIndex], destination, from, colour);
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (TextureHandle[]? copy in _copies.Values)
        {
            foreach (TextureHandle handle in copy ?? [])
            {
                _device.DestroyTexture(handle);
            }
        }

        _copies.Clear();
    }

    // The silhouette copies of a picture's textures, made the first time it is asked for; null when they could not be made.
    private TextureHandle[]? Get(SpriteResource source)
    {
        if (_copies.TryGetValue(source, out TextureHandle[]? copy))
        {
            return copy;
        }

        List<TextureHandle> made = [];
        try
        {
            foreach (TextureImage image in source.Asset.Textures)
            {
                ReadOnlySpan<Bgra32> pixels = image.GetLevel(0);
                Bgra32[] white = new Bgra32[pixels.Length];
                for (int i = 0; i < pixels.Length; i++)
                {
                    white[i] = Bgra32.FromRgba(255, 255, 255, pixels[i].A);
                }

                TextureImage silhouette = TextureImage.FromPixels(image.Width, image.Height, white).WithMipChain();
                made.Add(_device.CreateTexture(
                    new TextureDescription(silhouette.Width, silhouette.Height, silhouette.MipLevels, TextureFilter.Linear, TextureAddressMode.Clamp),
                    silhouette.Pixels));
            }

            copy = [.. made];
        }
        catch (GraphicsDeviceException)
        {
            foreach (TextureHandle handle in made)
            {
                _device.DestroyTexture(handle);
            }

            copy = null;
        }

        _copies[source] = copy;
        return copy;
    }
}
