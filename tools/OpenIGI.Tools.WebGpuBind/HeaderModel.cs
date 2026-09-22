namespace OpenIGI.Tools.WebGpuBind;

/// <summary>One value of a C enumeration.</summary>
/// <param name="Name">Its name with the enumeration's own prefix removed.</param>
/// <param name="Value">Its value, as the header spells it.</param>
/// <param name="Documentation">The comment above it, already flattened to plain lines.</param>
public sealed record EnumValue(string Name, string Value, IReadOnlyList<string> Documentation);

/// <summary>A C enumeration, or a set of flags built from a typedef and its constants.</summary>
/// <param name="Name">The C name, which the binding keeps.</param>
/// <param name="Values">Its values.</param>
/// <param name="IsFlags">Whether the values combine, which makes it a flags enumeration.</param>
/// <param name="UnderlyingType">The C# type it is stored as.</param>
/// <param name="Documentation">The comment above it.</param>
public sealed record EnumDeclaration(
    string Name,
    IReadOnlyList<EnumValue> Values,
    bool IsFlags,
    string UnderlyingType,
    IReadOnlyList<string> Documentation);

/// <summary>One field of a C structure.</summary>
/// <param name="Name">Its name.</param>
/// <param name="CType">Its type, as the header spells it.</param>
/// <param name="Documentation">The comment above it.</param>
public sealed record FieldDeclaration(string Name, string CType, IReadOnlyList<string> Documentation);

/// <summary>A C structure, bound as a sequential C# struct of the same layout.</summary>
/// <param name="Name">The C name, which the binding keeps.</param>
/// <param name="Fields">Its fields, in declaration order.</param>
/// <param name="Documentation">The comment above it.</param>
public sealed record StructDeclaration(
    string Name,
    IReadOnlyList<FieldDeclaration> Fields,
    IReadOnlyList<string> Documentation)
{
    /// <summary>Gets a value indicating whether every field sits at offset zero.</summary>
    /// <remarks>
    /// C has anonymous unions and C# does not, so one is lifted out into a structure of its own and laid
    /// out explicitly. There is exactly one in this API: the platform display handle a Linux surface is
    /// created from.
    /// </remarks>
    public bool IsUnion { get; init; }
}

/// <summary>An opaque object handle, bound as a one-word struct so the types stay apart.</summary>
/// <param name="Name">The C name, which the binding keeps.</param>
/// <param name="Documentation">The comment above it.</param>
public sealed record HandleDeclaration(string Name, IReadOnlyList<string> Documentation);

/// <summary>One parameter of a function or callback.</summary>
/// <param name="Name">Its name.</param>
/// <param name="CType">Its type, as the header spells it.</param>
public sealed record ParameterDeclaration(string Name, string CType);

/// <summary>A function pointer the API calls back through.</summary>
/// <param name="Name">The C name, which the binding keeps.</param>
/// <param name="ReturnCType">What it returns, as the header spells it.</param>
/// <param name="Parameters">What it takes.</param>
/// <param name="Documentation">The comment above it.</param>
public sealed record CallbackDeclaration(
    string Name,
    string ReturnCType,
    IReadOnlyList<ParameterDeclaration> Parameters,
    IReadOnlyList<string> Documentation);

/// <summary>An exported function.</summary>
/// <param name="Name">The C name, which the binding keeps.</param>
/// <param name="ReturnCType">What it returns, as the header spells it.</param>
/// <param name="Parameters">What it takes.</param>
/// <param name="Documentation">The comment above it.</param>
public sealed record FunctionDeclaration(
    string Name,
    string ReturnCType,
    IReadOnlyList<ParameterDeclaration> Parameters,
    IReadOnlyList<string> Documentation);

/// <summary>Everything one header declares.</summary>
/// <param name="Enums">Its enumerations and flag sets.</param>
/// <param name="Structs">Its structures.</param>
/// <param name="Handles">Its opaque object handles.</param>
/// <param name="Callbacks">Its callback function pointers.</param>
/// <param name="Functions">Its exported functions.</param>
public sealed record HeaderModel(
    IReadOnlyList<EnumDeclaration> Enums,
    IReadOnlyList<StructDeclaration> Structs,
    IReadOnlyList<HandleDeclaration> Handles,
    IReadOnlyList<CallbackDeclaration> Callbacks,
    IReadOnlyList<FunctionDeclaration> Functions)
{
    /// <summary>Joins two headers' declarations, the second extending the first.</summary>
    /// <param name="other">The extending header.</param>
    /// <returns>One model holding both.</returns>
    public HeaderModel Concat(HeaderModel other) => new(
        [.. Enums, .. other.Enums],
        [.. Structs, .. other.Structs],
        [.. Handles, .. other.Handles],
        [.. Callbacks, .. other.Callbacks],
        [.. Functions, .. other.Functions]);
}
