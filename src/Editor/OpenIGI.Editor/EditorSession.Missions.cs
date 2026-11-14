using OpenIGI.Editor.Core.Projects;
using OpenIGI.Editor.Panels;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
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
        (MissionDescriptor from, string name, int id, bool joinCampaign, MissionTarget target) = (request.From, request.Name, request.Id, request.JoinCampaign, request.Target);
        if (_host.Missions.Any(m => m.Id == id))
        {
            Notify(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Mission {id} is taken already."), isError: true);
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

            // A blank keeps the level as a place (its ground, sky and light) and the player, with an exit to end it.
            IEnumerable<(string RelativePath, ReadOnlyMemory<byte> Bytes)> level = request.Blank
                ? LevelFiles(folder).Where(static f => BlankMission.Keeps(f.RelativePath)).Select(static f => f.RelativePath.Equals(OpenIGI.Engine.Level.LevelLoader.ObjectsScriptName, StringComparison.OrdinalIgnoreCase)
                    ? (f.RelativePath, (ReadOnlyMemory<byte>)BlankMission.Make(f.Bytes).ToImage())
                    : f)
                : LevelFiles(folder);
            IReadOnlyList<(string GamePath, byte[] Bytes)> files = NewMission.Build(from.MissionDirectory, script, level, id, name, previous);
            foreach ((string path, byte[] bytes) in files)
            {
                _workspace.Project.Write(path, bytes);
            }

            _workspace.Project.SetTarget(id, target);
            _host.MountContent(_workspace.Project.LayerId, _workspace.Project.Name, _workspace.Project.FilesDirectory);
            _host.RescanMissions();
            _host.OpenMission(id);
            if (request.Blank)
            {
                ShapeBlankGround(id, request.Ground, request.Seed);
            }
            else
            {
                // A copy opens over its player's shoulder too, once it is up.
                _pendingSteps.Enqueue(() =>
                {
                    if (_workspace.Mission?.Id != id || _workspace.Document is not { } document || _host.IsWarmingUp)
                    {
                        return false;
                    }

                    LookOverPlayer(document);
                    return true;
                });
            }

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

    // A new blank mission's ground, shaped once it is the mission open and its terrain is up, then an AI graph over the
    // play area for soldiers to walk (the level's own graphs went with its soldiers), then saved with it.
    private void ShapeBlankGround(int id, BlankGround ground, int seed)
    {
        _pendingSteps.Enqueue(() =>
        {
            if (_workspace.Mission?.Id != id || _workspace.Document is not { } document || _host.IsWarmingUp)
            {
                return false;
            }

            string shapedNote = string.Empty;
            if (ground != BlankGround.Keep)
            {
                if (_host.Terrain is not { } terrain || Terrain.BlankGroundCommand.Shape(terrain, document, ground, seed) is not { } shaped)
                {
                    Notify("The new mission's ground could not be shaped here; it keeps the level's own.", isError: true);
                }
                else
                {
                    _workspace.Execute(shaped);
                    shapedNote = ground == BlankGround.Flat ? "Flattened the ground. " : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Generated the ground from seed {seed}. ");
                }
            }

            LookOverPlayer(document);
            string graphNote = BuildNavGraph(PlayArea(document), announce: false) is { } graph
                ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Built AI graph {graph.Id} over the play area. ")
                : string.Empty;
            _workspace.Save();
            Notify($"{shapedNote}{graphNote}Saved the mission.");
            return true;
        });
    }

    // A new mission's first view: from behind its player, towards the exit when it has one. The mission opens wherever the
    // game's camera was (the level's intro, or under ground just flattened), which showed nothing of it.
    private void LookOverPlayer(MissionDocument document)
    {
        MissionTask? player = document.EnumerateTasks().FirstOrDefault(static t => t.TypeName == "HumanPlayer");
        MissionTask? exit = document.EnumerateTasks().FirstOrDefault(static t => t is { TypeName: "AreaActivate", Label: "Exit" });
        if (player is not null && TaskPresentation.PositionMetres(player) is { } start)
        {
            _viewport.LookOver(start, exit is not null ? TaskPresentation.PositionMetres(exit) : null);
        }
    }

    // The middle of a blank mission's play: halfway from the player to the exit, in world metres.
    private static Vector3d? PlayArea(MissionDocument document)
    {
        MissionTask? player = document.EnumerateTasks().FirstOrDefault(static t => t.TypeName == "HumanPlayer");
        MissionTask? exit = document.EnumerateTasks().FirstOrDefault(static t => t is { TypeName: "AreaActivate", Label: "Exit" });
        return player is not null && TaskPresentation.PositionMetres(player) is { } start
            ? exit is not null && TaskPresentation.PositionMetres(exit) is { } end ? (start + end) * 0.5 : start
            : null;
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
