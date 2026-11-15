using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Map;
using OpenIGI.Host.Editor;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <content>
/// The level under the marks, as the multiplayer layout page draws it: the textured ground and every model from straight
/// above, cut at the storey chosen (Roofs, Ground, Floor 1 to 4), the plan's walls over it, and the floor under the pointer.
/// The whole level is drawn once a storey; the part in view is drawn again, as finely as it is shown, whenever the view
/// settles, off the frame.
/// </content>
internal sealed partial class MapPanel
{
    // The most pixels a side the whole level is drawn at, under the part in view.
    private const int OverviewPixels = 2048;

    // A run's pace, for how long a measured line takes on foot.
    private const double RunMetresPerSecond = 5.5;

    private OverheadScene? _scene;
    private FloorPlan? _plan;
    private OverheadRaster? _raster;
    private Task<(OverheadScene Scene, OverheadRaster Raster, FloorPlan Plan)>? _preparing;
    private Task<(OverheadView View, Bgra32[] Pixels, bool Whole)>? _rendering;
    private Picture? _overview;
    private Picture? _detail;
    private int _floor;
    private Vector3d? _pointerWorld;
    private (Vector3d From, Vector3d To)? _measured;
    private bool _measuringLine;
    private double? _zoomWanted;

    private Floor ShownFloor => FloorPlan.Floors[_floor];

    /// <summary>Cuts the map at a storey: 0 the roofs, 1 the ground, 2 and on the floors over it.</summary>
    /// <param name="floor">The storey.</param>
    public void ShowFloor(int floor)
    {
        _floor = Math.Clamp(floor, 0, FloorPlan.Floors.Count - 1);
        for (int i = 0; i < _floorButtons.Length; i++)
        {
            _floorButtons[i].IsChecked = i == _floor;
        }
    }

    /// <summary>Centres the map on the selection, at so many metres a pixel.</summary>
    /// <param name="metresPerPixel">How much of the world a pixel shows.</param>
    /// <remarks>Waits for the map's first framing, which would otherwise undo it.</remarks>
    public void ZoomOnSelection(double metresPerPixel) => _zoomWanted = metresPerPixel;

    // A zoom asked for, put into effect once the map has been framed.
    private void ApplyWantedZoom()
    {
        if (_zoomWanted is not { } metresPerPixel || !_framed)
        {
            return;
        }

        _zoomWanted = null;
        if (_workspace.Selection.Primary is { } task && TaskPresentation.PositionMetres(task) is { } at)
        {
            _centre = at;
        }

        _scale = Math.Clamp(1.0 / metresPerPixel, 0.02, 40.0);
    }

    private Button[] MakeFloorButtons() =>
        [.. FloorPlan.Floors.Select((floor, i) =>
        {
            Button button = new(Theme, floor.Name)
            {
                Style = ButtonStyle.Flat,
                IsToggle = true,
                IsChecked = i == 0,
                Tooltip = floor.IsRoofs
                    ? "The level whole, roofs and all ([ and ] step through the storeys)"
                    : string.Create(CultureInfo.InvariantCulture, $"Cut away everything higher than {floor.Cut:0.#} m over the ground under it, to look into the buildings' {floor.Name.ToLowerInvariant()} ([ and ])"),
            };
            button.Clicked += () => ShowFloor(i);
            return button;
        })];

    // The level from above, gathered once the mission is up (and again after Redraw or for another mission), then finished and
    // readied for drawing off the frame; the models' boxes stand in meanwhile.
    private void EnsureMap()
    {
        int? mission = _workspace.Mission?.Id;
        if (mission == _mapFor || mission is null || _host.IsWarmingUp)
        {
            return;
        }

        _mapFor = mission;
        Release();
        if (_host.MakeOverheadScene() is { } making)
        {
            _preparing = Task.Run(async () =>
            {
                OverheadScene scene = await making.ConfigureAwait(false);
                FloorPlan plan = new(scene);
                return (scene, new OverheadRaster(scene, plan), plan);
            });
        }
        else
        {
            CastGround();
        }

        _framed = false;
    }

    // The ground cast from above, for a title that cannot hand its own over.
    private void CastGround()
    {
        _map = _host.MakeOverheadMap(MapPixels);
        if (_map is { } map)
        {
            _image = _images.CreateImage(map.Width, map.Height, map.Pixels);
        }
    }

    /// <summary>Releases the pictures and stops any drawing.</summary>
    public void Release()
    {
        _rendering = null;
        _preparing = null;
        _raster = null;
        _plan = null;
        _scene = null;
        _map = null;
        _images.DestroyImage(_image);
        _image = UiImage.None;
        foreach (Picture? picture in (Picture?[])[_overview, _detail])
        {
            if (picture is not null)
            {
                _images.DestroyImage(picture.Image);
            }
        }

        _overview = null;
        _detail = null;
    }

    // Takes in whatever finished off the frame, and starts the next picture the view wants.
    private void AdvancePictures(UiRect area)
    {
        if (_preparing is { IsCompleted: true } preparing)
        {
            _preparing = null;
            if (preparing.IsCompletedSuccessfully)
            {
                (_scene, _raster, _plan) = preparing.Result;
                if (!_scene.HasGround)
                {
                    CastGround();
                }
            }
            else
            {
                _host.Logger.Warning("Editor", $"The map could not be drawn: {preparing.Exception?.GetBaseException().Message}");
            }
        }

        if (_rendering is { IsCompleted: true } rendering)
        {
            _rendering = null;
            if (rendering.IsCompletedSuccessfully)
            {
                (OverheadView view, Bgra32[] pixels, bool isWhole) = rendering.Result;
                ref Picture? slot = ref isWhole ? ref _overview : ref _detail;
                if (slot is { } old && old.View.Width == view.Width && old.View.Height == view.Height)
                {
                    _images.UpdateImage(old.Image, pixels);
                    slot = old with { View = view };
                }
                else
                {
                    if (slot is not null)
                    {
                        _images.DestroyImage(slot.Image);
                    }

                    slot = new Picture(_images.CreateImage(view.Width, view.Height, pixels), view);
                }
            }
        }

        if (_raster is not { } raster || _scene is not { } scene || _rendering is not null || area.Width < 2f || area.Height < 2f)
        {
            return;
        }

        Floor floor = ShownFloor;
        bool walls = _planShown.IsChecked;
        double wide = scene.EastMetres - scene.WestMetres, high = scene.NorthMetres - scene.SouthMetres;
        double overviewMetres = Math.Max(wide, high) / OverviewPixels;
        OverheadView? wanted = null;
        bool whole = false;
        if (_overview is not { } overview || overview.View.Floor != floor || overview.View.Walls != walls)
        {
            wanted = new OverheadView(scene.WestMetres, scene.NorthMetres, overviewMetres, Math.Max(1, (int)Math.Ceiling(wide / overviewMetres)), Math.Max(1, (int)Math.Ceiling(high / overviewMetres)), floor, walls);
            whole = true;
        }
        else if (1.0 / _scale < overviewMetres * 0.9)
        {
            Vector3d corner = ToWorld(new UiVec2(area.X, area.Y));
            OverheadView view = new(corner.X, corner.Y, 1.0 / _scale, (int)Math.Ceiling(area.Width), (int)Math.Ceiling(area.Height), floor, walls);
            if (_detail is not { } detail || !Same(detail.View, view))
            {
                wanted = view;
            }
        }

        if (wanted is not { } next)
        {
            return;
        }

        // A picture a released map no longer wants is drawn to the end and dropped: a tenth of a second.
        _rendering = Task.Run(() => (next, raster.Render(next), whole));
    }

    // Whether a picture already shows a view: the same storey and walls, scale and size, and within half a pixel of it.
    private static bool Same(OverheadView a, OverheadView b) =>
        a.Floor == b.Floor && a.Walls == b.Walls && a.Width == b.Width && a.Height == b.Height
        && Math.Abs(a.MetresPerPixel - b.MetresPerPixel) < a.MetresPerPixel * 1e-6
        && Math.Abs(a.WestMetres - b.WestMetres) < a.MetresPerPixel * 0.5 && Math.Abs(a.NorthMetres - b.NorthMetres) < a.MetresPerPixel * 0.5;

    // The level: the cast ground where the title gave none, the whole level at the storey, and the part in view over it
    // once it is drawn for this storey.
    private void DrawPicture(UiPainter painter)
    {
        if (_map is { } map && _image.IsValid)
        {
            UiVec2 topLeft = ToScreen(new Vector3d(map.WestMetres, map.NorthMetres, 0));
            float size = (float)(map.MetresPerPixel * _scale);
            painter.DrawSprite(_image, new UiRect(topLeft.X, topLeft.Y, map.Width * size, map.Height * size), new UiRect(0f, 0f, 1f, 1f), Bgra32.White);
        }

        foreach (Picture? picture in (Picture?[])[_overview, _detail])
        {
            if (picture is { } shown && shown.View.Floor == ShownFloor && shown.View.Walls == _planShown.IsChecked)
            {
                OverheadView view = shown.View;
                UiVec2 topLeft = ToScreen(new Vector3d(view.WestMetres, view.NorthMetres, 0));
                float size = (float)(view.MetresPerPixel * _scale);
                painter.DrawSprite(shown.Image, new UiRect(topLeft.X, topLeft.Y, view.Width * size, view.Height * size), new UiRect(0f, 0f, 1f, 1f), Bgra32.White);
            }
        }
    }

    // How far over the ground a point stands, in metres; NaN where the ground is not known.
    private double OverGround(Vector3d at) => _plan is { } plan ? at.Z - plan.Ground(at.X, at.Y) : double.NaN;

    // Whether a mark stands on the storey shown; on the roofs, everything does.
    private bool OnShownFloor(Vector3d at) => ShownFloor.IsRoofs || FloorPlan.IsOn(ShownFloor, OverGround(at));

    // Where something moved over the map comes to rest: on the storey shown at its new place, as high over it as it stood over
    // that storey at its old one; looking at the roofs, on the floor it stood on. Null when the plan cannot say.
    private double? HeightMovedTo(Vector3d from, Vector3d to)
    {
        if (_plan is not { } plan)
        {
            return _map?.HeightAt(from.X, from.Y) is { } before && _map.HeightAt(to.X, to.Y) is { } after ? from.Z + (after - before) : null;
        }

        double cut = ShownFloor.IsRoofs ? OverGround(from) : ShownFloor.Cut;
        if (double.IsNaN(cut))
        {
            return null;
        }

        double under = plan.Surface(from.X, from.Y, cut), there = plan.Surface(to.X, to.Y, cut);
        return double.IsNaN(under) || double.IsNaN(there) ? null : there + (from.Z - under);
    }

    // The line along the map's foot: where the pointer is, the floor under it on the storey shown, the scale.
    private void DrawStatus(UiPainter painter, UiRect area)
    {
        UiRect strip = new(area.X, area.Bottom - Theme.RowHeight, area.Width, Theme.RowHeight);
        painter.FillRect(strip, Bgra32.FromRgb(16, 18, 21).WithAlpha(215));
        string text = string.Create(CultureInfo.InvariantCulture, $"{ShownFloor.Name}   {1.0 / _scale:0.###} m/px");
        if (_pointerWorld is { } at)
        {
            string floor = "—";
            if (_plan is { } plan)
            {
                double ground = plan.Ground(at.X, at.Y);
                double surface = plan.Surface(at.X, at.Y, ShownFloor.IsRoofs ? double.PositiveInfinity : ShownFloor.Cut);
                floor = double.IsNaN(surface) ? "—" : string.Create(CultureInfo.InvariantCulture, $"{surface:0.0} m ({(double.IsNaN(ground) ? "—" : string.Create(CultureInfo.InvariantCulture, $"+{surface - ground:0.0} m"))})");
            }

            text = string.Create(CultureInfo.InvariantCulture, $"x {at.X:0.0} m   y {at.Y:0.0} m   floor {floor}   {text}");
        }

        if (_preparing is not null || _rendering is not null)
        {
            text += "   drawing…";
        }

        EditorDraw.Label(painter, Theme, text, strip with { X = strip.X + Theme.Px(8f), Width = strip.Width - Theme.Px(16f) }, Theme.TextMuted);
    }

    // A measured line and how far it is, on foot too.
    private void DrawMeasure(UiPainter painter)
    {
        if (_measured is not { } line)
        {
            return;
        }

        UiVec2 from = ToScreen(line.From), to = ToScreen(line.To);
        painter.StrokeLine(from, to, Theme.Px(2f), Bgra32.White);
        painter.FillDot(from, Theme.Px(3f), Bgra32.White);
        painter.FillDot(to, Theme.Px(3f), Bgra32.White);
        double metres = Math.Sqrt(((line.To.X - line.From.X) * (line.To.X - line.From.X)) + ((line.To.Y - line.From.Y) * (line.To.Y - line.From.Y)));
        string text = string.Create(CultureInfo.InvariantCulture, $"{metres:0.0} m  (~{metres / RunMetresPerSecond:0} s run)");
        EditorDraw.Label(painter, Theme, text, new UiRect(to.X + Theme.Px(10f), to.Y - (Theme.RowHeight * 0.5f), Theme.Font.MeasureWidth(text, Theme.TextSize) + Theme.Px(6f), Theme.RowHeight), Bgra32.White);
    }

    // A drawn picture and the view it shows.
    private sealed record Picture(UiImage Image, OverheadView View);
}
