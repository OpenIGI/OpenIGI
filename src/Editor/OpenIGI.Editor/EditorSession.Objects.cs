using OpenIGI.Editor.Core.Missions;
using OpenIGI.Core.Numerics;
using OpenIGI.UI.Editor.Actions;
using OpenIGI.UI.Editor.Widgets;

namespace OpenIGI.Editor;

/// <summary>Making objects and renaming them.</summary>
internal sealed partial class EditorSession
{
    private void RegisterObjectActions(ActionTable actions)
    {
        actions.Add(new EditorAction("edit.rename", "Rename", () =>
        {
            if (_workspace.Document is { } document && _workspace.Selection.Primary is { } task)
            {
                _outliner.BeginRename(task, label => _workspace.Execute(new SetIdentityCommand(document, task, label, task.Id)));
            }
        })
        {
            Category = "Edit",
            Shortcut = KeyChord.Parse("F2"),
            CanExecute = () => _workspace.Selection.Count == 1,
        });
        _outliner.CreateMenu = CreateMenu;
        _viewport.ModelDropped += PlaceModel;
    }

    // A model dragged from the content browser: a task of the type this mission places most models with, under the same
    // parent as the last of them, with its origin where the viewport stood it.
    private void PlaceModel(Panels.ContentNode file, Vector3d at)
    {
        if (_workspace.Document is not { } document)
        {
            return;
        }

        MissionTask? like = document.EnumerateTasks()
            .Where(static t => t.FindParameter(Game.World.WorldScene.ModelParameter) is not null && TaskPresentation.Position(t) is not null)
            .GroupBy(static t => t.TypeName)
            .OrderByDescending(static g => g.Count())
            .Select(static g => g.Last())
            .FirstOrDefault();
        if (like is null || !document.Declarations.ContainsKey(like.TypeName))
        {
            Notify("This mission places no models to copy the kind of object from.", isError: true);
            return;
        }

        string model = Path.GetFileNameWithoutExtension(file.Name);
        MissionTask task = new(like.TypeName, model);
        document.ApplyDeclaration(task);
        if (task.FindParameter(Game.World.WorldScene.ModelParameter) is { } named)
        {
            named.Arguments.Clear();
            named.Arguments.Add(new Scripting.Qsc.QscStringLiteral(model));
        }

        // The viewport gives where the origin goes, the model's base already stood on the ground under its footprint.
        TaskPresentation.Position(task)!.SetNumbers([WorldUnits.FromMetres(at.X), WorldUnits.FromMetres(at.Y), WorldUnits.FromMetres(at.Z)]);
        TaskLocation location = like.Parent is { } parent ? new TaskLocation(parent, parent.Children.Count) : new TaskLocation(null, document.Statements.Count);
        _workspace.Execute(new InsertTaskCommand(document, task, location));
        _workspace.Selection.Set([task]);
        _outliner.Reveal(task);
        if (!_live.IsLive(task))
        {
            Notify($"Placed {model}. Nothing in this level draws it yet: Save and Reload (Ctrl+R) to see it.");
        }
    }

    // One entry per type the mission declares, as only those can be written into it.
    private IEnumerable<MenuItem> CreateMenu(MissionTask? parent)
    {
        if (_workspace.Document is not { } document || document.Declarations.Count == 0)
        {
            return [MenuItem.Command("(this mission declares no types)", static () => { })];
        }

        return document.Declarations.Keys
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(type => MenuItem.Command(type, () => Create(type, parent)));
    }

    private void Create(string type, MissionTask? parent)
    {
        if (_workspace.Document is not { } document)
        {
            return;
        }

        MissionTask task = new(type, type);
        document.ApplyDeclaration(task);

        // Where the view is looking, so the new object is in front of the person who made it.
        if (TaskPresentation.Position(task) is { } position && _viewport.PlacementPoint() is { } at)
        {
            position.SetNumbers([WorldUnits.FromMetres(at.X), WorldUnits.FromMetres(at.Y), WorldUnits.FromMetres(at.Z)]);
        }

        TaskLocation location = parent is null
            ? new TaskLocation(null, document.Statements.Count)
            : new TaskLocation(parent, parent.Children.Count);
        _workspace.Execute(new InsertTaskCommand(document, task, location));
        _workspace.Selection.Set([task]);
        _outliner.Reveal(task);
        if (!_live.IsLive(task) && TaskPresentation.Position(task) is not null)
        {
            Notify($"Made {type}. Give it a model, then Save and Reload (Ctrl+R) to see it in the world.");
        }
    }
}
