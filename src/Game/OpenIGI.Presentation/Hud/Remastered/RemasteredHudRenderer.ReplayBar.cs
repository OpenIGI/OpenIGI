using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <content>
/// The replay viewer's transport bar, in the played title's look and kept low and out of the run's own HUD: on a sheet at the
/// foot of the screen, what is being watched and how it plays; the run as a track, filled as far as the level stands, its marks
/// along it and the moment it parted from its recording in the danger ink; the time; and the keys that drive it.
/// </content>
public sealed partial class RemasteredHudRenderer
{
    // The bar's measures, in layout pixels.
    private const float ReplayBarWidth = 1180f;
    private const float ReplayTrackHeight = 6f;

    // Where the last bar drew its track, in pixels; empty before one is drawn.
    private (float X, float Y, float Width, float Height) _replayTrack;

    /// <inheritdoc/>
    public (float X, float Y, float Width, float Height) ReplayTrack => _replayTrack;

    /// <inheritdoc/>
    public void DrawReplayBar(SpriteBatch batch, ReplayBar bar, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(bar);
        _replayTrack = default;
        if (Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        float body = o.Px(HudOverlay.Body);
        float small = o.Px(HudOverlay.Small);
        float pad = o.Px(BoardPad);
        float line = o.Text.LineHeightAt(body);
        float width = MathF.Min(o.Px(ReplayBarWidth), o.Safe.Width * 0.94f);
        float height = pad + line + o.Px(18f) + o.Px(ReplayTrackHeight) + o.Px(16f) + o.KeyCapHeight + pad;
        float x = o.Safe.X + ((o.Safe.Width - width) * 0.5f);
        float top = o.Safe.Bottom - height - o.Px(18f);
        o.Sheet(new UiRect(x, top, width, height), 0.92f);

        // What is watched, and how: its heading at the left; at the right the speed and whose view the picture is.
        float left = x + pad;
        float right = x + width - pad;
        float y = top + pad;
        o.Diamond(new UiVec2(left + o.Px(4f), y + (line * 0.5f)), o.Px(5f), bar.Paused ? o.Quiet : o.Ink.Accent);
        float after = left + o.Px(16f) + o.Text.DrawAt(bar.Paused ? "PAUSED" : "REPLAY", left + o.Px(16f), y, body, o.Ink.Highlight) + o.Px(16f);
        float tags = right;
        tags -= TagWidth(o, OverlayText.Capitals(bar.Camera));
        o.Tag(OverlayText.Capitals(bar.Camera), tags, y + ((line - o.Text.LineHeightAt(small) - o.Px(4f)) * 0.5f), o.Ink.Primary);
        string speed = bar.Seeking ? "SEEKING" : string.Create(CultureInfo.InvariantCulture, $"{bar.Speed:0.##}X");
        tags -= TagWidth(o, speed) + o.Px(8f);
        o.Tag(speed, tags, y + ((line - o.Text.LineHeightAt(small) - o.Px(4f)) * 0.5f), bar.Speed == 1f && !bar.Seeking ? o.Quiet : o.Ink.Accent, filled: bar.Seeking);
        o.Text.DrawAt(o.Text.ClipAt(OverlayText.Capitals(bar.Heading), body, MathF.Max(0f, tags - after - o.Px(12f))), after, y, body, o.Quiet);

        // The run as a track: filled as far as the level stands, a mark for each moment noted, and where it parted from its recording.
        y += line + o.Px(18f);
        var track = new UiRect(left, y, right - left, o.Px(ReplayTrackHeight));
        double length = Math.Max(bar.Length, 1.0 / 30.0);
        o.Meter(track, (float)(bar.Seconds / length), bar.PartedAt is null ? o.Ink.Highlight : o.Ink.Danger);
        foreach ((double seconds, string _) in bar.Marks)
        {
            float at = track.X + (track.Width * (float)Math.Clamp(seconds / length, 0.0, 1.0));
            o.Diamond(new UiVec2(at, track.Y - o.Px(7f)), o.Px(4f), o.Ink.Accent);
        }

        if (bar.PartedAt is { } parted)
        {
            float at = track.X + (track.Width * (float)Math.Clamp(parted / length, 0.0, 1.0));
            o.Painter.FillRect(new UiRect(at - o.Px(1.5f), track.Y - o.Px(8f), o.Px(3f), track.Height + o.Px(16f)), o.Ink.Danger);
        }

        float head = track.X + (track.Width * (float)Math.Clamp(bar.Seconds / length, 0.0, 1.0));
        o.Diamond(new UiVec2(head, track.Y + (track.Height * 0.5f)), o.Px(7f), o.Ink.Highlight);
        _replayTrack = (track.X, track.Y - o.Px(12f), track.Width, track.Height + o.Px(24f));

        // The time at the left, under it what the run came to or where it parted; the keys at the right.
        y = track.Bottom + o.Px(16f);
        float textY = y + ((o.KeyCapHeight - o.Text.LineHeightAt(body)) * 0.5f);
        float timeRight = left + o.Text.DrawAt($"{Clock(bar.Seconds)} / {Clock(bar.Length)}", left, textY, body, o.Ink.Highlight);
        string? note = bar.PartedAt is { } p
            ? $"PARTED FROM ITS RECORDING AT {Clock(p)}"
            : bar.Ending is { } ending ? OverlayText.Capitals(ending) : null;
        float keysLeft = right;
        foreach ((string key, string what) in ReplayKeys)
        {
            keysLeft -= o.KeyCapWidth(key) + o.Px(6f) + o.Text.MeasureAt(what, small) + o.Px(18f);
        }

        if (note is not null)
        {
            float noteX = timeRight + o.Px(18f);
            o.Text.DrawAt(o.Text.ClipAt(note, small, MathF.Max(0f, keysLeft - noteX - o.Px(12f))), noteX, y + ((o.KeyCapHeight - o.Text.LineHeightAt(small)) * 0.5f), small, bar.PartedAt is null ? o.Ink.Accent : o.Ink.Danger);
        }

        // Only as many keys as fit beside the time and the note, from the most used.
        float keyX = MathF.Max(keysLeft, timeRight + o.Px(24f));
        foreach ((string key, string what) in ReplayKeys)
        {
            float need = o.KeyCapWidth(key) + o.Px(6f) + o.Text.MeasureAt(what, small);
            if (keyX + need > right + o.Px(1f))
            {
                break;
            }

            UiRect cap = o.KeyCap(key, keyX, y);
            o.Text.DrawAt(what, cap.Right + o.Px(6f), cap.Y + ((cap.Height - o.Text.LineHeightAt(small)) * 0.5f), small, o.Quiet);
            keyX += need + o.Px(18f);
        }

        static float TagWidth(HudOverlay o, string word) => o.Text.MeasureAt(word, o.Px(HudOverlay.Small)) + o.Px(10f);

        static string Clock(double seconds)
        {
            int whole = (int)Math.Max(0.0, seconds);
            return string.Create(CultureInfo.InvariantCulture, $"{whole / 60:00}:{whole % 60:00}");
        }
    }

    // The viewer's keys, as its caps show them, most used first.
    private static readonly (string Key, string What)[] ReplayKeys =
    [
        ("Space", "PLAY"),
        ("J", "BACK"),
        ("L", "ON"),
        ("-", "SLOWER"),
        ("=", "FASTER"),
        ("C", "CAMERA"),
        ("H", "HIDE"),
    ];
}
