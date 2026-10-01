namespace OpenIGI.Rendering;

/// <summary>An asynchronous conservative visibility query owned by a graphics device.</summary>
public readonly record struct OcclusionQueryHandle(uint Id)
{
    /// <summary>Gets the absent handle.</summary>
    public static OcclusionQueryHandle None => default;

    /// <summary>Gets whether this names a device-owned query.</summary>
    public bool IsValid => Id != 0;
}
