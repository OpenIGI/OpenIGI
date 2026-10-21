namespace OpenIGI.Game.World;

/// <summary>The authored destructible object families present in retail IGI1.</summary>
public enum DestructibleObjectKind
{
    /// <summary>A glass magic-object attachment.</summary>
    Glass,

    /// <summary>An authored ExplodeObject task or explosive magic-object attachment.</summary>
    Explosive,

    /// <summary>A <c>Car</c> task — every one exports <c>isExploded</c>, wreck model or not.</summary>
    Vehicle,
}
