using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Rendering;

/// <content>The strips along the top and the bottom of the display, and the pointer.</content>
public sealed partial class TacticalComputerRenderer
{
    // A strip of glass across the screen: the feed behind it blurred under the title's tint, and a rule along its inner edge.
    private static void DrawStrip(ComputerCanvas canvas, UiRect strip, bool ruleBelow)
    {
        canvas.Painter.DrawBackdrop(strip, HudPanel.BlurSigma, canvas.Palette.Glass.WithAlpha(170));
        canvas.Painter.FillRect(strip, Bgra32.FromRgba(0, 0, 0, 150));
        canvas.Painter.FillRect(strip, ComputerCanvas.Fade(canvas.Palette.Primary, 0.05f));
        float weight = canvas.Hairline;
        Bgra32 rule = ComputerCanvas.Fade(canvas.Palette.Primary, 0.6f);
        if (ruleBelow)
        {
            canvas.Painter.FillRect(new UiRect(strip.X, strip.Bottom - weight, strip.Width, weight), rule);
            if (canvas.Theme.Motifs.DoubleRules)
            {
                canvas.Painter.FillRect(new UiRect(strip.X, strip.Bottom + (weight * 2f), strip.Width, weight), ComputerCanvas.Fade(rule, 0.4f));
            }
        }
        else
        {
            canvas.Painter.FillRect(new UiRect(strip.X, strip.Y, strip.Width, weight), rule);
            if (canvas.Theme.Motifs.DoubleRules)
            {
                canvas.Painter.FillRect(new UiRect(strip.X, strip.Y - (weight * 3f), strip.Width, weight), ComputerCanvas.Fade(rule, 0.4f));
            }
        }
    }

    // The tabs, and at the right what the mission stands at: its objectives done of all, and the hostile on the map.
    private void DrawTopStrip(ComputerCanvas canvas, IComputerObjectives runtime, int contacts)
    {
        TacticalComputerChrome chrome = canvas.Chrome;
        HudPalette palette = canvas.Palette;
        DrawStrip(canvas, chrome.Top, ruleBelow: true);

        for (int i = 0; i < PageNames.Length; i++)
        {
            UiRect tab = _tabs[i];
            bool selected = (int)_page == i;
            bool hovered = _hoverPage == i;
            Bgra32 ink;
            if (!IsSelectable((Page)i))
            {
                // A page the level has not given the player stays listed, quiet, the way the original leaves its MAP link
                // out rather than renumbering what is left.
                ink = ComputerCanvas.Fade(canvas.Quiet, 0.45f);
            }
            else if (selected)
            {
                float cut = canvas.Px(canvas.Theme.Motifs.CornerSize * 0.6f);
                UiCorners corners = canvas.Theme.Motifs.Corner == HudCorner.Cut ? UiCorners.TopLeft | UiCorners.BottomRight : UiCorners.All;
                canvas.Painter.FillCutRect(tab, cut, corners, ComputerCanvas.Fade(palette.Primary, 0.2f));
                canvas.Painter.FillRect(new UiRect(tab.X + cut, tab.Bottom - canvas.Hairline, tab.Width - (cut * 2f), canvas.Hairline), palette.Highlight);
                HudPanel.Brackets(canvas.Painter, canvas.Theme, chrome.Unit, tab, canvas.Px(5f), palette.Highlight);
                ink = palette.Highlight;
            }
            else if (hovered)
            {
                canvas.Painter.FillRect(tab, ComputerCanvas.Fade(palette.Primary, 0.1f));
                ink = palette.Primary;
            }
            else
            {
                ink = ComputerCanvas.Fade(palette.Primary, 0.7f);
            }

            float y = tab.Y + ((tab.Height - canvas.Text.LineHeight) * 0.5f);
            canvas.Text.Draw(PageNames[i], tab.X + ((tab.Width - _tabWidths[i]) * 0.5f), y, ink);
        }

        float line = chrome.Top.Y + ((chrome.Top.Height - canvas.Text.LineHeight) * 0.5f);
        float right = chrome.Top.Right - chrome.Gutter;
        float limit = _tabs[^1].Right + canvas.Px(24f);

        string count = contacts.ToString(CultureInfo.InvariantCulture);
        float left = right - canvas.Text.Measure("CONTACTS " + count);
        if (left >= limit)
        {
            float x = left + canvas.Text.Draw("CONTACTS ", left, line, canvas.Quiet);
            canvas.Text.Draw(count, x, line, contacts > 0 ? palette.Danger : canvas.Quiet);
            right = left - canvas.Px(22f);
        }

        int total = runtime.Objectives.Count;
        int complete = 0;
        foreach (MissionObjective objective in runtime.Objectives)
        {
            if (objective.State == MissionObjectiveState.Complete)
            {
                complete++;
            }
        }

        string done = string.Create(CultureInfo.InvariantCulture, $"{complete}/{total}");
        left = right - canvas.Text.Measure("OBJ " + done);
        if (left >= limit)
        {
            float x = left + canvas.Text.Draw("OBJ ", left, line, canvas.Quiet);
            canvas.Text.Draw(done, x, line, total > 0 && complete >= total ? palette.Primary : palette.Accent);
        }
    }

    // Where the player stands, the keys in the middle where there is room for them, and the map's scale.
    private void DrawBottomStrip(ComputerCanvas canvas, Vector3d player, Viewport map, float fieldOfView, string closeKey)
    {
        TacticalComputerChrome chrome = canvas.Chrome;
        HudPalette palette = canvas.Palette;
        DrawStrip(canvas, chrome.Bottom, ruleBelow: false);

        float middle = chrome.Bottom.Y + (chrome.Bottom.Height * 0.5f);
        float line = middle - (canvas.Text.LineHeight * 0.5f);

        // The player's own position, in metres east and north of the level's origin and over its datum: the engine's
        // 1/4096 of a metre made into a grid reference, which is what a map is for.
        float x = chrome.Gutter;
        x += canvas.Text.Draw(TacticalMapOverlay.GridReference(player), x, line, palette.Primary);
        long altitude = (long)Math.Round(player.Z / UnitsPerMetre);
        x += canvas.Text.Draw(string.Create(CultureInfo.InvariantCulture, $"  ALT {altitude}M"), x, line, canvas.Quiet);

        // The scale as a bar, which says how far away something is, and as a ratio, which says how far in the map is.
        float right = chrome.Bottom.Right - chrome.Gutter;
        if (MapAvailable)
        {
            right = TacticalMapOverlay.DrawScaleBar(canvas, right, middle, map, fieldOfView) - canvas.Px(18f);
            string ratio = string.Create(
                CultureInfo.InvariantCulture, $"1:{Math.Round(TacticalMapOverlay.MetresPerPixel(map, fieldOfView) * 1000.0):0}");
            right = canvas.Text.DrawRight(ratio, right, line, canvas.Quiet);
        }

        DrawKeys(canvas, x + canvas.Px(28f), right - canvas.Px(28f), line, closeKey);
    }

    // The keys, centred in the room between the two ends of the line; the last are left out where they would not fit.
    private void DrawKeys(ComputerCanvas canvas, float left, float right, float line, string closeKey)
    {
        // A touch screen is told what its fingers do; it has no keys to name.
        Span<(string Key, string Action)> keys = _touch
            ?
            [
                ("DRAG", "PAN"),
                ("PINCH", "ZOOM"),
                ("TAP TWICE", "CENTRE"),
            ]
            :
            [
                (closeKey.Length > 0 ? canvas.Text.Capitals(closeKey) : "ESC", "CLOSE"),
                ("TAB", "PAGE"),
                ("DRAG", "PAN"),
                ("WHEEL", "ZOOM"),
                ("SPACE", "CENTRE"),
            ];
        int count = MapAvailable ? keys.Length : _touch ? 0 : 2;
        float gap = canvas.Px(18f);
        float inner = canvas.Px(6f);

        float width = 0f;
        int shown = 0;
        for (int i = 0; i < count; i++)
        {
            float one = canvas.Text.Measure(keys[i].Key) + inner + canvas.Text.Measure(keys[i].Action);
            if (width + one + (shown > 0 ? gap : 0f) > right - left)
            {
                break;
            }

            width += one + (shown > 0 ? gap : 0f);
            shown++;
        }

        float x = left + ((right - left - width) * 0.5f);
        for (int i = 0; i < shown; i++)
        {
            x += canvas.Text.Draw(keys[i].Key, x, line, canvas.Palette.Highlight) + inner;
            x += canvas.Text.Draw(keys[i].Action, x, line, canvas.Quiet) + gap;
        }
    }

    /// <summary>Draws the pointer: a reticle with its place over the map, an arrow over the interface.</summary>
    /// <remarks>
    /// Two shapes because they answer two questions. Over the map the pointer is a position -- it is what the zoom anchors
    /// on and what the drag moves -- so it is a crosshair with an open middle that does not hide what it points at, and it
    /// says where that is. Over the interface it is a click, so it is an arrow.
    /// </remarks>
    private void DrawCursor(ComputerCanvas canvas, Vector3d player, Viewport map, float fieldOfView)
    {
        var at = new UiVec2(_mouseX, _mouseY);
        if (MapAvailable && _page == Page.Map && !canvas.Chrome.OverInterface(_mouseX, _mouseY))
        {
            Vector3d ground = TacticalMapProjection.Unproject(_mouseX, _mouseY, _centreUnits, fieldOfView, map);
            TacticalMapOverlay.DrawPointer(canvas, at, ground, player);
            return;
        }

        float size = canvas.Px(15f);
        float drop = canvas.Px(1.5f);
        canvas.Painter.FillTriangle(
            new UiVec2(at.X + drop, at.Y + drop),
            new UiVec2(at.X + drop, at.Y + size + drop),
            new UiVec2(at.X + (size * 0.66f) + drop, at.Y + (size * 0.72f) + drop),
            Bgra32.FromRgba(0, 0, 0, 190));
        canvas.Painter.FillTriangle(
            at,
            new UiVec2(at.X, at.Y + size),
            new UiVec2(at.X + (size * 0.66f), at.Y + (size * 0.72f)),
            canvas.Palette.Highlight);
    }
}
