using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Graphics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Menus;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>
/// Draws a menu screen from the task tree the menu script built.
/// </summary>
/// <remarks>
/// <para>
/// Everything drawn here comes from the data: the background is the screen's own <c>Background</c>
/// parameter, the frames are the <c>MenuFrame</c> children with their declared positions and sizes.
/// Nothing is hard-coded, and where a piece of the original's presentation is not implemented yet it is
/// simply not drawn rather than approximated.
/// </para>
/// <para>
/// Still missing, and tracked in <c>TODO.md</c> M5b: entry text needs the localised string table
/// (<c>MenuText.Text Resource</c> is a key, not a label), and selection, navigation and the
/// <c>MenuFrame</c> line-up modes need the types' own update methods reverse-engineered.
/// </para>
/// </remarks>
public sealed class MenuRenderer : IDisposable
{
    private readonly Dictionary<string, SpriteResource> _sprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly IGraphicsDevice _device;
    private readonly IresArchive _archive;

    /// <summary>Initializes a new instance of the <see cref="MenuRenderer"/> class.</summary>
    /// <param name="device">The device to upload art to.</param>
    /// <param name="fileSystem">The virtual file system rooted at the installation.</param>
    /// <param name="resourcePath">The archive named by the menu manager's <c>Resource path</c> parameter.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public MenuRenderer(IGraphicsDevice device, VirtualFileSystem fileSystem, string resourcePath)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);

        _device = device;
        _archive = IresArchive.Read(fileSystem.ReadAllBytes(resourcePath));
    }

    /// <summary>The resolution the original's menus are authored against.</summary>
    public const int AuthoredWidth = 640;

    /// <summary>The vertical resolution the original's menus are authored against.</summary>
    public const int AuthoredHeight = 480;

    /// <summary>Draws a screen.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="screen">The screen to draw.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public void Draw(SpriteBatch batch, MenuScreenTask screen)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(screen);

        batch.Begin();

        if (TryGetSprite(screen.Background) is { } background && background.Asset.TileLayout is not null)
        {
            background.DrawTiled(
                batch,
                new RectF(0, 0, AuthoredWidth, AuthoredHeight),
                Bgra32.White,
                BlendMode.Opaque);
        }

        foreach (MenuFrameTask frame in screen.Children.OfType<MenuFrameTask>())
        {
            DrawFrame(batch, frame);
        }

        batch.End();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (SpriteResource sprite in _sprites.Values)
        {
            sprite.Dispose();
        }

        _sprites.Clear();
    }

    private void DrawFrame(SpriteBatch batch, MenuFrameTask frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            return;
        }

        if (TryGetSprite(frame.SpriteResource) is { } sprite && sprite.Asset.TileLayout is not null)
        {
            sprite.DrawTiled(
                batch,
                new RectF(frame.X, frame.Y, frame.Width, frame.Height),
                Bgra32.White,
                BlendMode.AlphaBlend);
        }
    }

    /// <summary>Loads a sprite named by a task parameter, caching it.</summary>
    /// <remarks>
    /// Parameters name a bare file — "mainmenu.pic", "win1" — so the archive is searched by suffix.
    /// A name that resolves to nothing is not an error: the original tolerates an unset parameter and
    /// simply draws nothing.
    /// </remarks>
    private SpriteResource? TryGetSprite(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (_sprites.TryGetValue(name, out SpriteResource? cached))
        {
            return cached;
        }

        IresEntry? entry = _archive.Entries.FirstOrDefault(
            e => e.Kind == IresEntryKind.Body
                && Path.GetFileNameWithoutExtension(e.Name).Equals(
                    Path.GetFileNameWithoutExtension(name),
                    StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            _sprites[name] = null!;
            return null;
        }

        SpriteAsset asset = SpriteLoader.Load(entry.Data, entry.Name);
        SpriteResource resource = SpriteResource.Upload(_device, asset, TextureFilter.Point);
        _sprites[name] = resource;
        return resource;
    }
}
