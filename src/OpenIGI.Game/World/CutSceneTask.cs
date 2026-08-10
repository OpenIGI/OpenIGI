using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>
/// A level's cut-scene task — the original's <c>CutScene</c>, carrying the parameters level data declares
/// for it and exporting the three script variables other tasks read off it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The fourteen parameters are the ones level 1's <c>objects.qvm</c> declares</b>
/// (<c>Task_DeclareParameters("CutScene", …)</c>), in that order — the registry verifies a class against
/// the declaration, so they have to be present and match by type. The exported variables
/// are the ones the original's symbol registrar binds (<c>0x4F4FB0</c>): <c>isRun</c> (a byte at
/// <c>+96</c>), <c>isFinished</c> (a byte at <c>+97</c>) and <c>nTick</c> (an int at <c>+100</c>).
/// </para>
/// <para>
/// <see cref="CutsceneRuntime"/> reproduces the original Run/Reset/Start/Stop transitions, advances
/// <c>nTick</c>, and latches <c>isFinished</c> after the final nested EditCamera duration.
/// </para>
/// </remarks>
public partial class CutSceneTask : GameTask
{
    /// <summary>The script name the level data instantiates this type under.</summary>
    public const string TypeName = "CutScene";

    /// <summary>Where the cut-scene camera is anchored — <c>Position</c>.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>The camera's orientation — <c>Orientation</c>.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>The expression that drives it while running — <c>Run</c>.</summary>
    [ScriptParam(ScriptParamType.VarString, "Run")]
    public string Run { get; set; } = string.Empty;

    /// <summary>The expression that resets it — <c>Reset</c>.</summary>
    [ScriptParam(ScriptParamType.VarString, "Reset")]
    public string Reset { get; set; } = string.Empty;

    /// <summary>The per-tick time step — <c>Time delta (seconds)</c>.</summary>
    [ScriptParam(ScriptParamType.VarString, "Time delta (seconds)")]
    public string TimeDelta { get; set; } = string.Empty;

    /// <summary>When it starts — <c>Start time (seconds)</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Start time (seconds)")]
    public float StartTime { get; set; }

    /// <summary>Whether it runs from the outset — <c>Initial run</c>.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Initial run")]
    public bool InitialRun { get; set; }

    /// <summary>How fast time passes for it — <c>Time scale</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time scale")]
    public float TimeScale { get; set; }

    /// <summary>The letterbox factor — <c>Viewport height factor</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Viewport height factor")]
    public float ViewportHeightFactor { get; set; }

    /// <summary>How long the letterbox fades in — <c>Viewport height factor fade in time</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Viewport height factor fade in time")]
    public float ViewportFadeIn { get; set; }

    /// <summary>How long the letterbox fades out — <c>Viewport height factor fade out time</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Viewport height factor fade out time")]
    public float ViewportFadeOut { get; set; }

    /// <summary>The time of day it forces — <c>Time of day</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time of day")]
    public float TimeOfDay { get; set; }

    /// <summary>The expression evaluated when it starts — <c>Start expression</c>.</summary>
    [ScriptParam(ScriptParamType.VarString, "Start expression")]
    public string StartExpression { get; set; } = string.Empty;

    /// <summary>The expression evaluated when it stops — <c>Stop expression</c>.</summary>
    [ScriptParam(ScriptParamType.VarString, "Stop expression")]
    public string StopExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the cut-scene is currently running — <c>CutScene+96</c>, <c>isRun</c>.</summary>
    public bool IsRun { get; set; }

    /// <summary>Gets or sets whether the cut-scene has finished — <c>CutScene+97</c>, <c>isFinished</c>.</summary>
    /// <remarks>A newly-created authored scene is unfinished; <see cref="CutsceneRuntime"/> latches this once its final shot ends.</remarks>
    public bool IsFinished { get; set; }

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
