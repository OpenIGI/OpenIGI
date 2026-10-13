using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Computer;

/// <summary>
/// What the map computer shows of a level, kept by the computer's modules as the level runs: the objective set in force and the
/// one set aside (<c>DefineComputerObjective_ptQTask</c>, <c>…ptInactiveQTask</c>), the map's limit (<c>DefineComputerLimit_ptQTask</c>),
/// the highlights (<c>ComputerHilight_ptQTaskList</c>) and whether the computer may be opened (<c>Computer_isEnabled</c>).
/// </summary>
public sealed class Igi2Computer
{
    /// <summary>The most highlights the module lists, <c>QTaskList_New(32)</c>'s first block (it grows).</summary>
    public const int HilightBlock = 32;

    private readonly List<Igi2ComputerHilightTask> _hilights = [];
    private readonly List<Igi2ComputerPlanTask> _plans = [];

    /// <summary>Gets the objective set that last ran while valid, or <see langword="null"/>.</summary>
    public Igi2DefineComputerObjectiveTask? Objectives { get; internal set; }

    /// <summary>Gets the objective set that last ran while not valid, or <see langword="null"/>.</summary>
    public Igi2DefineComputerObjectiveTask? InactiveObjectives { get; internal set; }

    /// <summary>Gets the map's limit, or <see langword="null"/>.</summary>
    public Igi2DefineComputerLimitTask? Limit { get; internal set; }

    /// <summary>Gets the highlights, in the order they were made.</summary>
    public IReadOnlyList<Igi2ComputerHilightTask> Hilights => _hilights;

    /// <summary>Gets the building plans, in the order they were made.</summary>
    public IReadOnlyList<Igi2ComputerPlanTask> Plans => _plans;

    /// <summary>Gets or sets a value indicating whether the computer may be opened, <c>Computer_isEnabled</c>.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets an objective of the set in force, <c>DefineComputerObjective_GetObjective</c>.</summary>
    /// <param name="index">The objective, nought to seven.</param>
    /// <returns>The objective, or <see langword="null"/> with no set.</returns>
    public Igi2ComputerObjective? GetObjective(int index) => Objectives?.Objectives[index];

    /// <summary>Gets an objective of the set aside, <c>DefineComputerObjective_GetInactiveObjective</c>.</summary>
    /// <param name="index">The objective, nought to seven.</param>
    /// <returns>The objective, or <see langword="null"/> with no set.</returns>
    public Igi2ComputerObjective? GetInactiveObjective(int index) => InactiveObjectives?.Objectives[index];

    /// <summary>The positions of the placed tasks the highlights of an objective point at, <c>ComputerHilight_GetEnumObjectivePos</c>.</summary>
    /// <param name="objective">The objective.</param>
    /// <param name="positionOf">The position of a task by id, or <see langword="null"/> for none.</param>
    /// <returns>Every position found, the first being the primary.</returns>
    public IReadOnlyList<Core.Numerics.Vector3d> ObjectivePositions(int objective, Func<int, Core.Numerics.Vector3d?> positionOf)
    {
        ArgumentNullException.ThrowIfNull(positionOf);
        List<Core.Numerics.Vector3d> found = [];
        foreach (Igi2ComputerHilightTask hilight in _hilights)
        {
            if (hilight.Objective == objective && positionOf(hilight.ObjectiveTaskId) is { } position)
            {
                found.Add(position);
            }
        }

        return found;
    }

    /// <summary>Forgets the last level's computer, as its tasks' deletes do.</summary>
    public void BeginLevel()
    {
        Objectives = null;
        InactiveObjectives = null;
        Limit = null;
        _hilights.Clear();
        _plans.Clear();
        IsEnabled = true;
    }

    /// <summary>Lists a highlight, <c>QTaskList_Register</c> in its create handler.</summary>
    /// <param name="hilight">The highlight.</param>
    internal void Register(Igi2ComputerHilightTask hilight) => _hilights.Add(hilight);

    /// <summary>Lists a plan, <c>QVector_AddElement</c> in its create handler.</summary>
    /// <param name="plan">The plan.</param>
    internal void RegisterPlan(Igi2ComputerPlanTask plan) => _plans.Add(plan);

    /// <summary>The marker constants the computer object module registers.</summary>
    /// <param name="natives">The namespace.</param>
    public static void RegisterConstants(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);
        natives.RegisterConstant("MARKER_NONE", -1);
        natives.RegisterConstant("MARKER_BOX", 0);
        natives.RegisterConstant("MARKER_COLOR_NONE", -1);
        natives.RegisterConstant("MARKER_COLOR_RED", 0);
        natives.RegisterConstant("MARKER_COLOR_YELLOW", 1);
        natives.RegisterConstant("MARKER_COLOR_GREEN", 2);
        natives.RegisterConstant("MARKER_COLOR_BLUE", 4);
    }
}

/// <summary>One objective of a <c>DefineComputerObjective</c>, <c>Objective_t</c>.</summary>
public sealed class Igi2ComputerObjective
{
    /// <summary>Gets or sets the text resource it shows.</summary>
    public string TextResource { get; set; } = string.Empty;

    /// <summary>Gets or sets the objective number whose highlights it points to on the map.</summary>
    public int LinkToPosition { get; set; }

    /// <summary>Gets or sets the expression that completes it.</summary>
    public string CompleteExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that fails it.</summary>
    public string FailedExpression { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether it was complete at the last run.</summary>
    public bool IsComplete { get; internal set; }

    /// <summary>Gets a value indicating whether it had failed at the last run.</summary>
    public bool IsFailed { get; internal set; }

    /// <summary>Gets or sets the complete expression, compiled.</summary>
    internal Flow.Igi2LevelExpression? Complete { get; set; }

    /// <summary>Gets or sets the failed expression, compiled.</summary>
    internal Flow.Igi2LevelExpression? Failed { get; set; }
}
