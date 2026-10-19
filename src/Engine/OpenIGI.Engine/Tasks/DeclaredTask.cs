namespace OpenIGI.Engine.Tasks;

/// <summary>
/// A task of a type that level data declared but no class implements yet: it parses its parameters
/// correctly and keeps the values, but has no behaviour.
/// </summary>
/// <remarks>
/// <para>
/// Retail level data declares every task type it uses through <c>Task_DeclareParameters</c> before
/// spawning any, and those declarations carry each parameter's name and value type. That is enough to
/// consume exactly the right arguments, which is what keeps the surrounding tree well-formed — a
/// mis-counted parameter would swallow the nested <c>Task_New</c> calls that follow it.
/// </para>
/// <para>
/// So a whole mission's object tree loads and can be asserted on before the ~150 concrete task classes
/// exist. As each real class arrives it registers under the same name and takes over.
/// </para>
/// </remarks>
public sealed class DeclaredTask : GameTask
{
    private TaskParameterValues? _values;

    /// <inheritdoc/>
    protected override IReadOnlyList<TaskParameter> ScriptParameters => Type.Parameters;

    /// <summary>Gets the parsed value of a parameter.</summary>
    /// <param name="index">The parameter's position in <see cref="GameTask.ScriptParameters"/>.</param>
    /// <returns>The value, or <see langword="null"/> for a parameter that consumes no arguments.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a declared parameter.</exception>
    public object? GetValue(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _values?.Count ?? 0);
        return _values![index];
    }

    /// <summary>Gets the parsed value of a parameter by its declared name.</summary>
    /// <param name="name">The parameter name as level data declares it.</param>
    /// <param name="value">Receives the value.</param>
    /// <returns><see langword="true"/> when the type declares a parameter with that name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool TryGetValue(string name, out object? value) => TryGetScriptValue(name, out value);

    /// <summary>Gets the parsed value of a parameter by its declared name, when it is of the type asked for.</summary>
    /// <typeparam name="T">The type the value is wanted as.</typeparam>
    /// <param name="name">The parameter name as level data declares it.</param>
    /// <param name="value">Receives the value, or the type's default.</param>
    /// <returns><see langword="true"/> when the type declares the parameter and its value is a <typeparamref name="T"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool TryGet<T>(string name, out T value)
    {
        if (TryGetScriptValue(name, out object? raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    /// <inheritdoc/>
    public override bool TryGetScriptValue(string name, out object? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_values is null)
        {
            value = null;
            return false;
        }

        return _values.TryGetValue(name, out value);
    }

    /// <inheritdoc/>
    protected override void BindScriptParameters(ScriptArgumentReader reader) =>
        _values = TaskParameterValues.Read(reader, ScriptParameters);
}
