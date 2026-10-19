using OpenIGI.Core.Graphics;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Rendering;
using OpenIGI.Game.World;
using OpenIGI.Game.Menus;

namespace OpenIGI.Presentation.Igi1.Menus;

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
    private int _scale = UiScale.Native;

    /// <summary>Gets or sets the magnification asked for; <see cref="UiScale.Automatic"/> derives one.</summary>
    public int Scale { get; set; } = UiScale.Automatic;

    /// <summary>Gets the magnification the last draw used.</summary>
    public int EffectiveScale => _scale;

    private readonly IGraphicsDevice _device;
    private readonly List<IresArchive> _archives = [];
    private readonly MenuStringTable _strings;

    /// <summary>Initializes a new instance of the <see cref="MenuRenderer"/> class.</summary>
    /// <param name="device">The device to upload art to.</param>
    /// <param name="fileSystem">The virtual file system rooted at the installation.</param>
    /// <param name="resourcePath">The archive named by the menu manager's <c>Resource path</c> parameter.</param>
    /// <param name="strings">The language table entry labels resolve through.</param>
    /// <param name="extraArchives">
    /// Further archives to search when the main one does not hold a sprite. The mission briefing images
    /// live in <c>menusystem/missionsprites.res</c> rather than in the menu's own archive, so a picture
    /// box cannot find them without this.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public MenuRenderer(
        IGraphicsDevice device,
        VirtualFileSystem fileSystem,
        string resourcePath,
        MenuStringTable strings,
        IEnumerable<string>? extraArchives = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);

        _device = device;
        _strings = strings;
        _archives.Add(IresArchive.Read(fileSystem.ReadAllBytes(resourcePath)));

        foreach (string extra in extraArchives ?? [])
        {
            if (fileSystem.Exists(extra))
            {
                _archives.Add(IresArchive.Read(fileSystem.ReadAllBytes(extra)));
            }
        }
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

        // The whole screen is authored at 640x480 and the original centres it without scaling. On a
        // modern display that is a small island in the middle, so the offsets AND the art are both
        // magnified by the same whole number -- see UiScale. Scale.Set from the host, or derived.
        _scale = UiScale.Resolve(Scale, screenWidth, screenHeight);

        batch.Begin();

        if (TryGetSprite(screen.Background) is { Asset.TileLayout: not null } background)
        {
            background.DrawTiled(
                batch,
                Centred(0, 0, MenuLayout.AuthoredWidth, MenuLayout.AuthoredHeight, screenWidth, screenHeight),
                Bgra32.White,
                BlendMode.Opaque);
        }

        DrawBackgroundFx(batch, screen.Manager?.BackgroundFx, screenWidth, screenHeight);
        DrawLogo(batch, screenWidth, screenHeight);
        DrawScreenPanel(batch, screen, screenWidth, screenHeight);
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

        (int screenX, int screenY) = MenuLayout.ToScreen(x, y, screenWidth, screenHeight, _scale);

        batch.Begin();
        pointer.DrawFrame(batch, pointer.Asset.Frames[0], screenX, screenY, _scale, _scale, Bgra32.White);
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
            switch (child)
            {
                case MenuTextTask text:
                {
                    (string label, BitmapTextRenderer? font) = Resolve(text.TextResource, text.FontResource);
                    if (font is not null && label.Length > 0)
                    {
                        (float width, float height) = font.Measure(label);
                        text.LayoutWidth = (int)width;
                        text.LayoutHeight = (int)height;
                    }

                    break;
                }

                // A slider's size comes out of its three sprites, so they have to be resolved before
                // it can be measured — which is the order SlideBar_Prepare has too.
                case SlideBarTask slider:
                    MeasureSlideBar(slider);
                    break;

                case ListBoxTask list:
                    (list.LeftButton.LayoutWidth, list.LeftButton.LayoutHeight) =
                        SpriteSize(list.LeftButton.SpriteResource);
                    (list.RightButton.LayoutWidth, list.RightButton.LayoutHeight) =
                        SpriteSize(list.RightButton.SpriteResource);
                    list.MeasureFromParts();
                    break;

                default:
                    break;
            }

            MeasureText(child);
        }
    }

    /// <summary>Sizes a slider's arrows and carret from their sprites, then the slider from those.</summary>
    private void MeasureSlideBar(SlideBarTask slider)
    {
        (slider.LeftButton.LayoutWidth, slider.LeftButton.LayoutHeight) = SpriteSize(slider.LeftButton.SpriteResource);
        (slider.RightButton.LayoutWidth, slider.RightButton.LayoutHeight) = SpriteSize(slider.RightButton.SpriteResource);
        (slider.CarretWidth, slider.CarretHeight) = SpriteSize(slider.CarretSprite);

        slider.MeasureFromParts();
    }

    private (int Width, int Height) SpriteSize(string name)
    {
        if (name.Length == 0 || TryGetSprite(name) is not { } sprite || sprite.Asset.Frames.Count == 0)
        {
            return (0, 0);
        }

        SpriteFrame frame = sprite.Asset.Frames[0];
        return ((int)frame.Width, (int)frame.Height);
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
                case SlideBarTask slider:
                    DrawSlideBar(batch, slider, screenWidth, screenHeight);
                    break;
                case ToggleBoxTask toggle:
                    DrawToggleBox(batch, toggle, screenWidth, screenHeight);
                    break;
                case ListBoxTask list:
                    DrawListBox(batch, list, screenWidth, screenHeight);
                    break;
                case InputBoxTask input:
                    DrawWindowBox(batch, input, screenWidth, screenHeight);
                    DrawInBox(batch, input, string.Empty, [input.VisibleText], 0, screenWidth, screenHeight);
                    break;
                case ScrollListBoxTask scroll:
                    DrawWindowBox(batch, scroll, screenWidth, screenHeight);
                    DrawScrollList(batch, scroll, screenWidth, screenHeight);
                    break;
                case AsciiBoxTask ascii:
                    DrawWindowBox(batch, ascii, screenWidth, screenHeight);
                    DrawInBox(batch, ascii, ascii.FontResource, [.. ascii.Lines], ascii.ScrollOffset, screenWidth, screenHeight);
                    break;
                case TextBoxTask text2:
                    DrawWindowBox(batch, text2, screenWidth, screenHeight);
                    DrawInBox(batch, text2, text2.FontResource, [.. text2.Lines], 0, screenWidth, screenHeight);
                    break;
                case TypeWriterBoxTask typer:
                    DrawWindowBox(batch, typer, screenWidth, screenHeight);
                    DrawInBox(batch, typer, string.Empty, [typer.VisibleText], 0, screenWidth, screenHeight);
                    break;
                case PictureBoxTask picture:
                    DrawPictureBox(batch, picture, screenWidth, screenHeight);
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

        (int x, int y) = MenuLayout.ToScreen(text.LayoutX, text.LayoutY, screenWidth, screenHeight, _scale);

        // MenuText_Render tests the selection flag at +77, which the manager owns and which the mouse
        // and the keyboard both move — not the pointer-over state.
        Bgra32 colour = MenuLayout.ToPixel(MenuLayout.GetColour(text.ColourIndex, text.IsHighlighted));

        font.Draw(batch, label, x, y, colour, _scale, _scale);
    }

    /// <summary>Draws a slider: its box, its two arrows and its carret.</summary>
    /// <remarks>
    /// <c>SlideBar_Render</c> (<c>0x41FCE0</c>). The placement is <see cref="SlideBarTask.PlaceParts"/>
    /// so that the update hit-tests the same rectangle this draws into; only the sprite lookups and the
    /// screen mapping are here.
    /// </remarks>
    private void DrawSlideBar(SpriteBatch batch, SlideBarTask slider, int screenWidth, int screenHeight)
    {
        (_, _, int carretX, int carretY) = slider.PlaceParts();

        DrawWindowBox(batch, slider, screenWidth, screenHeight);
        DrawButton(batch, slider.LeftButton, screenWidth, screenHeight);
        DrawButton(batch, slider.RightButton, screenWidth, screenHeight);

        if (TryGetSprite(slider.CarretSprite) is { } carret && carret.Asset.Frames.Count > 0)
        {
            (int x, int y) = MenuLayout.ToScreen(carretX, carretY, screenWidth, screenHeight, _scale);
            carret.DrawFrame(batch, carret.Asset.Frames[0], x, y, _scale, _scale, Bgra32.White);
        }
    }

    /// <summary>Draws one of a slider's arrows.</summary>
    /// <remarks><c>MenuButton_Render</c> (<c>0x421600</c>) draws its sprite at its own position and nothing else.</remarks>
    private void DrawButton(SpriteBatch batch, MenuButtonTask button, int screenWidth, int screenHeight)
    {
        if (TryGetSprite(button.SpriteResource) is not { } sprite || sprite.Asset.Frames.Count == 0)
        {
            return;
        }

        (int x, int y) = MenuLayout.ToScreen(button.LayoutX, button.LayoutY, screenWidth, screenHeight, _scale);
        sprite.DrawFrame(batch, sprite.Asset.Frames[0], x, y, _scale, _scale, Bgra32.White);
    }

    /// <summary>Draws a switch: its box, its two words and a tint over whichever one is chosen.</summary>
    /// <remarks>
    /// <c>ToggleBox_Render</c> (<c>0x41E130</c>). The words sit at the quarter and three-quarter points
    /// of the control's width, each centred on its own measured width and vertically centred in the
    /// control. The tint goes over the chosen half <em>after</em> the text, so it colours the word.
    /// </remarks>
    private void DrawToggleBox(SpriteBatch batch, ToggleBoxTask toggle, int screenWidth, int screenHeight)
    {
        DrawWindowBox(batch, toggle, screenWidth, screenHeight);

        BitmapTextRenderer? font = TryGetFont(ToggleBoxTask.FontResource);
        if (font is null)
        {
            return;
        }

        string on = _strings.Get(ToggleBoxTask.OnTextResource);
        string off = _strings.Get(ToggleBoxTask.OffTextResource);

        // Integer division throughout, as the original's does — the halving of an odd width and an
        // odd text width both truncate.
        (float onWidth, float onHeight) = font.Measure(on);
        (float offWidth, _) = font.Measure(off);

        int textY = toggle.LayoutY + ((toggle.LayoutHeight - (int)onHeight) / 2);
        int onX = toggle.LayoutX + (toggle.LayoutWidth / 4) - ((int)onWidth / 2);
        int offX = toggle.LayoutX + (3 * toggle.LayoutWidth / 4) - ((int)offWidth / 2);

        // The chosen half is tinted with the same green the selection uses, lightened when the whole
        // control carries the selection — GetColour's (c * 0.5) + 64, arrived at independently here.
        //
        // DIVERGENCE: the original submits this AFTER the two words, which through a painter's-order
        // batch paints over the very word it is meant to mark. It goes behind them here. This is the
        // third place the original's submission order only makes sense if its 2D calls are not
        // immediate; see the re-note.
        Bgra32 tint = MenuLayout.ToPixel(MenuLayout.GetColour(MenuLayout.GreenIndex, toggle.IsHighlighted));
        int halfX = toggle.IsOn ? toggle.LayoutX : toggle.OffHalfX;
        int halfWidth = toggle.IsOn ? toggle.OnHalfWidth : toggle.OffHalfWidth;

        batch.FillRect(
            Centred(halfX, toggle.LayoutY, halfWidth, toggle.LayoutHeight, screenWidth, screenHeight),
            tint);

        Draw(batch, font, on, onX, textY, screenWidth, screenHeight);
        Draw(batch, font, off, offX, textY, screenWidth, screenHeight);
    }

    /// <summary>Draws a list box: its box, its two arrows and the current entry's label.</summary>
    /// <remarks>
    /// <c>ListBox_Render</c> (<c>0x41ED70</c>). The label is centred in the whole control rather than
    /// in the field between the arrows, and is dropped one pixel — both straight from the original.
    /// </remarks>
    private void DrawListBox(SpriteBatch batch, ListBoxTask list, int screenWidth, int screenHeight)
    {
        list.PlaceParts();

        DrawWindowBox(batch, list, screenWidth, screenHeight);
        DrawButton(batch, list.LeftButton, screenWidth, screenHeight);
        DrawButton(batch, list.RightButton, screenWidth, screenHeight);

        BitmapTextRenderer? font = TryGetFont(list.FontResource);
        if (font is null || list.Items.Count == 0)
        {
            return;
        }

        string label = list.Fit(list.ActiveLabel, candidate => (int)font.Measure(candidate).Width);
        (float width, float height) = font.Measure(label);

        int x = list.LayoutX + ((list.LayoutWidth - (int)width) / 2);
        int y = list.LayoutY + 1 + ((list.LayoutHeight - (int)height) / 2);

        Draw(batch, font, label, x, y, screenWidth, screenHeight);
    }

    /// <summary>Draws the turning green overlay.</summary>
    /// <remarks>
    /// <c>BackgroundFX_Render</c> (<c>0x419820</c>) draws the whole effect <b>twice</b>, at the
    /// current frame count and at one past it. Two faint passes a frame apart are what give the lines
    /// their doubled edge; one pass at double the alpha does not look the same.
    /// </remarks>
    private void DrawBackgroundFx(SpriteBatch batch, BackgroundFxTask? fx, int screenWidth, int screenHeight)
    {
        if (fx is null)
        {
            return;
        }

        DrawBackgroundFxPass(batch, fx.Time, screenWidth, screenHeight);
        DrawBackgroundFxPass(batch, fx.Time + 1f, screenWidth, screenHeight);
    }

    private void DrawBackgroundFxPass(SpriteBatch batch, float time, int screenWidth, int screenHeight)
    {
        Bgra32 lineColour = Bgra32.FromRgba(0, byte.MaxValue, 0, Alpha(BackgroundFxTask.LineAlpha));
        Bgra32 circleColour = Bgra32.FromRgba(0, byte.MaxValue, 0, Alpha(BackgroundFxTask.CircleAlpha));

        foreach (BackgroundFxLayer layer in BackgroundFxTask.Layers)
        {
            BackgroundFxTransform m = BackgroundFxTask.GetTransform(layer, time);

            if (layer.Grid is { } grid)
            {
                DrawFxGrid(batch, grid, m, lineColour, screenWidth, screenHeight);
            }

            foreach (BackgroundFxLine line in layer.Lines)
            {
                (float ax, float ay) = m.Apply(line.X0, line.Y0);
                (float bx, float by) = m.Apply(line.X1, line.Y1);
                DrawFxSegment(batch, ax, ay, bx, by, lineColour, screenWidth, screenHeight);
            }

            foreach (BackgroundFxCircle circle in layer.Circles)
            {
                DrawFxCircle(batch, circle, m, circleColour, screenWidth, screenHeight);
            }
        }
    }

    /// <summary>Draws a circle the way the original does: a closed loop of straight segments.</summary>
    /// <remarks>
    /// The count is <c>(int)(radius * pi / 10)</c> capped at forty, so a small circle is visibly a
    /// polygon and a large one is not. Reproduced rather than smoothed.
    /// </remarks>
    private void DrawFxCircle(
        SpriteBatch batch,
        BackgroundFxCircle circle,
        in BackgroundFxTransform m,
        Bgra32 colour,
        int screenWidth,
        int screenHeight)
    {
        (float cx, float cy) = m.Apply(circle.X, circle.Y);
        float radius = circle.Radius * m.Scale;

        int segments = Math.Min(BackgroundFxTask.MaxCircleSegments, (int)(radius * BackgroundFxTask.CircleSegmentFactor));
        if (segments <= 0)
        {
            return;
        }

        float step = MathF.Tau / segments;
        for (int i = 0; i < segments; i++)
        {
            float a = i * step;
            float b = (i + 1) * step;
            DrawFxSegment(
                batch,
                cx + (MathF.Cos(a) * radius),
                cy + (MathF.Sin(a) * radius),
                cx + (MathF.Cos(b) * radius),
                cy + (MathF.Sin(b) * radius),
                colour,
                screenWidth,
                screenHeight);
        }
    }

    private void DrawFxGrid(
        SpriteBatch batch,
        in BackgroundFxGrid grid,
        in BackgroundFxTransform m,
        Bgra32 colour,
        int screenWidth,
        int screenHeight)
    {
        // Centred on the origin, stepping five percent of the extent per line, count + 1 lines.
        float left = grid.OriginX - (grid.Width * 0.5f);
        float top = grid.OriginY - (grid.Height * 0.5f);

        for (int i = 0; i <= grid.Columns; i++)
        {
            float x = left + (grid.Width * BackgroundFxGrid.Step * i);
            (float ax, float ay) = m.Apply(x, top);
            (float bx, float by) = m.Apply(x, top + grid.Height);
            DrawFxSegment(batch, ax, ay, bx, by, colour, screenWidth, screenHeight);
        }

        for (int i = 0; i <= grid.Rows; i++)
        {
            float y = top + (grid.Height * BackgroundFxGrid.Step * i);
            (float ax, float ay) = m.Apply(left, y);
            (float bx, float by) = m.Apply(left + grid.Width, y);
            DrawFxSegment(batch, ax, ay, bx, by, colour, screenWidth, screenHeight);
        }
    }

    /// <summary>Draws one line of the overlay as a thin quad.</summary>
    /// <remarks>
    /// The original submits a two-vertex line primitive. A one-pixel quad along the same span is as
    /// close as this batch gets without a line pipeline, and at these alphas the difference does not
    /// show.
    /// </remarks>
    private void DrawFxSegment(
        SpriteBatch batch,
        float ax,
        float ay,
        float bx,
        float by,
        Bgra32 colour,
        int screenWidth,
        int screenHeight)
    {
        (int x0, int y0) = MenuLayout.ToScreen((int)ax, (int)ay, screenWidth, screenHeight, _scale);
        (int x1, int y1) = MenuLayout.ToScreen((int)bx, (int)by, screenWidth, screenHeight, _scale);

        float dx = x1 - x0;
        float dy = y1 - y0;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length < 1f)
        {
            return;
        }

        float nx = -dy / length * 0.5f;
        float ny = dx / length * 0.5f;

        batch.DrawQuad(
            TextureHandle.None,
            new Vertex2D(x0 + nx, y0 + ny, 0f, 0f, colour),
            new Vertex2D(x1 + nx, y1 + ny, 1f, 0f, colour),
            new Vertex2D(x1 - nx, y1 - ny, 1f, 1f, colour),
            new Vertex2D(x0 - nx, y0 - ny, 0f, 1f, colour));
    }

    /// <summary>Draws the green panel a screen frames its contents with.</summary>
    /// <remarks>
    /// <para>
    /// <c>MenuScreen_DrawPanel</c> (<c>0x421CD0</c>), and the reason the main menu looked bare. The
    /// rectangle is the screen's own <c>Frame x0/y0/x1/y1</c> parameters, each plus one, and it is
    /// three separate submissions:
    /// </para>
    /// <list type="number">
    /// <item><description>A four-corner quad filled green, alpha <c>0.2</c> at the top fading to
    /// <c>0.05</c> at the bottom — this is the tint over the wall.</description></item>
    /// <item><description>A closed five-vertex line loop in <c>(0, 0.75, 0)</c> — the border.</description></item>
    /// <item><description>A horizontal line twenty pixels below the top edge — the rule under the
    /// screen's title.</description></item>
    /// </list>
    /// <para>
    /// The whole panel is scaled horizontally out of the centre by the manager's fade alpha, so it
    /// opens outwards as a screen appears rather than cutting in.
    /// </para>
    /// </remarks>
    private void DrawScreenPanel(SpriteBatch batch, MenuScreenTask screen, int screenWidth, int screenHeight)
    {
        MenuScreenPanel panel = MenuScreenPanel.For(screen, screen.Manager?.Navigator.Alpha ?? MenuNavigator.FullAlpha);
        if (panel.IsEmpty)
        {
            return;
        }

        RectF area = Centred(panel.X0, panel.Y0, panel.X1 - panel.X0, panel.Y1 - panel.Y0, screenWidth, screenHeight);

        Bgra32 top = Bgra32.FromRgba(0, byte.MaxValue, 0, Alpha(panel.Alpha * MenuScreenPanel.TopFillAlpha));
        Bgra32 bottom = Bgra32.FromRgba(0, byte.MaxValue, 0, Alpha(panel.Alpha * MenuScreenPanel.BottomFillAlpha));

        batch.DrawQuad(
            TextureHandle.None,
            new Vertex2D(area.X, area.Y, 0f, 0f, top),
            new Vertex2D(area.X + area.Width, area.Y, 1f, 0f, top),
            new Vertex2D(area.X + area.Width, area.Y + area.Height, 1f, 1f, bottom),
            new Vertex2D(area.X, area.Y + area.Height, 0f, 1f, bottom));

        // The border, and the rule under the title, both in the darker green the outline uses.
        Bgra32 edge = Bgra32.FromRgba(0, MenuScreenPanel.EdgeGreen, 0, Alpha(panel.Alpha));

        foreach ((int x, int y, int width, int height) in
            MenuLayout.GetBoxOutline(panel.X0, panel.Y0, panel.X1 - panel.X0, panel.Y1 - panel.Y0))
        {
            batch.FillRect(Centred(x, y, width, height, screenWidth, screenHeight), edge);
        }

        batch.FillRect(
            Centred(panel.X0, panel.Y0 + MenuScreenPanel.TitleRuleOffset, panel.X1 - panel.X0, 1, screenWidth, screenHeight),
            edge);
    }

    private static byte Alpha(float value) => (byte)Math.Clamp(value * byte.MaxValue, 0f, byte.MaxValue);

    /// <summary>Draws the logo every screen carries above its contents.</summary>
    /// <remarks>
    /// <c>MenuScreen_Prepare</c> (<c>0x421A70</c>) loads this for every screen, not just the first —
    /// it is the engine's, not the script's, which is why it appears in no menu file.
    /// </remarks>
    private void DrawLogo(SpriteBatch batch, int screenWidth, int screenHeight)
    {
        if (TryGetSprite(MenuScreenTask.LogoResource) is not { } logo)
        {
            return;
        }

        // A .pic is a tile grid, so its size is the grid times the tile — the same shape the
        // background uses. A plain sprite reports its first frame instead.
        (int width, int height) = logo.Asset.TileLayout is { } grid && logo.Asset.Frames.Count > 0
            ? (grid.Columns * (int)logo.Asset.Frames[0].Width, grid.Rows * (int)logo.Asset.Frames[0].Height)
            : ((int)LogoWidth(logo), (int)LogoHeight(logo));

        RectF destination = Centred(
            MenuScreenTask.GetLogoX(width, unitedStates: false),
            MenuScreenTask.LogoY,
            width,
            height,
            screenWidth,
            screenHeight);

        if (logo.Asset.TileLayout is not null)
        {
            logo.DrawTiled(batch, destination, Bgra32.White, BlendMode.AlphaBlend);
            return;
        }

        logo.DrawStretched(batch, 0, destination, Bgra32.White);
    }

    private static float LogoWidth(SpriteResource logo) =>
        logo.Asset.Frames.Count > 0 ? logo.Asset.Frames[0].Width : 0f;

    private static float LogoHeight(SpriteResource logo) =>
        logo.Asset.Frames.Count > 0 ? logo.Asset.Frames[0].Height : 0f;

    /// <summary>Draws the picture a picture box has chosen, if it has one.</summary>
    /// <remarks>
    /// The set is filled by <c>Config_FillMissionPictureBox</c>, which needs mission data that does not
    /// exist yet, so this normally draws the empty box and nothing in it.
    /// </remarks>
    private void DrawPictureBox(SpriteBatch batch, PictureBoxTask box, int screenWidth, int screenHeight)
    {
        DrawWindowBox(batch, box, screenWidth, screenHeight);

        if (box.Picture.Length == 0 || TryGetSprite(box.Picture) is not { } sprite)
        {
            return;
        }

        sprite.DrawStretched(
            batch,
            0,
            Centred(box.LayoutX, box.LayoutY, box.LayoutWidth, box.LayoutHeight, screenWidth, screenHeight),
            Bgra32.White);
    }

    /// <summary>Draws a scrolling list: rows from its scroll offset, with the selected row lit.</summary>
    /// <remarks>
    /// The original draws the row at <c>selected - scrollOffset</c> in a second colour with a bar behind
    /// it (<c>0x41C080</c>), which is what tells you which entry OK will act on. The control needs the
    /// row height for its own hit-testing and the font lives here, so it is published back as we draw.
    /// </remarks>
    private void DrawScrollList(SpriteBatch batch, ScrollListBoxTask box, int screenWidth, int screenHeight)
    {
        BitmapTextRenderer? font = TryGetFont(box.FontResource.Length > 0 ? box.FontResource : DefaultBoxFont);
        if (font is null)
        {
            return;
        }

        int lineHeight = Math.Max(1, (int)font.Measure("A").Height);
        box.RowHeight = lineHeight;

        int rows = Math.Max(1, box.LayoutHeight / lineHeight);
        for (int row = 0; row < rows; row++)
        {
            int index = box.ScrollOffset + row;
            if (index < 0 || index >= box.Items.Count)
            {
                break;
            }

            int y = box.LayoutY + 1 + (row * lineHeight);
            if (index == box.Index)
            {
                // The selected row gets a bar behind it, the way MenuText lights the selected entry:
                // the same green at the highlighted level (GetColour's (c * 0.5) + 64).
                batch.FillRect(
                    Centred(box.LayoutX + 2, y, box.LayoutWidth - 4, lineHeight, screenWidth, screenHeight),
                    MenuLayout.ToPixel(MenuLayout.GetColour(MenuLayout.GreenIndex, selected: true)));
            }

            Draw(batch, font, box.Items[index].Label, box.LayoutX + 2, y, screenWidth, screenHeight);
        }
    }

    /// <summary>Draws as many lines as fit inside a control, starting at a scroll offset.</summary>
    /// <remarks>
    /// One shared routine because all five text-carrying boxes do the same thing: clip to the box, one
    /// line per row, starting from whichever line the control has scrolled to. Nothing is wrapped —
    /// the sources that fill these are already line-broken.
    /// </remarks>
    private void DrawInBox(
        SpriteBatch batch,
        MenuWindowTask box,
        string fontResource,
        IReadOnlyList<string> lines,
        int firstLine,
        int screenWidth,
        int screenHeight)
    {
        BitmapTextRenderer? font = TryGetFont(fontResource.Length > 0 ? fontResource : DefaultBoxFont);
        if (font is null || lines.Count == 0)
        {
            return;
        }

        int lineHeight = Math.Max(1, (int)font.Measure("A").Height);
        int rows = Math.Max(1, box.LayoutHeight / lineHeight);

        for (int row = 0; row < rows; row++)
        {
            int index = firstLine + row;
            if (index < 0 || index >= lines.Count)
            {
                break;
            }

            Draw(batch, font, lines[index], box.LayoutX + 2, box.LayoutY + 1 + (row * lineHeight), screenWidth, screenHeight);
        }
    }

    /// <summary>The font a box falls back to when its script names none.</summary>
    /// <remarks>
    /// <c>TypeWriterBox</c> declares only a width and a height, so it has no font parameter at all.
    /// This is the one the rest of the menu's body text uses.
    /// </remarks>
    public const string DefaultBoxFont = "font3.fnt";

    /// <summary>Draws the black backing and green outline every control sits in.</summary>
    /// <remarks>
    /// <c>MenuWindow_DrawBox</c> (<c>0x424980</c>). The original submits the outline and <em>then</em>
    /// the black fill, which through a painter's-order batch would cover it; the fill goes first here,
    /// for the same reason the pointer is drawn last. See the re-note.
    /// </remarks>
    private void DrawWindowBox(SpriteBatch batch, MenuWindowTask window, int screenWidth, int screenHeight)
    {
        if (window.LayoutWidth <= 0 || window.LayoutHeight <= 0)
        {
            return;
        }

        batch.FillRect(
            Centred(window.LayoutX, window.LayoutY, window.LayoutWidth, window.LayoutHeight, screenWidth, screenHeight),
            Bgra32.FromRgba(0, 0, 0, (byte)(byte.MaxValue * MenuLayout.BoxFillAlpha)));

        Bgra32 edge = MenuLayout.ToPixel(MenuLayout.GetColour(MenuLayout.GreenIndex, window.IsHighlighted));

        foreach ((int x, int y, int width, int height) in
            MenuLayout.GetBoxOutline(window.LayoutX, window.LayoutY, window.LayoutWidth, window.LayoutHeight))
        {
            batch.FillRect(Centred(x, y, width, height, screenWidth, screenHeight), edge);
        }
    }

    private void Draw(
        SpriteBatch batch,
        BitmapTextRenderer font,
        string label,
        int x,
        int y,
        int screenWidth,
        int screenHeight)
    {
        (int screenX, int screenY) = MenuLayout.ToScreen(x, y, screenWidth, screenHeight, _scale);
        font.Draw(batch, label, screenX, screenY, Bgra32.White, _scale, _scale);
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

        (int x, int y) = MenuLayout.ToScreen(frame.LayoutX, frame.LayoutY, screenWidth, screenHeight, _scale);

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
    private void Draw(SpriteBatch batch, SpriteResource? sprite, int x, int y)
    {
        if (sprite is not null && sprite.Asset.Frames.Count > 0)
        {
            sprite.DrawFrame(batch, sprite.Asset.Frames[0], x, y, _scale, _scale, MenuLayout.ToPixel((MenuLayout.FullIntensity, MenuLayout.FullIntensity, MenuLayout.FullIntensity)));
        }
    }

    /// <summary>Maps an authored rectangle onto the screen, centred rather than scaled.</summary>
    // The size is magnified along with the origin, so a box authored 100 wide stays 100 AUTHORED wide.
    private RectF Centred(int x, int y, int width, int height, int screenWidth, int screenHeight)
    {
        (int screenX, int screenY) = MenuLayout.ToScreen(x, y, screenWidth, screenHeight, _scale);
        return new RectF(screenX, screenY, width * _scale, height * _scale);
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

        IresEntry? entry = null;
        foreach (IresArchive archive in _archives)
        {
            entry = archive.Entries.FirstOrDefault(
                e => e.Kind == IresEntryKind.Body
                    && Path.GetFileNameWithoutExtension(e.Name).Equals(
                        Path.GetFileNameWithoutExtension(name),
                        StringComparison.OrdinalIgnoreCase));

            if (entry is not null)
            {
                break;
            }
        }

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
