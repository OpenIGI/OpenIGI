using System.Runtime.CompilerServices;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>
/// Where a co-op look's body is found when the level being played does not ship it: the pack that does, opened once it is first
/// asked for, as the multiplayer game has every one of its bodies at hand by opening its own pack.
/// </summary>
/// <remarks>
/// A campaign level packs its own cast only -- Jones (the common pack) and the location's people. The multiplayer game's bodies,
/// each with its three faces (<c>NetManager_atGoodModel</c>/<c>atBadModel</c>), are all in <c>missions/multiplayer/common</c>; the
/// campaign's Anya and Priboi are in the second and third locations' packs. A pack is opened only for a look a player wears that
/// the level lacks, so a game nobody dresses up opens nothing more than the original does. The world and its drawing each ask,
/// and are given the same pack.
/// </remarks>
public static class Igi2LookPacks
{
    /// <summary>The multiplayer game's pack, which holds every body its menu offers.</summary>
    public static readonly PackReference Multiplayer = new("LOCAL:missions/multiplayer/common/", "multiplayer");

    // The campaign's people the multiplayer pack lacks, by the start of their model's name, and the location pack that ships each.
    private static readonly (string Prefix, PackReference Pack)[] Campaign =
    [
        ("anya", new PackReference("LOCAL:missions/location3/common/", "location3")),
        ("priboi", new PackReference("LOCAL:missions/location2/common/", "location2")),
    ];

    // Each resource registry's packs opened so far, by name (null for one that would not open), with each one's models by name.
    private static readonly ConditionalWeakTable<ResourceRegistry, Dictionary<string, Opened?>> OpenedBy = new();

    /// <summary>Gets the pack that ships a look's model, for a level that does not.</summary>
    /// <param name="model">The model's name.</param>
    /// <returns>The pack.</returns>
    public static PackReference SourceOf(string model)
    {
        ArgumentNullException.ThrowIfNull(model);
        foreach ((string prefix, PackReference pack) in Campaign)
        {
            if (model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return pack;
            }
        }

        return Multiplayer;
    }

    /// <summary>Opens the pack that ships a look's model, once for each registry, or gives the one already open.</summary>
    /// <param name="model">The model's name.</param>
    /// <param name="resources">Where the pack is read from, and its archives registered.</param>
    /// <param name="logger">Where a pack that would not open is reported.</param>
    /// <returns>The pack, or <see langword="null"/> when this install lacks it or it holds no such model.</returns>
    public static LoadedPack? PackOf(string model, ResourceRegistry resources, IOpenIgiLogger? logger = null) =>
        IsLook(model) && Open(model, resources, logger) is { } opened && opened.Models(model) is not null ? opened.Pack : null;

    /// <summary>Finds a look's model in the pack that ships it.</summary>
    /// <param name="model">The model's name.</param>
    /// <param name="resources">Where the pack is read from.</param>
    /// <param name="logger">Where a pack that would not open is reported.</param>
    /// <returns>The model, or <see langword="null"/>.</returns>
    public static MefModel? ModelOf(string model, ResourceRegistry resources, IOpenIgiLogger? logger = null) =>
        IsLook(model) ? Open(model, resources, logger)?.Models(model) : null;

    /// <summary>
    /// Gets a value indicating whether a model is one of a co-op look's bodies, at any of its levels of detail: only those are
    /// looked for beyond the level, so a part the level lacks for any other reason opens nothing.
    /// </summary>
    /// <param name="model">The model's name, such as <c>igiagent2_1</c> or its lower detail <c>igiagent2_3</c>.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsLook(string model)
    {
        ArgumentNullException.ThrowIfNull(model);
        foreach (PlayerSkin look in PlayerSkins.Igi2.All)
        {
            // A body's levels of detail share its name to the last underscore and number on after it: igiagent2_1, igiagent2_2,
            // ... -- not a clip that happens to start with it, as anya_at_keyboard does.
            int stem = look.Model.LastIndexOf('_');
            if (stem > 0 && model.Length > stem + 1
                && string.Compare(model, 0, look.Model, 0, stem + 1, StringComparison.OrdinalIgnoreCase) == 0
                && model.AsSpan(stem + 1).IndexOfAnyExceptInRange('0', '9') < 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets a level's models with every look's beside them: the level's first, then the pack that ships a look it lacks.</summary>
    /// <param name="level">The level's models by name.</param>
    /// <param name="resources">Where a borrowed pack is read from.</param>
    /// <param name="logger">Where a pack that would not open is reported.</param>
    /// <returns>The models by name.</returns>
    public static Func<string, MefModel?> WithLooks(Func<string, MefModel?> level, ResourceRegistry resources, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(resources);
        return name => level(name) ?? ModelOf(name, resources, logger);
    }

    private static Opened? Open(string model, ResourceRegistry resources, IOpenIgiLogger? logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(model);
        ArgumentNullException.ThrowIfNull(resources);
        PackReference source = SourceOf(model);
        Dictionary<string, Opened?> opened = OpenedBy.GetValue(resources, static _ => new(StringComparer.OrdinalIgnoreCase));
        lock (opened)
        {
            if (opened.TryGetValue(source.Name, out Opened? pack))
            {
                return pack;
            }

            // A browser reads a location's pack only once it is wanted: until it is in place there is nothing to open yet,
            // and nothing is remembered, so the look is had once it arrives. Opened before then, it failed for good and
            // every look fell back to the mission's Jones.
            if (!resources.FileSystem.IsFolderInPlace(FolderOf(source)))
            {
                return null;
            }

            pack = TryOpen(source, resources, logger ?? NullLogger.Instance);
            opened[source.Name] = pack;
            return pack;
        }
    }

    // A pack's folder as the installation names it: "LOCAL:missions/multiplayer/common/" is missions/multiplayer/common.
    private static string FolderOf(PackReference source)
    {
        string directory = source.Directory;
        int device = directory.IndexOf(':', StringComparison.Ordinal);
        return directory[(device + 1)..].TrimEnd('/');
    }

    private static Opened? TryOpen(PackReference source, ResourceRegistry resources, IOpenIgiLogger logger)
    {
        try
        {
            LoadedPack pack = LoadedPack.Open(source.Directory, source.Name, resources, logger, AssetFormatProfile.Igi2);
            logger.Information("igi2.looks", $"Opened '{source.Name}' for the co-op looks the level does not ship.");
            return new Opened(pack, Igi2DrawableScene.Models(Igi2ModelTable.Create([pack.Pack]), [pack]));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or PackFormatException or EngineFatalException)
        {
            logger.Warning("igi2.looks", $"The co-op looks' pack '{source.Name}' did not open: {ex.Message}");
            return null;
        }
    }

    // A pack opened for the looks, and its models by name.
    private sealed record Opened(LoadedPack Pack, Func<string, MefModel?> Models);
}
