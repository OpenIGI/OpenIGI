using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Presets;

namespace OpenIGI.Desktop;

/// <summary>
/// The desktop shell's console: the registry, the command set, the settings file and the on-screen view.
/// </summary>
/// <remarks>
/// <para>
/// Composed once at start-up. The order matters and is the order the plan calls for: declarations are
/// forced to register, then <c>settings.cfg</c> is applied over the defaults, then the command line's
/// <c>+</c> statements are applied over that. So a command line always wins for the session, and what
/// it put in place is what gets saved — which is what a player switching to <c>+gfx_preset ultra</c>
/// once and then never passing it again expects.
/// </para>
/// <para>
/// Everything the game writes through <see cref="IIgiLogger"/> is mirrored into the scrollback, so
/// opening the console shows the log rather than an empty panel.
/// </para>
/// </remarks>
public sealed class DesktopConsole : IDisposable
{
    private const string LogCategory = "Console";

    private readonly IIgiLogger _logger;
    private readonly string _settingsPath;
    private readonly ConsoleRenderer _renderer;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="DesktopConsole"/> class.</summary>
    /// <param name="device">The device the console's font is uploaded to.</param>
    /// <param name="logger">Where to report what the console does.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public DesktopConsole(IGraphicsDevice device, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);

        _logger = logger ?? NullLogger.Instance;
        _settingsPath = SettingsPaths.GetSettingsPath();

        System = new ConsoleSystem();
        Model = new ConsoleModel(System);
        Input = new ConsoleInputController(Model);
        _renderer = new ConsoleRenderer(device, _logger);

        string configDirectory = Path.GetDirectoryName(_settingsPath) ?? SettingsPaths.PortableDirectory;
        BuiltInConsoleCommands.Register(System, new DirectoryConsoleFileSystem(configDirectory));
        GraphicsPresetCommands.Register(System);
        RegisterShellCommands();
    }

    /// <summary>Occurs when a console command has asked the game to quit.</summary>
    public event EventHandler? QuitRequested;

    /// <summary>Gets the console the registry and commands live on.</summary>
    public ConsoleSystem System { get; }

    /// <summary>Gets the on-screen console's state.</summary>
    public ConsoleModel Model { get; }

    /// <summary>Gets the keyboard-to-editing translator.</summary>
    public ConsoleInputController Input { get; }

    /// <summary>Gets a value indicating whether the console is showing and consuming input.</summary>
    public bool IsOpen => Model.IsOpen;

    /// <summary>Gets the full path of the settings file this console reads and writes.</summary>
    public string SettingsPath => _settingsPath;

    /// <summary>
    /// Brings the console up: registers every declaration, applies the settings file, then the command line.
    /// </summary>
    /// <param name="fileSystem">Where the console's font is read from.</param>
    /// <param name="startupCommands">The <c>+</c> statements from the command line.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fileSystem"/> is <see langword="null"/>.</exception>
    public void Start(VirtualFileSystem fileSystem, IReadOnlyList<string>? startupCommands = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        // Static field initialisers only run when something touches the declaring class, so the
        // generated registrations are what make the registry complete before settings are read.
        Rendering.Generated.CVarRegistrations.RegisterAll();

        if (SettingsFile.Load(System, _settingsPath))
        {
            _logger.Information(LogCategory, $"Loaded settings from {_settingsPath}.");
        }

        GraphicsPresetRegistry.Default.Detect();

        foreach (string statement in startupCommands ?? [])
        {
            System.Execute(statement);
        }

        if (!_renderer.Load(fileSystem))
        {
            _logger.Warning(LogCategory, "The console has no font; it will accept commands but draw nothing.");
        }

        System.Write($"OpenIGI console. {System.Variables.Count} variable(s), {System.Commands.All.Count} command(s).");
        System.Write("Type 'help' for commands, 'cvarlist' for settings, or press Tab to complete.");
    }

    /// <summary>Mirrors engine log output into the console's scrollback.</summary>
    /// <param name="inner">The logger to wrap.</param>
    /// <returns>A logger that writes to both.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    public IIgiLogger WrapLogger(IIgiLogger inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return new ConsoleMirrorLogger(inner, System);
    }

    /// <summary>Applies a frame's input.</summary>
    /// <param name="input">The input state for this frame.</param>
    /// <param name="elapsedSeconds">How long since the last call, for the caret blink.</param>
    /// <returns><see langword="true"/> when the console consumed the input and the game should ignore it.</returns>
    public bool Update(OpenIGI.Input.InputState input, float elapsedSeconds)
    {
        Input.Advance(elapsedSeconds);
        return Input.Update(input, _renderer.VisibleLines);
    }

    /// <summary>Draws the console, if it is open.</summary>
    /// <param name="batch">The batch to draw into; it must already be between Begin and End.</param>
    /// <param name="screenWidth">The back buffer's width.</param>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <param name="scale">How much to magnify the text.</param>
    public void Draw(SpriteBatch batch, int screenWidth, int screenHeight, float scale = 1f) =>
        _renderer.Draw(batch, Model, screenWidth, screenHeight, scale, Input.CaretVisible);

    /// <summary>Writes the settings file.</summary>
    /// <returns><see langword="true"/> when it was written.</returns>
    public bool SaveSettings()
    {
        bool saved = SettingsFile.Save(System, _settingsPath);
        if (!saved)
        {
            _logger.Warning(LogCategory, $"Could not write {_settingsPath}; settings were not saved.");
        }

        return saved;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _renderer.Dispose();
    }

    private void RegisterShellCommands()
    {
        System.Commands.Add(
            "quit",
            "Closes the game.",
            _ => QuitRequested?.Invoke(this, EventArgs.Empty));

        System.Commands.Add(
            "clear",
            "Empties the console's scrollback.",
            _ => Model.ClearScrollback());

        // Bindable, and usable from the command line as +toggleconsole -- which is how a headless
        // screenshot run captures the console itself.
        System.Commands.Add(
            "toggleconsole",
            "Shows or hides the console.",
            _ => Model.IsOpen = !Model.IsOpen);

        System.Commands.Add(
            "writeconfig",
            "Writes settings.cfg now instead of waiting for shutdown.",
            context => context.Print(SaveSettings() ? $"Wrote {_settingsPath}." : "Could not write the settings file."));

        System.Commands.Add(
            "cheats",
            "Reports or sets whether cheat commands and variables may be used.",
            context =>
            {
                if (context.Count > 0)
                {
                    System.CheatsEnabled = context[0] is "1" or "on" or "true" or "yes";
                }

                context.Print($"cheats {(System.CheatsEnabled ? "enabled" : "disabled")}");
            },
            "[0 | 1]");
    }

    /// <summary>Sends log output to the engine's logger and to the console's scrollback.</summary>
    /// <param name="inner">The logger to forward to.</param>
    /// <param name="console">The console to mirror into.</param>
    private sealed class ConsoleMirrorLogger(IIgiLogger inner, ConsoleSystem console) : IIgiLogger
    {
        public bool IsEnabled(LogLevel level) => inner.IsEnabled(level);

        public void Log(LogLevel level, string category, string message, Exception? exception = null)
        {
            inner.Log(level, category, message, exception);

            if (level < LogLevel.Information)
            {
                return;
            }

            ConsoleSeverity severity = level switch
            {
                LogLevel.Warning => ConsoleSeverity.Warning,
                LogLevel.Error or LogLevel.Fatal => ConsoleSeverity.Error,
                _ => ConsoleSeverity.Output,
            };

            console.Write(new ConsoleLine($"[{category}] {message}", severity));
        }
    }
}
