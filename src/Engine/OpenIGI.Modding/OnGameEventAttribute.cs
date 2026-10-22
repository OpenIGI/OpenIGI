namespace OpenIGI.Modding;

/// <summary>Makes a method of a mod's class a listener of one kind of game event.</summary>
/// <remarks>
/// The method, static or not, takes the event: <c>void Name(in TEvent gameEvent)</c>, where <c>TEvent</c> is one of
/// <c>OpenIGI.Game.Events</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class OnGameEventAttribute : Attribute
{
    /// <summary>Gets or sets the id of the game this is for, or <see langword="null"/> for every game the mod applies to.</summary>
    public string? Title { get; set; }
}
