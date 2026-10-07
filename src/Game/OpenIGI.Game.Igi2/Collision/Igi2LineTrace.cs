using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>The light a trace reads where it strikes an unlit surface: the level's ambient and its sun.</summary>
public interface IIgi2SceneLighting
{
    /// <summary>Gets the ambient colour the level's global light sets (the globals <c>igi2 0x7F9166C</c>–<c>0x7F91674</c>).</summary>
    Vector3f Ambient { get; }

    /// <summary>Gets the sun, <c>SkyDome_ptDirlight</c>, if the level has one.</summary>
    /// <param name="direction">The direction the sun's light shines toward a surface it lights, <c>tDir</c>.</param>
    /// <param name="colour">The colour of a lit face, <c>tFrontColor</c>.</param>
    /// <returns>Whether there is a sun.</returns>
    bool TryGetSun(out Vector3f direction, out Vector3f colour);
}

/// <summary>A batch of lines traced together, <c>TraceLinesContext_s</c>: their ends, their results, and what may stop them.</summary>
public sealed class Igi2LineTrace
{
    private int _count;

    /// <summary>The material row a trace ignores when none is named: row 0, air.</summary>
    /// <remarks><c>World_TraceLinesSpecificMesh</c> (<c>igi2 0x5519C0</c>) sets <c>iIgnoreMaterial</c> to zero.</remarks>
    public const int AirMaterial = 0;

    /// <summary>Initializes a new instance of the <see cref="Igi2LineTrace"/> class.</summary>
    /// <param name="lineCount">How many lines the batch traces.</param>
    public Igi2LineTrace(int lineCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lineCount);
        Starts = new Vector3d[lineCount];
        Ends = new Vector3d[lineCount];
        Intersections = new Igi2Intersection[lineCount];
        Count = lineCount;
        for (int i = 0; i < lineCount; i++)
        {
            Intersections[i] = new Igi2Intersection();
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
    public Igi2Intersection[] Intersections { get; }

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
    public IIgi2SceneLighting? Lighting { get; set; }
}
