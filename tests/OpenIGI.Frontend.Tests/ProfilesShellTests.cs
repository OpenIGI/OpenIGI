using OpenIGI.Frontend.Screens;
using OpenIGI.Game.Config;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>
/// The profile page: one operative -- their name, their campaign and difficulty, their saves -- and their online account,
/// signed into with Discord; no more profiles made.
/// </summary>
[Collection(ShellTestHarness.CollectionName)]
public sealed class ProfilesShellTests : ShellTestHarness
{
    /// <summary>The one profile, its campaign and its difficulty, which the page changes and keeps; nothing makes another.</summary>
    [Fact]
    public void The_page_shows_the_one_profile_and_changes_its_difficulty()
    {
        string dir = NewDirectory();
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            ProfilesScreen screen = Open(shell);

            Assert.Null(Find<Button>(screen, button => button.Text is "New profile" or "CREATE PROFILE"));
            Assert.NotNull(Find<Label>(screen, label => label.Text == "Joe"));
            ProfileCard progress = Find<ProfileCard>(screen, card => card.Id == "profile-progress")!;
            Assert.Contains("Mission Two", progress.Mission, StringComparison.Ordinal);
            Assert.Equal(2, progress.Unlocked);


            OptionCycler difficulty = Find<OptionCycler>(screen, cycler => cycler.Id == "profile-difficulty")!;
            difficulty.SelectedIndex = 2;
            Assert.Equal(2, host.Profile!.Difficulty);
            Assert.Equal(1, host.SaveProfilesCalls);
        }
        finally
        {
            Remove(dir);
        }
    }

    /// <summary>Renaming keeps the profile's saves, and refuses an empty name or one another profile here has.</summary>
    [Fact]
    public void Outside_a_mission_nothing_can_be_saved_and_an_empty_slot_neither_loads_nor_deletes()
    {
        string dir = NewDirectory();
        try
        {
            (Shell shell, _) = BuildShell(dir);
            ProfilesScreen screen = Open(shell);
            Pump(shell, 0.1f);
            Assert.False(Find<Button>(screen, button => button.Text == "Save here")!.IsEnabled);
            Assert.False(Find<Button>(screen, button => button.Text == "Load")!.IsEnabled);
            Assert.False(Find<Button>(screen, button => button.Text == "Delete save")!.IsEnabled);
        }
        finally
        {
            Remove(dir);
        }
    }

    [Fact]
    public void Renaming_keeps_the_saves_and_refuses_an_empty_or_taken_name()
    {
        string dir = NewDirectory();
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            GameConfig config = shell.Context.Config;
            Assert.True(config.CreateProfile("Alex", 1));
            config.ActiveIndex = 0;

            // Saved from inside a mission, the only place a game is saved from.
            shell.Context.InGame = true;
            ProfilesScreen screen = Open(shell);
            Find<Button>(screen, button => button.Text == "Save here")!.Activate();

            Find<Button>(screen, button => button.Text == "Rename")!.Activate();
            Pump(shell, 0.1f);
            TextBox box = Find<TextBox>(screen, field => field.Id == "profile-rename")!;
            Assert.Equal("Joe", box.Text);

            box.Text = "alex";
            Find<Button>(screen, button => button.Text == "RENAME")!.Activate();
            Assert.NotNull(Find<Label>(screen, label => label.Text == "Another profile on this computer has that name."));
            box.Text = "   ";
            Find<Button>(screen, button => button.Text == "RENAME")!.Activate();
            Assert.NotNull(Find<Label>(screen, label => label.Text == "Enter a name to continue."));
            Assert.Equal(0, host.SaveProfilesCalls);

            box.Text = "Anya";
            Find<Button>(screen, button => button.Text == "RENAME")!.Activate();
            Pump(shell, 0.1f);
            Assert.Equal("Anya", config.Active!.Name);
            Assert.Equal(1, host.SaveProfilesCalls);

            var saves = new OpenIGI.Frontend.Saves.SaveSlotStore(dir);
            Assert.Contains(saves.Slots, slot => slot.ProfileName == "Anya");
            Assert.DoesNotContain(saves.Slots, slot => slot.ProfileName == "Joe");
        }
        finally
        {
            Remove(dir);
        }
    }

    /// <summary>Signed out, SIGN IN WITH DISCORD; waiting, the means to open, copy or cancel; signed in, who as.</summary>
    [Fact]
    public void Signing_in_with_discord_follows_the_account()
    {
        string dir = NewDirectory();
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            var account = new FakeAccount();
            host.Account = account;
            ProfilesScreen screen = Open(shell);

            Assert.NotNull(Find<Label>(screen, label => label.Text == "Playing without an account"));
            Find<Button>(screen, button => button.Id == "profile-sign-in")!.Activate();
            Pump(shell, 0.1f);
            Assert.Equal(["sign in"], account.Asked);
            Assert.NotNull(Find<Label>(screen, label => label.Text == "FINISH SIGNING IN"));

            Find<Button>(screen, button => button.Text == "OPEN AGAIN")!.Activate();
            Pump(shell, 0.1f);
            Find<Button>(screen, button => button.Text == "COPY LINK")!.Activate();
            Pump(shell, 0.1f);
            Assert.NotNull(Find<Button>(screen, button => button.Text == "COPIED"));
            Assert.NotNull(Find<Label>(screen, label => label.Text == account.SignInUrl));

            // The master says yes: the page follows on its own.
            account.SignedInAs("Sh");
            Pump(shell, 0.1f);
            Assert.NotNull(Find<Label>(screen, label => label.Text == "SIGNED IN AS SH"));
            // Signed in, the page shows the Discord name, not the profile's, and offers no rename.
            Assert.NotNull(Find<Label>(screen, label => label.Text == "Signed in with Discord"));
            Assert.NotNull(Find<Label>(screen, label => label.Text == "Sh"));
            Assert.Null(Find<Button>(screen, button => button.Text == "Rename"));
            Assert.Null(Find<Button>(screen, button => button.Id == "profile-sign-in"));

            Find<Button>(screen, button => button.Text == "SIGN OUT")!.Activate();
            Pump(shell, 0.1f);
            Assert.Equal(["sign in", "open", "copy", "sign out"], account.Asked);
            Assert.NotNull(Find<Button>(screen, button => button.Id == "profile-sign-in"));
        }
        finally
        {
            Remove(dir);
        }
    }

    /// <summary>Signed in with a record, this season's counts and all time's are shown; without one, dashes and how to start.</summary>
    [Fact]
    public void The_online_record_shows_the_seasons_counts()
    {
        string dir = NewDirectory();
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            host.Account = new FakeAccount().SignedInAs("Sh");
            ProfilesScreen screen = Open(shell);

            Assert.NotNull(Find<Label>(screen, label => label.Text == "This season. All time: 30 matches, 16 wins, 210 kills."));
            Assert.NotNull(Find<OpenIGI.UI.Kit.Widgets.Stat>(screen, stat => stat.Value == "2.34"));
            Assert.NotNull(Find<OpenIGI.UI.Kit.Widgets.Eyebrow>(screen, eyebrow => eyebrow.Text == "ONLINE RECORD · NOVEMBER 2026"));
        }
        finally
        {
            Remove(dir);
        }
    }

    /// <summary>DELETE ACCOUNT asks first; CANCEL keeps it, DELETE PERMANENTLY deletes it.</summary>
    [Fact]
    public void Deleting_the_account_asks_first()
    {
        string dir = NewDirectory();
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            var account = new FakeAccount().SignedInAs("Sh");
            host.Account = account;
            ProfilesScreen screen = Open(shell);

            Find<Button>(screen, button => button.Text == "DELETE ACCOUNT")!.Activate();
            Pump(shell, 0.1f);
            Find<Button>(screen, button => button.Text == "CANCEL" && button.Tooltip == "Keep the account.")!.Activate();
            Pump(shell, 0.1f);
            Assert.Empty(account.Asked);

            Find<Button>(screen, button => button.Text == "DELETE ACCOUNT")!.Activate();
            Pump(shell, 0.1f);
            Find<Button>(screen, button => button.Text == "DELETE PERMANENTLY")!.Activate();
            Pump(shell, 0.1f);
            Assert.Equal(["delete"], account.Asked);
            Assert.NotNull(Find<Button>(screen, button => button.Id == "profile-sign-in"));
        }
        finally
        {
            Remove(dir);
        }
    }

    /// <summary>A build without an account says it plays offline, and offers no sign-in.</summary>
    [Fact]
    public void A_build_without_accounts_plays_offline()
    {
        string dir = NewDirectory();
        try
        {
            (Shell shell, _) = BuildShell(dir);
            ProfilesScreen screen = Open(shell);
            Assert.NotNull(Find<Label>(screen, label => label.Text == "This build plays offline. Your profile is kept on this computer."));
            Assert.Null(Find<Button>(screen, button => button.Id == "profile-sign-in"));
        }
        finally
        {
            Remove(dir);
        }
    }

    private static ProfilesScreen Open(Shell shell)
    {
        Assert.True(shell.OpenOn("profiles"));
        Pump(shell, 0.1f);
        return Assert.IsType<ProfilesScreen>(shell.Stack.Top);
    }

    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));

    private static void Remove(string dir)
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
