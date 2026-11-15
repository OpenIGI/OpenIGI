using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Editor.Theme;

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
            string count = move.Keys.Count > 1 ? string.Create(CultureInfo.InvariantCulture, $"  ·  {move.Keys.Count} keys") : string.Empty;
            string name = string.Create(CultureInfo.InvariantCulture, $"{first + 1}. {(lead.Label.Length > 0 ? lead.Label : "Shot")}  {end - start:0.#}s{count}{rides}");
            EditorDraw.Label(painter, Theme, EditorDraw.Fit(Theme.Font, name, Theme.TextSize, clip.Width - Theme.Px(8f)), clip with { X = clip.X + Theme.Px(4f), Width = clip.Width - Theme.Px(6f), Height = clip.Height - Theme.Px(7f) }, Bgra32.White);

            // The keys along the clip's foot: where each starts, the last of a move that ends on a glide's last key at its moment.
            for (int i = first; i <= last; i++)
            {
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
            Seek(keys[nearest].Start);
            if (nearest > 0)
            {
                _keyDrag = (key, keys[nearest].Start, shift);
            }

            return true;
        }

        double at = Seconds(pointer.X);
        if (keys.LastOrDefault(k => k.Start <= at && at < k.Start + Math.Max(k.Length, 1e-3)) is { Key: { } inside })
        {
            _workspace.Selection.Set(inside.Camera);
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
}
