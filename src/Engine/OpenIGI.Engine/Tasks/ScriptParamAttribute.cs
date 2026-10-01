namespace OpenIGI.Engine.Tasks;

/// <summary>
/// Marks a field or property as a task parameter the level script writes into.
/// </summary>
/// <remarks>
/// <para>
/// Models one entry of the original's property table (<c>Property_Add</c>, <c>0x4F1A80</c>), whose
/// members are a field offset, a value-type code and a flags byte whose bit 0 means "parse this from
/// script". Applying this attribute is the equivalent of setting that bit.
/// </para>
/// <para>
/// <strong>Declaration order is wire order.</strong> The <c>Task_New</c> script command parses parameters
/// positionally from one flat argument list, in property-table order, so reordering attributed members
/// changes how level data is read. The order must match the type's <c>Task_DeclareParameters</c> block in
/// retail level data.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Task_DeclareParameters("HeightMap", "Position","ObjectPos", "Level","Int32", "Size","Int32", ...)
/// public sealed partial class HeightMap : GameTask
/// {
///     [ScriptParam(ScriptParamType.ObjectPos, "Position")] public Vector3d Position;
///     [ScriptParam(ScriptParamType.Int32, "Level")] public int Level;
///     [ScriptParam(ScriptParamType.Int32, "Size")] public int Size;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ScriptParamAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="ScriptParamAttribute"/> class.</summary>
    /// <param name="type">The value type, which decides how many script arguments the parameter consumes.</param>
    /// <param name="name">
    /// The parameter's name as retail level data declares it, which is often prose with spaces
    /// ("Distance to first switch"). Defaults to the member's own name.
    /// </param>
    public ScriptParamAttribute(ScriptParamType type, string? name = null)
    {
        Type = type;
        Name = name;
    }

    /// <summary>Gets the value type.</summary>
    public ScriptParamType Type { get; }

    /// <summary>Gets the declared name, or <see langword="null"/> to use the member's name.</summary>
    public string? Name { get; }
}
