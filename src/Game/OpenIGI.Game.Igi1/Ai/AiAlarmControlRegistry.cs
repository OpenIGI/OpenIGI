using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Game.World;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>All authored <c>AlarmControl</c> tasks in one mission.</summary>
/// <remarks>
/// Each control evaluates its two level expressions in task-update order and exports the three exact
/// retail script fields: <c>isTrigger</c>, <c>isAlarm</c> and <c>nTriggerLastTick</c>. This is deliberately
/// separate from a guard's alert latch; the guard script queries the referenced control before deciding
/// whether to claim and run to its alarm trigger.
/// </remarks>
public sealed class AiAlarmControlRegistry
{
    private sealed record Entry(
        DeclaredTask Task,
        AiAlarmControlState State,
        string TriggerExpression,
        string AlarmExpression,
        string OnExpression,
        Func<bool> IsExploded);

    private readonly Dictionary<int, Entry> _controls = [];
    private readonly LevelExpressionRuntime _expressions;

    private AiAlarmControlRegistry(LevelExpressionRuntime expressions) => _expressions = expressions;

    /// <summary>Gets the number of controls collected from the task tree.</summary>
    public int Count => _controls.Count;

    /// <summary>Builds and exports every <c>AlarmControl</c> below <paramref name="root"/>.</summary>
    public static AiAlarmControlRegistry Build(GameTask root, ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(variables);

        AiAlarmControlRegistry registry = new(new LevelExpressionRuntime(variables));
        registry.Collect(root, variables);
        return registry;
    }

    /// <summary>Advances all controls by one fixed tick.</summary>
    public void Tick(int tick)
    {
        foreach (Entry entry in _controls.Values)
        {
            Tick(entry, tick);
        }
    }

    /// <summary>Puts every control into the level's update walk, each where the tree places it.</summary>
    /// <param name="walker">The level walk.</param>
    /// <param name="tick">Reads the tick a control stamps when it triggers.</param>
    /// <param name="ticked">Runs after a control has updated.</param>
    /// <remarks>
    /// The control's update (<c>0x4408A0</c>) runs its base type's update -- the walk of its children -- first,
    /// then latches the previous alarm and evaluates its two expressions.
    /// </remarks>
    public void AttachTo(LevelUpdateWalker walker, Func<int> tick, Action<int>? ticked = null)
    {
        ArgumentNullException.ThrowIfNull(walker);
        ArgumentNullException.ThrowIfNull(tick);
        foreach (Entry entry in _controls.Values)
        {
            walker.Bind(entry.Task, task =>
            {
                walker.RunChildren(task);
                Tick(entry, tick());
                ticked?.Invoke(task.Id);
            });
        }
    }

    private void Tick(Entry entry, int tick)
    {
        // AlarmControl is a Terminal (0x440710 registers it under word_53C680), and its update (0x4408A0) runs the
        // terminal's first: isOn (+576) is the On expression -- or stays on when there is none -- and is forced off
        // once the panel has exploded (+400). Exploded or off, the control raises nothing: Generator_99.isOn powers
        // the alarms of missions 5 and 7, and !Terminal_94.isHacked those of mission 9.
        bool exploded = entry.IsExploded();
        bool on = !exploded && _expressions.EvaluateCondition(entry.OnExpression, emptyValue: true, entry.Task);
        bool? trigger = string.IsNullOrWhiteSpace(entry.TriggerExpression)
            ? null
            : _expressions.EvaluateCondition(entry.TriggerExpression, emptyValue: false, entry.Task);
        entry.State.UpdateWithAlarmExpression(
            tick,
            disabled: exploded,
            enabled: on,
            trigger,
            () => string.IsNullOrWhiteSpace(entry.AlarmExpression)
                ? null
                : _expressions.EvaluateCondition(entry.AlarmExpression, emptyValue: false, entry.Task));
    }

    /// <summary>Gets whether a task id names a control that is sounding now.</summary>
    public bool IsOn(int id) => _controls.TryGetValue(id, out Entry? entry) && entry.State.IsOn;

    /// <summary>Gets whether any control at all is sounding now — the mission-wide alarm state.</summary>
    public bool AnyOn
    {
        get
        {
            foreach (Entry entry in _controls.Values)
            {
                if (entry.State.IsOn)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Gets a control's state for diagnostics and event delivery.</summary>
    public bool TryGet(int id, out AiAlarmControlState? state)
    {
        if (_controls.TryGetValue(id, out Entry? entry))
        {
            state = entry.State;
            return true;
        }

        state = null;
        return false;
    }

    private void Collect(GameTask task, ScriptVarRegistry variables)
    {
        if (task is DeclaredTask declared
            && task.Id != GameTask.NoId
            && task.Type.Name == "AlarmControl")
        {
            AiAlarmControlState state = new();
            string explodedSymbol = $"{declared.Name}.isExploded";
            Entry entry = new(
                declared,
                state,
                ReadString(declared, "Trigger Expression"),
                ReadString(declared, "Alarm Expression"),
                ReadString(declared, "On Expression"),
                () => variables.TryRead(explodedSymbol, out QvmValue blown) && blown.ToInt32() != 0);
            _controls[task.Id] = entry;

            // AlarmControl_RegisterScriptSymbolsHandler exports exactly these three names. They are
            // live aliases and the expressions may refer back to the control's previous isAlarm value.
            variables.BindBool(task, "isTrigger", () => state.IsTriggered, _ => { });
            variables.BindBool(task, "isAlarm", () => state.IsOn, _ => { });
            variables.BindInt(task, "nTriggerLastTick", () => state.LastTriggeredTick, _ => { });
        }

        foreach (GameTask child in task.Children)
        {
            Collect(child, variables);
        }
    }

    private static string ReadString(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? raw) && raw is string value ? value : string.Empty;
}
