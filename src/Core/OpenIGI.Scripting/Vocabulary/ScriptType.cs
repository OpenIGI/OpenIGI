namespace OpenIGI.Scripting.Vocabulary;

/// <summary>What a script value means to the person wiring it, beyond the VM's integer, real and string.</summary>
/// <remarks>
/// The VM only knows three representations. The editor needs more: a task id is an integer, but a pin that takes one should
/// accept a link from a task and offer a picker, not a number field. Each kind names what the native or task does with the
/// value, so a type check can say "this wants a patrol path, you gave it a distance".
/// </remarks>
public enum ScriptTypeKind
{
    /// <summary>Control flow between statements; never a value.</summary>
    Exec,

    /// <summary>A truth value: zero is false, anything else true.</summary>
    Bool,

    /// <summary>A whole number.</summary>
    Int,

    /// <summary>A real number.</summary>
    Real,

    /// <summary>Text.</summary>
    Text,

    /// <summary>A position: three reals.</summary>
    Vector3,

    /// <summary>An orientation: a 3×3 matrix of reals.</summary>
    Orientation,

    /// <summary>The id of a task in the mission.</summary>
    TaskRef,

    /// <summary>The id of a node in an AI graph.</summary>
    NodeRef,

    /// <summary>The id of a patrol path.</summary>
    PathRef,

    /// <summary>One of a family of named integer constants (<see cref="ScriptType.Family"/>).</summary>
    Enum,

    /// <summary>Any value; the callee decides how to read it.</summary>
    Any,
}

/// <summary>The type of a pin, parameter or result.</summary>
/// <param name="Kind">What the value means.</param>
/// <param name="Family">
/// For <see cref="ScriptTypeKind.Enum"/>, the constant family it is drawn from; for <see cref="ScriptTypeKind.TaskRef"/>, the
/// task type it must name (a <c>Car ID</c> wants a <c>Car</c>), or <see langword="null"/> for any task.
/// </param>
public readonly record struct ScriptType(ScriptTypeKind Kind, string? Family = null)
{
    /// <summary>Gets the exec type.</summary>
    public static ScriptType Exec => new(ScriptTypeKind.Exec);

    /// <summary>Gets the truth-value type.</summary>
    public static ScriptType Bool => new(ScriptTypeKind.Bool);

    /// <summary>Gets the integer type.</summary>
    public static ScriptType Int => new(ScriptTypeKind.Int);

    /// <summary>Gets the real type.</summary>
    public static ScriptType Real => new(ScriptTypeKind.Real);

    /// <summary>Gets the text type.</summary>
    public static ScriptType Text => new(ScriptTypeKind.Text);

    /// <summary>Gets the position type.</summary>
    public static ScriptType Vector3 => new(ScriptTypeKind.Vector3);

    /// <summary>Gets the orientation type.</summary>
    public static ScriptType Orientation => new(ScriptTypeKind.Orientation);

    /// <summary>Gets the task-id type.</summary>
    public static ScriptType TaskRef => new(ScriptTypeKind.TaskRef);

    /// <summary>Gets the graph-node-id type.</summary>
    public static ScriptType NodeRef => new(ScriptTypeKind.NodeRef);

    /// <summary>Gets the patrol-path-id type.</summary>
    public static ScriptType PathRef => new(ScriptTypeKind.PathRef);

    /// <summary>Gets the type that accepts anything.</summary>
    public static ScriptType Any => new(ScriptTypeKind.Any);

    /// <summary>Gets a value indicating whether the VM carries this type as a string.</summary>
    public bool IsText => Kind == ScriptTypeKind.Text;

    /// <summary>Gets a value indicating whether the VM carries this type as a number.</summary>
    public bool IsNumber => Kind is not (ScriptTypeKind.Exec or ScriptTypeKind.Text or ScriptTypeKind.Any);

    /// <summary>The type of a member of a constant family.</summary>
    /// <param name="family">The family's name, as <see cref="ScriptVocabulary.Families"/> keys it.</param>
    /// <returns>The type.</returns>
    public static ScriptType EnumOf(string family)
    {
        ArgumentException.ThrowIfNullOrEmpty(family);
        return new(ScriptTypeKind.Enum, family);
    }

    /// <summary>The type of a reference to a task of one type.</summary>
    /// <param name="taskType">The task type it must name.</param>
    /// <returns>The type.</returns>
    public static ScriptType TaskOf(string taskType)
    {
        ArgumentException.ThrowIfNullOrEmpty(taskType);
        return new(ScriptTypeKind.TaskRef, taskType);
    }

    /// <summary>Whether a value of this type may be wired into a pin of another.</summary>
    /// <param name="target">The pin's type.</param>
    /// <returns><see langword="true"/> when the game reads the value the way the target means it.</returns>
    /// <remarks>
    /// Numbers mix freely in the VM, so most numeric pairs connect. What is refused is what the game would misread: text into
    /// a number, a number into text, exec into a value, a vector into a scalar, and a member of one constant family into a
    /// pin that wants another.
    /// </remarks>
    public bool ConnectsTo(ScriptType target)
    {
        if (Kind == ScriptTypeKind.Exec || target.Kind == ScriptTypeKind.Exec)
        {
            return Kind == target.Kind;
        }

        if (Kind == ScriptTypeKind.Any || target.Kind == ScriptTypeKind.Any)
        {
            return true;
        }

        if (IsText || target.IsText)
        {
            return IsText && target.IsText;
        }

        if (Kind is ScriptTypeKind.Vector3 or ScriptTypeKind.Orientation
            || target.Kind is ScriptTypeKind.Vector3 or ScriptTypeKind.Orientation)
        {
            return Kind == target.Kind;
        }

        if (Kind == ScriptTypeKind.Enum && target.Kind == ScriptTypeKind.Enum)
        {
            return string.Equals(Family, target.Family, StringComparison.Ordinal);
        }

        // A reference to one task type does not fit a pin that wants another; an untyped reference fits either way.
        if (Kind == ScriptTypeKind.TaskRef && target.Kind == ScriptTypeKind.TaskRef && Family is not null && target.Family is not null)
        {
            return string.Equals(Family, target.Family, StringComparison.Ordinal);
        }

        return true;
    }

    /// <inheritdoc/>
    public override string ToString() => Family is null ? Kind.ToString() : $"{Kind}({Family})";
}
