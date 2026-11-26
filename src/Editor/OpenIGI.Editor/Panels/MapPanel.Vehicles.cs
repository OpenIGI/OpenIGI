using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Viewport;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <content>
/// Vehicles and what fires from fixed places: the splines trains run on and roads are laid along, the routes the game's AI drives cars along, and the
/// arcs mounted guns and armed vehicles watch.
/// </content>
internal sealed partial class MapPanel
{
    // A gun holder's and an armed vehicle's cone: its "Viewcone Gamma" is the yaw either side it may turn, not halved
    // (gunholder.c's maxYaw, AIStationaryGunHolder 360° all round), out to its length in metres.
    private static readonly (string Type, string Gamma, string Length)[] Watchers =
    [
        ("AIStationaryGunHolder", "Viewcone Gamma", "Viewcone Length"),
        ("Car", "ViewCone Gamma (degrees)", "ViewCone Length (meter)"),
        ("Heli", "ViewCone Gamma (degrees)", "ViewCone Length (meter)"),
    ];

    private void DrawVehicles(UiPainter painter, HashSet<MissionTask> selected)
    {
        if (_workspace.Document is not { } document)
        {
            return;
        }

        DrawRails(painter, document, selected);
        DrawCarRoutes(painter, document, selected);
        DrawWatchers(painter, document, selected);
    }

    // A spline -- a train's rail, a road -- through its SplineObj's waypoints in order (drawn straight between them; the
    // game curves it through them).
    private void DrawRails(UiPainter painter, MissionDocument document, HashSet<MissionTask> selected)
    {
        Bgra32 rail = Bgra32.FromRgb(0x9C, 0xA6, 0xB4);
        foreach (MissionTask spline in document.EnumerateTasks().Where(static t => t.TypeName == "SplineObj"))
        {
            Vector3d[] points = [.. spline.Children.Where(static c => c.TypeName == "SplineObjWaypoint")
                .Select(TaskPresentation.PositionMetres).OfType<Vector3d>()];
            bool lit = selected.Contains(spline) || spline.Children.Any(selected.Contains);
            for (int i = 1; i < points.Length; i++)
            {
                painter.StrokeLine(ToScreen(points[i - 1]), ToScreen(points[i]), Theme.Px(lit ? 3f : 2f), rail.WithAlpha((byte)(lit ? 230 : 150)));
            }
        }
    }

    // What the game's AI drives: each CarAI's car along its route (a PatrolPath) on its AI graph.
    private void DrawCarRoutes(UiPainter painter, MissionDocument document, HashSet<MissionTask> selected)
    {
        Bgra32 drive = Bgra32.FromRgb(0xE0, 0x9A, 0x48);
        Dictionary<int, MissionTask> byId = [];
        foreach (MissionTask task in document.EnumerateTasks().Where(static t => t.Id >= 0))
        {
            byId.TryAdd(task.Id, task);
        }

        foreach (MissionTask driver in document.EnumerateTasks().Where(static t => t.TypeName == "CarAI"))
        {
            if (!byId.TryGetValue((int)Number(driver, "Car ID", -1), out MissionTask? car)
                || !byId.TryGetValue((int)Number(driver, "Route ID", -1), out MissionTask? route)
                || !byId.TryGetValue((int)Number(driver, "Graph ID", -1), out MissionTask? graph))
            {
                continue;
            }

            bool lit = selected.Contains(car) || selected.Contains(driver);
            if ((!lit && !_guardsShown.IsChecked) || _paths.Walk(TaskPresentation.PositionMetres(car), route, graph) is not { } walk)
            {
                continue;
            }

            foreach (PatrolLeg leg in walk.Legs)
            {
                painter.StrokeLine(ToScreen(leg.From), ToScreen(leg.To), Theme.Px(leg.IsWayBack ? 1.5f : 3f), drive.WithAlpha((byte)(leg.IsWayBack ? 90 : lit ? 230 : 140)));
            }
        }
    }

    // Mounted guns' holders and armed vehicles: the arc they watch, from where they face.
    private void DrawWatchers(UiPainter painter, MissionDocument document, HashSet<MissionTask> selected)
    {
        Bgra32 colour = Bgra32.FromRgb(0xF2, 0x6B, 0xA8);
        Span<UiVec2> fan = stackalloc UiVec2[26];
        foreach ((string type, string gamma, string length) in Watchers)
        {
            foreach (MissionTask task in document.EnumerateTasks().Where(t => t.TypeName == type))
            {
                bool lit = selected.Contains(task);
                if ((!lit && !_guardsShown.IsChecked) || TaskPresentation.PositionMetres(task) is not { } at)
                {
                    continue;
                }

                double range = Number(task, length, 10);
                double half = Math.Min(Math.PI, Number(task, gamma, 360) * Math.PI / 180);
                double heading = Heading(task);
                Fan(fan, at, heading - half, heading + half, range);
                painter.FillPolygon(fan, colour.WithAlpha((byte)(lit ? 45 : 22)));
                painter.StrokePolygon(fan, 1f, colour.WithAlpha((byte)(lit ? 200 : 100)));
            }
        }
    }
}
