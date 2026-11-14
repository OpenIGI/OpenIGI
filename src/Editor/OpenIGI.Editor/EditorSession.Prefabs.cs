using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Core.Prefabs;
using OpenIGI.Core.Configuration;
using OpenIGI.UI.Editor.Actions;
using OpenIGI.UI.Editor.Widgets;

namespace OpenIGI.Editor;

/// <summary>
/// Prefabs: the selection kept in the library with everything under it and each guard's orders, and placed again where the
/// viewport looks, in this mission or another of the game, as new tasks that still find each other.
/// </summary>
internal sealed partial class EditorSession
{
    private static PrefabLibrary Prefabs => new(Path.Combine(SettingsPaths.GetUserDirectory(), "editor", "prefabs"));

    private void RegisterPrefabActions(ActionTable actions)
    {
        actions.Add(new EditorAction("tools.saveprefab", "Save Selection as Prefab…", SavePrefab)
        {
            Category = "Tools",
            CanExecute = () => _workspace.Document is not null && _workspace.Selection.Count > 0,
        });
        actions.Add(new EditorAction("tools.placeprefab", "Place Prefab…", OfferPrefabs)
        {
            Category = "Tools",
            CanExecute = () => _workspace.Document is not null,
        });
    }

    // The selection named and kept.
    private void SavePrefab()
    {
        if (_workspace.Document is null || _workspace.Selection.Primary is not { } primary)
        {
            return;
        }

        TextField name = new(_theme) { Text = primary.Label, Placeholder = "What to call it" };
        Dialog? dialog = null;
        Wizard ask = new(
            _theme,
            [new WizardPage("Name", "Save as Prefab", "Kept with everything under it and each guard's orders, to place again in any mission of this game.", name)],
            "Save",
            () => KeepPrefab(name.Text.Trim()));
        ask.Finished += () => dialog?.Close();
        dialog = Dialog.Open(_root, "Save as Prefab", ask, 520f, 260f);
    }

    private bool KeepPrefab(string name)
    {
        if (name.Length == 0 || _workspace.Document is not { } document)
        {
            return false;
        }

        if (Prefab.Capture(document, _workspace.Selection.Items, name, _host.Title.Id, _guards.Current) is not { } prefab)
        {
            return false;
        }

        Prefabs.Save(prefab);
        Notify(string.Create(CultureInfo.InvariantCulture, $"Kept \"{name}\" ({prefab.Description}). Tools > Place Prefab puts it where the view looks."));
        return true;
    }

    // The game's prefabs to choose from, by shelf.
    private void OfferPrefabs()
    {
        IReadOnlyList<Prefab> kept = Prefabs.List(_host.Title.Id);
        if (kept.Count == 0)
        {
            Notify("No prefabs yet: select something and use Tools > Save Selection as Prefab.");
            return;
        }

        CommandPalette.Open(_root, [.. kept.Select(p => new PaletteEntry(p.Name, () => PlacePrefab(p)) { Detail = $"{p.Category}  ·  {p.Description}" })], "Place a prefab where the view looks");
    }

    // A prefab put in where the view looks, in a container of its name, as one step; each guard's mind written with it.
    private void PlacePrefab(Prefab prefab)
    {
        if (_workspace.Document is not { } document || _viewport.PointAtCentre() is not { } at)
        {
            return;
        }

        PlacedPrefab placed;
        try
        {
            placed = prefab.Place(document, (WorldUnits.FromMetres(at.X), WorldUnits.FromMetres(at.Y), WorldUnits.FromMetres(at.Z)));
        }
        catch (Scripting.Qsc.QscParseException error)
        {
            Notify($"\"{prefab.Name}\" does not read: {error.Message}", isError: true);
            return;
        }

        if (placed.Tasks.Count == 0)
        {
            Notify($"This mission has none of what \"{prefab.Name}\" holds ({string.Join(", ", placed.Skipped.Distinct())}).", isError: true);
            return;
        }

        MissionTask holder = new(TaskCatalogue.ContainerType, prefab.Name);
        foreach (MissionTask task in placed.Tasks)
        {
            holder.AddChild(task);
        }

        // Beside the mission's own of the same kind (its guards with its guards), else last in the mission.
        string kind = placed.Tasks[0].TypeName;
        MissionTask? parent = document.EnumerateTasks().LastOrDefault(t => t.TypeName == kind && t.Parent is not null)?.Parent ?? document.RootTasks.LastOrDefault();
        _workspace.Execute(new InsertTaskCommand(document, holder, new TaskLocation(parent, parent?.Children.Count ?? 0)));

        // Each guard's mind goes with the mission when it is saved, so an undone or unsaved placing leaves no file behind.
        foreach ((int id, byte[] script) in placed.Minds)
        {
            _guards.Adopt(id, script);
        }

        _workspace.Selection.Set(holder);
        Notify(placed.Skipped.Count > 0
            ? $"Placed \"{prefab.Name}\"; left out what this mission does not declare ({string.Join(", ", placed.Skipped.Distinct())})."
            : $"Placed \"{prefab.Name}\".");
    }
}
