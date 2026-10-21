using OpenIGI.Core.Content;

namespace OpenIGI.Launcher.Services;

/// <summary>
/// The rules for a game's mod order: what is in it, where a new mod goes, and the profile the game is started with.
/// </summary>
/// <remarks>
/// <para>
/// In the order the mods apply: a later mod's changes are made over an earlier one's, so a mod installed now goes at the end
/// and wins over what was there. Moving and switching an entry are a content order's (<see cref="ContentLoadOrder.Move"/>,
/// <see cref="ContentLoadOrder.SetEnabled"/>); a mod order has no game folder entry.
/// </para>
/// <para>
/// A mod for more than one game is in each one's order, switched and placed in each apart: the order is the player's per
/// game, as the game loads it.
/// </para>
/// </remarks>
internal static class ModLoadOrder
{
    /// <summary>Brings a game's order in line with the mods there are for it.</summary>
    /// <param name="order">The order, changed in place.</param>
    /// <param name="mods">The ids of the mods installed for the game (and placed in its local folder), new ones in the order to add them.</param>
    /// <param name="prune">Whether to drop entries for mods that are no longer there; not while the library cannot be reached.</param>
    /// <returns><see langword="true"/> when the order changed.</returns>
    public static bool Reconcile(List<ContentOrderEntry> order, IReadOnlyList<string> mods, bool prune)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(mods);
        bool changed = prune && order.RemoveAll(entry => !mods.Contains(entry.Id, StringComparer.Ordinal)) > 0;
        foreach (string id in mods)
        {
            changed |= AddInstalled(order, id);
        }

        return changed;
    }

    /// <summary>Puts a newly installed mod at the end, switched on, or leaves it where it is.</summary>
    /// <param name="order">The order, changed in place.</param>
    /// <param name="id">The mod.</param>
    /// <returns><see langword="true"/> when it was added.</returns>
    public static bool AddInstalled(List<ContentOrderEntry> order, string id)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.Exists(entry => entry.Id == id))
        {
            return false;
        }

        order.Add(new ContentOrderEntry(id, true));
        return true;
    }

    /// <summary>Builds the mod profile the game is started with from every game's order.</summary>
    /// <param name="orders">Each game's order, by game id.</param>
    /// <param name="folder">
    /// Where a mod id's <c>mod.json</c> is for a game, or <see langword="null"/> when it is not there or not fit to play
    /// (damaged, too new for the game, being worked on).
    /// </param>
    /// <param name="off">The games the player starts without mods; their orders travel with every mod switched off.</param>
    /// <returns>The profile: every game, switched-off mods included, unusable ones left out.</returns>
    public static ModProfile ToProfile(
        IReadOnlyDictionary<string, List<ContentOrderEntry>> orders,
        Func<string, string, string?> folder,
        IReadOnlyCollection<string>? off = null)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(folder);
        Dictionary<string, IReadOnlyList<ModProfileEntry>> titles = new(StringComparer.Ordinal);
        foreach ((string game, List<ContentOrderEntry> order) in orders)
        {
            bool playsMods = off is null || !off.Contains(game);
            titles[game] = [.. order
                .Select(entry => (entry, path: folder(game, entry.Id)))
                .Where(static pair => pair.path is not null)
                .Select(pair => new ModProfileEntry(pair.entry.Id, pair.path!, pair.entry.Enabled && playsMods))];
        }

        return new ModProfile(ModProfile.CurrentSchema, titles);
    }

    /// <summary>Gets the other games whose data the mods a game is started with read.</summary>
    /// <param name="profile">The profile being handed over.</param>
    /// <param name="titleId">The game being started.</param>
    /// <param name="requires">The games a mod id reads besides its own.</param>
    /// <returns>Their ids, each once.</returns>
    public static IReadOnlyList<string> GamesRead(ModProfile profile, string titleId, Func<string, IReadOnlyList<string>> requires)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(requires);
        return [.. profile.For(titleId)
            .Where(static entry => entry.Enabled)
            .SelectMany(entry => requires(entry.Id))
            .Where(id => !string.Equals(id, titleId, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
