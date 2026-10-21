using OpenIGI.Core.Graphics;

namespace OpenIGI.Game.World;

/// <summary>What the terrain traversal culls and picks a level of detail against.</summary>
/// <remarks>
/// <para>
/// The original passes all of this through globals that <c>sub_4D2540</c> and <c>sub_511500</c> fill
/// immediately before the descent. Gathering them into one value makes the descent a pure function of
/// the tree and the view, which is what lets a test drive it without a renderer.
/// </para>
/// <para>
/// Everything here is in <b>engine units</b>, not metres: the octree's own coordinate space, and the
/// space the camera position arrives in.
/// </para>
/// </remarks>
/// <param name="Camera">
/// The camera, whose <see cref="Camera3D.Orientation"/> rows are the view right, up and forward axes —
/// the same shape as the original's 3×3 at <c>0xBCAAE0</c>.
/// </param>
/// <param name="TangentX">The tangent of the horizontal half field of view.</param>
/// <param name="TangentY">The tangent of the vertical half field of view.</param>
/// <param name="ViewDistance">
/// How far detail is worth resolving, in the units of the traversal's own distance metric. The original
/// derives it from a configured constant scaled by the horizontal field of view, and clamps it to at
/// least <see cref="MinimumViewDistance"/>.
/// </param>
/// <param name="FloorZ">
/// Chunks whose top lies below this are skipped. The original reads it from the water plane a level's
/// script publishes and falls back to <see cref="DefaultFloorZ"/> when there is none.
/// </param>
public readonly record struct TerrainView(
    Camera3D Camera,
    float TangentX,
    float TangentY,
    int ViewDistance,
    long FloorZ)
{
    /// <summary>Gets a value indicating whether the descent keeps the ground all round the camera, not only what it faces.</summary>
    /// <remarks>
    /// Not the original's -- it only ever draws what it faces. For a consumer that is not the screen: the
    /// ground the ray-traced sun is shadowed by. Built from the frustum alone, a hill behind the camera cast
    /// no shadow on the valley in front of it, and the valley went in and out of shadow as the player turned.
    /// The level of detail is the same distance rule either way.
    /// </remarks>
    public bool Panoramic { get; init; }

    /// <summary>The lowest view distance the original will use.</summary>
    public const int MinimumViewDistance = 70;

    /// <summary>The view-distance base for the <b>low</b> terrain-detail setting.</summary>
    public const int LowViewDistance = 96;

    /// <summary>The view-distance base for the <b>medium</b> terrain-detail setting.</summary>
    public const int MediumViewDistance = 129;

    /// <summary>The view-distance base for the <b>high</b> terrain-detail setting — the retail default.</summary>
    public const int HighViewDistance = 163;

    /// <summary>The view distance the original starts from, before the field of view widens it.</summary>
    /// <remarks>
    /// <b>The graphics detail setting chooses this</b>, not the <c>0x54844C</c> initializer. At startup
    /// <c>sub_406390</c> writes the base from the terrain-detail flags: <see cref="LowViewDistance"/>
    /// (96) when the low bit is set, <see cref="MediumViewDistance"/> (129) for medium, and otherwise
    /// <see cref="HighViewDistance"/> (163) — the default with neither bit set, which is what the retail
    /// game runs at. The <c>115</c> read straight out of the image is only the pre-<c>sub_406390</c>
    /// initializer and is never the value the descent uses; hard-coding it drew terrain coarser than even
    /// the medium setting, so near ground resolved detail too close and popped as the camera moved.
    /// </remarks>
    public const int DefaultViewDistance = HighViewDistance;

    /// <summary>The value <see cref="FloorZ"/> takes when a level publishes no water plane.</summary>
    public const long DefaultFloorZ = -0x20000000;

    /// <summary>The near plane the descent clips against, in engine units.</summary>
    public const double NearPlane = 64.0;

    /// <summary>
    /// The circumradius factor a node's extent is widened by before it is tested against the frustum.
    /// </summary>
    /// <remarks>
    /// The original spells it <c>1.7321</c>, which is the square root of three to five digits — the
    /// half-diagonal of a cube of unit half-width. Reproduced literally, because the culling decision has
    /// to match at the boundary.
    /// </remarks>
    public const double CircumradiusFactor = 1.7321;

    /// <summary>The root extent the frustum slack is expressed against.</summary>
    /// <remarks>
    /// The original writes <c>1073741800.0</c> — 2^30 rounded down to seven significant figures, not
    /// <see cref="Formats.World.TerrainOctree.RootExtent"/> exactly. Kept as written.
    /// </remarks>
    public const double SlackRootExtent = 1073741800.0;

    /// <summary>Builds a view from a camera and a symmetric perspective.</summary>
    /// <param name="camera">The camera, positioned in engine units.</param>
    /// <param name="verticalFieldOfView">The full vertical field of view in radians.</param>
    /// <param name="aspectRatio">The viewport's width divided by its height.</param>
    /// <param name="viewDistance">
    /// The distance metric bound, or <see langword="null"/> to derive it from the field of view the way
    /// <see cref="ViewDistanceFor"/> does. Either way it is clamped to <see cref="MinimumViewDistance"/>.
    /// </param>
    /// <param name="floorZ">The floor to cull below.</param>
    /// <returns>The view.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The field of view or the aspect ratio is not positive.</exception>
    public static TerrainView FromPerspective(
        in Camera3D camera,
        float verticalFieldOfView,
        float aspectRatio,
        int? viewDistance = null,
        long floorZ = DefaultFloorZ)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(verticalFieldOfView);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(aspectRatio);

        float tangentY = MathF.Tan(verticalFieldOfView * 0.5f);
        float tangentX = tangentY * aspectRatio;

        return new TerrainView(
            camera,
            tangentX,
            tangentY,
            viewDistance is { } given ? Math.Max(MinimumViewDistance, given) : ViewDistanceFor(tangentX),
            floorZ);
    }

    /// <summary>Derives the view distance from the horizontal field of view.</summary>
    /// <param name="tangentX">The tangent of the horizontal half field of view.</param>
    /// <param name="configured">The configured base distance.</param>
    /// <returns>The distance metric bound, at least <see cref="MinimumViewDistance"/>.</returns>
    /// <remarks>
    /// <c>sub_4D2540</c> divides the configured distance by the square root of the horizontal tangent,
    /// but only when that tangent is at or below one — so narrowing the field of view pushes detail
    /// further out, and widening it past 45° does not pull detail in any closer.
    /// </remarks>
    public static int ViewDistanceFor(float tangentX, int configured = DefaultViewDistance)
    {
        double distance = tangentX is > 0f and <= 1f
            ? configured / Math.Sqrt(tangentX)
            : configured;

        return Math.Max(MinimumViewDistance, (int)distance);
    }

    /// <summary>Gets the frustum slack the root's children are tested with, along X.</summary>
    /// <remarks>Halved once per level of the descent, so a node at depth <c>d</c> uses this over 2^(d-1).</remarks>
    public double SlackX => Slack(TangentX);

    /// <summary>Gets the frustum slack the root's children are tested with, along Y.</summary>
    public double SlackY => Slack(TangentY);

    private static double Slack(float tangent) =>
        CircumradiusFactor / Math.Cos(Math.Atan2(tangent, 1.0)) * SlackRootExtent;
}
