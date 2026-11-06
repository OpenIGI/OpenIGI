using OpenIGI.Editor.Core.Missions;
using OpenIGI.UI.Editor.Actions;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;

namespace OpenIGI.Editor;

/// <summary>The command palette: every command, and every object to go to, behind one search box.</summary>
internal sealed partial class EditorSession
{
    private void RegisterShellActions(ActionTable actions)
    {
        actions.Add(new EditorAction("edit.preferences", "Preferences", () => _dock.Show("preferences", beside: "inspector"))
        {
            Category = "Edit",
            Shortcut = KeyChord.Parse("Ctrl+Comma"),
            Icon = nameof(KitIcon.Gear),
        });
        actions.Add(new EditorAction("view.palette", "Command Palette", () => OpenPalette(commands: true))
        {
            Category = "View",
            Shortcut = KeyChord.Parse("Ctrl+Shift+P"),
        });
        actions.Add(new EditorAction("view.goto", "Go to Object", () => OpenPalette(commands: false))
        {
            Category = "View",
            Shortcut = KeyChord.Parse("Ctrl+P"),
            CanExecute = () => _workspace.Document is not null,
        });
    }

    private void OpenPalette(bool commands)
    {
        List<PaletteEntry> entries = [];
        if (commands)
        {
            entries.AddRange(_root.Actions.All
                .Where(static a => a.Id is not "view.palette" and not "edit.redo-alt")
                .OrderBy(static a => a.Category, StringComparer.Ordinal)
                .ThenBy(static a => a.CurrentTitle, StringComparer.Ordinal)
                .Select(action => new PaletteEntry(action.CurrentTitle, () => action.TryExecute())
                {
                    Detail = action.Category,
                    Shortcut = action.Shortcut?.ToString(),
                    Icon = Enum.TryParse(action.Icon, out KitIcon icon) ? icon : KitIcon.None,
                    IsEnabled = action.IsEnabled,
                }));
        }

        // Objects to go to: selecting one shows it in the outliner and frames it in the view.
        if (_workspace.Document is { } document)
        {
            entries.AddRange(document.EnumerateTasks().Select(task => new PaletteEntry(TaskPresentation.Name(task), () => GoTo(task))
            {
                Detail = task.TypeName,
                Icon = TaskPresentation.Icon(task),
            }));
        }

        CommandPalette.Open(_root, entries, commands ? "Type a command or an object's name" : "Type an object's name");
    }

    private void GoTo(MissionTask task)
    {
        _workspace.Selection.Set(task);
        _outliner.Reveal(task);
        _viewport.Frame(task);
    }
}
