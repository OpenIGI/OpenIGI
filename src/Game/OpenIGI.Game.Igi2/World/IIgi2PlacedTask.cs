namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// A level task with a class of its own that the world scene still places: it answers for its parameters by name, as a
/// placeholder does, so the scene, its collision and its lightmaps read it as they read the rest.
/// </summary>
public interface IIgi2PlacedTask
{
    /// <summary>Gets a parameter's value by the name level data declares it under.</summary>
    /// <param name="name">The parameter's name.</param>
    /// <param name="value">Receives the value, typed as a placeholder holds it.</param>
    /// <returns><see langword="true"/> when the task has the parameter.</returns>
    bool TryGetValue(string name, out object? value);
}
