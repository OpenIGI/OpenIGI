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
/// Everything here comes from the data: the background is the screen's own <c>Background</c>
/// parameter, entry labels are resolved through the language archive, positions come from running the
/// original's line-up over the task tree, and colours from the table its render indexes.
/// </para>
/// <para>
/// Drawing is a measure–arrange–draw pass, because the line-up needs every item's size before it can
/// place anything: each <c>MenuText</c> resolves its label and measures it in its own font, the
/// screen arranges its subtree, and only then is anything drawn. That is the order the original uses
/// too — its measure slot writes the item's width and height, and the line-up reads them.
/// </para>
/// </remarks>
public sealed class MenuRenderer : IDisposable
{
    private readonly Dictionary<string, SpriteResource?> _sprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapTextRenderer?> _fonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly IGraphicsDevice _device;
    private readonly IresArchive _archive;
    private readonly MenuStringTable _strings;

    /// <summary>Initializes a new instance of the <see cref="MenuRenderer"/> class.</summary>
    /// <param name="device">The device to upload art to.</param>
    /// <param name="fileSystem">The virtual file system rooted at the installation.</param>
    /// <param name="resourcePath">The archive named by the menu manager's <c>Resource path</c> parameter.</param>
    /// <param name="strings">The language table entry labels resolve through.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public MenuRenderer(
        IGraphicsDevice device,
        VirtualFileSystem fileSystem,
        string resourcePath,
        MenuStringTable strings)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);

        _device = device;
        _strings = strings;
        _archive = IresArchive.Read(fileSystem.ReadAllBytes(resourcePath));
    }

    /// <summary>Draws a screen.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="screen">The screen to draw.</param>
    /// <param name="screenWidth">The back-buffer width in pixels.</param>
    /// <param name="screenHeight">The back-buffer height in pixels.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public void Draw(SpriteBatch batch, MenuScreenTask screen, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(screen);

        batch.Begin();

        if (TryGetSprite(screen.Background) is { Asset.TileLayout: not null } background)
        {
            background.DrawTiled(
                batch,
                Centred(0, 0, MenuLayout.AuthoredWidth, MenuLayout.AuthoredHeight, screenWidth, screenHeight),
                Bgra32.White,
                BlendMode.Opaque);
        }

        DrawSubtree(batch, screen, screenWidth, screenHeight);

        batch.End();
    }

    /// <summary>The cursor sprite the manager loads out of its own archive.</summary>
    /// <remarks><c>MenuManager_LoadScreen</c> (<c>0x417CF0</c>) names it literally.</remarks>
    public const string PointerSprite = "pointer.spr";

    /// <summary>Draws the mouse cursor.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="x">The horizontal position in authored space.</param>
    /// <param name="y">The vertical position in authored space.</param>
    /// <param name="screenWidth">The back-buffer width in pixels.</param>
    /// <param name="screenHeight">The back-buffer height in pixels.</param>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The sprite's hot spot is its top-left corner: the frame carries its own offsets and
    /// <see cref="SpriteResource.DrawFrame"/> applies them, so the position is passed through
    /// unadjusted rather than being nudged to centre the tip.
    /// </remarks>
    public void DrawPointer(SpriteBatch batch, int x, int y, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (TryGetSprite(PointerSprite) is not { } pointer || pointer.Asset.Frames.Count == 0)
        {
            return;
        }

        (int screenX, int screenY) = MenuLayout.ToScreen(x, y, screenWidth, screenHeight);

        batch.Begin();
        pointer.DrawFrame(batch, pointer.Asset.Frames[0], screenX, screenY, 1f, 1f, Bgra32.White);
        batch.End();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (SpriteResource? sprite in _sprites.Values)
        {
            sprite?.Dispose();
        }

        _sprites.Clear();
        _fonts.Clear();
    }

    /// <summary>Resolves and measures a screen's labels so the line-up has sizes to work with.</summary>
    /// <param name="screen">The screen about to be shown.</param>
    /// <exception cref="ArgumentNullException"><paramref name="screen"/> is <see langword="null"/>.</exception>
    public void Prepare(MenuScreenTask screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        MeasureText(screen);
    }

    /// <summary>Resolves and measures every label, so the line-up has sizes to work with.</summary>
    private void MeasureText(MenuWindowTask window)
    {
        foreach (MenuWindowTask child in window.Children.OfType<MenuWindowTask>())
        {
            if (child is MenuTextTask text)
            {
                (string label, BitmapTextRenderer? font) = Resolve(text.TextResource, text.FontResource);
                if (font is not null && label.Length > 0)
                {
                    (float width, float height) = font.Measure(label);
                    text.LayoutWidth = (int)width;
                    text.LayoutHeight = (int)height;
                }
            }

            MeasureText(child);
        }
    }

    private void DrawSubtree(SpriteBatch batch, MenuWindowTask window, int screenWidth, int screenHeight)
    {
        foreach (MenuWindowTask child in window.Children.OfType<MenuWindowTask>())
        {
            switch (child)
            {
                case MenuTextTask text:
                    DrawText(batch, text, screenWidth, screenHeight);
                    break;
                case MenuFrameTask frame:
                    DrawFrameBorder(batch, frame, screenWidth, screenHeight);
                    break;
                default:
                    break;
            }

            DrawSubtree(batch, child, screenWidth, screenHeight);
        }
    }

    private void DrawText(SpriteBatch batch, MenuTextTask text, int screenWidth, int screenHeight)
    {
        (string label, BitmapTextRenderer? font) = Resolve(text.TextResource, text.FontResource);
        if (font is null || label.Length == 0)
        {
            return;
        }

        (int x, int y) = MenuLayout.ToScreen(text.LayoutX, text.LayoutY, screenWidth, screenHeight);

        // MenuText_Render tests the selection flag at +77, which the manager owns and which the mouse
        // and the keyboard both move — not the pointer-over state.
        Bgra32 colour = MenuLayout.ToPixel(MenuLayout.GetColour(text.ColourIndex, text.IsHighlighted));

        font.Draw(batch, label, x, y, colour);
    }

    /// <summary>Draws a frame's eight-piece border.</summary>
    /// <remarks>
    /// The placement arithmetic lives in <see cref="MenuBorderLayout"/> so it can be tested without a
    /// device; this only resolves the sprites and draws what it is told, in the neutral colour the
    /// original passes.
    /// </remarks>
    private void DrawFrameBorder(SpriteBatch batch, MenuFrameTask frame, int screenWidth, int screenHeight)
    {
        if (frame.SpriteResource.Length == 0 || frame.LayoutWidth <= 0 || frame.LayoutHeight <= 0)
        {
            return;
        }

        SpriteResource?[] pieces = new SpriteResource?[MenuLayout.FramePieceCount];
        (int Width, int Height)[] sizes = new (int, int)[MenuLayout.FramePieceCount];
        bool any = false;

        for (int i = 0; i < pieces.Length; i++)
        {
            pieces[i] = TryGetSprite(MenuLayout.GetFramePieceName(frame.SpriteResource, i + 1));
            sizes[i] = (Width(pieces[i]), Height(pieces[i]));
            any |= pieces[i] is not null;
        }

        if (!any)
        {
            return;
        }

        (int x, int y) = MenuLayout.ToScreen(frame.LayoutX, frame.LayoutY, screenWidth, screenHeight);

        foreach (MenuBorderPlacement placement in
            MenuBorderLayout.Compute(frame.LayoutWidth, frame.LayoutHeight, sizes))
        {
            Draw(batch, pieces[(int)placement.Piece], x + placement.X, y + placement.Y);
        }
    }

    private static int Width(SpriteResource? sprite) =>
        sprite is null
            ? 0
            : Math.Max(sprite.Asset.DefaultWidth, sprite.Asset.Frames.Count > 0 ? sprite.Asset.Frames[0].Width : 0);

    private static int Height(SpriteResource? sprite) =>
        sprite is null
            ? 0
            : Math.Max(sprite.Asset.DefaultHeight, sprite.Asset.Frames.Count > 0 ? sprite.Asset.Frames[0].Height : 0);

    /// <summary>Draws one border piece at its native size, in the neutral colour the original passes.</summary>
    private static void Draw(SpriteBatch batch, SpriteResource? sprite, int x, int y)
    {
        if (sprite is not null && sprite.Asset.Frames.Count > 0)
        {
            sprite.Draw(batch, 0, x, y, MenuLayout.ToPixel((MenuLayout.FullIntensity, MenuLayout.FullIntensity, MenuLayout.FullIntensity)));
        }
    }

    /// <summary>Maps an authored rectangle onto the screen, centred rather than scaled.</summary>
    private static RectF Centred(int x, int y, int width, int height, int screenWidth, int screenHeight)
    {
        (int screenX, int screenY) = MenuLayout.ToScreen(x, y, screenWidth, screenHeight);
        return new RectF(screenX, screenY, width, height);
    }

    private (string Label, BitmapTextRenderer? Font) Resolve(string textResource, string fontResource) =>
        (_strings.Get(textResource), TryGetFont(fontResource));

    private BitmapTextRenderer? TryGetFont(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (_fonts.TryGetValue(name, out BitmapTextRenderer? cached))
        {
            return cached;
        }

        SpriteResource? sprite = TryGetSprite(name);
        BitmapTextRenderer? font = sprite is { IsFont: true } ? new BitmapTextRenderer(sprite) : null;
        _fonts[name] = font;
        return font;
    }

    /// <summary>Loads a sprite named by a task parameter, caching hits and misses alike.</summary>
    /// <remarks>
    /// Parameters name a bare file, so the archive is searched by stem. A name that resolves to
    /// nothing is not an error: the original tolerates an unset parameter and draws nothing.
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

        SpriteResource? resource = null;
        if (entry is not null)
        {
            SpriteAsset asset = SpriteLoader.Load(entry.Data, entry.Name);
            resource = SpriteResource.Upload(_device, asset, TextureFilter.Point);
        }

        _sprites[name] = resource;
        return resource;
    }
}
