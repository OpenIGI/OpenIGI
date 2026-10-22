namespace OpenIGI.Modding;

/// <summary>Makes a method of a mod's class run once a session of a game has loaded its tables.</summary>
/// <remarks>The method, static or not, takes the context: <c>void Name(IModContext context)</c>.</remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class OnModLoadedAttribute : Attribute
{
    /// <summary>Gets or sets the id of the game this is for, or <see langword="null"/> for every game the mod applies to.</summary>
    public string? Title { get; set; }
}
