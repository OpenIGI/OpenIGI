using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Game.World;
using OpenIGI.Host.Editor;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <content>The timeline drawn: the ruler, the shots, the animations, the cue lanes and the list of a crowded mark's cues.</content>
internal sealed partial class SequencerPanel
{
    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        painter.FillRect(Bounds, Theme.Panel);
        if (_scene is not { } scene || _workspace.Document is not { } document)
        {
            EditorDraw.Label(painter, Theme, _workspace.Document is null ? "Open a mission to edit its cutscenes." : "This mission has no cutscenes.", new UiRect(Bounds.X + Theme.Px(12f), Bounds.Y + Bar + Theme.Px(10f), Bounds.Width, Theme.RowHeight), Theme.TextDisabled);
            return;
        }

        UiRect line = Timeline;
        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        IReadOnlyList<CutsceneCue> cues = CutsceneModel.Cues(document, scene);
        double length = Math.Max(CutsceneModel.Length(scene), cues.Count > 0 ? cues[^1].Seconds : 0);

        // The ruler: a tick a second, a label every so many.
        UiRect ruler = new(line.X, line.Y, line.Width, Theme.Px(RulerHeight));
        painter.FillRect(ruler, Theme.Chrome);
        int every = _pixelsPerSecond >= 60 ? 1 : _pixelsPerSecond >= 20 ? 5 : 10;
        painter.PushClip(line);
        for (int second = (int)Math.Floor(_scroll); second <= length + 30; second++)
        {
            float x = X(second);
            if (x > line.Right)
            {
                break;
            }

            bool major = second % every == 0;
            painter.FillRect(new UiRect(x, ruler.Bottom - Theme.Px(major ? 8f : 4f), 1f, Theme.Px(major ? 8f : 4f)), Theme.TextMuted);
            if (major)
            {
                EditorDraw.Label(painter, Theme, string.Create(CultureInfo.InvariantCulture, $"{second}s"), new UiRect(x + Theme.Px(3f), ruler.Y, Theme.Px(40f), ruler.Height), Theme.TextMuted);
            }
        }

        painter.PopClip();

        // The lanes, each with its name on the left.
        float laneY = ruler.Bottom + Theme.Px(4f);
        DrawLaneLabel(painter, "Camera", laneY);
        MissionTask? selected = _workspace.Selection.Primary;
        painter.PushClip(line);
        for (int i = 0; i < shots.Count; i++)
        {
            CutsceneShot shot = shots[i];
            double clipLength = _resizing is { } r && ReferenceEquals(r.Shot.Camera, shot.Camera) ? r.Length : shot.Length;
            double start = StartOf(shots, i);
            UiRect clip = new(X(start) + 1f, laneY + 2f, MathF.Max(Theme.Px(4f), (float)(clipLength * _pixelsPerSecond) - 2f), Theme.Px(LaneHeight) - 4f);
            bool isSelected = ReferenceEquals(shot.Camera, selected);
            EditorDraw.Frame(painter, Theme, clip, (isSelected ? Theme.Accent : Bgra32.FromRgb(0x3E, 0x6E, 0xA8)).WithAlpha(isSelected ? (byte)200 : (byte)170), isSelected ? Theme.Accent : Theme.Border);
            string rides = Number(shot.Camera, "Link task ID", -1) >= 0 ? "  (rides)" : Number(shot.Camera, "Target task ID", -1) >= 0 ? "  (watches)" : string.Empty;
            string name = string.Create(CultureInfo.InvariantCulture, $"{i + 1}. {(shot.Camera.Label.Length > 0 ? shot.Camera.Label : "Shot")}  {clipLength:0.#}s{rides}");
            EditorDraw.Label(painter, Theme, EditorDraw.Fit(Theme.Font, name, Theme.TextSize, clip.Width - Theme.Px(8f)), clip with { X = clip.X + Theme.Px(4f), Width = clip.Width - Theme.Px(6f) }, Bgra32.White);
        }

        painter.PopClip();
        laneY += Theme.Px(LaneHeight);

        // What the scene animates, a row each, a clip for as long as its keys run, named by what it moves.
        IReadOnlyList<CutsceneAnimation> animations = CutsceneModel.Animations(document, scene);
        foreach (CutsceneAnimation animation in animations)
        {
            DrawLaneLabel(painter, "Animation", laneY);
            painter.PushClip(line);
            {
                double start = _moving is { } held && ReferenceEquals(held.Cue.Task, animation.Task) ? held.Seconds : animation.Start;
                UiRect clip = new(X(start) + 1f, laneY + 2f, MathF.Max(Theme.Px(6f), (float)(animation.Length * _pixelsPerSecond) - 2f), Theme.Px(LaneHeight) - 4f);
                bool isSelected = ReferenceEquals(animation.Task, selected);
                EditorDraw.Frame(painter, Theme, clip, (isSelected ? Theme.Accent : Bgra32.FromRgb(0x5E, 0x8C, 0x4A)).WithAlpha(isSelected ? (byte)200 : (byte)170), isSelected ? Theme.Accent : Theme.Border);
                EditorDraw.Label(painter, Theme, EditorDraw.Fit(Theme.Font, AnimationName(document, animation), Theme.TextSize, clip.Width - Theme.Px(8f)), clip with { X = clip.X + Theme.Px(4f), Width = clip.Width - Theme.Px(6f) }, Bgra32.White);
            }

            painter.PopClip();
            laneY += Theme.Px(LaneHeight);
        }

        CutsceneCue[]? hovered = null;
        UiVec2 hoveredAt = default;
        foreach ((string title, Func<MissionTask, bool> holds) in CueLanes)
        {
            CutsceneCue[] mine = LaneCues(document, scene, cues, animations, title);
            if (mine.Length == 0)
            {
                continue;
            }

            DrawLaneLabel(painter, title, laneY);
            painter.PushClip(line);
            (CutsceneCue[] ordered, IReadOnlyList<CueLaneLayout.Mark> marks) = LaneMarks(mine);
            float middle = laneY + (Theme.Px(LaneHeight) * 0.5f);
            foreach (CueLaneLayout.Mark mark in marks)
            {
                bool isSelected = ordered.Skip(mark.First).Take(mark.Count).Any(c => ReferenceEquals(c.Task, selected));
                Bgra32 colour = isSelected ? Theme.Accent : Bgra32.FromRgb(0xE0, 0xB0, 0x4C);
                painter.FillDiamond(new UiVec2(mark.X, middle), Theme.Px(mark.Count > 1 ? 7f : 6f), colour);
                if (mark.Count > 1)
                {
                    // A mark standing for several: a darker core says so before its label is read.
                    painter.FillDiamond(new UiVec2(mark.X, middle), Theme.Px(3f), Theme.Panel);
                }

                if (mark.Label.Length > 0)
                {
                    EditorDraw.Label(painter, Theme, mark.Label, new UiRect(mark.X + Theme.Px(CueLabelGap), laneY + Theme.Px(4f), Theme.Font.MeasureWidth(mark.Label, Theme.TextSize) + Theme.Px(2f), Theme.RowHeight), Theme.Text);
                }

                if (_hover is { } over && over.Y >= laneY && over.Y < laneY + Theme.Px(LaneHeight) && MathF.Abs(over.X - mark.X) <= Theme.Px(CueReach))
                {
                    hovered = [.. ordered.Skip(mark.First).Take(mark.Count)];
                    hoveredAt = new UiVec2(mark.X, laneY + Theme.Px(LaneHeight));
                }
            }

            painter.PopClip();
            laneY += Theme.Px(LaneHeight);
        }

        // The playhead.
        float head = X(_time);
        if (head >= line.X && head <= line.Right)
        {
            painter.FillRect(new UiRect(head - 1f, line.Y, 2f, line.Height), Bgra32.FromRgb(0xFF, 0x5A, 0x4A));
        }

        // How the scene is shown, at the ruler's end, over the seconds past the scene.
        string status = LiveStatus();
        float statusWidth = Theme.Font.MeasureWidth(status, Theme.TextSize) + Theme.Px(12f);
        UiRect statusBox = new(line.Right - statusWidth - Theme.Px(4f), line.Y, statusWidth, Theme.Px(RulerHeight));
        painter.FillRect(statusBox, Theme.Chrome);
        EditorDraw.Label(painter, Theme, status, statusBox with { X = statusBox.X + Theme.Px(6f) }, LiveActive ? Theme.Success : Theme.TextMuted);
        if (!LiveActive && ShotAt(shots, _time) is { } now && Number(now.Camera, "Link task ID", -1) is >= 0 and double link
            && document.EnumerateTasks().FirstOrDefault(t => t.Id == (int)link) is { } carrier)
        {
            string note = $"This shot rides {TaskPresentation.Name(carrier)} ({carrier.TypeName} {carrier.Id}): the shots' path shows it where {TaskPresentation.Name(carrier)} starts; Live shows it moving.";
            EditorDraw.Label(painter, Theme, note, new UiRect(Bounds.X + Theme.Px(8f), Bounds.Bottom - Theme.RowHeight - Theme.Px(2f), Bounds.Width - Theme.Px(16f), Theme.RowHeight), Theme.Warning);
        }

        EditorDraw.Label(painter, Theme, string.Create(CultureInfo.InvariantCulture, $"{_time:0.00} / {CutsceneModel.Length(scene):0.00} s"), new UiRect(Bounds.X + Theme.Px(8f), Bounds.Y + Bar + Theme.Px(2f), Theme.Px(LabelWidth), Theme.RowHeight), Theme.Text);

        if (hovered is { Length: > 0 })
        {
            DrawCueList(painter, hovered, hoveredAt);
        }
    }

    // Every cue under the pointer's mark, with its moment, in a box under it: what a crowded lane's labels leave out.
    private void DrawCueList(UiPainter painter, CutsceneCue[] cues, UiVec2 at)
    {
        const int MostShown = 12;
        List<string> lines = [.. cues.Take(MostShown).Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.Seconds,6:0.00} s   {CueName(c)}"))];
        if (cues.Length > MostShown)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"and {cues.Length - MostShown} more"));
        }

        if (cues.Length > 1)
        {
            lines.Add("Click again to pick the next one");
        }

        float width = lines.Max(l => Theme.Font.MeasureWidth(l, Theme.TextSize)) + Theme.Px(16f);
        float height = (lines.Count * Theme.RowHeight) + Theme.Px(8f);
        float x = Math.Clamp(at.X - (width * 0.5f), Bounds.X + Theme.Px(4f), Bounds.Right - width - Theme.Px(4f));
        float y = at.Y + height > Bounds.Bottom ? at.Y - Theme.Px(LaneHeight) - height : at.Y;
        UiRect box = new(x, y, width, height);
        EditorDraw.Frame(painter, Theme, box, Theme.Chrome, Theme.Border);
        for (int i = 0; i < lines.Count; i++)
        {
            bool hint = cues.Length > 1 && i == lines.Count - 1;
            EditorDraw.Label(painter, Theme, lines[i], new UiRect(x + Theme.Px(8f), y + Theme.Px(4f) + (i * Theme.RowHeight), width, Theme.RowHeight), hint ? Theme.TextMuted : Theme.Text);
        }
    }

    // A lane's cues in order along it -- a cue being dragged where it is being dragged -- and the marks they are laid out as.
    private (CutsceneCue[] Ordered, IReadOnlyList<CueLaneLayout.Mark> Marks) LaneMarks(CutsceneCue[] cues)
    {
        double At(CutsceneCue cue) => _moving is { } m && ReferenceEquals(m.Cue.Task, cue.Task) && m.Cue.Parameter == cue.Parameter ? m.Seconds : cue.Seconds;
        CutsceneCue[] ordered = [.. cues.OrderBy(At)];
        IReadOnlyList<CueLaneLayout.Mark> marks = CueLaneLayout.Lay(
            [.. ordered.Select(c => (X(At(c)), CueName(c)))],
            (text, room) => EditorDraw.Fit(Theme.Font, text, Theme.TextSize, room),
            Theme.Px(CueMerge),
            Theme.Px(CueLabelGap),
            Theme.Px(CueShortestLabel),
            Timeline.Right);
        return (ordered, marks);
    }

    // An animation named by what it moves, else by its own name.
    private static string AnimationName(MissionDocument document, CutsceneAnimation animation)
    {
        string moves = animation.TargetId >= 0 && document.EnumerateTasks().FirstOrDefault(t => t.Id == animation.TargetId) is { } target
            ? $"{TaskPresentation.Name(target)} ({target.TypeName} {target.Id})"
            : animation.Task.Label.Length > 0 ? animation.Task.Label : "Animation";
        return string.Create(CultureInfo.InvariantCulture, $"{moves}  {animation.Length:0.#}s");
    }

    private void DrawLaneLabel(UiPainter painter, string title, float y)
    {
        painter.FillRect(new UiRect(Bounds.X, y, Bounds.Width, 1f), Theme.Border);
        EditorDraw.Label(painter, Theme, title, new UiRect(Bounds.X + Theme.Px(8f), y + Theme.Px(4f), Theme.Px(LabelWidth - 12f), Theme.RowHeight), Theme.TextMuted);
    }

    private string CueName(CutsceneCue cue)
    {
        if (cue.Task.TypeName == "StatusMessage" && cue.Task.FindParameter("Text") is { Arguments: [Scripting.Qsc.QscStringLiteral { Value: { } key }] }
            && _host.MissionText(key) is { Length: > 0 } words)
        {
            return words.Length > 40 ? $"“{words[..40]}…”" : $"“{words}”";
        }

        // What a link does, said plainly: the mission's end, the next scene played.
        if (cue.Task.TypeName == "LevelFlow" && cue.Parameter == "Complete")
        {
            return "Mission complete";
        }

        if (cue.Task.TypeName == CutsceneModel.SceneType && cue.Parameter == "Run")
        {
            return $"Plays {Title(cue.Task)}";
        }

        // An unnamed sound by what it plays (dialogue's _cut02_03), anything else unnamed by its type and id.
        return cue.Task.Label.Length > 0 ? cue.Task.Label
            : CutsceneModel.SoundOf(cue.Task) is { } sound ? sound
            : string.Create(CultureInfo.InvariantCulture, $"{cue.Task.TypeName} {cue.Task.Id}");
    }

    // A lane's cues: the scene's links for the Links lane, else the cues it holds -- animation cues only where no clip shows them.
    private static CutsceneCue[] LaneCues(MissionDocument document, MissionTask scene, IReadOnlyList<CutsceneCue> cues, IReadOnlyList<CutsceneAnimation> animations, string title) =>
        title == LinksLane
            ? [.. CutsceneModel.Links(document, scene)]
            : [.. cues.Where(c => LaneOf(c) == title && (title != "Animation" || animations.Count == 0))];

    private static string LaneOf(CutsceneCue cue)
    {
        foreach ((string title, Func<MissionTask, bool> holds) in CueLanes)
        {
            if (holds(cue.Task))
            {
                return title;
            }
        }

        return "Other";
    }
}
