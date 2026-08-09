using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Input;
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
    private readonly ConsoleUiRenderer _renderer;
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
        _renderer = new ConsoleUiRenderer(device, System, _logger);

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
    /// <param name="useSavedSettings">
    /// Whether <c>settings.cfg</c> is read and written. A capture run passes <see langword="false"/> —
    /// see <see cref="IsolatedFromSavedSettings"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="fileSystem"/> is <see langword="null"/>.</exception>
    public void Start(
        VirtualFileSystem fileSystem,
        IReadOnlyList<string>? startupCommands = null,
        bool useSavedSettings = true)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        IsolatedFromSavedSettings = !useSavedSettings;

        // Static field initialisers only run when something touches the declaring class, so the
        // generated registrations are what make the registry complete before settings are read.
        Rendering.Generated.CVarRegistrations.RegisterAll();

        bool settingsLoaded = false;
        if (IsolatedFromSavedSettings)
        {
            _logger.Information(
                LogCategory,
                "Capture mode: starting from defaults; settings.cfg is neither read nor written.");
        }
        else if (SettingsFile.Load(System, _settingsPath))
        {
            settingsLoaded = true;
            _logger.Information(LogCategory, $"Loaded settings from {_settingsPath}.");
        }

        if (!IsolatedFromSavedSettings && !settingsLoaded)
        {
            _logger.Information(LogCategory, "No saved settings found; using the Enhanced graphics preset.");
            GraphicsPresetRegistry.Default.Apply(GraphicsPreset.Enhanced);
        }

        GraphicsPresetRegistry.Default.Detect();

        foreach (string statement in startupCommands ?? [])
        {
            System.Execute(statement);
        }

        if (!_renderer.Load())
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
    public bool Update(InputState input, float elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(input);
        Input.Advance(elapsedSeconds);

        // Gamepad navigation is read first, while the frame's input is still uncaptured: once
        // Input.Update has taken the keyboard it also marks the frame captured, and the gamepad edge
        // tests would then report nothing.
        bool wasOpen = Model.IsOpen;
        if (wasOpen)
        {
            HandleGamepad(input);
        }

        bool consumed = Input.Update(input, _renderer.VisibleLines);
        if (wasOpen)
        {
            input.Captured = true;
        }

        return consumed || wasOpen;
    }

    /// <summary>Draws the console, if it is open.</summary>
    /// <param name="batch">The batch to draw into; it must already be between Begin and End.</param>
    /// <param name="screenWidth">The back buffer's width.</param>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <param name="scale">Retained for call-site compatibility; the reskinned console sizes itself.</param>
    public void Draw(SpriteBatch batch, int screenWidth, int screenHeight, float scale = 1f)
    {
        _ = scale;
        _renderer.Draw(batch, Model, screenWidth, screenHeight, Input.CaretVisible);
    }

    /// <summary>
    /// Drives the open console from the game controller, so it is fully navigable without a keyboard —
    /// scroll, history, completion, submit and close. Typing itself still needs a keyboard, as a text
    /// console must.
    /// </summary>
    /// <param name="input">The frame's input.</param>
    private void HandleGamepad(InputState input)
    {
        GamepadState pad = input.Gamepad;
        int page = Math.Max(1, _renderer.VisibleLines - 2);

        if (pad.WasPressed(GamepadButton.DpadUp))
        {
            Model.Scroll(1, _renderer.VisibleLines);
        }

        if (pad.WasPressed(GamepadButton.DpadDown))
        {
            Model.Scroll(-1, _renderer.VisibleLines);
        }

        if (pad.WasPressed(GamepadButton.LeftShoulder))
        {
            Model.Scroll(page, _renderer.VisibleLines);
        }

        if (pad.WasPressed(GamepadButton.RightShoulder))
        {
            Model.Scroll(-page, _renderer.VisibleLines);
        }

        if (pad.WasPressed(GamepadButton.DpadLeft))
        {
            Model.HistoryPrevious();
        }

        if (pad.WasPressed(GamepadButton.DpadRight))
        {
            Model.HistoryNext();
        }

        if (pad.WasPressed(GamepadButton.West))
        {
            Model.Complete();
        }

        if (pad.WasPressed(GamepadButton.South) || pad.WasPressed(GamepadButton.Start))
        {
            Model.Submit();
        }

        if (pad.WasPressed(GamepadButton.East) || pad.WasPressed(GamepadButton.Back))
        {
            Model.IsOpen = false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether this console is running without the saved settings file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A capture run must start from the defaults and apply only its command line. Otherwise a setting
    /// a player once changed silently decides what the reference set looks like — which is not
    /// hypothetical: an <c>r_scale 0.85</c> left over from an unrelated experiment quietly rendered
    /// every offscreen capture at 85% and upscaled it, and the resampling showed up as a 74%
    /// whole-frame difference that looked like a renderer bug.
    /// </para>
    /// <para>
    /// It must not <em>write</em> either, for the same reason in the other direction: a capture's
    /// <c>+</c> flags would otherwise be saved into the player's own settings on the way out.
    /// </para>
    /// </remarks>
    public bool IsolatedFromSavedSettings { get; private set; }

    /// <summary>Writes the settings file, unless this console is isolated from it.</summary>
    /// <returns><see langword="true"/> when it was written.</returns>
    public bool SaveSettings()
    {
        if (IsolatedFromSavedSettings)
        {
            return false;
        }

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
