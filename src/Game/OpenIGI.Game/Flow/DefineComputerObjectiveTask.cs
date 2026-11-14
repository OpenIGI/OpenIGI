using OpenIGI.Game.Level;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Flow;

/// <summary>One row of a <see cref="DefineComputerObjectiveTask"/>: the original's <c>Objective_t</c>.</summary>
public sealed class ComputerObjective
{
    /// <summary>Gets or sets the text resource it shows; a row with none is not shown.</summary>
    public string TextResource { get; set; } = string.Empty;

    /// <summary>Gets or sets what it points to on the map: a task's id in Project I.G.I., a highlight's number in I.G.I. 2.</summary>
    public int LinkToPosition { get; set; }

    /// <summary>Gets or sets the expression that completes it.</summary>
    public string CompleteExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that fails it.</summary>
    public string FailedExpression { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether it was complete when its definition last ran.</summary>
    public bool IsComplete { get; internal set; }

    /// <summary>Gets a value indicating whether it had failed when its definition last ran.</summary>
    public bool IsFailed { get; internal set; }

    /// <summary>Gets or sets the complete expression, compiled.</summary>
    internal LevelExpression? Complete { get; set; }

    /// <summary>Gets or sets the failed expression, compiled.</summary>
    internal LevelExpression? Failed { get; set; }
}

/// <summary>
/// The objectives the map computer lists: the engine's <c>DefineComputerObjective</c>, a set of rows a level may switch
/// between.
/// </summary>
/// <remarks>
/// <para>
/// One type in both games (Project I.G.I. <c>ComputerObjective_Open</c>, run handler <c>0x467410</c>; I.G.I. 2
/// <c>DefineComputerObjective</c>), with six rows in the first and eight in the second, which is why each title has a class
/// of its own for the parameter list and this one holds the handler.
/// </para>
/// <para>
/// Its run handler: a definition whose <c>Objectives Valid</c> slot is empty or holds becomes the computer's -- the last
/// to run is the one shown -- and each of its rows takes its flags from its two slots, off where a row has none. One whose
/// slot does not hold changes nothing, so the computer keeps the last that ran, flags and all.
/// </para>
/// </remarks>
public abstract class DefineComputerObjectiveTask : TickedTask
{
    private readonly ComputerObjective[] _objectives;
    private LevelExpression? _valid;

    /// <summary>Initializes a new instance of the <see cref="DefineComputerObjectiveTask"/> class.</summary>
    /// <param name="rows">How many rows the title's definition has.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rows"/> is not positive.</exception>
    protected DefineComputerObjectiveTask(int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        _objectives = new ComputerObjective[rows];
        for (int row = 0; row < rows; row++)
        {
            _objectives[row] = new ComputerObjective();
        }
    }

    /// <summary>Gets the rows, in order.</summary>
    public IReadOnlyList<ComputerObjective> Objectives => _objectives;

    /// <summary>Gets or sets the script that says whether this definition is the one in force.</summary>
    public abstract string ValidExpression { get; set; }

    /// <inheritdoc/>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (!level.EvaluatesExpressions)
        {
            return;
        }

        _valid ??= level.Compile(ValidExpression, this);
        if (!_valid.IsEmpty && !Flag(level.Evaluate(_valid)))
        {
            OnNotValid();
            return;
        }

        level.CurrentObjectives = this;
        OnValid();
        foreach (ComputerObjective objective in _objectives)
        {
            objective.Complete ??= level.Compile(objective.CompleteExpression, this);
            objective.Failed ??= level.Compile(objective.FailedExpression, this);
            objective.IsComplete = !objective.Complete.IsEmpty && Flag(level.Evaluate(objective.Complete));
            objective.IsFailed = !objective.Failed.IsEmpty && Flag(level.Evaluate(objective.Failed));
        }
    }

    /// <summary>Called when the definition has become the computer's, before its rows are read.</summary>
    protected virtual void OnValid()
    {
    }

    /// <summary>Called when the definition's slot did not hold.</summary>
    protected virtual void OnNotValid()
    {
    }

    // A slot's value as the original stores it: cut to a byte, set when that is not nought.
    private static bool Flag(double value) => unchecked((sbyte)LevelExpression.Truncate(value)) != 0;
}
