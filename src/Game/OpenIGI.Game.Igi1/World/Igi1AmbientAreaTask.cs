using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Level;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// A box that plays one of its sounds, now and then, while the listener is inside it: Project I.G.I.'s <c>AmbientArea</c>.
/// </summary>
/// <remarks>
/// <para>
/// The parameter list is the type's own (<c>AmbientArea</c>'s open, under <c>EditorRigidObj</c>): where it is, how it is
/// turned, its size, the least gap between sounds and the random wait added to it, and the sounds it draws from. I.G.I. 2's
/// type of the same name has thirteen parameters and another handler, so the two titles do not share this class.
/// </para>
/// <para>
/// Its run handler (<c>0x44C370</c>) is <see cref="AmbientAreaRegistry"/>'s step for it, which holds the level's random
/// stream and the listener; the task runs it where the level's walk reaches it, then its children.
/// </para>
/// </remarks>
public sealed partial class Igi1AmbientAreaTask : TickedTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = AmbientAreaRegistry.TaskTypeName;

    /// <summary>Gets or sets the box's centre.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the box's orientation, as three angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets the box's half extents, in metres.</summary>
    [ScriptParam(ScriptParamType.Real64x3, "Size")]
    public Vector3d Size { get; set; }

    /// <summary>Gets or sets the least gap between two sounds, in seconds.</summary>
    [ScriptParam(ScriptParamType.Real64, "Min Delay")]
    public double MinDelay { get; set; }

    /// <summary>Gets or sets the most that is added to the gap at random, in seconds.</summary>
    [ScriptParam(ScriptParamType.Real64, "Random wait")]
    public double RandomWait { get; set; }

    /// <summary>Gets or sets the sounds it draws from, separated by spaces, commas or semicolons.</summary>
    [ScriptParam(ScriptParamType.String256, "SoundDef")]
    public string SoundDef { get; set; } = string.Empty;

    /// <summary>Gets or sets the area's running state, once the mission's ambience has listed it.</summary>
    internal PlacedAmbientArea? Area { get; set; }

    /// <summary>Gets or sets the mission's ambience, which steps the area; none until it is built.</summary>
    internal AmbientAreaRegistry? Ambience { get; set; }

    /// <inheritdoc/>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Area is { } area)
        {
            Ambience?.RunFromWalk(area);
        }

        level.RunChildren(this);
    }
}
