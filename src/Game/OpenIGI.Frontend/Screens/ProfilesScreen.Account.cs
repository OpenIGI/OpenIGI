using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <content>
/// The online account, optional and signed into with Discord: what signing in brings, the sign-in itself (the player's
/// browser opened on Discord, the page waiting on it with the means to open or copy it again), who the player is signed in
/// as, SIGN OUT, and DELETE ACCOUNT behind a question. Under it the player's online record, which counts matches on the
/// official servers.
/// </content>
public sealed partial class ProfilesScreen
{
    // What the account showed when the page was last built, so it is built again only when that changes.
    private string _shownAccount = string.Empty;

    // Whether DELETE ACCOUNT has been asked once and waits for its answer; the link last copied.
    private bool _confirmingDelete;
    private string? _copiedLink;

    private IAccountPanel? Accounts => Context.Host.Account;

    private string AccountKey => Accounts is { } accounts
        ? $"{accounts.Status}|{accounts.Account?.Id}|{accounts.Account?.Name}|{accounts.Problem}|{accounts.SignInUrl}|{accounts.IsBusy}|{_confirmingDelete}|{_copiedLink}"
        : string.Empty;

    // The account's panel: what it is for while signed out, the sign-in while it waits, and the account once signed in.
    private GlassPanel BuildAccount(UiTheme theme)
    {
        bool compact = Size == ShellSize.Compact;
        GlassPanel panel = DetailPanel(theme);
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = compact ? 8f : 12f };
        column.AddChild(new Eyebrow(theme, "Online account") { TextSize = compact ? 12f : 14f });

        if (Accounts is not { } accounts)
        {
            column.AddChild(Note(theme, "This build plays offline. Your profile is kept on this computer."));
            panel.AddChild(column);
            return panel;
        }

        AccountSummary? account = accounts.Account;
        switch (accounts.Status)
        {
            case AccountStatus.Checking:
                column.AddChild(Note(theme, "Checking your account…"));
                break;

            case AccountStatus.SignedOut:
                column.AddChild(Headline(theme, "Play as yourself"));
                column.AddChild(Note(theme, "Sign in with Discord to play multiplayer under your Discord name, keep your progress on every computer and in the browser, and build an online record of your matches."));
                column.AddChild(Actions(SignInButton(theme, accounts)));
                break;

            case AccountStatus.Starting:
                column.AddChild(Note(theme, "Opening Discord…"));
                break;

            case AccountStatus.WaitingForBrowser:
                column.AddChild(Headline(theme, "Finish signing in"));
                column.AddChild(Note(theme, accounts.CanOpenLinks
                    ? "Approve OpenIGI on the Discord page that opened in your browser. This page carries on once you have."
                    : "Open this address in your browser and approve OpenIGI there. This page carries on once you have."));
                if (!accounts.CanOpenLinks || _copiedLink is not null)
                {
                    column.AddChild(new Label(theme.Mono, accounts.SignInUrl ?? string.Empty, 13f, theme.Accent) { Wrap = true });
                }

                column.AddChild(Actions(
                    accounts.CanOpenLinks ? Ghost(theme, "OPEN AGAIN", "Open the Discord page again.", accounts.OpenSignInPage) : null,
                    Ghost(theme, _copiedLink is null ? "COPY LINK" : "COPIED", "Copy the sign-in address, to open it in another browser.", () => _copiedLink = accounts.CopySignInLink()),
                    Ghost(theme, "CANCEL", "Stop waiting; nothing is changed.", accounts.CancelSignIn)));
                break;

            case AccountStatus.SignedIn when account is not null:
                column.AddChild(Headline(theme, $"Signed in as {account.Name}"));
                column.AddChild(Note(theme, "Multiplayer shows your Discord name, and your matches on the official servers count toward your record."));
                column.AddChild(_confirmingDelete ? DeleteQuestion(theme, accounts) : Actions(
                    Ghost(theme, "SIGN OUT", "Play without the account on this computer again. Everything else stays.", accounts.SignOut),
                    Destructive(theme, "DELETE ACCOUNT", "Delete your OpenIGI account and everything our servers keep of it.", () => _confirmingDelete = true)));
                break;

            case AccountStatus.Offline:
                column.AddChild(Headline(theme, account is null ? "Offline" : $"{account.Name} · offline"));
                column.AddChild(Note(theme, "The OpenIGI servers can't be reached right now. Everything you do is kept here and goes up once they can."));
                break;

            default:
                column.AddChild(Actions(SignInButton(theme, accounts)));
                break;
        }

        if (accounts.Problem is { } error && accounts.Status != AccountStatus.Offline)
        {
            column.AddChild(new Label(theme.Body, error, 15f, theme.Danger) { Wrap = true });
        }

        panel.AddChild(column);
        return panel;
    }

    // The online record: the official servers' count of the player's matches, or what it takes to start one.
    private GlassPanel BuildRecord(UiTheme theme)
    {
        bool compact = Size == ShellSize.Compact;
        GlassPanel panel = DetailPanel(theme);
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = compact ? 8f : 12f };
        column.AddChild(new Eyebrow(theme, "Online record") { TextSize = compact ? 12f : 14f });

        var stats = new FlexPanel { Orientation = Orientation.Horizontal, Gap = compact ? 18f : 30f };
        float size = compact ? 18f : 24f;
        foreach (string caption in (string[])["Matches", "Wins", "Kills", "K/D", "Headshots"])
        {
            stats.Add(new Stat(theme, caption) { Value = "—", ValueSize = size, ValueColor = theme.TextMuted });
        }

        column.AddChild(stats);
        column.AddChild(Note(theme, Accounts?.Status == AccountStatus.SignedIn
            ? "Matches on the official servers are counted here from your next one on."
            : "Sign in and play on the official servers to start your record."));
        panel.AddChild(column);
        return panel;
    }

    // Under the name: who the player is to everyone else.
    private string IdentityNote() => Accounts switch
    {
        { Status: AccountStatus.SignedIn, Account: { } account } => $"Signed in with Discord as {account.Name}",
        { Status: AccountStatus.Offline, Account: { } account } => $"{account.Name} · offline",
        null => "Playing on this computer",
        _ => "Playing without an account",
    };

    private StackPanel DeleteQuestion(UiTheme theme, IAccountPanel accounts)
    {
        var question = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f };
        question.AddChild(new Label(theme.Body, "Delete your account? Your online record and the copy of your profile on our servers are removed. What is on this computer stays.", 16f, theme.Destructive) { Wrap = true });
        question.AddChild(Actions(
            Destructive(theme, "DELETE PERMANENTLY", "Delete the account now.", () =>
            {
                _confirmingDelete = false;
                accounts.DeleteAccount();
            }),
            Ghost(theme, "CANCEL", "Keep the account.", () => _confirmingDelete = false)));
        return question;
    }

    private Button SignInButton(UiTheme theme, IAccountPanel accounts)
    {
        var button = new Button(theme, "SIGN IN WITH DISCORD")
        {
            Id = "profile-sign-in",
            Accent = ButtonAccent.Solid,
            CutCorner = 10f,
            LeadingGlyph = static (painter, centre, size, ink) => KitIcons.Draw(painter, KitIcon.People, centre, size, ink),
            GlyphSize = 18f,
            TextSize = 17f,
            Tracking = 0.12f,
            Padding = new Thickness(24f, 0f),
            Height = 48f,
            IsEnabled = !accounts.IsBusy,
            Tooltip = "Opens Discord in your browser to approve OpenIGI. We see your Discord name and avatar, nothing else.",
        };
        button.Activated += _ =>
        {
            _copiedLink = null;
            accounts.SignIn();
            Invalidate();
        };
        return button;
    }

    private Button Ghost(UiTheme theme, string text, string tooltip, Action act) => Act(new Button(theme, text) { Accent = ButtonAccent.Ghost, Tooltip = tooltip }, act);

    private Button Destructive(UiTheme theme, string text, string tooltip, Action act) => Act(new Button(theme, text) { Accent = ButtonAccent.Destructive, Tooltip = tooltip }, act);

    private Button Act(Button button, Action act)
    {
        button.TextSize = 15f;
        button.Tracking = 0.12f;
        button.Padding = new Thickness(16f, 0f);
        button.Height = 40f;
        button.IsEnabled = Accounts?.IsBusy != true;
        button.Activated += _ =>
        {
            act();
            Invalidate();
        };
        return button;
    }

    private static FlexPanel Actions(params Button?[] buttons)
    {
        var row = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f };
        foreach (Button? button in buttons)
        {
            if (button is not null)
            {
                row.Add(button);
            }
        }

        return row;
    }

    private Label Headline(UiTheme theme, string text) =>
        new(theme.Heading, text.ToUpperInvariant(), Size == ShellSize.Compact ? 18f : 22f, theme.TextBright) { Wrap = true };

    private Label Note(UiTheme theme, string text) =>
        new(theme.Body, text, Size == ShellSize.Compact ? 14f : 16f, theme.TextSecondary) { Wrap = true };
}
