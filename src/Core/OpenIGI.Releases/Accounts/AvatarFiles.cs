namespace OpenIGI.Releases.Accounts;

/// <summary>
/// Players' Discord avatars as files on this computer, for a list that draws many -- a scoreboard, a leaderboard: each address
/// downloaded once into a folder (<see cref="AvatarCache"/>), a few at a time, and one that can't be had not asked for again.
/// Polled: <see cref="PathOf"/> says where the file is once it has come down, and asks for it until then.
/// </summary>
/// <param name="http">The client to download through.</param>
/// <param name="folder">The folder the files are kept in.</param>
/// <param name="stem">What the files' names begin with.</param>
public sealed class AvatarFiles(HttpClient http, string folder, string stem = "player-avatar")
{
    /// <summary>The most downloads at once.</summary>
    public const int MostAtOnce = 4;

    private readonly Dictionary<string, string> _done = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<string>> _fetching = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failed = new(StringComparer.Ordinal);

    /// <summary>Gets how many downloads are on their way.</summary>
    public int Fetching => _fetching.Count;

    /// <summary>An avatar's file, once it has come down; else <see langword="null"/>, and it is asked for.</summary>
    /// <param name="url">The PNG's address: only one on Discord's own server is fetched, as the address came from another player.</param>
    /// <returns>The file, or <see langword="null"/>.</returns>
    public string? PathOf(string? url)
    {
        if (string.IsNullOrEmpty(url) || _failed.Contains(url))
        {
            return null;
        }

        if (_done.TryGetValue(url, out string? done))
        {
            return done;
        }

        if (_fetching.TryGetValue(url, out Task<string>? fetch))
        {
            if (!fetch.IsCompleted)
            {
                return null;
            }

            _fetching.Remove(url);
            if (!fetch.IsCompletedSuccessfully)
            {
                _failed.Add(url);
                return null;
            }

            _done[url] = fetch.Result;
            return fetch.Result;
        }

        if (_fetching.Count < MostAtOnce && IsDiscords(url))
        {
            _fetching[url] = AvatarCache.FetchUrlAsync(http, url, folder, stem);
        }
        else if (!IsDiscords(url))
        {
            _failed.Add(url);
        }

        return null;
    }

    /// <summary>Marks a file that came down but couldn't be drawn, so it is not offered again.</summary>
    /// <param name="url">The address it came from.</param>
    public void Unusable(string url)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);
        _done.Remove(url);
        _failed.Add(url);
    }

    private static bool IsDiscords(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? address) && address.Scheme == Uri.UriSchemeHttps && address.Host == "cdn.discordapp.com";
}
