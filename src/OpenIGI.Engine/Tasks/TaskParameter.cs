namespace OpenIGI.Engine.Tasks;

/// <summary>
/// One entry of a task type's parameter list: a name, a value type, and the position it occupies.
/// </summary>
/// <param name="Name">The name level data declares, which may contain spaces.</param>
/// <param name="Type">The value type.</param>
/// <remarks>
/// The original's equivalent is a 16-byte property-table entry (<c>0x4F1A80</c>) holding a field offset,
/// a type code, a default pointer and a flags byte. Only parameters with the parse bit set reach a script,
/// and this type models exactly those.
/// </remarks>
public readonly record struct TaskParameter(string Name, ScriptParamType Type)
{
    /// <summary>Gets how many positional script arguments this parameter consumes.</summary>
    /// <exception cref="NotSupportedException">The value type's parse callback is not reverse-engineered yet.</exception>
    public int ArgumentCount => ScriptParamTypeInfo.GetArgumentCount(Type);

    /// <inheritdoc/>
    public override string ToString() => $"{Name}: {ScriptParamTypeInfo.GetName(Type)}";
}
