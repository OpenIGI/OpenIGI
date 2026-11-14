using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Panels;
using OpenIGI.Editor.Viewport;
using OpenIGI.Scripting.Qsc;
using OpenIGI.UI.Editor.Actions;

namespace OpenIGI.Editor;

/// <summary>Soldiers and their patrols: placing a guard, the patrol tool, and the AI scripts the original needs them to have.</summary>
internal sealed partial class EditorSession
{
    // The model and kit a guard placed in the editor gets: level 1's plain AK guard (soldier 2003).
    private const string GuardModel = "003_01_1";

    private void RegisterPatrolActions(ActionTable actions)
    {
        _viewport.Patrol = new PatrolTool(_workspace, _host, at => BuildNavGraph(at, announce: false), (message, error) => Notify(message, error));
        actions.Add(new EditorAction("tool.patrol", "Patrol Tool", StartPatrol)
        {
            Category = "Tools",
            Shortcut = KeyChord.Parse("P"),
            IsChecked = () => _viewport.Tool == ViewportTool.Patrol,
            CanExecute = () => _host.Title.Id == "igi1" && PatrolTool.IsSoldier(_workspace.Selection.Primary),
            DynamicTitle = () => PatrolTool.IsSoldier(_workspace.Selection.Primary) ? "Patrol Tool" : "Patrol Tool (select a soldier first)",
        });
        actions.Add(new EditorAction("ai.patrolround", "Patrol Round Selection", PatrolRound)
        {
            Category = "Tools",
            CanExecute = () => _host.Title.Id == "igi1" && _workspace.Selection.Items.Any(PatrolTool.IsSoldier),
        });
        actions.Add(new EditorAction("ai.addguard", "Add Guard Here", AddGuard)
        {
            Category = "Tools",
            CanExecute = () => _host.Title.Id == "igi1" && _workspace.Document?.Declarations.ContainsKey("HumanSoldier") == true && _host.Terrain is not null,
        });

        // Picking another soldier while patrolling edits that one's patrol; picking anything else puts the tool down.
        _workspace.Selection.Changed += () =>
        {
            if (_viewport.Tool != ViewportTool.Patrol || _viewport.Patrol is not { } tool)
            {
                return;
            }

            if (PatrolTool.IsSoldier(_workspace.Selection.Primary))
            {
                if (!ReferenceEquals(tool.Soldier, _workspace.Selection.Primary))
                {
                    tool.Begin(_workspace.Selection.Primary!);
                }
            }
            else
            {
                tool.End();
                _viewport.Tool = ViewportTool.Move;
            }
        };
        _workspace.BeforeSave += WriteSoldierScripts;
    }

    private void StartPatrol()
    {
        if (_viewport.Patrol is { } tool && _workspace.Selection.Primary is { } soldier && tool.Begin(soldier))
        {
            _viewport.Tool = ViewportTool.Patrol;
            _dock.Show("viewport");
        }
    }

    // The selected soldier's patrol laid as a ring round the other thing selected (a building, a tower), far enough out to
    // walk round it; with only the soldier selected, round where he stands.
    private void PatrolRound()
    {
        if (_viewport.Patrol is not { } tool || _workspace.Selection.Items.FirstOrDefault(PatrolTool.IsSoldier) is not { } soldier)
        {
            return;
        }

        MissionTask? subject = _workspace.Selection.Items.FirstOrDefault(t => !ReferenceEquals(t, soldier) && TaskPresentation.PositionMetres(t) is not null);
        if ((subject ?? soldier) is not { } centreTask || TaskPresentation.PositionMetres(centreTask) is not { } centre || !tool.Begin(soldier))
        {
            return;
        }

        double radius = subject is null ? 10.0 : (_live.RadiusMetres(subject) ?? 4.0) + 4.0;
        tool.Circle(centre, Math.Clamp(radius, 4.0, 80.0));
        _workspace.Selection.Set([soldier]);
        _viewport.Tool = ViewportTool.Patrol;
        Notify(subject is null
            ? "Laid a patrol round where the soldier stands; drag its waypoints to shape it."
            : $"Laid a patrol round {TaskPresentation.Name(subject)}; drag its waypoints to shape it.");
    }

    // A guard in the middle of the view, on the AI graph there (built when there is none), selected with its patrol tool on.
    private void AddGuard()
    {
        if (_workspace.Document is not { } document || _viewport.PlacementPoint() is not { } at || _host.Terrain is not { } terrain)
        {
            return;
        }

        if (terrain.TryGetHeight(at.X, at.Y, out double z))
        {
            at = new Vector3d(at.X, at.Y, z);
        }

        string[] missing = [.. ((string[])["HumanSoldier", "HumanAI", "Gun"]).Where(t => !document.Declarations.ContainsKey(t))];
        if (missing.Length > 0)
        {
            Notify($"This level does not declare {string.Join(", ", missing)}, which a guard needs.", isError: true);
            return;
        }

        MissionTask? graph = NearestGraph(document, at) ?? BuildNavGraph(at, announce: false);
        HashSet<int> used = [.. document.EnumerateTasks().Select(static t => t.Id)];
        int NextId() => Enumerable.Range(1, MissionDocument.MaximumTaskId - 1).First(used.Add);

        // The level's own soldiers' model when it has one; else level 1's guard's.
        string model = document.EnumerateTasks().FirstOrDefault(static t => t.TypeName == "HumanSoldier")?.FindParameter("Model") is { } m
            && m.Arguments.Count > 0 && ScriptValues.TryGetString(m.Arguments[0], out string name) ? name : GuardModel;
        MissionTask soldier = New(document, "HumanSoldier", "Guard", NextId());
        soldier.FindParameter("Position")?.SetNumbers([at.X * WorldUnits.PerMetre, at.Y * WorldUnits.PerMetre, at.Z * WorldUnits.PerMetre]);
        Text(soldier, "Model", model);
        soldier.FindParameter("Team")?.SetNumbers([1]);
        soldier.FindParameter("Bone Heirachy")?.SetNumbers([1]);
        soldier.FindParameter("Stand Animation")?.SetNumbers([-1]);
        foreach (string weapon in (string[])["WEAPON_ID_AK47", "WEAPON_ID_GRENADE"])
        {
            MissionTask gun = New(document, "Gun", string.Empty, -1);
            Text(gun, gun.Parameters.FirstOrDefault()?.Name ?? string.Empty, weapon);
            soldier.AddChild(gun);
        }

        MissionTask ai = New(document, "HumanAI", string.Empty, NextId());
        Text(ai, "AI Type", "AITYPE_GUARD_AK");
        ai.FindParameter("Graph ID")?.SetNumbers([graph?.Id ?? -1]);
        soldier.AddChild(ai);

        MissionTask? player = document.EnumerateTasks().FirstOrDefault(static t => t.TypeName == "HumanPlayer");
        TaskLocation where = player is not null ? document.Locate(player) with { Index = document.Locate(player).Index + 1 } : new TaskLocation(null, document.Statements.Count);
        _workspace.Execute(new InsertTaskCommand(document, soldier, where));
        _workspace.Selection.Set([soldier]);
        if (model == GuardModel && _host.HasModel(model) == false)
        {
            Notify($"Added a guard, but this level has no model {model}; give it one of the level's in the Inspector.", isError: true);
        }

        StartPatrol();
        Notify("Added a guard. Click the ground to give it a patrol, or leave it to stand guard.");
    }

    // The AIGraph whose nodes come nearest a point, within a hundred metres.
    private MissionTask? NearestGraph(MissionDocument document, Vector3d at)
    {
        if (_workspace.Mission is not { } mission)
        {
            return null;
        }

        (MissionTask? Task, double Distance) best = (null, 100);
        foreach (MissionTask task in document.EnumerateTasks().Where(MissionGraphs.IsGraph))
        {
            if (_workspace.Graphs.Get(mission, task) is { } graph && MissionGraphs.BoundsMetres(task, graph) is { } bounds)
            {
                double distance = Math.Max(0, Vector3d.Distance(bounds.Centre, at) - bounds.Radius);
                if (distance < best.Distance)
                {
                    best = (task, distance);
                }
            }
        }

        return best.Task;
    }

    // Before the mission is written: every soldier's AI script, which the original loads from MISSION:AI/<HumanAI id> and
    // without which the soldier does nothing. A soldier with none is given the levels' own guard script; one whose patrol
    // was laid in the editor has it started on its idle event, and one whose patrol was taken away has it stopped -- in
    // its own script, changed only there (GuardOrders, AiGuardScriptEdit).
    private void WriteSoldierScripts()
    {
        if (_workspace.Document is not { } document || _host.Title.Id != "igi1")
        {
            return;
        }

        foreach (MissionTask soldier in document.EnumerateTasks().Where(PatrolTool.IsSoldier))
        {
            Game.Ai.AiGuardOrders orders = _guards.Of(soldier);
            MissionTask? path = PatrolPaths.Path(soldier);
            bool patrols = path is { Id: >= 0 } && PatrolRoute.Read(path).Waypoints.Count > 0;
            Game.Ai.AiGuardOrders wanted = patrols
                ? orders with { PatrolPathId = path!.Id }
                : path is not null && orders.PatrolPathId == path.Id ? orders with { PatrolPathId = null } : orders;
            if (_guards.Require(soldier, wanted) is { } problem)
            {
                Notify(problem, isError: true);
            }
        }
    }

    private static MissionTask New(MissionDocument document, string type, string label, int id)
    {
        MissionTask task = new(type, label, id);
        document.ApplyDeclaration(task);
        return task;
    }

    private static void Text(MissionTask task, string parameter, string text)
    {
        if (task.FindParameter(parameter) is { } found)
        {
            found.Arguments.Clear();
            found.Arguments.Add(new QscStringLiteral(text));
        }
    }
}
