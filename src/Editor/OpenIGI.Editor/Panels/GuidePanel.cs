using System.Globalization;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// Getting Started: the steps of a first mission, each ticked as soon as the mission shows it done, however it was done;
/// clicking a step opens or runs what does it.
/// </summary>
/// <param name="theme">The theme.</param>
/// <param name="workspace">The mission being made.</param>
/// <param name="steps">The steps.</param>
internal sealed class GuidePanel(EditorTheme theme, MissionWorkspace workspace, IReadOnlyList<GuideStep> steps) : EditorElement(theme)
{
    /// <summary>Gets or sets what a step's <see cref="GuideStep.Show"/> opens or runs.</summary>
    public Action<string>? Show { get; set; }

    /// <summary>Gets or sets whether the mission has been played since the guide began.</summary>
    public bool HasPlayed { get; set; }

    private float Header => Theme.Px(40f);

    private float StepHeight => Theme.Px(66f);

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override bool OnPointerDown(in PointerEvent e)
    {
        if (e.Button != MouseButton.Left || StepAt(e.Position) is not { } index)
        {
            return false;
        }

        Show?.Invoke(steps[index].Show);
        return true;
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        painter.FillRect(Bounds, Theme.Panel);
        painter.PushClip(Bounds);
        if (workspace.Document is not { } document)
        {
            EditorDraw.Label(painter, Theme, "Open a mission to begin.", new UiRect(Bounds.X + Theme.Px(14f), Bounds.Y + Theme.Px(10f), Bounds.Width, Theme.RowHeight), Theme.TextDisabled);
            painter.PopClip();
            return;
        }

        GuideFacts facts = new(document, HasPlayed);
        bool[] done = [.. steps.Select(s => s.IsDone(facts))];
        int count = done.Count(static d => d);
        string progress = count == steps.Count
            ? "All done: the mission has a guard on patrol, an objective, and an end. Save it and share it."
            : string.Create(CultureInfo.InvariantCulture, $"Your first mission: {count} of {steps.Count} done. Click a step for help with it.");
        EditorDraw.Label(painter, Theme, progress, new UiRect(Bounds.X + Theme.Px(14f), Bounds.Y + Theme.Px(8f), Bounds.Width - Theme.Px(28f), Theme.RowHeight), Theme.Text, strong: true);

        int next = Array.IndexOf(done, false);
        UiVec2 pointer = Root?.PointerPosition ?? default;
        for (int i = 0; i < steps.Count; i++)
        {
            UiRect row = new(Bounds.X + Theme.Px(8f), Bounds.Y + Header + (i * StepHeight), Bounds.Width - Theme.Px(16f), StepHeight - Theme.Px(4f));
            if (i == next || (IsPointerOver && row.Contains(pointer)))
            {
                EditorDraw.Fill(painter, Theme, row, i == next ? Theme.Selection : Theme.Hover, Theme.Px(4f));
            }

            // A tick when done, the step's number when not.
            UiVec2 mark = new(row.X + Theme.Px(16f), row.Y + Theme.Px(16f));
            painter.FillDot(mark, Theme.Px(10f), done[i] ? Theme.Success : Theme.Chrome);
            if (done[i])
            {
                EditorDraw.Icon(painter, Theme, KitIcon.Check, new UiRect(mark.X - Theme.Px(8f), mark.Y - Theme.Px(8f), Theme.Px(16f), Theme.Px(16f)), Theme.OnAccent);
            }
            else
            {
                string number = (i + 1).ToString(CultureInfo.InvariantCulture);
                float width = Theme.SmallFont.MeasureWidth(number, Theme.SmallTextSize);
                painter.DrawText(Theme.SmallFont, number, new UiVec2(mark.X - (width / 2), mark.Y - (Theme.SmallTextSize * 0.62f)), Theme.SmallTextSize, Theme.Text);
            }

            UiRect text = new(row.X + Theme.Px(36f), row.Y + Theme.Px(4f), row.Width - Theme.Px(42f), Theme.RowHeight);
            EditorDraw.Label(painter, Theme, steps[i].Title, text, done[i] ? Theme.TextMuted : Theme.Text, strong: i == next);
            float lineY = text.Y + Theme.RowHeight;
            foreach (string line in Wrap(steps[i].Hint, text.Width).Take(2))
            {
                painter.DrawText(Theme.SmallFont, line, new UiVec2(text.X, lineY), Theme.SmallTextSize, Theme.TextMuted);
                lineY += Theme.SmallFont.LineHeight(Theme.SmallTextSize);
            }
        }

        painter.PopClip();
    }

    // A hint broken at spaces to fit a width.
    private List<string> Wrap(string text, float width)
    {
        List<string> lines = [];
        string line = string.Empty;
        foreach (string word in text.Split(' '))
        {
            string longer = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && Theme.SmallFont.MeasureWidth(longer, Theme.SmallTextSize) > width)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = longer;
            }
        }

        lines.Add(line);
        return lines;
    }

    private int? StepAt(UiVec2 point)
    {
        int index = (int)MathF.Floor((point.Y - Bounds.Y - Header) / StepHeight);
        return point.Y >= Bounds.Y + Header && index >= 0 && index < steps.Count ? index : null;
    }
}
