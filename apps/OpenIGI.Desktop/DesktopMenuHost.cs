using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Flow;
using OpenIGI.Game.Menus;
using OpenIGI.Input;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>
/// The shell's side of both seams: the flow's host, and the menu manager's.
/// </summary>
/// <remarks>
/// <para>
/// The flow state machine owns when the menu exists — <see cref="ShowMainMenu"/> is what
/// <see cref="FlowEvent.MainMenu"/> calls — and the menu manager, being a task under the flow, is
/// updated and rendered by the frame walk. The shell only supplies input and drawing.
/// </para>
/// <para>
/// The states this cannot serve yet report through the log rather than pretending: there are no intro
/// screens, no outro and no game to begin.
/// </para>
/// </remarks>
public sealed class DesktopMenuHost : IFlowHost, IMenuHost
{
    private const string LogCategory = "Menu";

    private readonly List<MenuInputEvent> _events = [];
    private readonly IIgiLogger _logger;
    private readonly InputState _input;
    private readonly SpriteBatch _batch;
    private readonly Func<MenuRenderer?> _renderer;
    private readonly Func<int> _screenWidth;
    private readonly Func<int> _screenHeight;
    private readonly Action _quit;

    /// <summary>Initializes a new instance of the <see cref="DesktopMenuHost"/> class.</summary>
    /// <param name="input">The window's input state.</param>
    /// <param name="batch">The batch menu drawing goes through.</param>
    /// <param name="renderer">Supplies the renderer once the menu archive is known.</param>
    /// <param name="screenWidth">Reads the current back-buffer width.</param>
    /// <param name="screenHeight">Reads the current back-buffer height.</param>
    /// <param name="quit">Called when the menu asks to leave.</param>
    /// <param name="logger">Where to report menu activity.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public DesktopMenuHost(
        InputState input,
        SpriteBatch batch,
        Func<MenuRenderer?> renderer,
        Func<int> screenWidth,
        Func<int> screenHeight,
        Action quit,
        IIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(screenWidth);
        ArgumentNullException.ThrowIfNull(screenHeight);
        ArgumentNullException.ThrowIfNull(quit);
        ArgumentNullException.ThrowIfNull(logger);

        _input = input;
        _batch = batch;
        _renderer = renderer;
        _screenWidth = screenWidth;
        _screenHeight = screenHeight;
        _quit = quit;
        _logger = logger;
    }

    /// <summary>Gets or sets what <see cref="ShowMainMenu"/> should do, set by the composition root.</summary>
    public Action? LoadMainMenu { get; set; }

    /// <inheritdoc/>
    public void ShowMainMenu()
    {
        _logger.Information(LogCategory, "Flow entered the main menu.");
        LoadMainMenu?.Invoke();
    }

    /// <inheritdoc/>
    public void ShowIntroScreens() => NotYet(nameof(ShowIntroScreens));

    /// <inheritdoc/>
    public void ShowOutroScreens() => NotYet(nameof(ShowOutroScreens));

    /// <inheritdoc/>
    public void ShowAttractScreens() => NotYet(nameof(ShowAttractScreens));

    /// <inheritdoc/>
    public void BeginGame(FlowTask flow) => NotYet(nameof(BeginGame));

    /// <inheritdoc/>
    public void LoadLevel() => NotYet(nameof(LoadLevel));

    /// <inheritdoc/>
    public void EndGame() => NotYet(nameof(EndGame));

    /// <inheritdoc/>
    public bool TryGetPointer(out int x, out int y, out bool buttonDown)
    {
        (int authoredX, int authoredY) = ToAuthored((int)_input.Mouse.X, (int)_input.Mouse.Y);
        x = authoredX;
        y = authoredY;
        buttonDown = _input.Mouse.IsDown(MouseButton.Left);
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Drains the keyboard's press <em>queue</em> and maps each press onto the abstract code the
    /// manager consumes, which is what <c>MenuManager_HandleKeys</c> (<c>0x418580</c>) does with
    /// <c>Input_HasEvent</c>/<c>Input_NextEvent</c>.
    /// </para>
    /// <para>
    /// It has to be a queue rather than a per-frame edge test. The window dispatches key events at the
    /// display's rate and the manager reads them from a task update at the simulation's 30 Hz, so a
    /// press that begins and ends between two ticks — or simply one whose frame is not a tick frame —
    /// has its edge overwritten by the next frame's shadow before any tick can see it. That is what
    /// made the arrow keys and enter appear dead.
    /// </para>
    /// </remarks>
    public IEnumerable<MenuInputEvent> DrainEvents()
    {
        _events.Clear();

        while (_input.Keyboard.TryDequeuePress(out KeyCode key))
        {
            if (Map(key) is { } mapped)
            {
                _events.Add(mapped);
            }
        }

        return _events;
    }

    /// <summary>Maps a key onto the manager's abstract code.</summary>
    /// <param name="key">The key that was pressed.</param>
    /// <returns>The code, or <see langword="null"/> for a key the menu does not use.</returns>
    private static MenuInputEvent? Map(KeyCode key) => key switch
    {
        KeyCode.Down or KeyCode.Numpad2 => MenuInputEvent.Next,
        KeyCode.Up or KeyCode.Numpad8 => MenuInputEvent.Previous,
        KeyCode.Left or KeyCode.Numpad4 => MenuInputEvent.Left,
        KeyCode.Right or KeyCode.Numpad6 => MenuInputEvent.Right,
        KeyCode.Return or KeyCode.NumpadEnter => MenuInputEvent.Enter,
        KeyCode.Escape => MenuInputEvent.Escape,
        _ => null,
    };

    /// <inheritdoc/>
    public void PrepareScreen(MenuScreenTask screen) => _renderer()?.Prepare(screen);

    /// <inheritdoc/>
    public void DrawScreen(MenuScreenTask screen) =>
        _renderer()?.Draw(_batch, screen, _screenWidth(), _screenHeight());

    /// <inheritdoc/>
    public void DrawPointer(int x, int y) =>
        _renderer()?.DrawPointer(_batch, x, y, _screenWidth(), _screenHeight());

    /// <inheritdoc/>
    /// <remarks>Audio is M7; the request is logged so the wiring is visible before there is a mixer.</remarks>
    public void PlaySound(string name) => _logger.Debug(LogCategory, $"sound '{name}' (audio is M7)");

    /// <inheritdoc/>
    public void LeaveMenus(MenuManagerState state)
    {
        _logger.Information(LogCategory, $"Leaving the menus: {state}.");
        _quit();
    }

    private static (int X, int Y) ToAuthoredCore(int x, int y, int width, int height) =>
        (x + (MenuLayout.AuthoredWidth / 2) - (width >> 1),
         y + (MenuLayout.AuthoredHeight / 2) - (height >> 1));

    private (int X, int Y) ToAuthored(int x, int y) => ToAuthoredCore(x, y, _screenWidth(), _screenHeight());

    private void NotYet(string what) =>
        _logger.Information(LogCategory, $"Flow asked for {what}, which is not implemented yet.");
}
