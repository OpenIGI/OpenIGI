using OpenIGI.Core.Diagnostics;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Menus;

/// <summary>
/// The manager's per-tick behaviour: input, selection, navigation and drawing.
/// </summary>
/// <remarks>
/// <para>
/// This is the class's update and render methods — <c>MenuManager_Update</c> (<c>0x418080</c>) and
/// <c>MenuManager_Render</c> (<c>0x418970</c>) — so it runs off the task tree's walk rather than from a
/// loop in the shell.
/// </para>
/// <para>
/// The manager is created by the menu script before it is initialised, so everything here tolerates an
/// uninitialised manager and does nothing until <see cref="Initialize"/> has run.
/// </para>
/// </remarks>
public partial class MenuManagerTask
{
    private const string MoveSound = "menu_high";
    private const string ChooseSound = "menu_sel";

    private readonly MenuPointer _pointer = new();
    private IMenuHost? _host;
    private MenuSystem? _system;
    private ScriptExpressionEvaluator? _evaluator;
    private MenuScreenTask? _current;
    private IIgiLogger _logger = NullLogger.Instance;

    /// <summary>Gets the screen stack and fade this manager drives.</summary>
    public MenuNavigator Navigator { get; } = new();

    /// <summary>Gets the selection the pointer and the keys share.</summary>
    public MenuSelection Selection { get; } = new();

    /// <summary>Gets the script namespace menu actions are evaluated against.</summary>
    public MenuScriptContext? ScriptContext { get; private set; }

    /// <summary>Gets the screen currently being shown.</summary>
    public MenuScreenTask? CurrentScreen => _current;

    /// <summary>Wires the manager to its shell and starts it on the screen the script named.</summary>
    /// <param name="system">The loaded menu, used to resolve screen ids.</param>
    /// <param name="host">The shell providing input, presentation and sound.</param>
    /// <param name="logger">Where to report menu activity.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public void Initialize(MenuSystem system, IMenuHost host, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(host);

        _system = system;
        _host = host;
        _logger = logger ?? NullLogger.Instance;

        ScriptContext = new MenuScriptContext(Navigator, _logger);
        _evaluator = new ScriptExpressionEvaluator(ScriptContext);

        Selection.Moved += (_, _) => _host?.PlaySound(MoveSound);
        Navigator.Start(CurrentMenuScreen);
        EnterScreen(system.FindScreen(CurrentMenuScreen));
    }

    /// <inheritdoc/>
    protected override void OnUpdate()
    {
        base.OnUpdate();

        if (_host is null || _system is null)
        {
            return;
        }

        // Advance the fade first: the screen changes at the bottom of it.
        if (Navigator.Tick())
        {
            EnterScreen(_system.FindScreen(Navigator.CurrentScreen));
        }

        if (Navigator.IsLeaving)
        {
            _host.LeaveMenus(Navigator.State);
            return;
        }

        if (_current is null || Navigator.State != MenuManagerState.Active || ScriptContext?.IsEnabled != true)
        {
            return;
        }

        HandleKeys();
        HandlePointer();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The original draws the pointer, then the popup, then the screen, setting the draw alpha before
    /// each. Only the screen is drawn here; the pointer sprite and popup layering follow the controls.
    /// </remarks>
    protected override void OnRender()
    {
        base.OnRender();

        if (_current is not null)
        {
            _host?.DrawScreen(_current);
        }
    }

    /// <summary>Runs one menu action expression.</summary>
    /// <param name="source">The expression source, which may be empty.</param>
    /// <remarks>Reports a failure to the log and carries on, as the original does.</remarks>
    public void RunAction(string source)
    {
        if (_evaluator is null || string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        _evaluator.Evaluate(ScriptExpressionParser.Parse(source, ResolveConstant));
        if (_evaluator.HasError)
        {
            _logger.Warning("Menu", $"{source}: {_evaluator.ErrorMessage}");
        }
    }

    /// <summary>Resolves the constants a menu expression may name.</summary>
    /// <param name="name">The identifier.</param>
    /// <returns>The value, or <see langword="null"/> when it is not a constant.</returns>
    public static int? ResolveConstant(string name)
    {
        foreach ((string colour, int value) in MenuTaskTypes.TextColours)
        {
            if (string.Equals(colour, name, StringComparison.Ordinal))
            {
                return value;
            }
        }

        return name switch
        {
            "TRUE" => 1,
            "FALSE" => 0,
            "MENUMANAGER_IDLE" => (int)MenuManagerState.Idle,
            "MENUMANAGER_QUIT" => (int)MenuManagerState.Quit,
            "MENUMANAGER_QUITTOMAINMENU" => (int)MenuManagerState.QuitToMainMenu,
            "MENUMANAGER_RESTARTLEVEL" => (int)MenuManagerState.RestartLevel,
            "MENUMANAGER_STARTGAME" => (int)MenuManagerState.StartGame,
            _ => null,
        };
    }

    private void EnterScreen(MenuScreenTask? screen)
    {
        if (screen is null || ReferenceEquals(screen, _current))
        {
            return;
        }

        _current = screen;
        _host?.PrepareScreen(screen);
        screen.Arrange();
        Selection.Rebuild(screen);

        RunAction(screen.OnInitData);
    }

    private void HandleKeys()
    {
        foreach (MenuInputEvent raw in _host!.DrainEvents())
        {
            // A popup that asks for it swaps the two direction pairs on the way in.
            MenuInputEvent input = Navigator.PopupActive && SwapKeysWanted() ? MenuSelection.SwapKeys(raw) : raw;

            switch (input)
            {
                case MenuInputEvent.Escape:
                    RunAction(_current!.OnPressEscape);
                    break;

                case MenuInputEvent.Enter:
                    Choose(Selection.Selected);
                    break;

                default:
                    Selection.Handle(input);
                    break;
            }
        }
    }

    private void HandlePointer()
    {
        if (!_host!.TryGetPointer(out int x, out int y, out bool buttonDown))
        {
            return;
        }

        _pointer.Set(x, y, buttonDown);

        MenuWindowTask? clicked = _current!.HitTestSubtree(_pointer);

        // A fresh hover moves the shared selection, which is what lights the entry.
        foreach (MenuWindowTask item in Selection.Items)
        {
            if (item.IsActive)
            {
                Selection.SelectItem(item);
                break;
            }
        }

        if (clicked is not null)
        {
            Choose(clicked);
        }
    }

    private void Choose(MenuWindowTask? item)
    {
        if (item is not MenuTextTask entry || entry.OnClickScript.Length == 0)
        {
            return;
        }

        _host?.PlaySound(entry.ClickSoundResource.Length > 0 ? entry.ClickSoundResource : ChooseSound);
        _logger.Information("Menu", $"'{entry.TextResource}' -> {entry.OnClickScript}");
        RunAction(entry.OnClickScript);
    }

    private bool SwapKeysWanted() =>
        _system?.FindPopup(Navigator.PopupScreen) is { SwapKeys: true };
}
