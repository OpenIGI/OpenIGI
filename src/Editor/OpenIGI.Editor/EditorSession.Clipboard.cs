using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.UI.Editor.Actions;

namespace OpenIGI.Editor;

/// <summary>Cut, copy and paste of objects, as the script text that makes them, through the system clipboard.</summary>
internal sealed partial class EditorSession
{
    private void RegisterClipboardActions(ActionTable actions)
    {
        actions.Add(new EditorAction("edit.copy", "Copy", () => CopySelection(cut: false))
        {
            Category = "Edit",
            Shortcut = KeyChord.Parse("Ctrl+C"),
            CanExecute = () => _workspace.Selection.Count > 0,
        });
        actions.Add(new EditorAction("edit.cut", "Cut", () => CopySelection(cut: true))
        {
            Category = "Edit",
            Shortcut = KeyChord.Parse("Ctrl+X"),
            CanExecute = () => _workspace.Selection.Count > 0,
        });
        actions.Add(new EditorAction("edit.paste", "Paste", () => Paste(asChild: false))
        {
            Category = "Edit",
            Shortcut = KeyChord.Parse("Ctrl+V"),
            CanExecute = () => _workspace.Document is not null,
        });
        actions.Add(new EditorAction("edit.paste-child", "Paste As Child", () => Paste(asChild: true))
        {
            Category = "Edit",
            Shortcut = KeyChord.Parse("Ctrl+Shift+V"),
            CanExecute = () => _workspace.Document is not null && _workspace.Selection.Count > 0,
        });
    }

    private void CopySelection(bool cut)
    {
        if (_workspace.Document is not { } document || _workspace.Selection.Count == 0)
        {
            return;
        }

        IReadOnlyList<MissionTask> selected = [.. _workspace.Selection.Items];
        MissionTask[] tops = [.. selected.Where(task => !selected.Any(other => !ReferenceEquals(other, task) && IsUnder(task, other)))];
        _host.ClipboardText = document.WriteTasks(tops);
        if (cut)
        {
            _outliner.Delete(tops);
        }

        Notify(tops.Length == 1 ? $"{(cut ? "Cut" : "Copied")} {TaskPresentation.Name(tops[0])}." : $"{(cut ? "Cut" : "Copied")} {tops.Length} objects.");
    }

    private void Paste(bool asChild)
    {
        if (_workspace.Document is not { } document)
        {
            return;
        }

        PastedTasks pasted;
        try
        {
            pasted = document.ReadTasks(_host.ClipboardText ?? string.Empty);
        }
        catch (QscParseException)
        {
            Notify("The clipboard does not hold objects.");
            return;
        }

        if (pasted.Tasks.Count == 0)
        {
            Notify(pasted.Skipped.Count > 0
                ? $"This mission has no {string.Join(", ", pasted.Skipped.Distinct())} to paste."
                : "The clipboard does not hold objects.");
            return;
        }

        // Beside the selected object, or inside it, or else at the end of the mission.
        TaskLocation at;
        if (_workspace.Selection.Primary is not { } anchor)
        {
            at = new TaskLocation(null, document.Statements.Count);
        }
        else if (asChild)
        {
            at = new TaskLocation(anchor, anchor.Children.Count);
        }
        else
        {
            TaskLocation beside = document.Locate(anchor);
            at = beside with { Index = beside.Index + 1 };
        }
        using (_workspace.History.BeginTransaction(pasted.Tasks.Count == 1 ? $"Paste {TaskPresentation.Name(pasted.Tasks[0])}" : $"Paste {pasted.Tasks.Count} objects"))
        {
            foreach (MissionTask task in pasted.Tasks)
            {
                _workspace.Execute(new InsertTaskCommand(document, task, at));
                at = at with { Index = at.Index + 1 };
            }
        }

        _workspace.Selection.Set(pasted.Tasks);
        if (pasted.Skipped.Count > 0)
        {
            Notify($"Pasted {pasted.Tasks.Count}; left out {pasted.Skipped.Count} this mission does not declare ({string.Join(", ", pasted.Skipped.Distinct())}).");
        }
    }

    private static bool IsUnder(MissionTask task, MissionTask ancestor)
    {
        for (MissionTask? parent = task.Parent; parent is not null; parent = parent.Parent)
        {
            if (ReferenceEquals(parent, ancestor))
            {
                return true;
            }
        }

        return false;
    }
}
