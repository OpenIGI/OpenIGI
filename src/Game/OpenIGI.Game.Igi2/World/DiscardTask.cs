using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>The ground one <see cref="DiscardTask"/> takes away, as the box the renderer tests against.</summary>
/// <param name="MinX">The west edge, in world units.</param>
/// <param name="MinY">The south edge.</param>
/// <param name="MaxX">The east edge.</param>
/// <param name="MaxY">The north edge.</param>
/// <remarks>
/// <para>
/// <b>Two dimensions, not three.</b> <c>DiscardTask_IsDiscardVertex</c> (<c>igi2ed 0x65A670</c>) compares
/// X against the pair at <c>+344</c>/<c>+348</c> and Y against <c>+356</c>/<c>+352</c> and stops there;
/// the task's Z and its height are never in the test. A discard is a column through the whole world,
/// which is what lets one box under a hangar floor remove the hillside the hangar is cut into whatever
/// height either happens to sit at.
/// </para>
/// <para>
/// <b>Axis-aligned, even for a rotated task.</b> <c>DiscardTask_UpdateInternalDataHandler</c>
/// (<c>0x65AEE0</c>) forms the four floats straight from <c>Width</c> and <c>Height</c> without
/// consulting the task's gamma. The rotated corners it computes just above feed the editor's 2D discard
/// mesh and the task's collision cube instead, so a turned <c>DiscardTask</c> removes ground its own
/// outline does not cover. That is the original's behaviour rather than an approximation of it.
/// </para>
/// </remarks>
public readonly record struct Igi2TerrainDiscard(double MinX, double MinY, double MaxX, double MaxY)
{
    /// <summary>Builds the box one task describes.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The box, in world units.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <c>Width</c> and <c>Height</c> are full spans in metres and the original halves them as it scales:
    /// it writes <c>±Width * 2048</c>, which is half of <see cref="WorldScene.UnitsPerMetre"/>. So a
    /// <c>Width</c> of 56.5 is a box 56.5 metres across rather than 113.
    /// </remarks>
    public static Igi2TerrainDiscard From(DiscardTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        double halfWidth = task.Width * (WorldScene.UnitsPerMetre / 2d);
        double halfHeight = task.Height * (WorldScene.UnitsPerMetre / 2d);
        return new Igi2TerrainDiscard(
            task.Position.X - halfWidth,
            task.Position.Y - halfHeight,
            task.Position.X + halfWidth,
            task.Position.Y + halfHeight);
    }

    /// <summary>Gets whether a world position stands over discarded ground.</summary>
    /// <param name="x">The world X, in world units.</param>
    /// <param name="y">The world Y.</param>
    /// <returns><see langword="true"/> when the point is inside this box.</returns>
    /// <remarks>
    /// Closed on all four sides, which is the original's own pair of tests: <c>!(x &lt; min) &amp;&amp;
    /// x &lt;= max</c>, twice.
    /// </remarks>
    public bool Contains(double x, double y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

    /// <summary>Gets whether any of a level's boxes covers a world position.</summary>
    /// <param name="regions">The level's boxes.</param>
    /// <param name="x">The world X, in world units.</param>
    /// <param name="y">The world Y.</param>
    /// <returns><see langword="true"/> when at least one box contains the point.</returns>
    public static bool AnyContains(ReadOnlySpan<Igi2TerrainDiscard> regions, double x, double y)
    {
        foreach (Igi2TerrainDiscard region in regions)
        {
            if (region.Contains(x, y))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"terrain discard ({MinX}, {MinY})..({MaxX}, {MaxY})";
}

/// <summary>
/// The ground one <see cref="DiscardTask"/> takes away as the game's own ground query tests it: four floats
/// relative to the terrain, at the client's precision.
/// </summary>
/// <param name="MinX">The west edge, relative to the terrain.</param>
/// <param name="MinY">The south edge.</param>
/// <param name="MaxX">The east edge.</param>
/// <param name="MaxY">The north edge.</param>
/// <remarks>
/// <see cref="Igi2TerrainDiscard"/> is the same box in world doubles for drawing. The ground query needs these
/// floats bit for bit: a body standing on a box's edge is over ground or not by the last bit.
/// </remarks>
public readonly record struct Igi2DiscardBox(float MinX, float MinY, float MaxX, float MaxY)
{
    /// <summary>
    /// The box <c>DiscardTask_UpdateInternalDataHandler</c> (<c>igi2 0x5D1A90</c>) stores: the task's position
    /// less the terrain's, then the half extents either side.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="terrain">The position of the world's terrain.</param>
    /// <returns>The box.</returns>
    public static Igi2DiscardBox From(DiscardTask task, Vector3d terrain)
    {
        ArgumentNullException.ThrowIfNull(task);

        float x = ((Fpu)task.Position.X - terrain.X).F;
        Fpu y = (Fpu)task.Position.Y - terrain.Y;
        Fpu halfWidth = (Fpu)task.Width * 2048f;
        Fpu halfHeight = (Fpu)task.Height * 2048f;
        return new Igi2DiscardBox(((Fpu)x - halfWidth).F, (y - halfHeight).F, (halfWidth + x).F, (halfHeight + y).F);
    }

    /// <summary>
    /// <c>DiscardTask_IsDiscardVertex</c> (<c>igi2 0x5D1850</c>): closed on all four sides; an unordered point is
    /// outside.
    /// </summary>
    /// <param name="x">The X relative to the terrain.</param>
    /// <param name="y">The Y relative to the terrain.</param>
    /// <returns><see langword="true"/> when the box holds the point.</returns>
    public bool Contains(float x, float y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

    /// <summary>Gets whether any box holds a point.</summary>
    /// <param name="boxes">The boxes.</param>
    /// <param name="x">The X relative to the terrain.</param>
    /// <param name="y">The Y relative to the terrain.</param>
    /// <returns><see langword="true"/> when one does.</returns>
    public static bool AnyContains(ReadOnlySpan<Igi2DiscardBox> boxes, float x, float y)
    {
        foreach (Igi2DiscardBox box in boxes)
        {
            if (box.Contains(x, y))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// A hole cut through the ground — I.G.I. 2's <c>DiscardTask</c>, the box inside which the terrain is
/// neither drawn nor stood on.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the sequel's answer to the same problem I.G.I. 1 solves with <c>DiscardTerrain</c>.</b>
/// A level that cuts a vehicle ramp into a hillside, or sinks a hangar below it, has geometry where the
/// height field still says there is ground; without the hole the terrain draws straight through the
/// ramp's floor and the player walks on a hillside inside a building. The first game removes octree
/// cubes; this one removes render vertices, which is a much smaller mechanism because the sequel's
/// ground is a height field rather than a mesh.
/// </para>
/// <para>
/// <b>A triangle goes only when all three of its corners do.</b> <c>TerrainRender_DrawFace</c>
/// (<c>igi2ed 0x66AAB0</c>) ANDs the three per-vertex flags that
/// <c>TerrainRender_CalculateRenderVertex</c> (<c>0x66A740</c>) wrote, so the ground is cut back to the
/// box rather than around it, and the last ring of triangles inside the edge stays to close the hole
/// against whatever building sits in it.
/// </para>
/// <para>
/// The ground query obeys the same flag: <c>Terrain_GetNextZHandler</c> (<c>0x62E620</c>) tests the
/// point before it accepts a surface, so a body over a discard gets no terrain at all and falls to the
/// collision mesh of whatever was built there.
/// </para>
/// <para>
/// Four of the twenty-four shipped levels declare the type — <c>location1/level1</c> for its vehicle
/// hangar, <c>location1/LEVEL6</c>, <c>location2/LEVEL2</c> for its two sewer mouths, and the
/// multiplayer map <c>sandstorm</c> for ten of them. Levels that declare it also declare the three
/// parameters below and no others; the editor's own <c>Update</c> push button is not in level data.
/// </para>
/// </remarks>
public sealed partial class DiscardTask : GameTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "DiscardTask";

    /// <summary>Gets or sets where the box's middle sits — the task's <c>Position</c>.</summary>
    /// <remarks>
    /// The Z is carried because the declaration carries it, and nothing reads it: the discard test is
    /// flat. It is still what the editor places the task's handle at.
    /// </remarks>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets how far the box spans along x, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Width")]
    public float Width { get; set; }

    /// <summary>Gets or sets how far the box spans along y, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Height")]
    public float Height { get; set; }

    /// <summary>Gets the box this task removes, in world units.</summary>
    public Igi2TerrainDiscard Region => Igi2TerrainDiscard.From(this);
}
