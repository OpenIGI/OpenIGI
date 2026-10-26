using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Game.Computer;
using OpenIGI.Game.Igi1.Computer;
using OpenIGI.Game.Player;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.World;

/// <content>The map's own layers: the objects' marks, a selected camera's view, the interference and the boot box.</content>
internal sealed partial class MapComputerRenderer
{
    /// <summary>
    /// The marker colours as the vertex batch packs them (<c>0x50D4C0</c>): the table's 255s are 1/128
    /// over one, so each channel wraps to 252 and carries one into the next, the alpha 0.75 included.
    /// </summary>
    private static readonly Bgra32[] MarkerColours =
    [
        Bgra32.FromRgba(252, 0, 0, 192),
        Bgra32.FromRgba(253, 252, 0, 192),
        Bgra32.FromRgba(1, 252, 0, 191),
        Bgra32.FromRgba(128, 252, 127, 191),
        Bgra32.FromRgba(0, 1, 252, 191),
    ];

    // 0x467970 and the handlers it calls, projected through the camera the picture was drawn from.
    private void DrawMarks(SpriteBatch batch, Igi1Computer computer, ComputerMap map)
    {
        Projection p = new(map.CameraPosition, map.CameraFieldOfView, computer.Width, computer.Height, computer.PixelAspect);
        foreach (ComputerMark mark in map.Marks)
        {
            Vector3f v = p.ToView(mark.Position);
            (float x, float y) = p.Project(v);
            switch (mark.Kind)
            {
                case ComputerMarkKind.Marker:
                    if (mark.Culled && OutsideEye(v, mark.Size, computer))
                    {
                        break;
                    }

                    DrawMarker(batch, computer, mark, x, y, v.Z);
                    break;
                case ComputerMarkKind.Sprite when Frame(mark.Text, mark.Frame) is { } frame:
                    float sx = x - (frame.Sprite.Width * 0.5f), sy = y - (frame.Sprite.Height * 0.5f);
                    DrawFrame(batch, Animated(frame, mark), mark.Nudged ? sx - 10f : sx, sy, Unmodified);
                    break;
                case ComputerMarkKind.Label:
                    // 0x4680B0: from the point made whole, wrapped at 128 pixels, in red.
                    _labelWords.Clear();
                    ComputerTextWrap.Wrap(this, (int)x, (int)y, mark.Text, Igi1Computer.TextFont, 128, _labelWords);
                    (float r, float g, float b) = ComputerColors.Items[0];
                    foreach (ComputerWord word in _labelWords)
                    {
                        DrawText(batch, Igi1Computer.TextFont, word.Text, word.X, word.Y, ItemColour(r, g, b));
                    }

                    break;
            }
        }

        if (map.ViewCone is { } cone)
        {
            DrawCone(batch, p, cone);
        }
    }

    // 0x467800, called back for each rigid object the map's view draws (0x467900): the line between its model's magic
    // vertices 10 and 11 (MVTX, MESH+136), white for a door and half that for anything else. Only a model authored with
    // more than ten has one.
    private void DrawOutlines(SpriteBatch batch, Igi1Computer computer, ComputerMap map)
    {
        if (Scene is not { } scene)
        {
            return;
        }

        Projection p = new(map.CameraPosition, map.CameraFieldOfView, computer.Width, computer.Height, computer.PixelAspect);
        Bgra32 door = Bgra32.FromRgba(255, 255, 255, 255), other = Bgra32.FromRgba(127, 127, 127, 255);
        foreach (WorldPlacement placement in scene.Placements)
        {
            if (placement.IsSuppressed
                || placement.Model.MagicVertexCount <= 10
                || !placement.Model.TryGetMagicVertex(10, out Vector3f m10)
                || !placement.Model.TryGetMagicVertex(11, out Vector3f m11))
            {
                continue;
            }

            Vector3d origin = WorldScene.ToUnits(placement.Position);
            Vector3d a = origin + placement.Orientation.Transposed().Transform(Widen(m10));
            Vector3d b = origin + placement.Orientation.Transposed().Transform(Widen(m11));
            Vector3f va = p.ToView(a), vb = p.ToView(b);
            if (va.Z <= 0f || vb.Z <= 0f)
            {
                continue;
            }

            (float ax, float ay) = p.Project(va);
            (float bx, float by) = p.Project(vb);
            if (Math.Max(ax, bx) < 0f || Math.Min(ax, bx) > computer.Width || Math.Max(ay, by) < 0f || Math.Min(ay, by) > computer.Height)
            {
                continue;
            }

            Bgra32 colour = placement.Owner is DeclaredTask { Type.Name: "Door" } ? door : other;
            Line(batch, ax, ay, bx, by, colour, colour);
        }
    }

    private static Vector3d Widen(Vector3f v) => new(v.X, v.Y, v.Z);

    // A lens's frames turn every eighth tick through as many as its camera sprite has.
    private static SpriteFrameRef Animated(SpriteFrameRef frame, in ComputerMark mark)
    {
        int count = frame.Resource.Asset.Frames.Count;
        return mark.Frame <= 0 || count <= 1 ? frame : frame with { Frame = frame.Resource.Asset.Frames[mark.Frame % count] };
    }

    // 0x467F30: the shape's outline, scaled by the size over the depth at the PLAYER's view scale (the current view's
    // TransContext, 0x498040), turned by minus the heading.
    private void DrawMarker(SpriteBatch batch, Igi1Computer computer, in ComputerMark mark, float x, float y, float depth)
    {
        if (mark.Shape < 0 || mark.Shape >= ComputerMarkShapes.Shapes.Length || mark.Color < 0 || mark.Color >= MarkerColours.Length)
        {
            return;
        }

        float eyeScale = computer.Width * 0.5f / HumanView.DefaultTanX;
        float scaleX = mark.Size * eyeScale / depth, scaleY = computer.PixelAspect * scaleX;
        float angle = -mark.Yaw, c = MathF.Cos(angle), s = MathF.Sin(angle);
        (float X, float Y)[] points = ComputerMarkShapes.Shapes[mark.Shape];
        Bgra32 colour = MarkerColours[mark.Color];
        for (int i = 0; i + 1 < points.Length; i++)
        {
            float x0 = scaleX * points[i].X, y0 = scaleY * points[i].Y;
            float x1 = scaleX * points[i + 1].X, y1 = scaleY * points[i + 1].Y;
            if (angle != 0f)
            {
                (x0, y0) = ((x0 * c) - (y0 * s), (x0 * s) + (y0 * c));
                (x1, y1) = ((x1 * c) - (y1 * s), (x1 * s) + (y1 * c));
            }

            Line(batch, x0 + x, y0 + y, x1 + x, y1 + y, colour, colour);
        }
    }

    // 0x4D03D0 under the player's view, which is the one current as the menu draws: a soldier off its edges is not marked.
    private static bool OutsideEye(Vector3f v, float radius, Igi1Computer computer)
    {
        float tanX = HumanView.DefaultTanX, tanY = tanX * computer.Height / computer.Width;
        return v.Z + radius <= 0f
            || MathF.Abs(v.X) - radius > v.Z * tanX
            || MathF.Abs(v.Y) - radius > v.Z * tanY;
    }

    // 0x442DD0: a fan of eleven red triangles at 0.35 from the lens through the ends of its twelve rays.
    private void DrawCone(SpriteBatch batch, Projection p, in ComputerViewCone cone)
    {
        Bgra32 red = Bgra32.FromRgba(255, 0, 0, 89);
        (float ox, float oy) = p.Project(p.ToView(cone.Origin));
        for (int i = 1; i < cone.Ends.Length; i++)
        {
            (float ax, float ay) = p.Project(p.ToView(cone.Ends[i - 1]));
            (float bx, float by) = p.Project(p.ToView(cone.Ends[i]));
            batch.DrawQuad(
                TextureHandle.None,
                new Vertex2D(ox * _scale, oy * _scale, 0f, 0f, red),
                new Vertex2D(ax * _scale, ay * _scale, 0f, 0f, red),
                new Vertex2D(bx * _scale, by * _scale, 0f, 0f, red),
                new Vertex2D(bx * _scale, by * _scale, 0f, 0f, red));
        }
    }

    // 0x4A2CD0: a black line at 0x4C every four pixels down, then level x 10 grey streaks a row of up to fifty pixels, their
    // alpha the level; under one a row, one row every 1 / (level x 10).
    private void DrawNoise(SpriteBatch batch, float level, int width, int height)
    {
        Bgra32 line = Bgra32.FromRgba(0, 0, 0, 0x4C);
        for (float y = 0f; !(height < y + (4f * _scale)); y += 4f * _scale)
        {
            batch.FillRect(new RectF(0f, MathF.Floor(y), width, _scale), line);
        }

        if (!(level > 0f))
        {
            return;
        }

        float streaks = level * 10f, step = 1f;
        if (streaks < 1f)
        {
            step = 1f / streaks;
            streaks = 1f;
        }

        int perRow = (int)streaks;
        byte alpha = (byte)Math.Clamp((int)(level * 255f), 0, 255);
        float y0 = 0f;
        for (float next = step; !(height / _scale < next); y0 = next, next += step)
        {
            float row = MathF.Floor(y0);
            for (int k = 0; k < perRow; k++)
            {
                float x = (float)_filterRandom.NextDouble() * (width / _scale);
                byte grey = (byte)Math.Clamp((int)((float)_filterRandom.NextDouble() * 255f), 0, 255);
                float right = Math.Min(width / _scale, ((float)_filterRandom.NextDouble() * 50f) + x);
                batch.FillRect(new RectF(x * _scale, row * _scale, (right - x) * _scale, _scale), Bgra32.FromRgba(grey, grey, grey, alpha));
            }
        }
    }

    // 0x466480 and 0x466CF0: black under the first line, the line's text in font 3 above a box half the display wide, the bar
    // filling it as the line runs.
    private void DrawStartup(SpriteBatch batch, Igi1Computer computer, ComputerStartup startup)
    {
        if (!startup.IsRunning)
        {
            return;
        }

        float w = (int)computer.Width, h = (int)computer.Height;
        if (startup.Step == 0)
        {
            batch.FillRect(new RectF(0f, 0f, w * _scale, h * _scale), Bgra32.FromRgba(0, 0, 0, 255));
        }

        Bgra32 text = Pack(startup.TextColor), bar = Pack(startup.BarColor);
        float barH = h * 0.025f, halfW = w * 0.5f, left = w * 0.15f, top = h * 0.65f;
        int bw = (int)halfW, bh = (int)barH;
        Line(batch, left, top, left + bw, top, text, text);
        Line(batch, left, top + bh, left, top, text, text);
        Line(batch, left + bw, top + bh, left, top + bh, text, text);
        Line(batch, left + bw, top, left + bw, top + bh, text, text);
        float x1 = left + 1f, y1 = top + 1f, y2 = top + barH - 0.5f;
        float x2 = (startup.Progress * (halfW - 1f)) + left + 0.5f;
        batch.FillRect(new RectF(x1 * _scale, y1 * _scale, Math.Max(0f, x2 - x1) * _scale, Math.Max(0f, y2 - y1) * _scale), bar);

        string line = computer.Text(startup.Lines[startup.Step]);
        float ty = top - (ComputerFont("font3.fnt")?.LineHeight ?? 16) - 2f;
        (float r, float g, float b) = startup.TextColor;
        DrawText(batch, ComputerFont("font3.fnt"), line, left, ty, ItemColour(r * 127f, g * 127f, b * 127f));
    }

    // The boot box's colours, a channel times 255 made whole.
    private static Bgra32 Pack((float R, float G, float B) c) =>
        Bgra32.FromRgba((byte)(int)(c.R * 255f), (byte)(int)(c.G * 255f), (byte)(int)(c.B * 255f), 255);

    /// <summary>The map camera's projection (<c>0x4675B0</c>, <c>0x467620</c>), in display pixels.</summary>
    private readonly struct Projection(Vector3d centre, float zoom, float width, float height, float aspect)
    {
        public Vector3f ToView(Vector3d at)
        {
            Matrix3f m = ComputerMap.View;
            float dx = (float)(at.X - centre.X), dy = (float)(at.Y - centre.Y), dz = (float)(at.Z - centre.Z);
            return new Vector3f(
                (dz * m.M31) + (dy * m.M21) + (dx * m.M11),
                (dz * m.M32) + (dy * m.M22) + (dx * m.M12),
                (dz * m.M33) + (dy * m.M23) + (dx * m.M13));
        }

        public (float X, float Y) Project(Vector3f v)
        {
            float s = width * 0.5f / (v.Z * zoom);
            return ((width * 0.5f) + (s * v.X), (aspect * v.Y * s) + (height * 0.5f));
        }
    }
}
