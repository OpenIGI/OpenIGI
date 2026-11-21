using OpenIGI.Core.Configuration;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Kit;

namespace OpenIGI.Host.Menu;

/// <summary>
/// The Discord avatars of a session's players, as the scoreboard draws them: each fetched once, by the
/// <see cref="DiscordAvatar"/> reference its player's line carries, into the settings folder (<see cref="AvatarCache"/>) and made a
/// texture; a few at a time, and one that can't be had not asked for again.
/// </summary>
/// <param name="pictures">Makes the textures.</param>
/// <param name="http">The client to download through.</param>
internal sealed class PlayerAvatars(MenuPictureLoader pictures, HttpClient http) : IDisposable
{
    // The most pictures kept before they are all let go and fetched again as they are wanted, and the most downloads at once.
    private const int MostKept = 64;
    private const int MostAtOnce = 4;

    private readonly Dictionary<string, UiPicture> _made = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<string>> _fetching = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failed = new(StringComparer.Ordinal);

    /// <summary>A player's avatar, once it has come down; else <see langword="null"/>, and it is asked for.</summary>
    /// <param name="reference">The player's <see cref="DiscordAvatar"/> reference; empty for none.</param>
    /// <returns>The picture, or <see langword="null"/>.</returns>
    public UiPicture? Of(string reference)
    {
        if (string.IsNullOrEmpty(reference) || _failed.Contains(reference))
        {
            return null;
        }

        if (_made.TryGetValue(reference, out UiPicture made))
        {
            return made;
        }

        if (_fetching.TryGetValue(reference, out Task<string>? fetch))
        {
            if (!fetch.IsCompleted)
            {
                return null;
            }

            _fetching.Remove(reference);
            if (!fetch.IsCompletedSuccessfully || pictures.Load(fetch.Result) is not { } loaded)
            {
                _failed.Add(reference);
                return null;
            }

            if (_made.Count >= MostKept)
            {
                Release();
            }

            _made[reference] = loaded;
            return loaded;
        }

        // Only an address on Discord's own server: the reference came from another player.
        if (_fetching.Count < MostAtOnce && DiscordAvatar.Url(reference) is { } url)
        {
            _fetching[reference] = AvatarCache.FetchUrlAsync(http, url, SettingsPaths.GetUserDirectory(), "player-avatar");
        }

        return null;
    }

    /// <inheritdoc/>
    public void Dispose() => Release();

    private void Release()
    {
        foreach (UiPicture picture in _made.Values)
        {
            pictures.Release(picture);
        }

        _made.Clear();
    }
}
