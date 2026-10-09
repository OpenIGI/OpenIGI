using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>A grouping of level tasks, I.G.I. 2's <c>Container</c>: it runs its children, and that is all.</summary>
/// <remarks>
/// <c>Container_RunHandler</c> (<c>igi2 0x5C6C60</c>) runs each child's run handler in order. The sequel's editor gives it
/// one parameter of its own, which the lightmapper reads.
/// </remarks>
public sealed partial class Igi2ContainerTask : GameTask, IIgi2RunTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = EngineTaskTypes.Container;

    /// <summary>Gets or sets a value indicating whether what it holds is left out of the lightmaps.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Exclude from lightmaps")]
    public bool ExcludeFromLightmaps { get; set; }

    /// <inheritdoc/>
    public void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.RunChildren(this);
    }
}
