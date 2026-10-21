using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// The sky dome's lattice and its shading, which both games build the same way.
/// </summary>
/// <remarks>
/// <para>
/// This is one implementation because it is one piece of engine, not two that happen to look alike.
/// Read side by side, <c>IGI.exe sub_52CC40</c> and <c>igi2ed SkyDome_InitialiseVertexBuffer</c>
/// (<c>0x5717E0</c>) lay out the same apex, the same <b>fifteen rings of twenty</b>, the same six- and
/// eighteen-degree steps and the same skirt at half the radius below the horizon; <c>sub_52CE90</c> and
/// <c>SkyDome_InterpolateVertexColour</c> (<c>0x5719B0</c>) are the same two lerps and the same clamp.
/// </para>
/// <para>
/// Everything a title chooses is a parameter: the radius, the half angle, and six colours. I.G.I. 1
/// sizes the dome at <c>0.8 ×</c> its far plane; I.G.I. 2 fixes it at 8,000 units. Since the dome is a
/// backdrop drawn with depth off, the radius sets nothing but the units the vertices are expressed in —
/// which is exactly why it can differ between the titles without the geometry differing at all.
/// </para>
/// </remarks>
public static class SkyDomeGeometry
{
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

    /// <summary>How far the skirt hangs below the horizon, as a fraction of the radius.</summary>
    /// <remarks>
    /// Negative half. I.G.I. 2 writes it as a literal <c>-4000</c> against a radius of 8,000, which is
    /// the same rule the first game states as a fraction — one of the several places the two agree
    /// exactly once the radius is factored out.
    /// </remarks>
    public const float SkirtFraction = -0.5f;

    /// <summary>How many vertices the whole dome has.</summary>
    /// <remarks>
    /// 321, which is the 5,136-byte buffer <c>SkyDome_InitialiseVertexBuffer</c> locks at sixteen bytes
    /// a vertex — a position and a packed colour. Arithmetic that closes on the byte.
    /// </remarks>
    public const int VertexCount = 1 + (RingCount * RingVertexCount) + RingVertexCount;

    /// <summary>How many entries the colour table has.</summary>
    public const int ColourCount = 6;

    /// <summary>How many indices the dome draws.</summary>
    public const int IndexCount = (RingVertexCount + (RingCount * RingVertexCount * 2)) * 3;

    /// <summary>The index of the first vertex of the ring that sits on the horizon.</summary>
    public const int HorizonRingStart = 1 + ((RingCount - 1) * RingVertexCount);

    /// <summary>The index of the first vertex of the skirt.</summary>
    public const int SkirtStart = VertexCount - RingVertexCount;

    /// <summary>Builds the dome's vertices, in the order the original locks them into its buffer.</summary>
    /// <param name="radius">How far the vertices sit from the viewer.</param>
    /// <param name="halfAngle">Where the upper band meets the lower, in radians above the horizon.</param>
    /// <param name="colours">
    /// <see cref="ColourCount"/> colours, as the top pair, the middle pair and the bottom pair. Each
    /// pair is a north and a south, blended around the compass.
    /// </param>
    /// <returns>The positions and their colours, <see cref="VertexCount"/> of each.</returns>
    /// <exception cref="ArgumentException"><paramref name="colours"/> is not <see cref="ColourCount"/> long.</exception>
    public static (Vector3f[] Positions, Bgra32[] Colours) Build(
        float radius,
        float halfAngle,
        ReadOnlySpan<Vector3f> colours)
    {
        if (colours.Length != ColourCount)
        {
            throw new ArgumentException(
                $"The dome's colour table has {ColourCount} entries, not {colours.Length}.",
                nameof(colours));
        }

        Vector3f[] positions = new Vector3f[VertexCount];
        Bgra32[] shades = new Bgra32[VertexCount];

        // The apex takes the top colour four times over, so both lerps collapse to it.
        positions[0] = new Vector3f(0f, 0f, radius);
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
            float ringRadius = MathF.Cos(theta) * radius;
            float height = MathF.Sin(theta) * radius;

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

        // The skirt repeats the horizon ring straight down, keeping its colour, so a camera tipped below
        // the horizon sees sky rather than the void.
        for (int i = 0; i < RingVertexCount; i++)
        {
            int last = next - RingVertexCount;
            positions[next] = new Vector3f(positions[last].X, positions[last].Y, radius * SkirtFraction);
            shades[next] = shades[last];
            next++;
        }

        return (positions, shades);
    }

    /// <summary>Stitches the dome's fixed lattice into triangles.</summary>
    /// <returns>The index list, <see cref="IndexCount"/> long.</returns>
    /// <remarks>
    /// A fan from the apex to the first ring, a band between each pair of rings, and a band from the
    /// horizon ring down to the skirt. The lattice never varies, so neither does this.
    /// </remarks>
    public static ushort[] BuildIndices()
    {
        ushort[] indices = new ushort[IndexCount];
        int cursor = 0;

        for (int i = 0; i < RingVertexCount; i++)
        {
            indices[cursor++] = 0;
            indices[cursor++] = (ushort)(1 + i);
            indices[cursor++] = (ushort)(1 + ((i + 1) % RingVertexCount));
        }

        // Every band, including the horizon ring to the skirt, which is why the loop runs to RingCount.
        for (int ring = 0; ring < RingCount; ring++)
        {
            int upper = 1 + (ring * RingVertexCount);
            int lower = upper + RingVertexCount;

            for (int i = 0; i < RingVertexCount; i++)
            {
                int next = (i + 1) % RingVertexCount;
                indices[cursor++] = (ushort)(upper + i);
                indices[cursor++] = (ushort)(lower + i);
                indices[cursor++] = (ushort)(upper + next);

                indices[cursor++] = (ushort)(upper + next);
                indices[cursor++] = (ushort)(lower + i);
                indices[cursor++] = (ushort)(lower + next);
            }
        }

        return indices;
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
    /// Lerp each band round the compass, lerp the two results down, then <b>clamp each channel</b>.
    /// <para>
    /// The clamp is where an over-bright authored colour is dealt with — I.G.I. 2's corpus reaches
    /// 1.0385, and the engine carries it unchanged through the parameter and flattens it here rather
    /// than at load. A reader that clamped on the way in would agree by accident; one that clamped
    /// nowhere would overflow a byte.
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
        Vector3f above = Vector3f.Lerp(northAbove, southAbove, around);
        Vector3f below = Vector3f.Lerp(northBelow, southBelow, around);
        Vector3f blended = Vector3f.Lerp(above, below, down);

        return new Bgra32(Channel(blended.Z), Channel(blended.Y), Channel(blended.X), 255);
    }

    private static byte Channel(float value) =>
        (byte)(value <= 0f ? 0 : value > 1f ? 255 : (int)(value * 255f));
}
