namespace OpenIGI.Engine.Tasks;

/// <summary>The values level data gave a task's parameters, by the names and in the order its type declares them.</summary>
/// <remarks>
/// What a <see cref="DeclaredTask"/> keeps, and what an <see cref="ITaskParameterAdapter"/> is handed: a type made by a class
/// whose own parameters are not the ones the level writes is read in the level's terms and adapted from them.
/// </remarks>
public sealed class TaskParameterValues
{
    private readonly object?[] _values;

    private TaskParameterValues(IReadOnlyList<TaskParameter> parameters, object?[] values)
    {
        Parameters = parameters;
        _values = values;
    }

    /// <summary>Gets the parameters the values were read for, in wire order.</summary>
    public IReadOnlyList<TaskParameter> Parameters { get; }

    /// <summary>Gets how many values were read.</summary>
    public int Count => _values.Length;

    /// <summary>Gets the value of a parameter by its position.</summary>
    /// <param name="index">The parameter's position in <see cref="Parameters"/>.</param>
    /// <returns>The value, or <see langword="null"/> for a parameter that consumes no arguments.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a read parameter.</exception>
    public object? this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _values.Length);
            return _values[index];
        }
    }

    /// <summary>Gets the value of a parameter by its declared name.</summary>
    /// <param name="name">The parameter name as level data declares it.</param>
    /// <param name="value">Receives the value.</param>
    /// <returns><see langword="true"/> when the type declares a parameter with that name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool TryGetValue(string name, out object? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        for (int i = 0; i < Parameters.Count && i < _values.Length; i++)
        {
            if (string.Equals(Parameters[i].Name, name, StringComparison.Ordinal))
            {
                value = _values[i];
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>Gets the value of a parameter by its declared name, when it is of the type asked for.</summary>
    /// <typeparam name="T">The type the value is wanted as.</typeparam>
    /// <param name="name">The parameter name as level data declares it.</param>
    /// <param name="value">Receives the value, or the type's default.</param>
    /// <returns><see langword="true"/> when the type declares the parameter and its value is a <typeparamref name="T"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool TryGet<T>(string name, out T value)
    {
        if (TryGetValue(name, out object? raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>Reads one value per parameter from a level script's argument list, consuming exactly what each type takes.</summary>
    /// <summary>Makes a copy whose values can be changed apart from these.</summary>
    /// <returns>The copy.</returns>
    internal TaskParameterValues Copy() => new(Parameters, (object?[])_values.Clone());

    /// <summary>Changes a value by its parameter's name.</summary>
    /// <param name="name">The parameter name as level data declares it.</param>
    /// <param name="value">The value, of the type the parameter is read as.</param>
    /// <returns><see langword="true"/> when the type declares a parameter with that name.</returns>
    internal bool TrySet(string name, object? value)
    {
        for (int i = 0; i < Parameters.Count && i < _values.Length; i++)
        {
            if (string.Equals(Parameters[i].Name, name, StringComparison.Ordinal))
            {
                _values[i] = value;
                return true;
            }
        }

        return false;
    }

    /// <summary>Values given in code, by parameter name, as a level's script would give them; a parameter not named has none.</summary>
    /// <param name="parameters">The type's parameters, in their declared order.</param>
    /// <param name="values">The values, by name.</param>
    /// <returns>The values.</returns>
    /// <exception cref="ArgumentException">A value names no parameter of the type.</exception>
    internal static TaskParameterValues From(IReadOnlyList<TaskParameter> parameters, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(values);
        object?[] read = new object?[parameters.Count];
        foreach ((string name, object? value) in values)
        {
            int index = -1;
            for (int i = 0; i < parameters.Count && index < 0; i++)
            {
                if (string.Equals(parameters[i].Name, name, StringComparison.Ordinal))
                {
                    index = i;
                }
            }

            if (index < 0)
            {
                throw new ArgumentException($"The type has no parameter '{name}'.", nameof(values));
            }

            read[index] = value;
        }

        return new TaskParameterValues(parameters, read);
    }

    internal static TaskParameterValues Read(ScriptArgumentReader reader, IReadOnlyList<TaskParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(reader);
        object?[] values = new object?[parameters.Count];
        for (int i = 0; i < parameters.Count; i++)
        {
            values[i] = ReadValue(reader, parameters[i].Type);
        }

        return new TaskParameterValues(parameters, values);
    }

    private static object? ReadValue(ScriptArgumentReader reader, ScriptParamType type) =>
        ScriptParamTypeInfo.GetBaseType(type) switch
        {
            ScriptParamType.Int32 => reader.ReadInt32(),
            ScriptParamType.Int16 => reader.ReadInt16(),
            ScriptParamType.Bool8 => reader.ReadBool8(),
            ScriptParamType.Real32 => reader.ReadReal32(),
            ScriptParamType.Real64 => reader.ReadReal64(),
            ScriptParamType.Real32x3 => reader.ReadReal32x3(),
            ScriptParamType.Real64x3 => reader.ReadReal64x3(),
            ScriptParamType.Real32x9 => reader.ReadReal32x9(),
            ScriptParamType.Quat32 => reader.ReadQuat32(),
            ScriptParamType.GraphEnvelope => reader.ReadGraphEnvelope(),
            ScriptParamType.Graph => reader.ReadGraph(),
            ScriptParamType.AnimData => reader.ReadAnimData(),
            ScriptParamType.QTaskNote => null,
            _ => reader.ReadString(),
        };
}
