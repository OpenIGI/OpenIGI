namespace OpenIGI.Game.Level;

/// <summary>A level system made of a name and the work it does: the form most systems take.</summary>
/// <typeparam name="TBuild">The build it reads.</typeparam>
/// <param name="name">The system's name.</param>
/// <param name="build">What it does.</param>
public sealed class LevelSystem<TBuild>(string name, Action<TBuild> build) : ILevelSystem<TBuild>
    where TBuild : LevelBuild
{
    private readonly Action<TBuild> _build = build ?? throw new ArgumentNullException(nameof(build));

    /// <inheritdoc/>
    public string Name { get; } = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A level system needs a name.", nameof(name)) : name;

    /// <inheritdoc/>
    public void Build(TBuild build) => _build(build);

    /// <inheritdoc/>
    public override string ToString() => Name;
}
