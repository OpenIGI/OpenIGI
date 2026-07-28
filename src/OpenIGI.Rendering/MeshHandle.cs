namespace OpenIGI.Rendering;

/// <summary>An opaque reference to a mesh the graphics device holds.</summary>
/// <remarks>
/// Deliberately a value type with no backend detail: the engine keeps these in task state, and a device
/// that loses and recreates its resources must be able to keep the same handles valid.
/// </remarks>
/// <param name="Id">The device-assigned identifier; zero means "no mesh".</param>
public readonly record struct MeshHandle(uint Id)
{
    /// <summary>Gets the handle that refers to no mesh.</summary>
    public static MeshHandle None => default;

    /// <summary>Gets a value indicating whether this handle refers to a mesh.</summary>
    public bool IsValid => Id != 0;

    /// <inheritdoc/>
    public override string ToString() => IsValid ? $"mesh {Id}" : "no mesh";
}
