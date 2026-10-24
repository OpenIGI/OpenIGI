using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <summary>
/// The titles' pictures as the remastered display draws them: each a copy of the title's own, sampled smoothly, so a weapon
/// drawn larger than its art -- on the weapon wheel, say -- is smooth rather than blocky.
/// </summary>
/// <remarks>
/// The titles upload their art for nearest-texel sampling (<see cref="TextureFilter.Point"/>), right for the original displays,
/// which draw it pixel for pixel; those keep theirs. A copy is made the first time a picture is drawn here, and kept until the
/// display is let go.
/// </remarks>
public sealed class HudSmoothSprites : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly Dictionary<SpriteResource, SpriteResource?> _copies = new(ReferenceEqualityComparer.Instance);

    /// <summary>Initializes a new instance of the <see cref="HudSmoothSprites"/> class.</summary>
    /// <param name="device">The device the copies are uploaded to.</param>
    public HudSmoothSprites(IGraphicsDevice device) => _device = device ?? throw new ArgumentNullException(nameof(device));

    /// <summary>The smooth copy of a title's picture, made the first time it is asked for.</summary>
    /// <param name="source">The title's picture.</param>
    /// <returns>The copy, or <see langword="null"/> when it could not be made and the title's own is to be drawn.</returns>
    public SpriteResource? Get(SpriteResource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!_copies.TryGetValue(source, out SpriteResource? copy))
        {
            try
            {
                copy = SpriteResource.Upload(_device, source.Asset, TextureFilter.Linear);
            }
            catch (GraphicsDeviceException)
            {
                copy = null;
            }

            _copies[source] = copy;
        }

        return copy;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (SpriteResource? copy in _copies.Values)
        {
            copy?.Dispose();
        }

        _copies.Clear();
    }
}
