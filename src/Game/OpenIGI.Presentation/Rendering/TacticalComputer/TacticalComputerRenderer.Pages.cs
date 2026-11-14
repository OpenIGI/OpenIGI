using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Rendering;

/// <content>The text pages, each on one glass panel over the dimmed feed: the objectives, the log, and the mission failed.</content>
public sealed partial class TacticalComputerRenderer
{
    // How far a page's text stands in from its panel's edge, and where it starts under the heading and its rule.
    private static float PagePad(in TacticalComputerChrome chrome) => chrome.Px(22f);

    private static float PageBodyTop(in TacticalComputerChrome chrome) =>
        chrome.Page.Y + chrome.Px(16f) + chrome.Line + chrome.Px(10f) + chrome.Px(14f);

    // The panel, the page's name on it and a rule under that, then the page.
    private void DrawPage(ComputerCanvas canvas, IComputerObjectives runtime)
    {
        TacticalComputerChrome chrome = canvas.Chrome;
        UiRect card = chrome.Page;
        if (card.Width <= 0f || card.Height <= 0f)
        {
            return;
        }

        HudPanel.Draw(canvas.Painter, canvas.Theme, chrome.Unit, canvas.Seconds, card);

        float pad = PagePad(chrome);
        float y = card.Y + chrome.Px(16f);
        canvas.Text.Draw(PageHeading(), card.X + pad, y, canvas.Palette.Highlight);
        if (_page == Page.Save)
        {
            DrawBattery(canvas, card.Right - pad, y);
        }

        y += chrome.Line + chrome.Px(10f);
        HudPanel.Rule(canvas.Painter, canvas.Theme, chrome.Unit, card.X + pad, y, card.Width - (pad * 2f), ComputerCanvas.Fade(canvas.Palette.Primary, 0.55f));
        y = PageBodyTop(chrome);

        switch (_page)
        {
            case Page.Objectives:
                DrawObjectives(canvas, runtime, card, pad, y);
                break;
            case Page.Save:
                DrawSave(canvas, card, pad, y);
                break;
            default:
                DrawLog(canvas, runtime, card, pad, y);
                break;
        }
    }

    private string PageHeading() => _page == Page.Save ? SaveHeading() : PageNames[(int)_page];

    // Each objective: its number, its words, and what it stands at, in that state's colour.
    private void DrawObjectives(ComputerCanvas canvas, IComputerObjectives runtime, UiRect card, float pad, float y)
    {
        HudPalette palette = canvas.Palette;
        ComputerText text = canvas.Text;
        if (runtime.Objectives.Count == 0)
        {
            text.Draw("NO OBJECTIVES ON FILE", card.X + pad, y, canvas.Quiet);
            return;
        }

        float line = text.LineHeight;
        float rowPad = canvas.Px(9f);
        float bar = MathF.Max(2f, MathF.Round(canvas.Px(2.5f)));
        float numberWidth = text.Measure("00") + canvas.Px(14f);
        float stateWidth = text.Measure("COMPLETE") + canvas.Px(18f);
        float left = card.X + pad;
        float width = card.Width - (pad * 2f);
        float textLeft = left + bar + canvas.Px(12f) + numberWidth;
        float textWidth = MathF.Max(canvas.Px(80f), width - (textLeft - left) - stateWidth);

        for (int i = 0; i < runtime.Objectives.Count; i++)
        {
            MissionObjective objective = runtime.Objectives[i];
            string words = text.Prose(objective.Text.Length > 0 ? objective.Text : ComputerString(objective.TextResource));
            _lines.Clear();
            text.Wrap(words, textWidth, _lines);
            float rowHeight = (line * Math.Max(1, _lines.Count)) + (rowPad * 2f);
            if (y + rowHeight > card.Bottom - canvas.Px(10f))
            {
                break;
            }

            (Bgra32 ink, string state) = objective.State switch
            {
                MissionObjectiveState.Complete => (canvas.Quiet, "COMPLETE"),
                MissionObjectiveState.Failed => (palette.Danger, "FAILED"),
                _ => (palette.Accent, "ACTIVE"),
            };
            bool active = objective.State == MissionObjectiveState.Incomplete;

            var row = new UiRect(left, y, width, rowHeight);
            canvas.Painter.FillRect(row, ComputerCanvas.Fade(palette.Primary, active ? 0.08f : 0.04f));
            canvas.Painter.FillRect(new UiRect(row.X, row.Y, bar, row.Height), ink);

            text.Draw(string.Create(CultureInfo.InvariantCulture, $"{i + 1:00}"), row.X + bar + canvas.Px(12f), row.Y + rowPad, ink);
            for (int l = 0; l < _lines.Count; l++)
            {
                text.Draw(_lines[l], textLeft, row.Y + rowPad + (line * l), active ? palette.Primary : canvas.Quiet);
            }

            text.DrawRight(state, row.Right - canvas.Px(10f), row.Y + rowPad, ink);
            y += rowHeight + canvas.Px(6f);
        }
    }

    // The log, newest first: the last thing that happened is the thing being looked for, and a log that reads downwards
    // makes the player scroll to find out what they were just told.
    private void DrawLog(ComputerCanvas canvas, IComputerObjectives runtime, UiRect card, float pad, float y)
    {
        HudPalette palette = canvas.Palette;
        ComputerText text = canvas.Text;
        if (runtime.Log.Count == 0)
        {
            text.Draw("NO TRANSMISSIONS LOGGED", card.X + pad, y, canvas.Quiet);
            return;
        }

        float line = text.LineHeight;
        float mark = MathF.Max(3f, MathF.Round(canvas.Px(4f)));
        float left = card.X + pad;
        float textLeft = left + mark + canvas.Px(12f);
        float textWidth = card.Right - pad - textLeft;

        int index = 0;
        for (int i = runtime.Log.Count - 1; i >= 0; i--)
        {
            MissionComputerLogEntry entry = runtime.Log[i];
            string words = text.Prose(entry.Text.Length > 0 ? entry.Text : ComputerString(entry.TextResource));
            _lines.Clear();
            text.Wrap(words, textWidth, _lines);
            float height = line * Math.Max(1, _lines.Count);
            if (y + height > card.Bottom - canvas.Px(10f))
            {
                break;
            }

            // The newest entry alone at full strength; older ones step back a little each time and then hold, so recency
            // reads at a glance without the tail becoming unreadable.
            float strength = index == 0 ? 1f : MathF.Max(0.5f, 0.85f - (index * 0.07f));
            Bgra32 ink = ComputerCanvas.Fade(index == 0 ? palette.Highlight : palette.Primary, strength);
            canvas.Painter.FillRect(
                new UiRect(left, MathF.Round(y + ((line - mark) * 0.5f)), mark, mark),
                index == 0 ? palette.Accent : ComputerCanvas.Fade(canvas.Quiet, strength));
            for (int l = 0; l < _lines.Count; l++)
            {
                text.Draw(_lines[l], textLeft, y + (line * l), ink);
            }

            y += height + canvas.Px(9f);
            index++;
        }
    }

    // The mission failed: the screen darkened, and one panel in the colour of harm with what the keys do.
    private static void DrawMissionFailed(ComputerCanvas canvas, int screenWidth, int screenHeight)
    {
        HudPalette palette = canvas.Palette;
        ComputerText text = canvas.Text;
        canvas.Painter.FillRect(new UiRect(0f, 0f, screenWidth, screenHeight), Bgra32.FromRgba(0, 0, 0, 200));

        float line = text.LineHeight;
        float pad = canvas.Px(24f);
        float width = MathF.Min(canvas.Px(460f), screenWidth - (pad * 2f));
        float height = (pad * 2f) + (line * 3f) + canvas.Px(34f);
        var card = new UiRect(MathF.Round((screenWidth - width) * 0.5f), MathF.Round((screenHeight - height) * 0.5f), width, height);
        HudPanel.Draw(canvas.Painter, canvas.Theme, canvas.Chrome.Unit, canvas.Seconds, card);
        canvas.Painter.FillRect(new UiRect(card.X, card.Y, MathF.Max(2f, canvas.Px(3f)), card.Height), palette.Danger);

        float y = card.Y + pad;
        text.Draw("MISSION FAILED", card.X + pad, y, palette.Danger);
        y += line + canvas.Px(10f);
        HudPanel.Rule(canvas.Painter, canvas.Theme, canvas.Chrome.Unit, card.X + pad, y, card.Width - (pad * 2f), ComputerCanvas.Fade(palette.Danger, 0.6f));
        y += canvas.Px(14f);

        float x = card.X + pad;
        x += text.Draw("ENTER", x, y, palette.Highlight) + canvas.Px(8f);
        text.Draw("RESTART MISSION", x, y, canvas.Quiet);
        y += line + canvas.Px(10f);
        x = card.X + pad;
        x += text.Draw("ESC", x, y, palette.Highlight) + canvas.Px(8f);
        text.Draw("ABORT TO MENU", x, y, canvas.Quiet);
    }
}
