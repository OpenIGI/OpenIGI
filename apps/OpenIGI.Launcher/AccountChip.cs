using OpenIGI.Releases.Accounts;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher;

/// <summary>
/// The header's account: SIGN IN WITH DISCORD while signed out; while the browser is out on Discord, a word saying so with
/// OPEN AGAIN and CANCEL; once signed in, the player's initial and Discord name with SIGN OUT. The game uses the same
/// sign-in, so it never asks again.
/// </summary>
internal sealed class AccountChip : FlexPanel
{
    private readonly UiTheme _theme;
    private readonly AccountLink _account;
    private string _shown = "-";

    /// <summary>Initializes a new instance of the <see cref="AccountChip"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="account">The account.</param>
    public AccountChip(UiTheme theme, AccountLink account)
    {
        _theme = theme;
        _account = account;
        Orientation = Orientation.Horizontal;
        Gap = 12f;
        VerticalAlignment = VerticalAlignment.Center;
        Update();
    }

    /// <summary>Rebuilds the chip when what it shows changed; called every frame.</summary>
    /// <returns><see langword="true"/> when it was rebuilt, and the header needs laying out again.</returns>
    public bool Update()
    {
        string key = $"{_account.State}|{_account.Account?.Name}|{_account.IsBusy}|{_account.Error}";
        if (key == _shown)
        {
            return false;
        }

        _shown = key;
        ClearChildren();
        switch (_account.State)
        {
            case AccountLinkState.Checking:
                break;

            case AccountLinkState.Starting or AccountLinkState.Waiting:
                Add(Note("Finish signing in in your browser"));
                Add(Small("OPEN AGAIN", "Open the Discord page again.", _account.OpenLinkAgain));
                Add(Small("CANCEL", "Stop waiting; nothing is changed.", _account.CancelLink));
                break;

            case AccountLinkState.Linked or AccountLinkState.Offline when _account.Account is { } signedIn:
                Add(new Mark(_theme, signedIn.Name) { Online = _account.State == AccountLinkState.Linked });
                Add(new Label(_theme.BodyStrong, signedIn.Name, 16f, _theme.TextBright) { VerticalAlignment = VerticalAlignment.Center });
                Add(Small("SIGN OUT", "Sign this computer out of your account, the game included.", _account.Unlink));
                break;

            default:
                var signIn = new Button(_theme, "SIGN IN WITH DISCORD")
                {
                    Accent = ButtonAccent.Ghost,
                    LeadingGlyph = static (painter, centre, size, ink) => KitIcons.Draw(painter, KitIcon.People, centre, size, ink),
                    GlyphSize = 16f,
                    TextSize = 14f,
                    Tracking = 0.12f,
                    Padding = new Thickness(16f, 0f),
                    Height = 36f,
                    IsEnabled = !_account.IsBusy,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tooltip = _account.Error ?? "Sign in once here and the game is signed in too: your Discord name in multiplayer, your progress on every computer, your record.",
                };
                signIn.Activated += _ => _account.Link();
                Add(signIn);
                break;
        }

        return true;
    }

    private Label Note(string text) => new(_theme.Body, text, 14f, _theme.TextMuted) { VerticalAlignment = VerticalAlignment.Center };

    private Button Small(string text, string tooltip, Action act)
    {
        var button = new Button(_theme, text)
        {
            Accent = ButtonAccent.Text,
            TextSize = 13f,
            Tracking = 0.12f,
            Padding = new Thickness(8f, 0f),
            Height = 32f,
            IsEnabled = !_account.IsBusy,
            VerticalAlignment = VerticalAlignment.Center,
            Tooltip = tooltip,
        };
        button.Activated += _ => act();
        return button;
    }

    // The player's initial in a small square, in the accent while the servers can be reached.
    private sealed class Mark(UiTheme theme, string name) : UiElement
    {
        private readonly string _initial = name.Trim() is { Length: > 0 } trimmed ? trimmed[..1].ToUpperInvariant() : "?";

        public bool Online { get; init; }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(context.Px(32f), context.Px(32f));

        protected override void OnDraw(UiPainter painter)
        {
            var ink = Online ? theme.Accent : theme.TextMuted;
            painter.StrokeRect(Bounds, painter.Context.Px(1f), ink);
            float size = Bounds.Height * 0.55f;
            float width = theme.Display.MeasureWidth(_initial, size);
            painter.DrawText(theme.Display, _initial, new UiVec2(Bounds.Center.X - (width * 0.5f), UiRowStyle.CapsCenterY(theme.Display, size, Bounds)), size, ink);
        }
    }
}
