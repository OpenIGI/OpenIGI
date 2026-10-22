using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Physics;

/// <summary>A batch of lines traced together, <c>TraceLinesContext_s</c>: their ends, their results, and what may stop them.</summary>
/// <remarks>
/// The engine's own: both games build it the same way, stamped <c>0x41424344</c>, and send it to every object of the world's
/// trees along the lines -- <c>World_TraceLines</c> (<c>0x4CC2A0</c>; <c>igi2 0x551640</c>) -- whose handlers keep the
/// nearer of each line's results. Project I.G.I.'s human answers it with <c>0x45F500</c>, as I.G.I. 2's with
/// <c>Human_TraceLinesHandler</c>.
/// </remarks>
public sealed class WorldLineTrace
{
    private int _count;

    /// <summary>The material row a trace ignores when none is named: row 0, air.</summary>
    /// <remarks><c>World_TraceLinesSpecificMesh</c> (<c>igi2 0x5519C0</c>) sets <c>iIgnoreMaterial</c> to zero.</remarks>
    public const int AirMaterial = 0;

    /// <summary>Initializes a new instance of the <see cref="WorldLineTrace"/> class.</summary>
    /// <param name="lineCount">How many lines the batch traces.</param>
    public WorldLineTrace(int lineCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lineCount);
        Starts = new Vector3d[lineCount];
        Ends = new Vector3d[lineCount];
        Intersections = new WorldIntersection[lineCount];
        Count = lineCount;
        for (int i = 0; i < lineCount; i++)
        {
            Intersections[i] = new WorldIntersection();
        }
    }

    /// <summary>Gets or sets how many of the lines are traced, <c>nNumLines</c>: the first that many.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The count is negative or more than the batch holds.</exception>
    public int Count
    {
        get => _count;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, Starts.Length);
            _count = value;
        }
    }

    /// <summary>Gets each line's start, <c>ptStart</c>.</summary>
    public Vector3d[] Starts { get; }

    /// <summary>Gets each line's end, <c>ptEnd</c>.</summary>
    public Vector3d[] Ends { get; }

    /// <summary>Gets each line's result, <c>ptIntersection</c>.</summary>
    public WorldIntersection[] Intersections { get; }

    /// <summary>Gets or sets which of an object's collision meshes the lines meet, <c>iMesh</c>.</summary>
    public int Mesh { get; set; }

    /// <summary>Gets or sets a value indicating whether portal faces stop the lines, <c>isTracePortals</c>.</summary>
    public bool TracePortals { get; set; }

    /// <summary>Gets or sets the material row faces of which never stop the lines, <c>iIgnoreMaterial</c>.</summary>
    public int IgnoreMaterial { get; set; } = AirMaterial;

    /// <summary>Gets or sets which objects the lines may strike: the trace's evaluate event, as each object type answers it.</summary>
    /// <remarks>Given the struck object's owner. <see langword="null"/> lets every object take part.</remarks>
    public Func<object, bool>? Includes { get; set; }

    /// <summary>Gets or sets the light an unlit hit reads, or <see langword="null"/> for none.</summary>
    public IWorldSceneLighting? Lighting { get; set; }

    /// <summary>
    /// Gets or sets who an object-collision trace is run for, <c>CollisionEvaluateContext_s.ptQTask</c> of the
    /// <c>ObjectCollisionEvaluate</c> event, or <see langword="null"/> for a trace that asks no such event.
    /// </summary>
    /// <remarks>A door reads it: a human's lines that strike it while it moves hold it (<c>Door_TraceLinesHandler</c>, <c>igi2 0x536FE0</c>).</remarks>
    public object? Evaluator { get; set; }
}
