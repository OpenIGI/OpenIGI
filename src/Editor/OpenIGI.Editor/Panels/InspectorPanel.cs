using OpenIGI.Core.Numerics;
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
    private readonly PropertyGrid _grid;
    private MissionTask? _shown;
    private int _shownRevision = -1;

    public InspectorPanel(EditorTheme theme, MissionWorkspace workspace)
        : base(theme)
    {
        _workspace = workspace;
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
        MissionTask? primary = _workspace.Selection.Primary;
        int revision = primary?.Parameters.Count ?? -1;
        if (!ReferenceEquals(primary, _shown) || revision != _shownRevision)
        {
            _shown = primary;
            _shownRevision = revision;
            Rebuild();
        }
    }

    private static double[]? Numbers(MissionParameter parameter) =>
        parameter.TryGetNumbers(out double[] values) ? values : null;

    private void Rebuild()
    {
        _grid.Clear();
        if (_shown is not { } task || _workspace.Document is not { } document)
        {
            _grid.EmptyText = _workspace.Selection.Count > 1 ? $"{_workspace.Selection.Count} objects selected" : "Nothing selected";
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
            AddParameter(document, task, parameter);
        }
    }

    private void AddParameter(MissionDocument document, MissionTask task, MissionParameter parameter)
    {
        string name = parameter.Name;
        string tooltip = $"{parameter.Declaration.TypeName} parameter '{name}'";
        ScriptParamType? type = parameter.Declaration.Type;
        ScriptParamType? baseType = type is { } t ? ScriptParamTypeInfo.GetBaseType(t) : null;

        void SetNumbers(double[] values, bool final)
        {
            _workspace.Execute(SetParameterCommand.Numbers(document, task, name, values));
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
                    value => _workspace.Execute(SetParameterCommand.Numbers(document, task, name, [value ? 1 : 0])),
                    tooltip);
                break;

            case { } when type is { } textType && ScriptParamTypeInfo.IsText(textType) && parameter.Arguments is [QscStringLiteral]:
                _grid.Text(
                    name,
                    () => parameter.Arguments[0] is QscStringLiteral text ? text.Value : string.Empty,
                    text => _workspace.Execute(new SetParameterCommand(document, task, name, [new QscStringLiteral(text)])),
                    tooltip);
                break;

            default:
                // AnimData, graphs and anything written as an expression rather than a constant: shown as written.
                _grid.Text(name, () => string.Join(", ", parameter.Arguments.Select(QscPrinter.Print)), null, tooltip);
                break;
        }
    }
}
