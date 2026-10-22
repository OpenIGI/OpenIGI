namespace OpenIGI.Modding;

/// <summary>Makes an integer constant of a mod's class one the game's scripts can read by name.</summary>
/// <param name="name">The name the scripts read it by.</param>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class ScriptConstantAttribute(string name) : Attribute
{
    /// <summary>Gets the name the scripts read it by.</summary>
    public string Name { get; } = name;
}
