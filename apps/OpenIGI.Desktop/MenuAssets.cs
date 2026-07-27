using OpenIGI.Engine.Graphics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>
/// The menu-system art the shell needs on screen: the background picture and the three fonts.
/// </summary>
/// <remarks>
/// Everything comes out of <c>menusystem.res</c>, which is the archive the main-menu script names in
/// its <c>MenuManager</c> declaration (<c>LOCAL:menusystem/menusystem.res</c>). Loading it here rather
/// than through the resource registry keeps the shell honest about what it has wired so far: the
/// script-driven <c>MenuScreen</c> tree is still to come.
/// </remarks>
public sealed class MenuAssets : IDisposable
{
    private readonly List<SpriteResource> _owned = [];

    private MenuAssets(
        SpriteResource background,
        SpriteResource logo,
        SpriteResource titleFont,
        SpriteResource bodyFont)
    {
        Background = background;
        Logo = logo;
        TitleFont = titleFont;
        BodyFont = bodyFont;
        _owned.AddRange([background, logo, titleFont, bodyFont]);
    }

    /// <summary>Gets the main-menu background picture.</summary>
    public SpriteResource Background { get; }

    /// <summary>Gets the game logo.</summary>
    public SpriteResource Logo { get; }

    /// <summary>Gets the logo's assembled width in pixels.</summary>
    public int LogoWidth =>
        (Logo.Asset.TileLayout?.Columns ?? 1) * Logo.Asset.DefaultWidth;

    /// <summary>Gets the logo's assembled height in pixels.</summary>
    public int LogoHeight =>
        (Logo.Asset.TileLayout?.Rows ?? 1) * Logo.Asset.DefaultHeight;

    /// <summary>Gets the large font the menu headings use.</summary>
    public SpriteResource TitleFont { get; }

    /// <summary>Gets the small font the menu entries use.</summary>
    public SpriteResource BodyFont { get; }

    /// <summary>Loads the menu archive and uploads what the shell draws.</summary>
    /// <param name="fileSystem">The virtual file system rooted at the installation.</param>
    /// <param name="device">The device to upload textures to.</param>
    /// <returns>The loaded assets.</returns>
    /// <exception cref="FileNotFoundException">The menu archive is missing.</exception>
    /// <exception cref="InvalidOperationException">The archive does not hold an entry the shell needs.</exception>
    public static MenuAssets Load(VirtualFileSystem fileSystem, IGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(device);

        IresArchive archive = IresArchive.Read(fileSystem.ReadAllBytes("LOCAL:menusystem/menusystem.res"));

        // Tiled pictures are sampled point-filtered: linear filtering bleeds across tile edges and
        // draws a visible grid over the picture.
        SpriteResource background = Upload(archive, device, "mainmenu.pic", TextureFilter.Point);
        SpriteResource logo = Upload(archive, device, "logo.pic", TextureFilter.Point);
        SpriteResource titleFont = Upload(archive, device, "font3.fnt", TextureFilter.Point);
        SpriteResource bodyFont = Upload(archive, device, "font1.fnt", TextureFilter.Point);

        return new MenuAssets(background, logo, titleFont, bodyFont);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (SpriteResource resource in _owned)
        {
            resource.Dispose();
        }

        _owned.Clear();
    }

    private static SpriteResource Upload(IresArchive archive, IGraphicsDevice device, string suffix, TextureFilter filter)
    {
        IresEntry entry = archive.Entries.FirstOrDefault(
                e => e.Kind == IresEntryKind.Body && e.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"menusystem.res has no entry ending in '{suffix}'.");

        SpriteAsset asset = SpriteLoader.Load(entry.Data, entry.Name);
        return SpriteResource.Upload(device, asset, filter);
    }
}
