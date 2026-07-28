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
    /// Maps this frame's key presses onto the abstract codes the manager consumes. Only edges are
    /// reported, so holding a key moves the selection once.
    /// </remarks>
    public IEnumerable<MenuInputEvent> DrainEvents()
    {
        _events.Clear();
        KeyboardState keys = _input.Keyboard;

        Add(keys.WasPressed(KeyCode.Down) || keys.WasPressed(KeyCode.Numpad2), MenuInputEvent.Next);
        Add(keys.WasPressed(KeyCode.Up) || keys.WasPressed(KeyCode.Numpad8), MenuInputEvent.Previous);
        Add(keys.WasPressed(KeyCode.Left) || keys.WasPressed(KeyCode.Numpad4), MenuInputEvent.Left);
        Add(keys.WasPressed(KeyCode.Right) || keys.WasPressed(KeyCode.Numpad6), MenuInputEvent.Right);
        Add(keys.WasPressed(KeyCode.Return) || keys.WasPressed(KeyCode.NumpadEnter), MenuInputEvent.Enter);
        Add(keys.WasPressed(KeyCode.Escape), MenuInputEvent.Escape);

        return _events;
    }

    /// <inheritdoc/>
    public void PrepareScreen(MenuScreenTask screen) => _renderer()?.Prepare(screen);

    /// <inheritdoc/>
    public void DrawScreen(MenuScreenTask screen) =>
        _renderer()?.Draw(_batch, screen, _screenWidth(), _screenHeight());

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

    private void Add(bool pressed, MenuInputEvent input)
    {
        if (pressed)
        {
            _events.Add(input);
        }
    }

    private void NotYet(string what) =>
        _logger.Information(LogCategory, $"Flow asked for {what}, which is not implemented yet.");
}
