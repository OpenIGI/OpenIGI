using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace OpenIGI.Net.Rooms;

/// <summary>A room as the master server shows it.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Title">The game's id.</param>
/// <param name="Protocol">The protocol id, as hex.</param>
/// <param name="Name">Its name.</param>
/// <param name="Map">The map or mission its host plays.</param>
/// <param name="Mode">The game mode.</param>
/// <param name="Version">The host's build.</param>
/// <param name="Players">How many play, the host included.</param>
/// <param name="MaxPlayers">How many may.</param>
/// <param name="Passworded">Whether joining takes a password.</param>
public sealed record RoomListing(
    string Code, string Title, string Protocol, string Name, string Map, string Mode, string Version, int Players, int MaxPlayers, bool Passworded);

/// <summary>Reads rooms off the master server: one by its code, or the public ones.</summary>
public static class RoomDirectory
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Looks a room up by its code.</summary>
    /// <param name="master">The master server.</param>
    /// <param name="code">The code; case does not matter.</param>
    /// <param name="cancellation">Stops the lookup.</param>
    /// <returns>The room, or <see langword="null"/> when no room has the code.</returns>
    /// <exception cref="HttpRequestException">The master server could not be reached.</exception>
    public static async Task<RoomListing?> FindAsync(Uri master, string code, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        using HttpResponseMessage response = await Http.GetAsync(
            new Uri(master, $"/v1/rooms/{Uri.EscapeDataString(code.Trim().ToUpperInvariant())}"), cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return Read(JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)) as JsonObject);
    }

    /// <summary>Lists the public rooms of a game that speak its protocol.</summary>
    /// <param name="master">The master server.</param>
    /// <param name="title">The game's id.</param>
    /// <param name="protocol">The protocol id, as hex.</param>
    /// <param name="cancellation">Stops the lookup.</param>
    /// <returns>The rooms, fullest first.</returns>
    /// <exception cref="HttpRequestException">The master server could not be reached.</exception>
    public static async Task<IReadOnlyList<RoomListing>> ListAsync(Uri master, string title, string protocol, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        string query = $"/v1/servers?title={Uri.EscapeDataString(title)}&protocol={Uri.EscapeDataString(protocol)}";
        string text = await Http.GetStringAsync(new Uri(master, query), cancellation).ConfigureAwait(false);
        List<RoomListing> rooms = [];
        if (JsonNode.Parse(text) is JsonObject list && list["servers"] is JsonArray servers)
        {
            foreach (JsonNode? server in servers)
            {
                if (server is JsonObject entry && entry["room"] is not null && Read(entry) is { } room)
                {
                    rooms.Add(room);
                }
            }
        }

        return [.. rooms.OrderByDescending(r => r.Players).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static RoomListing? Read(JsonObject? room)
    {
        if (room is null || (string?)room["room"] is not { Length: > 0 } code)
        {
            return null;
        }

        return new RoomListing(
            code,
            (string?)room["title"] ?? string.Empty,
            (string?)room["protocol"] ?? string.Empty,
            (string?)room["name"] ?? code,
            (string?)room["map"] ?? string.Empty,
            (string?)room["mode"] ?? string.Empty,
            (string?)room["version"] ?? string.Empty,
            (int?)room["players"] ?? 0,
            (int?)room["maxPlayers"] ?? 0,
            (bool?)room["passworded"] ?? false);
    }

    /// <summary>Gets a room's mission number, when its map is one.</summary>
    /// <param name="room">The room.</param>
    /// <returns>The mission, or <see langword="null"/>.</returns>
    public static int? Mission(this RoomListing room) =>
        int.TryParse(room?.Map, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mission) ? mission : null;
}
