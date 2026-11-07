using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Igi1.World;

/// <content>
/// The chat, in the readouts' own hand: tracked mono capitals for what a thing is, the body face for what was said, the accent
/// for this player's side and the warning red for the other's, no boxes. Shut, the last few lines stand bare at the lower left,
/// each on a wash that fades out to the right, and go at the end of their time. Opened, the column takes the objective's
/// treatment -- an accent bar down its leading edge over a wash -- with a CHAT label over the history, and under it the line
/// being typed on an accent rule like the weapon's name, whom it goes to beside a diamond like the weapon's state, and the keys
/// that work as key caps. On a touch screen the column stands above the keyboard and shows no keys.
/// </content>
internal sealed partial class RemasteredHudRenderer
{
    // How wide the chat runs, its text size, and how many rows it shows open. Layout pixels.
    private const float ChatWidthUnits = 520f;
    private const float ChatTextUnits = 16f;
    private const int ChatOpenRows = 9;

    // The chat column: the history over the line being typed, or the recent lines bare.
    private void DrawChat(UiPainter painter, LayoutContext context, IReadOnlyList<HudChatLine> chat, HudChatEntry? entry, float bottom, float left)
    {
        if (_theme is not { } type)
        {
            return;
        }

        float Px(float units) => context.Px(units);
        float size = Px(ChatTextUnits);
        float rowHeight = MathF.Round(size * 1.55f);
        float width = Math.Min(Px(ChatWidthUnits), context.SafeRect.Width - Px(80f));

        // Above a touch screen's keyboard, when one is up.
        if (entry is { KeyboardInset: > 0f } raised)
        {
            bottom = Math.Min(bottom, context.Viewport.Y - raised.KeyboardInset - Px(14f));
        }

        if (entry is null)
        {
            int rows = chat.Count * 3;
            DrawChatRows(painter, type, chat, chat.Count - 1, new UiRect(left, bottom - (rowHeight * rows), width, rowHeight * rows), size, rowHeight, rows, washed: true, out _);
            return;
        }

        // The column, bottom up: the keys, the line typed on its rule, whom it goes to, the history, the label.
        float labelSize = Px(11f);
        float textX = left + Px(18f);
        float inner = width - Px(18f);
        float keysHeight = entry.Touch ? 0f : Px(30f);
        float lineSize = Px(20f);
        float fieldHeight = Px(18f) + lineSize + Px(12f);
        float historyHeight = rowHeight * ChatOpenRows;
        float headHeight = type.Mono.LineHeight(labelSize) + Px(12f);
        float top = bottom - keysHeight - fieldHeight - Px(14f) - historyHeight - headHeight;

        var block = new UiRect(left, top - Px(10f), width + Px(60f), bottom - top + Px(18f));
        Span<UiGradientStop> wash = [new(0f, UiPalette.InkAt(0.66f)), new(0.75f, UiPalette.InkAt(0.4f)), new(1f, UiPalette.InkAt(0f))];
        painter.FillGradient(block, wash, UiGradientDirection.Horizontal);
        painter.FillRect(new UiRect(left, block.Y, Px(3f), block.Height), Accent);

        // The label, and how much more of the history there is either way at its right.
        painter.DrawLabel(type.Mono, "CHAT", new UiVec2(textX, top), labelSize, Accent, 0.28f);
        var history = new UiRect(textX, top + headHeight, inner, historyHeight);
        int newest = Math.Clamp(chat.Count - 1 - entry.Scroll, -1, chat.Count - 1);
        painter.PushClip(history);
        DrawChatRows(painter, type, chat, newest, history, size, rowHeight, ChatOpenRows, washed: false, out int oldest);
        painter.PopClip();

        int newer = Math.Min(entry.Scroll, chat.Count);
        string? more = (oldest > 0, newer > 0) switch
        {
            (true, true) => string.Create(CultureInfo.InvariantCulture, $"{oldest} OLDER  ·  {newer} NEWER"),
            (true, false) => string.Create(CultureInfo.InvariantCulture, $"{oldest} OLDER"),
            (false, true) => string.Create(CultureInfo.InvariantCulture, $"{newer} NEWER"),
            _ => null,
        };
        if (more is not null)
        {
            painter.DrawLabel(type.Mono, more, new UiVec2(history.Right - UiPainter.MeasureLabel(type.Mono, more, labelSize, 0.28f), top), labelSize, Muted, 0.28f);
        }

        if (chat.Count == 0)
        {
            painter.DrawLabel(type.Body, "No messages yet.", new UiVec2(textX, history.Bottom - rowHeight + ((rowHeight - size) * 0.5f)), size, Muted.WithAlpha(150), 0f);
        }

        var field = new UiRect(textX, history.Bottom + Px(14f), inner, fieldHeight);
        DrawChatField(painter, type, entry, field, lineSize, Px);
        if (!entry.Touch)
        {
            DrawChatKeys(painter, context, type, new UiVec2(textX, field.Bottom + Px(8f)));
        }
    }

    // The rows of the lines from `newest` back, bottom up, as many as fit in `rows`; `oldest` is how many lines did not fit.
    private static void DrawChatRows(UiPainter painter, UiTheme type, IReadOnlyList<HudChatLine> chat, int newest, UiRect area, float size, float rowHeight, int rows, bool washed, out int oldest)
    {
        float tagSize = size * 0.66f;
        float gap = size * 0.5f;
        float y = area.Bottom;
        int used = 0;
        int i = newest;
        for (; i >= 0 && used < rows; i--)
        {
            HudChatLine line = chat[i];
            Bgra32 side = line.Enemy && line.Kind != ChatKind.Server ? Bad : Accent;
            string? tag = line.Kind switch
            {
                ChatKind.Server => "SERVER",
                ChatKind.Team when line.Dead => "TEAM · DEAD",
                ChatKind.Team => "TEAM",
                _ when line.Dead => "DEAD",
                _ => null,
            };

            float tagWidth = tag is null ? 0f : UiPainter.MeasureLabel(type.Mono, tag, tagSize, 0.24f) + gap;
            string name = line.Name.ToUpperInvariant();
            float nameWidth = name.Length == 0 ? 0f : UiPainter.MeasureLabel(type.Heading, name, size, 0.06f) + gap;
            List<string> wrapped = Wrap(type.Body, line.Text, size, area.Width - tagWidth - nameWidth, area.Width);
            if (used + wrapped.Count > rows && used > 0)
            {
                break;
            }

            used += wrapped.Count;
            y -= rowHeight * wrapped.Count;
            float alpha = Math.Clamp(line.Opacity, 0f, 1f);
            Bgra32 Fade(Bgra32 ink) => ink.WithAlpha((byte)(ink.A * alpha));
            float top = y + ((rowHeight - size) * 0.5f);

            // Shut, each line on a wash of its own, the objective's, fading out to the right past its end, its side's tick at the left.
            if (washed)
            {
                float longest = wrapped.Max(r => type.Body.MeasureWidth(r, size)) + (wrapped.Count == 1 ? tagWidth + nameWidth : 0f);
                var band = new UiRect(area.X - (size * 0.75f), y, longest + (size * 4.5f), rowHeight * wrapped.Count);
                Span<UiGradientStop> wash = [new(0f, UiPalette.InkAt(0.55f * alpha)), new(0.7f, UiPalette.InkAt(0.35f * alpha)), new(1f, UiPalette.InkAt(0f))];
                painter.FillGradient(band, wash, UiGradientDirection.Horizontal);
                painter.FillRect(new UiRect(band.X, band.Y + (size * 0.25f), MathF.Max(2f, size * 0.14f), band.Height - (size * 0.5f)), Fade(side));
            }

            float x = area.X;
            if (tag is not null)
            {
                painter.DrawLabel(type.Mono, tag, new UiVec2(x, top + ((size - tagSize) * 0.55f)), tagSize, Fade(line.Kind == ChatKind.Server ? Accent : Muted), 0.24f);
                x += tagWidth;
            }

            if (name.Length > 0)
            {
                painter.DrawLabel(type.Heading, name, new UiVec2(x, top), size, Fade(side), 0.06f);
                x += nameWidth;
            }

            for (int row = 0; row < wrapped.Count; row++)
            {
                painter.DrawLabel(type.Body, wrapped[row], new UiVec2(row == 0 ? x : area.X, top + (row * rowHeight)), size, Fade(Text), 0f);
            }
        }

        oldest = i + 1;
    }

    // The line being typed: whom it goes to by a diamond, as the weapon's state is shown, the count at the right, then the text
    // over an accent rule as the weapon's name stands, its caret in the accent, a hint while it is empty.
    private static void DrawChatField(UiPainter painter, UiTheme type, HudChatEntry entry, UiRect field, float size, Func<float, float> Px)
    {
        float labelSize = Px(11f);
        string whom = entry.Team ? "TO TEAM" : "TO ALL";
        Bgra32 whomInk = entry.Team ? Accent : Text;
        painter.FillDiamond(new UiVec2(field.X + Px(3f), field.Y + Px(6f)), Px(6f), whomInk);
        UiVec2 whomEnd = painter.DrawLabel(type.Mono, whom, new UiVec2(field.X + Px(14f), field.Y), labelSize, whomInk, 0.18f);
        if (!entry.Touch)
        {
            painter.DrawLabel(type.Mono, entry.Team ? "TAB FOR ALL" : "TAB FOR TEAM", new UiVec2(whomEnd.X + Px(16f), field.Y), labelSize, Muted.WithAlpha(150), 0.18f);
        }

        if (entry.Text.Length >= SessionTalk.MaxChatLength - 30)
        {
            string count = string.Create(CultureInfo.InvariantCulture, $"{entry.Text.Length} / {SessionTalk.MaxChatLength}");
            Bgra32 ink = entry.Text.Length >= SessionTalk.MaxChatLength ? Bad : Muted;
            painter.DrawLabel(type.Mono, count, new UiVec2(field.Right - UiPainter.MeasureLabel(type.Mono, count, labelSize, 0.28f), field.Y), labelSize, ink, 0.28f);
        }

        float textTop = field.Y + Px(18f);
        var text = new UiRect(field.X, textTop - Px(2f), field.Width, size + Px(6f));
        if (entry.Text.Length == 0)
        {
            painter.DrawLabel(type.Body, entry.Team ? "Say to your team" : "Say to everyone", new UiVec2(field.X, textTop), size, Muted.WithAlpha(130), 0f);
        }

        // Scrolled so the caret stays on the rule, with a little room after it.
        float toCaret = UiPainter.MeasureLabel(type.Body, entry.Text.AsSpan(0, entry.Caret), size, 0f);
        float shift = Math.Max(0f, toCaret - (text.Width - Px(16f)));
        painter.PushClip(text);
        painter.DrawLabel(type.Body, entry.Text, new UiVec2(field.X - shift, textTop), size, Text, 0f);
        if (entry.CaretOn)
        {
            painter.FillRect(new UiRect(field.X - shift + toCaret + Px(1f), textTop - (size * 0.04f), Math.Max(1f, Px(2f)), size * 1.08f), Accent);
        }

        painter.PopClip();
        painter.FillRect(new UiRect(field.X, field.Bottom - Px(2f), field.Width, Px(2f)), Accent.WithAlpha(185));
    }

    // The keys that work while typing, as the interaction prompt shows its key: a cap, then what it does.
    private static void DrawChatKeys(UiPainter painter, LayoutContext context, UiTheme type, UiVec2 at)
    {
        ReadOnlySpan<(string Key, string Does)> keys = [("ENTER", "SEND"), ("↑", "LAST SENT"), ("PG UP", "SCROLL"), ("ESC", "CLOSE")];
        float labelSize = context.Px(11f);
        float x = at.X;
        foreach ((string key, string does) in keys)
        {
            UiRect cap = KitDraw.KeyCap(painter, type, key, new UiVec2(x, at.Y));
            UiVec2 end = painter.DrawLabel(type.Mono, does, new UiVec2(cap.Right + context.Px(7f), UiRowStyle.CapsCenterY(type.Mono, labelSize, cap)), labelSize, Muted, 0.2f);
            x = end.X + context.Px(18f);
        }
    }

    // A line broken into rows at its spaces -- the first row shorter, after the speaker's name -- a word too long for a row cut.
    private static List<string> Wrap(UiFont font, string text, float size, float first, float rest)
    {
        var rows = new List<string>(2);
        int start = 0;
        float room = Math.Max(size, first);
        while (start < text.Length)
        {
            if (font.MeasureWidth(text.AsSpan(start), size) <= room)
            {
                rows.Add(text[start..]);
                break;
            }

            // The most that fits, found by halving; then back to the last space within it, if any.
            int low = 1;
            int high = text.Length - start;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (font.MeasureWidth(text.AsSpan(start, mid), size) <= room)
                {
                    low = mid;
                }
                else
                {
                    high = mid - 1;
                }
            }

            int space = text.LastIndexOf(' ', start + low - 1, low);
            int end = space > start ? space : start + low;
            rows.Add(text[start..end]);
            start = end;
            while (start < text.Length && text[start] == ' ')
            {
                start++;
            }

            room = Math.Max(size, rest);
        }

        if (rows.Count == 0)
        {
            rows.Add(string.Empty);
        }

        return rows;
    }
}
