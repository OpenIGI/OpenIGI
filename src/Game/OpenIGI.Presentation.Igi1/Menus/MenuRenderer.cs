using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Menus;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

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
    private int _scale = UiScale.Native;

    /// <summary>Gets or sets the magnification asked for; <see cref="UiScale.Automatic"/> derives one.</summary>
    public int Scale { get; set; } = UiScale.Automatic;

    /// <summary>Gets the magnification the last draw used.</summary>
    public int EffectiveScale => _scale;

    // The menu's archive, then the extra archives, searched in that order. Task parameters name a bare
    // file, so a sprite is matched by its stem; a sprite that will not decode throws, as it always has.
    private readonly RetailUiResources _resources;
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

        _strings = strings;
        _resources = new RetailUiResources(device, new RetailUiResourceOptions { MatchStem = true });
        _resources.AddArchive(fileSystem.ReadArchive(resourcePath));

        foreach (string extra in extraArchives ?? [])
        {
            if (fileSystem.Exists(extra))
            {
                _resources.AddArchive(fileSystem.ReadArchive(extra));
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

        MenuBackgroundFx.Draw(batch, (screen.Manager as MenuManagerTask)?.BackgroundFx, screenWidth, screenHeight, _scale);
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
        _resources.Dispose();
    }

    /// <summary>Resolves and measures a screen's or a popup's labels so the line-up has sizes to work with.</summary>
    /// <param name="window">The screen or popup about to be shown.</param>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
    public void Prepare(MenuWindowTask window)
    {
        ArgumentNullException.ThrowIfNull(window);
        MeasureText(window);
    }

    /// <summary>Draws a popup over the screen: its box, then its controls over it.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="popup">The popup.</param>
    /// <param name="alpha">The fade it is drawn at, on the manager's 0-to-16 scale.</param>
    /// <param name="screenWidth">The back-buffer width in pixels.</param>
    /// <param name="screenHeight">The back-buffer height in pixels.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The popup's render (<c>0x419CB0</c>) submits its controls and then its box (<c>0x419D30</c>), nearest first, so the box
    /// lies under them.
    /// </remarks>
    public void DrawPopup(SpriteBatch batch, DialogWindowTask popup, int alpha, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(popup);

        _scale = UiScale.Resolve(Scale, screenWidth, screenHeight);
        MenuDialogPanel panel = MenuDialogPanel.For(popup, alpha);
        if (panel.IsEmpty)
        {
            return;
        }

        batch.Begin();

        // The black backing, then the green fill over it, lit from above as a screen's panel is.
        RectF area = Centred(panel.X0, panel.Y0, panel.X1 - panel.X0, panel.Y1 - panel.Y0, screenWidth, screenHeight);
        batch.FillRect(area, Bgra32.FromRgba(0, 0, 0, Alpha(panel.Alpha * MenuDialogPanel.BackingAlpha)));

        Bgra32 top = Bgra32.FromRgba(0, byte.MaxValue, 0, Alpha(panel.Alpha * MenuScreenPanel.TopFillAlpha));
        Bgra32 bottom = Bgra32.FromRgba(0, byte.MaxValue, 0, Alpha(panel.Alpha * MenuScreenPanel.BottomFillAlpha));
        batch.DrawQuad(
            TextureHandle.None,
            new Vertex2D(area.X, area.Y, 0f, 0f, top),
            new Vertex2D(area.X + area.Width, area.Y, 1f, 0f, top),
            new Vertex2D(area.X + area.Width, area.Y + area.Height, 1f, 1f, bottom),
            new Vertex2D(area.X, area.Y + area.Height, 0f, 1f, bottom));

        // The outline is a line loop through the four corners themselves, and the rule spans the box under the title.
        Bgra32 edge = Bgra32.FromRgba(0, MenuScreenPanel.EdgeGreen, 0, Alpha(panel.Alpha));
        int width = panel.X1 - panel.X0;
        int height = panel.Y1 - panel.Y0;
        batch.FillRect(Centred(panel.X0, panel.Y0, width, 1, screenWidth, screenHeight), edge);
        batch.FillRect(Centred(panel.X1, panel.Y0, 1, height, screenWidth, screenHeight), edge);
        batch.FillRect(Centred(panel.X0, panel.Y1, width, 1, screenWidth, screenHeight), edge);
        batch.FillRect(Centred(panel.X0, panel.Y0, 1, height, screenWidth, screenHeight), edge);
        batch.FillRect(Centred(panel.X0, panel.RuleY, width, 1, screenWidth, screenHeight), edge);

        DrawSubtree(batch, popup, screenWidth, screenHeight);
        batch.End();
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
                case ControlsMenuTask controls:
                    DrawControlsMenu(batch, controls, screenWidth, screenHeight);
                    break;
                default:
                    break;
            }

            DrawSubtree(batch, child, screenWidth, screenHeight);
        }
    }

    /// <summary>Draws the controls list: its rows, its outline, and each row's action and key.</summary>
    /// <remarks>
    /// <para>
    /// <c>ControlsMenu_Render</c> (<c>0x4238B0</c>) fades it in over the second half of the manager's fade,
    /// <c>(alpha - 8) / 8</c>. Its outline (<c>0x423930</c>) is a loop one pixel inside the rectangle with a rule down the
    /// line between the columns, in full green.
    /// </para>
    /// <para>
    /// Each row (<c>0x423AC0</c>) is a band of green under each column at half the fade, brighter on the even rows and in the
    /// key column: 32 and 48 on the 0-to-128 scale for an even row, 16 and 32 for an odd one. The action is written four pixels
    /// in and five down in green, the key four pixels into its column in amber (128, 90, 0).
    /// </para>
    /// <para>
    /// The original submits the rows' text, then their bands, then the outline, nearest first; drawn back to front here.
    /// </para>
    /// </remarks>
    private void DrawControlsMenu(SpriteBatch batch, ControlsMenuTask controls, int screenWidth, int screenHeight)
    {
        int fade = (controls.Manager as MenuManagerTask)?.Navigator.Alpha ?? MenuNavigator.FullAlpha;
        float alpha = Math.Max(0f, (fade - (MenuNavigator.FullAlpha / 2)) * 0.125f);
        if (alpha <= 0f)
        {
            return;
        }

        int x = controls.LayoutX;
        int y = controls.LayoutY;
        int last = Math.Min(controls.Offset + ControlsMenuTask.VisibleRows, ControlsMenuTask.RowCount);
        for (int row = Math.Max(0, controls.Offset), band = 0; row < last; row++, band++)
        {
            bool even = (row & 1) == 0;
            int top = y + (band * ControlsMenuTask.RowHeight);
            batch.FillRect(
                Centred(x, top, ControlsMenuTask.LabelWidth, ControlsMenuTask.RowHeight, screenWidth, screenHeight),
                Shade(0f, even ? 32f : 16f, 0f, alpha * 0.5f));
            batch.FillRect(
                Centred(x + ControlsMenuTask.LabelWidth, top, ControlsMenuTask.KeyWidth, ControlsMenuTask.RowHeight, screenWidth, screenHeight),
                Shade(0f, even ? 48f : 32f, 0f, alpha * 0.5f));
        }

        Bgra32 edge = Shade(0f, MenuLayout.FullIntensity, 0f, alpha);
        foreach ((int ex, int ey, int width, int height) in MenuLayout.GetBoxOutline(x, y, controls.LayoutWidth, controls.LayoutHeight))
        {
            batch.FillRect(Centred(ex, ey, width, height, screenWidth, screenHeight), edge);
        }

        batch.FillRect(
            Centred(x + 1 + ControlsMenuTask.LabelWidth, y + 1, 1, controls.LayoutHeight - 1, screenWidth, screenHeight),
            edge);

        if (TryGetFont(ControlsMenuTask.FontResource) is not { } font)
        {
            return;
        }

        Bgra32 actionColour = Shade(0f, MenuLayout.FullIntensity, 0f, alpha);
        Bgra32 keyColour = Shade(MenuLayout.FullIntensity, 90f, 0f, alpha);
        for (int row = Math.Max(0, controls.Offset), band = 0; row < last; row++, band++)
        {
            int top = y + (band * ControlsMenuTask.RowHeight) + 5;
            Draw(batch, font, _strings.Get(ControlsMenuTask.Actions[row]), x + 4, top, actionColour, screenWidth, screenHeight);
            if (controls.KeyTextOf(row) is { Length: > 0 } key)
            {
                Draw(batch, font, _strings.Get(key), x + ControlsMenuTask.LabelWidth + 4, top, keyColour, screenWidth, screenHeight);
            }
        }
    }

    // A colour on the original's 0-to-128 scale at an alpha.
    private static Bgra32 Shade(float r, float g, float b, float alpha)
    {
        Bgra32 pixel = MenuLayout.ToPixel((r, g, b));
        return Bgra32.FromRgba(pixel.R, pixel.G, pixel.B, Alpha(alpha));
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
        // immediate.
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
        MenuScreenPanel panel = MenuScreenPanel.For(screen, (screen.Manager as MenuManagerTask)?.Navigator.Alpha ?? MenuNavigator.FullAlpha);
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

    /// <summary>Converts a 0-to-1 alpha to a byte, clamped.</summary>
    internal static byte Alpha(float value) => (byte)Math.Clamp(value * byte.MaxValue, 0f, byte.MaxValue);

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
    /// for the same reason the pointer is drawn last.
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
        Draw(batch, font, label, x, y, Bgra32.White, screenWidth, screenHeight);
    }

    private void Draw(
        SpriteBatch batch,
        BitmapTextRenderer font,
        string label,
        int x,
        int y,
        Bgra32 colour,
        int screenWidth,
        int screenHeight)
    {
        (int screenX, int screenY) = MenuLayout.ToScreen(x, y, screenWidth, screenHeight, _scale);
        font.Draw(batch, label, screenX, screenY, colour, _scale, _scale);
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

    private BitmapTextRenderer? TryGetFont(string name) => _resources.Font(name);

    /// <summary>Loads a sprite named by a task parameter, caching hits and misses alike.</summary>
    /// <remarks>
    /// Parameters name a bare file, so the archive is searched by stem. A name that resolves to
    /// nothing is not an error: the original tolerates an unset parameter and draws nothing.
    /// </remarks>
    private SpriteResource? TryGetSprite(string name) => _resources.Sprite(name);
}
