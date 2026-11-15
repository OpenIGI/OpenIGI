using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Host.Editor;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Viewport;

/// <summary>
/// The banner over a play-test: the way back, the play-test's keys (those in force lit) and the recording -- how long it has
/// run, or, played back, a scrubber with where it is and what happened when: a guard first seeing the player, a command given.
/// </summary>
/// <remarks>
/// Every shape first, then every word in one run: text drawn after another run of text and a shadow lost its first glyphs on
/// the D3D12 path.
/// </remarks>
internal static class PlaytestBanner
{
    private static readonly Bgra32 MarkColour = Bgra32.FromRgb(0xF0, 0x8A, 0x2E);
    private static readonly Bgra32 Recording = Bgra32.FromRgb(0xF0, 0x40, 0x3A);

    /// <summary>Draws the banner, and the scrubber under it while a recording plays back.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="test">The play-test as it stands.</param>
    /// <param name="screenWidth">The window's width.</param>
    /// <returns>The bottom of what was drawn, for whatever goes under it.</returns>
    public static float Draw(UiPainter painter, EditorTheme theme, EditorPlaytest test, float screenWidth)
    {
        EditorPlaytestTape? tape = test.Tape;
        bool replaying = tape is { Replaying: true };

        // At the recording's end the level is held there: F6 plays it again from the start, a step goes nowhere.
        bool ended = replaying && tape!.Seconds >= tape.Length;
        List<(string Text, Bgra32 Colour)> parts =
        [
            (ended ? "Replay ended" : replaying ? "Replay" : test.Paused ? "Paused" : "Playing", test.Paused || replaying ? theme.Accent : theme.Text),
        ];
        if (tape is { Recording: true })
        {
            parts.Add(($"REC {Clock(tape.Length)}", Recording));
        }

        parts.Add(("Esc editor", theme.Text));
        parts.Add((ended ? "F6 from start" : test.Paused ? "F6 run" : "F6 pause", theme.Text));
        if (!ended)
        {
            parts.Add(("F7 step", theme.Text));
        }

        parts.Add((string.Create(CultureInfo.InvariantCulture, $"F8 speed {test.Speed:0.##}x"), test.Speed != 1f ? theme.Accent : theme.Text));
        if (!replaying)
        {
            parts.Add(("F2 god", test.God ? theme.Accent : theme.Text));
            parts.Add(("F3 unseen", test.Unseen ? theme.Accent : theme.Text));
            if (test.HasCamera)
            {
                parts.Add(("F4 to camera", theme.Text));
            }
        }

        parts.Add(("F10 minds", test.Overlay ? theme.Accent : theme.Text));
        parts.Add((replaying ? "F9 play live" : "F9 replay", theme.Text));

        float gap = theme.Px(16f);
        float width = parts.Sum(p => theme.Font.MeasureWidth(p.Text, theme.TextSize)) + (gap * (parts.Count - 1)) + theme.Px(28f);
        UiRect banner = new(MathF.Round((screenWidth - width) * 0.5f), theme.Px(14f), width, theme.ControlHeight + theme.Px(4f));
        EditorDraw.Shadow(painter, theme, banner);
        EditorDraw.Frame(painter, theme, banner, theme.Popup.WithAlpha(230), theme.Accent);

        // The scrubber's shapes, under the banner, while a recording plays back.
        List<(UiRect Box, string Text, Bgra32 Colour)> words = [];
        float bottom = banner.Bottom;
        if (replaying && tape is { Length: > 0 })
        {
            UiRect strip = new(banner.X, banner.Bottom + theme.Px(6f), banner.Width, (theme.RowHeight * 2f) + theme.Px(10f));
            painter.FillRect(strip, theme.Popup.WithAlpha(220));
            UiRect bar = new(strip.X + theme.Px(14f), strip.Y + theme.Px(8f), strip.Width - theme.Px(28f), theme.Px(6f));
            painter.FillRect(bar, theme.Text.WithAlpha(50));
            float done = (float)Math.Clamp(tape.Seconds / tape.Length, 0, 1);
            painter.FillRect(bar with { Width = bar.Width * done }, theme.Accent);
            float lastLabel = float.NegativeInfinity;
            foreach ((double seconds, string text) in tape.Marks)
            {
                float x = bar.X + (bar.Width * (float)Math.Clamp(seconds / tape.Length, 0, 1));
                painter.FillRect(new UiRect(x - theme.Px(1f), bar.Y - theme.Px(4f), theme.Px(2f), bar.Height + theme.Px(8f)), MarkColour);
                float labelWidth = theme.Font.MeasureWidth(text, theme.TextSize);
                if (x > lastLabel + theme.Px(8f) && x + labelWidth < strip.Right)
                {
                    words.Add((new UiRect(x, bar.Bottom + theme.Px(4f), labelWidth + theme.Px(2f), theme.RowHeight), text, MarkColour));
                    lastLabel = x + labelWidth;
                }
            }

            string time = $"{Clock(tape.Seconds)} / {Clock(tape.Length)}   Left/Right 5 s   Home start";
            words.Add((new UiRect(bar.X, strip.Bottom - theme.RowHeight, theme.Font.MeasureWidth(time, theme.TextSize) + theme.Px(2f), theme.RowHeight), time, theme.TextMuted));
            bottom = strip.Bottom;
        }

        // Then every word.
        float at = banner.X + theme.Px(14f);
        foreach ((string text, Bgra32 colour) in parts)
        {
            float w = theme.Font.MeasureWidth(text, theme.TextSize);
            EditorDraw.Label(painter, theme, text, new UiRect(at, banner.Y, w + theme.Px(2f), banner.Height), colour);
            at += w + gap;
        }

        foreach ((UiRect box, string text, Bgra32 colour) in words)
        {
            EditorDraw.Label(painter, theme, text, box, colour);
        }

        return bottom;
    }

    private static string Clock(double seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}");
}
