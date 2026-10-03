using OpenIGI.Core.Content;

namespace OpenIGI.Launcher.Services;

/// <summary>One entry of a game's content load order, as the settings keep it.</summary>
/// <param name="Id">The pack id, or <see cref="ContentLayerSpec.LocalId"/> for the game folder's own overrides.</param>
/// <param name="Enabled">Whether the game is started with it.</param>
internal sealed record ContentOrderEntry(string Id, bool Enabled);

/// <summary>
/// The rules for a game's content load order: what is in it, where a new pack goes, and how the old
/// per-pack on/off settings become one.
/// </summary>
/// <remarks>
/// Highest priority first. The game folder's own overrides are an entry like any pack, at the top unless
/// the player moves them; a newly installed pack goes directly below them, so a developer's loose files keep
/// winning over whatever they install.
/// </remarks>
internal static class ContentLoadOrder
{
    /// <summary>Brings a game's order in line with what is installed for it.</summary>
    /// <param name="order">The order, changed in place.</param>
    /// <param name="installed">The ids of the packs installed for the game, in the order to add new ones.</param>
    /// <param name="legacy">The old per-pack on/off settings, read for packs the order has not met yet.</param>
    /// <param name="prune">Whether to drop entries for packs that are no longer installed.</param>
    /// <returns><see langword="true"/> when the order changed.</returns>
    /// <remarks>
    /// Pruning is the caller's call because an unreachable library (an unplugged drive) looks exactly like a
    /// library with nothing in it, and pruning then would throw away the player's order.
    /// </remarks>
    public static bool Reconcile(
        List<ContentOrderEntry> order,
        IReadOnlyList<string> installed,
        IReadOnlyDictionary<string, bool>? legacy,
        bool prune)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(installed);

        bool changed = false;
        if (!order.Exists(static entry => entry.Id == ContentLayerSpec.LocalId))
        {
            order.Insert(0, new ContentOrderEntry(ContentLayerSpec.LocalId, true));
            changed = true;
        }

        if (prune)
        {
            changed |= order.RemoveAll(entry => entry.Id != ContentLayerSpec.LocalId && !installed.Contains(entry.Id, StringComparer.Ordinal)) > 0;
        }

        // New packs go in as a block under the local folder, in the order given: the migration from the old
        // settings keeps the alphabetical order whose first pack used to be the one the game was given.
        int at = order.FindIndex(static entry => entry.Id == ContentLayerSpec.LocalId) + 1;
        foreach (string id in installed)
        {
            if (!order.Exists(entry => entry.Id == id))
            {
                order.Insert(at++, new ContentOrderEntry(id, legacy?.GetValueOrDefault(id, true) ?? true));
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Puts a newly installed pack directly below the local folder, or leaves it where it is.</summary>
    /// <param name="order">The order, changed in place.</param>
    /// <param name="id">The pack.</param>
    /// <returns><see langword="true"/> when it was added.</returns>
    public static bool AddInstalled(List<ContentOrderEntry> order, string id)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.Exists(entry => entry.Id == id))
        {
            return false;
        }

        Reconcile(order, [], null, prune: false);
        int local = order.FindIndex(static entry => entry.Id == ContentLayerSpec.LocalId);
        order.Insert(local + 1, new ContentOrderEntry(id, true));
        return true;
    }

    /// <summary>Moves an entry to a new position.</summary>
    /// <param name="order">The order, changed in place.</param>
    /// <param name="id">The entry.</param>
    /// <param name="index">Its new index, clamped to the list.</param>
    /// <returns><see langword="true"/> when it moved.</returns>
    public static bool Move(List<ContentOrderEntry> order, string id, int index)
    {
        ArgumentNullException.ThrowIfNull(order);
        int from = order.FindIndex(entry => entry.Id == id);
        if (from < 0)
        {
            return false;
        }

        int to = Math.Clamp(index, 0, order.Count - 1);
        if (to == from)
        {
            return false;
        }

        ContentOrderEntry entry = order[from];
        order.RemoveAt(from);
        order.Insert(to, entry);
        return true;
    }

    /// <summary>Switches an entry on or off.</summary>
    /// <param name="order">The order, changed in place.</param>
    /// <param name="id">The entry.</param>
    /// <param name="enabled">Whether the game is started with it.</param>
    /// <returns><see langword="true"/> when it changed.</returns>
    public static bool SetEnabled(List<ContentOrderEntry> order, string id, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(order);
        int at = order.FindIndex(entry => entry.Id == id);
        if (at < 0 || order[at].Enabled == enabled)
        {
            return false;
        }

        order[at] = order[at] with { Enabled = enabled };
        return true;
    }

    /// <summary>Builds the content profile the game is started with from every game's order.</summary>
    /// <param name="orders">Each game's order, by game id.</param>
    /// <param name="pack">
    /// What a pack id stands for -- its name, its game and its files folder -- or <see langword="null"/> when
    /// it is not installed or not fit to load (damaged, too new for the game, being worked on).
    /// </param>
    /// <returns>The profile: every game, disabled entries included, unusable packs left out.</returns>
    public static ContentProfile ToProfile(
        IReadOnlyDictionary<string, List<ContentOrderEntry>> orders,
        Func<string, PackLayer?> pack)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(pack);

        Dictionary<string, IReadOnlyList<ContentLayerSpec>> titles = new(StringComparer.Ordinal);
        foreach ((string game, List<ContentOrderEntry> order) in orders)
        {
            List<ContentLayerSpec> layers = [];
            foreach (ContentOrderEntry entry in order)
            {
                if (entry.Id == ContentLayerSpec.LocalId)
                {
                    layers.Add(ContentLayerSpec.Local(entry.Enabled));
                }
                else if (pack(entry.Id) is { } found && found.Game == game)
                {
                    layers.Add(new ContentLayerSpec(entry.Id, found.Name, ContentLayerKind.Pack, found.Files, entry.Enabled));
                }
            }

            titles[game] = layers;
        }

        return new ContentProfile(ContentProfile.CurrentSchema, titles);
    }

    /// <summary>The one folder a build from before load orders is given: the title's top enabled pack.</summary>
    /// <param name="profile">The profile being handed over.</param>
    /// <param name="titleId">The title being started.</param>
    /// <returns>The pack's files folder, or <see langword="null"/> when no pack is enabled.</returns>
    /// <remarks>
    /// Such a build reads one folder in place of the game folder's own overrides, so the local folder cannot be
    /// stacked with it; the top pack is the closest it can come to the player's order.
    /// </remarks>
    public static string? LegacyFolder(ContentProfile profile, string titleId)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.For(titleId).FirstOrDefault(static layer => layer.Kind == ContentLayerKind.Pack && layer.Enabled)?.Path;
    }

    /// <summary>An installed pack, as the profile names it.</summary>
    /// <param name="Name">Its title.</param>
    /// <param name="Game">The game it is for.</param>
    /// <param name="Files">Its files folder.</param>
    internal sealed record PackLayer(string Name, string Game, string Files);
}
