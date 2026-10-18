using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.World;

/// <summary>
/// Wires every <see cref="ConditionalContainerTask"/> in a loaded level with its live condition evaluator.
/// </summary>
/// <remarks>
/// <para>
/// This evaluates the initial registration state. <see cref="CutsceneRuntime"/> then reproduces the
/// per-tick <c>ConditionalContainer_RunHandler</c>, including start/stop expressions, and exposes the result as
/// <see cref="ConditionalContainerTask.IsRun"/>. The operands are the script variables the level's tasks
/// export — <c>CutScene_1203.isFinished</c>, <c>EditVariable_199.nValue</c>,
/// <c>ConditionalContainer_4007.isRun</c> — resolved through the same
/// <see cref="ScriptVarRegistry"/> a mission script reads.
/// </para>
/// <para>
/// A name the expression cannot resolve reads zero, matching the script evaluator's normal fallback.
/// </para>
/// </remarks>
public sealed class ConditionalContainerGate
{
    private const string LogCategory = "Level";

    private readonly VariablesContext _context;
    private readonly IOpenIgiLogger _logger;

    // Each condition is compiled once per owner: this. names the container the condition belongs to.
    private readonly Dictionary<(string Condition, ConditionalContainerTask Owner), LevelExpression> _expressions = [];

    // Diagnostic (headless verification only): IGI_DIAG_FORCE_CC=3902 or IGI_DIAG_FORCE_CC=3902:Expr forces
    // those container ids open regardless of their authored condition -- optionally until the given
    // expression is true -- so an area-gated subtree can be exercised without walking a frozen player into
    // an objective zone. Repeated ids overwrite cleanly (last entry wins) rather than throwing, so a stray
    // trailing comma or a double-listed id cannot break diagnostic startup.
    private readonly Dictionary<int, string?> _forcedContainers = Environment
        .GetEnvironmentVariable("IGI_DIAG_FORCE_CC") is { Length: > 0 } force
        ? force.Split(',')
            .Where(entry => int.TryParse(entry.Split(':', 2)[0], out _))
            .GroupBy(entry => int.Parse(entry.Split(':', 2)[0], System.Globalization.CultureInfo.InvariantCulture))
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    string[] parts = group.Last().Split(':', 2);
                    return parts is [_, var gateExpr] and not [_, ""] ? gateExpr : null;
                })
        : [];

    /// <summary>Initializes a new instance of the <see cref="ConditionalContainerGate"/> class.</summary>
    /// <param name="variables">The level's exported script variables, which the conditions read.</param>
    /// <param name="logger">Where to report a condition that would not parse; defaults to a sink.</param>
    /// <exception cref="ArgumentNullException"><paramref name="variables"/> is <see langword="null"/>.</exception>
    public ConditionalContainerGate(ScriptVarRegistry variables, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(variables);
        _context = new VariablesContext(variables);
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Gets whether a task is a <see cref="ConditionalContainerTask"/> that is not running, and whose
    /// subtree a walk should therefore skip.
    /// </summary>
    /// <param name="task">The task to test.</param>
    /// <returns><see langword="true"/> when the task's subtree is not registered into the world.</returns>
    public static bool IsDormant(GameTask? task) =>
        task is ConditionalContainerTask container && !container.IsActive;

    /// <summary>Gets whether a task and every conditional ancestor are registered in the live world.</summary>
    public static bool IsTaskActive(GameTask? task)
    {
        for (GameTask? current = task; current is not null; current = current.Parent)
        {
            if (current is ConditionalContainerTask container && !container.IsActive)
            {
                return false;
            }

            // A GuardGenerator's nested HumanSoldier is an authored template until the generator's
            // first successful update. IGI1's 0x488000 clone path only registers that live copy then;
            // keeping the parsed template out of runtime walks reproduces the same distinction.
            if (current is GuardGeneratorTask generator && !generator.IsSpawned)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Wires and evaluates every conditional container under a level root.</summary>
    /// <param name="levelRoot">The task the level's subtree hangs off.</param>
    /// <returns>The number of conditional containers that are dormant (their subtrees hidden).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levelRoot"/> is <see langword="null"/>.</exception>
    public int Prepare(GameTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);

        List<ConditionalContainerTask> containers = [];
        Collect(levelRoot, containers);

        foreach (ConditionalContainerTask container in containers)
        {
            container.ConditionResolver = Resolve;
        }

        // Force each result now, so the isRun variables are settled before any walk reads them and the
        // work is not repeated per walk. A container whose condition names another resolves it lazily.
        int dormant = 0;
        foreach (ConditionalContainerTask container in containers)
        {
            if (!container.IsRun)
            {
                dormant++;
            }
        }

        // Registration starts equal to the first result. (The original constructs every container active
        // and its first tick deactivates the false ones before they run; nothing reads the difference.)
        foreach (ConditionalContainerTask container in containers)
        {
            container.IsActive = container.IsRun;
        }

        if (containers.Count > 0)
        {
            _logger.Information(
                LogCategory,
                $"{containers.Count} conditional container(s); {dormant} dormant (their subtrees are not "
                    + "registered, as the original leaves cut-scene actors until their scene runs).");
        }

        return dormant;
    }

    private bool? Resolve(ConditionalContainerTask container)
    {
        if (_forcedContainers.TryGetValue(container.Id, out string? forcedCondition))
        {
            if (forcedCondition is null)
            {
                return true;
            }

            return Evaluate(container, forcedCondition);
        }

        if (string.IsNullOrWhiteSpace(container.Condition))
        {
            // With no condition the original leaves isRun as it is (0x4874E0 asks sub_4F16C0 first).
            return null;
        }

        return Evaluate(container, container.Condition);
    }

    /// <summary>Compiles (once, cached) and evaluates one condition expression against the level variables.</summary>
    private bool Evaluate(ConditionalContainerTask container, string condition)
    {
        if (!_expressions.TryGetValue((condition, container), out LevelExpression? expression))
        {
            expression = LevelExpressionRuntime.Create(condition, container);
            _expressions[(condition, container)] = expression;
            if (expression.IsEmpty)
            {
                _logger.Warning(
                    LogCategory,
                    $"Conditional container condition '{condition}' did not parse; treating it as running.");
            }
        }

        if (expression.IsEmpty)
        {
            return true;
        }

        // The float walk, chopped into the isRun byte (0x4874E0 stores (char)(__int64)sub_4F16E0). Each evaluation makes its
        // own evaluator, so resolving one container's condition can re-enter this method through another container's
        // exported isRun without disturbing the outer one's per-run error state or its this owner.
        return LevelExpression.Flag(expression.Evaluate(_context));
    }

    private static void Collect(GameTask task, List<ConditionalContainerTask> into)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is ConditionalContainerTask { SettlesOutsideTheTick: true } container)
            {
                into.Add(container);
            }

            Collect(child, into);
        }
    }

    // The level's exported script variables. The conditions read task fields (Name.field) and never call natives, so
    // an invoke answers nothing; an unknown name reads zero -- the value an unmodelled progress flag holds at the
    // start. The compile has already answered this. and .isExists.
    private sealed class VariablesContext(ScriptVarRegistry variables) : IScriptExpressionContext
    {
        public bool TryGetValue(string name, out QvmValue value) => variables.TryRead(name, out value);

        public QvmValueKind GetValueKind(string name) => variables.GetValueKind(name);

        public bool TrySetValue(string name, QvmValue value) => variables.TryWrite(name, value);

        public bool TryInvoke(string name, ScriptArguments arguments, out QvmValue result)
        {
            result = QvmValue.Zero;
            return false;
        }
    }
}
