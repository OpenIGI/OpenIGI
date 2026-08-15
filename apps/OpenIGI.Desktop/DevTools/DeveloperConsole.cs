using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Input;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Desktop;

/// <summary>
/// The developer console's on-screen half: a set of floating panels, the input that drives them, and
/// the drawing.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the fixed half-screen drop-down the console used to be. The panels are ordinary
/// <see cref="UiWindow"/>s in a <see cref="WindowLayer"/>, so dragging, resizing, snapping and z-order
/// are the framework's problem rather than this class's, and the variable browser and the watch list
/// are the same widget with different contents.
/// </para>
/// <para>
/// <b>It builds its own layout context.</b> The shell authors against a 1920×1080 reference so a menu
/// keeps its proportions on any display; a developer tool wants the opposite — constant physical size,
/// so the same monitor shows the same amount of text whatever the aspect ratio. Passing the real
/// viewport as the reference makes <see cref="LayoutContext.Scale"/> equal to
/// <c>con_scale</c> alone, which means every <c>Px()</c> in the widgets below becomes pixels without
/// any of them needing to know.
/// </para>
/// <para>
/// <b>Input is read before it is captured.</b> The mouse's buttons, motion and wheel all read as idle
/// once <see cref="InputState.Captured"/> is set, while its position deliberately keeps working. So
/// everything is sampled first and the capture is the last thing that happens.
/// </para>
/// </remarks>
public sealed class DeveloperConsole : IDisposable
{
    private const float BlinkPeriod = 0.53f;

    private readonly IGraphicsDevice _device;
    private readonly ConsoleModel _model;
    private readonly IIgiLogger _logger;

    private readonly KeyRepeater _backspace = new();
    private readonly KeyRepeater _delete = new();
    private readonly KeyRepeater _left = new();
    private readonly KeyRepeater _right = new();
    private readonly KeyRepeater _up = new();
    private readonly KeyRepeater _down = new();
    private readonly KeyRepeater _pageUp = new();
    private readonly KeyRepeater _pageDown = new();

    private UiSurfaces? _surfaces;
    private OpenIGI.UI.Text.UiFont? _monoFont;
    private DevConsoleTheme? _theme;
    private WindowLayer? _layer;
    private UiWindow? _consoleWindow;
    private ConsoleBody? _body;

    private float _blink;
    private bool _caretVisible = true;
    private bool _disposed;
    private bool _layoutRestored;
    private int _viewportWidth;
    private int _viewportHeight;

    /// <summary>Initialises a new instance of the <see cref="DeveloperConsole"/> class.</summary>
    /// <param name="device">The device fonts and generated sprites are uploaded to.</param>
    /// <param name="model">The console state shown and edited.</param>
    /// <param name="logger">Where to report what the console does.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public DeveloperConsole(IGraphicsDevice device, ConsoleModel model, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(model);

        _device = device;
        _model = model;
        _logger = logger ?? NullLogger.Instance;
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
    /// than being pushed once when the console opens — which the next frame would silently undo.
    /// </remarks>
    public bool WantsPointer => _model.IsOpen;

    /// <summary>Gets a value indicating whether anything is drawn while the console itself is shut.</summary>
    public static bool HasVisibleOverlays =>
        DevConsoleCVars.WatchVisible.Value || DevConsoleCVars.StatsVisible.Value;

    /// <summary>Gets how many scrollback lines are on screen, for page-up and page-down.</summary>
    public int VisibleLines => _body?.VisibleLines ?? 20;

    /// <summary>Builds the fonts, the generated sprites and the panels.</summary>
    /// <returns><see langword="true"/> when everything loaded.</returns>
    /// <remarks>Failure is reported and swallowed: a missing font must cost the console, never the game.</remarks>
    public bool Load()
    {
        if (_theme is not null)
        {
            return true;
        }

        try
        {
            _monoFont = UiFontLibrary.Load(_device, UiFontLibrary.Mono, UiFontLibrary.MonoPixelHeight);
            _surfaces = UiSurfaces.Create(_device);
            _theme = new DevConsoleTheme(_monoFont, DevConsoleCVars.Palette.Value);

            _layer = new WindowLayer { SnapDistance = DevConsoleCVars.Snap.Value };
            _body = new ConsoleBody(_model, _theme) { Surfaces = _surfaces.Rounded };

            _consoleWindow = new UiWindow(_theme.Ui, "Console")
            {
                Frame = new UiRect(60f, 60f, 1180f, 620f),
                MinSize = new UiVec2(420f, 200f),
                Surfaces = _surfaces,
                Content = _body,
            };

            _consoleWindow.CloseRequested += (_, _) => _model.IsOpen = false;
            _layer.Add(_consoleWindow);
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

        // Position, buttons and wheel are all read here, before the capture below turns every one of
        // them except the position to zero for the rest of the frame.
        HandlePointer(input);
        HandleKeys(input.Keyboard, elapsedSeconds);
        return true;
    }

    /// <summary>Draws the console and its panels.</summary>
    /// <param name="batch">An open sprite batch.</param>
    /// <param name="screenWidth">The back buffer's width.</param>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is <see langword="null"/>.</exception>
    public void Draw(SpriteBatch batch, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (_theme is null || _layer is null || _consoleWindow is null)
        {
            return;
        }

        LayoutContext context = BuildContext(screenWidth, screenHeight);
        RestoreLayoutOnce(context);
        SyncViewport(context);

        _consoleWindow.IsOpen = _model.IsOpen;
        if (_body is not null)
        {
            _body.CaretVisible = _caretVisible;
        }

        var painter = new UiPainter(batch, context);
        painter.PushOpacity(DevConsoleCVars.Opacity.Value);

        _layer.Measure(context, context.Viewport);
        _layer.Arrange(new UiRect(0f, 0f, context.Viewport.X, context.Viewport.Y));
        _layer.Draw(painter);

        painter.PopOpacity();
    }

    /// <summary>Stores where the panels sit, so the layout survives a restart.</summary>
    public void SaveLayout()
    {
        if (_consoleWindow is null)
        {
            return;
        }

        DevConsoleCVars.Layout.Value = DevConsoleLayout.Render(
            new Dictionary<string, UiWindow> { ["console"] = _consoleWindow });
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
        _monoFont?.Dispose();
    }

    /// <summary>
    /// Builds a layout context whose scale is the reader's alone.
    /// </summary>
    /// <param name="width">The back buffer's width.</param>
    /// <param name="height">The back buffer's height.</param>
    /// <returns>The context.</returns>
    /// <remarks>
    /// Passing the real viewport as the authored reference makes the width and height ratios both one,
    /// so <see cref="LayoutContext.Scale"/> collapses to the user scale. That is what turns every
    /// <c>Px()</c> below into a real pixel, and it is why the console does not grow on an ultrawide the
    /// way a design-unit screen correctly does.
    /// </remarks>
    private static LayoutContext BuildContext(int width, int height)
    {
        float w = MathF.Max(1f, width);
        float h = MathF.Max(1f, height);
        return new LayoutContext(w, h, reference: new UiVec2(w, h), userScale: DevConsoleCVars.Scale.Value);
    }

    private void RestoreLayoutOnce(LayoutContext context)
    {
        if (_layoutRestored || _consoleWindow is null)
        {
            return;
        }

        _layoutRestored = true;

        Dictionary<string, PanelPlacement> placements = DevConsoleLayout.Parse(DevConsoleCVars.Layout.Value);
        if (placements.TryGetValue("console", out PanelPlacement placement))
        {
            DevConsoleLayout.Apply(_consoleWindow, placement, context.Viewport);
        }
        else
        {
            // No saved layout: a wide, shallow window across the top, which is the shape a console has
            // had since Quake and the one that leaves the game visible underneath.
            float width = MathF.Min(context.Viewport.X - 80f, 1180f);
            _consoleWindow.Frame = new UiRect(40f, 40f, width, MathF.Min(context.Viewport.Y * 0.55f, 620f));
        }
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
        if (_layer is null || _body is null)
        {
            return;
        }

        MouseState mouse = input.Mouse;
        var point = new UiVec2(mouse.X, mouse.Y);

        // The completion list floats above every window, so it must be offered the press first or the
        // window beneath would take it and the reader could never click a candidate.
        if (mouse.WasPressed(MouseButton.Left) && _model.SelectSuggestion(_body.SuggestionAt(point)))
        {
            _model.AcceptSuggestion();
            ResetBlink();
            return;
        }

        _layer.UpdatePointer(mouse, new UiVec2(_viewportWidth, _viewportHeight));

        if (mouse.WheelDelta != 0f && _body.Log.Bounds.Contains(point))
        {
            _model.Scroll((int)(mouse.WheelDelta * 3f), VisibleLines);
        }
    }

    private void HandleKeys(KeyboardState keyboard, float elapsed)
    {
        if (keyboard.WasPressed(KeyCode.Escape))
        {
            _model.IsOpen = false;
            return;
        }

        bool control = keyboard.IsDown(KeyCode.LeftControl) || keyboard.IsDown(KeyCode.RightControl);
        bool shift = keyboard.IsDown(KeyCode.LeftShift) || keyboard.IsDown(KeyCode.RightShift);

        if (control && HandleControlKeys(keyboard))
        {
            ResetBlink();
            return;
        }

        HandleEditingKeys(keyboard, elapsed, control, shift);
        HandleText(keyboard);
    }

    private bool HandleControlKeys(KeyboardState keyboard)
    {
        if (keyboard.WasPressed(KeyCode.A))
        {
            _model.SelectAll();
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

    private void HandleEditingKeys(KeyboardState keyboard, float elapsed, bool control, bool shift)
    {
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

        if (_backspace.Poll(keyboard, KeyCode.Backspace, elapsed))
        {
            _model.Backspace();
            ResetBlink();
        }

        if (_delete.Poll(keyboard, KeyCode.Delete, elapsed))
        {
            _model.Delete();
            ResetBlink();
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

            ResetBlink();
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

            ResetBlink();
        }

        if (keyboard.WasPressed(KeyCode.Home))
        {
            _model.CaretToStart(shift);
            ResetBlink();
        }

        if (keyboard.WasPressed(KeyCode.End))
        {
            _model.CaretToEnd(shift);
            ResetBlink();
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
    }

    private void HandleText(KeyboardState keyboard)
    {
        while (keyboard.TryDequeueCharacter(out char typed))
        {
            // The toggle key must never reach the prompt, and the platform reports it as an ordinary
            // character before the key event is seen.
            if (typed is '`' or '~' or '\r' or '\n' or '\t' or '\b')
            {
                continue;
            }

            _model.Insert(typed.ToString());
            ResetBlink();
        }
    }
}
