using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Ai;

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
        string AlarmExpression);

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
            bool? trigger = string.IsNullOrWhiteSpace(entry.TriggerExpression)
                ? null
                : _expressions.EvaluateCondition(entry.TriggerExpression, emptyValue: false);
            entry.State.UpdateWithAlarmExpression(
                tick,
                disabled: false,
                enabled: true,
                trigger,
                () => string.IsNullOrWhiteSpace(entry.AlarmExpression)
                    ? null
                    : _expressions.EvaluateCondition(entry.AlarmExpression, emptyValue: false));
        }
    }

    /// <summary>Gets whether a task id names a control that is sounding now.</summary>
    public bool IsOn(int id) => _controls.TryGetValue(id, out Entry? entry) && entry.State.IsOn;

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
            Entry entry = new(
                declared,
                state,
                ReadString(declared, "Trigger Expression"),
                ReadString(declared, "Alarm Expression"));
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
