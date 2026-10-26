using System.Text;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Titles;
using OpenIGI.Formats;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Systems;

/// <summary>
/// Project I.G.I.'s content, read from its own install for its soldiers and player in another game's level: a session of the
/// game with its tables, the packs their bodies come from, its weapons and its sounds.
/// </summary>
/// <remarks>
/// The game opens one location, <c>location0</c>, whose common pack holds few bodies: most live in the levels' own packs. The
/// content opens the common pack and, after it, each level pack that names a body asked for and no earlier pack does, so
/// <see cref="Mission"/> resolves every body the way a mission of the game resolves its own.
/// </remarks>
public sealed class Igi1GuestContent
{
    /// <summary>The location the game's missions all play in.</summary>
    public const string Location = "location0";

    /// <summary>The directory sound banks are mounted from.</summary>
    public const string SoundDirectory = MissionLoader.CommonSoundDirectory;

    private Igi1GuestContent(GuestTitle guest, LoadedMission mission, Igi1Arsenal arsenal, SoundDefinitionRegistry sounds)
    {
        Guest = guest;
        Mission = mission;
        Arsenal = arsenal;
        SoundDefinitions = sounds;
    }

    /// <summary>Gets the game's session: its file system, resources, loop and tables.</summary>
    public GuestTitle Guest { get; }

    /// <summary>
    /// Gets the packs the bodies come from, as a mission of the game holds them: the location's common pack first, and the last
    /// level pack opened as its level pack.
    /// </summary>
    public LoadedMission Mission { get; }

    /// <summary>Gets the weapon, ammunition and material tables.</summary>
    public Igi1Arsenal Arsenal { get; }

    /// <summary>Gets the sound definitions the game's common sounds are named by.</summary>
    public SoundDefinitionRegistry SoundDefinitions { get; }

    /// <summary>Gets the directories the definitions play from.</summary>
    public IReadOnlyList<string> SoundBanks { get; } = [SoundDirectory];

    /// <summary>Reads the game's content from its install.</summary>
    /// <param name="dataRoot">The directory the game is installed in.</param>
    /// <param name="models">Every body a soldier or the player may be dressed in.</param>
    /// <param name="logger">Where problems are reported.</param>
    /// <returns>The content.</returns>
    /// <exception cref="IOException">A table or pack could not be read.</exception>
    /// <exception cref="InvalidDataException">A table or pack is malformed.</exception>
    public static Igi1GuestContent Load(string dataRoot, IEnumerable<string> models, IOpenIgiLogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataRoot);
        ArgumentNullException.ThrowIfNull(models);
        IOpenIgiLogger log = logger ?? NullLogger.Instance;
        GuestTitle guest = GuestTitle.Start(new Igi1Runtime(), dataRoot, log);
        Igi1Arsenal arsenal = Igi1Arsenal.Load(guest.Loop, guest.FileSystem, guest.Resources, log);

        string commonDirectory = $"missions/{Location}/common";
        List<LoadedPack> packs = [LoadedPack.Open($"LOCAL:{commonDirectory}/", Location, guest.Resources, log, AssetFormatProfile.Igi1)];
        HashSet<string> wanted = new(models, StringComparer.OrdinalIgnoreCase);
        wanted.RemoveWhere(model => packs[0].TryGetModel(model, out _));
        string levelDirectory = commonDirectory;
        string levelName = Location;
        foreach ((string directory, string name) in Levels(dataRoot, wanted))
        {
            packs.Add(LoadedPack.Open($"LOCAL:{directory}/", name, guest.Resources, log, AssetFormatProfile.Igi1));
            wanted.RemoveWhere(model => packs[^1].TryGetModel(model, out _));
            (levelDirectory, levelName) = (directory, name);
        }

        if (wanted.Count > 0)
        {
            log.Warning("Guest", $"No pack of Project I.G.I.'s holds {string.Join(", ", wanted.Order(StringComparer.Ordinal))}.");
        }

        MissionDescriptor descriptor = new(
            0, "guest", string.Empty, string.Empty, MissionDescriptor.NextMissionUndefined, levelDirectory, commonDirectory, levelDirectory,
            Location, levelName, string.Empty);
        LoadedMission mission = new(descriptor, packs[0], packs[^1], [], Packs: packs);

        SoundDefinitionRegistry sounds = new(log);
        sounds.Register(guest.Loop.Natives);
        sounds.Load(guest.Resources, guest.Loop.Natives, SoundDefinitionRegistry.CommonScriptResource);
        return new Igi1GuestContent(guest, mission, arsenal, sounds);
    }

    // The level packs that name a body still wanted, in the levels' order, until none is.
    private static IEnumerable<(string Directory, string Name)> Levels(string dataRoot, HashSet<string> wanted)
    {
        string location = Path.Combine(dataRoot, "missions", Location);
        if (!Directory.Exists(location))
        {
            yield break;
        }

        IEnumerable<string> levels = Directory.EnumerateDirectories(location, "level*")
            .OrderBy(static directory => int.TryParse(Path.GetFileName(directory).AsSpan("level".Length), out int n) ? n : int.MaxValue);
        foreach (string directory in levels)
        {
            if (wanted.Count == 0)
            {
                yield break;
            }

            string name = Path.GetFileName(directory).ToLowerInvariant();
            string pack = Path.Combine(directory, $"{name}.mtp");
            if (File.Exists(pack) && Encoding.Latin1.GetString(File.ReadAllBytes(pack)) is { } names
                && wanted.Any(model => names.Contains(model, StringComparison.OrdinalIgnoreCase)))
            {
                yield return ($"missions/{Location}/{name}", name);
            }
        }
    }
}
