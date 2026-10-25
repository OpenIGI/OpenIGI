using OpenIGI.Core.Graphics;
using OpenIGI.Game.Menus;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.Menus;

/// <summary>Draws the menu manager's turning green overlay, <c>BackgroundFX</c>.</summary>
/// <remarks>
/// The geometry and its motion are <see cref="BackgroundFxTask"/>'s; this is only the submission -- the
/// two passes, the grids, the segments and the polygonal circles -- mapped to the screen at the menu's
/// current magnification.
/// </remarks>
internal static class MenuBackgroundFx
{
    /// <summary>Draws the turning green overlay.</summary>
    /// <remarks>
    /// <c>BackgroundFX_Render</c> (<c>0x419820</c>) draws the whole effect <b>twice</b>, at the
    /// current frame count and at one past it. Two faint passes a frame apart are what give the lines
    /// their doubled edge; one pass at double the alpha does not look the same.
    /// </remarks>
    public static void Draw(SpriteBatch batch, BackgroundFxTask? fx, int screenWidth, int screenHeight, int scale)
    {
        if (fx is null)
        {
            return;
        }

        DrawPass(batch, fx.Time, screenWidth, screenHeight, scale);
        DrawPass(batch, fx.Time + 1f, screenWidth, screenHeight, scale);
    }

    private static void DrawPass(SpriteBatch batch, float time, int screenWidth, int screenHeight, int scale)
    {
        Bgra32 lineColour = Bgra32.FromRgba(0, byte.MaxValue, 0, MenuRenderer.Alpha(BackgroundFxTask.LineAlpha));
        Bgra32 circleColour = Bgra32.FromRgba(0, byte.MaxValue, 0, MenuRenderer.Alpha(BackgroundFxTask.CircleAlpha));

        foreach (BackgroundFxLayer layer in BackgroundFxTask.Layers)
        {
            BackgroundFxTransform m = BackgroundFxTask.GetTransform(layer, time);

            if (layer.Grid is { } grid)
            {
                DrawFxGrid(batch, grid, m, lineColour, screenWidth, screenHeight, scale);
            }

            foreach (BackgroundFxLine line in layer.Lines)
            {
                (float ax, float ay) = m.Apply(line.X0, line.Y0);
                (float bx, float by) = m.Apply(line.X1, line.Y1);
                DrawFxSegment(batch, ax, ay, bx, by, lineColour, screenWidth, screenHeight, scale);
            }

            foreach (BackgroundFxCircle circle in layer.Circles)
            {
                DrawFxCircle(batch, circle, m, circleColour, screenWidth, screenHeight, scale);
            }
        }
    }

    /// <summary>Draws a circle the way the original does: a closed loop of straight segments.</summary>
    /// <remarks>
    /// The count is <c>(int)(radius * pi / 10)</c> capped at forty, so a small circle is visibly a
    /// polygon and a large one is not. Reproduced rather than smoothed.
    /// </remarks>
    private static void DrawFxCircle(
        SpriteBatch batch,
        BackgroundFxCircle circle,
        in BackgroundFxTransform m,
        Bgra32 colour,
        int screenWidth,
        int screenHeight,
        int scale)
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
                screenHeight,
                scale);
        }
    }

    private static void DrawFxGrid(
        SpriteBatch batch,
        in BackgroundFxGrid grid,
        in BackgroundFxTransform m,
        Bgra32 colour,
        int screenWidth,
        int screenHeight,
        int scale)
    {
        // Centred on the origin, stepping five percent of the extent per line, count + 1 lines.
        float left = grid.OriginX - (grid.Width * 0.5f);
        float top = grid.OriginY - (grid.Height * 0.5f);

        for (int i = 0; i <= grid.Columns; i++)
        {
            float x = left + (grid.Width * BackgroundFxGrid.Step * i);
            (float ax, float ay) = m.Apply(x, top);
            (float bx, float by) = m.Apply(x, top + grid.Height);
            DrawFxSegment(batch, ax, ay, bx, by, colour, screenWidth, screenHeight, scale);
        }

        for (int i = 0; i <= grid.Rows; i++)
        {
            float y = top + (grid.Height * BackgroundFxGrid.Step * i);
            (float ax, float ay) = m.Apply(left, y);
            (float bx, float by) = m.Apply(left + grid.Width, y);
            DrawFxSegment(batch, ax, ay, bx, by, colour, screenWidth, screenHeight, scale);
        }
    }

    /// <summary>Draws one line of the overlay as a thin quad.</summary>
    /// <remarks>
    /// The original submits a two-vertex line primitive. A one-pixel quad along the same span is as
    /// close as this batch gets without a line pipeline, and at these alphas the difference does not
    /// show.
    /// </remarks>
    private static void DrawFxSegment(
        SpriteBatch batch,
        float ax,
        float ay,
        float bx,
        float by,
        Bgra32 colour,
        int screenWidth,
        int screenHeight,
        int scale)
    {
        (int x0, int y0) = MenuLayout.ToScreen((int)ax, (int)ay, screenWidth, screenHeight, scale);
        (int x1, int y1) = MenuLayout.ToScreen((int)bx, (int)by, screenWidth, screenHeight, scale);

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
}
