using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Flow;

namespace OpenIGI.Game.World;

/// <summary>Project I.G.I.'s <c>DefineComputerObjective</c>: the shared definition with the first game's six rows.</summary>
/// <remarks>
/// The parameter list is <c>ComputerObjective_Open</c>'s: the <c>Objectives Valid</c> slot, then for each row its text
/// resource, the task it points to, and its complete and failed slots.
/// </remarks>
public sealed partial class Igi1DefineComputerObjectiveTask : DefineComputerObjectiveTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "DefineComputerObjective";

    /// <summary>The number of rows in a Project I.G.I. definition.</summary>
    public const int Count = 6;

    /// <summary>Initializes a new instance of the <see cref="Igi1DefineComputerObjectiveTask"/> class.</summary>
    public Igi1DefineComputerObjectiveTask()
        : base(Count)
    {
    }

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.VarString, "Objectives Valid")]
    public override string ValidExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets row 1's text resource.</summary>
    [ScriptParam(ScriptParamType.String32, "Objective 1 Text Resource")]
    public string Text1 { get => Objectives[0].TextResource; set => Objectives[0].TextResource = value; }

    /// <summary>Gets or sets the id of the task row 1 points to on the map.</summary>
    [ScriptParam(ScriptParamType.Int32, "Objective 1 Link To Position")]
    public int Link1 { get => Objectives[0].LinkToPosition; set => Objectives[0].LinkToPosition = value; }

    /// <summary>Gets or sets the expression that completes row 1.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 1 Complete Expression")]
    public string Complete1 { get => Objectives[0].CompleteExpression; set => Objectives[0].CompleteExpression = value; }

    /// <summary>Gets or sets the expression that fails row 1.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 1 Failed Expression")]
    public string Failed1 { get => Objectives[0].FailedExpression; set => Objectives[0].FailedExpression = value; }

    /// <summary>Gets or sets row 2's text resource.</summary>
    [ScriptParam(ScriptParamType.String32, "Objective 2 Text Resource")]
    public string Text2 { get => Objectives[1].TextResource; set => Objectives[1].TextResource = value; }

    /// <summary>Gets or sets the id of the task row 2 points to on the map.</summary>
    [ScriptParam(ScriptParamType.Int32, "Objective 2 Link To Position")]
    public int Link2 { get => Objectives[1].LinkToPosition; set => Objectives[1].LinkToPosition = value; }

    /// <summary>Gets or sets the expression that completes row 2.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 2 Complete Expression")]
    public string Complete2 { get => Objectives[1].CompleteExpression; set => Objectives[1].CompleteExpression = value; }

    /// <summary>Gets or sets the expression that fails row 2.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 2 Failed Expression")]
    public string Failed2 { get => Objectives[1].FailedExpression; set => Objectives[1].FailedExpression = value; }

    /// <summary>Gets or sets row 3's text resource.</summary>
    [ScriptParam(ScriptParamType.String32, "Objective 3 Text Resource")]
    public string Text3 { get => Objectives[2].TextResource; set => Objectives[2].TextResource = value; }

    /// <summary>Gets or sets the id of the task row 3 points to on the map.</summary>
    [ScriptParam(ScriptParamType.Int32, "Objective 3 Link To Position")]
    public int Link3 { get => Objectives[2].LinkToPosition; set => Objectives[2].LinkToPosition = value; }

    /// <summary>Gets or sets the expression that completes row 3.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 3 Complete Expression")]
    public string Complete3 { get => Objectives[2].CompleteExpression; set => Objectives[2].CompleteExpression = value; }

    /// <summary>Gets or sets the expression that fails row 3.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 3 Failed Expression")]
    public string Failed3 { get => Objectives[2].FailedExpression; set => Objectives[2].FailedExpression = value; }

    /// <summary>Gets or sets row 4's text resource.</summary>
    [ScriptParam(ScriptParamType.String32, "Objective 4 Text Resource")]
    public string Text4 { get => Objectives[3].TextResource; set => Objectives[3].TextResource = value; }

    /// <summary>Gets or sets the id of the task row 4 points to on the map.</summary>
    [ScriptParam(ScriptParamType.Int32, "Objective 4 Link To Position")]
    public int Link4 { get => Objectives[3].LinkToPosition; set => Objectives[3].LinkToPosition = value; }

    /// <summary>Gets or sets the expression that completes row 4.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 4 Complete Expression")]
    public string Complete4 { get => Objectives[3].CompleteExpression; set => Objectives[3].CompleteExpression = value; }

    /// <summary>Gets or sets the expression that fails row 4.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 4 Failed Expression")]
    public string Failed4 { get => Objectives[3].FailedExpression; set => Objectives[3].FailedExpression = value; }

    /// <summary>Gets or sets row 5's text resource.</summary>
    [ScriptParam(ScriptParamType.String32, "Objective 5 Text Resource")]
    public string Text5 { get => Objectives[4].TextResource; set => Objectives[4].TextResource = value; }

    /// <summary>Gets or sets the id of the task row 5 points to on the map.</summary>
    [ScriptParam(ScriptParamType.Int32, "Objective 5 Link To Position")]
    public int Link5 { get => Objectives[4].LinkToPosition; set => Objectives[4].LinkToPosition = value; }

    /// <summary>Gets or sets the expression that completes row 5.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 5 Complete Expression")]
    public string Complete5 { get => Objectives[4].CompleteExpression; set => Objectives[4].CompleteExpression = value; }

    /// <summary>Gets or sets the expression that fails row 5.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 5 Failed Expression")]
    public string Failed5 { get => Objectives[4].FailedExpression; set => Objectives[4].FailedExpression = value; }

    /// <summary>Gets or sets row 6's text resource.</summary>
    [ScriptParam(ScriptParamType.String32, "Objective 6 Text Resource")]
    public string Text6 { get => Objectives[5].TextResource; set => Objectives[5].TextResource = value; }

    /// <summary>Gets or sets the id of the task row 6 points to on the map.</summary>
    [ScriptParam(ScriptParamType.Int32, "Objective 6 Link To Position")]
    public int Link6 { get => Objectives[5].LinkToPosition; set => Objectives[5].LinkToPosition = value; }

    /// <summary>Gets or sets the expression that completes row 6.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 6 Complete Expression")]
    public string Complete6 { get => Objectives[5].CompleteExpression; set => Objectives[5].CompleteExpression = value; }

    /// <summary>Gets or sets the expression that fails row 6.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 6 Failed Expression")]
    public string Failed6 { get => Objectives[5].FailedExpression; set => Objectives[5].FailedExpression = value; }
}
