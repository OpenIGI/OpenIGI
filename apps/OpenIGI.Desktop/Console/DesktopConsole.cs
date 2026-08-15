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
    private readonly DeveloperConsole _view;
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
        _view = new DeveloperConsole(device, Model, _logger);

        // Seconds since the console was built, which is near enough to "since launch" for reading a
        // log and needs no clock of its own. Stopwatch rather than wall time: it cannot go backwards
        // when the machine's clock is corrected mid-session. Fully qualified because this type's own
        // `System` property shadows the namespace.
        long started = global::System.Diagnostics.Stopwatch.GetTimestamp();
        System.Clock = () => global::System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds;

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

    /// <summary>Gets the on-screen view: the floating panels, their input and their drawing.</summary>
    public DeveloperConsole View => _view;

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
        // generated registrations are what make the registry complete before settings are read. Both
        // assemblies matter: the console's own con_* variables live here, and without this call a
        // saved layout line would be met with "unknown variable" and silently discarded.
        Rendering.Generated.CVarRegistrations.RegisterAll();
        Generated.CVarRegistrations.RegisterAll();

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

        GraphicsPreset detectedPreset = GraphicsPresetRegistry.Default.Detect();

        // Builds which shipped Ultra as dynamic wrote that value into settings.cfg. Merely correcting
        // the preset would leave those installations on the broken tower/interior path until the player
        // happened to reselect Ultra. Identify that old preset narrowly: if changing only lighting back
        // to baked makes every managed value match today's Ultra, migrate it. Any other custom setting
        // keeps the player's explicit dynamic choice.
        if (settingsLoaded
            && detectedPreset == GraphicsPreset.Custom
            && RenderCVars.Lighting.Value == LightingModel.Dynamic)
        {
            RenderCVars.Lighting.Value = LightingModel.Baked;
            if (GraphicsPresetRegistry.Default.Detect() == GraphicsPreset.Ultra)
            {
                _logger.Information(
                    LogCategory,
                    "Migrated the former Ultra dynamic-lighting default to authored baked lighting.");
            }
            else
            {
                RenderCVars.Lighting.Value = LightingModel.Dynamic;
                GraphicsPresetRegistry.Default.Detect();
            }
        }

        foreach (string statement in startupCommands ?? [])
        {
            System.Execute(statement);
        }

        _logger.Information(
            LogCategory,
            $"Effective settings: r_smooth={Rendering.RenderCVars.SmoothPresentation.Value}, "
                + $"r_hdr={Rendering.RenderCVars.OffscreenScene.Value}, "
                + $"r_deferred={Rendering.RenderCVars.Deferred.Value}, "
                + $"r_taa={Rendering.RenderCVars.TemporalFeedback.Value:0.###}, "
                + $"r_scale={Rendering.RenderCVars.RenderScale.Value:0.###}, "
                + $"r_dof={Rendering.RenderCVars.FocalDistance.Value:0.#}/{Rendering.RenderCVars.FocalRange.Value:0.#}/"
                + $"{Rendering.RenderCVars.FocalBlur.Value:0.#}, "
                + $"r_motion_blur={Rendering.RenderCVars.MotionBlur.Value:0.###}.");

        System.Variables.AnyChanged += OnVariableChanged;
        GraphicsPresetRegistry.Default.PresetChanged += OnPresetChanged;

        if (!_view.Load())
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

        // The toggle is handled here rather than in the view, because it is the one key that has to
        // work when the console is shut and the view is doing nothing at all.
        bool wasOpen = Model.IsOpen;
        if (input.Keyboard.WasPressed(ToggleKey))
        {
            Model.IsOpen = !wasOpen;
            DrainCharacters(input.Keyboard);
            input.Captured = true;
            return true;
        }

        // Gamepad navigation is read first, while the frame's input is still uncaptured: once the
        // frame is marked captured every edge test reports nothing.
        if (wasOpen)
        {
            HandleGamepad(input);
        }

        bool consumed = _view.Update(input, elapsedSeconds);
        if (wasOpen)
        {
            input.Captured = true;
        }

        return consumed || wasOpen;
    }

    /// <summary>Draws the console and any panel the reader has left showing.</summary>
    /// <param name="batch">The batch to draw into; it must already be between Begin and End.</param>
    /// <param name="screenWidth">The back buffer's width.</param>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <param name="scale">Retained for call-site compatibility; the console sizes itself from <c>con_scale</c>.</param>
    public void Draw(SpriteBatch batch, int screenWidth, int screenHeight, float scale = 1f)
    {
        _ = scale;
        _view.Draw(batch, screenWidth, screenHeight);
    }

    /// <summary>Gets a value indicating whether anything needs drawing this frame.</summary>
    /// <remarks>
    /// Not the same question as <see cref="IsOpen"/>: the watch and performance panels can be left
    /// showing with the console shut. It is deliberately false during a capture run, so a reader's
    /// pinned overlay cannot leak into a reference screenshot and break the vanilla pixel gate.
    /// </remarks>
    public bool WantsDraw =>
        Model.IsOpen || (!IsolatedFromSavedSettings && DeveloperConsole.HasVisibleOverlays);

    /// <summary>Gets a value indicating whether the pointer must be freed and shown this frame.</summary>
    /// <remarks>
    /// Polled from the game loop's cursor chain, which re-asserts the lock on every pass — so a console
    /// that merely released the cursor when it opened would have it taken back a frame later.
    /// </remarks>
    public bool WantsPointer => _view.WantsPointer;

    /// <summary>The key that shows and hides the console.</summary>
    public static KeyCode ToggleKey => KeyCode.Grave;

    /// <summary>
    /// Throws away the characters the toggle key itself produced.
    /// </summary>
    /// <param name="keyboard">The frame's keyboard.</param>
    /// <remarks>
    /// The platform decodes text before the key event is seen, so opening the console would otherwise
    /// leave a backtick sitting in the prompt.
    /// </remarks>
    private static void DrainCharacters(KeyboardState keyboard)
    {
        while (keyboard.TryDequeueCharacter(out _))
        {
            // Discarded.
        }
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
        int page = Math.Max(1, _view.VisibleLines - 2);

        if (pad.WasPressed(GamepadButton.DpadUp))
        {
            Model.Scroll(1, _view.VisibleLines);
        }

        if (pad.WasPressed(GamepadButton.DpadDown))
        {
            Model.Scroll(-1, _view.VisibleLines);
        }

        if (pad.WasPressed(GamepadButton.LeftShoulder))
        {
            Model.Scroll(page, _view.VisibleLines);
        }

        if (pad.WasPressed(GamepadButton.RightShoulder))
        {
            Model.Scroll(-page, _view.VisibleLines);
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

        // The panels' geometry is a setting like any other, so it is folded into the variable that
        // carries it just before the file is rendered rather than being written on every drag.
        _view.SaveLayout();

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
        System.Variables.AnyChanged -= OnVariableChanged;
        GraphicsPresetRegistry.Default.PresetChanged -= OnPresetChanged;
        _view.Dispose();
    }

    private void OnVariableChanged(object? sender, CVarChangedEventArgs e)
    {
        if (e.Variable.Name.StartsWith("r_", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Information(LogCategory, $"CVar changed: {e.Variable.Name} {e.PreviousText} -> {e.CurrentText}.");
        }
    }

    private void OnPresetChanged(object? sender, GraphicsPreset preset) =>
        _logger.Information(LogCategory, $"Graphics preset changed: {preset}.");

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
            if (!inner.IsEnabled(level))
            {
                return;
            }

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

            // The category rides beside the text rather than inside it. Formatted in, it could not be
            // filtered on, it matched every search for the subsystem's name, and it came along when a
            // line was copied.
            console.Write(new ConsoleLine(message, severity, category, console.Elapsed));
        }
    }
}
