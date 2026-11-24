using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Titles;
using OpenIGI.Net.Identity;
using OpenIGI.Releases.Accounts;
using OpenIGI.Releases.Maps;

namespace OpenIGI.Launcher;

/// <summary>
/// The player's online account, from the launcher: signed into with Discord, as from the game's profile page. Both sign in
/// with the same key -- the one the game plays under, kept in the settings folder -- and an account is the keys linked to
/// it, so signing in here signs the game in too, and signing out of either signs out of both.
/// </summary>
internal sealed partial class LauncherModel
{
    private static readonly HttpClient AccountHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly TimeSpan RecordEvery = TimeSpan.FromMinutes(3);

    private AccountLink? _account;
    private EcdsaPlayerIdentity? _identity;
    private int _accountShown;
    private Task<string?>? _avatarAsked;
    private string? _avatarFor;
    private Task<PlayerRecord>? _recordAsked;
    private DateTimeOffset? _recordAt;

    // The game the shown record is of, and the one last asked about: each game's record is its own.
    private string? _recordFor;
    private string? _recordAskedFor;

    /// <summary>Gets the account, made the first time it is asked for: the master is asked at once.</summary>
    public AccountLink Account => _account ??= new AccountLink(
        new AccountService(AccountHttp, MapService.Master),
        () => (_identity ??= LoadIdentity()) is { } identity ? new PlayerRequestSigner(identity) : null,
        Open,
        "Desktop",
        rememberPath: SettingsPaths.GetFilePath("account.json"));

    /// <summary>Gets the signed-in player's Discord avatar as a file on this computer, once it has come down; else <see langword="null"/>.</summary>
    public string? AvatarFile { get; private set; }

    /// <summary>Gets the signed-in player's record on the official servers, once read; else <see langword="null"/>.</summary>
    public PlayerRecord? Record { get; private set; }

    // The game's own key, read (or made, the first time anything needs it) where the game keeps it.
    private EcdsaPlayerIdentity? LoadIdentity()
    {
        try
        {
            return EcdsaPlayerIdentity.LoadOrCreate(SettingsPaths.GetFilePath("identity.key"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            _logger.Warning(LogCategory, $"The player's key could not be read: {ex.Message}");
            return null;
        }
    }

    // Each frame: the account's requests collected, a waiting sign-in asked about, the avatar and record fetched while
    // signed in; the page told when anything it shows changed.
    private void TickAccount()
    {
        if (_account is null)
        {
            return;
        }

        _account.Update();
        PlayerAccount? signedIn = _account.IsSignedIn ? _account.Account : null;
        TickAvatar(signedIn);
        TickRecord(_account.State == AccountLinkState.Linked ? signedIn : null);

        int shown = HashCode.Combine(_account.State, signedIn?.Id, signedIn?.Name, _account.Error, AvatarFile, Record?.SeasonTotals);
        if (shown != _accountShown)
        {
            _accountShown = shown;
            _logger.Information(LogCategory, _account.State switch
            {
                AccountLinkState.Linked => $"Account: signed in with Discord as {signedIn?.Name}.",
                AccountLinkState.Unlinked => "Account: not signed in.",
                AccountLinkState.Waiting => "Account: waiting for the Discord sign-in in the browser.",
                AccountLinkState.Offline => $"Account: the master can't be reached ({_account.Error}).",
                _ => $"Account: {_account.State}{(_account.Error is { } error ? $" ({error})" : string.Empty)}.",
            });
            Changed();
        }
    }

    // The avatar, fetched into the settings folder the game shares (AvatarCache): once per account and picture.
    private void TickAvatar(PlayerAccount? signedIn)
    {
        if (_avatarAsked is { IsCompleted: true } done)
        {
            _avatarAsked = null;
            AvatarFile = done.IsCompletedSuccessfully ? done.Result : null;
            if (!done.IsCompletedSuccessfully)
            {
                _logger.Warning(LogCategory, $"The Discord avatar could not be downloaded: {done.Exception?.GetBaseException().Message}");
            }
        }

        string? wanted = signedIn?.Avatar;
        if (wanted == _avatarFor || _avatarAsked is not null)
        {
            return;
        }

        _avatarFor = wanted;
        AvatarFile = null;
        if (signedIn is not null)
        {
            _avatarAsked = AvatarCache.FetchAsync(AccountHttp, signedIn, SettingsPaths.GetUserDirectory());
        }
    }

    // This season's record on the official servers, read every few minutes while signed in.
    private void TickRecord(PlayerAccount? signedIn)
    {
        if (_recordAsked is { IsCompleted: true } done)
        {
            _recordAsked = null;
            if (done.IsCompletedSuccessfully && _recordAskedFor == RecordTitle)
            {
                Record = done.Result;
                _recordFor = _recordAskedFor;
            }
        }

        if (signedIn is null)
        {
            Record = null;
            _recordAt = null;
            return;
        }

        // Another game chosen: its own record, not the last game's, as soon as it comes.
        string title = RecordTitle;
        if (_recordFor is not null && _recordFor != title)
        {
            Record = null;
            _recordFor = null;
            _recordAt = null;
        }

        if (_recordAsked is null && (_recordAt is not { } at || DateTimeOffset.UtcNow - at > RecordEvery))
        {
            _recordAt = DateTimeOffset.UtcNow;
            _recordAskedFor = title;
            _recordAsked = new StatsService(AccountHttp, MapService.Master).RecordAsync(signedIn.Id, title);
        }
    }

    // The game whose record the account panel shows: the one chosen (the editor's, Project I.G.I.'s).
    private string RecordTitle => (Games[Selected].Retail ?? GameTitle.Igi1).Id;

    private void DisposeAccount() => _identity?.Dispose();
}
