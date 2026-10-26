using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>Draws the retail binocular overlay in place of the ordinary weapon HUD.</summary>
/// <remarks>
/// <see cref="StatusScreenRenderer"/>'s binocular half: the device frame, the range readout, the sweep
/// ruler, the reticle and axes, the contact boxes and the compass strip. It owns no archive; the HUD's
/// font and the global sprite archive are handed in by the status screen that loaded them.
/// </remarks>
internal sealed class BinocularOverlayRenderer
{
    // 0x0046ff00 (LaserDesignatorOverlay_StatusDisplayHandler) positions the native range holder at
    // these normalized video coordinates. They are separate from the 0.7625 y-coordinate used by the
    // binocular reticle line group.
    private const float BinocularRangeX = 0.378125f;
    private const float BinocularRangeY = 0.55f;

    // FUN_00471120 fills the compass cardinal holders from 0x5402A8 as 'W','N','E','S'; FUN_00472050
    // draws them at ring ticks 0/16/32/48. Working in the project's 0=N heading, tick i sits at
    // i*5.625 degrees, so the labels appear here in N/E/S/W order (indexed by i >> 4).
    private static readonly string[] CompassCardinals = ["N", "E", "S", "W"];

    private readonly Func<BitmapTextRenderer?> _font;
    private readonly Func<string, SpriteResource?> _globalSprite;
    private readonly BinocularSweep _binocularSweep = new();

    /// <summary>Initializes a new instance of the <see cref="BinocularOverlayRenderer"/> class.</summary>
    /// <param name="font">The HUD's readout font, or <see langword="null"/> while it is unavailable.</param>
    /// <param name="globalSprite">A sprite from the global sprite archive only, by name.</param>
    public BinocularOverlayRenderer(Func<BitmapTextRenderer?> font, Func<string, SpriteResource?> globalSprite)
    {
        _font = font;
        _globalSprite = globalSprite;
    }

    /// <summary>Draws the retail binocular task overlay, including its authored device frame.</summary>
    /// <remarks>
    /// Ghidra identifies the retail task as <c>BinocularOverlay_Draw</c>, with separate compass and
    /// distance handlers. The binocular sprite is therefore drawn in place of the ordinary weapon HUD;
    /// the camera and zoom state remain owned by <see cref="OpenIGI.Game.Igi1.Player.BinocularsView"/>.
    /// </remarks>
    public void Draw(
        SpriteBatch batch,
        int screenWidth,
        int screenHeight,
        IReadOnlyList<EnemyOverlayBox>? targets,
        float headingRadians,
        int simulationTick,
        float? rangeMetres)
    {
        // STATUSSCREEN:binoculars.spr is the 37x26 ordinary weapon-HUD icon. It is not the camera
        // mask and must never be stretched across the back buffer. Both r2 and Ghidra show
        // BinocularOverlay's initializer (0x470fa0) resolving the separate, global 256x256
        // COMMON/SPRITES binoculars.spr texture, then FUN_00471480 drawing it as two mirrored
        // quads. Keep that archive selection explicit: Sprite("binoculars.spr") intentionally
        // prefers the status archive for normal HUD items.

        // RetailBinocularUpdater (0x0051A8C0) updates the holder's path; the overlay builder at
        // 0x00471480 then draws the authored binocular frame, reticle and range labels. The native
        // widget is a range readout (weapons/strings/Binoculars/range), not a compass strip. Its
        // distance is the crosshair raycast result supplied by the caller, not a detected soldier.
        DrawNativeBinocularRange(batch, screenWidth, screenHeight, rangeMetres);

        // The original advances twelve independently gated records before drawing the arms and axes.
        // FUN_00471A90 turns each phase into a column which grows upward in the lower-left ruler.
        foreach (BinocularSweepColumn column in _binocularSweep.Tick(simulationTick))
        {
            if (column.Height <= 0f)
            {
                continue;
            }

            byte red = (byte)Math.Clamp(MathF.Round(column.Phase * 128f), 0f, 128f);
            Bgra32 sweepColour = Bgra32.FromRgb(red, 128, 0);
            batch.FillRect(new RectF(
                column.X * screenWidth,
                column.Y * screenHeight,
                column.Width * screenWidth,
                column.Height * screenHeight),
                sweepColour);
        }

        DrawRetailBinocularFrame(batch, screenWidth, screenHeight);

        // FUN_00471480 draws the two reticle brackets and the full sight axes. FUN_004B5860 walks the
        // vertex strip at unk_534070 and joins each consecutive pair with a FUN_004B5730 line, so each
        // arm is a thin "[" / "]" bracket outline framing the centre -- NOT a filled block, which is
        // what a solid FillRect made it. The vertices are (-.5,-2.5),(-2,-2.5),(-2,2.5),(-.5,2.5) at
        // anchors of +/-9.375% of the width, scaled by 1.5625% of the width and 2.0833334% of height.
        float centreX = screenWidth * 0.5f;
        float centreY = screenHeight * 0.5f;
        Bgra32 signal = new(64, 160, 64, 210);
        // The reticle/axes are green in the retail device, but FUN_00471B60's contact
        // marker colour is chosen per target: red for a team-1 hostile, green for any other team,
        // blue for a non-collidable body. See MarkerColour and BinocularMarkerColor.
        // Bgra32's constructor is storage-order (blue, green, red, alpha); use the RGB factory so
        // the markers read correctly on screen.
        float lineWeight = MathF.Max(1f, UiScale.For(screenWidth, screenHeight));
        float armAnchor = screenWidth * 0.09375f;
        float armScaleX = screenWidth * 0.015625f;
        float armScaleY = screenHeight * 0.020833334f;
        // The left bracket uses positive scales; the right mirrors it with both scales negated, exactly
        // as the two FUN_004B5860 calls do.
        DrawSightBracket(batch, centreX - armAnchor, centreY, armScaleX, armScaleY, lineWeight, signal);
        DrawSightBracket(batch, centreX + armAnchor, centreY, -armScaleX, -armScaleY, lineWeight, signal);

        // FUN_00471480 emits two additional D3D line-list primitives through FUN_004B5730. The
        // first joins (0, centreY) to (screenWidth, centreY), and the second joins
        // (centreX, 0) to (centreX, screenHeight). They are the native binocular sight axes; the
        // short bracket arms above are separate geometry and must not replace these lines.
        batch.FillRect(new RectF(0f, centreY - 0.5f, screenWidth, 1f), signal);
        batch.FillRect(new RectF(centreX - 0.5f, 0f, 1f, screenHeight), signal);

        if (targets is not null)
        {
            foreach (EnemyOverlayBox target in targets)
            {
                // FUN_004B5860 submits the rotated, scaled strip unclipped and lets the device clip it.
                // Clamping the bounds first would move the rotation centre of an edge contact.
                if (target.IntersectsViewport(screenWidth, screenHeight))
                {
                    DrawTargetBox(batch, target, MarkerColour(target.Marker));
                }
            }
        }

        // The retail binocular task closes by drawing the compass strip (FUN_00472050) at the
        // bottom-right of the sight. It scrolls the full 360-degree ring, labels the four cardinals
        // above and below the ticks, and marks the current heading with a pointer at each edge.
        DrawBinocularCompass(batch, screenWidth, screenHeight, headingRadians);
    }

    /// <summary>Maps a native detector marker class to its on-screen rectangle colour.</summary>
    /// <remarks>
    /// FUN_00471B60 submits <c>(64,0,0)</c>/<c>(0,128,0)</c>/<c>(0,0,128)</c>, and FUN_004B5730
    /// multiplies every channel by 0.0078125 (1/128) before building the vertex. On the back buffer
    /// that is a half-bright red and full green and blue.
    /// </remarks>
    private static Bgra32 MarkerColour(BinocularMarkerColor marker) => marker switch
    {
        BinocularMarkerColor.Team => Bgra32.FromRgb(0, 255, 0),
        BinocularMarkerColor.Disabled => Bgra32.FromRgb(0, 0, 255),
        _ => Bgra32.FromRgb(128, 0, 0),
    };

    /// <summary>Draws the retail binocular compass strip (<c>FUN_00472050</c>).</summary>
    /// <remarks>
    /// The native task lays the strip out in the 640×480 authored space at x=62.5%, y=79.166% with a
    /// width of 28.125% of the screen, draws sixty-four ticks across the full ring (a major tick with a
    /// cardinal label above and below every sixteen, a medium tick every eight, a minor tick
    /// otherwise), and closes with a pointer triangle at the top and bottom edges marking the centred
    /// heading. The absolute pixel sizes are scaled by <see cref="UiScale"/> so the strip keeps its
    /// proportions on a modern back buffer, exactly as the DISTANCE readout is.
    /// </remarks>
    private void DrawBinocularCompass(SpriteBatch batch, int screenWidth, int screenHeight, float headingRadians)
    {
        if (screenWidth <= 0 || screenHeight <= 0 || _font() is not { } font)
        {
            return;
        }

        float scale = UiScale.For(screenWidth, screenHeight);
        float leftX = screenWidth * 0.625f;
        float topY = screenHeight * 0.79166669f;
        float totalWidth = screenWidth * 0.28125f;
        if (totalWidth <= 0f)
        {
            return;
        }

        // FUN_00472050's strip height is 2*textHeight + 26 authored pixels; the ticks measure the
        // font once (sub_4B7E00) and centre the ring on it.
        float textHeight = font.LineHeight * scale;
        float stripHeight = (2f * textHeight) + (26f * scale);
        float centreY = topY + (stripHeight * 0.5f);
        float bottomY = topY + stripHeight;
        float lineWidth = MathF.Max(1f, scale);
        Bgra32 green = new(64, 160, 64, 255);

        // The native task scrolls the ring by the view heading. Reuse the project's 0=N heading so the
        // strip agrees with the rest of the HUD: a direction D is placed at the wrapped fraction
        // ((D - heading)/360 + 0.5) across the strip, which puts the faced direction at the centre.
        float heading = RemasteredHudPresentation.HeadingDegrees(headingRadians);

        for (int i = 0; i < 64; i++)
        {
            float directionDegrees = i * (360f / 64f);
            float tickHeight;
            int cardinalIndex = -1;
            if ((i & 0xF) == 0)
            {
                tickHeight = 12f * scale;
                cardinalIndex = i >> 4;
            }
            else if ((i & 7) == 0)
            {
                tickHeight = 8f * scale;
            }
            else
            {
                tickHeight = 4f * scale;
            }

            float fraction = Frac(((directionDegrees - heading) / 360f) + 0.5f);
            float x = leftX + (fraction * totalWidth);
            float halfTick = tickHeight * 0.5f;
            float tickTop = centreY - halfTick;
            float tickBottom = centreY + halfTick;
            batch.FillRect(new RectF(x - (lineWidth * 0.5f), tickTop, lineWidth, tickHeight), green);

            if (cardinalIndex >= 0)
            {
                // FUN_00472050 draws each cardinal glyph both above (v20 - h - 2) and below (v15 + 2)
                // the tick, which is why the retail compass reads its four letters in two rows.
                string label = CompassCardinals[cardinalIndex];
                font.DrawCentered(batch, label, x, tickTop - textHeight - (2f * scale), green, scale, scale);
                font.DrawCentered(batch, label, x, tickBottom + (2f * scale), green, scale, scale);
            }
        }

        // The two heading pointers: a downward triangle at the top edge and an upward one at the
        // bottom, both centred on the strip. FUN_00472050 sizes them at ±4 authored pixels.
        float centreX = leftX + (totalWidth * 0.5f);
        float pointer = 4f * scale;
        FillTriangle(
            batch,
            new Point2(centreX, topY + pointer),
            new Point2(centreX - pointer, topY),
            new Point2(centreX + pointer, topY),
            green);
        FillTriangle(
            batch,
            new Point2(centreX, bottomY - pointer),
            new Point2(centreX - pointer, bottomY),
            new Point2(centreX + pointer, bottomY),
            green);
    }

    private static float Frac(float value) => value - MathF.Floor(value);

    private static void FillTriangle(SpriteBatch batch, Point2 apex, Point2 baseA, Point2 baseB, Bgra32 colour)
    {
        // A degenerate quad (the apex repeated) draws the single triangle FUN_00472050 submits.
        batch.DrawQuad(
            TextureHandle.None,
            new Vertex2D(baseA.X, baseA.Y, 0f, 0f, colour),
            new Vertex2D(baseB.X, baseB.Y, 1f, 0f, colour),
            new Vertex2D(apex.X, apex.Y, 1f, 1f, colour),
            new Vertex2D(apex.X, apex.Y, 0f, 1f, colour));
    }

    private void DrawRetailBinocularFrame(SpriteBatch batch, int screenWidth, int screenHeight)
    {
        if (_globalSprite("binoculars.spr") is not { Asset.Frames.Count: > 0 } frameSprite)
        {
            return;
        }

        // FUN_00471480 obtains s = videoWidth / spriteWidth, then submits the global 256x256
        // binocular art twice through FUN_004B51F0. The left quad spans (-s, -s) to
        // (videoWidth / 2, videoHeight + s); the right has reversed X coordinates from
        // (videoWidth + s, -s) to (videoWidth / 2, videoHeight + s). The negative second scale
        // retains that native mirror instead of cropping or stretching the status icon.
        SpriteFrame frame = frameSprite.Asset.Frames[0];
        float overscan = screenWidth / (float)frame.Width;
        float scaleX = ((screenWidth * 0.5f) + overscan) / frame.Width;
        float scaleY = (screenHeight + (overscan * 2f)) / frame.Height;
        Bgra32 neutral = BitmapTextRenderer.NeutralColor;

        frameSprite.DrawFrame(batch, frame, -overscan, -overscan, scaleX, scaleY, neutral);
        frameSprite.DrawFrame(batch, frame, screenWidth + overscan, -overscan, -scaleX, scaleY, neutral);
    }

    private static void DrawTargetBox(SpriteBatch batch, EnemyOverlayBox target, Bgra32 colour)
    {
        // Retail FUN_00471B60 passes DAT_005340B0 (the closed points {-1,-1}, {1,-1}, {1,1},
        // {-1,1}, {-1,-1}) to FUN_004B5860. That helper rotates and scales each of the four
        // segments around the projected target centre; keep that pulse instead of replacing it
        // with four static axis-aligned rectangles.
        float centreX = (target.Left + target.Right) * 0.5f;
        float centreY = (target.Top + target.Bottom) * 0.5f;
        // FUN_004B5860 scales the unit square by the projected half-size as is; a distant contact
        // really does shrink below a pixel.
        float halfWidth = (target.Right - target.Left) * 0.5f * target.PulseScale;
        float halfHeight = (target.Bottom - target.Top) * 0.5f * target.PulseScale;
        float cos = MathF.Cos(target.RotationRadians);
        float sin = MathF.Sin(target.RotationRadians);
        Point2 TopLeft() => Rotate(centreX, centreY, -halfWidth, -halfHeight, cos, sin);
        Point2 TopRight() => Rotate(centreX, centreY, halfWidth, -halfHeight, cos, sin);
        Point2 BottomRight() => Rotate(centreX, centreY, halfWidth, halfHeight, cos, sin);
        Point2 BottomLeft() => Rotate(centreX, centreY, -halfWidth, halfHeight, cos, sin);

        Point2 a = TopLeft();
        Point2 b = TopRight();
        Point2 c = BottomRight();
        Point2 d = BottomLeft();
        float thickness = Math.Clamp(MathF.Min(target.Right - target.Left, target.Bottom - target.Top) * 0.055f, 1f, 3f);
        Bgra32 faded = colour.WithAlpha((byte)(colour.A * Math.Clamp(target.Alpha, 0f, 1f)));
        DrawSegment(batch, a, b, thickness, faded);
        DrawSegment(batch, b, c, thickness, faded);
        DrawSegment(batch, c, d, thickness, faded);
        DrawSegment(batch, d, a, thickness, faded);
    }

    private static Point2 Rotate(float centreX, float centreY, float x, float y, float cos, float sin) =>
        new(centreX + (x * cos) - (y * sin), centreY + (x * sin) + (y * cos));

    // FUN_00471480's reticle arm geometry (unk_534070): an open "[" bracket walked as a line strip.
    private static readonly (float X, float Y)[] ArmStrip =
        [(-0.5f, -2.5f), (-2f, -2.5f), (-2f, 2.5f), (-0.5f, 2.5f)];

    /// <summary>Draws one reticle bracket as the line strip FUN_004B5860 walks through unk_534070.</summary>
    private static void DrawSightBracket(
        SpriteBatch batch,
        float originX,
        float originY,
        float scaleX,
        float scaleY,
        float thickness,
        Bgra32 colour)
    {
        Point2 previous = new(originX + (ArmStrip[0].X * scaleX), originY + (ArmStrip[0].Y * scaleY));
        for (int index = 1; index < ArmStrip.Length; index++)
        {
            Point2 current = new(originX + (ArmStrip[index].X * scaleX), originY + (ArmStrip[index].Y * scaleY));
            DrawSegment(batch, previous, current, thickness, colour);
            previous = current;
        }
    }

    private static void DrawSegment(SpriteBatch batch, Point2 start, Point2 end, float thickness, Bgra32 colour)
    {
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= 0.001f)
        {
            return;
        }

        float nx = -dy / length * (thickness * 0.5f);
        float ny = dx / length * (thickness * 0.5f);
        batch.DrawQuad(
            TextureHandle.None,
            new Vertex2D(start.X + nx, start.Y + ny, 0f, 0f, colour),
            new Vertex2D(end.X + nx, end.Y + ny, 1f, 0f, colour),
            new Vertex2D(end.X - nx, end.Y - ny, 1f, 1f, colour),
            new Vertex2D(start.X - nx, start.Y - ny, 0f, 1f, colour));
    }

    private readonly record struct Point2(float X, float Y);

    private void DrawNativeBinocularRange(
        SpriteBatch batch,
        int screenWidth,
        int screenHeight,
        float? rangeMetres)
    {
        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return;
        }

        if (_font() is not { } font)
        {
            return;
        }

        // The native holder uses the video dimensions directly: x=37.8125% and y=55%, with the text
        // height subtracted from y.
        float scale = UiScale.For(screenWidth, screenHeight);
        // FUN_00471480 casts a 1000 m ray from the eye along the aim (its scale factor 0.000244140625
        // is 1/4096 = 1/UnitsPerMetre) and formats the hit distance with "%s: %0.f%s"; with no hit it
        // takes the "%s: %0.f+%s" branch and prints 1000+M. A null/non-finite range is the no-hit case.
        string range = rangeMetres is { } metres && float.IsFinite(metres) && metres > 0f
            ? FormatBinocularRange(metres)
            : "DISTANCE: 1000+M";
        font.Draw(
            batch,
            range,
            screenWidth * BinocularRangeX,
            (screenHeight * BinocularRangeY) - (font.LineHeight * scale),
            // The text object keeps the colour Text_New gives it, 128 in each channel: the font's own art.
            BitmapTextRenderer.NeutralColor,
            scale,
            scale);
    }

    /// <summary>Formats the native binocular holder's two authored range labels.</summary>
    /// <remarks>
    /// <para>
    /// <c>FUN_00471480</c> builds the label with <c>"%s: %0.f%s"</c> (measured) or
    /// <c>"%s: %0.f+%s"</c> (no contact, value 1000/3000). The <c>%s</c> label is the localized
    /// <c>weapons/strings/Binoculars/range</c> string; the exe's fallback literal is <c>RANGE</c>,
    /// but the shipped English resource resolves that key to <b>DISTANCE</b>, which is what the retail
    /// binocular actually renders. The holder switches to the <c>1000+M</c> form at 1000 metres
    /// instead of letting the formatted value grow wider.
    /// </para>
    /// </remarks>
    internal static string FormatBinocularRange(float distanceMetres) => distanceMetres >= 1000f
        ? "DISTANCE: 1000+M"
        : FormattableString.Invariant($"DISTANCE: {MathF.Round(MathF.Max(0f, distanceMetres)):0}M");

    /// <summary>Returns the native range label, including the no-contact fallback.</summary>
    internal static string BinocularRangeText(EnemyOverlayBox? nearest) => nearest is { } target
        ? FormatBinocularRange(target.DistanceMetres)
        : "DISTANCE: 1000+M";
}
