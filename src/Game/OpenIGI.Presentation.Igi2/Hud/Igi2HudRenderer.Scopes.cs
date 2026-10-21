using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Rendering;
using OpenIGI.Game.Igi2;
using OpenIGI.Presentation.Igi2.World;

namespace OpenIGI.Presentation.Igi2.Hud;

/// <content>
/// The scopes' overlays (<c>sniperoverlay.c</c> and the AUG's, Dragunov's, G11's and PSG-1's), and the laser designator's
/// (<c>laserdesignatoroverlay.c</c>).
/// </content>
public sealed partial class Igi2HudRenderer
{
    /// <summary>A scope's frame, its top-left quarter mirrored into the other three.</summary>
    public const string ScopeCornerSprite = "snipersight.spr";

    /// <summary>The Dragunov's rangefinding reticle.</summary>
    public const string DragunovOpticsSprite = "dragunovAIM.spr";

    /// <summary>The G11's posts.</summary>
    public const string G11AimSprite = "g11sight.spr";

    /// <summary>The PSG-1's mil dots.</summary>
    public const string Psg1MilDotSprite = "psg1dot.spr";

    private static readonly Bgra32 Black = Bgra32.FromRgba(0, 0, 0, 255);
    private static readonly Bgra32 DesignatorGreen = Bgra32.FromRgba(77, 204, 77, 255);

    private string _lock = string.Empty;

    private void DrawScope(SpriteBatch batch, Igi2TitlePlayer title, string scope, float width, float height)
    {
        DrawCorners(batch, width, height);
        float cx = MathF.Floor(width * 0.5f), cy = MathF.Floor(height * 0.5f);
        DrawRing(batch, cx, cy, height * 0.4f, 48, height * 0.003f);
        switch (scope)
        {
            case "AugSniperOverlay":
                DrawAugReticle(batch, cx, cy, height);
                break;
            case "DragunovSniperOverlay":
                // DragunovSniperOverlay_Draw: the reticle hangs below the middle, a fifth of the width either side.
                float half = width * 0.2f;
                Quad(batch, DragunovOpticsSprite, (cx - half, height * 0.5f), (cx + half, height * 0.5f), (cx - half, cy + half), (cx + half, cy + half));
                break;
            case "G11SniperOverlay":
                DrawG11Reticle(batch, width, height);
                break;
            case "Psg1SniperOverlay":
                DrawPsg1Reticle(batch, width, height, cx, cy);
                break;
            default:
                DrawSniperReticle(batch, title, width, height, cx, cy);
                break;
        }
    }

    /// <summary><c>SniperOverlay_Draw</c>'s reticle, range and compass, all in black.</summary>
    private void DrawSniperReticle(SpriteBatch batch, Igi2TitlePlayer title, float width, float height, float cx, float cy)
    {
        float thick = MathF.Floor(height * 0.015f), post = MathF.Floor(height * 0.2f), ring = height * 0.4f;
        float right = ring + cx - (thick * 0.5f);
        Line(batch, right - post, cy, right, cy, Black, thick);
        float left = cx - ring + (thick * 0.5f);
        Line(batch, post + left, cy, left, cy, Black, thick);
        float bottom = ring + cy - (thick * 0.5f), top = bottom - post;
        Line(batch, cx, top, cx, bottom, Black, thick);
        Line(batch, cx, top - MathF.Floor(height * 0.075f), cx, top, Black, MathF.Floor(height * 0.007f));
        float across = MathF.Floor(width * 0.2f), halfWidth = width * 0.5f, halfHeight = height * 0.5f;
        Line(batch, halfWidth - across, halfHeight, across + halfWidth, halfHeight, Black);
        Line(batch, halfWidth, halfHeight, halfWidth, MathF.Floor(height * 0.05f) + halfHeight, Black);

        // SniperOverlay_DrawRangeMeter and SniperOverlay_DrawCompass.
        Text(batch, RangeText(title), MathF.Floor(width * 0.365f), MathF.Floor(height * 0.52f), Black);
        DrawCompass(batch, title, width * 0.325f, height * 0.56f, width * 0.35f, Black, Black);
        if (title.ScreenOverlay?.IsInfrared == true)
        {
            DrawBinocularFrame(batch, InfraredSprite, width, height);
        }
    }

    /// <summary><c>AugSniperOverlay_Draw</c>: posts left, right and below, hairlines on to a small ring at the middle.</summary>
    private void DrawAugReticle(SpriteBatch batch, float cx, float cy, float height)
    {
        float thick = MathF.Floor(height * 0.015f), post = MathF.Floor(height * 0.2f), ring = height * 0.4f, inner = height * 0.04f;
        float right = ring + cx - (thick * 0.5f), rightInner = right - post;
        Line(batch, rightInner, cy, right, cy, Black, thick);
        Line(batch, rightInner - MathF.Floor(rightInner - cx - inner), cy, rightInner, cy, Black);
        float left = cx - ring + (thick * 0.5f), leftInner = post + left;
        Line(batch, leftInner, cy, left, cy, Black, thick);
        Line(batch, MathF.Floor(cx - inner - leftInner) + leftInner, cy, leftInner, cy, Black);
        float bottom = ring + cy - (thick * 0.5f), top = bottom - post;
        Line(batch, cx, top, cx, bottom, Black, thick);
        Line(batch, cx, top - MathF.Floor(top - cy - inner), cx, top, Black);
        DrawRing(batch, cx, cy, inner, 32, MathF.Max(1f, height * 0.004f));
    }

    /// <summary><c>G11SniperOverlay_Draw</c>: four posts in from the ring, hairlines short of the middle, and a diamond there.</summary>
    private void DrawG11Reticle(SpriteBatch batch, float width, float height)
    {
        float cx = MathF.Floor(width * 0.5f), mx = width * 0.5f, my = height * 0.5f, ring = height * 0.4f, length = width * 0.2f;
        float across = 0.03f * length;
        float l0 = cx - ring, l1 = l0 + length;
        Quad(batch, G11AimSprite, (l0, my - across), (l1, my - across), (l0, my + across), (l1, my + across));
        float r0 = ring + cx, r1 = r0 - length;
        Quad(batch, G11AimSprite, (r0, my - across), (r1, my - across), (r0, my + across), (r1, my + across));
        float t0 = my - ring, t1 = t0 + length;
        Quad(batch, G11AimSprite, (mx - across, t0), (mx - across, t1), (mx + across, t0), (mx + across, t1));
        float b0 = ring + my, b1 = b0 - length;
        Quad(batch, G11AimSprite, (mx - across, b0), (mx - across, b1), (mx + across, b0), (mx + across, b1));

        float gap = height * 0.05f, hair = height * 0.15f;
        Line(batch, mx, gap + my, mx, hair + gap + my, Black);
        Line(batch, mx, my - gap, mx, my - gap - hair, Black);
        Line(batch, mx - gap, my, mx - gap - hair, my, Black);
        Line(batch, gap + mx, my, hair + gap + mx, my, Black);
        Line(batch, mx - gap, my, mx, my + gap, Black);
        Line(batch, mx, my + gap, mx + gap, my, Black);
        Line(batch, mx + gap, my, mx, my - gap, Black);
        Line(batch, mx, my - gap, mx - gap, my, Black);
    }

    /// <summary><c>Psg1SniperOverlay_Draw</c>: four posts in from the ring, a thin cross through it, and mil dots along the cross.</summary>
    private void DrawPsg1Reticle(SpriteBatch batch, float width, float height, float cx, float cy)
    {
        float post = MathF.Floor(height * 0.18f), thick = MathF.Floor(height * 0.025f), ring = height * 0.4f, half = thick * 0.5f;
        float right = ring + cx - half;
        Line(batch, right - post, cy, right, cy, Black, thick);
        float left = cx - ring + half;
        Line(batch, post + left, cy, left, cy, Black, thick);
        float bottom = ring + cy - half;
        Line(batch, cx, bottom - post, cx, bottom, Black, thick);
        float top = cy - ring + half;
        Line(batch, cx, post + top, cx, top, Black, thick);
        float cross = MathF.Floor(height * 0.8f), mx = width * 0.5f;
        Line(batch, mx - ring, cy, cross + mx - ring, cy, Black);
        Line(batch, cx, height * 0.1f, cx, cross + (height * 0.1f), Black);

        float dot = width * 0.01f * 0.5f, step = MathF.Floor(height * 0.2f) * 0.2f;
        for (int i = 1; i < 5; i++)
        {
            float along = i * step;
            Dot(batch, mx - along, cy, dot);
            Dot(batch, mx + along, cy, dot);
            Dot(batch, mx, cy - along, dot);
            Dot(batch, mx, cy + along, dot);
        }
    }

    /// <summary>
    /// <c>LaserDesignatorOverlay_Draw</c>: the binoculars' frame, the range and the lock under the middle, and green brackets either
    /// side of it. The lock reads locked on, the seconds left of the twenty a target is held for, or no lock.
    /// </summary>
    private void DrawLaserDesignator(SpriteBatch batch, Igi2TitlePlayer title, float width, float height)
    {
        DrawBinocularFrame(batch, BinocularSprite, width, height);
        float mx = width * 0.5f, my = height * 0.5f, lineHeight = _font?.LineHeight ?? 0;
        float x = MathF.Floor(mx - (width * 0.16f));
        float rangeY = lineHeight + my;
        Text(batch, RangeText(title), x, MathF.Floor(rangeY), Bgra32.White);

        OpenIGI.Game.Igi2.Weapons.Igi2LaserDesignatorState? designator = title.Weapons?.Firing.LaserDesignator;
        string text;
        if (designator?.IsLockedOn == true)
        {
            text = title.Strings?.Get("BOMB_LOCKED_ON") ?? "BOMB_LOCKED_ON";
        }
        else if (designator is { TickFirstHit: not 0 } held)
        {
            double left = Math.Max(0.0, 20.0 - ((held.LevelTick() - held.TickFirstHit) * 0.03333333507180214));
            text = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"L: {left:0.000}");
        }
        else
        {
            text = title.Strings?.Get("NO_LOCK") ?? "NO_LOCK";
        }

        if (_lock != text)
        {
            _lock = text;
        }

        Text(batch, _lock, x, MathF.Floor(lineHeight + rangeY), Bgra32.White);

        float gap = width * 0.01f, reach = width * 0.15f;
        float l = mx - gap, r = gap + mx;
        Line(batch, l, my, l - reach, my, DesignatorGreen);
        Line(batch, l, gap + my, l, my - gap, DesignatorGreen);
        Line(batch, r, my, reach + r, my, DesignatorGreen);
        Line(batch, r, gap + my, r, my - gap, DesignatorGreen);
    }

    /// <summary>The scope's frame: its corner sprite, a sprite pixel past each edge, mirrored into each quarter.</summary>
    private void DrawCorners(SpriteBatch batch, float width, float height)
    {
        if (_assets?.Get(ScopeCornerSprite) is not { } sprite)
        {
            return;
        }

        float edge = width / sprite.Asset.Frames[0].Width, mx = width * 0.5f, my = height * 0.5f;
        Quad(batch, ScopeCornerSprite, (-edge, -edge), (mx, -edge), (-edge, my), (mx, my));
        Quad(batch, ScopeCornerSprite, (width + edge, -edge), (mx, -edge), (width + edge, my), (mx, my));
        Quad(batch, ScopeCornerSprite, (-edge, height + edge), (mx, height + edge), (-edge, my), (mx, my));
        Quad(batch, ScopeCornerSprite, (width + edge, height + edge), (mx, height + edge), (width + edge, my), (mx, my));
    }

    /// <summary>A ring of straight segments, each a turn over their count.</summary>
    private void DrawRing(SpriteBatch batch, float cx, float cy, float radius, int segments, float thickness)
    {
        float step = MathF.Tau / segments;
        for (int i = 1; i <= segments; i++)
        {
            float a = (i - 1) * step, b = i * step;
            Line(batch, (MathF.Cos(a) * radius) + cx, (MathF.Sin(a) * radius) + cy, (MathF.Cos(b) * radius) + cx, (MathF.Sin(b) * radius) + cy, Black, thickness);
        }
    }

    private void Dot(SpriteBatch batch, float x, float y, float half) =>
        Quad(batch, Psg1MilDotSprite, (x - half, y - half), (x + half, y - half), (x - half, y + half), (x + half, y + half));

    /// <summary>
    /// A sprite over four corners in <c>QSprite_Register4</c>'s order: the texture's top left, top right, bottom left and bottom
    /// right, so a corner given out of place mirrors or turns it.
    /// </summary>
    private void Quad(SpriteBatch batch, string name, (float X, float Y) a, (float X, float Y) b, (float X, float Y) c, (float X, float Y) d)
    {
        if (_assets?.Get(name) is not { } sprite)
        {
            return;
        }

        SpriteFrame frame = sprite.Asset.Frames[0];
        Bgra32 white = Bgra32.White;
        batch.DrawQuad(sprite.Textures[frame.TextureIndex],
            new Vertex2D(a.X * _scale, a.Y * _scale, frame.U0, frame.V0, white),
            new Vertex2D(b.X * _scale, b.Y * _scale, frame.U1, frame.V0, white),
            new Vertex2D(d.X * _scale, d.Y * _scale, frame.U1, frame.V1, white),
            new Vertex2D(c.X * _scale, c.Y * _scale, frame.U0, frame.V1, white));
    }
}
