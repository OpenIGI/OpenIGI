using System.Globalization;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Viewport;
using OpenIGI.Game.Ai;
using OpenIGI.UI.Editor.Widgets;

namespace OpenIGI.Editor.Panels;

/// <content>
/// A guard's behaviour, under its parameters: how far and wide it sees, the alarm it runs for and when, the gun it mans,
/// whether it can be hurt, and its patrols. Each is one of its own script's orders (<see cref="GuardOrders"/>), written back
/// as the games' own guard scripts are when the mission is saved.
/// </content>
internal sealed partial class InspectorPanel
{
    private const string NoneChoice = "None";

    /// <summary>Gets or sets what shows a message: the text, and whether it is an error.</summary>
    public Action<string, bool>? Notify { get; set; }

    /// <summary>Gets or sets what starts the patrol tool on a soldier, for the Edit Patrol button.</summary>
    public Action<MissionTask>? EditPatrol { get; set; }

    private void AddGuardSection(MissionDocument document, MissionTask soldier)
    {
        if (_guards is not { } guards || !PatrolPaths.IsSoldier(soldier))
        {
            return;
        }

        AiViewCone cone = AiViewCone.DefaultPrimary;
        AiGuardOrders Orders() => guards.Of(soldier);
        void Set(Func<AiGuardOrders, AiGuardOrders> change)
        {
            if (guards.Set(soldier, KeepPatrol(soldier, change(Orders())), out string? refusal) is { } step)
            {
                _workspace.Execute(step);
            }
            else if (refusal is not null)
            {
                Notify?.Invoke(refusal, true);
                _shownRevision = -1;
            }
        }

        _grid.Section("Behaviour");

        PatrolRoute route = PatrolPaths.Path(soldier) is { } path ? PatrolRoute.Read(path) : PatrolRoute.Empty;
        _grid.Text(
            "Patrol",
            () => route.Waypoints.Count == 0 ? "Stands where it is put" : string.Create(CultureInfo.InvariantCulture, $"{route.Waypoints.Count} waypoint{(route.Waypoints.Count == 1 ? string.Empty : "s")}{(route.Stops ? ", then stops" : ", round again")}"),
            null,
            "Where it walks when nothing is happening. Edit Patrol lays it on the ground.");
        Button edit = new(Theme, "Edit Patrol") { Tooltip = "Lay the patrol on the ground in the viewport (P)" };
        edit.Clicked += () => EditPatrol?.Invoke(soldier);
        _grid.Add(string.Empty, edit);

        NumberField range = _grid.Number(
            "Sight range (m)",
            () => Orders().ViewLength ?? cone.Length / AiViewCone.UnitsPerMetre,
            (value, final) =>
            {
                if (final)
                {
                    Set(o => o with { ViewLength = (float)value });
                }
            },
            "How far it sees, AIFunction_SetViewLength. The game's own default is 26 m.");
        range.Minimum = 1;
        range.Maximum = 500;
        range.Decimals = 1;

        NumberField width = _grid.Number(
            "Sight width (°)",
            () => Orders().ViewGamma ?? cone.Gamma / AiViewCone.DegreesToRadians,
            (value, final) =>
            {
                if (final)
                {
                    Set(o => o with { ViewGamma = (float)value });
                }
            },
            "How far to each side it sees, AIFunction_SetViewGamma: 50° is the game's default, 180° all round.");
        width.Minimum = 1;
        width.Maximum = 180;
        width.Decimals = 0;

        MissionTask[] alarms = [.. document.EnumerateTasks().Where(static t => t.TypeName == "AlarmControl" && t.Id >= 0)];
        _grid.Choice(
            "Alarm",
            [NoneChoice, .. alarms.Select(Describe)],
            () => Orders().AlarmControlId is { } id ? Array.FindIndex(alarms, a => a.Id == id) + 1 : 0,
            index => Set(o => o with { AlarmControlId = index <= 0 ? null : alarms[index - 1].Id }),
            "The alarm panel it runs to and hears, AIFunction_SetAlarmControlID.");

        string[] access = ["As its kind does", "Runs for the alarm first", "Fights first, then raises it"];
        _grid.Choice(
            "On seeing you",
            access,
            () => Orders().AlarmAccess switch { AiAlarmAccess.BeforeCombat => 1, AiAlarmAccess.AfterCombat => 2, _ => 0 },
            index => Set(o => o with { AlarmAccess = index switch { 1 => AiAlarmAccess.BeforeCombat, 2 => AiAlarmAccess.AfterCombat, _ => null } }),
            "Whether it runs to raise the alarm before fighting, AIFunction_SetAlarmAccess.");

        MissionTask[] patrols = [.. document.EnumerateTasks().Where(static t => t.TypeName == PatrolRoute.PathType && t.Id >= 0)];
        _grid.Choice(
            "Alarm patrol",
            [NoneChoice, .. patrols.Select(Describe)],
            () => Orders().AlarmPatrolPathId is { } id ? Array.FindIndex(patrols, p => p.Id == id) + 1 : 0,
            index => Set(o => o with { AlarmPatrolPathId = index <= 0 ? null : patrols[index - 1].Id }),
            "The patrol it walks once the alarm sounds, AIAction_Patrol on AIEVENT_ALARMON.");

        MissionTask[] guns = [.. document.EnumerateTasks().Where(static t => t.TypeName == "StationaryGun" && t.Id >= 0)];
        _grid.Choice(
            "Mans gun",
            [NoneChoice, .. guns.Select(Describe)],
            () => Orders().GunnerId is { } id ? Array.FindIndex(guns, g => g.Id == id) + 1 : 0,
            index => Set(o => o with { GunnerId = index <= 0 ? null : guns[index - 1].Id }),
            "The mounted gun it mans, AIFunction_SetGunnerID.");

        _grid.Bool(
            "Cannot be hurt",
            () => Orders().IsInvulnerable == true,
            value => Set(o => o with { IsInvulnerable = value ? true : null }),
            "AIFunction_SetInvulnerability: for a soldier the story needs alive.");
    }

    // The soldier's own patrol, which its script must start on its idle event, kept in the orders whatever else changes.
    private static AiGuardOrders KeepPatrol(MissionTask soldier, AiGuardOrders orders) =>
        orders.PatrolPathId is null && PatrolPaths.Path(soldier) is { Id: >= 0 } path && PatrolRoute.Read(path).Waypoints.Count > 0
            ? orders with { PatrolPathId = path.Id }
            : orders;

    private static string Describe(MissionTask task) =>
        task.Label.Length > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{task.Label} ({task.Id})")
            : string.Create(CultureInfo.InvariantCulture, $"{task.TypeName} {task.Id}");
}
