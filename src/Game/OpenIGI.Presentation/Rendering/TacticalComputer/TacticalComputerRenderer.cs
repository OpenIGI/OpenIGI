using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Presentation.Text;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Rhi;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// The remaster's map computer: the level seen from overhead as a one-colour satellite feed filling the screen, with the
/// interface projected over it through the heads-up display's visor, in the title's own colours and font.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not retail.</b> The original's computer is a model held up before the camera with a 4:3 aperture cut in it, which at a
/// modern resolution is a small picture in a low-polygon casing; it is kept whole for the vanilla path
/// (<see cref="IMapComputerPresenter"/>) and shares no code with this.
/// </para>
/// <para>
/// <b>One look with the heads-up display.</b> Its colours, its corners, brackets and rules, and its visor are the title's
/// <see cref="HudTheme"/>; its glass is the display's (<see cref="Hud.Remastered.Widgets.HudPanel"/>); it is drawn into the
/// display's layer and laid over the picture by the same pass (<see cref="IDisplayDevice.ComposeHudLayer"/>). Its text is the
/// title's own bitmap font (<see cref="ComputerText"/>).
/// </para>
/// <para>
/// <b>The map is a feed.</b> The overhead picture is turned into one colour's display by a pass of its own
/// (<see cref="IDisplayDevice.DrawMapFeed"/>), which also brings it up: the tear and the refresh band of a display being
/// driven. Every page is drawn over it, the text pages on one glass panel under which it dims.
/// </para>
/// <para>
/// <b>The map is left clear.</b> A strip of tabs along the top, a status line along the bottom, and nothing else over the
/// ground but its grid and its marks.
/// </para>
/// </remarks>
public sealed partial class TacticalComputerRenderer : ITacticalComputerPresenter
{
    private const string LogCategory = "Computer";

    /// <summary>How high above the ground the map is read from, in metres.</summary>
    /// <remarks>The retail altitude, kept: it is what the authored field-of-view range is calibrated to.</remarks>
    public const double AltitudeMetres = TacticalMapProjection.AltitudeMetres;

    private const double UnitsPerMetre = TacticalMapProjection.UnitsPerMetre;

    /// <summary>Where the map opens: wide enough to hold a mission, close enough to read it.</summary>
    private const float DefaultZoom = 0.34f;

    /// <summary>How quickly the zoom eases towards what the wheel asked for, per second.</summary>
    private const float ZoomResponse = 16f;

    /// <summary>How far the display bows at its corners, as a share of the screen: not at all, the map is read flat.</summary>
    /// <remarks>One figure for the feed, the display laid over it and the pointer read through it, so the three agree.</remarks>
    public const float TubeCurvature = 0f;

    /// <summary>The original's computer's sounds, by their definitions' names: a control newly under the pointer, a click, and the picture changing.</summary>
    private const string HoverSound = "mapcomp_05", ClickSound = "mapcomp_01", StaticSound = "mapcomp_03";

    /// <summary>The least time between two of the zoom's sounds, in seconds: a wheel spun is one sound, not a rattle.</summary>
    private const float ZoomSoundGap = 0.14f;

    /// <summary>How long a change of page disturbs the feed, in seconds.</summary>
    private const float BurstSeconds = 0.2f;

    private static readonly string[] PageNames = ["MAP", "OBJECTIVES", "LOG", "SAVE"];
    private static readonly KeyCode[] PageKeys = [KeyCode.D1, KeyCode.D2, KeyCode.D3, KeyCode.D4];

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IOpenIgiLogger _logger;
    private readonly Func<string, string> _resolveText;
    private readonly HudTheme _theme;
    private readonly IComputerFontSource? _fonts;
    private readonly TacticalMapOverlay _overlay;
    private readonly UiRect[] _tabs = new UiRect[PageNames.Length];
    private readonly float[] _tabWidths = new float[PageNames.Length];
    private readonly List<string> _lines = [];

    private ComputerText? _text;
    private RhiBackdrop? _glass;
    private BackdropBlur? _blur;

    private Page _page = Page.Map;

    /// <inheritdoc/>
    public bool MapAvailable { get; set; } = true;

    /// <inheritdoc/>
    public Action<string>? PlaySound { get; set; }

    private float _inputClock;
    private float _zoomSoundAt = float.NegativeInfinity;
    private Vector3d _centreUnits;
    private bool _hasCentre;
    private float _zoom = DefaultZoom;
    private float _zoomTarget = DefaultZoom;
    private float _clock;
    private float _mouseX;
    private float _mouseY;
    private bool _dragging;
    private int _hoverPage = -1;
    private int _requestedScale = UiScale.Automatic;
    private float _pageDim;
    private float _burst;

    // Whether the last frame was laid over the picture through the visor, which bends it: the pointer is then read where
    // the bend puts it.
    private bool _layered = true;

    // The screen's pixel density and whether fingers are working it, as the last frame drawn said.
    private float _density = 1f;
    private bool _touch;

    // Fingers on the map: how far apart two were last frame, and where and when the last tap landed.
    private float _pinch;
    private float _tapAt = -1f;
    private float _tapX;
    private float _tapY;
    private bool _bent;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="TacticalComputerRenderer"/> class.</summary>
    /// <param name="device">The device the display is drawn through.</param>
    /// <param name="resources">The registry the mission's localised strings were preloaded into.</param>
    /// <param name="logger">Where to report a missing font.</param>
    /// <param name="resolveText">The fallback used for a string the archives do not carry.</param>
    /// <param name="theme">The title's look, the heads-up display's own.</param>
    /// <param name="fonts">Where the title's font comes from; <see langword="null"/> to set every line in the shared face.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public TacticalComputerRenderer(
        IGraphicsDevice device,
        ResourceRegistry resources,
        IOpenIgiLogger logger,
        Func<string, string> resolveText,
        HudTheme theme,
        IComputerFontSource? fonts = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(resolveText);
        ArgumentNullException.ThrowIfNull(theme);

        _device = device;
        _resources = resources;
        _logger = logger;
        _resolveText = resolveText;
        _theme = theme;
        _fonts = fonts;
        _overlay = new TacticalMapOverlay(ComputerString);
    }

    /// <summary>Gets a value indicating whether the interface has its faces and can draw.</summary>
    public bool IsReady => _text is not null;

    /// <summary>Gets the vertical field of view the map is currently read at, in radians.</summary>
    public float FieldOfView => TacticalMapProjection.FieldOfViewFor(_zoom);

    /// <summary>Builds the interface's faces.</summary>
    /// <returns><see langword="false"/> when the shared faces are missing and the computer cannot be shown.</returns>
    public bool Load()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!UiFontLibrary.TypographyAvailable)
        {
            _logger.Warning(LogCategory, $"No interface font at {UiFontLibrary.FontsDirectory}; the remastered computer cannot draw.");
            return false;
        }

        try
        {
            // The title's own font sets the text; the interface's monospaced face stands in for what it cannot.
            IgiTypography typography = UiFontLibrary.Typography(_device);
            _text = new ComputerText(typography.Mono);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            _logger.Warning(LogCategory, $"Could not build the remastered computer's fonts: {ex.Message}");
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
        _pageDim = _page == Page.Map ? 0f : 1f;
        _burst = 0f;
        _dragging = false;
        _saveShown = false;

        // The picture coming up, as the original sounds a view that has changed (0x467970's flare).
        PlaySound?.Invoke(HoverSound);
        PlaySound?.Invoke(StaticSound);
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
        if (_text is null)
        {
            return;
        }

        float step = Math.Clamp(elapsedSeconds, 0f, 0.1f);
        _inputClock += step;
        int width = Math.Max(1, screenWidth), height = Math.Max(1, screenHeight);
        (_mouseX, _mouseY) = Warp(input.Mouse.X, input.Mouse.Y, width, height);

        if (!MapAvailable && _page == Page.Map)
        {
            _page = Page.Objectives;
        }

        TacticalComputerChrome chrome = Layout(width, height);

        // An upload under way keeps its page.
        Page shown = _page;
        bool held = SaveBusy;
        int hoverBefore = _hoverPage;
        _hoverPage = -1;
        for (int i = 0; i < PageNames.Length; i++)
        {
            if (IsSelectable((Page)i) && TacticalComputerChrome.Contains(_tabs[i], _mouseX, _mouseY))
            {
                _hoverPage = i;
            }
        }

        // A tab newly under the pointer clicks, as the original's menu items do (ComputerMenu_TextItemActivateCB).
        if (_hoverPage >= 0 && _hoverPage != hoverBefore)
        {
            PlaySound?.Invoke(HoverSound);
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

        if (held || !IsSelectable(_page))
        {
            _page = shown;
            if (!IsSelectable(_page))
            {
                _page = Page.Objectives;
            }
        }

        // A change of page disturbs the feed for a moment, and sounds as the original's chosen item does.
        if (_page != shown)
        {
            PlaySound?.Invoke(ClickSound);
            _burst = 1f;
            chrome = chrome with { PageVisible = _page != Page.Map };
        }

        UpdateSave(input, step, chrome);

        // Back to the player, which is the one navigation action a map that can be panned across a whole level needs.
        // Bound to both the obvious keys rather than one.
        if (input.Keyboard.WasPressed(KeyCode.Home) || input.Keyboard.WasPressed(KeyCode.Space))
        {
            _centreUnits = playerUnits;
            PlaySound?.Invoke(ClickSound);
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

        float zoomAsked = _zoomTarget;
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

        // A step of the zoom sounds as the original's does: its button's click, and the view changing (0x467970).
        if (_zoomTarget != zoomAsked && _inputClock - _zoomSoundAt >= ZoomSoundGap)
        {
            _zoomSoundAt = _inputClock;
            PlaySound?.Invoke(ClickSound);
            PlaySound?.Invoke(HoverSound);
            PlaySound?.Invoke(StaticSound);
        }

        Viewport map = MapViewport(screenWidth, screenHeight);
        float previousFieldOfView = TacticalMapProjection.FieldOfViewFor(_zoom);

        // Exponential smoothing framed as a half-life rather than a per-frame factor, so the zoom takes the same real
        // time to arrive whatever the frame rate is.
        _zoom += (_zoomTarget - _zoom) * (1f - MathF.Exp(-ZoomResponse * step));
        float fieldOfView = TacticalMapProjection.FieldOfViewFor(_zoom);

        // Cursor-anchored zoom: whatever is under the pointer stays under it. Done by unprojecting the same pixel through
        // the old and the new frustum and moving the centre by the difference, which is exact at every intermediate step
        // of the ease rather than only at its ends.
        if (MathF.Abs(fieldOfView - previousFieldOfView) > 1e-7f)
        {
            Vector3d before = TacticalMapProjection.Unproject(_mouseX, _mouseY, _centreUnits, previousFieldOfView, map);
            Vector3d after = TacticalMapProjection.Unproject(_mouseX, _mouseY, _centreUnits, fieldOfView, map);
            _centreUnits = new Vector3d(
                _centreUnits.X + (before.X - after.X),
                _centreUnits.Y + (before.Y - after.Y),
                _centreUnits.Z);
        }

        UpdateTouch(input, chrome, playerUnits, map, fieldOfView);

        if (_dragging && (input.Mouse.DeltaX != 0f || input.Mouse.DeltaY != 0f))
        {
            (double unitsPerPixelX, double unitsPerPixelY) = TacticalMapProjection.UnitsPerPixel(fieldOfView, map);
            _centreUnits = new Vector3d(
                _centreUnits.X - (input.Mouse.DeltaX * unitsPerPixelX),
                _centreUnits.Y + (input.Mouse.DeltaY * unitsPerPixelY),
                _centreUnits.Z);
        }
    }

    // A touch screen's map: one finger drags it, two pinch it about the point between them, a tap on a tab opens its
    // page, and two taps on the map bring it back over the player. The pointer the fingers are also given moves no
    // distance a frame -- it is put where the finger is -- so a drag read from it never panned.
    private void UpdateTouch(InputState input, TacticalComputerChrome chrome, Vector3d playerUnits, Viewport map, float fieldOfView)
    {
        IReadOnlyList<TouchPoint> points = input.Touch.Points;
        int down = 0;
        TouchPoint first = default, second = default;
        foreach (TouchPoint point in points)
        {
            if (point.Ended)
            {
                continue;
            }

            if (down == 0)
            {
                first = point;
            }
            else if (down == 1)
            {
                second = point;
            }

            down++;
        }

        (double unitsPerPixelX, double unitsPerPixelY) = TacticalMapProjection.UnitsPerPixel(fieldOfView, map);
        if (down >= 2 && _page == Page.Map)
        {
            float apart = MathF.Sqrt(((second.X - first.X) * (second.X - first.X)) + ((second.Y - first.Y) * (second.Y - first.Y)));
            if (_pinch > 1f && apart > 1f)
            {
                // Twice as far apart is one step in; the zoom is anchored where the pointer is, so put it between them.
                _zoomTarget = Math.Clamp(_zoomTarget + (MathF.Log2(apart / _pinch) * 0.3f), 0f, 1f);
                _zoom = _zoomTarget;
                (_mouseX, _mouseY) = ((first.X + second.X) * 0.5f, (first.Y + second.Y) * 0.5f);
            }

            _pinch = apart;

            // And the pair dragged together moves the map with them.
            float dx = (first.DeltaX + second.DeltaX) * 0.5f, dy = (first.DeltaY + second.DeltaY) * 0.5f;
            _centreUnits = new Vector3d(_centreUnits.X - (dx * unitsPerPixelX), _centreUnits.Y + (dy * unitsPerPixelY), _centreUnits.Z);
            _tapAt = -1f;
            return;
        }

        _pinch = 0f;
        if (down == 1 && _page == Page.Map && !chrome.OverInterface(first.StartX, first.StartY) && (first.DeltaX != 0f || first.DeltaY != 0f))
        {
            _centreUnits = new Vector3d(
                _centreUnits.X - (first.DeltaX * unitsPerPixelX),
                _centreUnits.Y + (first.DeltaY * unitsPerPixelY),
                _centreUnits.Z);
        }

        // A finger lifted about where it landed is a tap.
        float slop = chrome.Px(14f);
        foreach (TouchPoint point in points)
        {
            if (!point.Ended || MathF.Abs(point.X - point.StartX) > slop || MathF.Abs(point.Y - point.StartY) > slop)
            {
                continue;
            }

            for (int i = 0; i < PageNames.Length; i++)
            {
                // A tab is a line of text tall; a thumb is given the whole strip's height over it.
                UiRect tab = _tabs[i];
                var reach = new UiRect(tab.X - chrome.Px(4f), 0f, tab.Width + chrome.Px(8f), MathF.Max(chrome.Top.Bottom, chrome.Px(44f)));
                if (IsSelectable((Page)i) && !SaveBusy && TacticalComputerChrome.Contains(reach, point.X, point.Y) && _page != (Page)i)
                {
                    _page = (Page)i;
                    PlaySound?.Invoke(ClickSound);
                    _burst = 1f;
                    _tapAt = -1f;
                    return;
                }
            }

            if (_page != Page.Map || chrome.OverInterface(point.X, point.Y))
            {
                continue;
            }

            bool again = _tapAt >= 0f && _inputClock - _tapAt < 0.35f
                && MathF.Abs(point.X - _tapX) < slop * 3f && MathF.Abs(point.Y - _tapY) < slop * 3f;
            if (again)
            {
                _centreUnits = playerUnits;
                PlaySound?.Invoke(ClickSound);
                _tapAt = -1f;
            }
            else
            {
                (_tapAt, _tapX, _tapY) = (_inputClock, point.X, point.Y);
            }
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

        return new MapSceneView(Vantage().ToCamera(), TacticalMapProjection.FieldOfViewFor(_zoom), MapViewport(screenWidth, screenHeight));
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

    /// <inheritdoc/>
    /// <remarks>The batch is handed in closed, and is left closed: the feed and the visor are passes of their own between its draws.</remarks>
    public void Draw(
        SpriteBatch batch,
        IComputerObjectives runtime,
        Vector3d player,
        IReadOnlyList<Vector3d> guards,
        IEnumerable<ComputerMapHighlight> highlights,
        in ComputerFrame frame)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(guards);
        ArgumentNullException.ThrowIfNull(highlights);

        if (_text is not { } text || frame.Width <= 0 || frame.Height <= 0)
        {
            return;
        }

        // The display's own clock: the feed's, which runs on while the simulation is held behind it.
        float elapsed = Math.Clamp(frame.Feed.Seconds - _clock, 0f, 0.1f);
        _clock = frame.Feed.Seconds;
        _requestedScale = frame.RequestedScale;
        _density = frame.PixelDensity > 0f ? frame.PixelDensity : 1f;
        _touch = frame.Touch;
        _burst = MathF.Max(0f, _burst - (elapsed / BurstSeconds));
        _pageDim += ((_page == Page.Map ? 0f : 1f) - _pageDim) * (1f - MathF.Exp(-12f * elapsed));

        // The feed first, over the picture; then the frame is copied again, so the glass stands on the feed.
        float curvature = TubeCurvature;
        bool fed = _device.DrawMapFeed(Feed(frame.Feed, _layered ? curvature : 0f));
        _glass = Glass(frame.Width, frame.Height);

        float opacity = Math.Clamp(frame.Opacity, 0f, 1f);
        if (opacity <= 0.004f)
        {
            _bent = false;
            return;
        }

        // A phone draws the display straight onto the picture. The visor is a layer of its own, bent and composited
        // over the frame: two more passes over every pixel of a screen with three times a monitor's rows to the inch,
        // on a graphics chip a fraction of a desktop's, and the map dragged along behind the finger.
        _layered = !frame.Touch && _device.BeginHudLayer();
        _bent = _layered && fed;

        batch.Begin();
        var layout = new LayoutContext(frame.Width, frame.Height);
        UiPainter painter = UiPainter.Create(batch, layout, _glass);
        TacticalComputerChrome chrome = Layout(frame.Width, frame.Height);

        // Through the visor the display flickers in band by band; without it, it fades.
        float fade = _layered ? 1f : opacity;
        painter.PushOpacity(fade);
        text.Begin(batch, painter, fade);
        var canvas = new ComputerCanvas(painter, text, _theme, chrome, _clock);

        if (!fed)
        {
            // No feed to stand on: the title's glass colour over the picture, so the display is still its own.
            painter.FillRect(new UiRect(0f, 0f, frame.Width, frame.Height), _theme.Palette.Glass.WithAlpha((byte)(150f + (60f * _pageDim))));
        }

        Viewport map = MapViewport(frame.Width, frame.Height);
        float fieldOfView = TacticalMapProjection.FieldOfViewFor(_zoom);
        if (MapAvailable)
        {
            painter.PushOpacity(1f - (0.75f * _pageDim));
            _overlay.Draw(canvas, map, fieldOfView, runtime, player, frame.HeadingRadians, guards, highlights, _centreUnits);
            painter.PopOpacity();
        }

        if (_page != Page.Map)
        {
            DrawPage(canvas, runtime);
        }

        DrawTopStrip(canvas, runtime, guards.Count);
        DrawBottomStrip(canvas, player, map, fieldOfView, frame.CloseKey);
        DrawCursor(canvas, player, map, fieldOfView);
        painter.PopOpacity();
        batch.End();

        if (_layered)
        {
            _device.ComposeHudLayer(_theme.Fx with
            {
                Intensity = 1f,
                Curvature = _bent ? curvature : 0f,
                Appear = opacity,
                Seconds = _clock,
            });
        }
    }

    /// <summary>Draws the mission-failed panel in the display's own language.</summary>
    /// <param name="batch">The batch to draw into; it must already be between Begin and End.</param>
    /// <param name="screenWidth">The back buffer's width.</param>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is <see langword="null"/>.</exception>
    public void DrawMissionFailed(SpriteBatch batch, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_text is not { } text || screenWidth <= 0 || screenHeight <= 0)
        {
            return;
        }

        UiPainter painter = UiPainter.Create(batch, new LayoutContext(screenWidth, screenHeight));
        TacticalComputerChrome chrome = Layout(screenWidth, screenHeight);
        text.Begin(batch, painter, 1f);
        DrawMissionFailed(new ComputerCanvas(painter, text, _theme, chrome, _clock), screenWidth, screenHeight);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // The shared faces are the device's, not this renderer's to free; the title's font is its source's.
        _text = null;
        _glass = null;
        _blur = null;
        _fonts?.Dispose();
    }

    /// <summary>Gets the part of the screen the overhead view is rendered into: all of it.</summary>
    /// <param name="width">The back buffer's width.</param>
    /// <param name="height">The back buffer's height.</param>
    /// <returns>The full-screen viewport.</returns>
    public static Viewport MapViewport(int width, int height) =>
        new(0, 0, Math.Max(1, width), Math.Max(1, height));

    /// <summary>The feed's look in a title's colours: its glass for the shadows, its quiet colour for the body, its own for the lights.</summary>
    /// <param name="theme">The title's look.</param>
    /// <param name="signal">How far the feed has come up.</param>
    /// <param name="curvature">How far the visor over it bends it.</param>
    /// <param name="dim">How far a text page dims it.</param>
    /// <param name="burst">The disturbance of a change of page.</param>
    /// <returns>The feed.</returns>
    public static MapFeedSettings FeedFor(HudTheme theme, in MapFeedSettings signal, float curvature, float dim, float burst)
    {
        ArgumentNullException.ThrowIfNull(theme);
        HudPalette palette = theme.Palette;
        Bgra32 light = Mix(palette.Primary, palette.Highlight, 0.3f);
        return signal with
        {
            Dim = dim,
            Burst = burst,
            Curvature = curvature,
            ScanlineDensity = theme.Fx.ScanlineDensity,
            ScanlineStrength = theme.Fx.ScanlineStrength * 0.85f,
            Grain = 0.05f,
            Shadow = (palette.Glass.R / 255f * 0.55f, palette.Glass.G / 255f * 0.55f, palette.Glass.B / 255f * 0.55f),
            Body = (palette.Dim.R / 255f, palette.Dim.G / 255f, palette.Dim.B / 255f),
            Highlight = (light.R / 255f, light.G / 255f, light.B / 255f),
        };
    }

    private MapFeedSettings Feed(in MapFeedSettings signal, float curvature) => FeedFor(_theme, signal, curvature, _pageDim, _burst);

    private static Bgra32 Mix(Bgra32 a, Bgra32 b, float t) => Bgra32.FromRgba(
        (byte)(a.R + ((b.R - a.R) * t)),
        (byte)(a.G + ((b.G - a.G) * t)),
        (byte)(a.B + ((b.B - a.B) * t)),
        255);

    // This frame's layout, from the text as it will be set: one measure for the hit tests and the draws.
    private TacticalComputerChrome Layout(int width, int height)
    {
        ComputerText text = _text!;
        text.Use(_fonts?.Font, ComputerText.ScaleFor(_requestedScale, height, _density));
        for (int i = 0; i < PageNames.Length; i++)
        {
            _tabWidths[i] = text.Measure(PageNames[i]);
        }

        return TacticalComputerChrome.Measure(width, height, text.LineHeight, _page != Page.Map, _tabWidths, _tabs, _density);
    }

    // Where the pointer is on the display as drawn: the visor draws a point of the display nearer the middle the further
    // out it is, so what is under the pointer is a little further out than the pointer.
    private (float X, float Y) Warp(float x, float y, int width, int height)
    {
        if (!_bent)
        {
            return (x, y);
        }

        float cx = ((x / width) * 2f) - 1f, cy = ((y / height) * 2f) - 1f;
        float k = 1f + (TubeCurvature * ((cx * cx) + (cy * cy)));
        return ((((cx * k) * 0.5f) + 0.5f) * width, (((cy * k) * 0.5f) + 0.5f) * height);
    }

    // The frame blurred for the glass, restated for the painter; kept while the device hands back the same blur.
    private RhiBackdrop? Glass(int width, int height)
    {
        BackdropBlur blur = _device.BlurFrameForBackdrop();
        if (!ReferenceEquals(blur, _blur) || _glass is null)
        {
            _blur = blur;
            _glass = RhiBackdrop.From(blur, width, height);
        }

        return _glass;
    }

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

    // The map is the level's to give; the save page is there for a mission that keeps save games.
    private bool IsSelectable(Page page) => page switch
    {
        Page.Map => MapAvailable,
        Page.Save => Saves is not null,
        _ => true,
    };

    private enum Page
    {
        Map,
        Objectives,
        Log,
        Save,
    }
}
