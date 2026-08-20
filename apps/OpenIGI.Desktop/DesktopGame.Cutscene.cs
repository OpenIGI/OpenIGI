using OpenIGI.Core.Graphics;
using OpenIGI.Game.World;
using OpenIGI.Engine.Tasks;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

internal sealed partial class DesktopGame
{
    private static bool HasAuthoredCutscene(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            if (child is CutSceneTask || HasAuthoredCutscene(child))
            {
                return true;
            }
        }

        return false;
    }

    private void DrawCutsceneFraming(SpriteBatch batch, CutsceneFrame frame)
    {
        if (_window.Width <= 0 || _window.Height <= 0)
        {
            // A minimized/hidden window reports 0x0; a zero-sized projection faults the renderer, so
            // defer the letterbox until a real size arrives.
            return;
        }

        float width = _window.Width;
        float height = _window.Height;
        _device.Set2DProjection(_window.Width, _window.Height);
        batch.Begin();

        static void Quad(SpriteBatch target, float left, float top, float right, float bottom, Bgra32 colour) =>
            target.DrawQuad(
                TextureHandle.None,
                new Vertex2D(left, top, 0f, 0f, colour),
                new Vertex2D(right, top, 1f, 0f, colour),
                new Vertex2D(right, bottom, 1f, 1f, colour),
                new Vertex2D(left, bottom, 0f, 1f, colour));

        string filter = frame.Filter ?? string.Empty;
        bool handicam = filter.Contains("HAND", StringComparison.OrdinalIgnoreCase);
        if (filter.Contains("LINE", StringComparison.OrdinalIgnoreCase) || handicam)
        {
            // DrawLineFilter (0x4A2CD0) and DrawHandiCamFilter (0x4A4300) pack
            // 0x4CRRGGBB: authored RGB with the fixed alpha 0x4C. They submit a one-pixel
            // horizontal row every four physical viewport pixels; Noise never changes these rows.
            Bgra32 line = new(
                Channel(frame.FilterColor.Z),
                Channel(frame.FilterColor.Y),
                Channel(frame.FilterColor.X),
                0x4C);
            for (float y = 0f; y < height; y += 4f)
            {
                Quad(batch, 0f, y, width, Math.Min(height, y + 1f), line);
            }

            if (!handicam && frame.FilterNoise > 0f)
            {
                DrawLineNoise(batch, frame, width, height, Quad);
            }
        }
        else if (filter.Contains("BAND", StringComparison.OrdinalIgnoreCase))
        {
            float band = ((frame.Scene.Ticks * 3) % Math.Max(1f, height + 80f)) - 40f;
            Quad(batch, 0f, band, width, band + 28f, new Bgra32(255, 255, 255, 18));
        }

        if (handicam)
        {
            // sub_4A4300 places the four bracket origins at (10%,20%), (90%,20%),
            // (10%,80%) and (90%,80%), then extends each arm by 50 authored pixels.
            float x0 = width * 0.10f;
            float x1 = width * 0.90f;
            float y0 = height * 0.20f;
            float y1 = height * 0.80f;
            // The routine uses physical viewport coordinates. Each arm is exactly 50 pixels and is
            // submitted twice, offset by one pixel, making the visible stroke two pixels wide.
            const float arm = 50f;
            const float stroke = 2f;
            Bgra32 white = new(255, 255, 255, 255);

            Quad(batch, x0, y0, x0 + arm, y0 + stroke, white);
            Quad(batch, x0, y0, x0 + stroke, y0 + arm, white);
            Quad(batch, x1 - arm, y0, x1, y0 + stroke, white);
            Quad(batch, x1 - stroke, y0, x1, y0 + arm, white);
            Quad(batch, x0, y1 - stroke, x0 + arm, y1, white);
            Quad(batch, x0, y1 - arm, x0 + stroke, y1, white);
            Quad(batch, x1 - arm, y1 - stroke, x1, y1, white);
            Quad(batch, x1 - stroke, y1 - arm, x1, y1, white);

            _hud?.DrawHandicamTimecode(batch, frame, _window.Width, _window.Height);
        }

        float visibleHeight = Math.Clamp(frame.ViewportHeightFactor, 0f, 1f);
        if (visibleHeight < 1f)
        {
            float bars = ((1f - visibleHeight) * (height + 1f) * 0.5f) + 1f;
            Bgra32 black = Bgra32.FromRgb(0, 0, 0);
            Quad(batch, 0f, 0f, width, bars, black);
            Quad(batch, 0f, height - bars, width, height, black);
        }

        batch.End();
    }

    private static byte Channel(float value) =>
        (byte)Math.Clamp((int)(value * 255f), 0, 255);

    private static void DrawLineNoise(
        SpriteBatch batch,
        CutsceneFrame frame,
        float width,
        float height,
        Action<SpriteBatch, float, float, float, float, Bgra32> quad)
    {
        // The second half of 0x4A2CD0 distributes short monochrome streaks across the viewport.
        // Noise*10 determines streaks per row; below 0.1 it instead spaces single rows farther apart.
        float density = frame.FilterNoise * 10f;
        int streaks = Math.Max(1, (int)density);
        float rowStep = density < 1f ? 1f / Math.Max(density, 0.0001f) : 1f;
        byte alpha = Channel(frame.FilterNoise);
        uint random = unchecked((uint)((frame.Scene.Id * 0x9E3779B9) ^ frame.Scene.Ticks ^ 0xA341316C));

        static float Next(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00FFFFFF) / 16777216f;
        }

        for (float y = rowStep; y <= height; y += rowStep)
        {
            for (int i = 0; i < streaks; i++)
            {
                float x = Next(ref random) * width;
                float right = Math.Min(width, x + (Next(ref random) * 50f));
                byte grey = Channel(Next(ref random));
                quad(batch, x, y, right, Math.Min(height, y + 1f), new Bgra32(grey, grey, grey, alpha));
            }
        }
    }
}
