using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Host.Multiplayer;

namespace OpenIGI.Host;

/// <summary>The files a multiplayer session plays from: the server's, on every player's machine.</summary>
/// <remarks>
/// <para>
/// While a session is set, no content layer stands in for a file a level plays from (<see cref="GameplayFiles"/>): those are
/// read from the installation for host, server and client alike, and a texture or sound pack still comes through. A server
/// lists and sends its own; a client compares them with its own and reads the server's in place of any that differ, kept by
/// hash in the user's cache folder and never written into the game's.
/// </para>
/// <para>
/// The tables a game reads once -- its weapons, ammunition, materials, the bodies' animations -- are read again when a
/// client puts a server's in place before it joins, and again when the session goes, so the game is its own once more.
/// </para>
/// </remarks>
public sealed partial class GameHost
{
    // The session's client-side files, while a client session is set.
    private SessionFileSync? _sessionFiles;

    // Which of the running title's files a session compares: the title's own description, set with the title.
    private GameplayFileLayout _gameplayFiles = GameplayFileLayout.None;

    // The rules for the running title, made with the mission table they read a map's folders from.
    private GameplayFileRules? _gameplayRules;
    private Engine.Titles.IMissionCatalogue<Game.World.MissionDescriptor>? _gameplayRulesMissions;

    /// <summary>Gets the folder the servers' files are kept in, by hash.</summary>
    public static string SessionFileCache => Path.Combine(Core.Configuration.SettingsPaths.GetUserDirectory(), "cache", "mp");

    // A session set, or gone: the shield on the level's files raised or lowered, and the session given the files it reads.
    private void AttachSessionFiles(IHostTickObserver? session)
    {
        GameplayFileRules rules = GameplayRules;
        if (session is null)
        {
            bool shared = _sessionFiles?.Pins.Keys.Any(p => !rules.IsLevelFile(p)) == true;
            _sessionFiles = null;
            _fileSystem.Pins = null;
            if (_overrides is not null)
            {
                _overrides.Withheld = null;
                _resources.ReapplyOverrides();
            }

            if (shared)
            {
                _logger.Information("Files", "The session is over; the game's own tables are read again.");
                ReloadTitleTables();
            }

            return;
        }

        if (_overrides is not null && _overrides.Withheld is null)
        {
            _overrides.Withheld = WithheldInSession;
            int swapped = _resources.ReapplyOverrides();
            if (swapped > 0)
            {
                _logger.Information("Files", $"{swapped} content-layer file(s) a level plays from are set aside for the session.");
            }
        }

        if (session is HostedSession hosted)
        {
            hosted.Files ??= new HostSessionFiles(_fileSystem, () => GameplayRules, withoutPins: false);
        }
        else if (session is SessionClient { Playback: null } client)
        {
            client.Files ??= NewSessionFileSync();
            client.MapFilesInPlace ??= MapFilesInPlace;
            _sessionFiles = client.Files;
            _fileSystem.Pins = _sessionFiles.Pins;
        }
    }

    // This game's side of a server's files: the installation's own as compared, the servers' kept in the shared cache.
    private SessionFileSync NewSessionFileSync() =>
        new(new HostSessionFiles(_fileSystem, () => GameplayRules, withoutPins: true), SessionFileCache, _logger);

    // Whether a map's level is where the game reads it: the load gate's word, which on a browser is also what fetches it.
    private bool MapFilesInPlace(string map) =>
        !int.TryParse(map, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id)
        || !_missions.TryGet(id, out Game.World.MissionDescriptor? mission)
        || mission is null
        || _loadGate.IsReady(mission, out _);

    // The files no content layer stands in for while a session lasts.
    private bool WithheldInSession(string path) => GameplayRules.IsGameplay(path);

    // The running title's rules, made again when the title or its mission table changes.
    private GameplayFileRules GameplayRules
    {
        get
        {
            if (_gameplayRules is null || !ReferenceEquals(_gameplayRules.Layout, _gameplayFiles) || !ReferenceEquals(_gameplayRulesMissions, _missions))
            {
                Engine.Titles.IMissionCatalogue<Game.World.MissionDescriptor> missions = _missions;
                _gameplayRulesMissions = missions;
                _gameplayRules = new GameplayFileRules(_gameplayFiles, map => MapFolders(missions, map));
            }

            return _gameplayRules;
        }
    }

    // The folders a map plays from, as a list writes them: those its mission names, from this machine's own table.
    private static IReadOnlyList<string>? MapFolders(Engine.Titles.IMissionCatalogue<Game.World.MissionDescriptor>? missions, string map)
    {
        if (missions is null
            || !int.TryParse(map, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id)
            || !missions.TryGet(id, out Game.World.MissionDescriptor? mission)
            || mission is null)
        {
            return null;
        }

        return [.. new[] { mission.MissionDirectory, mission.CommonPackDirectory, mission.LevelPackDirectory }
            .Where(static d => !string.IsNullOrWhiteSpace(d))
            .Select(static d => GameplayFiles.Normalize(d).TrimEnd('/'))
            .Where(static d => d.Length > 0 && GameplayFiles.IsSafePath(d))
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Reads the running game's tables again -- the whole game, as a change of game does -- at the next frame boundary.</summary>
    /// <remarks>For a server's shared files put in place, or taken out of it: the weapons and the rest are read once a game.</remarks>
    internal void ReloadTitleTables() => _pendingTitle = ActiveTitle;
}
