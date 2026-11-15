using OpenIGI.Core.Configuration;
using OpenIGI.Releases.Maps;

namespace OpenIGI.Launcher;

/// <content>
/// Community maps: those published for the selected game, and those installed in the game's own maps folder, where the game
/// finds them (the launcher downloads; the game unpacks a map, from its own files, the first time it is played).
/// </content>
internal sealed partial class LauncherModel
{
    private IReadOnlyList<MapListing> _onlineMaps = [];
    private string? _mapsFetchedFor;
    private bool _mapsFetching;

    /// <summary>Gets the maps folder the game reads: the user's, as the game's own settings folder holds it.</summary>
    private static MapLibrary MapLibrary => new(Path.Combine(SettingsPaths.GetUserDirectory(), "maps"));

    /// <summary>Gets the selected game's maps: installed ones first (with any newer version known), then those published.</summary>
    public IReadOnlyList<(MapListing Map, int? Installed)> Maps
    {
        get
        {
            if (Current.Retail is not { } title)
            {
                return [];
            }

            Dictionary<string, MapListing> online = _onlineMaps.ToDictionary(static m => m.Id, StringComparer.Ordinal);
            List<(MapListing, int?)> maps = [];
            foreach (MapListing mine in MapLibrary.Installed().Where(m => string.Equals(m.Game, title.Id, StringComparison.OrdinalIgnoreCase)))
            {
                maps.Add((online.GetValueOrDefault(mine.Id) ?? mine, mine.Version));
            }

            HashSet<string> had = [.. maps.Select(static m => m.Item1.Id)];
            maps.AddRange(_onlineMaps.Where(m => !had.Contains(m.Id)).Select(static m => (m, (int?)null)));
            return maps;
        }
    }

    /// <summary>Gets what the maps page says of the last thing done: a download, a failure; empty when nothing.</summary>
    public string MapsStatus { get; private set; } = string.Empty;

    /// <summary>Gets the map being downloaded, or <see langword="null"/>.</summary>
    public string? MapDownloading { get; private set; }

    /// <summary>Fetches what is published for the selected game, once a game, or again when asked.</summary>
    /// <param name="again">Whether to fetch even though it has been.</param>
    public void RefreshMaps(bool again = false)
    {
        if (Current.Retail is not { } title || _mapsFetching || (!again && _mapsFetchedFor == title.Id))
        {
            return;
        }

        _mapsFetching = true;
        _mapsFetchedFor = title.Id;
        string game = title.Id;
        _ = Task.Run(async () =>
        {
            string status = string.Empty;
            IReadOnlyList<MapListing> listed = [];
            try
            {
                using HttpClient http = new();
                listed = await new MapService(http, MapService.Master).ListAsync(game).ConfigureAwait(false);
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or MapRefusedException or System.Text.Json.JsonException)
            {
                status = $"The maps could not be fetched: {error.Message}";
            }

            _results.Enqueue(() =>
            {
                _mapsFetching = false;
                _onlineMaps = listed;
                MapsStatus = status;
                Changed();
            });
        });
    }

    /// <summary>Downloads a published map into the game's maps folder, or its newer version over the one there.</summary>
    /// <param name="id">The map.</param>
    public void InstallMap(string id)
    {
        if (MapDownloading is not null || _onlineMaps.FirstOrDefault(m => m.Id == id) is not { } listing)
        {
            return;
        }

        MapDownloading = id;
        MapsStatus = $"Downloading {listing.Title}…";
        Changed();
        _ = Task.Run(async () =>
        {
            string status;
            try
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromMinutes(5) };
                MapLibrary.Install(listing, await new MapService(http, MapService.Master).DownloadAsync(listing).ConfigureAwait(false));
                status = $"{listing.Title} is installed: play it from the game's Maps page.";
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or MapRefusedException or InvalidDataException or IOException)
            {
                status = $"{listing.Title} was not installed: {error.Message}";
            }

            _results.Enqueue(() =>
            {
                MapDownloading = null;
                MapsStatus = status;
                Changed();
            });
        });
    }

    /// <summary>Removes an installed map from the game's maps folder.</summary>
    /// <param name="id">The map.</param>
    public void RemoveMap(string id)
    {
        MapLibrary.Remove(id);
        MapsStatus = string.Empty;
        Changed();
    }
}
