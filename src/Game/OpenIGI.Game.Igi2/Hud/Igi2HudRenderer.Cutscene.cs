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

    private static void DrawCutsceneFraming(SpriteBatch batch, Igi2TitlePlayer title, int width, int height)
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
                DrawBand(batch, title, width, height);
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

    // A faint band rolling down the picture, standing in for BandFilter's noise texture.
    private static void DrawBand(SpriteBatch batch, Igi2TitlePlayer title, int width, int height)
    {
        float band = (((title.Level?.LevelFlow?.Tick ?? 0) * 3) % Math.Max(1f, height + 80f)) - 40f;
        Rectangle(batch, 0f, band, width, band + 28f, new Bgra32(255, 255, 255, 18));
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
