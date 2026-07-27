using OpenIGI.Core.Diagnostics;
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

        MenuSystem menu = new(tree, types, natives, manager);
        log.Information(
            "Menu",
            $"Loaded {menu.Screens.Count} screen(s) from '{manager.ResourcePath}', starting on {manager.CurrentMenuScreen}.");

        return menu;
    }

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
