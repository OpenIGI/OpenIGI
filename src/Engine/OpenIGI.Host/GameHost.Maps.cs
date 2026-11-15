using System.Globalization;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Releases.Maps;

namespace OpenIGI.Host;

/// <content>
/// Community maps on this machine: the installation's own files (which a map names rather than carries, and which unpacking
/// gives back), the installed maps' library, and a map played -- its files unpacked once, laid over the game as a content
/// layer of its own while it is played, and its mission started.
/// </content>
public sealed partial class GameHost
{
    // The content layer a map is played from, and the map it is.
    private const string MapLayerPrefix = "map-";
    private string? _mapLayer;

    // A map asked to be played, started between frames; and the downloads going on.
    private string? _mapPlayWanted;
    private readonly List<Task> _mapDownloads = [];

    private void RegisterMapCommands(ConsoleSystem console)
    {
        console.Commands.Add(
            "maps",
            "Lists the community maps installed, and with 'online' those published for this game.",
            context =>
            {
                foreach (MapListing map in Maps.Installed())
                {
                    context.Print($"{map.Id}  {map.Title} by {map.Author} (v{map.Version}, {map.Game})");
                }

                if (context.Count > 0 && context[0] == "online")
                {
                    string game = _titleRuntime.Title.Id;
                    IOpenIgiLogger logger = _logger;
                    _mapDownloads.Add(Task.Run(async () =>
                    {
                        using HttpClient http = new();
                        foreach (MapListing map in await new MapService(http, MapService.Master).ListAsync(game).ConfigureAwait(false))
                        {
                            logger.Information("Maps", string.Create(CultureInfo.InvariantCulture, $"online: {map.Id}  {map.Title} by {map.Author} (v{map.Version}, {map.Downloads} downloads, {map.Rating:0.0} stars)"));
                        }
                    }));
                }
            },
            "[online]");
        console.Commands.Add(
            "map_get",
            "Downloads a published community map and installs it: map_get <id>.",
            context =>
            {
                if (context.Count == 0)
                {
                    context.Error("Name the map.");
                    return;
                }

                string id = context[0];
                IOpenIgiLogger logger = _logger;
                _mapDownloads.Add(Task.Run(async () =>
                {
                    try
                    {
                        await InstallMapAsync(id).ConfigureAwait(false);
                        logger.Information("Maps", $"Installed {id}.");
                    }
                    catch (Exception error) when (error is HttpRequestException or MapRefusedException or InvalidDataException or IOException)
                    {
                        logger.Warning("Maps", $"{id} not installed: {error.Message}");
                    }
                }));
            },
            "<id>");
        console.Commands.Add(
            "map_play",
            "Plays an installed community map: map_play <id>.",
            context => _mapPlayWanted = context.Count > 0 ? context[0] : null,
            "<id>");
        console.Commands.Add(
            "map_remove",
            "Removes an installed community map: map_remove <id>.",
            context =>
            {
                if (context.Count > 0)
                {
                    Maps.Remove(context[0]);
                    context.Print($"Removed {context[0]}.");
                }
            },
            "<id>");
    }

    /// <summary>Downloads a published map and installs it.</summary>
    /// <param name="id">The map.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>Its listing.</returns>
    internal static async Task<MapListing> InstallMapAsync(string id, CancellationToken cancel = default)
    {
        using HttpClient http = new() { Timeout = TimeSpan.FromMinutes(5) };
        MapService service = new(http, MapService.Master);
        MapListing listing = (await service.ListAsync(null, cancel).ConfigureAwait(false)).FirstOrDefault(m => m.Id == id)
            ?? throw new InvalidDataException($"No published map {id}.");
        Maps.Install(listing, await service.DownloadAsync(listing, cancel).ConfigureAwait(false));
        return listing;
    }

    // A map asked for, played once the title is up and nothing is loading; downloads that have ended let go of.
    private void AdvanceMaps(FrameLoopState state)
    {
        _mapDownloads.RemoveAll(static t => t.IsCompleted);
        if (_mapPlayWanted is not { } id || _titleRuntime is null || EnteringMission || _menuFlow is null)
        {
            return;
        }

        _mapPlayWanted = null;
        // From the menu the menu's own way in, as Play on a mission there is; else straight in.
        Action<int> start = _menuFlow.MenuMode && state.ShellHost is { } shell
            ? mission => shell.StartMission(mission, _configuration.Active?.Difficulty ?? (int)Game.Ai.AiDifficulty.Normal)
            : StartMission;
        if (PlayMap(id, start) is { } problem)
        {
            _logger.Warning("Maps", $"{id} not played: {problem}");
        }
    }

    /// <summary>Gets the installed community maps.</summary>
    public static MapLibrary Maps => new(Path.Combine(SettingsPaths.GetUserDirectory(), "maps"));

    /// <summary>Gets the installation's publishing identity, made the first time it is asked for.</summary>
    /// <returns>The identity; the caller disposes it.</returns>
    public static MapIdentity MapIdentity() => Releases.Maps.MapIdentity.LoadOrCreate(Path.Combine(SettingsPaths.GetUserDirectory(), "publisher.key"));

    /// <summary>Lists the installation's own files under a folder and its folders, without any overlay.</summary>
    /// <param name="folder">The folder from the game's root.</param>
    /// <returns>Each file's path from the game's root, forward-slashed.</returns>
    internal IEnumerable<string> GameFiles(string folder)
    {
        if (!_fileSystem.TryGetDevice(LocalFileDevice.DeviceName, out IFileDevice? device))
        {
            yield break;
        }

        Stack<string> folders = new([folder.Replace('\\', '/').Trim('/')]);
        while (folders.TryPop(out string? at))
        {
            foreach (string file in device.EnumerateFiles(at, "*"))
            {
                yield return file.Replace('\\', '/');
            }

            foreach (string inner in device.EnumerateDirectories(at))
            {
                folders.Push(inner.Replace('\\', '/'));
            }
        }
    }

    /// <summary>Reads one of the installation's own files, without any overlay.</summary>
    /// <param name="path">The path from the game's root.</param>
    /// <returns>Its bytes, or <see langword="null"/> when the installation has none there.</returns>
    internal ReadOnlyMemory<byte>? ReadGameFile(string path)
    {
        if (!_fileSystem.TryGetDevice(LocalFileDevice.DeviceName, out IFileDevice? device)
            || !device.TryOpenRead(path.Replace('\\', '/').TrimStart('/'), out Stream? stream) || stream is null)
        {
            return null;
        }

        using (stream)
        {
            using MemoryStream copy = new();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
    }

    /// <summary>Plays an installed map: its files unpacked (once), laid over the game, the missions read again, its mission started.</summary>
    /// <param name="id">The map.</param>
    /// <param name="start">Starts its mission: from the menu as the menu starts one (the cinematic load, the menu left), else at once.</param>
    /// <returns>A problem, or <see langword="null"/> once it is starting.</returns>
    internal string? PlayMap(string id, Action<int> start)
    {
        (string files, MapManifest manifest) unpacked;
        try
        {
            unpacked = Maps.Unpacked(id, ReadGameFile);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return error.Message;
        }

        if (!string.Equals(unpacked.manifest.Game, _titleRuntime.Title.Id, StringComparison.OrdinalIgnoreCase))
        {
            return $"{unpacked.manifest.Title} is for another game ({unpacked.manifest.Game}).";
        }

        LeaveMap(rescan: false);
        _mapLayer = MapLayerPrefix + id;
        _overrides.Mount(new Engine.Resources.OverrideLayer(_mapLayer, unpacked.manifest.Title, unpacked.files));
        ApplyOverrideChange(out _);
        _missions.Scan(_fileSystem, _resources, _loop.Natives);
        if (!_missions.Missions.Any(m => m.Id == unpacked.manifest.MissionId))
        {
            LeaveMap(rescan: true);
            return $"{unpacked.manifest.Title} defines no mission the game can read.";
        }

        _logger.Information("Maps", $"Playing the community map {unpacked.manifest.Title} ({id}), mission {unpacked.manifest.MissionId}.");
        start(unpacked.manifest.MissionId);
        return null;
    }

    /// <summary>Takes a played map's layer off the game, so the missions are the game's own again.</summary>
    /// <param name="rescan">Whether to read the missions again now.</param>
    internal void LeaveMap(bool rescan)
    {
        if (_mapLayer is not { } layer)
        {
            return;
        }

        _mapLayer = null;
        if (_overrides.Unmount(layer, out _))
        {
            ApplyOverrideChange(out _);
        }

        if (rescan)
        {
            _missions.Scan(_fileSystem, _resources, _loop.Natives);
        }
    }
}
