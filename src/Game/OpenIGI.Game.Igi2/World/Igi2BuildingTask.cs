using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// A building, I.G.I. 2's <c>Building</c> (building.c): a sector object whose model the world scene places, whose portals the sector
/// graph reads, and whose interior is lit by its <c>Inside ambient</c>.
/// </summary>
/// <remarks>
/// <c>Building_CreateHandler</c> and <c>Building_RegisterHandler</c> register it in the world's sector objects with the radius of its
/// model's bound times its largest scale, and its children after it; the scene, its collision and the sector graph are built from
/// its placement as the level loads. Its run is its parent's, <c>SectorObject_RunHandler</c> (<c>igi2 0x589630</c>): its children, then
/// its attached objects' and its interiors' children, which are no tasks here.
/// </remarks>
public sealed partial class Igi2BuildingTask : Igi2Task, IIgi2PlacedTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Building";

    /// <summary>Gets or sets where it stands.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its turn, as three Euler angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets its model.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the ambient light inside it.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Inside ambient")]
    public Vector3f InsideAmbient { get; set; }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.RunChildren(this);
    }
}
