namespace OpenIGI.Modding;

/// <summary>Adds a task class of the mod's as a task type of its own, which level data and scripts spawn by its name.</summary>
/// <param name="typeName">The type's name.</param>
/// <remarks>The class derives from <c>GameTask</c> and has a public parameterless constructor.</remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class AddsTaskAttribute(string typeName) : Attribute
{
    /// <summary>Gets the type's name.</summary>
    public string TypeName { get; } = typeName;

    /// <summary>Gets or sets the id of the game this is for, or <see langword="null"/> for every game the mod applies to.</summary>
    public string? Title { get; set; }
}
