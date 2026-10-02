namespace OpenIGI.Launcher.Services;

/// <summary>One layer of a load order, for working out which files it wins and which it loses.</summary>
/// <param name="Id">The entry's id.</param>
/// <param name="Name">What the player calls it.</param>
/// <param name="Enabled">Whether it takes part.</param>
/// <param name="Files">The files it carries, relative to its root with forward slashes.</param>
internal sealed record ContentLayerFiles(string Id, string Name, bool Enabled, IReadOnlyCollection<string> Files);

/// <summary>What one layer's files come to against the layers around it.</summary>
/// <param name="CoveredBy">The layers above that win some of its files, and how many each, highest first.</param>
/// <param name="Covers">The layers below whose files it wins, and how many of each, highest first.</param>
internal sealed record ContentConflict(
    IReadOnlyList<(string Name, int Files)> CoveredBy,
    IReadOnlyList<(string Name, int Files)> Covers)
{
    /// <summary>Gets nothing in common with any other layer.</summary>
    public static ContentConflict None { get; } = new([], []);

    /// <summary>Gets how many of the layer's files another layer wins.</summary>
    public int CoveredFiles => CoveredBy.Sum(static entry => entry.Files);
}

/// <summary>Works out, from installed manifests and the local folder's listing, where packs overlap.</summary>
/// <remarks>
/// <para>
/// File by file, which is what a manifest records. The game resolves deeper -- every entry inside a
/// <c>.res</c> archive is a path of its own -- so two packs that ship different archives for one folder can
/// still overlap entry by entry; the game's <c>content conflicts</c> command sees those.
/// </para>
/// <para>
/// Only enabled layers count: a pack that is off covers nothing and is covered by nothing.
/// </para>
/// </remarks>
internal static class ContentConflicts
{
    /// <summary>Works out the conflicts of every layer.</summary>
    /// <param name="layers">The layers, highest priority first.</param>
    /// <returns>Each layer's conflicts, by id; layers with none map to <see cref="ContentConflict.None"/>.</returns>
    public static IReadOnlyDictionary<string, ContentConflict> Compute(IReadOnlyList<ContentLayerFiles> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);

        Dictionary<string, ContentConflict> result = new(StringComparer.Ordinal);
        Dictionary<string, int> owner = new(StringComparer.OrdinalIgnoreCase);
        var coveredBy = new Dictionary<int, int>[layers.Count];
        var covers = new Dictionary<int, int>[layers.Count];

        for (int index = 0; index < layers.Count; index++)
        {
            coveredBy[index] = [];
            covers[index] = [];
            if (!layers[index].Enabled)
            {
                continue;
            }

            foreach (string file in layers[index].Files)
            {
                string path = Normalize(file);
                if (owner.TryGetValue(path, out int winner))
                {
                    coveredBy[index][winner] = coveredBy[index].GetValueOrDefault(winner) + 1;
                    covers[winner][index] = covers[winner].GetValueOrDefault(index) + 1;
                }
                else
                {
                    owner[path] = index;
                }
            }
        }

        for (int index = 0; index < layers.Count; index++)
        {
            result[layers[index].Id] = coveredBy[index].Count == 0 && covers[index].Count == 0
                ? ContentConflict.None
                : new ContentConflict(Named(coveredBy[index], layers), Named(covers[index], layers));
        }

        return result;
    }

    private static List<(string Name, int Files)> Named(Dictionary<int, int> counts, IReadOnlyList<ContentLayerFiles> layers) =>
        [.. counts.OrderBy(static pair => pair.Key).Select(pair => (layers[pair.Key].Name, pair.Value))];

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}
