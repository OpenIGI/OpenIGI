using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>
/// A level's cut-scene task — the original's <c>CutScene</c>, reduced to the three script variables other
/// tasks read off it.
/// </summary>
/// <remarks>
/// <para>
/// The port does not play cut-scenes, but a level's AI scripts still read a cut-scene's state: level 1's
/// <c>MISSION:AI/&lt;id&gt;.qsc</c> reads <c>CutScene_1200.nTick</c> in its idle branch, and with no
/// <c>CutScene</c> task registered the symbol did not resolve — <c>guard.qsc</c> faulted with
/// <c>UnknownIdentifier: CutScene_1200.nTick</c> on <em>every</em> idle tick, cutting the script's idle and
/// patrol handling short. Registering the type so the variables export stops that.
/// </para>
/// <para>
/// <b>The three variables are the ones the original's symbol registrar binds</b> (<c>0x4F4FB0</c>):
/// <c>isRun</c> (a byte at <c>+96</c>), <c>isFinished</c> (a byte at <c>+97</c>) and <c>nTick</c> (an int at
/// <c>+100</c>). Their names and widths are read; what a running cut-scene sets them to over time is the
/// cut-scene <em>runtime</em>, which is not built.
/// </para>
/// <para>
/// <b>Stub values, recorded as such.</b> With no cut-scene playing, the task presents the scene as already
/// finished — <c>isRun = 0</c>, <c>isFinished = 1</c>, <c>nTick = 0</c> — so a script that gates normal
/// behaviour on the intro being over lets guards proceed rather than wait for a scene the port never runs.
/// Whether the mission script actually reads <c>nTick</c> as a timeline it advances against is not yet
/// established (flagged in <c>TODO.md</c>); the immediate goal is that the read resolves.
/// </para>
/// </remarks>
public sealed class CutSceneTask : GameTask
{
    /// <summary>The script name the level data instantiates this type under.</summary>
    public const string TypeName = "CutScene";

    /// <summary>Gets or sets whether the cut-scene is currently running — <c>CutScene+96</c>, <c>isRun</c>.</summary>
    public bool IsRun { get; set; }

    /// <summary>Gets or sets whether the cut-scene has finished — <c>CutScene+97</c>, <c>isFinished</c>.</summary>
    /// <remarks>Defaults to <see langword="true"/>: the port does not play the scene, so it reads as done.</remarks>
    public bool IsFinished { get; set; } = true;

    /// <summary>Gets or sets the cut-scene's tick counter — <c>CutScene+100</c>, <c>nTick</c>.</summary>
    public int Ticks { get; set; }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        variables.BindBool(this, "isRun", () => IsRun, value => IsRun = value);
        variables.BindBool(this, "isFinished", () => IsFinished, value => IsFinished = value);
        variables.BindInt(this, "nTick", () => Ticks, value => Ticks = value, size: sizeof(int));
    }
}
