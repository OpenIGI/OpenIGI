using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Computer;

/// <summary>
/// A level's objectives as the map computer lists them, I.G.I. 2's <c>DefineComputerObjective</c> (definecomputerobjective.c): up to
/// eight, each a text, the objective whose highlights it points to, and the expressions that complete and fail it.
/// </summary>
/// <remarks>
/// <c>DefineComputerObjective_RunHandler</c>: while its <c>Objectives Valid</c> expression does not hold it becomes the set aside;
/// otherwise it is the set in force and each objective's complete and failed flags are its expressions' values, truncated to a
/// byte; an objective with no expression is neither.
/// </remarks>
public sealed partial class Igi2DefineComputerObjectiveTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "DefineComputerObjective";

    /// <summary>The objectives a set holds.</summary>
    public const int Count = 8;

    private LevelExpression? _valid;

    /// <summary>Gets the objectives.</summary>
    public IReadOnlyList<Igi2ComputerObjective> Objectives { get; } = [.. Enumerable.Range(0, Count).Select(static _ => new Igi2ComputerObjective())];

    /// <summary>Gets or sets the computer the set reports to, or <see langword="null"/>.</summary>
    public Igi2Computer? Computer { get; set; }

    /// <summary>Gets or sets the expression under which the set is the one in force; blank for always.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objectives Valid")]
    public string ValidExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the text resource of objective 1.</summary>
    [ScriptParam(ScriptParamType.String32, "Objective 1 Text Resource")]
    public string Text1 { get => Objectives[0].TextResource; set => Objectives[0].TextResource = value; }

    /// <summary>Gets or sets the objective number whose highlights objective 1 points to.</summary>
    [ScriptParam(ScriptParamType.Int32, "Objective 1 Link To Position")]
    public int Link1 { get => Objectives[0].LinkToPosition; set => Objectives[0].LinkToPosition = value; }

    /// <summary>Gets or sets the expression that completes objective 1.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 1 Complete Expression")]
    public string Complete1 { get => Objectives[0].CompleteExpression; set => Objectives[0].CompleteExpression = value; }

    /// <summary>Gets or sets the expression that fails objective 1.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 1 Failed Expression")]
    public string Failed1 { get => Objectives[0].FailedExpression; set => Objectives[0].FailedExpression = value; }

    /// <summary>Gets or sets the text resource of objective 2.</summary>
    [ScriptParam(ScriptParamType.String32, "Objective 2 Text Resource")]
    public string Text2 { get => Objectives[1].TextResource; set => Objectives[1].TextResource = value; }

    /// <summary>Gets or sets the objective number whose highlights objective 2 points to.</summary>
    [ScriptParam(ScriptParamType.Int32, "Objective 2 Link To Position")]
    public int Link2 { get => Objectives[1].LinkToPosition; set => Objectives[1].LinkToPosition = value; }

    /// <summary>Gets or sets the expression that completes objective 2.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 2 Complete Expression")]
    public string Complete2 { get => Objectives[1].CompleteExpression; set => Objectives[1].CompleteExpression = value; }

    /// <summary>Gets or sets the expression that fails objective 2.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 2 Failed Expression")]
    public string Failed2 { get => Objectives[1].FailedExpression; set => Objectives[1].FailedExpression = value; }

    /// <summary>Gets or sets the text resource of objective 3.</summary>
    [ScriptParam(ScriptParamType.String32, "Objective 3 Text Resource")]
    public string Text3 { get => Objectives[2].TextResource; set => Objectives[2].TextResource = value; }

    /// <summary>Gets or sets the objective number whose highlights objective 3 points to.</summary>
    [ScriptParam(ScriptParamType.Int32, "Objective 3 Link To Position")]
    public int Link3 { get => Objectives[2].LinkToPosition; set => Objectives[2].LinkToPosition = value; }

    /// <summary>Gets or sets the expression that completes objective 3.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 3 Complete Expression")]
    public string Complete3 { get => Objectives[2].CompleteExpression; set => Objectives[2].CompleteExpression = value; }

    /// <summary>Gets or sets the expression that fails objective 3.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 3 Failed Expression")]
    public string Failed3 { get => Objectives[2].FailedExpression; set => Objectives[2].FailedExpression = value; }

    /// <summary>Gets or sets the text resource of objective 4.</summary>
    [ScriptParam(ScriptParamType.String32, "Objective 4 Text Resource")]
    public string Text4 { get => Objectives[3].TextResource; set => Objectives[3].TextResource = value; }

    /// <summary>Gets or sets the objective number whose highlights objective 4 points to.</summary>
    [ScriptParam(ScriptParamType.Int32, "Objective 4 Link To Position")]
    public int Link4 { get => Objectives[3].LinkToPosition; set => Objectives[3].LinkToPosition = value; }

    /// <summary>Gets or sets the expression that completes objective 4.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 4 Complete Expression")]
    public string Complete4 { get => Objectives[3].CompleteExpression; set => Objectives[3].CompleteExpression = value; }

    /// <summary>Gets or sets the expression that fails objective 4.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 4 Failed Expression")]
    public string Failed4 { get => Objectives[3].FailedExpression; set => Objectives[3].FailedExpression = value; }

    /// <summary>Gets or sets the text resource of objective 5.</summary>
    [ScriptParam(ScriptParamType.String32, "Objective 5 Text Resource")]
    public string Text5 { get => Objectives[4].TextResource; set => Objectives[4].TextResource = value; }

    /// <summary>Gets or sets the objective number whose highlights objective 5 points to.</summary>
    [ScriptParam(ScriptParamType.Int32, "Objective 5 Link To Position")]
    public int Link5 { get => Objectives[4].LinkToPosition; set => Objectives[4].LinkToPosition = value; }

    /// <summary>Gets or sets the expression that completes objective 5.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 5 Complete Expression")]
    public string Complete5 { get => Objectives[4].CompleteExpression; set => Objectives[4].CompleteExpression = value; }

    /// <summary>Gets or sets the expression that fails objective 5.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 5 Failed Expression")]
    public string Failed5 { get => Objectives[4].FailedExpression; set => Objectives[4].FailedExpression = value; }

    /// <summary>Gets or sets the text resource of objective 6.</summary>
    [ScriptParam(ScriptParamType.String32, "Objective 6 Text Resource")]
    public string Text6 { get => Objectives[5].TextResource; set => Objectives[5].TextResource = value; }

    /// <summary>Gets or sets the objective number whose highlights objective 6 points to.</summary>
    [ScriptParam(ScriptParamType.Int32, "Objective 6 Link To Position")]
    public int Link6 { get => Objectives[5].LinkToPosition; set => Objectives[5].LinkToPosition = value; }

    /// <summary>Gets or sets the expression that completes objective 6.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 6 Complete Expression")]
    public string Complete6 { get => Objectives[5].CompleteExpression; set => Objectives[5].CompleteExpression = value; }

    /// <summary>Gets or sets the expression that fails objective 6.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 6 Failed Expression")]
    public string Failed6 { get => Objectives[5].FailedExpression; set => Objectives[5].FailedExpression = value; }

    /// <summary>Gets or sets the text resource of objective 7.</summary>
    [ScriptParam(ScriptParamType.String32, "Objective 7 Text Resource")]
    public string Text7 { get => Objectives[6].TextResource; set => Objectives[6].TextResource = value; }

    /// <summary>Gets or sets the objective number whose highlights objective 7 points to.</summary>
    [ScriptParam(ScriptParamType.Int32, "Objective 7 Link To Position")]
    public int Link7 { get => Objectives[6].LinkToPosition; set => Objectives[6].LinkToPosition = value; }

    /// <summary>Gets or sets the expression that completes objective 7.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 7 Complete Expression")]
    public string Complete7 { get => Objectives[6].CompleteExpression; set => Objectives[6].CompleteExpression = value; }

    /// <summary>Gets or sets the expression that fails objective 7.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 7 Failed Expression")]
    public string Failed7 { get => Objectives[6].FailedExpression; set => Objectives[6].FailedExpression = value; }

    /// <summary>Gets or sets the text resource of objective 8.</summary>
    [ScriptParam(ScriptParamType.String32, "Objective 8 Text Resource")]
    public string Text8 { get => Objectives[7].TextResource; set => Objectives[7].TextResource = value; }

    /// <summary>Gets or sets the objective number whose highlights objective 8 points to.</summary>
    [ScriptParam(ScriptParamType.Int32, "Objective 8 Link To Position")]
    public int Link8 { get => Objectives[7].LinkToPosition; set => Objectives[7].LinkToPosition = value; }

    /// <summary>Gets or sets the expression that completes objective 8.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 8 Complete Expression")]
    public string Complete8 { get => Objectives[7].CompleteExpression; set => Objectives[7].CompleteExpression = value; }

    /// <summary>Gets or sets the expression that fails objective 8.</summary>
    [ScriptParam(ScriptParamType.VarString, "Objective 8 Failed Expression")]
    public string Failed8 { get => Objectives[7].FailedExpression; set => Objectives[7].FailedExpression = value; }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _valid ??= Igi2LevelExpressions.Create(ValidExpression, Name, level.Natives);
        if (!_valid.IsEmpty && (byte)(long)level.Evaluate(_valid) == 0)
        {
            Computer?.InactiveObjectives = this;
            return;
        }

        Computer?.Objectives = this;
        foreach (Igi2ComputerObjective objective in Objectives)
        {
            objective.Complete ??= Igi2LevelExpressions.Create(objective.CompleteExpression, Name, level.Natives);
            objective.Failed ??= Igi2LevelExpressions.Create(objective.FailedExpression, Name, level.Natives);
            objective.IsComplete = !objective.Complete.IsEmpty && (sbyte)(long)level.Evaluate(objective.Complete) != 0;
            objective.IsFailed = !objective.Failed.IsEmpty && (sbyte)(long)level.Evaluate(objective.Failed) != 0;
        }
    }
}
