using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>One parameter value of a task: its declaration and the script arguments that carry it.</summary>
/// <remarks>
/// A parameter is kept as the expressions the script wrote rather than as a parsed value, so a file saved
/// without changes is the file it was. <see cref="ScriptValues"/> reads and writes the numbers.
/// </remarks>
public sealed class MissionParameter
{
    internal MissionParameter(TaskParameterDeclaration declaration, IEnumerable<QscExpression> arguments)
    {
        Declaration = declaration;
        Arguments = [.. arguments];
    }

    /// <summary>Gets the declaration.</summary>
    public TaskParameterDeclaration Declaration { get; }

    /// <summary>Gets the parameter's name.</summary>
    public string Name => Declaration.Name;

    /// <summary>Gets the script arguments, as many as the type's wire width (three for a position).</summary>
    public List<QscExpression> Arguments { get; }

    /// <summary>Reads the arguments as numbers.</summary>
    /// <param name="values">Receives one value per argument.</param>
    /// <returns><see langword="false"/> when any argument is not a numeric constant.</returns>
    public bool TryGetNumbers(out double[] values)
    {
        values = new double[Arguments.Count];
        for (int i = 0; i < values.Length; i++)
        {
            if (!ScriptValues.TryGetNumber(Arguments[i], out values[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Builds the arguments for new numbers, keeping the spelling of every argument whose value did not change.</summary>
    /// <param name="values">One value per argument.</param>
    /// <returns>The new arguments; the parameter itself is not changed.</returns>
    /// <exception cref="ArgumentException">The count does not match the parameter's width.</exception>
    public QscExpression[] WithNumbers(ReadOnlySpan<double> values)
    {
        if (values.Length != Arguments.Count)
        {
            throw new ArgumentException($"'{Name}' takes {Arguments.Count} values, not {values.Length}.", nameof(values));
        }

        // An integer parameter stays an integer and a bool8 keeps level data's TRUE and FALSE: the engine reads each
        // argument as its declared type, and a real where an integer belongs is not what the original tools wrote.
        ScriptParamType? baseType = Declaration.Type is { } type ? ScriptParamTypeInfo.GetBaseType(type) : null;
        QscExpression[] arguments = new QscExpression[values.Length];
        for (int i = 0; i < arguments.Length; i++)
        {
            arguments[i] = baseType switch
            {
                ScriptParamType.Bool8 => ScriptValues.TryGetBool(Arguments[i], out bool was) && was == (values[i] != 0.0)
                    ? Arguments[i]
                    : ScriptValues.Bool(values[i] != 0.0),
                ScriptParamType.Int32 or ScriptParamType.Int16 => ScriptValues.KeepOrInteger(Arguments[i], values[i]),
                _ => ScriptValues.KeepOrReal(Arguments[i], values[i]),
            };
        }

        return arguments;
    }

    /// <summary>Writes new numbers in place, without history: see <see cref="WithNumbers"/>.</summary>
    /// <param name="values">One value per argument.</param>
    /// <exception cref="ArgumentException">The count does not match the parameter's width.</exception>
    public void SetNumbers(ReadOnlySpan<double> values)
    {
        QscExpression[] arguments = WithNumbers(values);
        Arguments.Clear();
        Arguments.AddRange(arguments);
    }
}

/// <summary>
/// One task of a mission's object tree: a <c>Task_New</c> call, split into its identity, its declared
/// parameters and its children.
/// </summary>
/// <remarks>
/// <para>
/// The wire layout is <c>Task_New(id, type, label, values…, children…)</c>. The children are the trailing run
/// of nested <c>Task_New</c> calls. The values become named <see cref="Parameters"/> when the level script
/// declares the type and the declaration accounts for every value; otherwise (a <c>Container</c>, which retail
/// never declares, or a declaration that does not fit) they are kept verbatim in <see cref="UnnamedArguments"/>
/// and written back unchanged, while the children are still a tree.
/// </para>
/// </remarks>
public sealed class MissionTask
{
    /// <summary>Initializes a new instance of the <see cref="MissionTask"/> class.</summary>
    /// <param name="typeName">The task type.</param>
    /// <param name="label">The label, which level data uses as the object's name.</param>
    /// <param name="id">The script id, or -1 for none.</param>
    public MissionTask(string typeName, string label, int id = -1)
    {
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        IdExpression = ScriptValues.Integer(id);
    }

    /// <summary>Gets or sets the expression the id is written as.</summary>
    public QscExpression IdExpression { get; set; }

    /// <summary>Gets or sets the script id: the slot in the 4096-entry id table, or -1 for none.</summary>
    public int Id
    {
        get => ScriptValues.TryGetInteger(IdExpression, out int id) ? id : -1;
        set => IdExpression = ScriptValues.Integer(value);
    }

    /// <summary>Gets or sets the task type's name.</summary>
    public string TypeName { get; set; }

    /// <summary>Gets or sets the label.</summary>
    public string Label { get; set; }

    /// <summary>Gets or sets the type's declaration, or <see langword="null"/> when the script declares none.</summary>
    public TaskDeclaration? Declaration { get; set; }

    /// <summary>Gets the named parameters, in declaration order. Empty when <see cref="UnnamedArguments"/> is set.</summary>
    public List<MissionParameter> Parameters { get; } = [];

    /// <summary>Gets the child tasks, in creation order.</summary>
    public List<MissionTask> Children { get; } = [];

    /// <summary>Gets the parent task, or <see langword="null"/> for a top-level task.</summary>
    public MissionTask? Parent { get; internal set; }

    /// <summary>
    /// Gets the values of a task whose type is undeclared or whose declaration does not fit them, carried verbatim,
    /// or <see langword="null"/> when the values are split into <see cref="Parameters"/>.
    /// </summary>
    public List<QscExpression>? UnnamedArguments { get; internal set; }

    /// <summary>Finds a parameter by its declared name.</summary>
    /// <param name="name">The name, compared ordinally.</param>
    /// <returns>The parameter, or <see langword="null"/>.</returns>
    public MissionParameter? FindParameter(string name) =>
        Parameters.Find(p => string.Equals(p.Name, name, StringComparison.Ordinal));

    /// <summary>Finds the first parameter of a value type, such as the position of a placed object.</summary>
    /// <param name="type">The value type.</param>
    /// <returns>The parameter, or <see langword="null"/>.</returns>
    public MissionParameter? FindParameter(ScriptParamType type) => Parameters.Find(p => p.Declaration.Type == type);

    /// <summary>Adds a child at the end, or at an index.</summary>
    /// <param name="child">The child; it must not already have a parent.</param>
    /// <param name="index">Where to insert it, or -1 for the end.</param>
    /// <exception cref="InvalidOperationException">The child already has a parent.</exception>
    public void AddChild(MissionTask child, int index = -1)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Parent is not null)
        {
            throw new InvalidOperationException($"'{child.Label}' already belongs to '{child.Parent.Label}'.");
        }

        Children.Insert(index < 0 ? Children.Count : index, child);
        child.Parent = this;
    }

    /// <summary>Removes a child.</summary>
    /// <param name="child">The child.</param>
    /// <returns>The index it had, or -1 when it was not a child of this task.</returns>
    public int RemoveChild(MissionTask child)
    {
        ArgumentNullException.ThrowIfNull(child);
        int index = Children.IndexOf(child);
        if (index >= 0)
        {
            Children.RemoveAt(index);
            child.Parent = null;
        }

        return index;
    }

    /// <summary>Enumerates this task and everything under it, depth first, parents before children.</summary>
    /// <returns>The tasks.</returns>
    public IEnumerable<MissionTask> SelfAndSubtree()
    {
        Stack<MissionTask> pending = new([this]);
        while (pending.Count > 0)
        {
            MissionTask task = pending.Pop();
            yield return task;
            for (int i = task.Children.Count - 1; i >= 0; i--)
            {
                pending.Push(task.Children[i]);
            }
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"{TypeName} '{Label}'" + (Id >= 0 ? $" #{Id}" : string.Empty);
}
