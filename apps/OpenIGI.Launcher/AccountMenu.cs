using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher;

/// <summary>
/// The player's account, at the header's right: who is signed in (their Discord avatar and name, and whether the servers
/// can be reached), or SIGN IN. Pressed, it drops a panel: signed in, the season's record, the way to the record and the
/// leaderboard on the site, and SIGN OUT (pressed twice, as it signs the game out too); signed out, what signing in brings
/// and CONTINUE WITH DISCORD; while the browser is out on Discord, OPEN AGAIN and CANCEL. A press outside, or Escape, shuts it.
/// </summary>
internal sealed class AccountMenu : Control
{
    private const float PanelWidth = 380f;
    private const float RowHeight = 54f;
    private const float PanelPadding = 24f;
    private static readonly TimeSpan ConfirmFor = TimeSpan.FromSeconds(4);

    private readonly LauncherModel _model;
    private readonly Func<string, UiPicture?> _pictures;
    private readonly Tween _open = new(0f, duration: 0.18f, Ease.OutCubic);
    private readonly List<(UiRect Box, MenuRow Row)> _rowBoxes = [];
    private UiRect _panel = UiRect.Empty;
    private int _highlighted = -1;
    private DateTimeOffset? _confirmingUntil;

    /// <summary>Initializes a new instance of the <see cref="AccountMenu"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="model">The launcher's model, whose account it shows.</param>
    /// <param name="pictures">Loads a picture file, for the avatar.</param>
    public AccountMenu(UiTheme theme, LauncherModel model, Func<string, UiPicture?> pictures)
        : base(theme)
    {
        _model = model;
        _pictures = pictures;
        VerticalAlignment = VerticalAlignment.Center;
        Tooltip = string.Empty;
    }

    private enum RowKind
    {
        Action,
        Primary,
        Destructive,
    }

    /// <summary>Gets whether the panel is open.</summary>
    public bool IsOpen { get; private set; }

    /// <inheritdoc/>
    public override UiRect PointerBounds => IsOpen && !_panel.IsEmpty ? Union(base.PointerBounds, _panel) : base.PointerBounds;

    private AccountLink Account => _model.Account;

    private PlayerAccount? SignedIn => Account.IsSignedIn ? Account.Account : null;

    /// <summary>Shuts the panel.</summary>
    public void Close()
    {
        IsOpen = false;
        _confirmingUntil = null;
        _open.Retarget(0f);
    }

    /// <inheritdoc/>
    public override void ActivateAt(UiVec2 point)
    {
        if (!IsOpen)
        {
            Open();
            return;
        }

        int index = RowAt(point);
        if (index >= 0)
        {
            Run(_rowBoxes[index].Row);
            return;
        }

        // The trigger again, or anywhere outside the rows: shut, as a menu does.
        if (!_panel.Contains(point))
        {
            Close();
        }
    }

    /// <inheritdoc/>
    public override void PointerMoved(UiVec2 point) => _highlighted = RowAt(point);

    /// <inheritdoc/>
    public override bool HandleNavigate(NavDirection direction)
    {
        if (!IsOpen || _rowBoxes.Count == 0)
        {
            return false;
        }

        _highlighted = direction switch
        {
            NavDirection.Up => (_highlighted - 1 + _rowBoxes.Count) % _rowBoxes.Count,
            NavDirection.Down => (_highlighted + 1) % _rowBoxes.Count,
            _ => _highlighted,
        };
        return true;
    }

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        if (!IsOpen)
        {
            Open();
        }
        else if (_highlighted >= 0 && _highlighted < _rowBoxes.Count)
        {
            Run(_rowBoxes[_highlighted].Row);
        }
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        _open.Update(elapsedSeconds);
        if (_confirmingUntil is { } until && DateTimeOffset.UtcNow > until)
        {
            _confirmingUntil = null;
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float height = context.Px(52f);
        if (SignedIn is { } account)
        {
            float text = MathF.Max(Theme.BodyStrong.MeasureWidth(Fit(account.Name), context.Px(18f)), context.Px(14f) + UiPainter.MeasureLabel(Theme.Mono, StatusLine(), context.Px(12f), 0.18f));
            return new UiVec2(context.Px(8f + 38f + 14f) + text + context.Px(18f + 10f + 10f), height);
        }

        string caption = TriggerCaption();
        return new UiVec2(context.Px(18f + 20f + 12f) + UiPainter.MeasureLabel(Theme.Heading, caption, context.Px(17f), 0.14f) + context.Px(20f), height);
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        // The panel hangs under the trigger, its right edge on the trigger's, so it never runs off the window.
        float width = context.Px(PanelWidth);
        float x = Bounds.Right - width;
        float y = Bounds.Bottom + context.Px(12f);
        float height = context.Px(PanelPadding) * 2f + HeaderHeight(context) + (Rows().Count * context.Px(RowHeight)) + context.Px(12f);
        _panel = new UiRect(x, y, width, height);
        LayRows(context);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        bool hot = IsPointerOver || IsOpen || Visual.Focus > 0.01f;
        if (hot)
        {
            painter.FillRect(Bounds, UiPalette.White(IsOpen ? 0.08f : 0.05f * appear));
        }

        painter.StrokeRect(Bounds, context.Px(1f), UiPalette.Fade(IsOpen ? Theme.Accent : UiPalette.White(hot ? 0.22f : 0.12f), appear));

        if (SignedIn is { } account)
        {
            var avatar = new UiRect(Bounds.X + context.Px(7f), Bounds.Center.Y - context.Px(19f), context.Px(38f), context.Px(38f));
            DrawAvatar(painter, avatar, account, appear);
            float textX = avatar.Right + context.Px(14f);
            float nameSize = context.Px(18f);
            float statusSize = context.Px(12f);
            float block = Theme.BodyStrong.LineHeight(nameSize) + Theme.Mono.LineHeight(statusSize);
            float top = Bounds.Center.Y - (block * 0.5f);
            painter.DrawText(Theme.BodyStrong, Fit(account.Name), new UiVec2(textX, top), nameSize, UiPalette.Fade(Theme.TextBright, appear));
            float statusY = top + Theme.BodyStrong.LineHeight(nameSize);
            Bgra32 dot = Account.State == AccountLinkState.Linked ? Theme.Success : Theme.Warning;
            painter.FillDiamond(new UiVec2(textX + context.Px(4f), statusY + (Theme.Mono.LineHeight(statusSize) * 0.5f)), context.Px(4f), UiPalette.Fade(dot, appear));
            painter.DrawLabel(Theme.Mono, StatusLine(), new UiVec2(textX + context.Px(14f), statusY), statusSize, UiPalette.Fade(Theme.TextMuted, appear), 0.18f);
            DrawCaret(painter, new UiVec2(Bounds.Right - context.Px(19f), Bounds.Center.Y), appear);
            return;
        }

        bool waiting = Account.State is AccountLinkState.Starting or AccountLinkState.Waiting;
        Bgra32 ink = UiPalette.Fade(waiting ? Theme.TextSecondary : Theme.Accent, appear);
        KitIcons.Draw(painter, KitIcon.People, new UiVec2(Bounds.X + context.Px(28f), Bounds.Center.Y), context.Px(20f), ink);
        painter.DrawLabel(Theme.Heading, TriggerCaption(), new UiVec2(Bounds.X + context.Px(50f), UiRowStyle.CapsCenterY(Theme.Heading, context.Px(17f), Bounds)), context.Px(17f), ink, 0.14f);
    }

    /// <inheritdoc/>
    protected override void OnDrawOverlay(UiPainter painter)
    {
        float amount = _open.Value;
        if (amount <= 0.002f || _panel.IsEmpty)
        {
            return;
        }

        LayoutContext context = painter.Context;
        float drop = context.Px(6f) * (1f - amount);
        var box = new UiRect(_panel.X, _panel.Y - drop, _panel.Width, _panel.Height);
        painter.PushOpacity(amount);

        // The page under the header steps back while the panel is open, so the panel reads as above it rather than as one
        // more card beside the news; the header (and the button that opened it) stays as it was.
        painter.FillRect(new UiRect(0f, _panel.Y, context.Viewport.X, context.Viewport.Y - _panel.Y), UiPalette.InkAt(0.55f));

        // A soft shadow under the panel, widening and fading.
        for (int i = 1; i <= 6; i++)
        {
            float spread = context.Px(4f * i);
            painter.FillRect(new UiRect(box.X - spread, box.Y + (spread * 0.5f), box.Width + (spread * 2f), box.Height + spread), Bgra32.FromRgba(0, 0, 0, (byte)(34 - (i * 4))));
        }

        // Solid: nothing behind shows through.
        painter.FillRect(box, Bgra32.FromRgb(15, 17, 18));
        painter.StrokeRect(box, context.Px(1f), UiPalette.White(0.14f));
        painter.FillRect(new UiRect(box.X, box.Y, box.Width, context.Px(2f)), Theme.Accent);

        float pad = context.Px(PanelPadding);
        var header = new UiRect(box.X + pad, box.Y + pad, box.Width - (pad * 2f), HeaderHeight(context));
        DrawHeader(painter, header);

        for (int i = 0; i < _rowBoxes.Count; i++)
        {
            (UiRect rowBox, MenuRow row) = _rowBoxes[i];
            var at = new UiRect(rowBox.X, rowBox.Y - drop, rowBox.Width, rowBox.Height);
            DrawRow(painter, at, row, i == _highlighted);
        }

        painter.PopOpacity();
    }

    // The panel's top: who is signed in and how they stand, or what signing in brings, or what the browser is waiting on.
    private float HeaderHeight(LayoutContext context) => SignedIn is not null
        ? context.Px(_model.Record switch { null => 72f, { SeasonTotals.Matches: 0 } => 72f + 62f, _ => 72f + 96f })
        : context.Px(Account.State is AccountLinkState.Starting or AccountLinkState.Waiting ? 112f : Account.Error is null ? 124f : 150f);

    // What signing in brings, a line each.
    private static readonly string[] SignInPoints =
    [
        "Cloud sync for progress, difficulty and controls",
        "Your Discord name in multiplayer",
        "Stats and a place on the leaderboard",
    ];

    private void DrawHeader(UiPainter painter, UiRect header)
    {
        LayoutContext context = painter.Context;
        if (SignedIn is { } account)
        {
            var avatar = new UiRect(header.X, header.Y, context.Px(64f), context.Px(64f));
            DrawAvatar(painter, avatar, account, 1f);
            float textX = avatar.Right + context.Px(16f);
            painter.DrawLabel(Theme.Mono, "SIGNED IN WITH DISCORD", new UiVec2(textX, header.Y + context.Px(2f)), context.Px(11f), Theme.Accent, 0.2f);
            painter.DrawText(Theme.Display, Fit(account.Name), new UiVec2(textX, header.Y + context.Px(18f)), context.Px(30f), Theme.TextBright);
            painter.DrawText(
                Theme.Body,
                Account.State == AccountLinkState.Linked ? "Cloud sync on · launcher and game" : "Offline: the servers can't be reached",
                new UiVec2(textX, header.Y + context.Px(50f)),
                context.Px(14f),
                Account.State == AccountLinkState.Linked ? Theme.TextMuted : Theme.Warning);

            if (_model.Record is { } record)
            {
                float y = header.Y + context.Px(84f);
                painter.FillRect(new UiRect(header.X, y, header.Width, context.Px(1f)), UiPalette.White(0.08f));
                painter.DrawLabel(Theme.Mono, $"{(record.Title == "igi2" ? "I.G.I. 2" : "PROJECT I.G.I.")} · SEASON · {SeasonName(record.Season)}".ToUpperInvariant(), new UiVec2(header.X, y + context.Px(14f)), context.Px(11f), Theme.TextMuted, 0.2f);
                StatTotals season = record.SeasonTotals;
                if (season.Matches == 0)
                {
                    DrawWrapped(painter, "No matches yet this season.", new UiVec2(header.X, y + context.Px(38f)), header.Width);
                    return;
                }

                (string Caption, string Value)[] figures =
                [
                    ("MATCHES", Number(season.Matches)),
                    ("WINS", Number(season.Wins)),
                    ("KILLS", Number(season.Kills)),
                    ("ASSISTS", Number(season.Assists)),
                    ("K/D", season.Kd.ToString("0.00", CultureInfo.InvariantCulture)),
                ];
                float column = header.Width / figures.Length;
                for (int i = 0; i < figures.Length; i++)
                {
                    float x = header.X + (column * i);
                    painter.DrawText(Theme.Display, figures[i].Value, new UiVec2(x, y + context.Px(34f)), context.Px(28f), Theme.TextBright);
                    painter.DrawLabel(Theme.Mono, figures[i].Caption, new UiVec2(x, y + context.Px(70f)), context.Px(10f), Theme.TextMuted, 0.2f);
                }
            }

            return;
        }

        if (Account.State is AccountLinkState.Starting or AccountLinkState.Waiting)
        {
            painter.DrawLabel(Theme.Mono, "SIGNING IN", new UiVec2(header.X, header.Y), context.Px(11f), Theme.Accent, 0.2f);
            painter.DrawText(Theme.Display, "Finish in your browser", new UiVec2(header.X, header.Y + context.Px(18f)), context.Px(30f), Theme.TextBright);
            DrawWrapped(painter, "Approve OpenIGI on the Discord page that opened. This carries on by itself once you have.", new UiVec2(header.X, header.Y + context.Px(58f)), header.Width);
            return;
        }

        painter.DrawText(Theme.Display, "Sign in with Discord", new UiVec2(header.X, header.Y), context.Px(28f), Theme.TextBright);
        float line = header.Y + context.Px(46f);
        foreach (string point in SignInPoints)
        {
            KitIcons.Draw(painter, KitIcon.Check, new UiVec2(header.X + context.Px(8f), line + context.Px(10f)), context.Px(14f), Theme.Accent);
            painter.DrawText(Theme.Body, point, new UiVec2(header.X + context.Px(26f), line), context.Px(15f), Theme.Text);
            line += context.Px(26f);
        }

        if (Account.Error is { } error)
        {
            DrawWrapped(painter, error, new UiVec2(header.X, line + context.Px(4f)), header.Width, Theme.Danger);
        }
    }

    private void DrawWrapped(UiPainter painter, string text, UiVec2 at, float width, Bgra32? colour = null)
    {
        float size = painter.Context.Px(15f);
        string wrapped = Theme.Body.WrapText(text, size, width);
        painter.DrawText(Theme.Body, wrapped, at, size, colour ?? Theme.TextMuted);
    }

    private void DrawRow(UiPainter painter, UiRect row, MenuRow item, bool highlighted)
    {
        LayoutContext context = painter.Context;
        Bgra32 ink = item.Kind switch
        {
            RowKind.Destructive => Theme.Destructive,
            RowKind.Primary => Theme.OnAccent,
            _ => highlighted ? Theme.TextBright : Theme.Text,
        };

        if (item.Kind == RowKind.Primary)
        {
            painter.FillRect(row, highlighted ? Theme.AccentHover : Theme.Accent);
        }
        else if (highlighted)
        {
            painter.FillRect(row, item.Kind == RowKind.Destructive ? UiPalette.DestructiveWash : UiPalette.White(0.06f));
        }

        float x = row.X + context.Px(14f);
        if (item.Icon != KitIcon.None)
        {
            KitIcons.Draw(painter, item.Icon, new UiVec2(x + context.Px(9f), row.Center.Y), context.Px(18f), ink);
            x += context.Px(34f);
        }

        painter.DrawLabel(Theme.Heading, item.Label, new UiVec2(x, UiRowStyle.CapsCenterY(Theme.Heading, context.Px(17f), row)), context.Px(17f), ink, 0.12f);
        if (item.Detail is { Length: > 0 } detail)
        {
            float size = context.Px(11f);
            float width = UiPainter.MeasureLabel(Theme.Mono, detail, size, 0.18f);
            painter.DrawLabel(Theme.Mono, detail, new UiVec2(row.Right - context.Px(14f) - width, UiRowStyle.CenterY(Theme.Mono, size, row)), size, item.Kind == RowKind.Primary ? Theme.OnAccent : Theme.TextDim, 0.18f);
        }
    }

    private void DrawAvatar(UiPainter painter, UiRect box, PlayerAccount account, float appear)
    {
        UiPicture? avatar = _model.AvatarFile is { } file ? _pictures(file) : null;
        KitDraw.PlayerMark(painter, Theme, box, account.Name, avatar, signedIn: Account.State == AccountLinkState.Linked, appear);
    }

    private void DrawCaret(UiPainter painter, UiVec2 centre, float appear)
    {
        float s = painter.Context.Px(5f) * (IsOpen ? -1f : 1f);
        Span<UiVec2> caret = [new(centre.X - painter.Context.Px(5f), centre.Y - (s * 0.5f)), new(centre.X + painter.Context.Px(5f), centre.Y - (s * 0.5f)), new(centre.X, centre.Y + (s * 0.5f))];
        painter.FillPolygon(caret, UiPalette.Fade(IsOpen ? Theme.Accent : Theme.TextMuted, appear));
    }

    // What the panel offers, for where the account stands.
    private List<MenuRow> Rows()
    {
        if (SignedIn is not null)
        {
            bool confirming = _confirmingUntil is not null;
            return
            [
                new(KitIcon.ArrowRight, "MY RECORD", "OPENIGI.COM", RowKind.Action, _model.OpenRecord, Closes: true),
                new(KitIcon.ArrowRight, "LEADERBOARD", "OPENIGI.COM", RowKind.Action, _model.OpenLeaderboard, Closes: true),
                new(KitIcon.Close, confirming ? "PRESS AGAIN TO SIGN OUT" : "SIGN OUT", confirming ? "LAUNCHER AND GAME" : string.Empty, RowKind.Destructive, SignOut),
            ];
        }

        if (Account.State is AccountLinkState.Starting or AccountLinkState.Waiting)
        {
            return
            [
                new(KitIcon.ArrowRight, "OPEN THE PAGE AGAIN", string.Empty, RowKind.Action, Account.OpenLinkAgain),
                new(KitIcon.Close, "CANCEL", string.Empty, RowKind.Action, Account.CancelLink),
            ];
        }

        return [new(KitIcon.People, "SIGN IN WITH DISCORD", string.Empty, RowKind.Primary, Account.Link)];
    }

    private void SignOut()
    {
        if (_confirmingUntil is null)
        {
            _confirmingUntil = DateTimeOffset.UtcNow + ConfirmFor;
            return;
        }

        _confirmingUntil = null;
        Account.Unlink();
        Close();
    }

    private void Run(MenuRow row)
    {
        row.Act();
        if (row.Closes)
        {
            Close();
        }
    }

    private void LayRows(LayoutContext context)
    {
        _rowBoxes.Clear();
        float pad = context.Px(PanelPadding);
        float y = _panel.Y + pad + HeaderHeight(context) + context.Px(12f);
        foreach (MenuRow row in Rows())
        {
            _rowBoxes.Add((new UiRect(_panel.X + pad - context.Px(6f), y, _panel.Width - (pad * 2f) + context.Px(12f), context.Px(RowHeight) - context.Px(4f)), row));
            y += context.Px(RowHeight);
        }

        _highlighted = Math.Min(_highlighted, _rowBoxes.Count - 1);
    }

    private int RowAt(UiVec2 point)
    {
        for (int i = 0; i < _rowBoxes.Count; i++)
        {
            if (_rowBoxes[i].Box.Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    private void Open()
    {
        IsOpen = true;
        _confirmingUntil = null;
        _highlighted = -1;
        _open.Retarget(1f);
    }

    private string TriggerCaption() => Account.State switch
    {
        AccountLinkState.Starting or AccountLinkState.Waiting => "SIGNING IN…",
        _ => "SIGN IN",
    };

    private string StatusLine() => Account.State == AccountLinkState.Linked ? "ONLINE" : "OFFLINE";

    private static string Fit(string name) => name.Length > 20 ? name[..19] + "…" : name;

    private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string SeasonName(string season) =>
        DateTime.TryParseExact(season, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime month)
            ? month.ToString("MMMM yyyy", CultureInfo.InvariantCulture)
            : season;

    private static UiRect Union(UiRect a, UiRect b) =>
        UiRect.FromEdges(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Max(a.Right, b.Right), MathF.Max(a.Bottom, b.Bottom));

    private sealed record MenuRow(KitIcon Icon, string Label, string Detail, RowKind Kind, Action Act, bool Closes = false);
}
