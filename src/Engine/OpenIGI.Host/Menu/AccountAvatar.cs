using OpenIGI.Core.Configuration;
using OpenIGI.Frontend;
using OpenIGI.Releases.Accounts;

namespace OpenIGI.Host.Menu;

/// <summary>
/// The signed-in player's Discord avatar as a picture the menu draws: fetched as PNG into the settings folder
/// (<see cref="AvatarCache"/>, shared with the launcher) and made a texture; let go when the player signs out or changes it.
/// </summary>
/// <param name="pictures">Makes the texture.</param>
/// <param name="http">The client to download through.</param>
internal sealed class AccountAvatar(MenuPictureLoader pictures, HttpClient http) : IDisposable
{
    private string? _for;
    private Task<string?>? _fetch;

    /// <summary>Gets the avatar, once it has come down; else <see langword="null"/>.</summary>
    public MenuPicture? Picture { get; private set; }

    /// <summary>Follows the account signed in: its avatar fetched and made, or let go. Called every frame.</summary>
    /// <param name="account">The account, or <see langword="null"/> when nobody is signed in.</param>
    public void Update(PlayerAccount? account)
    {
        if (_fetch is { IsCompleted: true } fetched)
        {
            _fetch = null;
            if (fetched.IsCompletedSuccessfully && fetched.Result is { } file)
            {
                Release();
                Picture = pictures.Load(file);
            }
        }

        if (account?.Avatar == _for || _fetch is not null)
        {
            return;
        }

        _for = account?.Avatar;
        Release();
        if (account is not null)
        {
            _fetch = AvatarCache.FetchAsync(http, account, SettingsPaths.GetUserDirectory());
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Release();

    private void Release()
    {
        if (Picture is { } made)
        {
            pictures.Release(made);
            Picture = null;
        }
    }
}
