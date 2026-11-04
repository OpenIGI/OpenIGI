using System.Globalization;
using System.Text;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Events;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Events;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Host;
using OpenIGI.Input;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Desktop.Tests.Completion;

/// <summary>
/// Plays a mission to its end the way its own scripts say it ends. It reads LevelFlow's <c>Complete</c> expression,
/// follows every counter, container, cutscene and message it names down to something the player does, and does that.
/// </summary>
/// <remarks>
/// <para>What the player does is done through the game's own paths. The player stands in an area, walks onto a pickup, and
/// presses and holds the use key on a switch, terminal, cabinet or TBA the interaction scan offers. A named soldier or a car
/// is hit by the LAW rocket's explosion (<c>Explosion0</c>, weapon 12, the only blast that blows a car up). The player is
/// teleported to each target instead of walking there: this tests the mission's logic, not its routes. The player cannot be
/// hurt (the infinite-health cheat), and nothing of the player's own is saved (<c>-nosave</c>).</para>
/// <para>Every tick it works out, from the expression's live values, the first thing still needed. Of an <c>&amp;&amp;</c>,
/// a one-off action comes before standing somewhere, which holds only while the player stays. Of an <c>||</c>, the side fewer
/// steps away. A <c>!</c>, a timer or a cutscene in progress is waited for. A task in a container that is not running needs
/// the container; a timer, animation or terminal needs its On or Run expression; a scripted explosion needs its Explosion
/// expression. An area that counts vehicles needs the car or animation that drives one there, and an area that must hold a
/// companion as well needs the way cleared and the companion's own AI script followed (see <see cref="Escort"/>).</para>
/// </remarks>
internal sealed partial class MissionSolver
{
    private const double OriginAboveFeet = 3877.2;

    private readonly GameHost _host;
    private readonly GameLoop _loop;
    private readonly MissionWorld _mission;
    private readonly Igi1Gameplay _game;
    private readonly PlayerCamera _player;
    private readonly InputState _input;
    private readonly TaskTree _tree;
    private readonly DeclaredTask _levelFlow;
    private readonly Context _context;
    private readonly Dictionary<(string Source, GameTask Owner), ScriptExpression?> _parsed = [];
    private readonly StringBuilder _log = new();
    private readonly HashSet<string> _visiting = [];
    private Goal? _goal;
    private int _goalTicks;
    private int _useStep;
    private int _notes;
    private readonly HashSet<GameTask> _dead = [];
    private readonly Dictionary<GameTask, IReadOnlyList<string>> _counters = [];
    private bool _escorting;
    private readonly Dictionary<GameTask, Queue<(int Tick, Vector3d At)>> _tracks = [];
    private int _missionId;
    private readonly List<Vector3d> _spots = [];
    private int _spot;
    private int _spotSince;

    private MissionSolver(GameHost host, PlayerCamera player, InputState input)
    {
        _host = host;
        _loop = host.Loop();
        _mission = host.Mission();
        _game = host.Gameplay();
        _player = player;
        _input = input;
        _levelFlow = FindLevelFlow(_mission.LevelRoot ?? throw new InvalidOperationException("The mission has no level."));
        _tree = _levelFlow.Tree;
        _context = new Context(_loop.Variables);
        host.EventsForTesting!.Subscribe((in MissionEnded ended) =>
        {
            Outcome ??= ended.Completed;
        });
    }

    /// <summary>Gets how the mission ended: completed, failed, or not yet.</summary>
    public bool? Outcome { get; private set; }

    /// <summary>Gets what the solver did, tick by tick, for a failure message.</summary>
    public string Log => _log.ToString();

    /// <summary>Starts a mission headless and stands a solver over it, once the player is up.</summary>
    /// <param name="host">The host, started.</param>
    /// <returns>The solver.</returns>
    /// <param name="missionId">The mission, whose folder holds its soldiers' AI scripts.</param>
    public static MissionSolver Over(GameHost host, int missionId)
    {
        InputState input = new();
        host.ScriptedViewInput = input;
        PlayerCamera? player = null;
        for (int i = 0; i < 20_000 && player is null; i++)
        {
            if (!host.Step())
            {
                throw new InvalidOperationException("The host stopped before the player came up.");
            }

            player = host.Player();
        }

        return new MissionSolver(host, player ?? throw new InvalidOperationException("No player came up."), input) { _missionId = missionId };
    }

    /// <summary>Gets or sets a file the log is copied to as it grows, so a run that is killed still leaves it.</summary>
    public string? LogFile { get; set; }

    /// <summary>Plays until the mission ends or the ticks run out.</summary>
    /// <param name="maxTicks">The most ticks to play.</param>
    public void Run(int maxTicks) => Pursue(Parse(Text(_levelFlow, "Complete"), _levelFlow), maxTicks);

    /// <summary>Plays until a level expression comes true, the mission ends, or the ticks run out.</summary>
    /// <param name="expression">The expression, in the level's own words.</param>
    /// <param name="maxTicks">The most ticks to play.</param>
    /// <returns>Whether the expression came true.</returns>
    public bool RunUntil(string expression, int maxTicks)
    {
        ScriptExpression? goal = Parse(expression, _levelFlow);
        Pursue(goal, maxTicks, stopWhenTrue: true);
        return goal is not null && Truth(goal);
    }

    /// <summary>Lets the level run with the player doing nothing, until the mission ends or the ticks run out.</summary>
    /// <param name="maxTicks">The most ticks to wait.</param>
    /// <param name="watch">Called after every tick.</param>
    public void Idle(int maxTicks, Action? watch = null)
    {
        _input.Keyboard.SetKey(KeyCode.E, down: false);
        for (int tick = 0; tick < maxTicks && Outcome is null && _host.Step(); tick++)
        {
            watch?.Invoke();
        }
    }

    private void Pursue(ScriptExpression? target, int maxTicks, bool stopWhenTrue = false)
    {
        for (int tick = 0; tick < maxTicks && Outcome is null; tick++)
        {
            if (stopWhenTrue && target is not null && Truth(target))
            {
                break;
            }

            if (tick % 1000 == 0)
            {
                if (LogFile is not null)
                {
                    File.WriteAllText(LogFile, _log.ToString());
                }
            }

            Goal goal = Need(target, _levelFlow) ?? Goal.Wait("nothing to work on");
            if (goal != _goal)
            {
                _log.AppendLine(CultureInfo.InvariantCulture, $"{_loop.Clock.TickCount,7} {goal}");
                _goal = goal;
                _goalTicks = 0;
                _useStep = 0;
                _spots.Clear();
            }

            Act(goal);
            WatchDeaths();

            _goalTicks++;
            if (!_host.Step())
            {
                break;
            }
        }

        if (_mission.Objectives is { IsFailed: true } && Outcome is null)
        {
            Outcome = false;
        }
    }

    // What has to happen for an expression to come true, or null when it already is.
    private Goal? Need(ScriptExpression? node, GameTask owner)
    {
        if (node is null || Truth(node))
        {
            return null;
        }

        switch (node.Kind)
        {
            case ScriptExpressionKind.Group:
                return Need(node.Left, owner);
            case ScriptExpressionKind.Identifier when !node.IsCall:
                return NeedWord(node.Text, owner);
            case ScriptExpressionKind.Operator:
                return node.Operator switch
                {
                    ScriptOperator.LogicalAnd => NeedAll(node, owner),
                    ScriptOperator.LogicalOr => Either(Need(node.Left, owner), Need(node.Right, owner)),
                    ScriptOperator.Equal or ScriptOperator.Greater or ScriptOperator.GreaterOrEqual
                        when Escort(node) is { } clear => clear,
                    ScriptOperator.Equal or ScriptOperator.Greater or ScriptOperator.GreaterOrEqual
                        or ScriptOperator.Less or ScriptOperator.LessOrEqual or ScriptOperator.NotEqual =>
                        node.Left is { Kind: ScriptExpressionKind.Identifier, IsCall: false } left ? NeedWord(left.Text, owner)
                        : node.Right is { Kind: ScriptExpressionKind.Identifier, IsCall: false } right ? NeedWord(right.Text, owner)
                        : Goal.Wait($"{node}"),
                    _ => Goal.Wait($"{node}"),
                };
            default:
                return Goal.Wait($"{node}");
        }
    }

    // Every false term of an && chain is needed. Standing in an area holds only while the player stays there, so the one-off
    // actions come first, the place last, and a term that can only be waited for after both.
    private Goal? NeedAll(ScriptExpression node, GameTask owner)
    {
        List<ScriptExpression> terms = [];
        Flatten(node, terms);
        Goal? stand = null;
        Goal? wait = null;
        foreach (ScriptExpression term in terms)
        {
            switch (Need(term, owner))
            {
                case null:
                    break;
                // A counter compared with a value it is not at, inside its own Add, is a branch for another step: dead now.
                case { Kind: GoalKind.Wait } waiting when waiting.Why.EndsWith("(circular)", StringComparison.Ordinal):
                    return waiting;
                case { Kind: GoalKind.Wait } waiting:
                    wait ??= waiting;
                    break;
                case { Kind: GoalKind.Stand } standing:
                    stand ??= standing;
                    break;
                case { } goal:
                    return goal;
            }
        }

        return stand ?? wait;

        static void Flatten(ScriptExpression node, List<ScriptExpression> into)
        {
            ScriptExpression inner = node;
            while (inner is { Kind: ScriptExpressionKind.Group, Left: { } grouped })
            {
                inner = grouped;
            }

            if (inner is { Kind: ScriptExpressionKind.Operator, Operator: ScriptOperator.LogicalAnd })
            {
                Flatten(inner.Left!, into);
                Flatten(inner.Right!, into);
            }
            else
            {
                into.Add(inner);
            }
        }
    }

    // Either side of an || will do: the one that can be worked on, and of two, the one fewer steps away.
    private static Goal? Either(Goal? left, Goal? right)
    {
        Goal? l = Workable(left);
        Goal? r = Workable(right);
        if (l is not null && r is not null)
        {
            return Steps(r) < Steps(l) ? r : l;
        }

        return l ?? r ?? left ?? right;

        static int Steps(Goal goal) => goal.Why.Split(" > ").Length;
    }

    private static Goal? Workable(Goal? goal) => goal is { Kind: not GoalKind.Wait } ? goal : null;

    // What has to happen for one task's field to turn, named by the words that led to it.
    private Goal? NeedWord(string word, GameTask owner)
    {
        Goal? goal = NeedWordCore(word, owner);
        return goal is { Kind: not GoalKind.Wait } && !goal.Why.StartsWith(word, StringComparison.Ordinal)
            ? goal with { Why = $"{word} > {goal.Why}" }
            : goal;
    }

    private Goal? NeedWordCore(string word, GameTask owner)
    {
        if (!TrySplit(word, out string type, out int id, out string field) || _tree.FromId(id) is not { } target)
        {
            return Goal.Wait(word);
        }

        if (!_visiting.Add(word))
        {
            return Goal.Wait($"{word} (circular)");
        }

        try
        {
            // Nothing in a container that is not running updates or can be worked on: start the container.
            if (InactiveContainer(target) is { } closed)
            {
                return NeedStart(closed, owner) ?? Goal.Wait($"{word} behind {closed.Name}");
            }

            if (target is ConditionalContainerTask container)
            {
                return field == "isRun" ? Need(Parse(container.Condition, container), container) ?? Goal.Wait(word) : Goal.Wait(word);
            }

            switch (type, field)
            {
                case ("HumanSoldier" or "HumanSoldierFemale", "isDead"):
                    return new Goal(GoalKind.Kill, target, word);
                // An object whose script sets it off (its Explosion expression) goes when that comes true; else it is blown up.
                case ("Car" or "ExplodeObject", _):
                    return target is DeclaredTask scripted
                        && Text(scripted, "Explosion expression") is { Length: > 0 } fuse
                        && Need(Parse(fuse, scripted), scripted) is { } lit
                        ? lit
                        : new Goal(GoalKind.Blow, target, word);
            }

            if (target is not DeclaredTask task)
            {
                return Goal.Wait($"{word} ({target.GetType().Name})");
            }

            return (type, field) switch
            {
                ("EditVariable", "nValue") => Need(Parse(Text(task, "Add"), task), task) ?? Goal.Wait(word),
                ("StatusMessage", _) when !_context.Read($"{task.Name}.isSendt") =>
                    Need(Parse(Text(task, "Send"), task), task) ?? Goal.Wait(word),
                ("AreaActivate", "nActive") when Text(task, "Criteria").Trim() == "CRITERIA_VEHICLE" =>
                    NeedVehicle(task, word),
                ("AreaActivate", "nActive") => new Goal(GoalKind.Stand, task, word),
                ("GunPickup" or "GenericPickup" or "AmmoPickup", "isPickedUp") => new Goal(GoalKind.Take, task, word),
                ("Switch" or "Terminal" or "Cabinet" or "GenericTBA" or "Door", _) => NeedUsable(task, owner) ?? new Goal(GoalKind.Use, task, word),
                _ => NeedOn(task) ?? Goal.Wait($"{word} under {Chain(task)}"),
            };
        }
        finally
        {
            _visiting.Remove(word);
        }
    }

    // What starts a container: its word, or for one the level gave no id, its condition.
    private Goal? NeedStart(ConditionalContainerTask container, GameTask owner) =>
        container.Id >= 0
            ? NeedWord($"{container.Name}.isRun", owner)
            : Need(Parse(container.Condition, container), container);

    // A terminal or TBA is offered only while its On Expression holds (0x443C80 asks that it is on).
    private Goal? NeedUsable(DeclaredTask task, GameTask owner)
    {
        if (_mission.Interactions is { } interactions
            && interactions.TryGet(task, out WorldInteraction? interaction)
            && interaction is { IsAvailable: false }
            && Text(task, "On Expression") is { Length: > 0 } on)
        {
            return Need(Parse(on, task), task);
        }

        return null;
    }

    private static IEnumerable<GameTask> Descendants(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            yield return child;
            foreach (GameTask nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    // A timer or anything else that runs itself while its On expression holds (LevelTimer's "On").
    private Goal? NeedOn(DeclaredTask task)
    {
        foreach (string parameter in new[] { "On", "On Expression", "On expression", "Run" })
        {
            if (Text(task, parameter) is { Length: > 0 } on && Parse(on, task) is { } expression && !Truth(expression))
            {
                return Need(expression, task);
            }
        }

        return null;
    }

    private static string Chain(GameTask task)
    {
        StringBuilder chain = new();
        for (GameTask? parent = task.Parent; parent is not null && chain.Length < 300; parent = parent.Parent)
        {
            chain.Append(parent is ConditionalContainerTask c ? $"{c.Name}[run {c.IsRun}, active {c.IsActive}]" : $"{parent.Type.Name}:{parent.Name}");
            chain.Append(" < ");
        }

        return chain.ToString();
    }

    private static ConditionalContainerTask? InactiveContainer(GameTask task)
    {
        for (GameTask? parent = task.Parent; parent is not null; parent = parent.Parent)
        {
            // Not running, or not in the world: either way nothing under it runs.
            if (parent is ConditionalContainerTask container && (!container.IsRun || !container.IsActive))
            {
                return container;
            }
        }

        return null;
    }

    private string Value(string name) => _loop.Variables.TryRead(name, out QvmValue v) ? v.ToDouble().ToString(CultureInfo.InvariantCulture) : "-";

    private void Note(string text)
    {
        if (_notes++ < 200)
        {
            _log.AppendLine(CultureInfo.InvariantCulture, $"{_loop.Clock.TickCount,7}   {text}");
        }
    }

    private static string Fmt(Vector3d p) => string.Create(CultureInfo.InvariantCulture, $"({p.X:0},{p.Y:0},{p.Z:0})");

    private bool Truth(ScriptExpression node)
    {
        ScriptExpressionEvaluator evaluator = new(_context);
        return evaluator.EvaluateReal(node) != 0.0;
    }

    private ScriptExpression? Parse(string source, GameTask owner)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        if (!_parsed.TryGetValue((source, owner), out ScriptExpression? parsed))
        {
            try
            {
                parsed = ScriptExpressionParser.Parse(
                    LevelExpression.Compile(source, owner.Name, LevelExpressionDialect.Igi1),
                    LevelExpressionRuntime.Constant);
            }
            catch (ScriptExpressionException)
            {
                parsed = null;
            }

            _parsed[(source, owner)] = parsed;
        }

        return parsed;
    }

    private static string Text(DeclaredTask task, string parameter) =>
        task.TryGetValue(parameter, out object? raw) && raw is string text ? text : string.Empty;

    private static bool TrySplit(string word, out string type, out int id, out string field)
    {
        type = field = string.Empty;
        id = -1;
        int dot = word.IndexOf('.', StringComparison.Ordinal);
        int underscore = dot > 0 ? word.LastIndexOf('_', dot) : -1;
        if (underscore <= 0
            || !int.TryParse(word.AsSpan(underscore + 1, dot - underscore - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
        {
            return false;
        }

        type = word[..underscore];
        field = word[(dot + 1)..];
        return true;
    }

    private static DeclaredTask FindLevelFlow(GameTask root) =>
        FindLevelFlowOrNull(root) ?? throw new InvalidOperationException("The level has no LevelFlow.");

    private static DeclaredTask? FindLevelFlowOrNull(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            if (child is DeclaredTask { Type.Name: "LevelFlow" } flow)
            {
                return flow;
            }

            if (FindLevelFlowOrNull(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private enum GoalKind
    {
        Wait,
        Stand,
        Ride,
        Take,
        Use,
        Kill,
        Blow,
    }

    private sealed record Goal(GoalKind Kind, GameTask? Task, string Why)
    {
        public static Goal Wait(string why) => new(GoalKind.Wait, null, why);

        public override string ToString() => Kind == GoalKind.Wait ? $"wait   {Why}" : $"{Kind.ToString().ToLowerInvariant(),-6} {Why}";
    }

    private sealed class Context(ScriptVarRegistry variables) : IScriptExpressionContext
    {
        public bool Read(string name) => variables.TryRead(name, out QvmValue value) && value.ToDouble() != 0.0;

        public bool TryGetValue(string name, out QvmValue value)
        {
            if (variables.TryRead(name, out value))
            {
                return true;
            }

            // A name nothing exports reads zero, as the level's own evaluator reads it.
            value = QvmValue.Zero;
            return true;
        }

        public QvmValueKind GetValueKind(string name) => variables.GetValueKind(name);

        public bool TrySetValue(string name, QvmValue value) => false;

        public bool TryInvoke(string name, ScriptArguments arguments, out QvmValue result)
        {
            result = QvmValue.Zero;
            return true;
        }
    }
}
