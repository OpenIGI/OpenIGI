using OpenIGI.Host.Editor;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Qsc;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// The selected task's identity and parameters as a property grid. Every edit goes through the history; a scrub
/// is one undo step however many frames it takes.
/// </summary>
internal sealed class InspectorPanel : EditorElement
{
    private static readonly string[] ColorLabels = ["R", "G", "B"];

    private readonly MissionWorkspace _workspace;
    private readonly IEditorHost _host;
    private readonly PropertyGrid _grid;
    private MissionTask? _shown;
    private int _shownRevision = -1;
    private string? _shownDetails;

    /// <summary>Initializes a new instance of the <see cref="InspectorPanel"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="workspace">The open mission and its selection.</param>
    /// <param name="host">The running game, for what conditions read in it.</param>
    public InspectorPanel(EditorTheme theme, MissionWorkspace workspace, IEditorHost host)
        : base(theme)
    {
        _workspace = workspace;
        _host = host;
        _grid = new PropertyGrid(theme);
        AddChild(_grid);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        _grid.Measure(context, available);
        return available;
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect) => _grid.Arrange(contentRect);

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        // No task selected but a graph node is: its details, as a node editor's Details panel shows them.
        if (_workspace.Selection.Primary is null && _workspace.Details is { } details)
        {
            if (details.Key != _shownDetails)
            {
                _shown = null;
                _shownDetails = details.Key;
                _grid.Clear();
                _grid.RevertAll = null;
                details.Build(_grid);
            }

            return;
        }

        if (_shownDetails is not null)
        {
            _shownDetails = null;
            _shownRevision = -1;
            _shown = null;
            Rebuild();
        }

        MissionTask? primary = _workspace.Selection.Primary;
        int revision = ((primary?.Parameters.Count ?? -1) * 4096) + _workspace.Selection.Count;
        if (!ReferenceEquals(primary, _shown) || revision != _shownRevision)
        {
            _shown = primary;
            _shownRevision = revision;
            Rebuild();
        }
    }

    // The parameter conditional tasks hold their expression in.
    private const string ConditionParameter = "Condition";

    // The type of the task an AnimTask drives, from its TargetID (or "Target ID"), or empty when it names none.
    private static string TargetType(MissionDocument document, MissionTask task)
    {
        MissionParameter? target = task.FindParameter("TargetID") ?? task.FindParameter("Target ID");
        return target is { Arguments: [{ } id] } && ScriptValues.TryGetInteger(id, out int targetId)
            && document.EnumerateTasks().FirstOrDefault(t => t.Id == targetId) is { } found
            ? found.TypeName
            : string.Empty;
    }

    // Selects the task scripts call by a name (CutScene_1200: its type and id).
    private void SelectByScriptName(string name)
    {
        int split = name.LastIndexOf('_');
        if (_workspace.Document is not { } document || split <= 0
            || !int.TryParse(name.AsSpan(split + 1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int id))
        {
            return;
        }

        string type = name[..split];
        if (document.EnumerateTasks().FirstOrDefault(t => t.Id == id && t.TypeName == type) is { } task)
        {
            _workspace.Selection.Set([task]);
        }
    }

    private static double[]? Numbers(MissionParameter parameter) =>
        parameter.TryGetNumbers(out double[] values) ? values : null;

    private void Rebuild()
    {
        _grid.Clear();
        _grid.RevertAll = null;
        if (_shown is not { } task || _workspace.Document is not { } document)
        {
            _grid.EmptyText = "Nothing selected";
            return;
        }

        // Several objects of one type are edited together; of mixed types, only the primary one is.
        IReadOnlyList<MissionTask> selected = [.. _workspace.Selection.Items];
        if (selected.Count > 1)
        {
            if (selected.Any(other => other.TypeName != task.TypeName) || task.UnnamedArguments is not null)
            {
                _grid.EmptyText = $"{selected.Count} objects of different types selected";
                return;
            }

            _grid.Section($"{selected.Count} × {task.TypeName}");
            foreach (MissionParameter parameter in task.Parameters.Where(static p => p.Declaration.Type != ScriptParamType.ObjectPos))
            {
                AddParameter(document, selected, parameter);
            }

            return;
        }

        _grid.Section(task.TypeName);
        _grid.Text("Name", () => task.Label, label => _workspace.Execute(new SetIdentityCommand(document, task, label, task.Id)));
        NumberField id = _grid.Number("Id", () => task.Id, (value, final) =>
        {
            if (final && (int)value != task.Id)
            {
                _workspace.Execute(new SetIdentityCommand(document, task, task.Label, (int)value));
            }
        }, "The task's slot in the 4096-entry id table; -1 for none. Scripts find tasks by it.");
        id.Decimals = 0;
        id.Minimum = -1;
        id.Maximum = TaskTree.IdCapacity - 1;

        if (task.UnnamedArguments is { Count: > 0 } unnamed)
        {
            _grid.Section("Values");
            _grid.Text("Arguments", () => string.Join(", ", unnamed.Select(QscPrinter.Print)), null,
                "This task's type is not declared by the mission script, so its values are kept exactly as written.");
            return;
        }

        if (task.Parameters.Count > 0)
        {
            _grid.Section("Parameters");
        }

        foreach (MissionParameter parameter in task.Parameters)
        {
            AddParameter(document, [task], parameter);
        }

        _grid.RevertAll = () => Revert(document, task, [.. _workspace.Baseline.ModifiedParameters(task)]);
    }

    // Puts parameters back to their saved values, as one undo step.
    private void Revert(MissionDocument document, MissionTask task, IReadOnlyList<string> parameters)
    {
        List<IEditorCommand> commands = [];
        foreach (string name in parameters)
        {
            if (_workspace.Baseline.Original(task, name) is { } original)
            {
                commands.Add(new SetParameterCommand(document, task, name, [.. original]));
            }
        }

        if (commands.Count > 0)
        {
            _workspace.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand($"Revert {TaskPresentation.Name(task)}", commands));
            _workspace.History.BreakMerge();
        }
    }

    // One parameter's row, editing it on every task given (the first supplies the value shown), as one step.
    private void AddParameter(MissionDocument document, IReadOnlyList<MissionTask> tasks, MissionParameter parameter)
    {
        AddParameterRow(document, tasks, parameter);
        string revertName = parameter.Name;
        if (tasks.Count > 1)
        {
            _grid.Mixed(() => tasks.Select(t => t.FindParameter(revertName) is { } p ? string.Join(",", p.Arguments.Select(QscPrinter.Print)) : string.Empty).Distinct().Count() > 1);
        }

        _grid.Revertible(
            () => tasks.Any(t => _workspace.Baseline.IsModified(t, revertName)),
            () =>
            {
                foreach (MissionTask each in tasks)
                {
                    Revert(document, each, [revertName]);
                }
            });
    }

    private void Execute(IReadOnlyList<MissionTask> tasks, Func<MissionTask, IEditorCommand> make) =>
        _workspace.Execute(tasks.Count == 1 ? make(tasks[0]) : new CompositeCommand(make(tasks[0]).Name, [.. tasks.Select(make)]));

    private void AddParameterRow(MissionDocument document, IReadOnlyList<MissionTask> tasks, MissionParameter parameter)
    {
        string name = parameter.Name;
        string tooltip = $"{parameter.Declaration.TypeName} parameter '{name}'";
        ScriptParamType? type = parameter.Declaration.Type;
        ScriptParamType? baseType = type is { } t ? ScriptParamTypeInfo.GetBaseType(t) : null;

        void SetNumbers(double[] values, bool final)
        {
            Execute(tasks, each => SetParameterCommand.Numbers(document, each, name, values));
            if (final)
            {
                _workspace.History.BreakMerge();
            }
        }

        bool numeric = Numbers(parameter) is not null;
        switch (baseType)
        {
            case ScriptParamType.Real64x3 when numeric && type == ScriptParamType.ObjectPos:
            {
                // Positions are shown in metres: level data's 1/4096 m units mean nothing to a reader.
                VectorField field = _grid.Vector(
                    name,
                    () => [.. (Numbers(parameter) ?? [0, 0, 0]).Select(WorldUnits.ToMetres)],
                    (metres, final) => SetNumbers([.. metres.Select(WorldUnits.FromMetres)], final),
                    tooltip: tooltip + " (metres)");
                foreach (NumberField axis in field.Fields)
                {
                    axis.Step = 0.01;
                    axis.Decimals = 3;
                }

                break;
            }

            case ScriptParamType.Real32x9 when numeric:
            {
                // Level data stores radians about X, Y and Z; degrees are what a reader thinks in.
                VectorField field = _grid.Vector(
                    name,
                    () => [.. (Numbers(parameter) ?? [0, 0, 0]).Select(static r => r * (180.0 / Math.PI))],
                    (degrees, final) => SetNumbers([.. degrees.Select(static d => d * (Math.PI / 180.0))], final),
                    tooltip: tooltip + " (degrees about X, Y and Z)");
                foreach (NumberField axis in field.Fields)
                {
                    axis.Step = 0.5;
                    axis.Decimals = 2;
                }

                break;
            }

            case ScriptParamType.Real32x3 or ScriptParamType.Real64x3 or ScriptParamType.Quat32 when numeric:
            {
                VectorField field = _grid.Vector(
                    name,
                    () => Numbers(parameter) ?? [0, 0, 0],
                    SetNumbers,
                    labels: type == ScriptParamType.Rgb ? ColorLabels : null,
                    tooltip: tooltip);
                foreach (NumberField axis in field.Fields)
                {
                    axis.Step = type == ScriptParamType.Rgb ? 0.005 : 0.01;
                }

                break;
            }

            case ScriptParamType.Real32 or ScriptParamType.Real64 when numeric:
                _grid.Number(name, () => (Numbers(parameter) ?? [0])[0], (value, final) => SetNumbers([value], final), tooltip);
                break;

            case ScriptParamType.Int32 or ScriptParamType.Int16 when numeric:
                _grid.Number(name, () => (Numbers(parameter) ?? [0])[0], (value, final) => SetNumbers([value], final), tooltip).Decimals = 0;
                break;

            case ScriptParamType.Bool8 when ScriptValues.TryGetBool(parameter.Arguments[0], out _):
                _grid.Bool(
                    name,
                    () => ScriptValues.TryGetBool(parameter.Arguments[0], out bool value) && value,
                    value => Execute(tasks, each => SetParameterCommand.Numbers(document, each, name, [value ? 1 : 0])),
                    tooltip);
                break;

            case { } when tasks.Count == 1 && name == ConditionParameter && parameter.Arguments is [QscStringLiteral]:
            {
                // A condition is edited as what it says (tests of tasks' fields), not as script text in a box.
                ConditionEditor condition = new(
                    Theme,
                    _host,
                    () => parameter.Arguments[0] is QscStringLiteral text ? text.Value : string.Empty,
                    text => Execute(tasks, each => new SetParameterCommand(document, each, name, [new QscStringLiteral(text)])),
                    SelectByScriptName,
                    () => _shownRevision = int.MinValue);
                _grid.Add(name, condition, condition.Refresh, tooltip + ": what must hold for it to run", condition.Lines);
                break;
            }

            case { } when tasks.Count == 1 && parameter.Arguments is [QscStringLiteral { Value: { } script }] && ScriptField.HoldsScript(script):
            {
                // Script to run (Run at start, Run at stop) is edited as code, highlighted and checked, not in a one-line box.
                ScriptField field = new(
                    Theme,
                    () => parameter.Arguments[0] is QscStringLiteral text ? text.Value : string.Empty,
                    text => Execute(tasks, each => new SetParameterCommand(document, each, name, [new QscStringLiteral(text)])));
                _grid.Add(name, field, field.Refresh, tooltip + ": script run by the task", field.Lines);
                break;
            }

            case { } when type is { } comboType && ScriptParamTypeInfo.IsText(comboType) && parameter.Arguments is [QscStringLiteral]
                && ValuesOf(tasks[0].TypeName, name) is { } values:
                // A name of something the game has (a model, a sound, a weapon, a text key): its own offered in a dropdown,
                // anything else still taken as typed.
                _grid.Combo(
                    name,
                    () => parameter.Arguments[0] is QscStringLiteral text ? text.Value : string.Empty,
                    text => Execute(tasks, each => new SetParameterCommand(document, each, name, [new QscStringLiteral(text)])),
                    () => _workspace.Values.For(values.Kind, values.Prefix),
                    tooltip);
                break;

            case { } when type is { } textType && ScriptParamTypeInfo.IsText(textType) && parameter.Arguments is [QscStringLiteral]:
                _grid.Text(
                    name,
                    () => parameter.Arguments[0] is QscStringLiteral text ? text.Value : string.Empty,
                    text => Execute(tasks, each => new SetParameterCommand(document, each, name, [new QscStringLiteral(text)])),
                    tooltip);
                break;

            case ScriptParamType.Graph when numeric && parameter.Arguments.Count == 3:
                // Retail's parser (Graph_ParseParam 0x4FAE50) reads only the second value, the node slots the graph starts
                // with; the first and third are read and dropped, so they are kept as they are and not shown.
                _grid.Number(
                    "Node slots",
                    () => (Numbers(parameter) ?? [0, 0, 0])[1],
                    (value, final) => SetNumbers([(Numbers(parameter) ?? [0, 0, 0])[0], Math.Max(1, Math.Round(value)), (Numbers(parameter) ?? [0, 0, 0])[2]], final),
                    tooltip + ": the nodes the graph has room for before it reads graphs/graph<id>.dat, which can grow it").Decimals = 0;
                break;

            case ScriptParamType.AnimData when tasks.Count == 1 && AnimDataEditor.Takes(parameter.Arguments):
            {
                // A programme of channel writes and waits, edited as keys on a timeline.
                MissionTask task = tasks[0];
                AnimDataEditor animation = new(
                    Theme,
                    () => parameter.Arguments,
                    arguments => Execute(tasks, each => new SetParameterCommand(document, each, name, arguments)),
                    () => TargetType(document, task),
                    () => _shownRevision = int.MinValue);
                _grid.Add("Keys", animation, animation.Refresh, tooltip + ": values set on the target's channels over time", animation.Lines);
                break;
            }

            default:
                // Anything written as an expression rather than a constant: shown as written.
                _grid.Text(name, () => string.Join(", ", parameter.Arguments.Select(QscPrinter.Print)), null, tooltip);
                break;
        }
    }

    // What a task's name-like parameter names, by how the levels declare it: a model (Model, Destroyed model, Camera Model),
    // a sound (Sound, SoundDef), a weapon or ammo (a gun's or pickup's ID), an AI type, an area's criteria, a status
    // message's or objective's text key, a sprite or texture. Null for a parameter that names nothing in particular.
    private static (ValueKind Kind, string? Prefix)? ValuesOf(string taskType, string parameter) => parameter switch
    {
        _ when parameter.Contains("Model", StringComparison.OrdinalIgnoreCase) => (ValueKind.Model, null),
        _ when parameter.Contains("Sound", StringComparison.OrdinalIgnoreCase) => (ValueKind.Sound, null),
        "AI Type" => (ValueKind.Constant, "AITYPE_"),
        "Weapon ID" => (ValueKind.Constant, "WEAPON_ID_"),
        "ID" when taskType == "AmmoPickup" => (ValueKind.Constant, "AMMO_ID_"),
        "ID" when taskType.StartsWith("Gun", StringComparison.Ordinal) => (ValueKind.Constant, "WEAPON_ID_"),
        "Criteria" => (ValueKind.Constant, "CRITERIA_"),
        "Text" when taskType == "StatusMessage" => (ValueKind.TextKey, null),
        _ when parameter.EndsWith("Text Resource", StringComparison.Ordinal) => (ValueKind.TextKey, null),
        "Sprite" or "Texture File Name" => (ValueKind.Texture, null),
        _ => null,
    };
}
