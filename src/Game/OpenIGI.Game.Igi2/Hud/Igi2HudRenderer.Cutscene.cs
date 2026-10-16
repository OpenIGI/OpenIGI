using OpenIGI.Core.Graphics;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi2.Hud;

/// <content>
/// A running cutscene's framing (<c>pc_filter.c</c>): the letterbox its camera opens and closes, and the filter its camera puts over
/// the picture, in the screen's own pixels.
/// </content>
public sealed partial class Igi2HudRenderer
{
    /// <summary>The line filter's alpha, packed with the camera's colour (<c>LineFilter_D3DDraw</c>).</summary>
    public const byte LineAlpha = 0x4C;

    /// <summary>How far apart the line filter's rows are, in pixels.</summary>
    public const int LineSpacing = 4;

    private void DrawCutsceneFraming(SpriteBatch batch, Igi2TitlePlayer title, int width, int height)
    {
        if (!title.IsCutscenePlaying || title.Level?.CutsceneView is not { } view)
        {
            return;
        }

        switch (view.FilterId)
        {
            case Igi2CutsceneCameras.LineFilter:
                DrawLines(batch, view, width, height);
                break;
            case Igi2CutsceneCameras.BandFilter or Igi2CutsceneCameras.BandUpdateFilter:
                DrawBand(batch, title, view, width, height);
                break;
            case Igi2CutsceneCameras.HandiCamFilter:
                DrawLines(batch, view, width, height);
                DrawHandiCam(batch, width, height);
                break;
        }

        // The letterbox: the picture's height a share of the screen's, black above and below.
        float visible = Math.Clamp(view.ViewportHeight, 0f, 1f);
        if (visible < 1f)
        {
            float bars = ((1f - visible) * (height + 1f) * 0.5f) + 1f;
            Bgra32 black = Bgra32.FromRgb(0, 0, 0);
            Rectangle(batch, 0f, 0f, width, bars, black);
            Rectangle(batch, 0f, height - bars, width, height, black);
        }
    }

    // LineFilter_D3DDraw: a one-pixel row every fourth pixel down the picture, the camera's colour at a fixed alpha.
    private static void DrawLines(SpriteBatch batch, Igi2CutsceneView view, int width, int height)
    {
        Bgra32 line = new(Channel(view.FilterColor.Z), Channel(view.FilterColor.Y), Channel(view.FilterColor.X), LineAlpha);
        for (int y = 0; y < height; y += LineSpacing)
        {
            Rectangle(batch, 0f, y, width, Math.Min(height, y + 1), line);
        }
    }

    /// <summary>The band filter's roll, in ticks, <c>BandFilter_New</c>'s move loop as the cutscene makes it.</summary>
    public const int BandMoveLoop = 60;

    /// <summary>Its noise's longest wait, in ticks, the noise loop.</summary>
    public const int BandNoiseLoop = 90;

    /// <summary>The band filter's texture's side, in texels and in the screen's pixels.</summary>
    public const int BandTexture = 16;

    private readonly Bgra32[] _bandTexels = new Bgra32[BandTexture * BandTexture];
    private TextureHandle _bandStripes;
    private TextureHandle _bandPlain;
    private float _bandNoise;
    private int _bandMove;
    private int _bandNoiseTimer = BandNoiseLoop;
    private int _bandNoiseLoop = BandNoiseLoop;
    private int _bandTick = -1;

    // BandFilter (pc_filter.c), a security camera's feed: the picture in bands a twentieth of its height, brightened by the
    // camera's colour as the band texture has it (BandFilter_D3DDraw blends DESTCOLOR, ONE: the picture times one and the
    // texel). Every other band is striped, two rows at half the colour and two at full, and moves half a band on after
    // three quarters of its roll; the rest are plain. Noise darkens random texels, flaring at random and dying away.
    private void DrawBand(SpriteBatch batch, Igi2TitlePlayer title, Igi2CutsceneView view, int width, int height)
    {
        RunBand(title, view);

        // With no noise there are no bands (BandFilter_D3DDraw draws nothing): BandFilter_DrawHandler multiplies the picture by
        // the colour instead, which the cutscenes fade to and from black with.
        if (view.FilterNoise == 0f)
        {
            Core.Numerics.Vector3f tint = view.FilterColor;
            batch.FillRect(
                new RectF(0f, 0f, width, height),
                Bgra32.FromRgba(Half(tint.X), Half(tint.Y), Half(tint.Z), 255),
                BlendMode.Modulate2X);
            return;
        }

        if (_bandStripes == TextureHandle.None)
        {
            _bandStripes = _device.CreateTexture(new TextureDescription(BandTexture, BandTexture, 1, TextureFilter.Point, TextureAddressMode.Clamp), _bandTexels);
            _bandPlain = _device.CreateTexture(new TextureDescription(BandTexture, BandTexture, 1, TextureFilter.Point, TextureAddressMode.Clamp), _bandTexels);
        }

        Core.Numerics.Vector3f colour = view.FilterColor;
        float r = Math.Clamp(colour.X, 0f, 1f), g = Math.Clamp(colour.Y, 0f, 1f), b = Math.Clamp(colour.Z, 0f, 1f);

        // BandFilter_MakeBandTexture: four times two rows at half the colour, two at full.
        for (int row = 0; row < BandTexture; row++)
        {
            float k = (row & 2) == 0 ? 128f : 255f;
            Bgra32 texel = Texel(r * k, g * k, b * k);
            for (int column = 0; column < BandTexture; column++)
            {
                _bandTexels[(row * BandTexture) + column] = texel;
            }
        }

        AddBandNoise();
        _device.UpdateTexture(_bandStripes, 0, _bandTexels);

        // BandFilter_MakeEmptyTexture: the colour in full.
        Array.Fill(_bandTexels, Texel(r * 255f, g * 255f, b * 255f));
        AddBandNoise();
        _device.UpdateTexture(_bandPlain, 0, _bandTexels);

        float band = height * 0.05f;
        List<float> rows = [0f, _bandMove > 45 ? band * 0.5f : 0f];
        for (float y = rows[1] + band; y < height; y += band)
        {
            rows.Add(y);
        }

        rows.Add(height);
        for (int i = 0; i + 1 < rows.Count; i++)
        {
            Tile(batch, i % 2 == 1 ? _bandStripes : _bandPlain, rows[i], rows[i + 1], width);
        }
    }

    // A share as the half a MODULATE2X multiplies by it.
    private static byte Half(float share) => (byte)Math.Clamp((int)(Math.Clamp(share, 0f, 1f) * 127.5f), 0, 255);

    // The picture times (one plus the texel), drawn as MODULATE2X of half of one plus it.
    private static Bgra32 Texel(float r, float g, float b) =>
        Bgra32.FromRgba((byte)((255 + (int)r) / 2), (byte)((255 + (int)g) / 2), (byte)((255 + (int)b) / 2), 255);

    // A band of the screen tiled with the texture a pixel a texel, as its wrapping coordinates lay it.
    private static void Tile(SpriteBatch batch, TextureHandle texture, float top, float bottom, int width)
    {
        Bgra32 white = Bgra32.FromRgba(255, 255, 255, 255);
        for (float y = top; y < bottom; y += BandTexture)
        {
            float rowBottom = Math.Min(bottom, y + BandTexture), v = (rowBottom - y) / BandTexture;
            for (float x = 0f; x < width; x += BandTexture)
            {
                float right = Math.Min(width, x + BandTexture), u = (right - x) / BandTexture;
                batch.Draw(texture, new RectF(x, y, right - x, rowBottom - y), new RectF(0f, 0f, u, v), white, BlendMode.Modulate2X);
            }
        }
    }

    // BandFilter_AddNoiseToBandTexture: as many texels as the noise is of 256, each at random, darkened by a random share.
    private void AddBandNoise()
    {
        int count = (int)(_bandNoise * 256f);
        for (int i = 0; i < count; i++)
        {
            int at = (int)(_filterRandom.NextDouble() * 256.0);
            double keep = _filterRandom.NextDouble();
            Bgra32 texel = _bandTexels[at];

            // The texel stands for one plus the colour; the noise scales the colour's part.
            _bandTexels[at] = Bgra32.FromRgba(Dim(texel.R, keep), Dim(texel.G, keep), Dim(texel.B, keep), 255);
        }
    }

    private static byte Dim(byte half, double keep) => (byte)((255 + (int)(((half * 2) - 255) * keep)) / 2);

    // BandFilter_RunHandler, once a tick: the noise dies away by at most four rolls' worth, the roll turns, and at the noise's
    // time it flares again to a random share of the camera's noise and waits a random part of its loop.
    private void RunBand(Igi2TitlePlayer title, Igi2CutsceneView view)
    {
        int tick = title.Level?.LevelFlow?.Tick ?? 0;
        if (view.IsCameraJustChanged || _bandTick < 0 || tick < _bandTick)
        {
            _bandNoise = 0f;
            _bandMove = 0;
            _bandNoiseTimer = BandNoiseLoop;
            _bandNoiseLoop = BandNoiseLoop;
            _bandTick = tick;
        }

        for (; _bandTick < tick; _bandTick++)
        {
            _bandNoise += Math.Max(-_bandNoise, -4f / BandMoveLoop);
            _bandMove = _bandMove >= BandMoveLoop ? 0 : _bandMove + 1;
            if (_bandNoiseTimer >= _bandNoiseLoop)
            {
                _bandNoiseTimer = (int)(_filterRandom.NextDouble() * _bandNoiseLoop * 0.699999988079071);
                _bandNoise = (float)(_filterRandom.NextDouble() * view.FilterNoise);
            }
            else
            {
                _bandNoiseTimer++;
            }
        }
    }

    // HandiCamFilter_D3DDraw: four corner brackets at a tenth and nine tenths across and a fifth and four fifths down, each arm fifty
    // pixels long, drawn twice a pixel apart.
    private static void DrawHandiCam(SpriteBatch batch, int width, int height)
    {
        float x0 = width * 0.10f, x1 = width * 0.90f, y0 = height * 0.20f, y1 = height * 0.80f;
        const float arm = 50f, stroke = 2f;
        Bgra32 white = Bgra32.White;
        Rectangle(batch, x0, y0, x0 + arm, y0 + stroke, white);
        Rectangle(batch, x0, y0, x0 + stroke, y0 + arm, white);
        Rectangle(batch, x1 - arm, y0, x1, y0 + stroke, white);
        Rectangle(batch, x1 - stroke, y0, x1, y0 + arm, white);
        Rectangle(batch, x0, y1 - stroke, x0 + arm, y1, white);
        Rectangle(batch, x0, y1 - arm, x0 + stroke, y1, white);
        Rectangle(batch, x1 - arm, y1 - stroke, x1, y1, white);
        Rectangle(batch, x1 - stroke, y1 - arm, x1, y1, white);
    }

    private static void Rectangle(SpriteBatch batch, float left, float top, float right, float bottom, Bgra32 colour) =>
        batch.DrawQuad(
            TextureHandle.None,
            new Vertex2D(left, top, 0f, 0f, colour),
            new Vertex2D(right, top, 1f, 0f, colour),
            new Vertex2D(right, bottom, 1f, 1f, colour),
            new Vertex2D(left, bottom, 0f, 1f, colour));
}
