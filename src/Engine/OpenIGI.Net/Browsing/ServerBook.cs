using System.Text.Json.Nodes;

namespace OpenIGI.Net.Browsing;

/// <summary>A server the player keeps: a favourite, or one played on.</summary>
/// <param name="Key">What it is known by (<see cref="BrowserServer.Key"/>).</param>
/// <param name="Name">The name it had, shown until it answers again.</param>
/// <param name="At">When it was added or last played on.</param>
public sealed record BookedServer(string Key, string Name, DateTimeOffset At);

/// <summary>
/// The player's favourite servers and the ones played on of late, kept in a small JSON file in the profile so the browser
/// asks after them even when the master does not list them (a LAN server, a private one, one whose master is down).
/// </summary>
/// <remarks>Every read and write is best effort: a book that cannot be read is empty, one that cannot be written stays in memory.</remarks>
public sealed class ServerBook
{
    /// <summary>The most servers kept as played on; the oldest goes first.</summary>
    public const int HistoryLength = 20;

    private readonly string? _path;
    private readonly List<BookedServer> _favourites = [];
    private readonly List<BookedServer> _history = [];

    /// <summary>Initializes a new instance of the <see cref="ServerBook"/> class.</summary>
    /// <param name="path">The file it is kept in, or <see langword="null"/> for one kept in memory only.</param>
    public ServerBook(string? path = null)
    {
        _path = path;
        Load();
    }

    /// <summary>Gets the favourites, newest first.</summary>
    public IReadOnlyList<BookedServer> Favourites => _favourites;

    /// <summary>Gets the servers played on, most recent first.</summary>
    public IReadOnlyList<BookedServer> History => _history;

    /// <summary>Gets whether a server is a favourite.</summary>
    /// <param name="key">Its key.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool IsFavourite(string key) => _favourites.Exists(f => f.Key == key);

    /// <summary>Makes a server a favourite, or takes it off the favourites.</summary>
    /// <param name="key">Its key.</param>
    /// <param name="name">Its name.</param>
    /// <param name="favourite">Whether it is one.</param>
    public void SetFavourite(string key, string name, bool favourite)
    {
        _favourites.RemoveAll(f => f.Key == key);
        if (favourite)
        {
            _favourites.Insert(0, new BookedServer(key, name, DateTimeOffset.UtcNow));
        }

        Save();
    }

    /// <summary>Notes a server played on.</summary>
    /// <param name="key">Its key.</param>
    /// <param name="name">Its name.</param>
    public void Played(string key, string name)
    {
        _history.RemoveAll(h => h.Key == key);
        _history.Insert(0, new BookedServer(key, name, DateTimeOffset.UtcNow));
        if (_history.Count > HistoryLength)
        {
            _history.RemoveRange(HistoryLength, _history.Count - HistoryLength);
        }

        Save();
    }

    private void Load()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            if (!File.Exists(_path) || JsonNode.Parse(File.ReadAllText(_path)) is not JsonObject book)
            {
                return;
            }

            Read(book["favourites"], _favourites);
            Read(book["history"], _history);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // An unreadable book is an empty one.
        }
    }

    private static void Read(JsonNode? list, List<BookedServer> into)
    {
        foreach (JsonNode? entry in list as JsonArray ?? [])
        {
            if (entry is JsonObject item && (string?)item["key"] is { Length: > 0 } key)
            {
                DateTimeOffset at = DateTimeOffset.TryParse((string?)item["at"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
                    ? parsed
                    : DateTimeOffset.MinValue;
                into.Add(new BookedServer(key, (string?)item["name"] ?? key, at));
            }
        }
    }

    private void Save()
    {
        if (_path is null)
        {
            return;
        }

        JsonObject book = new() { ["favourites"] = Write(_favourites), ["history"] = Write(_history) };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, book.ToJsonString());
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept in memory for this run.
        }
    }

    private static JsonArray Write(List<BookedServer> list) =>
        [.. list.Select(static b => (JsonNode)new JsonObject
        {
            ["key"] = b.Key,
            ["name"] = b.Name,
            ["at"] = b.At.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        })];
}
