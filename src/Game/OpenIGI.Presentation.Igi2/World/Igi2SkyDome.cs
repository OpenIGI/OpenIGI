using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Game.Igi2;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Presentation.Igi2.World;

/// <summary>
/// I.G.I. 2's sky dome: what the title fixes about the shared lattice, and how a level colours it.
/// </summary>
/// <remarks>
/// <para>
/// The geometry is not authored and does not vary, and it is not I.G.I. 2's own — it is
/// <see cref="SkyDomeGeometry"/>, which both games build identically. What belongs here is the two
/// facts that are this title's: the radius <c>SkyDome_InitialiseVertexBuffer</c>
/// (<c>igi2ed 0x5717E0</c>) hard-codes, and the order <c>FlatSky_UpdateInternalDataHandler</c>
/// (<c>0x654730</c>) hands the level's five colours over in.
/// </para>
/// </remarks>
public static class Igi2SkyDome
{
    /// <summary>How far the dome's vertices sit from the viewer.</summary>
    /// <remarks>
    /// Fixed, where I.G.I. 1 scales its dome to the far plane. It makes no visible difference — the dome
    /// is a backdrop drawn with depth off, so only the direction to each vertex matters.
    /// </remarks>
    public const float Radius = 8000f;

    /// <summary>How far below the horizon the skirt hangs.</summary>
    /// <remarks>A flat <c>-4000</c> in the binary, which is <see cref="SkyDomeGeometry.SkirtFraction"/> of the radius.</remarks>
    public const float SkirtDepth = Radius * SkyDomeGeometry.SkirtFraction;

    /// <summary>How many rings the dome has below its apex.</summary>
    public const int RingCount = SkyDomeGeometry.RingCount;

    /// <summary>How many vertices go round each ring.</summary>
    public const int RingVertexCount = SkyDomeGeometry.RingVertexCount;

    /// <summary>The angle between one ring and the next, in radians.</summary>
    public const float RingStep = SkyDomeGeometry.RingStep;

    /// <summary>The angle between one vertex of a ring and the next, in radians.</summary>
    public const float VertexStep = SkyDomeGeometry.VertexStep;

    /// <summary>How many vertices the whole dome has.</summary>
    public const int VertexCount = SkyDomeGeometry.VertexCount;

    /// <summary>Turns a level's <c>SkyDome Angle</c> into the half angle the dome is banded by.</summary>
    /// <param name="degrees">The angle as the level authors it.</param>
    /// <returns>The half angle, in radians.</returns>
    /// <remarks>
    /// <c>FlatSky_UpdateInternalDataHandler</c> computes <c>angle * 3.1415927 * 0.0055555557</c> — a
    /// degrees-to-radians conversion written as a multiplication by <c>1/180</c> — and passes it to
    /// <c>SkyDome_SetHalfAngle</c>.
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

    /// <summary>Builds the dome's vertices at this title's radius.</summary>
    /// <param name="colours">The six-entry table from <see cref="ColourTableFor"/>.</param>
    /// <param name="halfAngle">The half angle, in radians, from <see cref="HalfAngleOf"/>.</param>
    /// <returns>The positions and their colours, <see cref="VertexCount"/> of each.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="colours"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="colours"/> does not hold six entries.</exception>
    public static (Vector3f[] Positions, Bgra32[] Colours) Build(Vector3f[] colours, float halfAngle)
    {
        ArgumentNullException.ThrowIfNull(colours);
        return SkyDomeGeometry.Build(Radius, halfAngle, colours);
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
    /// <c>SkyDome_InterpolateVertexColour</c> (<c>igi2ed 0x5719B0</c>), which is
    /// <see cref="SkyDomeGeometry.Shade"/>.
    /// </remarks>
    public static Bgra32 Shade(
        Vector3f northAbove,
        Vector3f southAbove,
        Vector3f northBelow,
        Vector3f southBelow,
        float around,
        float down) =>
        SkyDomeGeometry.Shade(northAbove, southAbove, northBelow, southBelow, around, down);
}
