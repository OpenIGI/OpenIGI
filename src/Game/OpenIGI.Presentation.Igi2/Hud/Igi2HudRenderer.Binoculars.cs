using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Rendering;
using OpenIGI.Game.Igi2;
using OpenIGI.Presentation.Igi2.World;
using OpenIGI.Presentation.Rendering;

namespace OpenIGI.Presentation.Igi2.Hud;

/// <content>The binoculars' overlay, <c>BinocularOverlay_Draw</c> (<c>igi2 0x525FA0</c>).</content>
public sealed partial class Igi2HudRenderer
{
    /// <summary>The overlay's frame, one half of it mirrored across the screen.</summary>
    public const string BinocularSprite = "binoculars.spr";

    /// <summary>The frame while the view is infrared.</summary>
    public const string InfraredSprite = "infravision.spr";

    /// <summary>The range line's text key.</summary>
    public const string RangeKey = "weapons/strings/Binoculars/range";

    // binocularoverlay.c BinocularOverlay_atBox: a unit square, closed.
    private static readonly (float X, float Y)[] MarkBox = [(-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f), (-1f, -1f)];

    // BinocularOverlay_CreateHandler's compass letters, a quarter turn apart from the strip's start.
    private static readonly string[] CompassLetters = ["W", "N", "E", "S"];

    // QSprite colours run to 128 for full: half green for the compass, full green, half red, full blue for the marks.
    private static readonly Bgra32 CompassGreen = Bgra32.FromRgba(0, 128, 0, 255);
    private static readonly Bgra32 FriendlyMark = Bgra32.FromRgba(0, 255, 0, 255);
    private static readonly Bgra32 EnemyMark = Bgra32.FromRgba(128, 0, 0, 255);

    private string _range = string.Empty;
    private long _rangeMetres = long.MinValue;

    /// <summary>The overlay the player's zoom has up, drawn over the view as its status display draws it.</summary>
    private void DrawZoomOverlay(SpriteBatch batch, Igi2TitlePlayer title, float width, float height)
    {
        if (title.Human.IsDead)
        {
            return;
        }

        switch (title.ZoomOverlay)
        {
            // The remastered binoculars are the host's, drawn for whichever player looks through them.
            case Igi2TitlePlayer.BinocularOverlayType when RemasteredHudPresentation.UsesRemasteredTitleOptics:
                break;
            case Igi2TitlePlayer.BinocularOverlayType:
                DrawBinoculars(batch, title, width, height);
                break;
            case Igi2TitlePlayer.LaserDesignatorOverlayType:
                DrawLaserDesignator(batch, title, width, height);
                break;
            case { } scope:
                DrawScope(batch, title, scope, width, height);
                break;
        }
    }

    private void DrawBinoculars(SpriteBatch batch, Igi2TitlePlayer title, float width, float height)
    {
        bool infrared = title.ScreenOverlay?.IsInfrared == true;
        DrawBinocularFrame(batch, infrared ? InfraredSprite : BinocularSprite, width, height);
        if (infrared)
        {
            return;
        }

        DrawRange(batch, title, width, height);
        DrawCompass(batch, title, width, height);
        DrawMarks(batch, title, width, height);
    }

    /// <summary>The frame: its sprite stretched over the left half and mirrored over the right, a sprite pixel past each edge.</summary>
    private void DrawBinocularFrame(SpriteBatch batch, string name, float width, float height)
    {
        if (_assets?.Get(name) is not { } sprite)
        {
            return;
        }

        SpriteFrame frame = sprite.Asset.Frames[0];
        float edge = width / frame.Width, half = width * 0.5f;
        TextureHandle texture = sprite.Textures[frame.TextureIndex];
        Bgra32 white = Bgra32.White;
        batch.DrawQuad(texture,
            new Vertex2D(-edge * _scale, -edge * _scale, frame.U0, frame.V0, white),
            new Vertex2D(half * _scale, -edge * _scale, frame.U1, frame.V0, white),
            new Vertex2D(half * _scale, (height + edge) * _scale, frame.U1, frame.V1, white),
            new Vertex2D(-edge * _scale, (height + edge) * _scale, frame.U0, frame.V1, white));
        batch.DrawQuad(texture,
            new Vertex2D(half * _scale, -edge * _scale, frame.U1, frame.V0, white),
            new Vertex2D((width + edge) * _scale, -edge * _scale, frame.U0, frame.V0, white),
            new Vertex2D((width + edge) * _scale, (height + edge) * _scale, frame.U0, frame.V1, white),
            new Vertex2D(half * _scale, (height + edge) * _scale, frame.U1, frame.V1, white));
    }

    /// <summary>The range to what the view is trained on, in whole metres, or a thousand and more.</summary>
    private void DrawRange(SpriteBatch batch, Igi2TitlePlayer title, float width, float height)
    {
        float lineHeight = _font?.LineHeight ?? 0;
        Text(batch, RangeText(title), MathF.Floor(width * 0.46875f), MathF.Floor((height * 0.7291667f) - lineHeight), Bgra32.White);
    }

    /// <summary>The range line, <c>"%s: %0.fm"</c>, or a thousand and more with nothing in reach.</summary>
    private string RangeText(Igi2TitlePlayer title)
    {
        long metres = title.ZoomRange is { } range ? (long)Math.Round(range / OpenIGI.Game.World.WorldScene.UnitsPerMetre) : -1;
        if (metres != _rangeMetres)
        {
            _rangeMetres = metres;
            string label = title.Strings?.Get(RangeKey) ?? RangeKey;
            _range = metres < 0
                ? string.Create(CultureInfo.InvariantCulture, $"{label}: 1000+m")
                : string.Create(CultureInfo.InvariantCulture, $"{label}: {metres}m");
        }

        return _range;
    }

    /// <summary>
    /// The compass (<c>BinocularOverlay_DrawCompass</c>): sixty-four ticks across a strip, the heading setting where the first
    /// falls, every sixteenth long and lettered above and below, every eighth longer than the rest, wrapping round; and a notch
    /// at the strip's middle, top and bottom.
    /// </summary>
    private void DrawCompass(SpriteBatch batch, Igi2TitlePlayer title, float width, float height) =>
        DrawCompass(batch, title, width * 0.625f, height * 0.7916667f, width * 0.28125f, CompassGreen, Bgra32.White);

    private void DrawCompass(SpriteBatch batch, Igi2TitlePlayer title, float left, float top, float span, Bgra32 ticks, Bgra32 letters)
    {
        Matrix3f view = title.Human.View.Orientation;
        float heading = Igi2Pose.DirectionGamma(new Vector3f(view.M12, view.M22, view.M32));
        if (heading < 0f)
        {
            heading += MathF.Tau;
        }

        float lineHeight = _font?.LineHeight ?? 0;
        float strip = (lineHeight * 2f) + 26f, middle = top + (strip * 0.5f), pitch = span * 0.015625f;
        float x = span * heading * 0.15915494f;
        for (int i = 0; i < 64; i++)
        {
            float length = (i & 15) == 0 ? 12f : (i & 7) == 0 ? 8f : 4f;
            float tick = x + left;
            Line(batch, tick, middle - (length * 0.5f), tick, middle + (length * 0.5f), ticks);
            if ((i & 15) == 0)
            {
                string letter = CompassLetters[i >> 4];
                float letterWidth = _font?.Measure(letter).Width ?? 0f;
                float letterX = MathF.Floor(tick - (letterWidth * 0.5f));
                Text(batch, letter, letterX, MathF.Floor(middle - (length * 0.5f) - lineHeight - 2f), letters);
                Text(batch, letter, letterX, MathF.Floor(middle + (length * 0.5f) + 2f), letters);
            }

            x += pitch;
            if (x >= span)
            {
                x -= span;
            }
        }

        float centre = left + (span * 0.5f);
        Triangle(batch, (centre, top + 4f), (centre - 4f, top), (centre + 4f, top), ticks);
        Triangle(batch, (centre, top + strip - 4f), (centre - 4f, top + strip), (centre + 4f, top + strip), ticks);
    }

    /// <summary>
    /// The people the binoculars pick out, those in sight (<c>BinocularOverlay_TrackMovement</c>): a box over each, as wide as
    /// they are on the screen, that closes in from eleven times its size and turns a quarter turn over the quarter second after
    /// they are first picked out; red for the enemy, green for the rest.
    /// </summary>
    private void DrawMarks(SpriteBatch batch, Igi2TitlePlayer title, float width, float height)
    {
        Vector3d eye = title.Human.View.Position;
        Matrix3f view = title.Human.View.Orientation;
        float tanX = title.Human.View.FieldOfView, tanY = tanX * height / width;
        if (!(tanX > 0f))
        {
            return;
        }

        float scaleX = width * 0.5f / tanX, scaleY = height * 0.5f / tanY;
        long now = title.BinocularClock;
        foreach (Igi2BinocularMark? mark in title.BinocularMarks)
        {
            if (mark is not { IsActive: true, IsOccluded: false })
            {
                continue;
            }

            Vector3d at = mark.Body.Position;
            double dx = at.X - eye.X, dy = at.Y - eye.Y, dz = at.Z - eye.Z;
            double ahead = (dx * view.M12) + (dy * view.M22) + (dz * view.M32);
            if (ahead <= 0.0)
            {
                continue;
            }

            double across = (dx * view.M11) + (dy * view.M21) + (dz * view.M31);
            double up = (dx * view.M13) + (dy * view.M23) + (dz * view.M33);
            float sx = (width * 0.5f) + (float)(across / ahead * scaleX);
            float sy = (height * 0.5f) - (float)(up / ahead * scaleY);
            float t = Math.Clamp(now - mark.FirstSeen, 0, 250) * 0.004f;
            float grow = ((1f - t) * 10f) + 1f, turn = MathF.PI * 0.5f * t;
            float radius = mark.Body.ModelRadius;
            float boxX = (float)(scaleX * radius / ahead) * grow, boxY = (float)(scaleY * radius / ahead) * grow;
            float cos = MathF.Cos(turn), sin = MathF.Sin(turn);
            Bgra32 colour = mark.Body.IsDead ? Bgra32.FromRgba(0, 0, 255, 255) : mark.IsFriendly ? FriendlyMark : EnemyMark;
            for (int i = 0; i + 1 < MarkBox.Length; i++)
            {
                (float ax, float ay) = Turn(MarkBox[i], boxX, boxY, cos, sin);
                (float bx, float by) = Turn(MarkBox[i + 1], boxX, boxY, cos, sin);
                Line(batch, sx + ax, sy + ay, sx + bx, sy + by, colour);
            }
        }

        static (float X, float Y) Turn((float X, float Y) point, float sx, float sy, float cos, float sin)
        {
            float x = point.X * sx, y = point.Y * sy;
            return ((x * cos) - (y * sin), (x * sin) + (y * cos));
        }
    }

    private void Line(SpriteBatch batch, float x1, float y1, float x2, float y2, Bgra32 colour, float thickness = 1f) =>
        Stroke(batch, new Igi2SightLine(x1 * _scale, y1 * _scale, x2 * _scale, y2 * _scale, 1f), MathF.Max(1f, thickness * _scale), colour);

    private void Triangle(SpriteBatch batch, (float X, float Y) a, (float X, float Y) b, (float X, float Y) c, Bgra32 colour) =>
        batch.DrawQuad(TextureHandle.None,
            new Vertex2D(a.X * _scale, a.Y * _scale, 0f, 0f, colour),
            new Vertex2D(b.X * _scale, b.Y * _scale, 0f, 0f, colour),
            new Vertex2D(c.X * _scale, c.Y * _scale, 0f, 0f, colour),
            new Vertex2D(c.X * _scale, c.Y * _scale, 0f, 0f, colour));
}
