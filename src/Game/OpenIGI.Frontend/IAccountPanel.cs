namespace OpenIGI.Frontend;

/// <summary>Where the player stands with their online account, as the profile page shows it.</summary>
public enum AccountStatus
{
    /// <summary>Asking the OpenIGI servers.</summary>
    Checking,

    /// <summary>Playing without an account.</summary>
    SignedOut,

    /// <summary>Asking for a sign-in link.</summary>
    Starting,

    /// <summary>The player is signing in with Discord in their browser.</summary>
    WaitingForBrowser,

    /// <summary>Signed in.</summary>
    SignedIn,

    /// <summary>The servers can't be reached; the account last known is still shown.</summary>
    Offline,
}

/// <summary>The account the player is signed in with.</summary>
/// <param name="Id">Its id, the player's Discord user id.</param>
/// <param name="Name">The name shown, their Discord name.</param>
/// <param name="AvatarUrl">Their avatar's address.</param>
public sealed record AccountSummary(string Id, string Name, string AvatarUrl);

/// <summary>A player's totals on the official servers over a period.</summary>
/// <param name="Matches">The matches played.</param>
/// <param name="Wins">The matches won.</param>
/// <param name="Kills">The kills.</param>
/// <param name="Deaths">The deaths.</param>
/// <param name="Kd">Kills a death.</param>
/// <param name="Score">The points.</param>
public sealed record RecordTotals(int Matches, int Wins, int Kills, int Deaths, double Kd, int Score);

/// <summary>The player's record on the official servers.</summary>
/// <param name="Season">The season now, as <c>yyyy-MM</c>.</param>
/// <param name="ThisSeason">This season's totals.</param>
/// <param name="AllTime">All time's.</param>
public sealed record AccountRecord(string Season, RecordTotals ThisSeason, RecordTotals AllTime);

/// <summary>
/// The player's online account: optional, and signed into with Discord. Signing in names the player by their Discord name
/// in multiplayer, keeps their profile in step between their computers and the browser, and counts their stats. The host
/// supplies it where the build can reach the OpenIGI servers.
/// </summary>
public interface IAccountPanel
{
    /// <summary>Gets where the player stands.</summary>
    AccountStatus Status { get; }

    /// <summary>Gets the account, or the one last known while offline.</summary>
    AccountSummary? Account { get; }

    /// <summary>Gets the signed-in player's Discord avatar as a picture, once it has come down; <see langword="null"/> until then.</summary>
    MenuPicture? Avatar => null;

    /// <summary>Gets the sign-in page while one is waiting, for a player whose browser didn't open.</summary>
    string? SignInUrl { get; }

    /// <summary>Gets what went wrong last, for the player, or <see langword="null"/>.</summary>
    string? Problem { get; }

    /// <summary>Gets how the profile stands with the account, for the player, or <see langword="null"/> while signed out.</summary>
    string? SyncNote => null;

    /// <summary>Gets the player's record on the official servers, once read; <see langword="null"/> while signed out or not yet read.</summary>
    AccountRecord? Record => null;

    /// <summary>Gets whether a request is on its way, for buttons to wait.</summary>
    bool IsBusy { get; }

    /// <summary>Gets whether this build can open the sign-in page itself; otherwise the page offers to copy it.</summary>
    bool CanOpenLinks { get; }

    /// <summary>Begins signing in with Discord: the sign-in page is opened in the player's browser.</summary>
    void SignIn();

    /// <summary>Opens the waiting sign-in page again.</summary>
    void OpenSignInPage();

    /// <summary>Copies the waiting sign-in page's address, where the build has a clipboard.</summary>
    /// <returns>The address copied, or <see langword="null"/>.</returns>
    string? CopySignInLink();

    /// <summary>Stops waiting on the sign-in.</summary>
    void CancelSignIn();

    /// <summary>Signs this computer out: it plays without the account again, which keeps everything else.</summary>
    void SignOut();

    /// <summary>Deletes the account and everything the OpenIGI servers keep of it.</summary>
    void DeleteAccount();
}
