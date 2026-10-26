using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Computer;

/// <summary>
/// The extent of a level's map on the computer, I.G.I. 2's <c>DefineComputerLimit</c> (definecomputerlimit.c): a centre and the size of
/// a quadrant. The last one made is the computer's (<c>DefineComputerLimit_ptQTask</c>, <c>DefineComputerLimit_GetLimit</c>); it does
/// nothing as the level runs.
/// </summary>
public sealed partial class Igi2DefineComputerLimitTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "DefineComputerLimit";

    /// <summary>Gets or sets the map's centre.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the size of a quadrant, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Quadrant Size (m)")]
    public float QuadrantSize { get; set; }

    /// <summary>Gets the square it lets the map look over.</summary>
    public OpenIGI.Game.Computer.ComputerMapLimit Limit => new(Position, QuadrantSize);

    /// <summary>Makes it the computer's limit, as its create handler does.</summary>
    /// <param name="computer">The computer.</param>
    public void Attach(Igi2Computer computer)
    {
        ArgumentNullException.ThrowIfNull(computer);
        computer.Limit = this;
    }
}
