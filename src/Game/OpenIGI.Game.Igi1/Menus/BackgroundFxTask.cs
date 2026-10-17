using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi1.Menus;

/// <summary>
/// The slow green vector overlay that turns behind every menu screen.
/// </summary>
/// <remarks>
/// <para>
/// An eighteenth task type — <c>BackgroundFX</c> (<c>0x419760</c>, 36 bytes) — that no script
/// declares. The <em>manager</em> creates one at <c>+10208</c>, and <c>MenuScreen_Render</c>
/// (<c>0x421C20</c>) draws it between the logo and the background.
/// </para>
/// <para>
/// The whole state is one float: a frame counter, incremented by <c>BackgroundFX_Update</c>
/// (<c>0x419800</c>) and nothing else. Every position in the effect is a function of it, so it needs
/// no per-shape state and never has to be reset.
/// </para>
/// <para>
/// <b>It is drawn twice per frame</b>, at <c>t</c> and at <c>t + 1</c>
/// (<c>BackgroundFX_Render</c>, <c>0x419820</c>). Two passes one frame apart at alpha 0.075 each is
/// what gives the lines their soft doubled edge; drawing it once at 0.15 would not look the same.
/// </para>
/// </remarks>
public sealed class BackgroundFxTask : GameTask
{
    /// <summary>The green every part of the effect is drawn in.</summary>
    /// <remarks>Full green, no red or blue, in all three of the drawing routines.</remarks>
    public const float Green = 1.0f;

    /// <summary>The alpha a circle is drawn at.</summary>
    /// <remarks><c>0x419630</c> writes <c>0.075</c>.</remarks>
    public const float CircleAlpha = 0.075f;

    /// <summary>The alpha a line is drawn at.</summary>
    /// <remarks>
    /// <c>0x3D97AE5A</c> in both the polyline and the grid routines, which is <c>2f / 27f</c> to the
    /// bit — a hair under the circles' 0.075.
    /// </remarks>
    public const float LineAlpha = 2f / 27f;

    /// <summary>The most segments a circle is ever broken into.</summary>
    /// <remarks>
    /// The count is <c>(int)(radius * 0.31415927)</c>, capped at forty — so a circle's smoothness
    /// follows its size on screen, and the constant is a tenth of pi.
    /// </remarks>
    public const int MaxCircleSegments = 40;

    /// <summary>The segments-per-pixel factor, a tenth of pi.</summary>
    public const float CircleSegmentFactor = 0.31415927f;

    /// <summary>Gets the frame counter every position is derived from.</summary>
    public float Time { get; private set; }

    /// <summary>The three layers, in the order they are drawn.</summary>
    /// <remarks>
    /// Each carries its own rotation rate, its own scale pulse and its own centre. The second turns
    /// <em>backwards</em>, which is what stops the three reading as one rigid object.
    /// </remarks>
    public static IReadOnlyList<BackgroundFxLayer> Layers { get; } =
    [
        new BackgroundFxLayer(
            RotationRate: 0.002f,
            ScaleBase: 1.25f,
            ScaleAmplitude: 0.25f,
            ScaleRate: 0.005f,
            ScaleUsesSine: false,
            CentreX: 200f,
            CentreY: 300f,
            Grid: new BackgroundFxGrid(20, 20, 0f, 0f, 300f, 300f),
            Circles: [],
            Lines: []),

        new BackgroundFxLayer(
            RotationRate: -1f / 700f,
            ScaleBase: 1.15f,
            ScaleAmplitude: 0.15f,
            ScaleRate: 0.0025f,
            ScaleUsesSine: false,
            CentreX: 400f,
            CentreY: 256f,
            Grid: null,
            Circles: [new BackgroundFxCircle(0f, 0f, 100f), new BackgroundFxCircle(28f, -28f, 60f)],
            Lines:
            [
                new BackgroundFxLine(0f, 70f, 0f, 300f),
                new BackgroundFxLine(5f, 70f, 5f, 300f),
                new BackgroundFxLine(0f, -80f, 0f, -300f),
                new BackgroundFxLine(90f, 0f, 300f, 0f),
                new BackgroundFxLine(-65f, 65f, -190f, 190f),
            ]),

        new BackgroundFxLayer(
            RotationRate: 1f / 900f,
            ScaleBase: 2.0f,
            ScaleAmplitude: 1.0f,
            ScaleRate: 1f / 300f,
            ScaleUsesSine: true,
            CentreX: 150f,
            CentreY: 256f,
            Grid: null,
            Circles: [new BackgroundFxCircle(0f, -100f, 200f)],
            Lines:
            [
                new BackgroundFxLine(-300f, -100f, 300f, -100f),
                new BackgroundFxLine(-300f, 100f, 300f, 100f),
                new BackgroundFxLine(-200f, 100f, -200f, -100f),
                new BackgroundFxLine(200f, 100f, 200f, -100f),
                new BackgroundFxLine(0f, -400f, 0f, 150f),
            ]),
    ];

    /// <inheritdoc/>
    /// <remarks>The whole of <c>BackgroundFX_Update</c>: add one.</remarks>
    protected override void OnUpdate()
    {
        Time += 1.0f;
        base.OnUpdate();
    }

    /// <summary>Builds the transform for a layer at a moment in time.</summary>
    /// <param name="layer">The layer.</param>
    /// <param name="time">The frame counter.</param>
    /// <returns>The transform.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="layer"/> is <see langword="null"/>.</exception>
    public static BackgroundFxTransform GetTransform(BackgroundFxLayer layer, float time)
    {
        ArgumentNullException.ThrowIfNull(layer);

        float wave = layer.ScaleUsesSine
            ? MathF.Sin(time * layer.ScaleRate)
            : MathF.Cos(time * layer.ScaleRate);

        float scale = (wave * layer.ScaleAmplitude) + layer.ScaleBase;
        float angle = time * layer.RotationRate;

        return new BackgroundFxTransform(
            MathF.Cos(angle) * scale,
            MathF.Sin(angle) * scale,
            layer.CentreX,
            layer.CentreY,
            scale);
    }
}

/// <summary>One layer's rotation, pulse and contents.</summary>
/// <param name="RotationRate">Radians per frame. Negative turns the other way.</param>
/// <param name="ScaleBase">The scale the pulse oscillates about.</param>
/// <param name="ScaleAmplitude">How far the pulse swings.</param>
/// <param name="ScaleRate">The pulse's rate.</param>
/// <param name="ScaleUsesSine">Whether the pulse is a sine rather than a cosine.</param>
/// <param name="CentreX">Where the layer's origin sits, in authored pixels.</param>
/// <param name="CentreY">The origin's vertical position.</param>
/// <param name="Grid">A grid to draw, or <see langword="null"/>.</param>
/// <param name="Circles">Circles to draw.</param>
/// <param name="Lines">Line segments to draw.</param>
public sealed record BackgroundFxLayer(
    float RotationRate,
    float ScaleBase,
    float ScaleAmplitude,
    float ScaleRate,
    bool ScaleUsesSine,
    float CentreX,
    float CentreY,
    BackgroundFxGrid? Grid,
    IReadOnlyList<BackgroundFxCircle> Circles,
    IReadOnlyList<BackgroundFxLine> Lines);

/// <summary>A rotation-and-scale transform with a translation.</summary>
/// <param name="M0">Row one, column one.</param>
/// <param name="M1">Row one, column two.</param>
/// <param name="TranslateX">The horizontal translation, which is the layer's centre.</param>
/// <param name="TranslateY">The vertical translation.</param>
/// <param name="Scale">The uniform scale, kept separately because a circle's radius uses it directly.</param>
/// <remarks>
/// Built by <c>0x4192E0</c> as <c>[cos·s, sin·s; -sin·s, cos·s]</c>, so the second row is the first
/// one's perpendicular and does not need storing. The translation slots are the layer's width and
/// height fields, which is how the layer's centre reaches the maths.
/// </remarks>
public readonly record struct BackgroundFxTransform(float M0, float M1, float TranslateX, float TranslateY, float Scale)
{
    /// <summary>Maps a point out of the layer's space into authored pixels.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <returns>The transformed point.</returns>
    public (float X, float Y) Apply(float x, float y) =>
        ((x * M0) + (y * -M1) + TranslateX, (x * M1) + (y * M0) + TranslateY);
}

/// <summary>A circle in a layer's own space.</summary>
/// <param name="X">The centre's horizontal position.</param>
/// <param name="Y">The centre's vertical position.</param>
/// <param name="Radius">The radius, which the layer's scale multiplies.</param>
public readonly record struct BackgroundFxCircle(float X, float Y, float Radius);

/// <summary>A line segment in a layer's own space.</summary>
/// <param name="X0">The first end's horizontal position.</param>
/// <param name="Y0">The first end's vertical position.</param>
/// <param name="X1">The second end's horizontal position.</param>
/// <param name="Y1">The second end's vertical position.</param>
public readonly record struct BackgroundFxLine(float X0, float Y0, float X1, float Y1);

/// <summary>A grid of lines filling a rectangle.</summary>
/// <param name="Columns">The number of steps across.</param>
/// <param name="Rows">The number of steps down.</param>
/// <param name="OriginX">The rectangle's origin, before centring.</param>
/// <param name="OriginY">The origin's vertical position.</param>
/// <param name="Width">The rectangle's width in the layer's space.</param>
/// <param name="Height">The rectangle's height.</param>
/// <remarks>
/// <c>0x419330</c> steps by <b>five percent of the extent</b> per line and runs the loop
/// <c>count + 1</c> times, so twenty steps lay down twenty-one lines and the grid closes on itself.
/// The rectangle is centred on the origin by subtracting half its extent up front.
/// </remarks>
public readonly record struct BackgroundFxGrid(int Columns, int Rows, float OriginX, float OriginY, float Width, float Height)
{
    /// <summary>The fraction of the extent each line advances.</summary>
    public const float Step = 0.05f;
}
