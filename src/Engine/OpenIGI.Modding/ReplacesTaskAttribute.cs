namespace OpenIGI.Modding;

/// <summary>Makes the level's tasks of one of the game's types out of a task class of the mod's.</summary>
/// <param name="typeName">The game's type, as the level's data spawns it.</param>
/// <remarks>
/// The class derives from <c>GameTask</c> and has a public parameterless constructor. It reads the parameters the game's type
/// declares; when its own are different, <see cref="Adapter"/> names a class that sets them.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ReplacesTaskAttribute(string typeName) : Attribute
{
    /// <summary>Gets the game's type.</summary>
    public string TypeName { get; } = typeName;

    /// <summary>Gets or sets the id of the game this is for, or <see langword="null"/> for every game the mod applies to.</summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets an <c>ITaskParameterAdapter</c> class, with a public parameterless constructor, that sets the mod's class from
    /// the game's parameters; or <see langword="null"/> when the class reads them as they are.
    /// </summary>
    public Type? Adapter { get; set; }
}
