namespace OpenIGI.Game.World;

/// <summary>
/// The distances each model switches its levels of detail at, both games' per-model <c>ModelLODSettings</c> (I.G.I. 2's
/// <c>LODVirModel_s.avLODDistance</c> with <c>isLODDistanceSpecified</c>): the game's own file at start-up, and a level's over it.
/// </summary>
public sealed class ModelLodTable
{
    private readonly Dictionary<string, float[]> _distances = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, float[]> _game = new Dictionary<string, float[]>();

    /// <summary>Notes the distances as they stand as the game's own, which each level starts from.</summary>
    public void KeepAsGame() => _game = Snapshot();

    /// <summary>Puts the game's own distances back, as a level starts.</summary>
    public void BeginLevel() => Reset(_game);

    /// <summary>Gets the models with distances of their own.</summary>
    public int Count => _distances.Count;

    /// <summary>Gets a model's distances, in world units: to its second to fifth levels and its cutoff.</summary>
    /// <param name="model">The model.</param>
    /// <param name="distances">Receives its distances.</param>
    /// <returns>Whether it has distances of its own.</returns>
    public bool TryGet(string model, out IReadOnlyList<float> distances)
    {
        bool found = _distances.TryGetValue(model, out float[]? stored);
        distances = stored ?? [];
        return found;
    }

    /// <summary>Gives a model its distances.</summary>
    /// <param name="model">The model.</param>
    /// <param name="distances">Its distances, in world units.</param>
    public void Set(string model, float[] distances)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(distances);
        _distances[model] = distances;
    }

    /// <summary>Forgets the distances a level gave, keeping the game's.</summary>
    /// <param name="game">The game's distances, as they stood after start-up.</param>
    public void Reset(IReadOnlyDictionary<string, float[]> game)
    {
        ArgumentNullException.ThrowIfNull(game);
        _distances.Clear();
        foreach ((string model, float[] distances) in game)
        {
            _distances[model] = distances;
        }
    }

    /// <summary>Gets the distances as they stand, live: a level's settings read later are seen here.</summary>
    public IReadOnlyDictionary<string, float[]> Distances => _distances;

    /// <summary>Takes a copy of the distances as they stand.</summary>
    /// <returns>The copy.</returns>
    public IReadOnlyDictionary<string, float[]> Snapshot() => new Dictionary<string, float[]>(_distances, StringComparer.OrdinalIgnoreCase);
}
