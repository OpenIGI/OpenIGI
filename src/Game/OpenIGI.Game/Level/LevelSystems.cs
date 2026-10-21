using System.Collections;

namespace OpenIGI.Game.Level;

/// <summary>A title's level systems in the order they run, edited by name.</summary>
/// <typeparam name="TBuild">The title's build.</typeparam>
/// <remarks>
/// A title fills the list with its own systems; a mod inserts before or after one of them, replaces one, or removes one, by
/// name, so it never depends on where in the list a system happens to sit.
/// </remarks>
public sealed class LevelSystems<TBuild> : IReadOnlyList<ILevelSystem<TBuild>>
    where TBuild : LevelBuild
{
    private readonly List<ILevelSystem<TBuild>> _systems = [];

    /// <inheritdoc/>
    public int Count => _systems.Count;

    /// <inheritdoc/>
    public ILevelSystem<TBuild> this[int index] => _systems[index];

    /// <summary>Adds a system at the end.</summary>
    /// <param name="system">The system.</param>
    /// <returns>This list.</returns>
    /// <exception cref="ArgumentException">A system of the same name is already listed.</exception>
    public LevelSystems<TBuild> Add(ILevelSystem<TBuild> system)
    {
        Check(system);
        _systems.Add(system);
        return this;
    }

    /// <summary>Inserts a system to run just before another.</summary>
    /// <param name="name">The other system's name.</param>
    /// <param name="system">The system.</param>
    /// <returns>This list.</returns>
    /// <exception cref="KeyNotFoundException">No system of that name is listed.</exception>
    public LevelSystems<TBuild> InsertBefore(string name, ILevelSystem<TBuild> system)
    {
        Check(system);
        _systems.Insert(IndexOf(name), system);
        return this;
    }

    /// <summary>Inserts a system to run just after another.</summary>
    /// <param name="name">The other system's name.</param>
    /// <param name="system">The system.</param>
    /// <returns>This list.</returns>
    /// <exception cref="KeyNotFoundException">No system of that name is listed.</exception>
    public LevelSystems<TBuild> InsertAfter(string name, ILevelSystem<TBuild> system)
    {
        Check(system);
        _systems.Insert(IndexOf(name) + 1, system);
        return this;
    }

    /// <summary>Replaces a system with another, which runs where it did.</summary>
    /// <param name="name">The name of the system replaced.</param>
    /// <param name="system">Its replacement, which may have another name.</param>
    /// <returns>This list.</returns>
    /// <exception cref="KeyNotFoundException">No system of that name is listed.</exception>
    public LevelSystems<TBuild> Replace(string name, ILevelSystem<TBuild> system)
    {
        ArgumentNullException.ThrowIfNull(system);
        int index = IndexOf(name);
        _systems.RemoveAt(index);
        Check(system);
        _systems.Insert(index, system);
        return this;
    }

    /// <summary>Removes a system.</summary>
    /// <param name="name">Its name.</param>
    /// <returns><see langword="true"/> when one was listed.</returns>
    public bool Remove(string name)
    {
        int index = _systems.FindIndex(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        _systems.RemoveAt(index);
        return true;
    }

    /// <summary>Runs every system, in order.</summary>
    /// <param name="build">The level being built.</param>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is <see langword="null"/>.</exception>
    public void BuildAll(TBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);
        foreach (ILevelSystem<TBuild> system in _systems)
        {
            system.Build(build);
        }
    }

    /// <inheritdoc/>
    public IEnumerator<ILevelSystem<TBuild>> GetEnumerator() => _systems.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private int IndexOf(string name)
    {
        int index = _systems.FindIndex(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        return index >= 0 ? index : throw new KeyNotFoundException($"No level system is named '{name}'.");
    }

    private void Check(ILevelSystem<TBuild> system)
    {
        ArgumentNullException.ThrowIfNull(system);
        if (_systems.Exists(s => string.Equals(s.Name, system.Name, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"A level system named '{system.Name}' is already listed.", nameof(system));
        }
    }
}
