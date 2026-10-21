namespace OpenIGI.Modding.Loader;

/// <summary>What the machine a run starts on has installed, as the mods' manifests ask of it.</summary>
/// <param name="HasTitleData">Gets whether a game, by title id, is installed where this run finds it.</param>
public sealed record ModEnvironment(Func<string, bool> HasTitleData)
{
    /// <summary>Gets a machine with every game installed, for a run that asks nothing of it.</summary>
    public static ModEnvironment Any { get; } = new(static _ => true);
}
