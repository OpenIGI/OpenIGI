using System.Globalization;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Editor.Core.Projects;
using OpenIGI.Host.Editor;
using OpenIGI.Game.World;
using OpenIGI.UI.Editor.Widgets;

namespace OpenIGI.Editor;

/// <summary>
/// Autosave and recovery: while a mission has changes not saved, a copy of its objects every few minutes
/// (<c>ed_autosave_minutes</c>), kept apart from the project so the game never plays it; saving throws the copies away, and a
/// mission opened with a copy newer than its save offers to bring that work back.
/// </summary>
internal sealed partial class EditorSession
{
    private double _sinceAutosave;
    private int _autosavedHash;
    private MissionDescriptor? _recoveryCheckedFor;

    private MissionAutosave Autosaves => new(Path.Combine(SettingsPaths.GetUserDirectory(), "editor", "autosave"), _host.Title.Id);

    private void StartAutosave() => _workspace.Saving += () =>
    {
        if (_workspace.Mission is { } mission)
        {
            Autosaves.Clear(MissionWorkspace.ObjectsGamePath(mission));
            _autosavedHash = 0;
        }
    };

    // A copy when the interval has passed and the mission has changed since the last; the offer to recover once a mission opens.
    private void Autosave(float elapsedSeconds)
    {
        if (_workspace.Document is not { } document || _workspace.Mission is not { } mission || _host.IsWarmingUp)
        {
            return;
        }

        if (!ReferenceEquals(_recoveryCheckedFor, mission))
        {
            _recoveryCheckedFor = mission;
            _sinceAutosave = 0;
            _autosavedHash = 0;
            OfferRecovery(mission);
            return;
        }

        double interval = EditorCVars.AutosaveMinutes.Value * 60.0;
        _sinceAutosave += elapsedSeconds;
        if (interval <= 0 || _sinceAutosave < interval || !_workspace.History.IsDirty)
        {
            return;
        }

        _sinceAutosave = 0;
        try
        {
            byte[] image = document.ToImage();
            int hash = HashOf(image);
            if (hash != _autosavedHash)
            {
                Autosaves.Write(MissionWorkspace.ObjectsGamePath(mission), image, DateTime.UtcNow);
                _autosavedHash = hash;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _host.Logger.Warning("Editor", $"The autosave copy could not be taken: {error.Message}");
        }
    }

    // Work taken after the last save, offered back.
    private void OfferRecovery(MissionDescriptor mission)
    {
        string objects = MissionWorkspace.ObjectsGamePath(mission);
        string saved = _workspace.Project.PathFor(objects);
        DateTime savedUtc = File.Exists(saved) ? File.GetLastWriteTimeUtc(saved) : DateTime.MinValue;
        if (Autosaves.Unsaved(objects, savedUtc) is not { } copy)
        {
            return;
        }

        string when = copy.TakenUtc.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
        Dialog.Ask(
            _root,
            "Recover Unsaved Work",
            $"Work on {_workspace.MissionName} from {when} was never saved.",
            ("Recover", () => Recover(mission, copy.File)),
            ("Discard", () => Autosaves.Clear(objects)),
            ("Not Now", static () => { }));
    }

    private void Recover(MissionDescriptor mission, string file)
    {
        try
        {
            _workspace.WriteToProject(MissionWorkspace.ObjectsGamePath(mission), File.ReadAllBytes(file));
        }
        catch (IOException error)
        {
            Notify($"The copy could not be recovered: {error.Message}", isError: true);
            return;
        }

        _workspace.Reload();
        _host.ReloadMission();
        Notify("Recovered the unsaved work: it is saved into the project and the mission reloaded.");
    }

    private static int HashOf(byte[] bytes)
    {
        HashCode hash = default;
        hash.AddBytes(bytes);
        return hash.ToHashCode();
    }
}
