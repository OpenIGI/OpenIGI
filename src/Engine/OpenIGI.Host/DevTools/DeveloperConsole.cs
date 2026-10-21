using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Input;
using OpenIGI.Presentation.Text;
using OpenIGI.Rendering;
using OpenIGI.UI.Layout;
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
/// <para>
/// This class is the coordinator. The panels themselves live in <see cref="DevConsolePanels"/>, the
/// mouse in <see cref="DevConsolePointer"/>, typing in <see cref="PromptEditor"/>, the panel commands in
/// <see cref="DevConsoleCommands"/> and the overlay marks in <see cref="SelectionMarkerRenderer"/>; each
/// panel's own keys are handled by its body.
/// </para>
/// </remarks>
public sealed class DeveloperConsole : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly ConsoleModel _model;
    private readonly IOpenIgiLogger _logger;
    private readonly ContextMenu _menu = new();
    private readonly PromptEditor _editor = new();
    private readonly DevConsolePanels _panels;
    private readonly DevConsolePointer _pointer;

    // The panels' key handlers are handed these every frame a panel has the keyboard, so they are
    // made once here rather than allocated afresh on each keystroke.
    private readonly Action _closePalette;
    private readonly Action _runPaletteSelection;
    private readonly Action _closeCVars;
    private readonly Action _closeInspector;

    private UiSurfaces? _surfaces;
    private MonoFontSet? _fonts;

    private bool _disposed;
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
        _panels = new DevConsolePanels(model, _editor, Watch);
        _pointer = new DevConsolePointer(_panels, _editor, _menu);
        _closePalette = _panels.ClosePalette;
        _runPaletteSelection = _panels.RunPaletteSelection;
        _closeCVars = () => _panels.CloseToConsole(_panels.CVarWindow!);
        _closeInspector = () => _panels.CloseToConsole(_panels.InspectorWindow!);
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
        _panels.World = world;
        _panels.Inspector?.Attach(world);
    }

    /// <summary>Gets a value indicating whether the fonts and sprites are loaded.</summary>
    public bool IsReady => _panels.Theme is not null;

    /// <summary>Gets or sets the clipboard reader, so a paste can reach the system clipboard.</summary>
    public Func<string>? ReadClipboard
    {
        get => _editor.ReadClipboard;
        set => _editor.ReadClipboard = value;
    }

    /// <summary>Gets or sets the clipboard writer.</summary>
    public Action<string>? WriteClipboard
    {
        get => _editor.WriteClipboard;
        set => _editor.WriteClipboard = value;
    }

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
        _panels.InspectorWindow is { IsOpen: true } && _panels.Inspector?.Selected is { } entity ? entity.Id : null;

    /// <summary>Gets how many scrollback lines are on screen, for page-up and page-down.</summary>
    public int VisibleLines => _panels.VisibleLines;

    /// <summary>Builds the fonts, the generated sprites and every panel.</summary>
    /// <param name="console">The console the panels read from and act on.</param>
    /// <returns><see langword="true"/> when everything loaded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="console"/> is <see langword="null"/>.</exception>
    /// <remarks>Failure is reported and swallowed: a missing font must cost the console, never the game.</remarks>
    public bool Load(ConsoleSystem console)
    {
        ArgumentNullException.ThrowIfNull(console);

        if (_panels.Theme is not null)
        {
            return true;
        }

        try
        {
            _fonts = new MonoFontSet(_device, UiFontLibrary.Mono);
            _surfaces = UiSurfaces.Create(_device);
            var theme = new DevConsoleTheme(_fonts, DevConsoleCVars.Palette.Value);
            var layer = new WindowLayer { SnapDistance = DevConsoleCVars.Snap.Value };

            _panels.Build(console, theme, _surfaces, layer);

            // Pins are restored here rather than at registration because settings.cfg has only just
            // been applied; asking any earlier would read the variable's default. They are *added*
            // rather than parsed over the top, so a `watch` that ran from the configuration or the
            // command line before this point is kept instead of being wiped by the saved set.
            foreach (string pinned in DevConsoleCVars.Watch.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Watch.Add(pinned);
            }

            // Panels asked for before there were any: apply them now that they exist.
            foreach (string name in _panels.PendingOpen)
            {
                if (_panels.Windows.TryGetValue(name, out UiWindow? deferred))
                {
                    deferred.IsOpen = true;
                }
            }

            _panels.PendingOpen.Clear();
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

        _editor.AdvanceCaret(elapsedSeconds);
        _panels.Layer?.Update(elapsedSeconds);

        if (!_model.IsOpen)
        {
            return false;
        }

        // Position, buttons and wheel are all read here, before the capture turns every one of them
        // except the position to zero for the rest of the frame.
        _pointer.Update(input, new UiVec2(_viewportWidth, _viewportHeight));
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

        if (_panels.Theme is not { } theme || _panels.Layer is not { } layer)
        {
            return;
        }

        LayoutContext context = BuildContext(screenWidth, screenHeight);

        // The type scale and the spacing rhythm both follow the reader's settings, and every panel
        // reads them from here -- so a change to con_font_size or con_scale moves the whole interface
        // together instead of leaving one panel at the old size.
        theme.Resize(context.Px(DevConsoleCVars.FontSize.Value), context.Scale);
        ApplyChrome();

        RestoreLayoutOnce(context);
        SyncViewport(context);
        SyncPanels();

        var painter = UiPainter.Create(batch, context);
        painter.PushOpacity(DevConsoleCVars.Opacity.Value);

        layer.Measure(context, context.Viewport);
        layer.Arrange(new UiRect(0f, 0f, context.Viewport.X, context.Viewport.Y));
        layer.Draw(painter);

        // Over every window, and at full opacity: a menu the reader just asked for should not be
        // showing the scene through it.
        SelectionMarkerRenderer.DrawSelectionMarker(painter, context.Viewport, _panels.Theme, _panels.Inspector, _panels.InspectorWindow);
        _menu.Draw(painter, theme, context.Viewport);
        SelectionMarkerRenderer.DrawPointer(painter, _panels.Theme, _pointer.Position);
    }

    /// <summary>Stores where the panels sit and what is pinned, so both survive a restart.</summary>
    public void SaveLayout()
    {
        if (_panels.Windows.Count > 0)
        {
            DevConsoleCVars.Layout.Value = DevConsoleLayout.Render(_panels.Windows);
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

        new DevConsoleCommands(_panels, _editor).Register(console);
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
        if (_panels.LayoutRestored || _panels.Windows.Count == 0)
        {
            return;
        }

        _panels.LayoutRestored = true;

        Dictionary<string, PanelPlacement> placements = DevConsoleLayout.Parse(DevConsoleCVars.Layout.Value);
        foreach ((string key, UiWindow window) in _panels.Windows)
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
            DevConsolePanels.ConsolePanel => new UiRect(margin, margin, (w * 0.72f) - margin, (h * 0.58f) - margin),
            DevConsolePanels.CVarPanel => new UiRect(w * 0.73f, margin, (w * 0.27f) - margin, h * 0.72f),
            DevConsolePanels.WatchPanel => new UiRect(margin, h * 0.62f, w * 0.28f, h * 0.12f),
            DevConsolePanels.InspectorPanel => new UiRect(w * 0.32f, h * 0.62f, w * 0.40f, h * 0.34f),
            DevConsolePanels.PalettePanel => new UiRect(w * 0.22f, h * 0.16f, w * 0.56f, h * 0.5f),
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
        _panels.Layer?.ClampAll(context.Viewport);
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

        if (_panels.ConsoleWindow is { } consoleWindow)
        {
            consoleWindow.IsOpen = open;
        }

        if (!open)
        {
            if (_panels.CVarWindow is { } cvarWindow)
            {
                cvarWindow.IsOpen = false;
            }

            if (_panels.PaletteWindow is { } paletteWindow)
            {
                paletteWindow.IsOpen = false;
            }

            if (_panels.InspectorWindow is { } inspectorWindow)
            {
                inspectorWindow.IsOpen = false;
            }

            if (_panels.WatchWindow is { } watchWindow)
            {
                watchWindow.IsOpen = DevConsoleCVars.WatchVisible.Value;
            }
        }

        // The watch panel hugs what is pinned, so adding a value grows it instead of leaving a band
        // of nothing under two rows -- until the reader resizes it, after which it is theirs.
        if (_panels.WatchAutoHeight && _panels.Watch is { } watchBody && _panels.WatchWindow is { IsOpen: true } watch)
        {
            float wanted = MathF.Round(watch.TitleHeight + watchBody.PreferredHeight);
            if (MathF.Abs(watch.Frame.Height - wanted) > 0.5f)
            {
                watch.Frame = new UiRect(watch.Frame.X, watch.Frame.Y, watch.Frame.Width, MathF.Max(watch.MinSize.Y, wanted));
            }
        }

        bool caretVisible = _editor.CaretVisible;

        if (_panels.Body is { } body)
        {
            body.CaretVisible = caretVisible && _panels.Focused == _panels.ConsoleWindow;
        }

        if (_panels.CVars is { } cvars)
        {
            cvars.CaretVisible = caretVisible;
        }

        if (_panels.Palette is { } palette)
        {
            palette.CaretVisible = caretVisible;
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
        if (_panels.Theme is not { } theme)
        {
            return;
        }

        foreach (UiWindow window in _panels.Windows.Values)
        {
            window.TitleFont = theme.TitleFont;
            window.TitleTextSize = theme.TitleSize;
            window.TitleHeight = theme.TitleBarHeight;
            window.Gutter = theme.Gutter;
            window.CornerRadius = DevConsoleTheme.CornerRadius;
            window.GripSize = MathF.Max(5f, theme.Unit * 1.5f);
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
            _panels.OpenPalette();
            return;
        }

        if (_panels.PaletteWindow is { IsOpen: true })
        {
            _panels.Palette!.HandleKeys(keyboard, elapsed, control, _editor, _closePalette, _runPaletteSelection);
            return;
        }

        if (_panels.CVarWindow is { IsOpen: true } && ReferenceEquals(_panels.Focused, _panels.CVarWindow))
        {
            _panels.CVars!.HandleKeys(keyboard, elapsed, control, shift, _editor, _closeCVars);
            return;
        }

        if (_panels.InspectorWindow is { IsOpen: true } && ReferenceEquals(_panels.Focused, _panels.InspectorWindow))
        {
            _panels.Inspector!.HandleKeys(keyboard, elapsed, control, shift, _editor, _closeInspector);
            return;
        }

        HandleConsoleKeys(keyboard, elapsed, control, shift);
    }

    private void HandleConsoleKeys(KeyboardState keyboard, float elapsed, bool control, bool shift)
    {
        if (keyboard.WasPressed(KeyCode.Escape))
        {
            _model.IsOpen = false;
            return;
        }

        if (control && _editor.HandleClipboardKeys(_model, _panels.Body, keyboard))
        {
            _editor.ResetBlink();
            return;
        }

        // While the find box has been clicked into, typing narrows the scrollback rather than
        // composing a command. Escape and Return both hand the keyboard back to the prompt.
        if (_panels.FindFocused && _panels.Body is { } body)
        {
            if (keyboard.WasPressed(KeyCode.Return) || keyboard.WasPressed(KeyCode.NumpadEnter))
            {
                _panels.FindFocused = false;
                return;
            }

            if (_editor.EditText(body.FindEdit, keyboard, elapsed, control, shift))
            {
                _model.Filter.Search = body.FindEdit.Text;
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

            _editor.ResetBlink();
            return;
        }

        if (keyboard.WasPressed(KeyCode.Tab))
        {
            _model.Complete();
            _editor.ResetBlink();
            return;
        }

        // Up and down steer the completion list while it is showing and the history when it is not,
        // which is the reading every shell with a dropdown has.
        if (_editor.Up.Poll(keyboard, KeyCode.Up, elapsed))
        {
            if (_model.RichSuggestions.Count > 0)
            {
                _model.MoveSuggestion(-1);
            }
            else
            {
                _model.HistoryPrevious();
            }

            _editor.ResetBlink();
        }

        if (_editor.Down.Poll(keyboard, KeyCode.Down, elapsed))
        {
            if (_model.SuggestionIndex >= 0)
            {
                _model.MoveSuggestion(1);
            }
            else
            {
                _model.HistoryNext();
            }

            _editor.ResetBlink();
        }

        int page = Math.Max(1, VisibleLines - 2);

        if (_editor.PageUp.Poll(keyboard, KeyCode.PageUp, elapsed))
        {
            _model.Scroll(page, VisibleLines);
        }

        if (_editor.PageDown.Poll(keyboard, KeyCode.PageDown, elapsed))
        {
            _model.Scroll(-page, VisibleLines);
        }

        if (_editor.EditPrompt(_model, keyboard, elapsed, control, shift))
        {
            _editor.ResetBlink();
        }
    }
}
