using OpenIGI.Presentation;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Text;
using OpenIGI.Rendering;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// The remaster's map computer: a full-screen tactical interface drawn over a live overhead view of the
/// level, set in the shell's own faces.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no device.</b> The retail computer is a model held up in front of the camera with a 4:3
/// aperture cut in it, and everything the player came to read is inside that aperture — which at a modern
/// resolution is a postage stamp in the middle of the screen surrounded by a low-polygon casing. This
/// draws the map edge to edge and floats the interface over it, which is the whole reason the redesign
/// exists. <see cref="MapComputerRenderer"/> keeps the original for the vanilla path; the two do not
/// share code, because the one that must not change cannot be built out of the one that will.
/// </para>
/// <para>
/// <b>The map is always underneath.</b> Every page — the map, the objectives, the log — is drawn over the
/// same live overhead pass, with the text pages laid on a translucent card rather than on a black
/// rectangle. That is not decoration: it means switching pages never costs the player their sense of
/// where they are, and it means the level is still moving behind the objective they are reading.
/// </para>
/// <para>
/// <b>Everything is vector.</b> The markers, the grid, the scale bar and the cursor are rectangles,
/// triangles and text rather than sprites, so they are sharp at any resolution and can be coloured by
/// meaning — hostile, complete, selected — instead of by which frame of which sprite happened to be
/// authored. The one thing that would be worth a texture, the phosphor itself, is not drawn here at all:
/// it is a display transform applied to the resolved scene, and this interface is drawn crisp on top of
/// it, exactly as a real overlay on a real screen would be.
/// </para>
/// <para>
/// <b>Fonts are owned here.</b> The shell's faces are only built when the cinematic shell is, and the
/// computer has to work under <c>-vanillamenu</c> too, so this loads its own through
/// <see cref="UiFontLibrary"/>. The atlas cache on disk means that costs a file read rather than a
/// rasterisation.
/// </para>
/// </remarks>
internal sealed class TacticalComputerRenderer : ITacticalComputerPresenter
{
    private const string LogCategory = "Computer";

    /// <summary>How high above the ground the map is read from, in metres.</summary>
    /// <remarks>The retail altitude, kept: it is what the authored field-of-view range is calibrated to.</remarks>
    public const double AltitudeMetres = 900.0;

    private const double UnitsPerMetre = 4096.0;

    /// <summary>The widest the map opens out to, in radians — the retail overview.</summary>
    private const float WidestFieldOfView = 0.7853982f;

    /// <summary>The closest the map goes in, in radians.</summary>
    /// <remarks>
    /// About forty metres of ground across the frame, which is a building and its yard. The retail
    /// closest was a tenth of this and framed roughly a doorway, which is past the point where an
    /// overhead view of a level tells anyone anything.
    /// </remarks>
    private const float NarrowestFieldOfView = 0.0455f;

    /// <summary>Where the map opens: wide enough to hold a mission, close enough to read it.</summary>
    private const float DefaultZoom = 0.34f;

    /// <summary>How quickly the zoom eases towards what the wheel asked for, per second.</summary>
    private const float ZoomResponse = 16f;

    private static readonly string[] PageNames = ["MAP", "OBJECTIVES", "LOG"];
    private static readonly KeyCode[] PageKeys = [KeyCode.D1, KeyCode.D2, KeyCode.D3];

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IOpenIgiLogger _logger;
    private readonly Func<string, string> _resolveText;

    private UiFont? _body;
    private UiFont? _label;
    private UiFont? _display;

    private Page _page = Page.Map;

    /// <inheritdoc/>
    public bool MapAvailable { get; set; } = true;
    private Vector3d _centreUnits;
    private bool _hasCentre;
    private float _zoom = DefaultZoom;
    private float _zoomTarget = DefaultZoom;
    private float _seconds;
    private float _mouseX;
    private float _mouseY;
    private bool _dragging;
    private int _hoverPage = -1;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="TacticalComputerRenderer"/> class.</summary>
    /// <param name="device">The device the font atlases are uploaded to.</param>
    /// <param name="resources">The registry the mission's localised strings were preloaded into.</param>
    /// <param name="logger">Where to report a missing font.</param>
    /// <param name="resolveText">The fallback used for a string the archives do not carry.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public TacticalComputerRenderer(
        IGraphicsDevice device,
        ResourceRegistry resources,
        IOpenIgiLogger logger,
        Func<string, string> resolveText)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(resolveText);

        _device = device;
        _resources = resources;
        _logger = logger;
        _resolveText = resolveText;
    }

    /// <summary>Gets a value indicating whether the interface has its faces and can draw.</summary>
    public bool IsReady => _body is not null;

    /// <summary>Gets the vertical field of view the map is currently read at, in radians.</summary>
    public float FieldOfView => FieldOfViewFor(_zoom);

    /// <summary>Builds the interface's faces.</summary>
    /// <returns><see langword="false"/> when the fonts are missing and the computer cannot be shown.</returns>
    public bool Load()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!UiFontLibrary.TypographyAvailable)
        {
            _logger.Warning(LogCategory, $"No interface font at {UiFontLibrary.FontsDirectory}; the tactical computer cannot draw.");
            return false;
        }

        try
        {
            // The design's faces, shared with the menus and the HUD: Barlow for prose, the condensed
            // semibold for the letterspaced capitals the interface labels everything in, and the
            // distance-field title face for the wordmark, so it can carry an outline and a glow.
            IgiTypography typography = UiFontLibrary.Typography(_device);
            _body = typography.Body;
            _label = typography.Heading;
            _display = typography.Display;

            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            _logger.Warning(LogCategory, $"Could not build the tactical computer's fonts: {ex.Message}");
            return false;
        }
    }

    /// <summary>Starts a session centred on the player, at the mission-overview scale.</summary>
    /// <param name="playerUnits">Where the player is, in engine units.</param>
    public void Open(Vector3d playerUnits)
    {
        _centreUnits = playerUnits;
        _hasCentre = true;
        _zoom = DefaultZoom;
        _zoomTarget = DefaultZoom;
        _page = MapAvailable ? Page.Map : Page.Objectives;
        _dragging = false;
    }

    /// <summary>Takes a frame's input: page selection, pan, and cursor-anchored zoom.</summary>
    /// <param name="input">The frame's input.</param>
    /// <param name="elapsedSeconds">The real seconds since the last call.</param>
    /// <param name="playerUnits">Where the player is, for the recentre key.</param>
    /// <param name="screenWidth">The back buffer's width.</param>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    public void Update(InputState input, float elapsedSeconds, Vector3d playerUnits, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(input);

        float step = Math.Clamp(elapsedSeconds, 0f, 0.1f);
        _seconds += step;
        _mouseX = input.Mouse.X;
        _mouseY = input.Mouse.Y;

        if (!MapAvailable && _page == Page.Map)
        {
            _page = Page.Objectives;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        Chrome chrome = Measure(context, _page != Page.Map);

        _hoverPage = -1;
        for (int i = 0; i < PageNames.Length; i++)
        {
            if (IsSelectable((Page)i) && Contains(chrome.PageRow(i), _mouseX, _mouseY))
            {
                _hoverPage = i;
            }
        }

        if (input.Mouse.WasPressed(MouseButton.Left) && _hoverPage >= 0)
        {
            _page = (Page)_hoverPage;
        }

        for (int i = 0; i < PageKeys.Length; i++)
        {
            if (input.Keyboard.WasPressed(PageKeys[i]) && IsSelectable((Page)i))
            {
                _page = (Page)i;
            }
        }

        if (input.Keyboard.WasPressed(KeyCode.Tab))
        {
            do
            {
                _page = (Page)(((int)_page + 1) % PageNames.Length);
            }
            while (!IsSelectable(_page));
        }

        // Back to the player, which is the one navigation action a map that can be panned across a whole
        // level genuinely needs. Bound to both the obvious keys rather than one.
        if (input.Keyboard.WasPressed(KeyCode.Home) || input.Keyboard.WasPressed(KeyCode.Space))
        {
            _centreUnits = playerUnits;
        }

        bool overMap = !chrome.OverInterface(_mouseX, _mouseY);
        if (!input.Mouse.IsDown(MouseButton.Left) && !input.Mouse.IsDown(MouseButton.Middle))
        {
            _dragging = false;
        }
        else if (!_dragging
            && (input.Mouse.WasPressed(MouseButton.Left) || input.Mouse.WasPressed(MouseButton.Middle))
            && (overMap || input.Mouse.IsDown(MouseButton.Middle)))
        {
            _dragging = true;
        }

        if (input.Mouse.WheelDelta != 0f && (overMap || _page == Page.Map))
        {
            _zoomTarget = Math.Clamp(_zoomTarget + (input.Mouse.WheelDelta * 0.085f), 0f, 1f);
        }

        if (input.Keyboard.WasPressed(KeyCode.Equals))
        {
            _zoomTarget = Math.Clamp(_zoomTarget + 0.12f, 0f, 1f);
        }

        if (input.Keyboard.WasPressed(KeyCode.Minus))
        {
            _zoomTarget = Math.Clamp(_zoomTarget - 0.12f, 0f, 1f);
        }

        Viewport map = MapViewport(screenWidth, screenHeight);
        float previousFieldOfView = FieldOfViewFor(_zoom);

        // Exponential smoothing framed as a half-life rather than a per-frame factor, so the zoom takes
        // the same real time to arrive whatever the frame rate is.
        _zoom += (_zoomTarget - _zoom) * (1f - MathF.Exp(-ZoomResponse * step));
        float fieldOfView = FieldOfViewFor(_zoom);

        // Cursor-anchored zoom: whatever is under the pointer stays under it. Done by unprojecting the
        // same pixel through the old and the new frustum and moving the centre by the difference, which
        // is exact at every intermediate step of the ease rather than only at its ends.
        if (MathF.Abs(fieldOfView - previousFieldOfView) > 1e-7f)
        {
            Vector3d before = Unproject(_mouseX, _mouseY, _centreUnits, previousFieldOfView, map);
            Vector3d after = Unproject(_mouseX, _mouseY, _centreUnits, fieldOfView, map);
            _centreUnits = new Vector3d(
                _centreUnits.X + (before.X - after.X),
                _centreUnits.Y + (before.Y - after.Y),
                _centreUnits.Z);
        }

        if (_dragging && (input.Mouse.DeltaX != 0f || input.Mouse.DeltaY != 0f))
        {
            (double unitsPerPixelX, double unitsPerPixelY) = UnitsPerPixel(fieldOfView, map);
            _centreUnits = new Vector3d(
                _centreUnits.X - (input.Mouse.DeltaX * unitsPerPixelX),
                _centreUnits.Y + (input.Mouse.DeltaY * unitsPerPixelY),
                _centreUnits.Z);
        }
    }

    /// <summary>Builds the straight-down view the map is read through.</summary>
    /// <param name="playerUnits">Where the player is, used only until a centre has been chosen.</param>
    /// <param name="screenWidth">The back buffer's width.</param>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <returns>The camera, its field of view, and the full-screen viewport it fills.</returns>
    public MapSceneView SceneView(Vector3d playerUnits, int screenWidth, int screenHeight)
    {
        if (!_hasCentre)
        {
            _centreUnits = playerUnits;
            _hasCentre = true;
        }

        return new MapSceneView(Vantage().ToCamera(), FieldOfViewFor(_zoom), MapViewport(screenWidth, screenHeight));
    }

    /// <summary>Gets the pose the map is read from, for the transition to fly to and from.</summary>
    /// <returns>The straight-down vantage over the map's centre.</returns>
    public CameraPose Vantage()
    {
        Vector3d centre = new(
            _centreUnits.X / UnitsPerMetre,
            _centreUnits.Y / UnitsPerMetre,
            _centreUnits.Z / UnitsPerMetre);

        return new CameraPose(
            new Vector3d(centre.X, centre.Y, centre.Z + AltitudeMetres),
            0f,
            MapComputerCamera.MapPitch);
    }

    /// <summary>Draws the interface over the overhead view already on screen.</summary>
    /// <param name="batch">The batch to draw into; it must already be between Begin and End.</param>
    /// <param name="runtime">The mission's objectives and event log.</param>
    /// <param name="player">Where the player is, in engine units.</param>
    /// <param name="guards">The hostile contacts the map is allowed to show, in engine units.</param>
    /// <param name="highlights">The authored points of interest currently visible.</param>
    /// <param name="screenWidth">The back buffer's width.</param>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <param name="opacity">How much of the interface is showing, 0 to 1, as the display comes up.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public void Draw(
        SpriteBatch batch,
        MissionObjectiveRuntime runtime,
        Vector3d player,
        IReadOnlyList<Vector3d> guards,
        IEnumerable<ComputerMapHighlight> highlights,
        int screenWidth,
        int screenHeight,
        float opacity)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(guards);
        ArgumentNullException.ThrowIfNull(highlights);

        if (_body is not { } body || opacity <= 0.004f)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);
        Chrome chrome = Measure(context, _page != Page.Map);
        Viewport map = MapViewport(screenWidth, screenHeight);
        float fieldOfView = FieldOfViewFor(_zoom);

        // The whole interface fades and rises together. A few pixels of travel is the difference between
        // the screen having been switched on and the interface having been brought up on it.
        painter.PushOpacity(opacity);
        float lift = (1f - opacity) * context.Px(14f);

        DrawMapOverlay(painter, context, chrome, map, fieldOfView, runtime, player, guards, highlights);

        if (_page != Page.Map)
        {
            DrawPagePanel(painter, context, chrome, body, runtime, lift);
        }

        DrawHeader(painter, context, chrome, body, runtime, guards.Count, lift);
        DrawRail(painter, context, chrome, body, lift);
        DrawStatusBlock(painter, context, chrome, body, player, map, fieldOfView, lift);
        DrawFooter(painter, context, chrome, body, map, fieldOfView, lift);

        painter.PopOpacity();

        // The cursor is outside the fade. It is the player's own pointer rather than part of the screen
        // being brought up, and one that faded in with the chrome would leave them without one for the
        // half-second the display takes to arrive.
        DrawCursor(painter, context, chrome.OverInterface(_mouseX, _mouseY));
    }

    /// <summary>Draws the mission-failed panel in the interface's own language.</summary>
    /// <param name="batch">The batch to draw into; it must already be between Begin and End.</param>
    /// <param name="screenWidth">The back buffer's width.</param>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is <see langword="null"/>.</exception>
    public void DrawMissionFailed(SpriteBatch batch, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (_body is not { } body)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);
        UiFont label = _label ?? body;

        painter.FillRect(new UiRect(0f, 0f, screenWidth, screenHeight), UiPalette.InkAt(0.84f));

        float width = context.Px(620f);
        float height = context.Px(240f);
        var card = new UiRect((screenWidth - width) * 0.5f, (screenHeight - height) * 0.5f, width, height);

        painter.FillRect(card, PanelSolid);
        painter.StrokeRect(card, context.Px(1f), UiPalette.Danger.WithAlpha(110));
        painter.FillRect(new UiRect(card.X, card.Y, card.Width, context.Px(3f)), Hostile);

        float pad = context.Px(40f);
        DrawTracked(painter, label, "MISSION FAILED", new UiVec2(card.X + pad, card.Y + context.Px(44f)),
            context.Px(34f), Hostile, context.Px(3.5f));

        painter.FillRect(
            new UiRect(card.X + pad, card.Y + context.Px(102f), card.Width - (pad * 2f), MathF.Max(1f, context.Px(1f))),
            Border);

        DrawKeyHint(painter, context, body, "ENTER", "RESTART MISSION",
            new UiVec2(card.X + pad, card.Y + context.Px(132f)));
        DrawKeyHint(painter, context, body, "ESC", "ABORT TO MENU",
            new UiVec2(card.X + pad, card.Y + context.Px(176f)));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // The faces are the device's shared set, not this renderer's to free.
        _body = null;
        _label = null;
        _display = null;
    }

    // ---------------------------------------------------------------------------------------------
    // The map itself.
    // ---------------------------------------------------------------------------------------------

    private void DrawMapOverlay(
        UiPainter painter,
        LayoutContext context,
        in Chrome chrome,
        Viewport map,
        float fieldOfView,
        MissionObjectiveRuntime runtime,
        Vector3d player,
        IReadOnlyList<Vector3d> guards,
        IEnumerable<ComputerMapHighlight> highlights)
    {
        DrawGrid(painter, context, map, fieldOfView);

        // Points of interest first, so a hostile standing on one is drawn over its label rather than
        // under it. Labels are suppressed once they would collide, nearest-authored winning, because a
        // map whose annotations overlap is less readable than one that is missing a few.
        List<(float X, float Y)> taken = [];
        float labelGuardX = context.Px(150f);
        float labelGuardY = context.Px(26f);
        foreach (ComputerMapHighlight highlight in highlights)
        {
            if (!TryProject(highlight.Position, _centreUnits, fieldOfView, map, out float hx, out float hy))
            {
                continue;
            }

            DrawDiamond(painter, new UiVec2(hx, hy), context.Px(6f), Signal.WithAlpha(210));
            DrawDiamond(painter, new UiVec2(hx, hy), context.Px(2.5f), UiPalette.TextBright);

            bool clear = taken.All(point =>
                MathF.Abs(point.X - hx) > labelGuardX || MathF.Abs(point.Y - hy) > labelGuardY);
            string title = ComputerString(highlight.TitleResource);
            if (clear && !string.IsNullOrWhiteSpace(title) && !chrome.OverInterface(hx, hy))
            {
                DrawLabelChip(painter, context, title, new UiVec2(hx + context.Px(12f), hy - context.Px(9f)));
                taken.Add((hx, hy));
            }
        }

        foreach (MissionObjective objective in runtime.Objectives)
        {
            if (objective.Position is not { } position || objective.State == MissionObjectiveState.Failed)
            {
                continue;
            }

            if (TryProject(position, _centreUnits, fieldOfView, map, out float ox, out float oy))
            {
                DrawObjectiveMarker(
                    painter,
                    context,
                    new UiVec2(ox, oy),
                    objective.State == MissionObjectiveState.Complete);
            }
        }

        foreach (Vector3d guard in guards)
        {
            if (TryProject(guard, _centreUnits, fieldOfView, map, out float gx, out float gy))
            {
                DrawHostileMarker(painter, context, new UiVec2(gx, gy));
            }
        }

        if (TryProject(player, _centreUnits, fieldOfView, map, out float px, out float py))
        {
            DrawPlayerMarker(painter, context, new UiVec2(px, py));
        }
    }

    /// <summary>Draws the world-aligned reference grid the map is read against.</summary>
    /// <remarks>
    /// Aligned to the world rather than to the screen, so it pans and zooms with the terrain and is a
    /// measure of it instead of a texture over it. The spacing steps through a 1-2-5 sequence — the one
    /// every chart and every map in the world uses — so a division is always a number a person can hold,
    /// and every fifth line is drawn brighter to give the eye something to count in.
    /// </remarks>
    private void DrawGrid(UiPainter painter, LayoutContext context, Viewport map, float fieldOfView)
    {
        double halfHeightMetres = AltitudeMetres * Math.Tan(fieldOfView * 0.5);
        double metresPerPixel = 2.0 * halfHeightMetres / Math.Max(1, map.Height);
        double spacing = NiceStep(metresPerPixel * context.Px(120f));
        double spacingUnits = spacing * UnitsPerMetre;
        if (spacingUnits <= 0.0)
        {
            return;
        }

        float thin = MathF.Max(1f, context.Px(1f));
        Bgra32 minor = UiPalette.White(0.05f);
        Bgra32 major = UiPalette.White(0.11f);

        (double unitsPerPixelX, double unitsPerPixelY) = UnitsPerPixel(fieldOfView, map);
        double leftUnits = _centreUnits.X - (unitsPerPixelX * map.Width * 0.5);
        double topUnits = _centreUnits.Y + (unitsPerPixelY * map.Height * 0.5);

        // NiceStep is chosen so a division is about 120 design units wide, which bounds the count at a
        // couple of dozen per axis; the cap is only there so a degenerate viewport or a zoom that has run
        // to a denormal cannot turn a grid into a hang.
        const int MaxLines = 256;

        long firstColumn = (long)Math.Floor(leftUnits / spacingUnits);
        for (int drawn = 0; drawn < MaxLines; drawn++)
        {
            long i = firstColumn + drawn;
            float x = map.X + (float)(((i * spacingUnits) - leftUnits) / unitsPerPixelX);
            if (x > map.X + map.Width)
            {
                break;
            }

            if (x >= map.X)
            {
                painter.FillRect(new UiRect(x, map.Y, thin, map.Height), (i % 5) == 0 ? major : minor);
            }
        }

        long firstRow = (long)Math.Floor(topUnits / spacingUnits);
        for (int drawn = 0; drawn < MaxLines; drawn++)
        {
            long i = firstRow - drawn;
            float y = map.Y + (float)((topUnits - (i * spacingUnits)) / unitsPerPixelY);
            if (y > map.Y + map.Height)
            {
                break;
            }

            if (y >= map.Y)
            {
                painter.FillRect(new UiRect(map.X, y, map.Width, thin), (i % 5) == 0 ? major : minor);
            }
        }
    }

    private void DrawPlayerMarker(UiPainter painter, LayoutContext context, UiVec2 at)
    {
        // A slow breath rather than a blink. The player's own marker is the one thing on the map that has
        // to be findable without being looked for, and a pulse does that where a brighter static dot only
        // competes with everything else that is also bright.
        float pulse = 0.5f + (0.5f * MathF.Sin(_seconds * 2.4f));
        float radius = context.Px(9f);

        StrokeRing(painter, at, radius + (context.Px(7f) * pulse), context.Px(1.5f),
            Accent.WithAlpha((byte)(90 * (1f - pulse))));
        StrokeRing(painter, at, radius, context.Px(2f), Accent);
        FillDisc(painter, at, context.Px(4f), UiPalette.AccentLight);

        // Four brackets rather than a full ring, so the marker reads as a fix on a position instead of as
        // one more circle among the objective markers.
        float arm = context.Px(16f);
        float gap = context.Px(11f);
        float weight = MathF.Max(1f, context.Px(1.5f));
        painter.FillRect(new UiRect(at.X - arm, at.Y - (weight * 0.5f), arm - gap, weight), Accent.WithAlpha(190));
        painter.FillRect(new UiRect(at.X + gap, at.Y - (weight * 0.5f), arm - gap, weight), Accent.WithAlpha(190));
        painter.FillRect(new UiRect(at.X - (weight * 0.5f), at.Y - arm, weight, arm - gap), Accent.WithAlpha(190));
        painter.FillRect(new UiRect(at.X - (weight * 0.5f), at.Y + gap, weight, arm - gap), Accent.WithAlpha(190));
    }

    private static void DrawHostileMarker(UiPainter painter, LayoutContext context, UiVec2 at)
    {
        DrawDiamond(painter, at, context.Px(8f), Bgra32.FromRgba(40, 10, 8, 220));
        DrawDiamond(painter, at, context.Px(6f), Hostile);
        DrawDiamond(painter, at, context.Px(2f), UiPalette.Danger);
    }

    private static void DrawObjectiveMarker(UiPainter painter, LayoutContext context, UiVec2 at, bool complete)
    {
        Bgra32 colour = complete ? Complete : Accent;
        float half = context.Px(9f);
        float tick = context.Px(5f);
        float weight = MathF.Max(1f, context.Px(1.8f));

        // Corner ticks: a target rather than a box. Four short pairs of rectangles, which is both cheaper
        // and more legible at small sizes than an outlined square.
        foreach ((float sx, float sy) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
        {
            float cornerX = at.X + (half * sx);
            float cornerY = at.Y + (half * sy);
            painter.FillRect(
                new UiRect(sx < 0f ? cornerX : cornerX - tick, cornerY - (weight * 0.5f), tick, weight), colour);
            painter.FillRect(
                new UiRect(cornerX - (weight * 0.5f), sy < 0f ? cornerY : cornerY - tick, weight, tick), colour);
        }

        if (complete)
        {
            FillDisc(painter, at, context.Px(3.5f), colour, 10);
        }
        else
        {
            StrokeRing(painter, at, context.Px(3.5f), weight, colour, 10);
        }
    }

    private void DrawLabelChip(UiPainter painter, LayoutContext context, string text, UiVec2 at)
    {
        UiFont font = _label ?? _body!;
        float size = context.Px(14f);
        float tracking = context.Px(1.2f);
        float width = MeasureTracked(font, text, size, tracking);
        float padX = context.Px(8f);
        float padY = context.Px(5f);
        float height = font.LineHeight(size) + (padY * 2f);

        var chip = new UiRect(at.X, at.Y - (height * 0.5f), width + (padX * 2f) + context.Px(3f), height);
        painter.FillRect(chip, UiPalette.Ink.WithAlpha(190));
        painter.FillRect(new UiRect(chip.X, chip.Y, context.Px(2f), chip.Height), Signal.WithAlpha(200));
        DrawTracked(painter, font, text, new UiVec2(chip.X + padX + context.Px(3f), chip.Y + padY), size,
            UiPalette.Text, tracking);
    }

    // ---------------------------------------------------------------------------------------------
    // The chrome.
    // ---------------------------------------------------------------------------------------------

    private void DrawHeader(
        UiPainter painter,
        LayoutContext context,
        in Chrome chrome,
        UiFont body,
        MissionObjectiveRuntime runtime,
        int contacts,
        float lift)
    {
        UiRect bar = chrome.Header with { Y = chrome.Header.Y - lift };
        UiFont label = _label ?? body;

        painter.FillRect(bar, Panel);
        painter.FillRect(new UiRect(bar.X, bar.Bottom - MathF.Max(1f, context.Px(1f)), bar.Width, MathF.Max(1f, context.Px(1f))), Border);

        float x = chrome.Gutter;
        float mid = bar.Y + (bar.Height * 0.5f);

        // The system mark: a small solid block, then the name. The block is doing real work — it is what
        // the eye lands on first and what tells it where the interface starts.
        float block = context.Px(16f);
        painter.FillRect(new UiRect(x, mid - (block * 0.5f), block, block), Signal);
        painter.FillRect(new UiRect(x + context.Px(4f), mid - (block * 0.5f) + context.Px(4f), block - context.Px(8f), block - context.Px(8f)), PanelSolid);
        x += block + context.Px(14f);

        float markSize = context.Px(26f);
        if (_display is { } display)
        {
            SdfTextParams effects = SdfTextParams.Plain(0f)
                .WithOutline(Bgra32.FromRgba(0, 0, 0, 170), 0.16f)
                .WithGlow(Signal, 0.4f, 0.55f);
            painter.DrawText(display, "SIMOTECH", new UiVec2(x, mid - (display.LineHeight(markSize) * 0.5f)),
                markSize, UiPalette.TextBright, effects);
            x += display.MeasureWidth("SIMOTECH", markSize) + context.Px(16f);
        }
        else
        {
            x += DrawTracked(painter, label, "SIMOTECH", new UiVec2(x, mid - (label.LineHeight(markSize) * 0.5f)),
                markSize, UiPalette.TextBright, context.Px(2f)) + context.Px(16f);
        }

        painter.FillRect(new UiRect(x, mid - context.Px(10f), MathF.Max(1f, context.Px(1f)), context.Px(20f)), Border);
        x += context.Px(16f);

        float subSize = context.Px(13f);
        DrawTracked(painter, label, "TACTICAL MAP INTERFACE",
            new UiVec2(x, mid - (label.LineHeight(subSize) * 0.5f)), subSize, TextMuted, context.Px(2.4f));

        // The readouts, right-aligned and laid out from the right edge inwards so the widest one does not
        // push the others off the screen on a narrow window.
        int complete = runtime.Objectives.Count(objective => objective.State == MissionObjectiveState.Complete);
        float chipRight = bar.Right - chrome.Gutter;
        chipRight = DrawReadout(
            painter,
            context,
            label,
            chipRight,
            mid,
            "CONTACTS",
            contacts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Hostile);
        chipRight -= context.Px(14f);
        DrawReadout(painter, context, label, chipRight, mid, "OBJECTIVES",
            $"{complete}/{runtime.Objectives.Count}", complete >= runtime.Objectives.Count && runtime.Objectives.Count > 0 ? Complete : Signal);
    }

    private static float DrawReadout(
        UiPainter painter,
        LayoutContext context,
        UiFont label,
        float right,
        float middle,
        string caption,
        string value,
        Bgra32 valueColour)
    {
        float captionSize = context.Px(11f);
        float valueSize = context.Px(19f);
        float tracking = context.Px(1.8f);
        float captionWidth = MeasureTracked(label, caption, captionSize, tracking);
        float valueWidth = MeasureTracked(label, value, valueSize, tracking);
        float width = MathF.Max(captionWidth, valueWidth);
        float left = right - width;

        DrawTracked(painter, label, caption, new UiVec2(right - captionWidth, middle - context.Px(17f)),
            captionSize, TextMuted, tracking);
        DrawTracked(painter, label, value, new UiVec2(right - valueWidth, middle - context.Px(3f)),
            valueSize, valueColour, tracking);
        return left;
    }

    private void DrawRail(UiPainter painter, LayoutContext context, in Chrome chrome, UiFont body, float lift)
    {
        UiFont label = _label ?? body;
        UiRect card = chrome.Rail with { X = chrome.Rail.X - lift };

        painter.FillRect(card, Panel);
        painter.StrokeRect(card, MathF.Max(1f, context.Px(1f)), Border);

        for (int i = 0; i < PageNames.Length; i++)
        {
            UiRect row = chrome.PageRow(i) with { X = chrome.PageRow(i).X - lift };
            bool selected = (int)_page == i;
            bool hovered = _hoverPage == i;

            if (selected)
            {
                painter.FillRect(row, UiPalette.AccentAt(0.13f));
                painter.FillRect(new UiRect(row.X, row.Y, context.Px(3f), row.Height), Accent);
            }
            else if (hovered)
            {
                painter.FillRect(row, UiPalette.White(0.06f));
            }

            // A page the level has not given the player stays listed, greyed, the way the original leaves
            // its MAP link out rather than renumbering what is left.
            Bgra32 colour = selected ? UiPalette.AccentLight : hovered ? Text : TextMuted;
            if (!IsSelectable((Page)i))
            {
                colour = TextMuted.WithAlpha(70);
            }

            float indexSize = context.Px(11f);
            float nameSize = context.Px(16f);
            float textX = row.X + context.Px(20f);

            DrawTracked(painter, label, $"0{i + 1}", new UiVec2(textX, row.Y + context.Px(9f)),
                indexSize, selected ? Accent : TextMuted.WithAlpha(150), context.Px(1.4f));
            DrawTracked(painter, label, PageNames[i], new UiVec2(textX + context.Px(34f), row.Y + context.Px(7f)),
                nameSize, colour, context.Px(2.2f));
        }
    }

    private void DrawStatusBlock(
        UiPainter painter,
        LayoutContext context,
        in Chrome chrome,
        UiFont body,
        Vector3d player,
        Viewport map,
        float fieldOfView,
        float lift)
    {
        UiFont label = _label ?? body;
        UiRect card = chrome.Status with { X = chrome.Status.X - lift };

        painter.FillRect(card, Panel);
        painter.StrokeRect(card, MathF.Max(1f, context.Px(1f)), Border);

        float pad = context.Px(20f);
        float y = card.Y + context.Px(16f);
        DrawTracked(painter, label, "OPERATOR", new UiVec2(card.X + pad, y), context.Px(11f), TextMuted, context.Px(2f));
        y += context.Px(20f);

        // The player's own position, in metres east and north of the level's origin. The engine keeps it
        // in 1/4096 of a metre, which is a number nobody can read; this is that number made into a grid
        // reference, which is what a map is for.
        long east = (long)Math.Round(player.X / UnitsPerMetre);
        long north = (long)Math.Round(player.Y / UnitsPerMetre);
        long altitude = (long)Math.Round(player.Z / UnitsPerMetre);
        DrawTracked(painter, label, $"E {east:0000}  N {north:0000}", new UiVec2(card.X + pad, y),
            context.Px(16f), Signal, context.Px(1.2f));
        y += context.Px(26f);
        DrawTracked(painter, label, $"ALT {altitude} M", new UiVec2(card.X + pad, y),
            context.Px(13f), TextMuted, context.Px(1.4f));

        y += context.Px(26f);
        painter.FillRect(new UiRect(card.X + pad, y, card.Width - (pad * 2f), MathF.Max(1f, context.Px(1f))), Border);
        y += context.Px(14f);

        // The scale as a ratio, which is the one number that says how far in the map is without the
        // player having to measure the bar.
        double halfHeightMetres = AltitudeMetres * Math.Tan(fieldOfView * 0.5);
        double metresPerPixel = 2.0 * halfHeightMetres / Math.Max(1, map.Height);
        DrawTracked(painter, label, "SCALE", new UiVec2(card.X + pad, y), context.Px(11f), TextMuted, context.Px(2f));
        DrawTracked(painter, label, $"1:{Math.Round(metresPerPixel * 1000.0):0}", new UiVec2(card.X + pad + context.Px(74f), y),
            context.Px(11f), Text, context.Px(1.4f));
    }

    private void DrawFooter(
        UiPainter painter,
        LayoutContext context,
        in Chrome chrome,
        UiFont body,
        Viewport map,
        float fieldOfView,
        float lift)
    {
        UiFont label = _label ?? body;
        UiRect bar = chrome.Footer with { Y = chrome.Footer.Y + lift };

        painter.FillRect(bar, Panel);
        painter.FillRect(new UiRect(bar.X, bar.Y, bar.Width, MathF.Max(1f, context.Px(1f))), Border);

        float mid = bar.Y + (bar.Height * 0.5f);
        float x = chrome.Gutter;
        x = DrawKeyCap(painter, context, label, "C", "CLOSE", x, mid);
        x = DrawKeyCap(painter, context, label, "1-3", "SECTION", x, mid);
        x = DrawKeyCap(painter, context, label, "DRAG", "PAN", x, mid);
        x = DrawKeyCap(painter, context, label, "WHEEL", "ZOOM", x, mid);
        DrawKeyCap(painter, context, label, "SPACE", "RECENTRE", x, mid);

        DrawScaleBar(painter, context, label, bar, map, fieldOfView);
    }

    /// <summary>Draws the bar that says how far across the map a given distance is.</summary>
    /// <remarks>
    /// A ratio tells the player how far in they are; a bar tells them how far away something is, which is
    /// the question a map is actually asked. Its length is chosen from the 1-2-5 sequence so the number
    /// under it is always round, and the bar is whatever width that number turns out to be rather than a
    /// fixed width with an awkward number under it.
    /// </remarks>
    private static void DrawScaleBar(
        UiPainter painter,
        LayoutContext context,
        UiFont label,
        UiRect bar,
        Viewport map,
        float fieldOfView)
    {
        double halfHeightMetres = AltitudeMetres * Math.Tan(fieldOfView * 0.5);
        double metresPerPixel = 2.0 * halfHeightMetres / Math.Max(1, map.Height);
        double metres = NiceStep(metresPerPixel * context.Px(170f));
        float width = (float)(metres / Math.Max(metresPerPixel, 1e-9));
        if (!float.IsFinite(width) || width <= 0f)
        {
            return;
        }

        float right = bar.Right - context.Px(28f);
        float left = right - width;
        float mid = bar.Y + (bar.Height * 0.5f);
        float weight = MathF.Max(1f, context.Px(1.5f));
        float tick = context.Px(6f);

        painter.FillRect(new UiRect(left, mid + context.Px(3f), width, weight), Signal.WithAlpha(200));
        painter.FillRect(new UiRect(left, mid + context.Px(3f) - tick, weight, tick + weight), Signal.WithAlpha(200));
        painter.FillRect(new UiRect(right - weight, mid + context.Px(3f) - tick, weight, tick + weight), Signal.WithAlpha(200));

        string text = metres >= 1000.0 ? $"{metres / 1000.0:0.#} KM" : $"{metres:0} M";
        float size = context.Px(12f);
        float tracking = context.Px(1.6f);
        DrawTracked(painter, label, text,
            new UiVec2(right - MeasureTracked(label, text, size, tracking), mid - context.Px(14f)),
            size, TextMuted, tracking);
    }

    private static float DrawKeyCap(
        UiPainter painter,
        LayoutContext context,
        UiFont label,
        string key,
        string action,
        float x,
        float middle)
    {
        float keySize = context.Px(12f);
        float actionSize = context.Px(12f);
        float tracking = context.Px(1.2f);
        float keyWidth = MeasureTracked(label, key, keySize, tracking);
        float padX = context.Px(7f);
        float capHeight = context.Px(20f);

        var cap = new UiRect(x, middle - (capHeight * 0.5f), keyWidth + (padX * 2f), capHeight);
        painter.FillRect(cap, UiPalette.White(0.1f));
        painter.StrokeRect(cap, MathF.Max(1f, context.Px(1f)), Border);
        DrawTracked(painter, label, key, new UiVec2(cap.X + padX, cap.Y + context.Px(3f)), keySize, Text, tracking);

        float textX = cap.Right + context.Px(9f);
        float actionWidth = DrawTracked(painter, label, action,
            new UiVec2(textX, middle - (label.LineHeight(actionSize) * 0.5f)), actionSize, TextMuted, context.Px(2f));

        return textX + actionWidth + context.Px(26f);
    }

    private static void DrawKeyHint(UiPainter painter, LayoutContext context, UiFont font, string key, string action, UiVec2 at)
    {
        float size = context.Px(15f);
        float width = font.MeasureWidth(key, size);
        painter.FillRect(new UiRect(at.X, at.Y - context.Px(3f), width + context.Px(16f), context.Px(26f)),
            UiPalette.White(0.1f));
        painter.DrawText(font, key, new UiVec2(at.X + context.Px(8f), at.Y), size, Text);
        painter.DrawText(font, action, new UiVec2(at.X + width + context.Px(32f), at.Y), size, TextMuted);
    }

    // ---------------------------------------------------------------------------------------------
    // The text pages.
    // ---------------------------------------------------------------------------------------------

    private void DrawPagePanel(
        UiPainter painter,
        LayoutContext context,
        in Chrome chrome,
        UiFont body,
        MissionObjectiveRuntime runtime,
        float lift)
    {
        UiRect card = chrome.Page with { Y = chrome.Page.Y + lift };
        UiFont label = _label ?? body;

        painter.FillRect(card, PanelSolid);
        painter.StrokeRect(card, MathF.Max(1f, context.Px(1f)), Border);
        painter.FillRect(new UiRect(card.X, card.Y, card.Width, MathF.Max(1f, context.Px(2f))), Signal.WithAlpha(160));

        float pad = context.Px(34f);
        float y = card.Y + context.Px(26f);
        DrawTracked(painter, label, PageNames[(int)_page], new UiVec2(card.X + pad, y),
            context.Px(20f), Text, context.Px(3.2f));
        y += context.Px(38f);
        painter.FillRect(new UiRect(card.X + pad, y, card.Width - (pad * 2f), MathF.Max(1f, context.Px(1f))), Border);
        y += context.Px(24f);

        painter.PushClip(card);
        if (_page == Page.Objectives)
        {
            DrawObjectives(painter, context, body, label, runtime, card, pad, y);
        }
        else
        {
            DrawLog(painter, context, body, label, runtime, card, pad, y);
        }

        painter.PopClip();
    }

    private void DrawObjectives(
        UiPainter painter,
        LayoutContext context,
        UiFont body,
        UiFont label,
        MissionObjectiveRuntime runtime,
        UiRect card,
        float pad,
        float y)
    {
        if (runtime.Objectives.Count == 0)
        {
            DrawTracked(painter, label, "NO OBJECTIVES ON FILE", new UiVec2(card.X + pad, y),
                context.Px(14f), TextMuted, context.Px(2f));
            return;
        }

        float textSize = context.Px(17f);
        float rowPad = context.Px(18f);
        float width = card.Width - (pad * 2f);
        float textLeft = card.X + pad + context.Px(58f);
        float textWidth = width - context.Px(58f) - context.Px(140f);

        for (int i = 0; i < runtime.Objectives.Count; i++)
        {
            MissionObjective objective = runtime.Objectives[i];
            string text = ComputerString(objective.TextResource);
            string wrapped = body.WrapText(text, textSize, MathF.Max(context.Px(120f), textWidth));
            float textHeight = body.Measure(wrapped, textSize).Y;
            float rowHeight = MathF.Max(context.Px(58f), textHeight + (rowPad * 2f));
            var row = new UiRect(card.X + pad, y, width, rowHeight);

            (Bgra32 accent, string state) = objective.State switch
            {
                MissionObjectiveState.Complete => (Complete, "COMPLETE"),
                MissionObjectiveState.Failed => (Hostile, "FAILED"),
                _ => (Accent, "ACTIVE"),
            };

            painter.FillRect(row, UiPalette.White(0.05f));
            painter.FillRect(new UiRect(row.X, row.Y, context.Px(3f), row.Height), accent);

            DrawTracked(painter, label, $"0{i + 1}", new UiVec2(row.X + context.Px(20f), row.Y + rowPad),
                context.Px(15f), accent, context.Px(1.6f));

            painter.DrawText(body, wrapped, new UiVec2(textLeft, row.Y + rowPad), textSize,
                objective.State == MissionObjectiveState.Complete ? TextMuted : Text);

            DrawStatusPill(painter, context, label, state, accent,
                new UiVec2(row.Right - context.Px(20f), row.Y + rowPad));

            y += rowHeight + context.Px(10f);
        }
    }

    private void DrawLog(
        UiPainter painter,
        LayoutContext context,
        UiFont body,
        UiFont label,
        MissionObjectiveRuntime runtime,
        UiRect card,
        float pad,
        float y)
    {
        if (runtime.Log.Count == 0)
        {
            DrawTracked(painter, label, "NO TRANSMISSIONS LOGGED", new UiVec2(card.X + pad, y),
                context.Px(14f), TextMuted, context.Px(2f));
            return;
        }

        float textSize = context.Px(16f);
        float railX = card.X + pad + context.Px(6f);
        float textLeft = railX + context.Px(28f);
        float textWidth = card.Right - pad - textLeft;
        float top = y;

        // Newest first: the last thing that happened is the thing being looked for, and a log that reads
        // downwards makes the player scroll to find out what they were just told.
        int index = 0;
        foreach (MissionComputerLogEntry entry in runtime.Log.Reverse())
        {
            if (y > card.Bottom - context.Px(30f))
            {
                break;
            }

            string wrapped = body.WrapText(ComputerString(entry.TextResource), textSize, MathF.Max(context.Px(120f), textWidth));
            float height = body.Measure(wrapped, textSize).Y;

            // The newest entry is the only one at full strength. Older ones step back a little each time
            // and then hold, so recency is legible at a glance without the tail becoming unreadable.
            byte alpha = (byte)(index == 0 ? 255 : Math.Max(120, 210 - (index * 18)));
            FillDisc(painter, new UiVec2(railX, y + context.Px(9f)), context.Px(index == 0 ? 4f : 2.5f),
                (index == 0 ? Signal : SignalDim).WithAlpha(alpha), 10);
            painter.DrawText(body, wrapped, new UiVec2(textLeft, y), textSize,
                (index == 0 ? Text : TextMuted).WithAlpha(alpha));

            y += height + context.Px(16f);
            index++;
        }

        // The rail behind the dots, drawn last so it runs exactly as far as the entries did.
        painter.FillRect(
            new UiRect(railX - MathF.Max(1f, context.Px(0.5f)), top + context.Px(9f), MathF.Max(1f, context.Px(1f)),
                MathF.Max(0f, y - top - context.Px(25f))),
            Border);
    }

    private static void DrawStatusPill(
        UiPainter painter,
        LayoutContext context,
        UiFont label,
        string text,
        Bgra32 colour,
        UiVec2 rightTop)
    {
        float size = context.Px(11f);
        float tracking = context.Px(1.8f);
        float width = MeasureTracked(label, text, size, tracking);
        float padX = context.Px(10f);
        float height = context.Px(22f);

        var pill = new UiRect(rightTop.X - width - (padX * 2f), rightTop.Y, width + (padX * 2f), height);
        painter.FillRect(pill, colour.WithAlpha(38));
        painter.StrokeRect(pill, MathF.Max(1f, context.Px(1f)), colour.WithAlpha(120));
        DrawTracked(painter, label, text, new UiVec2(pill.X + padX, pill.Y + context.Px(4f)), size, colour, tracking);
    }

    // ---------------------------------------------------------------------------------------------
    // The cursor.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Draws the pointer, as a reticle over the map and as an arrow over the interface.</summary>
    /// <remarks>
    /// Two shapes because they answer two questions. Over the map the pointer is a position — it is what
    /// the zoom anchors on and what the drag moves — so it is a crosshair with an open middle that does
    /// not hide the thing being pointed at. Over the interface it is a click, so it is an arrow.
    /// </remarks>
    private void DrawCursor(UiPainter painter, LayoutContext context, bool overInterface)
    {
        var at = new UiVec2(_mouseX, _mouseY);

        if (overInterface)
        {
            float size = context.Px(16f);
            painter.FillTriangle(
                new UiVec2(at.X + context.Px(1f), at.Y + context.Px(1f)),
                new UiVec2(at.X + context.Px(1f), at.Y + size + context.Px(2f)),
                new UiVec2(at.X + (size * 0.66f) + context.Px(2f), at.Y + (size * 0.72f) + context.Px(2f)),
                Bgra32.FromRgba(0, 0, 0, 190));
            painter.FillTriangle(
                at,
                new UiVec2(at.X, at.Y + size),
                new UiVec2(at.X + (size * 0.66f), at.Y + (size * 0.72f)),
                UiPalette.TextBright);
            return;
        }

        float arm = context.Px(13f);
        float gap = context.Px(4f);
        float weight = MathF.Max(1f, context.Px(1.4f));
        Bgra32 shadow = Bgra32.FromRgba(0, 0, 0, 150);

        foreach ((float dx, float dy) in new[] { (-1f, 0f), (1f, 0f), (0f, -1f), (0f, 1f) })
        {
            float x = at.X + (dx * gap) - (dx < 0f ? arm - gap : 0f) - (dx == 0f ? weight * 0.5f : 0f);
            float y = at.Y + (dy * gap) - (dy < 0f ? arm - gap : 0f) - (dy == 0f ? weight * 0.5f : 0f);
            float w = dx == 0f ? weight : arm - gap;
            float h = dy == 0f ? weight : arm - gap;
            painter.FillRect(new UiRect(x + 1f, y + 1f, w, h), shadow);
            painter.FillRect(new UiRect(x, y, w, h), Signal);
        }

        FillDisc(painter, at, context.Px(1.6f), UiPalette.TextBright, 8);
    }

    // ---------------------------------------------------------------------------------------------
    // Layout, projection and drawing primitives.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Gets the part of the screen the overhead view is rendered into: all of it.</summary>
    /// <param name="width">The back buffer's width.</param>
    /// <param name="height">The back buffer's height.</param>
    /// <returns>The full-screen viewport.</returns>
    public static Viewport MapViewport(int width, int height) =>
        new(0, 0, Math.Max(1, width), Math.Max(1, height));

    private static Chrome Measure(LayoutContext context, bool pageVisible)
    {
        float gutter = context.Px(28f);
        float width = context.Viewport.X;
        float height = context.Viewport.Y;
        float headerHeight = context.Px(72f);
        float footerHeight = context.Px(54f);
        float railWidth = context.Px(268f);
        float rowHeight = context.Px(44f);
        float railPad = context.Px(12f);

        var rail = new UiRect(
            gutter,
            headerHeight + gutter,
            railWidth,
            (rowHeight * PageNames.Length) + (railPad * 2f));

        var status = new UiRect(gutter, rail.Bottom + context.Px(14f), railWidth, context.Px(178f));

        float pageLeft = rail.Right + gutter;
        var page = new UiRect(
            pageLeft,
            headerHeight + gutter,
            MathF.Max(context.Px(200f), width - pageLeft - gutter),
            MathF.Max(context.Px(200f), height - footerHeight - gutter - headerHeight - gutter));

        return new Chrome(
            new UiRect(0f, 0f, width, headerHeight),
            new UiRect(0f, height - footerHeight, width, footerHeight),
            rail,
            status,
            page,
            pageVisible,
            rowHeight,
            railPad,
            gutter);
    }

    /// <summary>Gets the field of view a zoom position asks for, in radians.</summary>
    /// <remarks>
    /// Geometric rather than linear across the range, for the reason
    /// <see cref="MapComputerCamera"/> interpolates one geometrically: zoom is perceived in ratios, and a
    /// linear ramp over a range this wide races at one end and crawls at the other.
    /// </remarks>
    private static float FieldOfViewFor(float zoom) =>
        WidestFieldOfView * MathF.Pow(NarrowestFieldOfView / WidestFieldOfView, Math.Clamp(zoom, 0f, 1f));

    private static (double X, double Y) UnitsPerPixel(float fieldOfView, Viewport viewport)
    {
        double altitudeUnits = AltitudeMetres * UnitsPerMetre;
        double halfHeight = altitudeUnits * Math.Tan(fieldOfView * 0.5);
        double halfWidth = halfHeight * viewport.Width / Math.Max(1.0, viewport.Height);
        return (2.0 * halfWidth / Math.Max(1, viewport.Width), 2.0 * halfHeight / Math.Max(1, viewport.Height));
    }

    private static bool TryProject(
        Vector3d worldUnits,
        Vector3d centreUnits,
        float fieldOfView,
        Viewport viewport,
        out float x,
        out float y)
    {
        (double unitsPerPixelX, double unitsPerPixelY) = UnitsPerPixel(fieldOfView, viewport);
        x = viewport.X + (viewport.Width * 0.5f) + (float)((worldUnits.X - centreUnits.X) / unitsPerPixelX);
        y = viewport.Y + (viewport.Height * 0.5f) - (float)((worldUnits.Y - centreUnits.Y) / unitsPerPixelY);
        return x >= viewport.X && x <= viewport.X + viewport.Width
            && y >= viewport.Y && y <= viewport.Y + viewport.Height;
    }

    private static Vector3d Unproject(
        float x,
        float y,
        Vector3d centreUnits,
        float fieldOfView,
        Viewport viewport)
    {
        (double unitsPerPixelX, double unitsPerPixelY) = UnitsPerPixel(fieldOfView, viewport);
        return new Vector3d(
            centreUnits.X + ((x - viewport.X - (viewport.Width * 0.5)) * unitsPerPixelX),
            centreUnits.Y - ((y - viewport.Y - (viewport.Height * 0.5)) * unitsPerPixelY),
            centreUnits.Z);
    }

    /// <summary>Rounds a span up to the next 1, 2 or 5 times a power of ten.</summary>
    /// <param name="value">The span to round.</param>
    /// <returns>The nearest round number at or above it.</returns>
    private static double NiceStep(double value)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            return 1.0;
        }

        double magnitude = Math.Pow(10.0, Math.Floor(Math.Log10(value)));
        double normalised = value / magnitude;
        double step = normalised <= 1.0 ? 1.0 : normalised <= 2.0 ? 2.0 : normalised <= 5.0 ? 5.0 : 10.0;
        return step * magnitude;
    }

    /// <summary>Draws a string with extra space between its letters.</summary>
    /// <param name="painter">The surface to draw into.</param>
    /// <param name="font">The face to set it in.</param>
    /// <param name="text">The text.</param>
    /// <param name="at">The top-left of the line, in pixels.</param>
    /// <param name="size">The text size in pixels.</param>
    /// <param name="colour">The text colour.</param>
    /// <param name="tracking">The extra space after each letter, in pixels.</param>
    /// <returns>The width the line took, in pixels.</returns>
    /// <remarks>
    /// Letterspaced capitals are the whole voice of this interface, and no font carries them: tracking is
    /// a typesetting decision made per use, not a property of a face. Drawn a glyph at a time because the
    /// painter advances by the font's own metrics and there is nowhere else to put the extra space.
    /// </remarks>
    private static float DrawTracked(
        UiPainter painter,
        UiFont font,
        ReadOnlySpan<char> text,
        UiVec2 at,
        float size,
        Bgra32 colour,
        float tracking)
    {
        Span<char> one = stackalloc char[1];
        float x = at.X;
        foreach (char c in text)
        {
            one[0] = c;
            painter.DrawText(font, one, new UiVec2(x, at.Y), size, colour);
            x += font.MeasureWidth(one, size) + tracking;
        }

        return MathF.Max(0f, x - at.X - tracking);
    }

    private static float MeasureTracked(UiFont font, ReadOnlySpan<char> text, float size, float tracking) =>
        text.Length == 0 ? 0f : font.MeasureWidth(text, size) + (tracking * (text.Length - 1));

    private static void FillDisc(UiPainter painter, UiVec2 centre, float radius, Bgra32 colour, int segments = 16)
    {
        float step = MathF.Tau / segments;
        for (int i = 0; i < segments; i++)
        {
            float a = i * step;
            float b = (i + 1) * step;
            painter.FillTriangle(
                centre,
                new UiVec2(centre.X + (MathF.Cos(a) * radius), centre.Y + (MathF.Sin(a) * radius)),
                new UiVec2(centre.X + (MathF.Cos(b) * radius), centre.Y + (MathF.Sin(b) * radius)),
                colour);
        }
    }

    private static void StrokeRing(
        UiPainter painter,
        UiVec2 centre,
        float radius,
        float thickness,
        Bgra32 colour,
        int segments = 22)
    {
        float inner = MathF.Max(0f, radius - (thickness * 0.5f));
        float outer = radius + (thickness * 0.5f);
        float step = MathF.Tau / segments;

        for (int i = 0; i < segments; i++)
        {
            float a = i * step;
            float b = (i + 1) * step;
            var innerA = new UiVec2(centre.X + (MathF.Cos(a) * inner), centre.Y + (MathF.Sin(a) * inner));
            var innerB = new UiVec2(centre.X + (MathF.Cos(b) * inner), centre.Y + (MathF.Sin(b) * inner));
            var outerA = new UiVec2(centre.X + (MathF.Cos(a) * outer), centre.Y + (MathF.Sin(a) * outer));
            var outerB = new UiVec2(centre.X + (MathF.Cos(b) * outer), centre.Y + (MathF.Sin(b) * outer));
            painter.FillTriangle(innerA, outerA, outerB, colour);
            painter.FillTriangle(innerA, outerB, innerB, colour);
        }
    }

    private static void DrawDiamond(UiPainter painter, UiVec2 centre, float radius, Bgra32 colour)
    {
        var top = new UiVec2(centre.X, centre.Y - radius);
        var right = new UiVec2(centre.X + radius, centre.Y);
        var bottom = new UiVec2(centre.X, centre.Y + radius);
        var left = new UiVec2(centre.X - radius, centre.Y);
        painter.FillTriangle(top, right, bottom, colour);
        painter.FillTriangle(top, bottom, left, colour);
    }

    private static bool Contains(UiRect rect, float x, float y) =>
        x >= rect.X && x <= rect.Right && y >= rect.Y && y <= rect.Bottom;

    private string ComputerString(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        if (_resources.TryGetEntry(key, out ResourceEntry? entry))
        {
            ReadOnlySpan<byte> bytes = entry.Data.Span;
            int terminator = bytes.IndexOf((byte)0);
            return System.Text.Encoding.Latin1.GetString(terminator < 0 ? bytes : bytes[..terminator]);
        }

        return _resolveText(key);
    }

    // ---------------------------------------------------------------------------------------------
    // The palette: the shell's own tokens, so the computer reads as the same product as the menus. The
    // design's amber carries the chrome, its lighter hover tone the two things that are about the player
    // rather than about the level — where they are, and what is selected — over warm greys and dark glass.
    // ---------------------------------------------------------------------------------------------

    private static Bgra32 Signal => UiPalette.Accent;
    private static Bgra32 SignalDim => UiPalette.AccentAt(0.6f);
    private static Bgra32 Accent => UiPalette.AccentLight;
    private static Bgra32 Hostile => UiPalette.Danger;
    private static Bgra32 Complete => UiPalette.Success;
    private static Bgra32 Text => UiPalette.Text;
    private static Bgra32 TextMuted => UiPalette.TextMuted;
    private static Bgra32 Panel => UiPalette.GlassDense;
    private static Bgra32 PanelSolid => UiPalette.GlassMenu;
    private static Bgra32 Border => UiPalette.Hairline;

    private bool IsSelectable(Page page) => page != Page.Map || MapAvailable;

    private enum Page
    {
        Map,
        Objectives,
        Log,
    }

    /// <summary>Where every piece of the interface is this frame, in back-buffer pixels.</summary>
    /// <remarks>
    /// Measured once and passed to both the hit tests and the draws. Two copies of a layout is the oldest
    /// way there is to get a button that highlights somewhere other than where it can be clicked.
    /// </remarks>
    private readonly record struct Chrome(
        UiRect Header,
        UiRect Footer,
        UiRect Rail,
        UiRect Status,
        UiRect Page,
        bool PageVisible,
        float RowHeight,
        float RailPad,
        float Gutter)
    {
        /// <summary>Gets the row a page's rail entry occupies.</summary>
        /// <param name="index">The page's index.</param>
        /// <returns>The row rectangle.</returns>
        public UiRect PageRow(int index) => new(
            Rail.X + RailPad,
            Rail.Y + RailPad + (RowHeight * index),
            Rail.Width - (RailPad * 2f),
            RowHeight);

        /// <summary>Determines whether a point is over the interface rather than over the bare map.</summary>
        /// <param name="x">The horizontal position in pixels.</param>
        /// <param name="y">The vertical position in pixels.</param>
        /// <returns><see langword="true"/> when a click there belongs to a control.</returns>
        public bool OverInterface(float x, float y) =>
            Contains(Header, x, y)
            || Contains(Footer, x, y)
            || Contains(Rail, x, y)
            || Contains(Status, x, y)
            || (PageVisible && Contains(Page, x, y));
    }
}
