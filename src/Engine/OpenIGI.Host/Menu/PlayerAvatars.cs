using OpenIGI.Core.Configuration;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Kit;

namespace OpenIGI.Host.Menu;

/// <summary>
/// The Discord avatars of a session's players, as the scoreboard and the leaderboard draw them: each fetched once, by the
/// <see cref="DiscordAvatar"/> reference its player's line carries, into the settings folder (<see cref="AvatarFiles"/>, a few
/// at a time, one that can't be had not asked for again) and made a texture.
/// </summary>
/// <param name="pictures">Makes the textures.</param>
/// <param name="http">The client to download through.</param>
internal sealed class PlayerAvatars(MenuPictureLoader pictures, HttpClient http) : IDisposable
{
    // The most pictures kept before they are all let go and made again as they are wanted.
    private const int MostKept = 64;

    private readonly AvatarFiles _files = new(http, SettingsPaths.GetUserDirectory(), "player-avatar");
    private readonly Dictionary<string, UiPicture> _made = new(StringComparer.Ordinal);

    /// <summary>A player's avatar, once it has come down; else <see langword="null"/>, and it is asked for.</summary>
    /// <param name="reference">The player's <see cref="DiscordAvatar"/> reference; empty for none.</param>
    /// <returns>The picture, or <see langword="null"/>.</returns>
    public UiPicture? Of(string reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        if (_made.TryGetValue(reference, out UiPicture made))
        {
            return made;
        }

        // Only an address on Discord's own server: the reference came from another player.
        if (DiscordAvatar.Url(reference) is not { } url || _files.PathOf(url) is not { } file)
        {
            return null;
        }

        if (pictures.Load(file) is not { } loaded)
        {
            _files.Unusable(url);
            return null;
        }

        if (_made.Count >= MostKept)
        {
            Release();
        }

        _made[reference] = loaded;
        return loaded;
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
