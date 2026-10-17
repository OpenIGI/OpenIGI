using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Text;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Text standing in the level itself: distance-field glyphs laid on the ground at the player's start,
/// tens of metres across, or posted upright on a wall, drawn through the same camera the frame is
/// looking through.
/// </summary>
/// <remarks>
/// <para>
/// The way Splinter Cell captions a location -- the name of the place set into the place -- rather than
/// a label over the picture. The letters are quads in a plane in the world; each corner is projected
/// through the eye every frame and drawn through the sprite path's distance-field shader, so the glyphs
/// are as sharp at two hundred metres across as the menu's headings are at twenty pixels. Nothing
/// occludes them: from the vantage the ground around a spawn is open, and a notice on a wall is drawn
/// where a wall is, so a roof cannot come between.
/// </para>
/// <para>
/// What makes it read as an object rather than a decal: a dark extrusion a few centimetres behind the
/// face, which moves against it exactly as a real relief would when the viewer moves; a soft light
/// halo added over it; a slow sweep of light across the letters; and a faint flicker, as of something
/// projected. The letters arrive one after another, each dropping a little way into place as it fades
/// in, and the whole block dissolves on request. No device, no clock of its own: the frame hands it an
/// eye and a time.
/// </para>
/// </remarks>
public sealed class WorldTitleRenderer
{
    /// <summary>One line of the caption.</summary>
    /// <param name="Text">What it says.</param>
    /// <param name="HeightMetres">How tall a line of it is.</param>
    /// <param name="Colour">Its colour.</param>
    /// <param name="GapMetres">The space left above it.</param>
    public readonly record struct Line(string Text, float HeightMetres, Bgra32 Colour, float GapMetres);

    private readonly record struct Glyph(
        Vector3d TopLeft,
        Vector3d TopRight,
        Vector3d BottomRight,
        Vector3d BottomLeft,
        RectF Source,
        Bgra32 Colour,
        int Index,
        Vector3d Drop,
        float Across);

    /// <summary>Seconds between one letter's arrival and the next, for a short caption.</summary>
    private const float StaggerSeconds = 0.035f;

    /// <summary>The most the whole block's arrival is allowed to take, so a long notice does not trickle in.</summary>
    private const float LongestArrivalSeconds = 1.6f;

    /// <summary>How long one letter takes to arrive.</summary>
    private const float ArriveSeconds = 0.42f;

    /// <summary>How many steps a per-letter burn is quantised to, so neighbouring letters share a draw.</summary>
    private const float BurnSteps = 24f;

    /// <summary>How long the light takes to sweep the block once, and how long it rests between sweeps.</summary>
    private const float SweepSeconds = 2.6f;
    private const float SweepRestSeconds = 3.4f;

    private readonly UiFont _font;
    private readonly List<Glyph> _glyphs = [];
    private readonly SdfTextParams _face;
    private readonly SdfTextParams _relief;
    private readonly SdfTextParams _halo;
    private Vector3d _normal;
    private float _blockWidth = 1f;
    private float _seed;
    private float _stagger = StaggerSeconds;

    /// <summary>Initializes a new instance of the <see cref="WorldTitleRenderer"/> class.</summary>
    /// <param name="font">The distance-field face every line is set in.</param>
    public WorldTitleRenderer(UiFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        _font = font;

        float range = font.Atlas.SdfPadding;

        // The face: a thin dark halo under the fill, so the letters read over a sunlit yard as well as
        // over snow. The relief behind it is plain and dark. The halo over it is wide, light and added.
        _face = SdfTextParams.Plain(range).WithGlow(Bgra32.FromRgb(6, 10, 16), 0.28f, 0.8f);
        _relief = SdfTextParams.Plain(range);
        _halo = SdfTextParams.Plain(range).WithGlow(Bgra32.FromRgb(120, 160, 210), 0.48f, 1f);
    }

    /// <summary>Gets whether there is anything to draw.</summary>
    public bool HasTitle => _glyphs.Count > 0;

    /// <summary>Gets or sets how much of the whole caption shows, one for all of it.</summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>Gets how long the letters have been arriving, in seconds.</summary>
    public float Age { get; private set; }

    /// <summary>Gets or sets how many relief layers stand behind the face, for depth; zero for a flat decal.</summary>
    public int ReliefLayers { get; set; } = 3;

    /// <summary>Gets or sets how far each relief layer steps back from the face, in metres.</summary>
    public float ReliefStepMetres { get; set; } = 0.012f;

    /// <summary>Gets or sets whether the light halo is added over the face.</summary>
    public bool Halo { get; set; } = true;

    /// <summary>Gets or sets whether a light sweeps the block now and then.</summary>
    public bool Sweep { get; set; } = true;

    /// <summary>Gets or sets how much the letters flicker, as of something projected; zero for steady.</summary>
    public float Flicker { get; set; } = 0.035f;

    /// <summary>Gets or sets how far the block has burned away, zero whole to one gone; runs across it left to right.</summary>
    public float Dissolve { get; set; }

    /// <summary>Gets or sets whether the letters materialise out of the grain as they arrive, rather than only fading in.</summary>
    public bool Materialise { get; set; }

    /// <summary>Gets or sets whether the block burns away as it goes, rather than lifting and fading.</summary>
    public bool Burn { get; set; }

    /// <summary>Gets how long the whole block takes to arrive, in seconds.</summary>
    public float ArrivalSeconds => (_glyphs.Count * _stagger) + ArriveSeconds;

    /// <summary>Gets whether any of the block has been inside the frame since it was laid.</summary>
    public bool Seen { get; private set; }

    /// <summary>Gets the age at which the block was first inside the frame.</summary>
    public float SeenAtAge { get; private set; }

    /// <summary>Lays the caption in the world.</summary>
    /// <param name="lines">The lines, top to bottom.</param>
    /// <param name="anchorMetres">The centre of the block, in metres.</param>
    /// <param name="yaw">The heading the caption reads towards: its "up" on the ground, or the way it faces when upright.</param>
    /// <param name="upright">
    /// Whether the caption stands up, facing a viewer who looks along <paramref name="yaw"/>, rather than
    /// lying on the ground to be read from above.
    /// </param>
    public void Lay(IReadOnlyList<Line> lines, Vector3d anchorMetres, float yaw, bool upright = false)
    {
        ArgumentNullException.ThrowIfNull(lines);
        _glyphs.Clear();
        Age = 0f;
        Opacity = 1f;

        // The heading's forward is the caption's up on the ground; right is a quarter turn from it. Same
        // convention as the shot's travel yaw: measured from +Y towards +X. Stood up, the caption's up is
        // world up and its face is towards the viewer, who reads it left to right along the same right.
        var forward = new Vector3d(Math.Sin(yaw), Math.Cos(yaw), 0.0);
        var right = new Vector3d(Math.Cos(yaw), -Math.Sin(yaw), 0.0);
        var up = upright ? new Vector3d(0.0, 0.0, 1.0) : forward;

        // The face's normal, towards the viewer: back along the heading for a notice, straight up for
        // the ground. The relief steps the other way.
        _normal = upright ? new Vector3d(-forward.X, -forward.Y, 0.0) : new Vector3d(0.0, 0.0, 1.0);

        float totalHeight = 0f;
        float widest = 0.01f;
        foreach (Line line in lines)
        {
            float metresPerPixel = line.HeightMetres / _font.BasePixelHeight;
            totalHeight += line.GapMetres + (_font.LineHeight(_font.BasePixelHeight) * metresPerPixel);
            widest = MathF.Max(widest, _font.MeasureWidth(line.Text, _font.BasePixelHeight) * metresPerPixel);
        }

        _blockWidth = widest;
        _seed = (float)((anchorMetres.X * 0.37 + anchorMetres.Y * 0.11) % 1.0);
        Dissolve = 0f;
        Seen = false;
        SeenAtAge = 0f;

        // Laid from the top of the block downwards, with the block centred on the anchor.
        double cursor = totalHeight * 0.5;
        int index = 0;

        foreach (Line line in lines)
        {
            float metresPerPixel = line.HeightMetres / _font.BasePixelHeight;
            float lineHeight = _font.LineHeight(_font.BasePixelHeight) * metresPerPixel;
            float widthMetres = _font.MeasureWidth(line.Text, _font.BasePixelHeight) * metresPerPixel;

            cursor -= line.GapMetres;
            double lineTop = cursor;
            double baseline = lineTop - (_font.Ascent(_font.BasePixelHeight) * metresPerPixel);
            double pen = -widthMetres * 0.5;

            foreach (char c in line.Text)
            {
                GlyphInfo glyph = _font.GetGlyph(c);
                if (glyph.HasBitmap)
                {
                    double x0 = pen + (glyph.OffsetX * metresPerPixel);
                    double y0 = baseline + (glyph.OffsetY * metresPerPixel);
                    double x1 = x0 + (glyph.Width * metresPerPixel);
                    double y1 = y0 + (glyph.Height * metresPerPixel);

                    // Glyph space: x along right, y DOWN the caption -- so the position is the anchor plus
                    // x along right and minus y along the caption's up.
                    Vector3d At(double x, double y) => new(
                        anchorMetres.X + (right.X * x) - (up.X * y),
                        anchorMetres.Y + (right.Y * x) - (up.Y * y),
                        anchorMetres.Z - (up.Z * y));

                    _glyphs.Add(new Glyph(
                        At(x0, y0),
                        At(x1, y0),
                        At(x1, y1),
                        At(x0, y1),
                        glyph.SourceRect(_font.Atlas.Width, _font.Atlas.Height).ToRectF(),
                        line.Colour,
                        index,
                        new Vector3d(-up.X * lineHeight * 0.4, -up.Y * lineHeight * 0.4, -up.Z * lineHeight * 0.4),
                        (float)(((x0 + x1) * 0.5 / widest) + 0.5)));
                    index++;
                }

                pen += glyph.Advance * metresPerPixel;
            }

            cursor -= lineHeight;
        }

        _stagger = _glyphs.Count == 0 ? StaggerSeconds : MathF.Min(StaggerSeconds, LongestArrivalSeconds / _glyphs.Count);
    }

    /// <summary>Takes the caption down.</summary>
    public void Clear()
    {
        _glyphs.Clear();
        Age = 0f;
    }

    /// <summary>Advances the letters' arrival and the light.</summary>
    /// <param name="elapsedSeconds">Real seconds since the last call.</param>
    public void Update(float elapsedSeconds) => Age += Math.Clamp(elapsedSeconds, 0f, 0.1f);

    /// <summary>Draws the caption through an eye.</summary>
    /// <param name="batch">The 2D batch, already begun.</param>
    /// <param name="eye">The camera the frame was drawn from.</param>
    /// <param name="horizontalTangent">The tangent of half the horizontal field of view.</param>
    /// <param name="aspectRatio">The output's width over its height.</param>
    /// <param name="width">The output width in pixels.</param>
    /// <param name="height">The output height in pixels.</param>
    public void Draw(SpriteBatch batch, in Camera3D eye, float horizontalTangent, float aspectRatio, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_glyphs.Count == 0 || Opacity <= 0.002f || !_font.Texture.IsValid)
        {
            return;
        }

        Camera3D camera = eye;
        Matrix3f worldToView = camera.Orientation.Transposed();
        double tangentH = Math.Max(0.0001f, horizontalTangent);
        double tangentV = tangentH / Math.Max(0.0001f, aspectRatio);

        // The flicker: a projected thing never quite holds its brightness. Stepped, not smooth, so it
        // reads as a flicker rather than a pulse.
        float flicker = Flicker > 0f
            ? 1f - (Flicker * Hash(MathF.Floor(Age * 24f)))
            : 1f;

        // Where the light is across the block, or off it while it rests.
        float cycle = SweepSeconds + SweepRestSeconds;
        float sweepPhase = Age % cycle;
        float sweepAt = sweepPhase < SweepSeconds ? (sweepPhase / SweepSeconds * 1.4f) - 0.2f : -10f;

        // The relief first, from the deepest layer forward, so the face lands on top of it.
        for (int layer = ReliefLayers; layer >= 1; layer--)
        {
            double back = -ReliefStepMetres * layer;
            var offset = new Vector3d(_normal.X * back, _normal.Y * back, _normal.Z * back);
            float shade = 0.22f + (0.06f * (ReliefLayers - layer));
            DrawLayer(offset, glyph => Scale(glyph.Colour, shade), 0.92f, _relief, BlendMode.AlphaBlend);
        }

        // The face, with the light passing over it.
        DrawLayer(
            default,
            glyph =>
            {
                float band = Sweep ? MathF.Exp(-Square((glyph.Across - sweepAt) / 0.09f)) : 0f;
                return Lift(glyph.Colour, band * 0.55f);
            },
            flicker,
            _face,
            BlendMode.AlphaBlend);

        // The halo, added: light the letters throw around themselves.
        if (Halo)
        {
            DrawLayer(default, glyph => Scale(glyph.Colour, 0.55f), 0.28f * flicker, _halo, BlendMode.Additive);
        }

        void DrawLayer(Vector3d offset, Func<Glyph, Bgra32> tint, float alphaScale, in SdfTextParams shading, BlendMode blend)
        {
            SdfTextParams baseShading = shading;

            foreach (Glyph glyph in _glyphs)
            {
                float arrival = Easing.Evaluate(Ease.OutCubic, Math.Clamp((Age - (glyph.Index * _stagger)) / ArriveSeconds, 0f, 1f));
                if (arrival <= 0.001f)
                {
                    continue;
                }

                // The leaving runs across the block, each letter a little behind the one before it. By
                // default a letter lifts the way it dropped in and fades as it goes; as a burn, the grain
                // takes it. The arrival can be the burn in reverse. Quantised so that runs of letters
                // share a draw.
                float leave = Easing.Evaluate(Ease.InCubic, Math.Clamp((Dissolve * 1.35f) - (glyph.Across * 0.35f), 0f, 1f));
                if (leave >= 0.999f)
                {
                    continue;
                }

                float burn = 0f;
                if (Burn)
                {
                    burn = leave;
                }

                if (Materialise)
                {
                    burn = MathF.Max(burn, 1f - arrival);
                }

                burn = MathF.Round(burn * BurnSteps) / BurnSteps;
                if (burn >= 1f)
                {
                    continue;
                }

                SdfTextParams glyphShading = burn > 0f ? baseShading.WithDissolve(burn, _seed) : baseShading;

                float presence = (Materialise ? 1f : arrival) * (Burn ? 1f : 1f - leave);
                float alpha = Math.Clamp(presence * Opacity * alphaScale, 0f, 1f);
                Bgra32 colour = tint(glyph).WithAlpha((byte)(255f * alpha));

                // Each letter drops the last stretch of a line height into its place, and lifts the same
                // way when it goes.
                double settle = (1.0 - arrival) - (Burn ? 0.0 : leave * 0.6);
                var drop = new Vector3d(
                    offset.X + (glyph.Drop.X * settle),
                    offset.Y + (glyph.Drop.Y * settle),
                    offset.Z + (glyph.Drop.Z * settle));

                if (!TryProject(glyph.TopLeft + drop, out (float X, float Y) a)
                    || !TryProject(glyph.TopRight + drop, out (float X, float Y) b)
                    || !TryProject(glyph.BottomRight + drop, out (float X, float Y) c)
                    || !TryProject(glyph.BottomLeft + drop, out (float X, float Y) d))
                {
                    continue;
                }

                // Seen once a letter's corner lands inside the frame -- what a hold that waits for the
                // player to look at the notice counts from.
                if (!Seen && (Inside(a) || Inside(b) || Inside(c) || Inside(d)))
                {
                    Seen = true;
                    SeenAtAge = Age;
                }

                batch.DrawTextQuad(
                    _font.Texture.ToTextureHandle(),
                    new Vertex2D(a.X, a.Y, glyph.Source.X, glyph.Source.Y, colour),
                    new Vertex2D(b.X, b.Y, glyph.Source.Right, glyph.Source.Y, colour),
                    new Vertex2D(c.X, c.Y, glyph.Source.Right, glyph.Source.Bottom, colour),
                    new Vertex2D(d.X, d.Y, glyph.Source.X, glyph.Source.Bottom, colour),
                    glyphShading,
                    blend);
            }
        }

        bool Inside((float X, float Y) point) =>
            point.X >= 0f && point.X <= width && point.Y >= 0f && point.Y <= height;

        bool TryProject(Vector3d world, out (float X, float Y) screen)
        {
            var offset = new Vector3d(world.X - camera.Position.X, world.Y - camera.Position.Y, world.Z - camera.Position.Z);
            Vector3d view = worldToView.Transform(offset);
            if (view.Z <= 0.05)
            {
                screen = default;
                return false;
            }

            double ndcX = view.X / view.Z / tangentH;
            double ndcY = view.Y / view.Z / tangentV;
            screen = (
                (float)(((ndcX * 0.5) + 0.5) * width),
                (float)((1.0 - ((ndcY * 0.5) + 0.5)) * height));
            return true;
        }
    }

    private static float Square(float value) => value * value;

    private static float Hash(float value)
    {
        float s = MathF.Sin(value * 12.9898f) * 43758.5453f;
        return s - MathF.Floor(s);
    }

    private static Bgra32 Scale(Bgra32 colour, float by) => Bgra32.FromRgb(
        (byte)Math.Clamp(colour.R * by, 0f, 255f),
        (byte)Math.Clamp(colour.G * by, 0f, 255f),
        (byte)Math.Clamp(colour.B * by, 0f, 255f));

    private static Bgra32 Lift(Bgra32 colour, float towardsWhite) => Bgra32.FromRgb(
        (byte)Math.Clamp(colour.R + ((255 - colour.R) * towardsWhite), 0f, 255f),
        (byte)Math.Clamp(colour.G + ((255 - colour.G) * towardsWhite), 0f, 255f),
        (byte)Math.Clamp(colour.B + ((255 - colour.B) * towardsWhite), 0f, 255f));
}
