using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// The camera lane as moves with their keys, as Unreal's Sequencer draws camera cuts with transform keys on them: one clip a move
/// (keys gliding one into the next, ended by a cut), a diamond a key -- a key that only ends a glide is a diamond, not a sliver to
/// miss. A diamond dragged retimes its key (the key before gives or takes the time; Shift ripples the rest along); a move's end
/// dragged stretches all its keys; a click in a move picks the key there and puts the playhead where it was clicked.
/// </summary>
internal sealed partial class SequencerPanel
{
    private const float KeyReach = 7f;

    // A key being dragged to another moment, and whether the keys after it go along.
    private (MissionTask Key, double Seconds, bool Ripple)? _keyDrag;

    // A move whose end is being dragged, and the length it is being stretched to.
    private (CutsceneMove Move, double Length)? _moveStretch;

    // A view copied from a key, to paste into another: where it stands and how it is turned, as written.
    private (double X, double Y, double Z, double Alpha, double Beta, double Gamma, double Lens)? _copiedView;

    // The scene's keys where the timeline shows them now: a drag or a stretch going on shown as it would leave them.
    private List<(CutsceneShot Key, double Start, double Length)> ShownKeys(MissionTask scene)
    {
        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        double[] lengths = [.. shots.Select(static s => s.Length)];
        if (_keyDrag is { } drag && IndexIn(shots, drag.Key) is > 0 and int index)
        {
            double end = shots[index].Start + shots[index].Length;
            double at = Math.Clamp(drag.Seconds, shots[index - 1].Start, drag.Ripple ? double.MaxValue : end);
            lengths[index - 1] = at - shots[index - 1].Start;
            if (!drag.Ripple)
            {
                lengths[index] = end - at;
            }
        }

        if (_moveStretch is { } stretch && stretch.Move.Length > 0)
        {
            double factor = stretch.Length / stretch.Move.Length;
            foreach (CutsceneShot key in stretch.Move.Keys)
            {
                lengths[IndexIn(shots, key.Camera)] = key.Length * factor;
            }
        }

        List<(CutsceneShot Key, double Start, double Length)> shown = new(shots.Count);
        double start = 0;
        for (int i = 0; i < shots.Count; i++)
        {
            shown.Add((shots[i], start, lengths[i]));
            start += lengths[i];
        }

        return shown;
    }

    // The camera lane: a clip a move, its keys as diamonds along its foot.
    private void DrawCameraLane(UiPainter painter, MissionTask scene, float laneY, MissionTask? selected)
    {
        List<(CutsceneShot Key, double Start, double Length)> keys = ShownKeys(scene);
        foreach (CutsceneMove move in CutsceneMoves.Of(scene))
        {
            int first = IndexIn(keys, move.Keys[0].Camera);
            int last = first + move.Keys.Count - 1;
            double start = keys[first].Start;
            double end = keys[last].Start + keys[last].Length;
            UiRect clip = new(X(start) + 1f, laneY + 2f, MathF.Max(Theme.Px(4f), (float)((end - start) * _pixelsPerSecond) - 2f), Theme.Px(LaneHeight) - 4f);
            bool holdsSelected = move.Keys.Any(k => ReferenceEquals(k.Camera, selected));
            EditorDraw.Frame(painter, Theme, clip, (holdsSelected ? Theme.Accent : Bgra32.FromRgb(0x3E, 0x6E, 0xA8)).WithAlpha(holdsSelected ? (byte)160 : (byte)170), holdsSelected ? Theme.Accent : Theme.Border);

            MissionTask lead = move.Keys[0].Camera;
            string rides = Number(lead, "Link task ID", -1) >= 0 ? "  (rides)" : Number(lead, "Target task ID", -1) >= 0 ? "  (watches)" : string.Empty;
            int own = move.Keys.Count(static k => !CutsceneEasing.IsHelper(k.Camera));
            string count = own > 1 ? string.Create(CultureInfo.InvariantCulture, $"  ·  {own} keys") : string.Empty;
            int number = 1 + keys.Take(first).Count(static k => !CutsceneEasing.IsHelper(k.Key.Camera));
            string eased = move.Keys.Select(k => CutsceneEasing.Of(scene, k.Camera)).FirstOrDefault(static e => e != CutsceneEase.None) is var e and not CutsceneEase.None ? "  ·  " + EaseName(e) : string.Empty;
            string name = string.Create(CultureInfo.InvariantCulture, $"{number}. {(lead.Label.Length > 0 ? lead.Label : "Shot")}  {end - start:0.#}s{count}{rides}{eased}");
            EditorDraw.Label(painter, Theme, EditorDraw.Fit(Theme.Font, name, Theme.TextSize, clip.Width - Theme.Px(8f)), clip with { X = clip.X + Theme.Px(4f), Width = clip.Width - Theme.Px(6f), Height = clip.Height - Theme.Px(7f) }, Bgra32.White);

            // The keys along the clip's foot: where each starts, the last of a move that ends on a glide's last key at its moment.
            for (int i = first; i <= last; i++)
            {
                if (CutsceneEasing.IsHelper(keys[i].Key.Camera))
                {
                    continue;
                }

                DrawEase(painter, scene, keys, i, clip.Bottom - Theme.Px(5f));
                bool isSelected = ReferenceEquals(keys[i].Key.Camera, selected);
                UiVec2 at = new(X(keys[i].Start), clip.Bottom - Theme.Px(5f));
                painter.FillDiamond(at, Theme.Px(isSelected ? 6f : 4.5f), Theme.Background);
                painter.FillDiamond(at, Theme.Px(isSelected ? 5f : 3.5f), isSelected ? Theme.Accent : Bgra32.White);
            }
        }
    }

    // A press on the camera lane: on a key's diamond (it is selected, and taken hold of to retime), on a move's end (taken hold of
    // to stretch), or in a move (the key there is selected and the playhead put where the press was).
    private bool PressCameraLane(MissionTask scene, UiVec2 pointer, bool shift)
    {
        List<(CutsceneShot Key, double Start, double Length)> keys = ShownKeys(scene);
        int nearest = -1;
        float closest = Theme.Px(KeyReach);
        for (int i = 0; i < keys.Count; i++)
        {
            if (CutsceneEasing.IsHelper(keys[i].Key.Camera))
            {
                continue;
            }

            float distance = MathF.Abs(X(keys[i].Start) - pointer.X);
            if (distance <= closest)
            {
                (nearest, closest) = (i, distance);
            }
        }

        foreach (CutsceneMove move in CutsceneMoves.Of(scene))
        {
            if (MathF.Abs(X(move.End) - pointer.X) <= Theme.Px(EdgeGrip) && nearest < 0)
            {
                _moveStretch = (move, move.Length);
                return true;
            }
        }

        if (nearest >= 0)
        {
            MissionTask key = keys[nearest].Key.Camera;
            _workspace.Selection.Set(key);
            Seek(Into(keys[nearest].Start, keys[nearest].Length));
            if (nearest > 0)
            {
                _keyDrag = (key, keys[nearest].Start, shift);
            }

            return true;
        }

        double at = Seconds(pointer.X);
        if (keys.LastOrDefault(k => k.Start <= at && at < k.Start + Math.Max(k.Length, 1e-3)) is { Key: { } inside })
        {
            _workspace.Selection.Set(CutsceneEasing.OwnerOf(scene, inside.Camera));
            Seek(at);
            return true;
        }

        return false;
    }

    // A drag on the camera lane: the key held or the move's end, where the pointer is (snapped to tenths, Shift to frames).
    private bool DragCameraLane(float pointerX, bool shift)
    {
        if (_keyDrag is { } drag)
        {
            _keyDrag = (drag.Key, Snap(Seconds(pointerX), shift), drag.Ripple);
            return true;
        }

        if (_moveStretch is { } stretch)
        {
            _moveStretch = (stretch.Move, Math.Max(0.1, Snap(Seconds(pointerX), shift) - stretch.Move.Start));
            return true;
        }

        return false;
    }

    // The drag let go: the key retimed, or the move's keys stretched, as one step.
    private void ReleaseCameraLane(MissionDocument document, MissionTask scene)
    {
        if (_keyDrag is { } drag && CutsceneModel.Shots(scene).FirstOrDefault(s => ReferenceEquals(s.Camera, drag.Key)) is { } held
            && Math.Abs(held.Start - drag.Seconds) > 1e-6 && CutsceneMoves.Retime(document, scene, drag.Key, drag.Seconds, drag.Ripple) is { } retime)
        {
            _workspace.Execute(retime);
        }

        if (_moveStretch is { } stretch && Math.Abs(stretch.Length - stretch.Move.Length) > 1e-6 && stretch.Move.Length > 0)
        {
            double factor = stretch.Length / stretch.Move.Length;
            _workspace.Execute(new CompositeCommand("Stretch Move", [.. stretch.Move.Keys
                .Select(k => CutsceneModel.SetLength(document, scene, k.Camera, k.Length * factor))]));
        }

        _keyDrag = null;
        _moveStretch = null;
    }

    /// <summary>Adds a key where the path is at the playhead, as Unreal keys the camera at the current time.</summary>
    public void AddKeyAtPlayhead()
    {
        if (_scene is not { } scene || _workspace.Document is not { } document)
        {
            return;
        }

        if (CutsceneMoves.AddKey(document, scene, _time) is not { } added)
        {
            Notify?.Invoke("The playhead is not inside a key that runs: put it inside a shot.", true);
            return;
        }

        _workspace.Execute(added.Step);
        _workspace.Selection.Set(added.Key);
    }

    // The selected key taken out, the key before taking its time so every later key keeps its moment.
    private void DeleteShot()
    {
        if (SelectedShot() is { } shot && _scene is { } scene && _workspace.Document is { } document)
        {
            StopPilot();
            _workspace.Execute(CutsceneMoves.RemoveKey(document, scene, shot.Camera));
            _workspace.Selection.Clear();
        }
    }

    // A key's view copied, to paste into another.
    private void CopyView(MissionTask key)
    {
        CutsceneKeyValues values = CutsceneKeyValues.Of(key);
        _copiedView = (values.X, values.Y, values.Z, values.Alpha, values.Beta, values.Gamma, Number(key, "FOV", 1));
        Notify?.Invoke("Copied the key's view.", false);
    }

    // The view copied written into a key: where it stands, how it is turned, its lens.
    private void PasteView(MissionDocument document, MissionTask key)
    {
        if (_copiedView is not { } view)
        {
            return;
        }

        _workspace.Execute(new CompositeCommand("Paste View", [
            CutsceneModel.SetView(document, key, (view.X, view.Y, view.Z), (view.Alpha, view.Beta, view.Gamma)),
            SetParameterCommand.Numbers(document, key, "FOV", [view.Lens]),
        ]));
    }

    // A copy of a key put in after it, taking half its time.
    private void DuplicateKey(MissionDocument document, MissionTask scene, CutsceneShot key)
    {
        MissionTask copy = key.Camera.Clone();
        copy.Label = key.Camera.Label;
        double half = key.Length * 0.5;
        TaskLocation at = document.Locate(key.Camera) with { Index = document.Locate(key.Camera).Index + 1 };
        _workspace.Execute(new CompositeCommand("Duplicate Key", [
            CutsceneModel.SetLength(document, scene, key.Camera, half),
            CutsceneModel.SetLength(document, scene, copy, key.Length - half),
            new InsertTaskCommand(document, copy, at),
        ]));
        _workspace.Selection.Set(copy);
    }

    private static int IndexIn(IReadOnlyList<CutsceneShot> shots, MissionTask key)
    {
        for (int i = 0; i < shots.Count; i++)
        {
            if (ReferenceEquals(shots[i].Camera, key))
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexIn(List<(CutsceneShot Key, double Start, double Length)> keys, MissionTask key)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            if (ReferenceEquals(keys[i].Key.Camera, key))
            {
                return i;
            }
        }

        return -1;
    }

    // A key's place and turn as written.
    private readonly record struct CutsceneKeyValues(double X, double Y, double Z, double Alpha, double Beta, double Gamma)
    {
        public static CutsceneKeyValues Of(MissionTask key)
        {
            double[] at = key.FindParameter("Position") is { } position && position.TryGetNumbers(out double[] values) && values.Length == 3 ? values : [0, 0, 0];
            return new CutsceneKeyValues(at[0], at[1], at[2], Number(key, "Alpha", 0), Number(key, "Beta", 0), Number(key, "Gamma", 0));
        }
    }

    // A key's ease, from it to the next key of its own: a band along the clip's foot (its name is in the clip's), the helpers it stands for not drawn.
    private void DrawEase(UiPainter painter, MissionTask scene, List<(CutsceneShot Key, double Start, double Length)> keys, int index, float y)
    {
        if (CutsceneEasing.Of(scene, keys[index].Key.Camera) == CutsceneEase.None)
        {
            return;
        }

        int next = index + 1;
        while (next < keys.Count && CutsceneEasing.IsHelper(keys[next].Key.Camera))
        {
            next++;
        }

        double end = next < keys.Count ? keys[next].Start : keys[^1].Start + keys[^1].Length;
        float from = X(keys[index].Start), to = X(end);
        painter.FillRect(new UiRect(from, y - Theme.Px(1f), MathF.Max(0, to - from), Theme.Px(2f)), Bgra32.FromRgb(0xE8, 0xB0, 0x30).WithAlpha(200));
    }

    private static string EaseName(CutsceneEase ease) => ease switch { CutsceneEase.In => "ease in", CutsceneEase.Out => "ease out", CutsceneEase.InOut => "ease in-out", _ => "even speed" };

    // The menu's ease entries for a key that glides on.
    private List<PaletteEntry> EaseEntries(MissionDocument document, MissionTask scene, MissionTask key)
    {
        if (!CutsceneMoves.Glides(key))
        {
            return [];
        }

        CutsceneEase now = CutsceneEasing.Of(scene, key);
        List<PaletteEntry> entries = [];
        foreach ((CutsceneEase ease, string name, string detail) in ((CutsceneEase, string, string)[])[
            (CutsceneEase.Out, "Ease Out", "slow into the next key, coming to rest there"),
            (CutsceneEase.In, "Ease In", "start from rest, gathering speed"),
            (CutsceneEase.InOut, "Ease In and Out", "start from rest and slow into the next key"),
            (CutsceneEase.Even, "Even Speed", "the same way at one speed throughout"),
            (CutsceneEase.None, "Remove Ease", "the game's own curve again, the key as it was written"),
        ])
        {
            if (ease != now && (ease != CutsceneEase.None || now != CutsceneEase.None))
            {
                entries.Add(new PaletteEntry(name, () => SetEase(document, scene, key, ease)) { Detail = detail + " (baked into keys the game reads)" });
            }
        }

        return entries;
    }

    /// <summary>Eases the open scene's nth key of its own (from nought), as its menu's Ease entries do.</summary>
    /// <param name="index">The key's index among the keys of their own.</param>
    /// <param name="ease">The ease.</param>
    public void EaseKey(int index, CutsceneEase ease)
    {
        if (_scene is { } scene && _workspace.Document is { } document
            && CutsceneEasing.OwnKeys(scene).ElementAtOrDefault(index)?.Camera is { } key)
        {
            _workspace.Selection.Set(key);
            SetEase(document, scene, key, ease);
        }
    }

    private void SetEase(MissionDocument document, MissionTask scene, MissionTask key, CutsceneEase ease)
    {
        if (CutsceneEasing.Set(document, scene, key, ease) is { } step)
        {
            _workspace.Execute(step);
        }
        else
        {
            Notify?.Invoke("That key has no next key to ease into", true);
        }
    }
}
