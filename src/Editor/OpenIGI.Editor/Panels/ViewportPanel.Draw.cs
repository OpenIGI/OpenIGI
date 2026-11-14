using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Viewport;
using OpenIGI.Game.World;
using OpenIGI.Host.Editor;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>The viewport's drawing over the world: the grid, outlines, labels and the start page.</summary>
internal sealed partial class ViewportPanel
{
    private UiRect OrientationArea => OrientationGizmo.Area(WorldRect, Theme);

    // The ground grid, at the ground under the camera: never where the camera happens to look, so it holds still as the view
    // turns, and never at what is selected, so dragging something has nothing to do with it.
    private void DrawGrid(UiPainter painter)
    {
        if (!EditorCVars.ShowGrid.Value || _host.Camera is null)
        {
            return;
        }

        if (GroundUnderEye() is { } ground)
        {
            _gridHeight = ground;
        }

        if (_gridHeight is { } height)
        {
            ViewportGrid.Draw(painter, Theme, Projector, height, EditorCVars.GridStep.Value);
        }
    }

    // The ground straight below the camera, in metres: the level's own ground, or a model's top when one is nearer.
    private double? GroundUnderEye()
    {
        Vector3d eye = Projector.Eye;
        double? ground = null;
        if (_host.Ground is { } field
            && field.TryDrop(WorldUnits.FromMetres(eye.X), WorldUnits.FromMetres(eye.Y), WorldUnits.FromMetres(eye.Z), WorldUnits.FromMetres(PickReachMetres), upward: false, out Game.World.TerrainGround below))
        {
            ground = WorldUnits.ToMetres(below.Height);
        }

        if (_live.Pick(eye, new Vector3d(0, 0, -1), PickReachMetres, out double distance) is not null && (ground is not { } g || eye.Z - distance > g))
        {
            ground = eye.Z - distance;
        }

        return ground;
    }

    // Boxes around what is selected, steady, and around what the pointer is over, pulsing, as scene editors show them.
    private void DrawOutlines(UiPainter painter)
    {
        ViewportProjector projector = Projector;
        Bgra32 selectedColour = Theme.Accent.WithAlpha(200);
        foreach (MissionTask task in _workspace.Selection.Items)
        {
            bool drawn = false;
            foreach (BoxGizmo.Box box in _live.Boxes(task))
            {
                BoxGizmo.DrawEdges(painter, Theme, projector, box, selectedColour);
                drawn = true;
            }

            // Something with no model of its own (a patrol point, a trigger, a graph) is marked where it is, with its name, so
            // what the gizmo moves can be seen.
            if (!drawn && TaskPresentation.PositionMetres(task) is { } at && projector.TryProject(at, out UiVec2 spot))
            {
                painter.FillDiamond(spot, Theme.Px(12f), Theme.Accent);
                painter.FillDiamond(spot, Theme.Px(6f), Bgra32.White);
                UiRect label = new(spot.X + Theme.Px(10f), spot.Y - Theme.Px(22f), Theme.Px(220f), Theme.RowHeight);
                EditorDraw.Label(painter, Theme, TaskPresentation.Name(task), label, Theme.Text, strong: true);
            }
        }

        if (_hovered is { } hovered && !_workspace.Selection.Contains(hovered) && !Sculpting)
        {
            double pulse = 0.5 + (0.5 * Math.Sin(Environment.TickCount64 / 1000.0 * 8.0));
            Bgra32 hoverColour = Bgra32.FromRgb(255, 222, 90).WithAlpha((byte)(90 + (110 * pulse)));
            foreach (BoxGizmo.Box box in _live.Boxes(hovered))
            {
                BoxGizmo.DrawEdges(painter, Theme, projector, box, hoverColour);
            }
        }
    }

    private void DrawChips(UiPainter painter)
    {
        string mode = _workspace.Mission is not null ? $"{_host.Title.DisplayName}  •  {_workspace.MissionName}" : string.Empty;
        UiRect chip = new(WorldRect.X + Theme.Px(10f), WorldRect.Y + Theme.Px(10f), Theme.Font.MeasureWidth(mode, Theme.TextSize) + Theme.Px(20f), Theme.ControlHeight);
        EditorDraw.Fill(painter, Theme, chip, Theme.Background.WithAlpha(170));
        EditorDraw.Label(painter, Theme, mode, chip.Deflate(new Thickness(Theme.Px(10f), 0f)), Theme.Text);

        if (_host.Camera is { } camera)
        {
            string speed = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Camera {camera.Speed:0.#} m/s");
            float width = Theme.SmallFont.MeasureWidth(speed, Theme.SmallTextSize) + Theme.Px(16f);
            UiRect right = new(WorldRect.Right - width - Theme.Px(10f), WorldRect.Y + Theme.Px(10f), width, Theme.ControlHeight);
            EditorDraw.Fill(painter, Theme, right, Theme.Background.WithAlpha(170));
            painter.DrawText(Theme.SmallFont, speed, new UiVec2(right.X + Theme.Px(8f), MathF.Round(right.Y + ((right.Height - Theme.SmallFont.LineHeight(Theme.SmallTextSize)) * 0.5f))), Theme.SmallTextSize,
                _camera.SecondsSinceSpeedChange < 1.5 ? Theme.Accent : Theme.TextMuted);
        }

        if (_host.IsWarmingUp)
        {
            EditorDraw.Label(painter, Theme, "Loading…", new UiRect(Bounds.Center.X - Theme.Px(30f), Bounds.Center.Y, Theme.Px(80f), Theme.RowHeight), Theme.TextMuted);
        }
    }

    private UiRect StartList
    {
        get
        {
            float width = MathF.Min(Theme.Px(520f), Bounds.Width - Theme.Px(40f));
            float top = Bounds.Y + MathF.Max(Theme.Px(40f), (Bounds.Height * 0.18f));
            return new UiRect(MathF.Round(Bounds.Center.X - (width * 0.5f)), top + Theme.Px(70f), width, Bounds.Bottom - top - Theme.Px(100f));
        }
    }

    private int? MissionAt(UiVec2 point)
    {
        UiRect list = StartList;
        if (!list.Contains(point))
        {
            return null;
        }

        int index = (int)((point.Y - list.Y) / (Theme.RowHeight + Theme.Px(6f)));
        return index >= 0 && index < _host.Missions.Count ? index : null;
    }

    private void DrawStartPage(UiPainter painter)
    {
        painter.FillRect(Bounds, Theme.Background);
        UiRect list = StartList;
        float y = list.Y - Theme.Px(70f);
        EditorDraw.Label(painter, Theme, $"Open a {_host.Title.DisplayName} mission", new UiRect(list.X, y, list.Width, Theme.Px(30f)), Theme.Text, strong: true);
        EditorDraw.Label(painter, Theme, "Edits are saved to a project of their own; the game's files are never changed.", new UiRect(list.X, y + Theme.Px(28f), list.Width, Theme.RowHeight), Theme.TextMuted);

        painter.PushClip(list);
        float rowHeight = Theme.RowHeight + Theme.Px(6f);
        for (int i = 0; i < _host.Missions.Count; i++)
        {
            MissionDescriptor mission = _host.Missions[i];
            UiRect row = new(list.X, list.Y + (i * rowHeight), list.Width, rowHeight - Theme.Px(4f));
            EditorDraw.Fill(painter, Theme, row, i == _hoveredMission ? Theme.Selection : Theme.Panel);
            EditorDraw.Icon(painter, Theme, KitIcon.Terrain, new UiRect(row.X + Theme.Px(8f), row.Y, Theme.IconSize, row.Height));
            EditorDraw.Label(painter, Theme, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{mission.Id}.  {_host.MissionName(mission)}"), new UiRect(row.X + Theme.Px(32f), row.Y, row.Width - Theme.Px(40f), row.Height), Theme.Text);
        }

        painter.PopClip();
    }
}
