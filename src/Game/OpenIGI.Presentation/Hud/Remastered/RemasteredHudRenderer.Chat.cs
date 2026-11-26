using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <content>
/// <para>
/// The chat, as I.G.I. 2 keeps it (network/client.c <c>Client_Talk</c>, <c>Client_GameMessageChatHandler</c>): each line the
/// speaker's name and what was said, team talk in the accent where talk to all is plain, the server's
/// own word in the title's bright ink; and the line being typed after its <c>SAY ALL:</c> or <c>SAY TEAM:</c>, in the same inks.
/// </para>
/// <para>
/// Shut, the last few lines stand at the lower left on a wash of the title's glass and go at the end of their time. Opened, the
/// column stands on a sheet of the title's glass with a CHAT strip over its history, the line being typed on a rule under it,
/// and the keys that work as the title's key caps. On a touch screen the column stands above the keyboard and shows no keys.
/// </para>
/// </content>
public sealed partial class RemasteredHudRenderer
{
    // How wide the chat runs, and how many rows it shows open. Layout pixels.
    private const float ChatWidthUnits = 560f;
    private const int ChatOpenRows = 9;

    // The chat column: the history over the line being typed, or the recent lines bare.
    private static void DrawChat(HudOverlay o, IReadOnlyList<HudChatLine> chat, HudChatEntry? entry, float bottom, float left)
    {
        float size = o.Px(HudOverlay.Body);
        float rowHeight = o.Text.LineHeightAt(size) + o.Px(6f);
        float width = Math.Min(o.Px(ChatWidthUnits), o.Safe.Width - o.Px(80f));

        // Above a touch screen's keyboard, when one is up.
        if (entry is { KeyboardInset: > 0f } raised)
        {
            bottom = Math.Min(bottom, o.Context.Viewport.Y - raised.KeyboardInset - o.Px(14f));
        }

        if (entry is null)
        {
            int rows = chat.Count * 3;
            DrawChatRows(o, chat, chat.Count - 1, new UiRect(left, bottom - (rowHeight * rows), width, rowHeight * rows), rowHeight, rows, washed: true, out _);
            return;
        }

        // The column, bottom up: the keys, the line typed on its rule, the history, the strip.
        float pad = o.Px(14f);
        float small = o.Px(HudOverlay.Small);
        float keysHeight = entry.Touch ? 0f : o.KeyCapHeight + o.Px(10f);
        float fieldHeight = o.Text.LineHeightAt(size) + o.Px(16f);
        float historyHeight = rowHeight * ChatOpenRows;
        float stripHeight = o.Text.LineHeightAt(small) + o.Px(14f);
        float height = stripHeight + o.Px(8f) + historyHeight + o.Px(8f) + fieldHeight + keysHeight + pad;
        var sheet = new UiRect(left - pad, bottom - height, width + (pad * 2f), height);
        o.Sheet(sheet);
        var strip = new UiRect(sheet.X, sheet.Y, sheet.Width, stripHeight);
        o.HeaderStrip(strip);
        float stripY = strip.Y + ((strip.Height - o.Text.LineHeightAt(small)) * 0.5f);
        o.Text.DrawAt("CHAT", left, stripY, small, o.Ink.Highlight);

        // How much more of the history there is either way, at the strip's right.
        var history = new UiRect(left, strip.Bottom + o.Px(8f), width, historyHeight);
        int newest = Math.Clamp(chat.Count - 1 - entry.Scroll, -1, chat.Count - 1);
        o.Painter.PushClip(history);
        DrawChatRows(o, chat, newest, history, rowHeight, ChatOpenRows, washed: false, out int oldest);
        o.Painter.PopClip();
        int newer = Math.Min(entry.Scroll, chat.Count);
        string? more = (oldest > 0, newer > 0) switch
        {
            (true, true) => string.Create(CultureInfo.InvariantCulture, $"{oldest} OLDER - {newer} NEWER"),
            (true, false) => string.Create(CultureInfo.InvariantCulture, $"{oldest} OLDER"),
            (false, true) => string.Create(CultureInfo.InvariantCulture, $"{newer} NEWER"),
            _ => null,
        };
        if (more is not null)
        {
            o.Text.DrawRightAt(more, history.Right, stripY, small, o.Quiet);
        }

        if (chat.Count == 0)
        {
            o.Text.DrawAt("NO MESSAGES YET", left, history.Bottom - rowHeight, size, HudOverlay.Fade(o.Quiet, 0.7f));
        }

        var field = new UiRect(left, history.Bottom + o.Px(8f), width, fieldHeight);
        DrawChatField(o, entry, field, size);
        if (!entry.Touch)
        {
            DrawChatKeys(o, entry, new UiVec2(left, field.Bottom + o.Px(6f)));
        }
    }

    // The rows of the lines from `newest` back, bottom up, as many as fit in `rows`; `oldest` is how many lines did not fit.
    private static void DrawChatRows(HudOverlay o, IReadOnlyList<HudChatLine> chat, int newest, UiRect area, float rowHeight, int rows, bool washed, out int oldest)
    {
        float size = o.Px(HudOverlay.Body);
        float y = area.Bottom;
        int used = 0;
        int i = newest;
        for (; i >= 0 && used < rows; i--)
        {
            HudChatLine line = chat[i];
            bool server = line.Kind == ChatKind.Server;
            Bgra32 side = server ? o.Ink.Highlight : line.Enemy ? o.Ink.Enemy : o.Ink.Ally;
            Bgra32 said = server ? o.Ink.Highlight : line.Kind == ChatKind.Team ? o.Ink.Accent : o.Ink.Plain;

            // Who said it, as the original's line begins: the server's word, or the name, marked when said to the team or dead.
            string marks = (line.Kind, line.Dead) switch
            {
                (ChatKind.Team, true) => "(TEAM) *DEAD* ",
                (ChatKind.Team, false) => "(TEAM) ",
                (_, true) => "*DEAD* ",
                _ => string.Empty,
            };
            string who = server ? "SERVER: " : line.Name.Length > 0 ? OverlayText.Prose(line.Name) + ": " : string.Empty;
            float marksWidth = o.Text.MeasureAt(marks, size);
            float whoWidth = o.Text.MeasureAt(who, size);
            string text = OverlayText.Prose(line.Text);
            List<string> wrapped = o.Text.WrapAt(text, size, area.Width - marksWidth - whoWidth, area.Width);
            if (used + wrapped.Count > rows && used > 0)
            {
                break;
            }

            used += wrapped.Count;
            y -= rowHeight * wrapped.Count;
            float alpha = Math.Clamp(line.Opacity, 0f, 1f);
            float top = y + ((rowHeight - o.Text.LineHeightAt(size)) * 0.5f);

            // Shut, each line on a wash of its own, the speaker's side's tick at the left.
            if (washed)
            {
                float longest = wrapped.Max(r => o.Text.MeasureProseAt(r, size)) + (wrapped.Count == 1 ? marksWidth + whoWidth : 0f);
                o.Wash(new UiRect(area.X - o.Px(10f), y, longest + o.Px(70f), rowHeight * wrapped.Count), side, alpha);
            }

            float x = area.X;
            if (marks.Length > 0)
            {
                x += o.Text.DrawAt(marks, x, top, size, HudOverlay.Fade(o.Quiet, alpha), shadow: washed);
            }

            if (who.Length > 0)
            {
                x += o.Text.DrawAt(who, x, top, size, HudOverlay.Fade(side, alpha), shadow: washed);
            }

            for (int row = 0; row < wrapped.Count; row++)
            {
                o.Text.DrawProseAt(wrapped[row], row == 0 ? x : area.X, top + (row * rowHeight), size, HudOverlay.Fade(said, alpha), shadow: washed);
            }
        }

        oldest = i + 1;
    }

    // The line being typed, after the original's prompt -- SAY ALL: in the plain ink, SAY TEAM: in the accent -- on a rule, its
    // caret blinking after it, and the count once it nears the most a line may hold.
    private static void DrawChatField(HudOverlay o, HudChatEntry entry, UiRect field, float size)
    {
        Bgra32 ink = entry.Team ? o.Ink.Accent : o.Ink.Plain;
        string prompt = entry.Team ? "SAY TEAM: " : "SAY ALL: ";
        float textTop = field.Y + o.Px(4f);
        float promptWidth = o.Text.DrawAt(prompt, field.X, textTop, size, ink);
        float small = o.Px(HudOverlay.Small);
        if (entry.Text.Length >= SessionTalk.MaxChatLength - 30)
        {
            string count = string.Create(CultureInfo.InvariantCulture, $"{entry.Text.Length}/{SessionTalk.MaxChatLength}");
            o.Text.DrawRightAt(count, field.Right, textTop + o.Text.LineHeightAt(size) - o.Text.LineHeightAt(small), small, entry.Text.Length >= SessionTalk.MaxChatLength ? o.Ink.Danger : o.Quiet);
        }

        // Scrolled so the caret stays on the rule, with a little room after it.
        var text = new UiRect(field.X + promptWidth, field.Y, field.Width - promptWidth - o.Px(60f), field.Height);
        string typed = OverlayText.Prose(entry.Text);
        float toCaret = o.Text.MeasureAt(typed[..Math.Min(entry.Caret, typed.Length)], size);
        float shift = Math.Max(0f, toCaret - (text.Width - o.Px(16f)));
        o.Painter.PushClip(text);
        if (entry.Text.Length == 0)
        {
            o.Text.DrawAt(entry.Team ? "TO YOUR TEAM" : "TO EVERYONE", text.X, textTop, size, HudOverlay.Fade(o.Quiet, 0.6f));
        }

        o.Text.DrawAt(typed, text.X - shift, textTop, size, ink);
        if (entry.CaretOn)
        {
            o.Painter.FillRect(new UiRect(text.X - shift + toCaret + o.Px(1f), textTop, Math.Max(1f, o.Px(2f)), o.Text.LineHeightAt(size)), o.Ink.Highlight);
        }

        o.Painter.PopClip();
        o.Rule(field.X, field.Bottom - o.Px(4f), field.Width, HudOverlay.Fade(ink, 0.7f));
    }

    // The keys that work while typing: a cap each, then what it does.
    private static void DrawChatKeys(HudOverlay o, HudChatEntry entry, UiVec2 at)
    {
        ReadOnlySpan<(string Key, string Does)> keys =
            [("ENTER", "SEND"), ("TAB", entry.Team ? "TO ALL" : "TO TEAM"), ("UP", "LAST SENT"), ("PG UP", "SCROLL"), ("ESC", "CLOSE")];
        float small = o.Px(HudOverlay.Small);
        float x = at.X;
        foreach ((string key, string does) in keys)
        {
            UiRect cap = o.KeyCap(key, x, at.Y);
            x = cap.Right + o.Px(6f);
            x += o.Text.DrawAt(does, x, cap.Y + ((cap.Height - o.Text.LineHeightAt(small)) * 0.5f), small, o.Quiet) + o.Px(16f);
        }
    }
}
