using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Menus;

/// <summary>
/// Loads a menu script and exposes the task tree it builds.
/// </summary>
/// <remarks>
/// The menu is data: <c>mainmenu.qvm</c> declares its own task types and instantiates them, so
/// "loading the menu" is running the script against the task system. Nothing about the screens,
/// entries, fonts or actions is hard-coded here — everything a caller can read comes from the tree the
/// script built.
/// </remarks>
public sealed class MenuSystem
{
    private MenuSystem(TaskTree tree, TaskTypeRegistry types, NativeRegistry natives, MenuManagerTask manager)
    {
        Tree = tree;
        Types = types;
        Natives = natives;
        Manager = manager;
    }

    /// <summary>Gets the task tree the script built.</summary>
    public TaskTree Tree { get; }

    /// <summary>Gets the task types, including the ones the script declared.</summary>
    public TaskTypeRegistry Types { get; }

    /// <summary>Gets the script namespace the menu was run against.</summary>
    public NativeRegistry Natives { get; }

    /// <summary>Gets the manager the script created.</summary>
    public MenuManagerTask Manager { get; }

    /// <summary>Gets the registry the menu's named variables are bound in.</summary>
    /// <remarks>
    /// <c>Task_New</c> exports each task as it creates it, so by the time the script has run every
    /// <c>SlideBar_950.vValue</c> in it is already a live alias onto the task's field. Menu expressions
    /// read and write these.
    /// </remarks>
    public ScriptVarRegistry? Variables { get; private init; }

    /// <summary>Gets every screen the script created, in creation order.</summary>
    public IReadOnlyList<MenuScreenTask> Screens => [.. Descend(Manager).OfType<MenuScreenTask>()];

    /// <summary>Runs a compiled menu script and returns the tree it builds.</summary>
    /// <param name="script">The script bytecode.</param>
    /// <param name="logger">Where to report progress.</param>
    /// <returns>The loaded menu.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="script"/> is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">The script faulted, or built no manager.</exception>
    public static MenuSystem Load(ReadOnlyMemory<byte> script, IIgiLogger? logger = null)
    {
        IIgiLogger log = logger ?? NullLogger.Instance;

        NativeRegistry natives = new();
        TaskTypeRegistry types = new();

        EngineTaskTypes.Register(types);
        MenuTaskTypes.RegisterAll(types);
        MenuTaskTypes.RegisterConstants(natives);

        TaskTree tree = new(types);
        ScriptVarRegistry variables = new(natives);
        TaskScriptCommands commands = new(tree, variables);
        commands.Register(natives);

        natives.RegisterConstant("TRUE", 1);
        natives.RegisterConstant("FALSE", 0);

        QvmProgram program = QvmProgram.Read(script);
        QvmInterpreter interpreter = new(program, natives);

        QvmError error = interpreter.Run();
        if (error != QvmError.None)
        {
            throw new EngineFatalException(
                $"The menu script faulted with {error}: {interpreter.ErrorMessage} ({interpreter.ErrorIdentifier})");
        }

        MenuManagerTask manager = Descend(tree.Root).OfType<MenuManagerTask>().FirstOrDefault()
            ?? throw new EngineFatalException("The menu script built no MenuManager.");

        MenuSystem menu = new(tree, types, natives, manager) { Variables = variables };
        log.Information(
            "Menu",
            $"Loaded {menu.Screens.Count} screen(s) from '{manager.ResourcePath}', starting on {manager.CurrentMenuScreen}.");

        return menu;
    }

    /// <summary>Runs a menu script inside an existing game loop, so its tasks live under the flow.</summary>
    /// <param name="script">The script bytecode.</param>
    /// <param name="loop">The loop whose task tree, types and script namespace to use.</param>
    /// <param name="logger">Where to report progress.</param>
    /// <returns>The loaded menu.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">The script faulted, or built no manager.</exception>
    /// <remarks>
    /// This is the shape the original has: the menu is a subtree of the flow task, so the frame walk
    /// updates and renders it like anything else. The loop has already registered <c>Task_New</c> and
    /// <c>Task_DeclareParameters</c>, so only the menu types and constants are added here.
    /// </remarks>
    public static MenuSystem Load(ReadOnlyMemory<byte> script, GameLoop loop, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        IIgiLogger log = logger ?? NullLogger.Instance;

        MenuTaskTypes.RegisterAll(loop.Types);
        MenuTaskTypes.RegisterConstants(loop.Natives);

        if (!loop.Natives.Contains("TRUE"))
        {
            loop.Natives.RegisterConstant("TRUE", 1);
        }

        if (!loop.Natives.Contains("FALSE"))
        {
            loop.Natives.RegisterConstant("FALSE", 0);
        }

        QvmInterpreter interpreter = new(QvmProgram.Read(script), loop.Natives);
        QvmError error = interpreter.Run();
        if (error != QvmError.None)
        {
            throw new EngineFatalException(
                $"The menu script faulted with {error}: {interpreter.ErrorMessage} ({interpreter.ErrorIdentifier})");
        }

        MenuManagerTask manager = Descend(loop.Tasks.Root).OfType<MenuManagerTask>().FirstOrDefault()
            ?? throw new EngineFatalException("The menu script built no MenuManager.");

        MenuSystem menu = new(loop.Tasks, loop.Types, loop.Natives, manager) { Variables = loop.Variables };
        log.Information(
            "Menu",
            $"Loaded {menu.Screens.Count} screen(s) from '{manager.ResourcePath}', starting on {manager.CurrentMenuScreen}.");

        return menu;
    }

    /// <summary>Gets every popup the script created, in creation order.</summary>
    /// <remarks>
    /// A popup is a <c>DialogWindow</c>, not a <c>MenuScreen</c>: <c>mainmenu.qvm</c> declares twelve
    /// screens numbered 900 and 904–916, and the ids its
    /// <c>MenuManager_ActivatePopupScreen</c> calls name — 930 upward — are its six
    /// <c>DialogWindow</c> tasks.
    /// </remarks>
    public IReadOnlyList<DialogWindowTask> Popups => [.. Descend(Manager).OfType<DialogWindowTask>()];

    /// <summary>Finds any menu window by the id the script gave it.</summary>
    /// <param name="id">The script id.</param>
    /// <returns>The window, or <see langword="null"/> when nothing has that id.</returns>
    public MenuWindowTask? FindWindow(int id) =>
        Descend(Manager).OfType<MenuWindowTask>().FirstOrDefault(w => w.Id == id);

    /// <summary>Finds a popup by the id the script gave it.</summary>
    /// <param name="id">The script id, as <c>MenuManager_ActivatePopupScreen</c> names it.</param>
    /// <returns>The popup, or <see langword="null"/> when no popup has that id.</returns>
    public DialogWindowTask? FindPopup(int id) => Popups.FirstOrDefault(p => p.Id == id);

    /// <summary>Finds a screen by the id the script gave it.</summary>
    /// <param name="id">The script id, as <c>MenuManager_PushScreen</c> names it.</param>
    /// <returns>The screen, or <see langword="null"/> when no screen has that id.</returns>
    public MenuScreenTask? FindScreen(int id) =>
        Screens.FirstOrDefault(s => s.Id == id);

    /// <summary>Gets the screen the manager starts on.</summary>
    /// <returns>The screen, or <see langword="null"/> when the id does not resolve.</returns>
    public MenuScreenTask? GetInitialScreen() => FindScreen(Manager.CurrentMenuScreen);

    private static IEnumerable<GameTask> Descend(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            yield return child;
            foreach (GameTask descendant in Descend(child))
            {
                yield return descendant;
            }
        }
    }
}
