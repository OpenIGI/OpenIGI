using OpenIGI.Editor.Core.Projects;
using OpenIGI.Editor.Panels;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Game.World;
using OpenIGI.UI.Editor.Actions;

namespace OpenIGI.Editor;

/// <summary>Making new missions.</summary>
internal sealed partial class EditorSession
{
    private const string GameDevice = "LOCAL:";

    // The new-mission wizard, in a dialog in the middle of the editor.
    private void OpenNewMission(MissionTarget target = MissionTarget.Retail, string name = "") =>
        NewMissionWizard.Open(_root, _host, CreateMission, target, name);

    // The open mission's settings: for now what it is made to run in.
    private void OpenMissionSettings()
    {
        if (_workspace.Mission is not { } mission)
        {
            return;
        }

        MissionTarget now = _workspace.Project.TargetOf(mission.Id);
        OptionCards targets = new(_theme)
        {
            Cards =
            [
                new OptionCard("Retail compatible", "Plays in the original game as well as OpenIGI; kept to what the retail game can load."),
                new OptionCard("OpenIGI only", "May use what OpenIGI adds, such as mountain areas; the original game cannot play it."),
            ],
            SelectedIndex = now == MissionTarget.Retail ? 0 : 1,
        };
        Dialog? dialog = null;
        Wizard settings = new(
            _theme,
            [new WizardPage("Target", $"{_workspace.MissionName}", "What the mission is made to run in decides what the editor lets it use.", targets)],
            "Apply",
            () =>
            {
                MissionTarget chosen = targets.SelectedIndex == 1 ? MissionTarget.OpenIgi : MissionTarget.Retail;
                _workspace.Project.SetTarget(mission.Id, chosen);
                Notify(chosen == MissionTarget.Retail
                    ? $"{_workspace.MissionName} is retail compatible: only what the original game loads is offered."
                    : $"{_workspace.MissionName} is OpenIGI only: the original game cannot play it.");
                return true;
            });
        settings.Finished += () => dialog?.Close();
        dialog = Dialog.Open(_root, "Mission Settings", settings, 600f, 360f);
    }

    private void RegisterMissionActions(ActionTable actions)
    {
        actions.Add(new EditorAction("file.missionsettings", "Mission Settings…", OpenMissionSettings)
        {
            Category = "File",
            CanExecute = () => _workspace.Mission is not null,
        });
        actions.Add(new EditorAction("file.newmission", "New Mission…", () => OpenNewMission())
        {
            Category = "File",
            CanExecute = () => _host.Missions.Count > 0,
        });
    }

    // A mission made as a copy of another, saved into the project with its target, found by the game and opened; whether
    // it was made.
    private bool CreateMission(NewMissionRequest request)
    {
        (MissionDescriptor from, string name, int id, bool joinCampaign, MissionTarget target) = request;
        if (_host.Missions.Any(m => m.Id == id))
        {
            Notify(FormattableString.Invariant($"Mission {id} is taken already."), isError: true);
            return false;
        }

        string folder = $"{GameDevice}{from.MissionDirectory.TrimEnd('/')}";
        try
        {
            if (!_host.TryReadShippedFile($"{folder}/{NewMission.ScriptName}", out ReadOnlyMemory<byte> script))
            {
                Notify($"{_host.MissionName(from)} has no {NewMission.ScriptName} to start from.", isError: true);
                return false;
            }

            (string, ReadOnlyMemory<byte>)? previous = null;
            if (joinCampaign && LastInCampaign() is { } last
                && _host.TryReadShippedFile($"{GameDevice}{last.MissionDirectory.TrimEnd('/')}/{NewMission.ScriptName}", out ReadOnlyMemory<byte> lastScript))
            {
                previous = (last.MissionDirectory, lastScript);
            }

            IReadOnlyList<(string GamePath, byte[] Bytes)> files = NewMission.Build(from.MissionDirectory, script, LevelFiles(folder), id, name, previous);
            foreach ((string path, byte[] bytes) in files)
            {
                _workspace.Project.Write(path, bytes);
            }

            _workspace.Project.SetTarget(id, target);
            _host.MountContent(_workspace.Project.LayerId, _workspace.Project.Name, _workspace.Project.FilesDirectory);
            _host.RescanMissions();
            _host.OpenMission(id);
            _host.Logger.Information("Editor", $"Made mission {id} '{name}' from mission {from.Id}: {files.Count} file(s).");
            Notify($"Made mission {id}, '{name}', from {_host.MissionName(from)}. Opening it.");
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            Notify($"Could not make the mission: {error.Message}", isError: true);
            return false;
        }
    }

    // The campaign's last mission: the end of the chain from its first.
    private MissionDescriptor? LastInCampaign()
    {
        Dictionary<int, MissionDescriptor> byId = _host.Missions.ToDictionary(static m => m.Id);
        if (_host.Missions.Count == 0)
        {
            return null;
        }

        MissionDescriptor at = _host.Missions[0];
        HashSet<int> seen = [at.Id];
        while (byId.TryGetValue(at.NextMissionId, out MissionDescriptor? next) && seen.Add(next.Id))
        {
            at = next;
        }

        return at;
    }

    // Every file of a level's folder as the original game would read it (the project's copy, else the installation's; never
    // a remastered replacement the original cannot load), with its path relative to the folder.
    private IEnumerable<(string RelativePath, ReadOnlyMemory<byte> Bytes)> LevelFiles(string folder)
    {
        Stack<string> pending = new([folder]);
        while (pending.TryPop(out string? directory))
        {
            foreach (string child in _host.Files.EnumerateDirectories(directory))
            {
                pending.Push(child.Replace('\\', '/'));
            }

            foreach (string file in _host.Files.EnumerateFiles(directory, "*"))
            {
                string path = file.Replace('\\', '/');
                if (_host.TryReadShippedFile(path, out ReadOnlyMemory<byte> bytes))
                {
                    yield return (path[(folder.Length + 1)..], bytes);
                }
            }
        }
    }
}
