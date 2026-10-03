using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Input;
using OpenIGI.Presentation.Text;
using OpenIGI.Rendering;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using OpenIGI.UI;

namespace OpenIGI.Host.DevTools;

/// <summary>
/// The developer console's on-screen half: a set of floating panels, the input that drives them, and
/// the drawing.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the fixed half-screen drop-down the console used to be. The panels are ordinary
/// <see cref="UiWindow"/>s in a <see cref="WindowLayer"/>, so dragging, resizing, snapping and z-order
/// are the framework's problem rather than this class's, and the variable browser, the watch list and
/// the statistics panel are the same widget with different contents.
/// </para>
/// <para>
/// <b>Keyboard focus follows the window in front.</b> There is no separate focus concept: the layer
/// already raises whatever was pressed, so the frontmost panel is the one the reader last touched, and
/// that is the one that should receive typing. It is the rule every desktop has, and it costs nothing.
/// </para>
/// <para>
/// <b>It builds its own layout context.</b> The shell authors against a 1920×1080 reference so a menu
/// keeps its proportions on any display; a developer tool wants the opposite — constant physical size,
/// so the same monitor shows the same amount of text whatever the aspect ratio. Passing the real
/// viewport as the reference makes <see cref="LayoutContext.Scale"/> equal <c>con_scale</c> alone,
/// which turns every <c>Px()</c> in the widgets below into a real pixel without any of them knowing.
/// </para>
/// <para>
/// <b>Input is read before it is captured.</b> The mouse's buttons, motion and wheel all read as idle
/// once <see cref="InputState.Captured"/> is set, while its position deliberately keeps working. So
/// everything is sampled first, and the capture is the last thing that happens.
/// </para>
/// </remarks>
public sealed class DeveloperConsole : IDisposable
{
    private const float BlinkPeriod = 0.53f;

    private const string ConsolePanel = "console";
    private const string CVarPanel = "cvars";
    private const string WatchPanel = "watch";
    private const string PalettePanel = "palette";
    private const string InspectorPanel = "world";

    /// <summary>Every panel's key, known before any of them has been built.</summary>
    private static readonly string[] KnownPanels = [ConsolePanel, CVarPanel, WatchPanel, InspectorPanel, PalettePanel];

    private readonly IGraphicsDevice _device;
    private readonly ConsoleModel _model;
    private readonly IOpenIgiLogger _logger;
    private readonly Dictionary<string, UiWindow> _panels = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingOpen = new(StringComparer.OrdinalIgnoreCase);
    private readonly ContextMenu _menu = new();

    private readonly KeyRepeater _backspace = new();
    private readonly KeyRepeater _delete = new();
    private readonly KeyRepeater _left = new();
    private readonly KeyRepeater _right = new();
    private readonly KeyRepeater _up = new();
    private readonly KeyRepeater _down = new();
    private readonly KeyRepeater _pageUp = new();
    private readonly KeyRepeater _pageDown = new();

    private UiSurfaces? _surfaces;
    private MonoFontSet? _fonts;
    private DevConsoleTheme? _theme;
    private WindowLayer? _layer;

    private UiWindow? _consoleWindow;
    private UiWindow? _cvarWindow;
    private UiWindow? _watchWindow;
    private UiWindow? _paletteWindow;
    private UiWindow? _inspectorWindow;

    private ConsoleBody? _body;
    private CVarBrowserBody? _cvars;
    private WatchBody? _watch;
    private CommandPaletteBody? _palette;
    private InspectorBody? _inspector;
    private IWorldInspector? _world;

    /// <summary>An <c>ent_select</c> that arrived before the panels existed, applied once they do.</summary>
    private int? _pendingSelection;

    private float _blink;
    private bool _caretVisible = true;
    private bool _disposed;
    private bool _layoutRestored;
    private bool _findFocused;
    private bool _watchAutoHeight = true;
    private UiVec2 _viewportPointer;
    private int _viewportWidth = 1;
    private int _viewportHeight = 1;

    /// <summary>Initialises a new instance of the <see cref="DeveloperConsole"/> class.</summary>
    /// <param name="device">The device fonts and generated sprites are uploaded to.</param>
    /// <param name="model">The console state shown and edited.</param>
    /// <param name="logger">Where to report what the console does.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public DeveloperConsole(IGraphicsDevice device, ConsoleModel model, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(model);

        _device = device;
        _model = model;
        _logger = logger ?? NullLogger.Instance;
        Watch = new ConsoleWatchList();
    }

    /// <summary>Gets the pinned values shown by the watch panel.</summary>
    public ConsoleWatchList Watch { get; }

    /// <summary>Attaches the world the inspector lists, or detaches when a mission ends.</summary>
    /// <param name="world">The world, or <see langword="null"/> for none.</param>
    /// <remarks>
    /// Kept even when there are no panels yet, because the world is loaded before the display is and
    /// the inspector would otherwise come up empty on the first mission of a session.
    /// </remarks>
    public void AttachWorld(IWorldInspector? world)
    {
        _world = world;
        _inspector?.Attach(world);
    }

    /// <summary>Gets a value indicating whether the fonts and sprites are loaded.</summary>
    public bool IsReady => _theme is not null;

    /// <summary>Gets or sets the clipboard reader, so a paste can reach the system clipboard.</summary>
    public Func<string>? ReadClipboard { get; set; }

    /// <summary>Gets or sets the clipboard writer.</summary>
    public Action<string>? WriteClipboard { get; set; }

    /// <summary>Gets a value indicating whether the console wants the pointer freed and shown.</summary>
    /// <remarks>
    /// The cursor lock is re-asserted from the game loop on every pass, so this is polled there rather
    /// than pushed once when the console opens — which the next frame would silently undo.
    /// </remarks>
    public bool WantsPointer => _model.IsOpen;

    /// <summary>Gets a value indicating whether a panel is pinned to stay up with the console shut.</summary>
    public static bool HasVisibleOverlays => DevConsoleCVars.WatchVisible.Value;

    /// <summary>Gets the handle of the entity selected in the World panel, or null when none is.</summary>
    /// <remarks>Read from the 3D pass, which draws the selection silhouette around it.</remarks>
    public int? SelectedEntityId =>
        _inspectorWindow is { IsOpen: true } && _inspector?.Selected is { } entity ? entity.Id : null;

    /// <summary>Gets how many scrollback lines are on screen, for page-up and page-down.</summary>
    public int VisibleLines => _body?.VisibleLines ?? 20;

    /// <summary>Builds the fonts, the generated sprites and every panel.</summary>
    /// <param name="console">The console the panels read from and act on.</param>
    /// <returns><see langword="true"/> when everything loaded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="console"/> is <see langword="null"/>.</exception>
    /// <remarks>Failure is reported and swallowed: a missing font must cost the console, never the game.</remarks>
    public bool Load(ConsoleSystem console)
    {
        ArgumentNullException.ThrowIfNull(console);

        if (_theme is not null)
        {
            return true;
        }

        try
        {
            _fonts = new MonoFontSet(_device, UiFontLibrary.Mono);
            _surfaces = UiSurfaces.Create(_device);
            _theme = new DevConsoleTheme(_fonts, DevConsoleCVars.Palette.Value);
            _layer = new WindowLayer { SnapDistance = DevConsoleCVars.Snap.Value };

            BuildPanels(console);

            // Pins are restored here rather than at registration because settings.cfg has only just
            // been applied; asking any earlier would read the variable's default. They are *added*
            // rather than parsed over the top, so a `watch` that ran from the configuration or the
            // command line before this point is kept instead of being wiped by the saved set.
            foreach (string pinned in DevConsoleCVars.Watch.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Watch.Add(pinned);
            }

            // Panels asked for before there were any: apply them now that they exist.
            foreach (string name in _pendingOpen)
            {
                if (_panels.TryGetValue(name, out UiWindow? deferred))
                {
                    deferred.IsOpen = true;
                }
            }

            _pendingOpen.Clear();
            return true;
        }
        catch (Exception ex) when (ex is IOException or FileNotFoundException or InvalidOperationException)
        {
            _logger.Warning("Console", $"The developer console could not load its font: {ex.Message}");
            return false;
        }
    }

    /// <summary>Applies a frame's input.</summary>
    /// <param name="input">The frame's input state.</param>
    /// <param name="elapsedSeconds">Real seconds since the last call.</param>
    /// <returns><see langword="true"/> when the console used the input.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    public bool Update(InputState input, float elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(input);

        AdvanceCaret(elapsedSeconds);
        _layer?.Update(elapsedSeconds);

        if (!_model.IsOpen)
        {
            return false;
        }

        // Position, buttons and wheel are all read here, before the capture turns every one of them
        // except the position to zero for the rest of the frame.
        HandlePointer(input);
        HandleKeys(input.Keyboard, elapsedSeconds);
        return true;
    }

    /// <summary>Draws whichever panels are showing.</summary>
    /// <param name="batch">An open sprite batch.</param>
    /// <param name="screenWidth">The back buffer's width.</param>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is <see langword="null"/>.</exception>
    public void Draw(SpriteBatch batch, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (_theme is null || _layer is null)
        {
            return;
        }

        LayoutContext context = BuildContext(screenWidth, screenHeight);

        // The type scale and the spacing rhythm both follow the reader's settings, and every panel
        // reads them from here -- so a change to con_font_size or con_scale moves the whole interface
        // together instead of leaving one panel at the old size.
        _theme.Resize(context.Px(DevConsoleCVars.FontSize.Value), context.Scale);
        ApplyChrome();

        RestoreLayoutOnce(context);
        SyncViewport(context);
        SyncPanels();

        var painter = UiPainter.Create(batch, context);
        painter.PushOpacity(DevConsoleCVars.Opacity.Value);

        _layer.Measure(context, context.Viewport);
        _layer.Arrange(new UiRect(0f, 0f, context.Viewport.X, context.Viewport.Y));
        _layer.Draw(painter);

        // Over every window, and at full opacity: a menu the reader just asked for should not be
        // showing the scene through it.
        DrawSelectionMarker(painter, context.Viewport);
        _menu.Draw(painter, _theme, context.Viewport);
        DrawPointer(painter);
    }

    /// <summary>
    /// Marks the entity selected in the world panel, wherever it is.
    /// </summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <param name="viewport">The back buffer's size in pixels.</param>
    /// <remarks>
    /// <para>
    /// Drawn in the interface pass, over the scene, which is what makes it visible through a wall with
    /// no depth-test trickery — and the reason this is a screen-space bracket rather than a
    /// world-space box.
    /// </para>
    /// <para>
    /// The bracket shrinks with distance but never below a size that can be seen, so something a
    /// kilometre away is still a mark rather than a pixel. When it leaves the viewport the mark pins
    /// to the nearest edge and becomes an arrow pointing at it, so a selection is never simply lost
    /// the moment the reader turns around.
    /// </para>
    /// </remarks>
    private void DrawSelectionMarker(UiPainter painter, UiVec2 viewport)
    {
        if (_theme is null ||
            _inspector is null ||
            _inspectorWindow is not { IsOpen: true } ||
            _inspector.Selected is not { } entity ||
            _inspector.World is not { } world ||
            !world.TryProject(entity.Id, out ScreenMarker marker))
        {
            return;
        }

        float margin = _theme.Gutter * 2f;
        bool onScreen = marker.IsInFront &&
            marker.X >= margin && marker.X <= viewport.X - margin &&
            marker.Y >= margin && marker.Y <= viewport.Y - margin;

        float x = Math.Clamp(marker.X, margin, viewport.X - margin);
        float y = Math.Clamp(marker.Y, margin, viewport.Y - margin);

        if (onScreen)
        {
            DrawBracket(painter, x, y, marker.DistanceMetres, entity);
        }
        else
        {
            DrawEdgeArrow(painter, x, y, viewport, marker, entity);
        }
    }

    /// <summary>Draws the corner bracket that sits on a visible selection.</summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <param name="x">Where it sits horizontally.</param>
    /// <param name="y">Where it sits vertically.</param>
    /// <param name="distance">How far away the entity is.</param>
    /// <param name="entity">What is selected.</param>
    /// <remarks>
    /// Corners rather than a closed rectangle: four short strokes read as a reticle placed over
    /// something, where a full box reads as a thing in its own right and hides what it frames. Each
    /// stroke is drawn twice, dark then bright, so it holds against a pale sky as well as a dark
    /// interior — the outline is what makes it legible over any scene at all.
    /// </remarks>
    private void DrawBracket(UiPainter painter, float x, float y, double distance, in InspectableEntity entity)
    {
        DevConsoleTheme theme = _theme!;
        float half = MathF.Round(Math.Clamp(900f / MathF.Max(4f, (float)distance), 14f, 90f));
        float arm = MathF.Round(half * 0.45f);
        float weight = MathF.Max(2f, MathF.Round(theme.Unit * 0.5f));

        x = MathF.Round(x);
        y = MathF.Round(y);

        for (int pass = 0; pass < 2; pass++)
        {
            bool shadow = pass == 0;
            float grow = shadow ? 1f : 0f;
            Bgra32 colour = shadow ? Bgra32.FromRgba(0, 0, 0, 190) : theme.Ui.Accent;
            float thickness = weight + (shadow ? 2f : 0f);

            float left = x - half - grow;
            float right = x + half + grow;
            float top = y - half - grow;
            float bottom = y + half + grow;

            painter.FillRect(new UiRect(left, top, arm, thickness), colour);
            painter.FillRect(new UiRect(left, top, thickness, arm), colour);

            painter.FillRect(new UiRect(right - arm, top, arm, thickness), colour);
            painter.FillRect(new UiRect(right - thickness, top, thickness, arm), colour);

            painter.FillRect(new UiRect(left, bottom - thickness, arm, thickness), colour);
            painter.FillRect(new UiRect(left, bottom - arm, thickness, arm), colour);

            painter.FillRect(new UiRect(right - arm, bottom - thickness, arm, thickness), colour);
            painter.FillRect(new UiRect(right - thickness, bottom - arm, thickness, arm), colour);
        }

        DrawMarkerLabel(painter, x, y - half - (theme.Unit * 2f), entity.Name, distance);
    }

    /// <summary>Draws the pinned arrow standing in for a selection off the edge of the screen.</summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <param name="x">Where the mark was clamped to horizontally.</param>
    /// <param name="y">Where the mark was clamped to vertically.</param>
    /// <param name="viewport">The back buffer's size in pixels.</param>
    /// <param name="marker">Where the entity really projected to.</param>
    /// <param name="entity">What is selected.</param>
    private void DrawEdgeArrow(
        UiPainter painter,
        float x,
        float y,
        UiVec2 viewport,
        in ScreenMarker marker,
        in InspectableEntity entity)
    {
        DevConsoleTheme theme = _theme!;
        float size = MathF.Max(10f, theme.Unit * 3f);

        // Point away from the middle, along the direction the entity actually lies in.
        float dx = marker.X - (viewport.X * 0.5f);
        float dy = marker.Y - (viewport.Y * 0.5f);
        float length = MathF.Max(0.0001f, MathF.Sqrt((dx * dx) + (dy * dy)));
        dx /= length;
        dy /= length;

        var tip = new UiVec2(MathF.Round(x + (dx * size)), MathF.Round(y + (dy * size)));
        var left = new UiVec2(MathF.Round(x - (dy * size * 0.6f)), MathF.Round(y + (dx * size * 0.6f)));
        var right = new UiVec2(MathF.Round(x + (dy * size * 0.6f)), MathF.Round(y - (dx * size * 0.6f)));

        painter.FillTriangle(tip, left, right, theme.Ui.Accent);
        DrawMarkerLabel(painter, x, y - (size * 2.2f), entity.Name, marker.DistanceMetres);
    }

    private void DrawMarkerLabel(UiPainter painter, float centreX, float top, string name, double distance)
    {
        DevConsoleTheme theme = _theme!;
        string text = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{name}  {distance:0.0} m");

        float width = theme.SmallFont.MeasureWidth(text, theme.SmallSize);
        float height = theme.SmallFont.LineHeight(theme.SmallSize);
        var box = new UiRect(
            MathF.Round(centreX - (width * 0.5f) - theme.Unit),
            MathF.Round(top - height - theme.Unit),
            MathF.Round(width + (theme.Unit * 2f)),
            MathF.Round(height + (theme.Unit * 1.5f)));

        painter.FillRect(box, Bgra32.FromRgba(0, 0, 0, 175));
        painter.DrawText(
            theme.SmallFont,
            text,
            new UiVec2(MathF.Round(box.X + theme.Unit), MathF.Round(box.Y + (theme.Unit * 0.75f))),
            theme.SmallSize,
            theme.Ui.Accent);
    }

    /// <summary>
    /// Draws the pointer, last of all.
    /// </summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <remarks>
    /// The console draws its own rather than letting the window system show the operating system's.
    /// A composited cursor sits on top of everything including the menu the reader is aiming at, it
    /// does not match the one the shell already draws, and turning it on while the shell was drawing
    /// its own put two arrows on screen at once.
    /// </remarks>
    private void DrawPointer(UiPainter painter)
    {
        if (_theme is null)
        {
            return;
        }

        var tip = new UiVec2(_viewportPointer.X, _viewportPointer.Y);
        float size = Math.Clamp(painter.Context.Viewport.Y / 42f, 18f, 40f);

        DrawArrow(painter, tip, size + 3f, Bgra32.FromRgba(0, 0, 0, 200));
        DrawArrow(painter, tip, size, Bgra32.White);
    }

    private static void DrawArrow(UiPainter painter, UiVec2 tip, float size, Bgra32 color)
    {
        var down = new UiVec2(tip.X, tip.Y + size);
        var elbow = new UiVec2(tip.X + (size * 0.36f), tip.Y + (size * 0.72f));
        var barb = new UiVec2(tip.X + (size * 0.72f), tip.Y + (size * 0.72f));
        painter.FillTriangle(tip, down, elbow, color);
        painter.FillTriangle(tip, elbow, barb, color);
    }

    /// <summary>Stores where the panels sit and what is pinned, so both survive a restart.</summary>
    public void SaveLayout()
    {
        if (_panels.Count > 0)
        {
            DevConsoleCVars.Layout.Value = DevConsoleLayout.Render(_panels);
        }

        DevConsoleCVars.Watch.Value = Watch.Render();
    }

    /// <summary>Releases the generated sprites and the font.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _surfaces?.Dispose();
        _fonts?.Dispose();
    }

    private void BuildPanels(ConsoleSystem console)
    {
        DevConsoleTheme theme = _theme!;
        WindowLayer layer = _layer!;

        _body = new ConsoleBody(_model, theme) { Surfaces = _surfaces };
        _consoleWindow = Panel(ConsolePanel, "Console", new UiRect(40f, 40f, 1180f, 600f), _body, new UiVec2(420f, 200f));

        _cvars = new CVarBrowserBody(console.Variables, theme) { Surfaces = _surfaces };
        _cvarWindow = Panel(CVarPanel, "Variables", new UiRect(1240f, 40f, 620f, 720f), _cvars, new UiVec2(460f, 240f));
        _cvarWindow.IsOpen = false;

        _watch = new WatchBody(Watch, console.Variables, theme);
        _watchWindow = Panel(WatchPanel, "Watch", new UiRect(40f, 660f, 420f, 150f), _watch, new UiVec2(240f, 90f));
        _watchWindow.IsOpen = false;

        _inspector = new InspectorBody(theme) { Surfaces = _surfaces };
        _inspectorWindow = Panel(InspectorPanel, "World", new UiRect(1040f, 660f, 640f, 340f), _inspector, new UiVec2(380f, 180f));
        _inspectorWindow.IsOpen = false;
        _inspector.Attach(_world);

        if (_pendingSelection is { } pending)
        {
            _pendingSelection = null;
            _inspector.SelectWhenListed(pending);
        }

        _palette = new CommandPaletteBody(console, theme) { Surfaces = _surfaces };
        _paletteWindow = Panel(PalettePanel, "Command palette", new UiRect(520f, 180f, 900f, 460f), _palette, new UiVec2(420f, 200f));
        _paletteWindow.IsOpen = false;
        _paletteWindow.IsResizable = false;

        _ = layer;
    }

    private UiWindow Panel(string key, string title, UiRect frame, UiElement content, UiVec2 minSize)
    {
        var window = new UiWindow(_theme!.Ui, title)
        {
            Frame = frame,
            MinSize = minSize,
            Surfaces = _surfaces,
            Content = content,
        };

        window.CloseRequested += (_, _) => ClosePanel(key);
        _layer!.Add(window);
        _panels[key] = window;
        return window;
    }

    private void ClosePanel(string key)
    {
        if (string.Equals(key, ConsolePanel, StringComparison.OrdinalIgnoreCase))
        {
            _model.IsOpen = false;
            return;
        }

        if (_panels.TryGetValue(key, out UiWindow? window))
        {
            window.IsOpen = false;
        }

        // Closing a pinned panel by its own button must also unpin it, or it would reappear the moment
        // the console was shut -- which reads as the button not having worked.
        if (string.Equals(key, WatchPanel, StringComparison.OrdinalIgnoreCase))
        {
            DevConsoleCVars.WatchVisible.Value = false;
        }
    }

    /// <summary>Registers the commands that drive the panels.</summary>
    /// <param name="console">The console to register with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="console"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Called before <c>settings.cfg</c> is read, and therefore before <see cref="Load"/> has built
    /// anything — so every one of these must survive being run with no panels in existence. A saved
    /// configuration naming a panel has to work, and the alternative is a start-up error for a line
    /// the console itself wrote.
    /// </remarks>
    public void RegisterCommands(ConsoleSystem console)
    {
        ArgumentNullException.ThrowIfNull(console);

        console.Commands.Add(
            "con_panel",
            "Shows or hides a developer panel.",
            context =>
            {
                string name = context[0];

                if (!_panels.TryGetValue(name, out UiWindow? window))
                {
                    // Before the display exists the panels do not either. A named one is remembered
                    // and applied once they are built; anything else lists what there is.
                    if (name.Length > 0 && _theme is null && IsPanelName(name))
                    {
                        _pendingOpen.Add(name);
                        return;
                    }

                    context.Print($"Panels: {string.Join(", ", KnownPanels)}");
                    return;
                }

                string mode = context[1];
                bool wanted = mode switch
                {
                    "on" or "1" => true,
                    "off" or "0" => false,
                    _ => !window.IsOpen,
                };

                // The palette is stateful in a way the other panels are not — it has a query and a
                // result list — so showing it has to go through the same door the chord uses, or it
                // comes up reading "no matches" because nothing ever ran the search.
                if (wanted && string.Equals(name, PalettePanel, StringComparison.OrdinalIgnoreCase))
                {
                    OpenPalette();
                    context.Print($"{name} is shown.");
                    return;
                }

                window.IsOpen = wanted;

                // The two pinnable panels take their visibility from a variable whenever the console
                // is shut, so showing one through this command has to set that variable too. Without
                // it, `con_panel watch on` would work until the console was closed and then lose the
                // panel, which reads as the command not having stuck.
                if (string.Equals(name, WatchPanel, StringComparison.OrdinalIgnoreCase))
                {
                    DevConsoleCVars.WatchVisible.Value = wanted;
                }

                if (wanted)
                {
                    _layer?.Raise(window);
                    _model.IsOpen = true;
                }

                context.Print($"{name} is {(wanted ? "shown" : "hidden")}.");
            },
            "<console|cvars|watch|world|palette> [on|off]");

        console.Commands.Add(
            "con_reset_layout",
            "Puts every developer panel back where it started.",
            _ =>
            {
                DevConsoleCVars.Layout.Value = string.Empty;
                _layoutRestored = false;
            });

        console.Commands.Add(
            "watch",
            "Pins a variable to the watch panel.",
            context =>
            {
                string name = context[0];
                if (name.Length == 0)
                {
                    context.Print(Watch.Names.Count == 0 ? "Nothing is pinned." : string.Join(", ", Watch.Names));
                    return;
                }

                if (Watch.Add(name))
                {
                    ShowPinned(WatchPanel, DevConsoleCVars.WatchVisible);
                    context.Print($"Watching {name}.");
                }
            },
            "<variable>");

        console.Commands.Add(
            "unwatch",
            "Unpins a variable from the watch panel.",
            context =>
            {
                if (context[0].Length == 0)
                {
                    Watch.Clear();
                    context.Print("Unpinned everything.");
                    return;
                }

                context.Print(Watch.Remove(context[0]) ? $"Unpinned {context[0]}." : $"{context[0]} was not pinned.");
            },
            "[variable]");

        console.Commands.Add(
            "palette",
            "Opens the command palette.",
            _ => OpenPalette());

        console.Commands.Add(
            "find_log",
            "Filters the scrollback to lines containing some text.",
            context =>
            {
                _model.Filter.Search = context.Rest(0);
                context.Print(_model.Filter.Search.Length == 0
                    ? "Log filter cleared."
                    : $"Showing {_model.VisibleLineIndices.Count} line(s) matching '{_model.Filter.Search}'.");
            },
            "[text]");

        console.Commands.Add(
            "ent_list",
            "Lists what is in the world.",
            context =>
            {
                if (_world is null)
                {
                    context.Error("No world is loaded.");
                    return;
                }

                List<InspectableEntity> found = [];
                _world.Describe(context.Rest(0), found);

                foreach (InspectableEntity entity in found)
                {
                    context.Print(string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"{entity.Id,5}  {entity.Kind,-8} {entity.Name,-28} {entity.State}"));
                }

                context.Print($"{found.Count} entity(ies).");
            },
            "[filter]");

        console.Commands.Add(
            "ent_info",
            "Reports what is known about one entity.",
            context =>
            {
                if (_world is null)
                {
                    context.Error("No world is loaded.");
                    return;
                }

                if (!int.TryParse(context[0], System.Globalization.CultureInfo.InvariantCulture, out int id))
                {
                    context.Error("Usage: ent_info <id>");
                    return;
                }

                List<string> lines = [];
                _world.Detail(id, lines);

                if (lines.Count == 0)
                {
                    context.Error($"No entity {id}.");
                    return;
                }

                foreach (string line in lines)
                {
                    context.Print(line);
                }
            },
            "<id>");

        console.Commands.Add(
            "ent_select",
            "Marks an entity in the world and shows it in the World panel.",
            context =>
            {
                if (!int.TryParse(context[0], System.Globalization.CultureInfo.InvariantCulture, out int id))
                {
                    context.Error("Usage: ent_select <id>");
                    return;
                }

                // Same story as `con_panel`: from `settings.cfg` or a `+` argument this runs before
                // there is a display, so the selection is remembered rather than lost.
                if (_inspector is null)
                {
                    _pendingSelection = id;
                    _pendingOpen.Add(InspectorPanel);
                    return;
                }

                if (_inspectorWindow is not null)
                {
                    _inspectorWindow.IsOpen = true;
                    _model.IsOpen = true;
                }

                _inspector.SelectWhenListed(id);
                context.Print($"Selected {id}.");
            },
            "<id>");

        console.Commands.Add(
            "ent_goto",
            "Moves the free camera to look at an entity.",
            context =>
            {
                if (_world is null || !int.TryParse(context[0], System.Globalization.CultureInfo.InvariantCulture, out int id))
                {
                    context.Error("Usage: ent_goto <id>");
                    return;
                }

                context.Print(_world.TryMoveCameraTo(id) ? $"Camera moved to {id}." : $"Could not reach {id}.");
            },
            "<id>",
            CommandOptions.Cheat);

        console.Commands.Add(
            "ent_teleport",
            "Moves the player to an entity.",
            context =>
            {
                if (_world is null || !int.TryParse(context[0], System.Globalization.CultureInfo.InvariantCulture, out int id))
                {
                    context.Error("Usage: ent_teleport <id>");
                    return;
                }

                context.Print(_world.TryTeleportPlayerTo(id) ? $"Teleported to {id}." : $"Could not reach {id}.");
            },
            "<id>",
            CommandOptions.Cheat);

        console.Commands.Add(
            "con_copy",
            "Copies the visible scrollback to the clipboard.",
            context =>
            {
                if (WriteClipboard is null)
                {
                    context.Error("This platform has no clipboard.");
                    return;
                }

                var text = new System.Text.StringBuilder();
                foreach (int index in _model.VisibleLineIndices)
                {
                    text.AppendLine(_model.Lines[index].Text);
                }

                WriteClipboard(text.ToString());
                context.Print($"Copied {_model.VisibleLineIndices.Count} line(s).");
            });
    }

    private static bool IsPanelName(string name) =>
        Array.Exists(KnownPanels, panel => panel.Equals(name, StringComparison.OrdinalIgnoreCase));

    private void ShowPinned(string key, BoolCVar pinned)
    {
        if (_panels.TryGetValue(key, out UiWindow? window))
        {
            window.IsOpen = true;
            pinned.Value = true;
        }
    }

    private void OpenPalette()
    {
        if (_paletteWindow is null || _palette is null)
        {
            return;
        }

        _model.IsOpen = true;
        _palette.Reset();
        _paletteWindow.IsOpen = true;
        _layer?.Raise(_paletteWindow);
        ResetBlink();
    }

    private void ClosePalette()
    {
        if (_paletteWindow is not null)
        {
            _paletteWindow.IsOpen = false;
        }

        if (_consoleWindow is not null)
        {
            _layer?.Raise(_consoleWindow);
        }
    }

    /// <summary>
    /// Builds a layout context whose scale is the reader's alone.
    /// </summary>
    /// <param name="width">The back buffer's width.</param>
    /// <param name="height">The back buffer's height.</param>
    /// <returns>The context.</returns>
    /// <remarks>
    /// Passing the real viewport as the authored reference makes both ratios one, so the scale
    /// collapses to the user scale. That is what turns every <c>Px()</c> below into a real pixel, and
    /// why the console does not grow on an ultrawide the way a design-unit screen correctly does.
    /// </remarks>
    private static LayoutContext BuildContext(int width, int height)
    {
        float w = MathF.Max(1f, width);
        float h = MathF.Max(1f, height);
        return new LayoutContext(
            w,
            h,
            reference: new UiVec2(w, h),
            userScale: AutoScale(h) * DevConsoleCVars.Scale.Value);
    }

    /// <summary>
    /// Works out how much to enlarge the interface for the display it is on.
    /// </summary>
    /// <param name="height">The back buffer's height in pixels.</param>
    /// <returns>A factor <c>con_scale</c> multiplies.</returns>
    /// <remarks>
    /// <para>
    /// Sizing in raw pixels is right for aspect ratio and wrong for resolution: a wider display should
    /// show <em>more</em> console, but a denser one should not show a <em>smaller</em> one. Fourteen
    /// pixels of text is comfortable at 1080p and a squint at 4K on the same-sized monitor.
    /// </para>
    /// <para>
    /// Taken from the height against a 1080p baseline, and never allowed below one — at 720p the text
    /// is already as small as it should ever be, and shrinking it further to be proportional would be
    /// proportional and unreadable.
    /// </para>
    /// </remarks>
    private static float AutoScale(float height) => Math.Clamp(height / 1080f, 1f, 3f);

    private void RestoreLayoutOnce(LayoutContext context)
    {
        if (_layoutRestored || _panels.Count == 0)
        {
            return;
        }

        _layoutRestored = true;

        Dictionary<string, PanelPlacement> placements = DevConsoleLayout.Parse(DevConsoleCVars.Layout.Value);
        foreach ((string key, UiWindow window) in _panels)
        {
            if (placements.TryGetValue(key, out PanelPlacement placement))
            {
                DevConsoleLayout.Apply(window, placement, context.Viewport);
            }
            else
            {
                DefaultPlacement(key, window, context.Viewport);
            }
        }
    }

    /// <summary>
    /// Places a panel that has no stored position, in proportion to the display it finds itself on.
    /// </summary>
    /// <param name="key">Which panel it is.</param>
    /// <param name="window">The window to place.</param>
    /// <param name="viewport">The back buffer's size in pixels.</param>
    /// <remarks>
    /// The defaults are fractions rather than the numbers the panels were authored with, because those
    /// were chosen against a 1080p screen and would put half the variable browser off a 1280×720 one.
    /// </remarks>
    private static void DefaultPlacement(string key, UiWindow window, UiVec2 viewport)
    {
        float w = viewport.X;
        float h = viewport.Y;
        float margin = 24f;

        window.Frame = key switch
        {
            ConsolePanel => new UiRect(margin, margin, (w * 0.72f) - margin, (h * 0.58f) - margin),
            CVarPanel => new UiRect(w * 0.73f, margin, (w * 0.27f) - margin, h * 0.72f),
            WatchPanel => new UiRect(margin, h * 0.62f, w * 0.28f, h * 0.12f),
            InspectorPanel => new UiRect(w * 0.32f, h * 0.62f, w * 0.40f, h * 0.34f),
            PalettePanel => new UiRect(w * 0.22f, h * 0.16f, w * 0.56f, h * 0.5f),
            _ => window.Frame,
        };

        window.ClampIntoView(viewport);
    }

    private void SyncViewport(LayoutContext context)
    {
        int width = (int)context.Viewport.X;
        int height = (int)context.Viewport.Y;

        if (width == _viewportWidth && height == _viewportHeight)
        {
            return;
        }

        _viewportWidth = width;
        _viewportHeight = height;

        // A panel parked against the right edge of a wide display would otherwise sit entirely off a
        // narrower one, and be restored there again on every launch.
        _layer?.ClampAll(context.Viewport);
    }

    /// <summary>
    /// Decides which panels are showing this frame and hands each its per-frame state.
    /// </summary>
    /// <remarks>
    /// The console's own window follows <see cref="ConsoleModel.IsOpen"/>. The tools that belong to it
    /// are hidden with it. The two pinnable panels are the exception: they may outlive it, which is the
    /// whole reason for pinning.
    /// </remarks>
    private void SyncPanels()
    {
        bool open = _model.IsOpen;

        if (_consoleWindow is not null)
        {
            _consoleWindow.IsOpen = open;
        }

        if (!open)
        {
            if (_cvarWindow is not null)
            {
                _cvarWindow.IsOpen = false;
            }

            if (_paletteWindow is not null)
            {
                _paletteWindow.IsOpen = false;
            }

            if (_inspectorWindow is not null)
            {
                _inspectorWindow.IsOpen = false;
            }

            if (_watchWindow is not null)
            {
                _watchWindow.IsOpen = DevConsoleCVars.WatchVisible.Value;
            }
        }

        // The watch panel hugs what is pinned, so adding a value grows it instead of leaving a band
        // of nothing under two rows -- until the reader resizes it, after which it is theirs.
        if (_watchAutoHeight && _watch is not null && _watchWindow is { IsOpen: true } watch)
        {
            float wanted = MathF.Round(watch.TitleHeight + _watch.PreferredHeight);
            if (MathF.Abs(watch.Frame.Height - wanted) > 0.5f)
            {
                watch.Frame = new UiRect(watch.Frame.X, watch.Frame.Y, watch.Frame.Width, MathF.Max(watch.MinSize.Y, wanted));
            }
        }

        if (_body is not null)
        {
            _body.CaretVisible = _caretVisible && Focused == _consoleWindow;
        }

        if (_cvars is not null)
        {
            _cvars.CaretVisible = _caretVisible;
        }

        if (_palette is not null)
        {
            _palette.CaretVisible = _caretVisible;
        }
    }

    /// <summary>
    /// Keeps every window's chrome in step with the type scale.
    /// </summary>
    /// <remarks>
    /// The title face has to be handed to the window rather than derived inside it: a coverage atlas
    /// is only crisp at the height it was rasterised at, so the bar has to be told which face to use
    /// rather than scaling whichever one it happened to hold.
    /// </remarks>
    private void ApplyChrome()
    {
        if (_theme is null)
        {
            return;
        }

        foreach (UiWindow window in _panels.Values)
        {
            window.TitleFont = _theme.TitleFont;
            window.TitleTextSize = _theme.TitleSize;
            window.TitleHeight = _theme.TitleBarHeight;
            window.Gutter = _theme.Gutter;
            window.CornerRadius = DevConsoleTheme.CornerRadius;
            window.GripSize = MathF.Max(5f, _theme.Unit * 1.5f);
        }
    }

    private UiWindow? Focused => _layer?.Focused;

    private void AdvanceCaret(float elapsedSeconds)
    {
        _blink += elapsedSeconds;
        while (_blink >= BlinkPeriod)
        {
            _blink -= BlinkPeriod;
            _caretVisible = !_caretVisible;
        }
    }

    private void ResetBlink()
    {
        _blink = 0f;
        _caretVisible = true;
    }

    private void HandlePointer(InputState input)
    {
        if (_layer is null)
        {
            return;
        }

        MouseState mouse = input.Mouse;
        var point = new UiVec2(mouse.X, mouse.Y);
        bool pressed = mouse.WasPressed(MouseButton.Left);
        _viewportPointer = point;

        // The menu is above everything, so it gets first refusal on every press -- otherwise a click
        // meant for an item would be taken by whatever window happens to be under it.
        if (_menu.IsOpen)
        {
            _menu.HoveredIndex = _menu.ItemAt(point, _theme!.RowHeight);

            if (pressed)
            {
                if (!_menu.Invoke(_menu.HoveredIndex))
                {
                    _menu.Close();
                }

                return;
            }

            if (mouse.WasPressed(MouseButton.Right))
            {
                _menu.Close();
            }
        }

        if (mouse.WasPressed(MouseButton.Right))
        {
            OpenContextMenu(point);
            return;
        }

        // The palette's list and the console's completion dropdown both float above the windows they
        // belong to, so they are offered the press first; the window beneath would otherwise take it
        // and neither list could ever be clicked.
        if (pressed && HandleFloatingClick(point))
        {
            return;
        }

        if (HandleScrollBar(point, mouse, pressed) || HandleLogSelection(point, mouse, pressed))
        {
            return;
        }

        _layer.UpdatePointer(mouse, new UiVec2(_viewportWidth, _viewportHeight));

        // Dragging the watch panel's top or bottom edge is the reader deciding its height, so it stops
        // deciding its own.
        if (ReferenceEquals(_layer.Active, _watchWindow) &&
            (_layer.ActiveRegion & (WindowRegion.Top | WindowRegion.Bottom)) != 0)
        {
            _watchAutoHeight = false;
        }

        if (pressed)
        {
            HandlePanelClick(point);
        }

        if (mouse.WheelDelta != 0f)
        {
            HandleWheel(point, mouse.WheelDelta);
        }
    }

    /// <summary>Runs the scrollbar's thumb drag, which has to be offered the pointer before selection.</summary>
    /// <param name="point">Where the pointer is.</param>
    /// <param name="mouse">The frame's mouse, read before capture.</param>
    /// <param name="pressed">Whether the left button went down this frame.</param>
    /// <returns><see langword="true"/> when the bar owned the pointer.</returns>
    private bool HandleScrollBar(UiVec2 point, MouseState mouse, bool pressed)
    {
        if (_body is null || _consoleWindow is not { IsOpen: true })
        {
            return false;
        }

        if (_body.IsScrollDragging)
        {
            if (mouse.IsDown(MouseButton.Left))
            {
                _body.DragScroll(point);
            }
            else
            {
                _body.EndScrollDrag();
            }

            return true;
        }

        return pressed && _body.BeginScrollDrag(point);
    }

    /// <summary>
    /// Runs the scrollback's click-and-drag text selection.
    /// </summary>
    /// <param name="point">Where the pointer is.</param>
    /// <param name="mouse">The frame's mouse, read before capture.</param>
    /// <param name="pressed">Whether the left button went down this frame.</param>
    /// <returns><see langword="true"/> when the pointer belonged to a selection.</returns>
    /// <remarks>
    /// A drag continues while the button is held even after the pointer leaves the log — the row is
    /// clamped rather than dropped — so running off the top or bottom keeps extending the selection
    /// instead of stopping dead where the viewport ended.
    /// </remarks>
    private bool HandleLogSelection(UiVec2 point, MouseState mouse, bool pressed)
    {
        if (_body is null || _consoleWindow is not { IsOpen: true })
        {
            return false;
        }

        if (_body.Selection.IsDragging)
        {
            if (mouse.IsDown(MouseButton.Left))
            {
                _body.ExtendSelect(point);
            }
            else
            {
                _body.EndSelect();
            }

            return true;
        }

        if (pressed && _consoleWindow.Frame.Contains(point) && _body.BeginSelect(point))
        {
            _layer?.Raise(_consoleWindow);
            return true;
        }

        // A press anywhere else drops the selection, which is what makes it feel like a selection
        // rather than a highlight that has to be dismissed.
        if (pressed)
        {
            _body.ClearSelection();
        }

        return false;
    }

    /// <summary>
    /// Opens the right-click menu with whatever is possible where the pointer is.
    /// </summary>
    /// <param name="point">Where the pointer was.</param>
    private void OpenContextMenu(UiVec2 point)
    {
        if (_body is null || _consoleWindow is not { IsOpen: true } || !_consoleWindow.Frame.Contains(point))
        {
            return;
        }

        bool hasSelection = _body.Selection.IsActive;
        bool canWrite = WriteClipboard is not null;

        // Copy is offered whether or not anything is selected, and greyed when it is not. Hiding it
        // would move every item below it, so the same entry would sit in a different place depending
        // on what the reader had done a moment earlier.
        _menu.Open(
            point,
            [
                new ContextMenuItem("Copy", () => WriteClipboard?.Invoke(_body.SelectedText()), hasSelection && canWrite),
                new ContextMenuItem("Copy all", () => WriteClipboard?.Invoke(_body.AllVisibleText()), canWrite),
                new ContextMenuItem("Select all", _body.SelectAllLines),
                new ContextMenuItem("Paste", PasteFromClipboard, ReadClipboard is not null),
                new ContextMenuItem("Clear", () =>
                {
                    _body.ClearSelection();
                    _model.ClearScrollback();
                }),
            ]);
    }

    private void PasteFromClipboard()
    {
        if (ReadClipboard?.Invoke() is { Length: > 0 } pasted)
        {
            _model.Insert(pasted.ReplaceLineEndings(" ").Trim());
        }
    }

    private bool HandleFloatingClick(UiVec2 point)
    {
        if (_paletteWindow is { IsOpen: true } && _palette is not null && _palette.EntryAt(point) is >= 0 and var entry)
        {
            _palette.SelectedIndex = entry;
            RunPaletteSelection();
            return true;
        }

        if (_consoleWindow is { IsOpen: true } && _body is not null && _model.SelectSuggestion(_body.SuggestionAt(point)))
        {
            _model.AcceptSuggestion();
            ResetBlink();
            return true;
        }

        return false;
    }

    private void HandlePanelClick(UiVec2 point)
    {
        if (_inspector is not null && _inspectorWindow is { IsOpen: true } && _inspectorWindow.Frame.Contains(point))
        {
            int entity = _inspector.RowAt(point);
            if (entity >= 0)
            {
                _inspector.SelectedIndex = entity;
            }

            return;
        }

        if (_body is not null && _consoleWindow is { IsOpen: true } && _consoleWindow.Frame.Contains(point))
        {
            for (int i = 0; i < _body.SeverityToggles.Count; i++)
            {
                if (_body.SeverityToggles[i].Contains(point))
                {
                    ConsoleSeverity severity = ConsoleBody.SeverityAt(i);
                    _model.Filter.SetSeverity(severity, !_model.Filter.IsSeverityShown(severity));
                    return;
                }
            }

            if (_body.FindRect.Contains(point))
            {
                _findFocused = true;
                return;
            }

            _findFocused = false;
        }

        if (_cvars is null || _cvarWindow is not { IsOpen: true } || !_cvarWindow.Frame.Contains(point))
        {
            return;
        }

        int row = _cvars.RowAt(point);
        if (row >= 0)
        {
            // A second click on the row already chosen opens the value for editing, which is the
            // gesture a table has trained everyone to expect.
            bool again = row == _cvars.SelectedIndex;
            _cvars.SelectedIndex = row;

            if (again && !_cvars.IsEditing && !_cvars.CycleSelected())
            {
                _cvars.BeginEdit();
            }

            ResetBlink();
        }
    }

    private void HandleWheel(UiVec2 point, float wheel)
    {
        if (_inspector is not null && _inspectorWindow is { IsOpen: true } && _inspector.Rows.Bounds.Contains(point))
        {
            _inspector.Rows.ScrollByRows(-wheel * 3f);
            return;
        }

        if (_cvars is not null && _cvarWindow is { IsOpen: true } && _cvars.Rows.Bounds.Contains(point))
        {
            _cvars.Rows.ScrollByRows(-wheel * 3f);
            return;
        }

        if (_body is not null && _consoleWindow is { IsOpen: true } && _body.Log.Bounds.Contains(point))
        {
            _model.Scroll((int)(wheel * 3f), VisibleLines);
        }
    }

    private void HandleKeys(KeyboardState keyboard, float elapsed)
    {
        bool control = keyboard.IsDown(KeyCode.LeftControl) || keyboard.IsDown(KeyCode.RightControl);
        bool shift = keyboard.IsDown(KeyCode.LeftShift) || keyboard.IsDown(KeyCode.RightShift);

        // Escape dismisses the menu before it closes anything else, so the key does the least
        // destructive thing that is still meaningful.
        if (_menu.IsOpen && keyboard.WasPressed(KeyCode.Escape))
        {
            _menu.Close();
            return;
        }

        if (control && keyboard.WasPressed(KeyCode.P))
        {
            OpenPalette();
            return;
        }

        if (_paletteWindow is { IsOpen: true })
        {
            HandlePaletteKeys(keyboard, elapsed, control);
            return;
        }

        if (_cvarWindow is { IsOpen: true } && ReferenceEquals(Focused, _cvarWindow))
        {
            HandleCVarKeys(keyboard, elapsed, control, shift);
            return;
        }

        if (_inspectorWindow is { IsOpen: true } && ReferenceEquals(Focused, _inspectorWindow))
        {
            HandleInspectorKeys(keyboard, elapsed, control, shift);
            return;
        }

        HandleConsoleKeys(keyboard, elapsed, control, shift);
    }

    private void HandlePaletteKeys(KeyboardState keyboard, float elapsed, bool control)
    {
        CommandPaletteBody palette = _palette!;

        if (keyboard.WasPressed(KeyCode.Escape))
        {
            ClosePalette();
            return;
        }

        if (keyboard.WasPressed(KeyCode.Return) || keyboard.WasPressed(KeyCode.NumpadEnter))
        {
            RunPaletteSelection();
            return;
        }

        if (_up.Poll(keyboard, KeyCode.Up, elapsed))
        {
            palette.SelectedIndex--;
        }

        if (_down.Poll(keyboard, KeyCode.Down, elapsed))
        {
            palette.SelectedIndex++;
        }

        if (EditText(palette.Query, keyboard, elapsed, control, shift: false))
        {
            palette.Refresh();
            palette.SelectedIndex = 0;
        }
    }

    private void RunPaletteSelection()
    {
        if (_palette?.Selected is not { } entry)
        {
            return;
        }

        ClosePalette();

        // A command is run; a variable is only offered, because running a bare variable name merely
        // prints it and a reader who went looking for it almost always meant to change it.
        if (entry.Kind == ConsoleSuggestionKind.Variable)
        {
            _model.SetInput(entry.Text + " ");
        }
        else
        {
            _model.SetInput(entry.Text);
            _model.Submit();
        }

        ResetBlink();
    }

    private void HandleCVarKeys(KeyboardState keyboard, float elapsed, bool control, bool shift)
    {
        CVarBrowserBody cvars = _cvars!;

        if (cvars.IsEditing)
        {
            if (keyboard.WasPressed(KeyCode.Escape))
            {
                cvars.CancelEdit();
                return;
            }

            if (keyboard.WasPressed(KeyCode.Return) || keyboard.WasPressed(KeyCode.NumpadEnter))
            {
                cvars.CommitEdit();
                return;
            }

            EditText(cvars.ValueEdit, keyboard, elapsed, control, shift);
            return;
        }

        if (keyboard.WasPressed(KeyCode.Escape))
        {
            _cvarWindow!.IsOpen = false;
            if (_consoleWindow is not null)
            {
                _layer?.Raise(_consoleWindow);
            }

            return;
        }

        if (_up.Poll(keyboard, KeyCode.Up, elapsed))
        {
            cvars.SelectedIndex = Math.Max(0, cvars.SelectedIndex - 1);
            cvars.RevealSelection();
        }

        if (_down.Poll(keyboard, KeyCode.Down, elapsed))
        {
            cvars.SelectedIndex++;
            cvars.RevealSelection();
        }

        if (keyboard.WasPressed(KeyCode.Return) || keyboard.WasPressed(KeyCode.NumpadEnter))
        {
            if (!cvars.CycleSelected())
            {
                cvars.BeginEdit();
            }

            ResetBlink();
            return;
        }

        if (control && keyboard.WasPressed(KeyCode.R))
        {
            cvars.ResetSelected();
            return;
        }

        if (EditText(cvars.FilterEdit, keyboard, elapsed, control, shift))
        {
            cvars.ApplyFilter();
        }
    }

    private void HandleInspectorKeys(KeyboardState keyboard, float elapsed, bool control, bool shift)
    {
        InspectorBody inspector = _inspector!;

        if (keyboard.WasPressed(KeyCode.Escape))
        {
            _inspectorWindow!.IsOpen = false;
            if (_consoleWindow is not null)
            {
                _layer?.Raise(_consoleWindow);
            }

            return;
        }

        if (_up.Poll(keyboard, KeyCode.Up, elapsed))
        {
            inspector.SelectedIndex = Math.Max(0, inspector.SelectedIndex - 1);
            inspector.RevealSelection();
        }

        if (_down.Poll(keyboard, KeyCode.Down, elapsed))
        {
            inspector.SelectedIndex++;
            inspector.RevealSelection();
        }

        // Return takes the camera to whatever is selected, which is what a reader wants from a list of
        // things they cannot see; teleporting the player is a cheat and stays behind its command.
        if (keyboard.WasPressed(KeyCode.Return) && inspector.Selected is { } entity)
        {
            _world?.TryMoveCameraTo(entity.Id);
        }

        EditText(inspector.FilterEdit, keyboard, elapsed, control, shift);
    }

    private void HandleConsoleKeys(KeyboardState keyboard, float elapsed, bool control, bool shift)
    {
        if (keyboard.WasPressed(KeyCode.Escape))
        {
            _model.IsOpen = false;
            return;
        }

        if (control && HandleClipboardKeys(keyboard))
        {
            ResetBlink();
            return;
        }

        // While the find box has been clicked into, typing narrows the scrollback rather than
        // composing a command. Escape and Return both hand the keyboard back to the prompt.
        if (_findFocused && _body is not null)
        {
            if (keyboard.WasPressed(KeyCode.Return) || keyboard.WasPressed(KeyCode.NumpadEnter))
            {
                _findFocused = false;
                return;
            }

            if (EditText(_body.FindEdit, keyboard, elapsed, control, shift))
            {
                _model.Filter.Search = _body.FindEdit.Text;
            }

            return;
        }

        if (keyboard.WasPressed(KeyCode.Return) || keyboard.WasPressed(KeyCode.NumpadEnter))
        {
            if (_model.SuggestionIndex >= 0)
            {
                _model.AcceptSuggestion();
            }
            else
            {
                _model.Submit();
            }

            ResetBlink();
            return;
        }

        if (keyboard.WasPressed(KeyCode.Tab))
        {
            _model.Complete();
            ResetBlink();
            return;
        }

        // Up and down steer the completion list while it is showing and the history when it is not,
        // which is the reading every shell with a dropdown has.
        if (_up.Poll(keyboard, KeyCode.Up, elapsed))
        {
            if (_model.RichSuggestions.Count > 0)
            {
                _model.MoveSuggestion(-1);
            }
            else
            {
                _model.HistoryPrevious();
            }

            ResetBlink();
        }

        if (_down.Poll(keyboard, KeyCode.Down, elapsed))
        {
            if (_model.SuggestionIndex >= 0)
            {
                _model.MoveSuggestion(1);
            }
            else
            {
                _model.HistoryNext();
            }

            ResetBlink();
        }

        int page = Math.Max(1, VisibleLines - 2);

        if (_pageUp.Poll(keyboard, KeyCode.PageUp, elapsed))
        {
            _model.Scroll(page, VisibleLines);
        }

        if (_pageDown.Poll(keyboard, KeyCode.PageDown, elapsed))
        {
            _model.Scroll(-page, VisibleLines);
        }

        if (EditPrompt(keyboard, elapsed, control, shift))
        {
            ResetBlink();
        }
    }

    private bool HandleClipboardKeys(KeyboardState keyboard)
    {
        if (keyboard.WasPressed(KeyCode.A))
        {
            _model.SelectAll();
            return true;
        }

        // A scrollback selection wins over the prompt's, because it is the one the reader can see and
        // the one they just made with the mouse. The prompt's own selection is usually empty anyway.
        if (keyboard.WasPressed(KeyCode.C) && _body is { } body && body.Selection.IsActive)
        {
            WriteClipboard?.Invoke(body.SelectedText());
            return true;
        }

        if (keyboard.WasPressed(KeyCode.C) && _model.Edit.HasSelection)
        {
            WriteClipboard?.Invoke(_model.Edit.SelectedText);
            return true;
        }

        if (keyboard.WasPressed(KeyCode.X) && _model.Edit.HasSelection)
        {
            WriteClipboard?.Invoke(_model.Edit.SelectedText);
            _model.Backspace();
            return true;
        }

        if (keyboard.WasPressed(KeyCode.V))
        {
            if (ReadClipboard?.Invoke() is { Length: > 0 } pasted)
            {
                _model.Insert(pasted);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Applies editing keys and typed text to the console's prompt.
    /// </summary>
    /// <param name="keyboard">The frame's keyboard.</param>
    /// <param name="elapsed">Real seconds since the last call.</param>
    /// <param name="control">Whether a control key is held.</param>
    /// <param name="shift">Whether a shift key is held.</param>
    /// <returns><see langword="true"/> when anything changed.</returns>
    /// <remarks>
    /// Separate from <see cref="EditText"/> because the prompt is not a plain field: every edit has to
    /// refresh the completion list, which only the model can do.
    /// </remarks>
    private bool EditPrompt(KeyboardState keyboard, float elapsed, bool control, bool shift)
    {
        bool changed = false;

        if (_backspace.Poll(keyboard, KeyCode.Backspace, elapsed))
        {
            _model.Backspace();
            changed = true;
        }

        if (_delete.Poll(keyboard, KeyCode.Delete, elapsed))
        {
            _model.Delete();
            changed = true;
        }

        if (_left.Poll(keyboard, KeyCode.Left, elapsed))
        {
            if (control)
            {
                _model.MoveCaretByWord(-1, shift);
            }
            else
            {
                _model.MoveCaret(-1, shift);
            }

            changed = true;
        }

        if (_right.Poll(keyboard, KeyCode.Right, elapsed))
        {
            if (control)
            {
                _model.MoveCaretByWord(1, shift);
            }
            else
            {
                _model.MoveCaret(1, shift);
            }

            changed = true;
        }

        if (keyboard.WasPressed(KeyCode.Home))
        {
            _model.CaretToStart(shift);
            changed = true;
        }

        if (keyboard.WasPressed(KeyCode.End))
        {
            _model.CaretToEnd(shift);
            changed = true;
        }

        while (keyboard.TryDequeueCharacter(out char typed))
        {
            if (IsPrintable(typed))
            {
                _model.Insert(typed.ToString());
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Applies editing keys and typed text to a plain field.</summary>
    /// <param name="edit">The field being edited.</param>
    /// <param name="keyboard">The frame's keyboard.</param>
    /// <param name="elapsed">Real seconds since the last call.</param>
    /// <param name="control">Whether a control key is held.</param>
    /// <param name="shift">Whether a shift key is held.</param>
    /// <returns><see langword="true"/> when the text changed.</returns>
    private bool EditText(TextEditState edit, KeyboardState keyboard, float elapsed, bool control, bool shift)
    {
        bool changed = false;

        if (control && keyboard.WasPressed(KeyCode.A))
        {
            edit.SelectAll();
        }

        if (control && keyboard.WasPressed(KeyCode.V) && ReadClipboard?.Invoke() is { Length: > 0 } pasted)
        {
            changed |= edit.Insert(pasted);
        }

        if (_backspace.Poll(keyboard, KeyCode.Backspace, elapsed))
        {
            changed |= edit.Backspace();
        }

        if (_delete.Poll(keyboard, KeyCode.Delete, elapsed))
        {
            changed |= edit.Delete();
        }

        if (_left.Poll(keyboard, KeyCode.Left, elapsed))
        {
            edit.Move(-1, shift);
        }

        if (_right.Poll(keyboard, KeyCode.Right, elapsed))
        {
            edit.Move(1, shift);
        }

        if (keyboard.WasPressed(KeyCode.Home))
        {
            edit.MoveToStart(shift);
        }

        if (keyboard.WasPressed(KeyCode.End))
        {
            edit.MoveToEnd(shift);
        }

        while (keyboard.TryDequeueCharacter(out char typed))
        {
            if (IsPrintable(typed))
            {
                changed |= edit.Insert(typed.ToString());
            }
        }

        if (changed)
        {
            ResetBlink();
        }

        return changed;
    }

    /// <summary>
    /// Decides whether a decoded character belongs in a field.
    /// </summary>
    /// <param name="c">The character the platform decoded.</param>
    /// <returns><see langword="true"/> when it should be inserted.</returns>
    /// <remarks>
    /// The toggle key is excluded because the platform decodes text before the key event is seen, so
    /// opening the console would otherwise leave a backtick sitting in whatever had focus.
    /// </remarks>
    private static bool IsPrintable(char c) =>
        c is not ('`' or '~' or '\r' or '\n' or '\t' or '\b') && !char.IsControl(c);
}
