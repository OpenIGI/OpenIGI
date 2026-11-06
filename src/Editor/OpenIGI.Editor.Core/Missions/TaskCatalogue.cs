using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>Which way a pin faces.</summary>
public enum PinDirection
{
    /// <summary>Into the node: a parameter the task reads.</summary>
    Input,

    /// <summary>Out of the node: a field the task exports.</summary>
    Output,
}

/// <summary>One pin of a task node.</summary>
/// <param name="Name">The parameter's or field's name, as level data spells it.</param>
/// <param name="Type">What it carries.</param>
/// <param name="Direction">Which way it faces.</param>
public sealed record TaskPin(string Name, ScriptType Type, PinDirection Direction)
{
    /// <summary>Gets the declared parameter type, for an input; <see langword="null"/> for an output.</summary>
    public ScriptParamType? ParamType { get; init; }

    /// <summary>Gets what a <c>VarString</c> input holds, or <see langword="null"/> for any other pin.</summary>
    public TextRole? Role { get; init; }

    /// <summary>Gets a value indicating whether the pin takes links, rather than only a value typed in the node.</summary>
    /// <remarks>
    /// A condition, an action, a number expression and a reference are wired; a position, a model name or a colour is a
    /// value the node edits in place.
    /// </remarks>
    public bool IsLinkable { get; init; }
}

/// <summary>What a node for one task type looks like: its inputs from the type's declaration, its outputs from its exported fields.</summary>
/// <param name="TypeName">The task type's name.</param>
/// <param name="Inputs">Its parameters, in declaration order.</param>
/// <param name="Outputs">Its exported fields, in binding order.</param>
public sealed record TaskNodeDefinition(string TypeName, IReadOnlyList<TaskPin> Inputs, IReadOnlyList<TaskPin> Outputs)
{
    /// <summary>Gets a value indicating whether the type holds child tasks: drawn as a frame rather than a node.</summary>
    public bool IsContainer { get; init; }

    /// <summary>Gets a value indicating whether the type takes part in mission logic: it reads a condition or exports a field.</summary>
    /// <remarks>The mission graph shows these by default; other tasks (props, lights) appear only when something links to them.</remarks>
    public bool IsLogic => Outputs.Count > 0 || Inputs.Any(static p => p.IsLinkable);
}

/// <summary>Every task type a mission can use, as graph nodes: built from the level's declarations and the title's vocabulary.</summary>
public sealed class TaskCatalogue
{
    /// <summary>The type every level writes undeclared: a plain container of child tasks.</summary>
    public const string ContainerType = "Container";

    /// <summary>The type whose children run only while its condition holds.</summary>
    public const string ConditionalContainerType = "ConditionalContainer";

    private readonly Dictionary<string, TaskNodeDefinition> _definitions = new(StringComparer.Ordinal);

    /// <summary>Gets every definition, by type name.</summary>
    public IReadOnlyDictionary<string, TaskNodeDefinition> Definitions => _definitions;

    /// <summary>Builds the catalogue for a set of declarations.</summary>
    /// <param name="declarations">The task types' declarations, such as a mission's <see cref="MissionDocument.Declarations"/>.</param>
    /// <param name="vocabulary">The title's vocabulary, for each type's exported fields.</param>
    /// <returns>The catalogue, which always holds <see cref="ContainerType"/>.</returns>
    public static TaskCatalogue Build(IEnumerable<TaskDeclaration> declarations, ScriptVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(vocabulary);
        TaskCatalogue catalogue = new();
        catalogue._definitions[ContainerType] = new TaskNodeDefinition(ContainerType, [], []) { IsContainer = true };
        foreach (TaskDeclaration declaration in declarations)
        {
            catalogue._definitions[declaration.TypeName] = Define(declaration, vocabulary);
        }

        return catalogue;
    }

    /// <summary>Looks a type up.</summary>
    /// <param name="typeName">The task type's name.</param>
    /// <param name="definition">Receives its definition.</param>
    /// <returns><see langword="true"/> when the catalogue has it.</returns>
    public bool TryGet(string typeName, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TaskNodeDefinition? definition) =>
        _definitions.TryGetValue(typeName, out definition);

    /// <summary>The pin a declared parameter becomes.</summary>
    /// <param name="taskType">The task type.</param>
    /// <param name="parameter">The parameter's declaration.</param>
    /// <returns>The input pin.</returns>
    public static TaskPin InputOf(string taskType, TaskParameterDeclaration parameter)
    {
        ArgumentNullException.ThrowIfNull(taskType);
        ArgumentNullException.ThrowIfNull(parameter);
        if (parameter.Type is not { } declared)
        {
            return new TaskPin(parameter.Name, ScriptType.Any, PinDirection.Input);
        }

        ScriptParamType baseType = ScriptParamTypeInfo.GetBaseType(declared);
        // Only a parameter declared VarString is an expression; a DropDownCombo shares its base type but holds a picked name.
        if (declared == ScriptParamType.VarString)
        {
            TextRole role = TaskPinRoles.RoleOf(taskType, parameter.Name);
            ScriptType type = role switch
            {
                TextRole.Condition => ScriptType.Bool,
                TextRole.Action => ScriptType.Exec,
                TextRole.Number => ScriptType.Real,
                _ => ScriptType.Text,
            };
            return new TaskPin(parameter.Name, type, PinDirection.Input)
            {
                ParamType = declared,
                Role = role,
                IsLinkable = TaskPinRoles.IsExpression(role),
            };
        }

        if (TaskPinHints.TryGetReference(taskType, parameter.Name, out ScriptType reference))
        {
            return new TaskPin(parameter.Name, reference, PinDirection.Input) { ParamType = declared, IsLinkable = true };
        }

        return new TaskPin(parameter.Name, ValueTypeOf(baseType), PinDirection.Input) { ParamType = declared };
    }

    // A parameter that is edited in place: its value's type.
    private static ScriptType ValueTypeOf(ScriptParamType baseType) => baseType switch
    {
        ScriptParamType.Bool8 => ScriptType.Bool,
        ScriptParamType.Int32 or ScriptParamType.Int16 or ScriptParamType.Bitfield => ScriptType.Int,
        ScriptParamType.Real32 or ScriptParamType.Real64 or ScriptParamType.Angle or ScriptParamType.Degrees => ScriptType.Real,
        ScriptParamType.ObjectPos or ScriptParamType.Real32x3 or ScriptParamType.Real64x3 => ScriptType.Vector3,
        ScriptParamType.Real32x9 => ScriptType.Orientation,
        ScriptParamType.String16 or ScriptParamType.String32 or ScriptParamType.String256 => ScriptType.Text,
        ScriptParamType.LevelExpression => ScriptType.Bool,
        _ => ScriptType.Any,
    };

    private static TaskNodeDefinition Define(TaskDeclaration declaration, ScriptVocabulary vocabulary)
    {
        List<TaskPin> inputs = [.. declaration.Parameters.Select(p => InputOf(declaration.TypeName, p))];
        List<TaskPin> outputs = vocabulary.TaskFields.TryGetValue(declaration.TypeName, out IReadOnlyList<NativeParameter>? fields)
            ? [.. fields.Select(static f => new TaskPin(f.Name, f.Type, PinDirection.Output) { IsLinkable = true })]
            : [];
        return new TaskNodeDefinition(declaration.TypeName, inputs, outputs)
        {
            IsContainer = string.Equals(declaration.TypeName, ConditionalContainerType, StringComparison.Ordinal),
        };
    }
}
