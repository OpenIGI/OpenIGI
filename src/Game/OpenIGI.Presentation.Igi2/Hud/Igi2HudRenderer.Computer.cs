using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Graphics;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Igi2.Computer;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Rendering;
using OpenIGI.Game.Igi2;
using OpenIGI.Presentation.Igi2.World;

namespace OpenIGI.Presentation.Igi2.Hud;

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

    private readonly List<Igi2ComputerObjectiveLine> _markWords = [];
    private readonly List<(int X, int Y)> _logRules = [];
    private BitmapTextRenderer? _textFont;
    private BitmapTextRenderer? _logFont;
    private float _computerScale = 1f;
    // Game_ptGame->tDrawRandomState and Filter_tRandomState, as Game_Open and Filter_Open seed them.
    private readonly Core.Numerics.IgiRandom _drawRandom = new(unchecked((uint)-923067897), 971667286u, 2146516173u);
    private readonly Core.Numerics.IgiRandom _filterRandom = new(277206966u, 1200274863u, 2120272633u);
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
        if (string.Equals(font, Igi2ComputerDisplay.LogFont, StringComparison.OrdinalIgnoreCase))
        {
            if (_logFont is null && _assets?.Get(Igi2ComputerDisplay.LogFont) is { IsFont: true } log)
            {
                _logFont = new BitmapTextRenderer(log);
            }

            return _logFont ?? _font;
        }

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

        // Only the map shows the world (the game screen's world flag); the other screens black it out. A plan fills the screen
        // (ComputerPlan_DrawHandler).
        if (!display.IsMapShown)
        {
            batch.FillRect(new RectF(0f, 0f, screenWidth, screenHeight), Bgra32.FromRgba(0, 0, 0, 255));
        }

        if (display.PlanPicture is { Length: > 0 } plan && _assets?.GetTexture(plan) is { } picture && picture != TextureHandle.None)
        {
            batch.Draw(picture, new RectF(0f, 0f, screenWidth, screenHeight), RectF.Unit, Bgra32.FromRgba(255, 255, 255, 255));
        }

        DrawComputerFilter(batch, display, screenWidth, screenHeight);
        ComputerRect(batch, w * 0.14375f, h * 0.675f, w * 0.15625f, h * 0.125f, Bgra32.FromRgba(80, 80, 80, 255), BlendMode.Modulate2X);
        if (display.IsMapShown)
        {
            DrawComputerMarks(batch, title, display, w, h);
            DrawComputerMap(batch, w, h);
        }
        else if (display.Screen == Igi2ComputerMenu.Objectives)
        {
            DrawObjectives(batch, display, w, h);
        }
        else if (display.Screen == Igi2ComputerMenu.Log)
        {
            DrawLog(batch, display, w, h);
        }
        else if (display.Screen == Igi2ComputerMenu.Save)
        {
            DrawSave(batch, display, w, h);
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

    // ComputerObject's marks: each projected through the map's camera. A sprite and a text are held inside the glass; a
    // triangle or a diamond is a sprite pulsing, sized by the object's radius and turned its way; a box is an outline.
    private void DrawComputerMarks(SpriteBatch batch, Igi2TitlePlayer title, Igi2ComputerDisplay display, float w, float h)
    {
        if (display.Map is not { } map)
        {
            return;
        }

        int ticks = title.Level?.LevelFlow?.Tick ?? 0;
        _markWords.Clear();
        foreach (Igi2ComputerMapMark mark in title.ComputerMapMarks())
        {
            double depth = map.Position.Z - mark.Position.Z;
            double z = depth > 1.0 ? depth : 1.0;
            double scale = w * 0.5 / (z * map.FieldOfView);
            float x = (float)(((mark.Position.X - map.Position.X) * scale) + (w * 0.5));
            float y = (float)((-(mark.Position.Y - map.Position.Y) * scale) + (h * 0.5));
            switch (mark.Kind)
            {
                case Igi2ComputerMarkKind.Sprite:
                {
                    (x, y) = (Math.Clamp(x, w * 0.15f, w * 0.85f), Math.Clamp(y, h * 0.1f, h * 0.8f));
                    if (_assets?.Get("COMPUTER:" + mark.Name + ".spr") is { } sprite)
                    {
                        SpriteFrame frame = sprite.Asset.Frames[0];
                        (float r, float g, float b) = Igi2ComputerMapMark.Colours[(int)Igi2ComputerMarkerColour.BrightBlue];
                        ComputerSprite(batch, "COMPUTER:" + mark.Name + ".spr", x - (frame.Width * 0.5f), y - (frame.Height * 0.5f), Colour128(r * 0.5f, g * 0.5f, b * 0.5f));
                    }

                    break;
                }

                case Igi2ComputerMarkKind.Text:
                    (x, y) = (Math.Clamp(x, w * 0.15f, w * 0.85f), Math.Clamp(y, h * 0.1f, h * 0.8f));
                    display.Wrap((int)x + 10, (int)y, mark.Name, Igi2ComputerDisplay.TextFont, 128, Igi2ComputerColour.DarkRed, _markWords);
                    break;
                default:
                    DrawMarker(batch, mark, x, y, (float)(w * 0.5 / map.FieldOfView / z), ticks);
                    break;
            }
        }

        foreach (Igi2ComputerObjectiveLine word in _markWords)
        {
            ComputerText(batch, word.Font, word.Text, word.X, word.Y, Colour(word.Colour));
        }
    }

    // ComputerObject_DrawMarker.
    private void DrawMarker(SpriteBatch batch, Igi2ComputerMapMark mark, float x, float y, float perUnit, int ticks)
    {
        if (mark.Marker == Igi2ComputerMarker.None || mark.Colour == Igi2ComputerMarkerColour.None)
        {
            return;
        }

        float radius = Math.Max(mark.Radius, Igi2ComputerMapMark.MinimumRadius);
        float sx = perUnit * radius, sy = perUnit * radius;
        (float r, float g, float b) = Igi2ComputerMapMark.Colours[(int)mark.Colour];
        float s = MathF.Sin(mark.Gamma), c = MathF.Cos(mark.Gamma);
        if (mark.Marker is Igi2ComputerMarker.Triangle or Igi2ComputerMarker.LargeTriangle or Igi2ComputerMarker.Diamond)
        {
            float across = ((sx - 128f) * 0.9f) + 128f, along = ((sy - 128f) * 0.9f) + 128f;
            if (mark.Marker == Igi2ComputerMarker.LargeTriangle)
            {
                across *= 1.5f;
                along *= 1.5f;
            }

            float cx = x - (s * along * 0.33333334f), cy = y - (c * along * 0.33333334f);
            float bx = c * across, sa = s * across, sl = s * along, cl = c * along;
            float alpha = (float)((Math.Sin(ticks * 0.10000000149011612) * 0.20000000298023224) + 0.5);
            string name = mark.Marker == Igi2ComputerMarker.Diamond ? "diamond.spr" : "triangle.spr";
            if (_assets?.Get(name) is not { } sprite)
            {
                return;
            }

            SpriteFrame frame = sprite.Asset.Frames[0];
            Bgra32 colour = Bgra32.FromRgba(Channel(r / 255f), Channel(g / 255f), Channel(b / 255f), Channel(alpha));
            float k = _computerScale;
            batch.DrawQuad(
                sprite.Textures[frame.TextureIndex],
                new Vertex2D((cx - bx - sl) * k, (cy + sa - cl) * k, frame.U0, frame.V0, colour),
                new Vertex2D((bx + cx - sl) * k, (cy - sa - cl) * k, frame.U1, frame.V0, colour),
                new Vertex2D((bx + cx + sl) * k, (cy - sa + cl) * k, frame.U1, frame.V1, colour),
                new Vertex2D((cx - bx + sl) * k, (cy + sa + cl) * k, frame.U0, frame.V1, colour));
            return;
        }

        // QSprite_RegisterLineMesh: the outline scaled about the object and turned by its heading, three quarters opaque.
        Bgra32 line = Bgra32.FromRgba(Channel(r / 255f), Channel(g / 255f), Channel(b / 255f), Channel(0.75f));
        IReadOnlyList<(float X, float Y)> outline = Igi2ComputerMapMark.BoxOutline;
        for (int i = 1; i < outline.Count; i++)
        {
            (float ax, float ay) = Turn(outline[i - 1], sx, sy, s, c);
            (float ex, float ey) = Turn(outline[i], sx, sy, s, c);
            Stroke(batch, new Igi2SightLine((x + ax) * _computerScale, (y + ay) * _computerScale, (x + ex) * _computerScale, (y + ey) * _computerScale, 1f), 1f, line);
        }
    }

    // An outline's point scaled and turned by minus the heading.
    private static (float X, float Y) Turn((float X, float Y) point, float sx, float sy, float s, float c)
    {
        float px = point.X * sx, py = point.Y * sy;
        return ((px * c) + (py * s), (py * c) - (px * s));
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

    // ComputerLog_DrawHandler: its title, then its lines as the display laid them out, each underlined by a line from the
    // screen's own green fading to dark over a hundred pixels.
    private void DrawLog(SpriteBatch batch, Igi2ComputerDisplay display, float w, float h)
    {
        string heading = display.Text("computer/LOG");
        ComputerText(batch, Igi2ComputerDisplay.LabelFont, heading, (w * 0.5f) - StringWidth(Igi2ComputerDisplay.LabelFont, heading), h * 0.078125f, Colour128(128f, 128f, 128f));
        _logRules.Clear();
        foreach (Igi2ComputerObjectiveLine line in display.LayoutLog(_logRules))
        {
            ComputerText(batch, line.Font, line.Text, line.X, line.Y, Colour(line.Colour));
        }

        Bgra32 from = Colour128(48f, 96f, 48f), to = Colour128(0f, 16f, 0f);
        foreach ((int x, int y) in _logRules)
        {
            float left = x * _computerScale, right = (x + 100) * _computerScale, top = y * _computerScale, bottom = top + Math.Max(1f, _computerScale);
            batch.DrawQuad(
                TextureHandle.None,
                new Vertex2D(left, top, 0f, 0f, from),
                new Vertex2D(right, top, 1f, 0f, to),
                new Vertex2D(right, bottom, 1f, 1f, to),
                new Vertex2D(left, bottom, 0f, 1f, from));
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

            Bgra32 colour = Colour(item.IsActive && !item.IsDisabled ? item.HighlightColour : item.Colour);
            ComputerText(batch, item.Font, item.Text, item.X, item.Y, colour);

            // ComputerSaveMenu_SaveSlotDrawHandler: a used slot's date under its line, in its colour.
            if (display.Screen == Igi2ComputerMenu.Save && item.Id - 10 is >= 0 and < Igi2ComputerDisplay.SaveSlots and var slot
                && display.SaveSlotLabels[slot].When is { Length: > 0 } when)
            {
                ComputerText(batch, item.Font, when, (int)item.X, (int)(item.Y + LineHeight(item.Font)), colour);
            }
        }
    }

    // ComputerSaveMenu_DrawHandler: the title centred, what the slots are for, the battery's label right of its gauge; on the
    // overwrite page the question; the gauge filling from the bottom under the battery's outline; while uploading, its bar and
    // its count.
    private void DrawSave(SpriteBatch batch, Igi2ComputerDisplay display, float w, float h)
    {
        string label = Igi2ComputerDisplay.LabelFont, text = Igi2ComputerDisplay.TextFont;
        string title = display.Text("computer/IGI HQ");
        ComputerText(batch, label, title, (int)((w - StringWidth(label, title)) * 0.5f), (int)(h * 0.1041666641831398f), Colour128(128f, 128f, 128f));
        ComputerText(batch, label, display.SaveSubtitle, (int)(w * 0.15625f), (int)(h * 0.1875f), Colour128(128f, 128f, 128f));
        bool flat = display.Battery == 0f;
        string battery = display.Text(flat ? "computer/BATTERY LOW" : "computer/Battery Level");
        ComputerText(batch, text, battery, (int)((w * 0.78125f) - StringWidth(text, battery)), (int)((h * 0.6458333134651184f) + 64f - LineHeight(text)),
            Colour(flat ? Igi2ComputerColour.DarkRed : Igi2ComputerColour.Green));
        if (display.SaveState == Igi2ComputerSaveState.Overwrite)
        {
            ComputerText(batch, text, display.Text("computer/Overwrite?"), display.OverwriteX, display.OverwriteY, Colour(Igi2ComputerColour.DarkRed));
            ComputerText(batch, text, Igi2ComputerDisplay.SaveSlash, display.SlashX, display.OverwriteY, Colour(Igi2ComputerColour.Green));
        }

        // The gauge: yellow at the top shading to the charge's green below, its height the charge.
        float charge = display.Battery, left = w * 0.78125f;
        float top = ((1f - charge) * 64f) + (h * 0.6458333134651184f), height = charge * 64f;
        Bgra32 yellow = Colour128(128f, 128f, 0f), low = Colour128(128f, charge * 128f, 0f);
        ComputerGradient(batch, left, top, 64f, height, yellow, yellow, low, low);
        ComputerStretch(batch, "COMPUTER:battery.spr", left, h * 0.6458333134651184f, left + 64f, (h * 0.6458333134651184f) + 64f,
            Colour(Igi2ComputerColour.Green), BlendMode.AlphaBlend);

        if (display.SaveState is Igi2ComputerSaveState.Uploading or Igi2ComputerSaveState.Complete)
        {
            float progress = display.UploadProgress;
            Bgra32 from = Colour128(0f, progress * 128f, 128f), to = Colour128(0f, 128f, 128f);
            ComputerGradient(batch, w * 0.15625f, h * 0.7916666865348816f, progress * 0.65625f * w, h * 0.01666666753590107f, from, to, from, to);
            string uploading = display.Text("computer/Uploading") + ":";
            float labelY = (int)((h * 0.7916666865348816f) - LineHeight(text) - 2f), labelX = (int)(w * 0.15625f);
            ComputerText(batch, text, uploading, labelX, labelY, Colour(Igi2ComputerColour.Green));
            string count = display.SaveState == Igi2ComputerSaveState.Complete
                ? display.Text("computer/Complete")
                : ((int)(progress * 100f)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
            ComputerText(batch, text, count, (int)(StringWidth(text, uploading) + labelX), labelY, Colour(Igi2ComputerColour.White));
        }
    }

    private void ComputerGradient(SpriteBatch batch, float x, float y, float width, float height, Bgra32 topLeft, Bgra32 topRight, Bgra32 bottomLeft, Bgra32 bottomRight)
    {
        float k = _computerScale;
        batch.DrawQuad(
            TextureHandle.None,
            new Vertex2D(x * k, y * k, 0f, 0f, topLeft),
            new Vertex2D((x + width) * k, y * k, 1f, 0f, topRight),
            new Vertex2D((x + width) * k, (y + height) * k, 1f, 1f, bottomRight),
            new Vertex2D(x * k, (y + height) * k, 0f, 1f, bottomLeft));
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

    // Game_ptGame->tDrawRandomState: the presentation's own stream.
    private uint DrawRandom() => _drawRandom.NextRaw();

    // Filter_tRandomState: the screen filters' stream.
    private uint FilterRandom() => _filterRandom.NextRaw();
}
