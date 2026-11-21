namespace OpenIGI.Frontend.Tests;

/// <summary>An account as the profile page sees one, moved between its states by a test and recording what it was asked.</summary>
internal sealed class FakeAccount : IAccountPanel
{
    public AccountStatus Status { get; set; } = AccountStatus.SignedOut;

    public AccountSummary? Account { get; set; }

    public string? SignInUrl { get; set; }

    public string? Problem { get; set; }

    public bool IsBusy { get; set; }

    public AccountRecord? Record { get; set; }

    public bool CanOpenLinks { get; set; } = true;

    public List<string> Asked { get; } = [];

    public void SignIn()
    {
        Asked.Add("sign in");
        Status = AccountStatus.WaitingForBrowser;
        SignInUrl = "https://master.openigi.com/v1/auth/discord/start?code=NSTG5TWS";
    }

    public void OpenSignInPage() => Asked.Add("open");

    public string? CopySignInLink()
    {
        Asked.Add("copy");
        return SignInUrl;
    }

    public void CancelSignIn()
    {
        Asked.Add("cancel");
        Status = AccountStatus.SignedOut;
        SignInUrl = null;
    }

    public void SignOut()
    {
        Asked.Add("sign out");
        Status = AccountStatus.SignedOut;
        Account = null;
    }

    public void DeleteAccount()
    {
        Asked.Add("delete");
        Status = AccountStatus.SignedOut;
        Account = null;
    }

    /// <summary>Signed in, as the master would say once the player approved.</summary>
    public FakeAccount SignedInAs(string name)
    {
        Status = AccountStatus.SignedIn;
        Account = new AccountSummary("279730101439168512", name, "https://cdn.discordapp.com/embed/avatars/5.png");
        SignInUrl = null;
        Record = new AccountRecord("2026-11", new RecordTotals(12, 7, 96, 41, 2.34, 1460, 23), new RecordTotals(30, 16, 210, 118, 1.78, 3320, 64), ["First blood", "First win", "Regular", "Marksman"]);
        return this;
    }
}
