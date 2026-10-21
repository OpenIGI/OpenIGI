using OpenIGI.Core.Configuration;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Host.DevTools;

/// <summary>
/// The developer console's panels: the windows, the bodies inside them, and the handful of things that
/// open, close and pin them.
/// </summary>
/// <remarks>
/// <para>
/// Shared by the console's drawing, its pointer, its keys and its commands, which all need to ask the
/// same questions — is the variable browser showing, which window is the palette — of the same set.
/// Holding the set in one place is what lets each of those live in its own class without four copies
/// of the window fields drifting apart.
/// </para>
/// <para>
/// Everything here is empty until <see cref="Build"/> runs, which happens only once the display
/// exists. The commands are registered long before that, so the requests that arrive early —
/// <see cref="PendingOpen"/> and <see cref="PendingSelection"/> — are held here and applied later.
/// </para>
/// </remarks>
internal sealed class DevConsolePanels
{
    /// <summary>The console's own panel's key.</summary>
    public const string ConsolePanel = "console";

    /// <summary>The variable browser's key.</summary>
    public const string CVarPanel = "cvars";

    /// <summary>The watch panel's key.</summary>
    public const string WatchPanel = "watch";

    /// <summary>The command palette's key.</summary>
    public const string PalettePanel = "palette";

    /// <summary>The world inspector's key.</summary>
    public const string InspectorPanel = "world";

    /// <summary>Every panel's key, known before any of them has been built.</summary>
    public static readonly string[] KnownPanels = [ConsolePanel, CVarPanel, WatchPanel, InspectorPanel, PalettePanel];

    private readonly ConsoleModel _model;
    private readonly PromptEditor _editor;

    /// <summary>Initialises a new instance of the <see cref="DevConsolePanels"/> class.</summary>
    /// <param name="model">The console state the panels show and edit.</param>
    /// <param name="editor">The typing shared by every panel, whose caret a panel opening resets.</param>
    /// <param name="watch">The pinned values the watch panel shows.</param>
    public DevConsolePanels(ConsoleModel model, PromptEditor editor, ConsoleWatchList watch)
    {
        _model = model;
        _editor = editor;
        WatchList = watch;
    }

    /// <summary>Gets the console state the panels show and edit.</summary>
    public ConsoleModel Model => _model;

    /// <summary>Gets the pinned values shown by the watch panel.</summary>
    public ConsoleWatchList WatchList { get; }

    /// <summary>Gets every built panel by key.</summary>
    public Dictionary<string, UiWindow> Windows { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the panels asked for before there were any, opened once they are built.</summary>
    public HashSet<string> PendingOpen { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets an <c>ent_select</c> that arrived before the panels existed, applied once they do.</summary>
    public int? PendingSelection { get; set; }

    /// <summary>Gets or sets the world the inspector lists.</summary>
    public IWorldInspector? World { get; set; }

    /// <summary>Gets the palette and faces, or <see langword="null"/> before the panels are built.</summary>
    public DevConsoleTheme? Theme { get; private set; }

    /// <summary>Gets the layer the windows float in, or <see langword="null"/> before the panels are built.</summary>
    public WindowLayer? Layer { get; private set; }

    /// <summary>Gets or sets a value indicating whether the stored layout has been applied yet.</summary>
    public bool LayoutRestored { get; set; }

    /// <summary>Gets or sets a value indicating whether the scrollback's find box has the keyboard.</summary>
    public bool FindFocused { get; set; }

    /// <summary>Gets or sets a value indicating whether the watch panel still sizes itself to what is pinned.</summary>
    public bool WatchAutoHeight { get; set; } = true;

    /// <summary>Gets the console's own window.</summary>
    public UiWindow? ConsoleWindow { get; private set; }

    /// <summary>Gets the variable browser's window.</summary>
    public UiWindow? CVarWindow { get; private set; }

    /// <summary>Gets the watch panel's window.</summary>
    public UiWindow? WatchWindow { get; private set; }

    /// <summary>Gets the command palette's window.</summary>
    public UiWindow? PaletteWindow { get; private set; }

    /// <summary>Gets the world inspector's window.</summary>
    public UiWindow? InspectorWindow { get; private set; }

    /// <summary>Gets the console's scrollback and prompt.</summary>
    public ConsoleBody? Body { get; private set; }

    /// <summary>Gets the variable browser.</summary>
    public CVarBrowserBody? CVars { get; private set; }

    /// <summary>Gets the watch panel's contents.</summary>
    public WatchBody? Watch { get; private set; }

    /// <summary>Gets the command palette.</summary>
    public CommandPaletteBody? Palette { get; private set; }

    /// <summary>Gets the world inspector.</summary>
    public InspectorBody? Inspector { get; private set; }

    /// <summary>Gets the window in front, which is the one that receives typing.</summary>
    public UiWindow? Focused => Layer?.Focused;

    /// <summary>Gets how many scrollback lines are on screen, for page-up and page-down.</summary>
    public int VisibleLines => Body?.VisibleLines ?? 20;

    /// <summary>Decides whether a name is one of the panels', built or not.</summary>
    /// <param name="name">The name to test.</param>
    /// <returns><see langword="true"/> when it names a panel.</returns>
    public static bool IsPanelName(string name) =>
        Array.Exists(KnownPanels, panel => panel.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Builds every panel.</summary>
    /// <param name="console">The console the panels read from and act on.</param>
    /// <param name="theme">The palette and faces.</param>
    /// <param name="surfaces">The generated sprites the chrome draws with.</param>
    /// <param name="layer">The layer the windows float in.</param>
    public void Build(ConsoleSystem console, DevConsoleTheme theme, UiSurfaces surfaces, WindowLayer layer)
    {
        Theme = theme;
        Layer = layer;

        Body = new ConsoleBody(_model, theme) { Surfaces = surfaces };
        ConsoleWindow = Panel(ConsolePanel, "Console", new UiRect(40f, 40f, 1180f, 600f), Body, new UiVec2(420f, 200f), surfaces);

        CVars = new CVarBrowserBody(console.Variables, theme) { Surfaces = surfaces };
        CVarWindow = Panel(CVarPanel, "Variables", new UiRect(1240f, 40f, 620f, 720f), CVars, new UiVec2(460f, 240f), surfaces);
        CVarWindow.IsOpen = false;

        Watch = new WatchBody(WatchList, console.Variables, theme);
        WatchWindow = Panel(WatchPanel, "Watch", new UiRect(40f, 660f, 420f, 150f), Watch, new UiVec2(240f, 90f), surfaces);
        WatchWindow.IsOpen = false;

        Inspector = new InspectorBody(theme) { Surfaces = surfaces };
        InspectorWindow = Panel(InspectorPanel, "World", new UiRect(1040f, 660f, 640f, 340f), Inspector, new UiVec2(380f, 180f), surfaces);
        InspectorWindow.IsOpen = false;
        Inspector.Attach(World);

        if (PendingSelection is { } pending)
        {
            PendingSelection = null;
            Inspector.SelectWhenListed(pending);
        }

        Palette = new CommandPaletteBody(console, theme) { Surfaces = surfaces };
        PaletteWindow = Panel(PalettePanel, "Command palette", new UiRect(520f, 180f, 900f, 460f), Palette, new UiVec2(420f, 200f), surfaces);
        PaletteWindow.IsOpen = false;
        PaletteWindow.IsResizable = false;
    }

    /// <summary>Opens a pinnable panel and pins it, when it has been built.</summary>
    /// <param name="key">Which panel.</param>
    /// <param name="pinned">The variable that keeps it up with the console shut.</param>
    public void ShowPinned(string key, BoolCVar pinned)
    {
        if (Windows.TryGetValue(key, out UiWindow? window))
        {
            window.IsOpen = true;
            pinned.Value = true;
        }
    }

    /// <summary>Opens the command palette over everything, with a fresh query.</summary>
    public void OpenPalette()
    {
        if (PaletteWindow is null || Palette is null)
        {
            return;
        }

        _model.IsOpen = true;
        Palette.Reset();
        PaletteWindow.IsOpen = true;
        Layer?.Raise(PaletteWindow);
        _editor.ResetBlink();
    }

    /// <summary>Closes the command palette and hands the keyboard back to the console.</summary>
    public void ClosePalette()
    {
        if (PaletteWindow is not null)
        {
            PaletteWindow.IsOpen = false;
        }

        if (ConsoleWindow is not null)
        {
            Layer?.Raise(ConsoleWindow);
        }
    }

    /// <summary>Closes a panel and raises the console's own window in its place.</summary>
    /// <param name="window">The panel to close.</param>
    public void CloseToConsole(UiWindow window)
    {
        window.IsOpen = false;
        if (ConsoleWindow is not null)
        {
            Layer?.Raise(ConsoleWindow);
        }
    }

    /// <summary>Acts on whatever the palette has selected, then closes it.</summary>
    public void RunPaletteSelection()
    {
        if (Palette?.Selected is not { } entry)
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

        _editor.ResetBlink();
    }

    private UiWindow Panel(string key, string title, UiRect frame, UiElement content, UiVec2 minSize, UiSurfaces surfaces)
    {
        var window = new UiWindow(Theme!.Ui, title)
        {
            Frame = frame,
            MinSize = minSize,
            Surfaces = surfaces,
            Content = content,
        };

        window.CloseRequested += (_, _) => ClosePanel(key);
        Layer!.Add(window);
        Windows[key] = window;
        return window;
    }

    private void ClosePanel(string key)
    {
        if (string.Equals(key, ConsolePanel, StringComparison.OrdinalIgnoreCase))
        {
            _model.IsOpen = false;
            return;
        }

        if (Windows.TryGetValue(key, out UiWindow? window))
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
}
