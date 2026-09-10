using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// I.G.I. 2's sky dome: a fixed lattice of vertices whose colours come from the level's five settings.
/// </summary>
/// <remarks>
/// <para>
/// The geometry is not authored and does not vary. <c>SkyDome_InitialiseVertexBuffer</c>
/// (<c>igi2ed 0x5717E0</c>) builds the same lattice every time: an apex, then <b>15 rings of 20
/// vertices</b>, then a skirt of 20 — 321 in all, which is exactly the 5,136-byte buffer it locks at
/// sixteen bytes a vertex.
/// </para>
/// <para>
/// What a level chooses is the five colours and one angle. Everything below is read from the binary
/// rather than shaped to look right.
/// </para>
/// </remarks>
public static class Igi2SkyDome
{
    /// <summary>How far the dome's vertices sit from the viewer.</summary>
    public const float Radius = 8000f;

    /// <summary>How far below the horizon the skirt hangs.</summary>
    /// <remarks>A flat <c>-4000</c>, half the radius, copying the last ring's colour straight down.</remarks>
    public const float SkirtDepth = -4000f;

    /// <summary>How many rings the dome has below its apex.</summary>
    public const int RingCount = 15;

    /// <summary>How many vertices go round each ring.</summary>
    public const int RingVertexCount = 20;

    /// <summary>The angle between one ring and the next, in radians.</summary>
    /// <remarks><c>0.10471976</c> in the binary, which is <c>π / 30</c> — six degrees.</remarks>
    public const float RingStep = MathF.PI / 30f;

    /// <summary>The angle between one vertex of a ring and the next, in radians.</summary>
    /// <remarks><c>0.31415927</c>, which is <c>π / 10</c> — eighteen degrees, twenty to the circle.</remarks>
    public const float VertexStep = MathF.PI / 10f;

    /// <summary>How many vertices the whole dome has.</summary>
    public const int VertexCount = 1 + (RingCount * RingVertexCount) + RingVertexCount;

    /// <summary>Turns a level's <c>SkyDome Angle</c> into the half angle the dome is banded by.</summary>
    /// <param name="degrees">The angle as the level authors it.</param>
    /// <returns>The half angle, in radians.</returns>
    /// <remarks>
    /// <c>FlatSky_UpdateInternalDataHandler</c> (<c>igi2ed 0x654730</c>) computes
    /// <c>angle * 3.1415927 * 0.0055555557</c> — a degrees-to-radians conversion written as a
    /// multiplication by <c>1/180</c> — and passes it to <c>SkyDome_SetHalfAngle</c>.
    /// </remarks>
    public static float HalfAngleOf(float degrees) => degrees * MathF.PI / 180f;

    /// <summary>Fills the dome's six-entry colour table from a level's five settings.</summary>
    /// <param name="sky">The level's sky.</param>
    /// <returns>The table, in the order the dome indexes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sky"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <c>SkyDome_SetColour</c> (<c>igi2ed 0x571730</c>) writes six entries and
    /// <c>FlatSky_UpdateInternalDataHandler</c> supplies them as
    /// <c>(Top, Middle 2, Middle 1, Bottom 2, Bottom 1)</c> — so the pairs arrive the other way round
    /// from how the level declares them, and the top is <b>duplicated</b> into the first two slots.
    /// </para>
    /// <para>
    /// The parameter names say what the pairs are for: <c>ptHalfNorthColour</c> and
    /// <c>ptHalfSouthColour</c>, <c>ptBottomNorthColour</c> and <c>ptBottomSouthColour</c>. The two are
    /// blended <em>around the compass</em>, which is why a sky can be brighter on one side.
    /// </para>
    /// <para>
    /// <c>Snap Colours</c> is what removes that: the handler copies Middle 2 over Middle 1 and Bottom 2
    /// over Bottom 1 before building the table, so each pair becomes one colour and the horizon stops
    /// varying with heading.
    /// </para>
    /// </remarks>
    public static Vector3f[] ColourTableFor(FlatSkyTask sky)
    {
        ArgumentNullException.ThrowIfNull(sky);

        Vector3f middleNorth = sky.MiddleColour2;
        Vector3f middleSouth = sky.SnapColours ? sky.MiddleColour2 : sky.MiddleColour1;
        Vector3f bottomNorth = sky.BottomColour2;
        Vector3f bottomSouth = sky.SnapColours ? sky.BottomColour2 : sky.BottomColour1;

        return [sky.TopColour, sky.TopColour, middleNorth, middleSouth, bottomNorth, bottomSouth];
    }

    /// <summary>Builds the dome's vertices, in the order the original locks them into its buffer.</summary>
    /// <param name="colours">The six-entry table from <see cref="ColourTableFor"/>.</param>
    /// <param name="halfAngle">The half angle, in radians, from <see cref="HalfAngleOf"/>.</param>
    /// <returns>The positions and their colours, <see cref="VertexCount"/> of each.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="colours"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="colours"/> does not hold six entries.</exception>
    public static (Vector3f[] Positions, Bgra32[] Colours) Build(Vector3f[] colours, float halfAngle)
    {
        ArgumentNullException.ThrowIfNull(colours);
        if (colours.Length != 6)
        {
            throw new ArgumentException($"The dome's colour table has six entries, not {colours.Length}.", nameof(colours));
        }

        Vector3f[] positions = new Vector3f[VertexCount];
        Bgra32[] shades = new Bgra32[VertexCount];

        // The apex takes the top colour four times over, so both lerps collapse to it.
        positions[0] = new Vector3f(0f, 0f, Radius);
        shades[0] = Shade(colours[0], colours[0], colours[0], colours[0], 0f, 0f);

        int next = 1;
        float theta = MathF.PI / 2f;
        for (int ring = 0; ring < RingCount; ring++)
        {
            // Stepped before use, so the first ring already sits six degrees below the apex and the
            // fifteenth lands exactly on the horizon.
            theta -= RingStep;

            // Which pair of bands this ring lies between, and how far across it is.
            int band;
            float across;
            if (theta >= halfAngle)
            {
                band = 0;
                across = (theta - halfAngle) / ((MathF.PI / 2f) - halfAngle);
            }
            else
            {
                band = 2;
                float half = halfAngle * 0.5f;
                across = theta < half ? 0f : (theta / half) - 1f;
            }

            float down = 1f - across;
            float ringRadius = MathF.Cos(theta) * Radius;
            float height = MathF.Sin(theta) * Radius;

            float phi = 0f;
            float sweep = 0f;
            for (int i = 0; i < RingVertexCount; i++)
            {
                positions[next] = new Vector3f(MathF.Cos(phi) * ringRadius, MathF.Sin(phi) * ringRadius, height);

                // A triangle wave round the compass: one at due north, nought at due south.
                float around = sweep >= 1f ? sweep - 1f : 1f - sweep;
                shades[next] = Shade(colours[band], colours[band + 1], colours[band + 2], colours[band + 3], around, down);

                next++;
                phi += VertexStep;
                sweep += 0.1f;
            }
        }

        // The skirt repeats the last ring straight down, keeping its colour, so the dome meets the
        // ground rather than ending at the horizon.
        for (int i = 0; i < RingVertexCount; i++)
        {
            int last = next - RingVertexCount;
            positions[next] = new Vector3f(positions[last].X, positions[last].Y, SkirtDepth);
            shades[next] = shades[last];
            next++;
        }

        return (positions, shades);
    }

    /// <summary>Blends four colours the way the dome's vertices are shaded.</summary>
    /// <param name="northAbove">The northern colour of the band above.</param>
    /// <param name="southAbove">The southern colour of the band above.</param>
    /// <param name="northBelow">The northern colour of the band below.</param>
    /// <param name="southBelow">The southern colour of the band below.</param>
    /// <param name="around">How far round the compass, nought to one.</param>
    /// <param name="down">How far down between the two bands, nought to one.</param>
    /// <returns>The vertex colour, opaque.</returns>
    /// <remarks>
    /// <c>SkyDome_InterpolateVertexColour</c> (<c>igi2ed 0x5719B0</c>): lerp each band round the
    /// compass, lerp the two results down, then <b>clamp each channel to nought and one</b>.
    /// <para>
    /// The clamp is where an over-bright authored colour is dealt with — the corpus reaches 1.0385 and
    /// the engine takes it at the parameter and flattens it here, not at load. A reader that clamped on
    /// the way in would agree by accident; one that clamped nowhere would not.
    /// </para>
    /// </remarks>
    public static Bgra32 Shade(
        Vector3f northAbove,
        Vector3f southAbove,
        Vector3f northBelow,
        Vector3f southBelow,
        float around,
        float down)
    {
        Vector3f above = Lerp(northAbove, southAbove, around);
        Vector3f below = Lerp(northBelow, southBelow, around);
        Vector3f blended = Lerp(above, below, down);

        return new Bgra32(Channel(blended.Z), Channel(blended.Y), Channel(blended.X), 255);
    }

    private static Vector3f Lerp(Vector3f a, Vector3f b, float t) =>
        new(((b.X - a.X) * t) + a.X, ((b.Y - a.Y) * t) + a.Y, ((b.Z - a.Z) * t) + a.Z);

    private static byte Channel(float value) =>
        (byte)(value <= 0f ? 0 : value > 1f ? 255 : (int)(value * 255f));
}
