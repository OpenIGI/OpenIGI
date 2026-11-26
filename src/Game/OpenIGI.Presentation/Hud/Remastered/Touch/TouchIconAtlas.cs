using System.Reflection;
using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Rhi;

namespace OpenIGI.Presentation.Hud.Remastered.Touch;

/// <summary>
/// The touch controls' icons: one texture of square tiles, each a button's picture as coverage alone, drawn a quad apiece in
/// whatever colour the button is in.
/// </summary>
/// <remarks>
/// <para>
/// The pictures are SVGs (<c>assets/touch-icons</c>) baked into the tiles ahead of time by <c>tools/OpenIGI.Tools.IconAtlas</c>
/// and embedded in this assembly, so the game reads no SVG and tessellates no path: a button's picture costs one textured
/// quad, on a phone as on a desk.
/// </para>
/// <para>
/// The atlas itself is the interface's (<see cref="IconAtlas"/>): its tiles kept at smaller sizes too, a picture drawn from the
/// one nearest the size it is drawn at, so a small corner button never shimmers as it moves.
/// </para>
/// </remarks>
public sealed class TouchIconAtlas : IDisposable
{
    private const string Resource = "OpenIGI.Presentation.touch-icons.bin";

    private readonly RhiImageFactory _images;
    private IconAtlas? _atlas;
    private bool _loaded;

    /// <summary>Initializes a new instance of the <see cref="TouchIconAtlas"/> class.</summary>
    /// <param name="device">The device the tiles are uploaded to, once they are first drawn.</param>
    public TouchIconAtlas(IGraphicsDevice device)
    {
        _images = new RhiImageFactory(device ?? throw new ArgumentNullException(nameof(device)));
    }

    /// <summary>Draws a button's picture, where the atlas has one.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="button">The button.</param>
    /// <param name="centre">The picture's middle, in pixels.</param>
    /// <param name="side">The picture's side, in pixels.</param>
    /// <param name="ink">Its colour.</param>
    /// <returns>Whether it was drawn; a button with no picture is left to the caller.</returns>
    public bool TryDraw(UiPainter painter, TouchButton button, UiVec2 centre, float side, Bgra32 ink)
    {
        ArgumentNullException.ThrowIfNull(painter);
        Load();
        return _atlas is { } atlas && atlas.TryDraw(painter, button.ToString(), centre, side, ink);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _atlas?.Dispose();
        _atlas = null;
        _loaded = false;
    }

    // Reads the baked tiles once and uploads them.
    private void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource);
        _atlas = stream is null ? null : IconAtlas.Read(stream, _images);
    }
}
