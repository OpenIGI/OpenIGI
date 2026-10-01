using OpenIGI.Core.Diagnostics;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Config;

/// <summary>
/// Reads <c>config.qvm</c>, which is where the game keeps every setting it remembers.
/// </summary>
/// <remarks>
/// <para>
/// The save file is a compiled script. Its entire body is calls into sixteen <c>GO*</c> natives the
/// engine registers, one group per profile, and running it <em>is</em> loading the configuration —
/// there is no separate parser and no binary record on disk.
/// </para>
/// <para>
/// The shape, from a retail file: <c>GOStart(n)</c> and <c>GOContentControlPW(s)</c> once, then for
/// each profile <c>GOPlayer(name)</c> followed by that profile's settings and its twenty-seven
/// <c>GOInRemap(action, device, key)</c> bindings. <c>GOPlayer</c> is the delimiter: every setting
/// after one belongs to it.
/// </para>
/// <para>
/// Bindings are stored by <em>name</em> — <c>MoveLeft</c>, <c>INPUTPORT_DEVICE_KEYBOARD</c>,
/// <c>KEY_LEFT</c> — rather than by number, so they survive a change to the engine's key numbering.
/// The names arrive as script constants, which is why the loader registers each one it has not seen
/// and records the name rather than the value.
/// </para>
/// </remarks>
public sealed class ConfigScript
{
    private const string LogCategory = "Config";

    /// <summary>The retail save file, relative to the installation root.</summary>
    public const string RetailPath = "LOCAL:config.qvm";

    /// <summary>The file OpenIGI writes its own settings to.</summary>
    /// <remarks>
    /// Deliberately not the retail file. The format is identical, so copying this over
    /// <c>config.qvm</c> would work, but nothing here writes into an original installation.
    /// </remarks>
    public const string OpenIgiPath = "LOCAL:openigi-config.qvm";

    /// <summary>Runs a compiled configuration script and returns what it built.</summary>
    /// <param name="script">The bytecode.</param>
    /// <param name="logger">Where to report progress.</param>
    /// <returns>The configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="script"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A faulted script yields whatever was loaded before the fault rather than throwing: a corrupt
    /// save file should cost the settings, not the session.
    /// </remarks>
    /// <param name="source">
    /// What to call the script in a diagnostic, for when it is not the retail file. Reported rather than
    /// assumed because this loads OpenIGI's own file far more often than the original's, and a fault
    /// blamed on the wrong one sends you reading a file that turns out to be perfectly good.
    /// </param>
    public static GameConfig Load(
        ReadOnlyMemory<byte> script,
        IIgiLogger? logger = null,
        string? source = null)
    {
        IIgiLogger log = logger ?? NullLogger.Instance;
        GameConfig config = new();
        Loader loader = new(config);

        NativeRegistry natives = new();
        loader.Register(natives);

        QvmProgram program = QvmProgram.Read(script);

        // Every identifier the file names — GD_2, ENGLISH, KEY_LEFT — is a constant the engine would
        // have registered. Their values are irrelevant here because a binding is stored by name, so
        // an unknown one is registered as itself rather than rejected.
        foreach (string? symbol in program.SymbolNames)
        {
            if (symbol is not null && !natives.Contains(symbol))
            {
                natives.RegisterConstant(symbol, loader.Intern(symbol));
            }
        }

        QvmInterpreter interpreter = new(program, natives);
        QvmError error = interpreter.Run();
        if (error != QvmError.None)
        {
            log.Warning(
                LogCategory,
                $"{source ?? RetailPath} faulted with {error} at offset {interpreter.ErrorOffset}: "
                    + interpreter.ErrorMessage);
        }

        loader.Finish();
        config.RestoreMissingProfileBindings();
        log.Information(LogCategory, $"Loaded {config.Profiles.Count} profile(s), active '{config.Active?.Name}'.");
        return config;
    }

    /// <summary>The names a configuration file may call: the retail file's, and OpenIGI's own <c>GOControlsVersion</c>.</summary>
    /// <remarks>Listed in the order a retail file uses them, which is also the order the writer emits.</remarks>
    public static IReadOnlyList<string> NativeNames { get; } =
    [
        "GOStart",
        "GOContentControlPW",
        "GOControlsVersion",
        "GOPlayer",
        "GOActiveMission",
        "GOInMouInv",
        "GOInMouSens",
        "GOInRemap",
        "GOGfxDisp",
        "GOGfxDevice",
        "GOGfxGamma",
        "GOGfxPerformance",
        "GOGameLang",
        "GOGameDiff",
        "GOIsBlood",
        "GOSoundSpeech",
        "GOSoundMusic",
        "GOSoundFX",
    ];

    /// <summary>Binds the <c>GO*</c> natives to a configuration being built.</summary>
    private sealed class Loader(GameConfig config)
    {
        private readonly Dictionary<string, int> _interned = new(StringComparer.Ordinal);
        private readonly List<string> _names = [];
        private PlayerProfile? _current;
        private int _pendingActive;

        /// <summary>Applies the active index the file opened with, once every profile has been read.</summary>
        public void Finish() => config.ActiveIndex = _pendingActive;

        /// <summary>Gives a name a stable integer so a constant can carry it back.</summary>
        /// <param name="name">The constant's name.</param>
        /// <returns>The token.</returns>
        public int Intern(string name)
        {
            if (_interned.TryGetValue(name, out int token))
            {
                return token;
            }

            token = _names.Count;
            _interned[name] = token;
            _names.Add(name);
            return token;
        }

        /// <summary>Recovers the name behind a token, or the number when it is a plain literal.</summary>
        private string NameOf(int token) =>
            token >= 0 && token < _names.Count
                ? _names[token]
                : token.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public void Register(NativeRegistry natives)
        {
            // GOStart is the ACTIVE PROFILE INDEX, not the profile count. A retail file whose
            // GOStart is 6 goes on to list seven players, and the seventh is the one being played.
            // It arrives before any GOPlayer, so it is stashed and applied once they all exist.
            natives.RegisterFunction("GOStart", c => Set(() => _pendingActive = c.GetInt32(0)), 1);
            natives.RegisterFunction("GOContentControlPW", c => Set(() => config.ContentControlPassword = c.GetString(0)), 1);

            // OpenIGI's own; see GameConfig.ControlsVersion. The retail file never calls it.
            natives.RegisterFunction("GOControlsVersion", c => Set(() => config.ControlsVersion = c.GetInt32(0)), 1);

            natives.RegisterFunction("GOPlayer", c => Set(() =>
            {
                _current = new PlayerProfile { Name = GameConfig.Truncate(c.GetString(0)) };
                config.Add(_current);
            }), 1);

            natives.RegisterFunction("GOActiveMission", c => OnProfile(p => p.ActiveMission = c.GetInt32(0)), 1);
            natives.RegisterFunction("GOInMouInv", c => OnProfile(p => p.InvertMouse = c.GetInt32(0) != 0), 1);
            natives.RegisterFunction("GOInMouSens", c => OnProfile(p => p.MouseSensitivity = c.GetSingle(0)), 1);

            natives.RegisterFunction("GOInRemap", c => OnProfile(p => p.Bindings.Add(
                new InputBinding(NameOf(c.GetInt32(0)), NameOf(c.GetInt32(1)), NameOf(c.GetInt32(2))))), 3);

            natives.RegisterFunction("GOGfxDisp", c => OnProfile(p => p.DisplayMode = c.GetInt32(0)), 1);
            natives.RegisterFunction("GOGfxDevice", c => OnProfile(p => p.DeviceGuid = c.GetString(0)), 1);
            natives.RegisterFunction("GOGfxGamma", c => OnProfile(p => p.Gamma = c.GetSingle(0)), 1);
            natives.RegisterFunction("GOGfxPerformance", c => OnProfile(p => p.PerformanceFlags = c.GetInt32(0)), 1);
            natives.RegisterFunction("GOGameLang", c => OnProfile(p => p.Language = c.GetInt32(0)), 1);
            natives.RegisterFunction("GOGameDiff", c => OnProfile(p => p.Difficulty = c.GetInt32(0)), 1);
            natives.RegisterFunction("GOIsBlood", c => OnProfile(p => p.BloodEnabled = c.GetInt32(0) != 0), 1);
            natives.RegisterFunction("GOSoundSpeech", c => OnProfile(p => p.SpeechVolume = c.GetSingle(0)), 1);
            natives.RegisterFunction("GOSoundMusic", c => OnProfile(p => p.MusicVolume = c.GetSingle(0)), 1);
            natives.RegisterFunction("GOSoundFX", c => OnProfile(p => p.SoundEffectsVolume = c.GetSingle(0)), 1);
        }

        private static QvmValue Set(Action action)
        {
            action();
            return QvmValue.FromInt32(1);
        }

        /// <summary>Applies a setting to the profile the file is currently describing.</summary>
        /// <remarks>A setting before the first <c>GOPlayer</c> has nowhere to go and is dropped.</remarks>
        private QvmValue OnProfile(Action<PlayerProfile> action)
        {
            if (_current is not null)
            {
                action(_current);
            }

            return QvmValue.FromInt32(1);
        }
    }
}
