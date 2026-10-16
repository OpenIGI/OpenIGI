using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Graphics;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Igi2.Computer;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi2.Hud;

/// <content>
/// The open map computer (<c>ComputerMenu_DrawHandler</c> and its screens'): the picture under the casing's line filter, the menu
/// down the left on its grey panel, the screen's own text and buttons, the pointer, the casing over everything and its lamp's
/// glow. The screens are laid out 480 lines high, as on the display the casing was drawn for, and stretched across.
/// </content>
public sealed partial class Igi2HudRenderer : IIgi2ComputerFonts
{
    /// <summary>The lines the computer's screens are laid out in.</summary>
    public const float ComputerLines = 480f;

    /// <summary>The line filter's tiles across the screen, <c>ComputerFilter_D3DDraw</c>'s texture coordinates.</summary>
    public const int FilterColumns = 40;

    /// <summary>Its tiles down.</summary>
    public const int FilterRows = 30;

    private BitmapTextRenderer? _textFont;
    private float _computerScale = 1f;
    private uint _drawRandom = 0x2545F491u;
    private uint _drawRandomB = 0x9E3779B9u;
    private uint _drawRandomC = 0x7F4A7C15u;
    private int _filterFrame;
    private int _filterGlitch;
    private bool _brokenSound;

    /// <inheritdoc/>
    public int StringWidth(string font, string text)
    {
        BitmapTextRenderer? renderer = Font(font);
        return renderer is null ? text.Length * 8 : (int)renderer.Measure(text).Width;
    }

    /// <inheritdoc/>
    public int LineHeight(string font) => Font(font)?.LineHeight ?? 16;

    private BitmapTextRenderer? Font(string font)
    {
        if (string.Equals(font, Igi2ComputerDisplay.TextFont, StringComparison.OrdinalIgnoreCase))
        {
            if (_textFont is null && _assets?.Get(Igi2ComputerDisplay.TextFont) is { IsFont: true } loaded)
            {
                _textFont = new BitmapTextRenderer(loaded);
            }

            return _textFont ?? _font;
        }

        return _font;
    }

    private void DrawComputer(SpriteBatch batch, Igi2TitlePlayer title, int screenWidth, int screenHeight)
    {
        if (title.ComputerDisplay is not { IsOpen: true } display)
        {
            return;
        }

        float w = display.Width, h = display.Height;

        // Only the map shows the world (the game screen's world flag); the other screens black it out.
        if (!display.IsMapShown)
        {
            batch.FillRect(new RectF(0f, 0f, screenWidth, screenHeight), Bgra32.FromRgba(0, 0, 0, 255));
        }

        DrawComputerFilter(batch, display, screenWidth, screenHeight);
        ComputerRect(batch, w * 0.14375f, h * 0.675f, w * 0.15625f, h * 0.125f, Bgra32.FromRgba(80, 80, 80, 255), BlendMode.Modulate2X);
        if (display.IsMapShown)
        {
            DrawComputerMap(batch, w, h);
        }
        else if (display.Screen == Igi2ComputerMenu.Objectives)
        {
            DrawObjectives(batch, display, w, h);
        }

        DrawComputerItems(batch, display);

        // The pointer, its hand while the map is dragged, centred on where it is.
        string pointer = display.IsPointerLocked ? "COMPUTER:pointer_hand.spr" : "COMPUTER:pointer.spr";
        if (_assets?.Get(pointer) is { } sprite)
        {
            SpriteFrame frame = sprite.Asset.Frames[0];
            ComputerSprite(batch, pointer, display.PointerX - (frame.Width * 0.5f), display.PointerY - (frame.Height * 0.5f), Colour128(128f, 128f, 128f));
        }

        // The casing over the whole screen, and the glow of its lamp flickering by the draw stream.
        ComputerStretch(batch, display.IsBroken ? "COMPUTER:overlay_broken.spr" : "COMPUTER:overlay.spr", 0f, 0f, w, h, Colour128(128f, 128f, 128f), BlendMode.AlphaBlend);
        int step = (int)(DrawRandom() % 12u) % 12;
        if (step >= 6)
        {
            step = 12 - step;
        }

        float glow = ((step * 0.013333332724869251f) + 0.30000001192092896f) * 128f;
        ComputerStretch(batch, "COMPUTER:glowsprite.spr", w * 0.21875f, h * 0.8f, w * 0.3187499940395355f, h * 0.9333333373069763f, Colour128(glow, glow, glow), BlendMode.Additive);
    }

    // ComputerFilter_D3DDraw: the line texture tiled forty by thirty, doubling what it multiplies; it flips between its two
    // frames each draw, and a broken casing now and then shows the torn pair for five draws, with a crackle.
    private void DrawComputerFilter(SpriteBatch batch, Igi2ComputerDisplay display, int screenWidth, int screenHeight)
    {
        bool glitch = false;
        if (display.IsBroken && FilterRandom() % 100u < 2u)
        {
            _filterGlitch = 5;
            glitch = true;
        }

        int texture = _filterGlitch != 0 ? _filterFrame + 2 : _filterFrame;
        if (_filterGlitch > 0)
        {
            _filterGlitch--;
        }

        _filterFrame = 1 - _filterFrame;
        if (glitch)
        {
            display.PlaySound?.Invoke(_brokenSound ? "mcbroken_02" : "mcbroken_01");
            _brokenSound = !_brokenSound;
        }

        TextureHandle handle = _assets?.GetTexture($"LOCAL:common/textures/linefilter{texture + 1}.tga") ?? TextureHandle.None;
        if (handle == TextureHandle.None)
        {
            return;
        }

        float tileWidth = (float)screenWidth / FilterColumns, tileHeight = (float)screenHeight / FilterRows;
        Bgra32 white = Bgra32.FromRgba(255, 255, 255, 255);
        for (int row = 0; row < FilterRows; row++)
        {
            for (int column = 0; column < FilterColumns; column++)
            {
                batch.Draw(handle, new RectF(column * tileWidth, row * tileHeight, tileWidth, tileHeight), RectF.Unit, white, BlendMode.Modulate2X);
            }
        }
    }

    // ComputerMap_DrawHandler: the compass at the top right, scaled with the display.
    private void DrawComputerMap(SpriteBatch batch, float w, float h)
    {
        if (_assets?.Get("COMPUTER:compass.spr") is not { } compass)
        {
            return;
        }

        SpriteFrame frame = compass.Asset.Frames[0];
        float left = (int)(520f * w * 0.0015625f), top = (int)(50f * h * 0.0020833334f);
        float right = (int)((w * frame.Width * 0.0015625f) + (520f * w * 0.0015625f));
        float bottom = (int)((h * frame.Height * 0.0020833334f) + (50f * h * 0.0020833334f));
        ComputerStretch(batch, "COMPUTER:compass.spr", left, top, right, bottom, Colour128(128f, 128f, 128f), BlendMode.AlphaBlend);
    }

    // ComputerObjectives_DrawHandler: the screen's title centred near the top, then each objective as the display laid it out.
    private void DrawObjectives(SpriteBatch batch, Igi2ComputerDisplay display, float w, float h)
    {
        string heading = display.Text("computer/OBJECTIVES");
        ComputerText(batch, Igi2ComputerDisplay.LabelFont, heading, (w * 0.5f) - StringWidth(Igi2ComputerDisplay.LabelFont, heading), h * 0.078125f, Colour128(128f, 128f, 128f));
        foreach (Igi2ComputerObjectiveLine line in display.LayoutObjectives())
        {
            ComputerText(batch, line.Font, line.Text, line.X, line.Y, Colour(line.Colour));
        }
    }

    // The menu's items: a text item in its colour, lit in its light one; a button its plain sprite, or lit, pressed.
    private void DrawComputerItems(SpriteBatch batch, Igi2ComputerDisplay display)
    {
        foreach (Igi2ComputerMenuItem item in display.Items.Items)
        {
            if (item.Sprites is { } sprites)
            {
                string sprite = item.IsActive ? (item.IsPressed ? sprites[2] : sprites[1]) : sprites[0];
                ComputerSprite(batch, "COMPUTER:" + sprite, item.X, item.Y, Colour128(128f, 128f, 128f));
                continue;
            }

            ComputerText(batch, item.Font, item.Text, item.X, item.Y, Colour(item.IsActive ? item.HighlightColour : item.Colour));
        }
    }

    // ComputerMenu_atColor, 128 for full.
    private static Bgra32 Colour(Igi2ComputerColour colour) => colour switch
    {
        Igi2ComputerColour.DarkRed => Colour128(128f, 16f, 16f),
        Igi2ComputerColour.Green => Colour128(51.2f, 76.8f, 128f),
        Igi2ComputerColour.LightGreen => Colour128(115.2f, 115.2f, 128f),
        Igi2ComputerColour.White => Colour128(128f, 128f, 128f),
        _ => Colour128(89.6f, 89.6f, 89.6f),
    };

    private static Bgra32 Colour128(float r, float g, float b) =>
        Bgra32.FromRgba(Channel(r / 128f), Channel(g / 128f), Channel(b / 128f), 255);

    /// <summary>Tells the computer the screen its pages are laid out in and the fonts it measures them in, before it opens.</summary>
    private void MeasureComputer(Igi2TitlePlayer title, int screenWidth, int screenHeight)
    {
        _computerScale = screenHeight / ComputerLines;
        if (title.ComputerDisplay is { } display)
        {
            display.Width = screenWidth / _computerScale;
            display.Height = ComputerLines;
            display.Fonts = this;
        }
    }

    private void ComputerRect(SpriteBatch batch, float x, float y, float width, float height, Bgra32 colour, BlendMode blend) =>
        batch.FillRect(new RectF(x * _computerScale, y * _computerScale, width * _computerScale, height * _computerScale), colour, blend);

    private void ComputerText(SpriteBatch batch, string font, string text, float x, float y, Bgra32 colour) =>
        Font(font)?.Draw(batch, text, x * _computerScale, y * _computerScale, colour, _computerScale, _computerScale);

    private void ComputerSprite(SpriteBatch batch, string name, float x, float y, Bgra32 colour)
    {
        if (_assets?.Get(name) is not { } sprite)
        {
            return;
        }

        sprite.DrawFrame(batch, sprite.Asset.Frames[0], x * _computerScale, y * _computerScale, _computerScale, _computerScale, colour);
    }

    private void ComputerStretch(SpriteBatch batch, string name, float left, float top, float right, float bottom, Bgra32 colour, BlendMode blend)
    {
        if (_assets?.Get(name) is not { } sprite)
        {
            return;
        }

        SpriteFrame frame = sprite.Asset.Frames[0];
        batch.Draw(
            sprite.Textures[frame.TextureIndex],
            new RectF(left * _computerScale, top * _computerScale, (right - left) * _computerScale, (bottom - top) * _computerScale),
            RectF.FromEdges(frame.U0, frame.V0, frame.U1, frame.V1),
            colour,
            blend);
    }

    // Game_ptGame->tDrawRandomState: the presentation's own stream, the step the run stream takes.
    private uint DrawRandom()
    {
        _drawRandomC ^= _drawRandom >> 7;
        _drawRandomB ^= _drawRandomC;
        _drawRandom += _drawRandomB;
        return _drawRandom;
    }

    // Filter_tRandomState, the same step over its own words.
    private uint FilterRandom() => DrawRandom();
}
