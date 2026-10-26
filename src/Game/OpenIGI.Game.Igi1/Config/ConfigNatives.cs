using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Game.Config;
using OpenIGI.Game.Menus;

namespace OpenIGI.Game.Igi1.Config;

/// <summary>
/// The fifty-one <c>Config_*</c> functions the menu scripts call.
/// </summary>
/// <remarks>
/// <para>
/// Registered by the original at <c>0x402FF0</c>. All but a handful are one-line accessors on the
/// active profile, so they are grouped here rather than spread across fifty-one methods; the ones
/// with real behaviour — the perf-level mapping, profile creation, the list fillers — say where they
/// came from individually.
/// </para>
/// <para>
/// These are reached through <see cref="MenuScriptContext.Fallback"/>, which hands over the
/// <em>unevaluated</em> arguments. That matters: a volume setter has to pull its argument as a float,
/// and a profile name as a string, and neither would survive the integer walk.
/// </para>
/// </remarks>
public sealed class ConfigNatives : IMenuConfiguration, IMenuInputBindings
{
    private readonly GameConfig _config;
    private readonly IConfigHost _host;

    /// <summary>Initializes a new instance of the <see cref="ConfigNatives"/> class.</summary>
    /// <param name="config">The configuration to read and write.</param>
    /// <param name="host">Supplies what the shell knows: display modes, devices and missions.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public ConfigNatives(GameConfig config, IConfigHost host)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(host);

        _config = config;
        _host = host;
    }

    /// <summary>Gets the configuration these act on.</summary>
    public GameConfig Config => _config;

    /// <inheritdoc/>
    /// <remarks>The active profile's (<c>0x4061C0</c>).</remarks>
    public InputBinding? Find(string action) =>
        _config.Active?.Bindings.FindIndex(binding => Names(binding, action)) is { } index and >= 0
            ? _config.Active.Bindings[index]
            : null;

    /// <inheritdoc/>
    /// <remarks>
    /// The original resets the list from its own defaults (<c>0x536410</c>); the port's are a fresh profile's,
    /// <see cref="PlayerProfile.DefaultBindings"/>, as its profiles' own reset gives.
    /// </remarks>
    public InputBinding? DefaultOf(string action) =>
        PlayerProfile.DefaultBindings.FirstOrDefault(binding => Names(binding, action)) is { Action: not null } found ? found : null;

    /// <inheritdoc/>
    public void Bind(InputBinding binding)
    {
        if (_config.Active is not { } profile)
        {
            return;
        }

        int index = profile.Bindings.FindIndex(candidate => Names(candidate, binding.Action));
        if (index >= 0)
        {
            profile.Bindings[index] = binding;
        }
        else
        {
            profile.Bindings.Add(binding);
        }
    }

    private static bool Names(InputBinding binding, string action) =>
        string.Equals(binding.Action, action, StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets the list box a fill call is currently targeting, if any.</summary>
    /// <remarks>
    /// The original's fill natives write into whichever control is running its
    /// <c>Collect Items Script</c>, which it knows because the expression is being evaluated for that
    /// task. Here the manager sets it before running the expression.
    /// </remarks>
    public MenuWindowTask? Target { get; set; }

    /// <summary>Handles a call, if it is one of these.</summary>
    /// <param name="name">The function name.</param>
    /// <param name="arguments">The unevaluated arguments.</param>
    /// <param name="result">Receives the result.</param>
    /// <returns><see langword="false"/> when the name is not a <c>Config_*</c> function.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="arguments"/> is <see langword="null"/>.</exception>
    public bool TryInvoke(string name, ScriptArguments arguments, out QvmValue result)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        result = QvmValue.FromInt32(0);
        PlayerProfile? p = _config.Active;

        switch (name)
        {
            // ---- player profiles ----

            case "Config_GetNumberOfPlayerProfiles":
                result = QvmValue.FromInt32(_config.Profiles.Count);
                return true;

            case "Config_GetActivePlayerProfileIndex":
                result = QvmValue.FromInt32(_config.ActiveIndex);
                return true;

            case "Config_SetActivePlayerProfileIndex":
                _config.ActiveIndex = arguments.GetInt32(0);
                return true;

            case "Config_CreateNewPlayerProfile":
                result = QvmValue.FromInt32(_config.CreateProfile(arguments.GetText(0), arguments.GetInt32(1)) ? 1 : 0);
                return true;

            case "Config_DeletePlayerProfile":
                result = QvmValue.FromInt32(_config.DeleteProfile(arguments.GetInt32(0)) ? 1 : 0);
                return true;

            case "Config_PlayerGetActiveMission":
                result = QvmValue.FromInt32(ProfileAt(arguments.GetInt32(0))?.ActiveMission ?? 0);
                return true;

            case "Config_FillPlayerProfileListBox":
                Fill(_config.Profiles.Select((profile, index) => new MenuListItem(index, profile.Name)));
                return true;

            // ---- graphics ----

            case "Config_FillRenderDeviceListBox":
                Fill(_host.GetRenderDevices());
                return true;

            case "Config_FillScreenResolutionListBox":
                Fill(_host.GetDisplayModes(arguments.GetInt32(0)));
                return true;

            case "Config_GraphicOptionsGetDevice":
                result = QvmValue.FromInt32(_host.GetDeviceIndex(p?.DeviceGuid ?? PlayerProfile.DefaultDeviceGuid));
                return true;

            case "Config_GraphicOptionsSetDevice":
                Apply(p, x => x.DeviceGuid = _host.GetDeviceGuid(arguments.GetInt32(0)));
                return true;

            case "Config_GraphicOptionsGetResolution":
                result = QvmValue.FromInt32(p?.DisplayMode ?? 0);
                return true;

            case "Config_GraphicOptionsSetResolution":
                Apply(p, x => x.DisplayMode = arguments.GetInt32(0));
                _host.SetResolution(p?.DisplayMode ?? 0);
                return true;

            case "Config_GraphicOptionsGetGamma":
                result = QvmValue.FromSingle(p?.Gamma ?? 0f);
                return true;

            case "Config_GraphicOptionsSetGamma":
                Apply(p, x => x.Gamma = (float)arguments.GetReal(0));
                _host.SetGamma(p?.Gamma ?? 0f);
                return true;

            case "Config_GraphicOptionsGetTransparency":
                result = QvmValue.FromInt32(p?.Transparency == true ? 1 : 0);
                return true;

            case "Config_GraphicOptionsSetTransparency":
                Apply(p, x => x.Transparency = arguments.GetBoolean(0));
                return true;

            case "Config_GraphicOptionsGetPerformanceFlags":
                result = QvmValue.FromInt32(p?.PerformanceFlags ?? 0);
                return true;

            case "Config_GraphicOptionsSetPerformanceFlags":
                Apply(p, x => x.PerformanceFlags = arguments.GetInt32(0));
                return true;

            case "Config_GraphicOptionsGetPerfFlagsFromLevel":
                result = QvmValue.FromInt32(GraphicsPerformance.FlagsForLevel(arguments.GetInt32(0)));
                return true;

            case "Config_GraphicOptionsGetPerfLevelFromFlags":
                result = QvmValue.FromInt32(GraphicsPerformance.LevelForFlags(arguments.GetInt32(0)));
                return true;

            case "Config_GraphicOptionsNumPerfLevels":
                result = QvmValue.FromInt32(GraphicsPerformance.LevelCount);
                return true;

            case "Config_ResetGraphicOptions":
                ResetGraphics(p);
                return true;

            // ---- sound ----

            case "Config_SoundOptionsGetSoundsEffects":
                result = QvmValue.FromInt32(p?.SoundEffects == true ? 1 : 0);
                return true;

            case "Config_SoundOptionsSetSoundsEffects":
                Apply(p, x => x.SoundEffects = arguments.GetBoolean(0));
                return true;

            case "Config_SoundOptionsGetSoundsEffectsVolume":
                result = QvmValue.FromSingle(p?.SoundEffectsVolume ?? 0f);
                return true;

            case "Config_SoundOptionsSetSoundsEffectsVolume":
                Apply(p, x => x.SoundEffectsVolume = (float)arguments.GetReal(0));
                return true;

            case "Config_SoundOptionsGetMusic":
                result = QvmValue.FromInt32(p?.Music == true ? 1 : 0);
                return true;

            case "Config_SoundOptionsSetMusic":
                Apply(p, x => x.Music = arguments.GetBoolean(0));
                return true;

            case "Config_SoundOptionsGetMusicVolume":
                result = QvmValue.FromSingle(p?.MusicVolume ?? 0f);
                return true;

            case "Config_SoundOptionsSetMusicVolume":
                Apply(p, x => x.MusicVolume = (float)arguments.GetReal(0));
                return true;

            case "Config_SoundOptionsGetSpeech":
                result = QvmValue.FromInt32(p?.Speech == true ? 1 : 0);
                return true;

            case "Config_SoundOptionsSetSpeech":
                Apply(p, x => x.Speech = arguments.GetBoolean(0));
                return true;

            case "Config_SoundOptionsGetSpeechVolume":
                result = QvmValue.FromSingle(p?.SpeechVolume ?? 0f);
                return true;

            case "Config_SoundOptionsSetSpeechVolume":
                Apply(p, x => x.SpeechVolume = (float)arguments.GetReal(0));
                return true;

            case "Config_SoundOptionsGetReverseStereo":
                result = QvmValue.FromInt32(p?.ReverseStereo == true ? 1 : 0);
                return true;

            case "Config_SoundOptionsSetReverseStereo":
                Apply(p, x => x.ReverseStereo = arguments.GetBoolean(0));
                return true;

            // ---- input and gameplay ----

            // Both the getter and the setter negate, so the value a script sees round-trips and only
            // the stored sense is inverted. Modelled the way a script sees it.
            case "Config_GameOptionsInputGetInvertMouse":
                result = QvmValue.FromInt32(p?.InvertMouse == true ? 1 : 0);
                return true;

            case "Config_GameOptionsInputSetInvertMouse":
                Apply(p, x => x.InvertMouse = arguments.GetBoolean(0));
                return true;

            case "Config_GameOptionsInputGetMouseSensitivity":
                result = QvmValue.FromSingle(p?.MouseSensitivity ?? 0f);
                return true;

            case "Config_GameOptionsInputSetMouseSensitivity":
                Apply(p, x => x.MouseSensitivity = (float)arguments.GetReal(0));
                return true;

            case "Config_GameOptionsInputGetBloodEnabled":
                result = QvmValue.FromInt32(p?.BloodEnabled == true ? 1 : 0);
                return true;

            case "Config_GameOptionsInputSetBloodEnabled":
                Apply(p, x => x.BloodEnabled = arguments.GetBoolean(0));
                return true;

            // ---- content control ----

            case "Config_SetContentControlPassword":
                _config.ContentControlPassword = arguments.GetText(0);
                return true;

            case "Config_GetContentControlPassword":
                result = QvmValue.FromString(_config.ContentControlPassword);
                return true;

            case "Config_VerifyContentControlPassword":
                result = QvmValue.FromInt32(
                    string.Equals(_config.ContentControlPassword, arguments.GetText(0), StringComparison.Ordinal) ? 1 : 0);
                return true;

            case "Config_IsContentControlPasswordEnabled":
                result = QvmValue.FromInt32(_config.ContentControlPassword.Length > 0 ? 1 : 0);
                return true;

            case "Config_IsGermany":
                result = QvmValue.FromInt32(_config.IsGermany ? 1 : 0);
                return true;

            // 0x488E90: whether the machine's locale country is "USA", which hides the main menu's Language item. The
            // locale names its country in full ("United States"), so the original never finds it.
            case "LevelFlow_IsCountryUSA":
                result = QvmValue.FromInt32(0);
                return true;

            // ---- missions ----

            case "Config_FillMissionSelectionBox":
                Fill(_host.GetMissions(_config.Active?.ActiveMission ?? 0));
                return true;

            case "Config_FillMissionPictureBox":
                _host.FillMissionPictures(Target);
                return true;

            case "Config_FillMissionTextBox":
                _host.FillMissionText(Target);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Restores the graphics settings to what a fresh installation has.</summary>
    /// <remarks>
    /// <c>Config_ResetGraphicOptions</c> (<c>0x403B70</c>) is called from
    /// <c>Config_ResetToDefaults</c> as well as from the menu's Reset entry, so it is the one place
    /// the graphics defaults live.
    /// </remarks>
    private void ResetGraphics(PlayerProfile? profile)
    {
        if (profile is null)
        {
            return;
        }

        profile.DeviceGuid = PlayerProfile.DefaultDeviceGuid;
        profile.DisplayMode = _host.DefaultDisplayMode;
        profile.Gamma = 0f;
        profile.Transparency = true;
        profile.PerformanceFlags = GraphicsPerformance.FlagsForLevel(0);
    }

    private PlayerProfile? ProfileAt(int index) =>
        index >= 0 && index < _config.Profiles.Count ? _config.Profiles[index] : null;

    /// <summary>Applies a change, and does nothing when there is no profile to change.</summary>
    /// <remarks>
    /// The original dereferences its active-profile pointer unguarded, which cannot be null because a
    /// default profile is always created. Refusing rather than faulting is the safer shape and cannot
    /// differ on any configuration the loader produces.
    /// </remarks>
    private static void Apply(PlayerProfile? profile, Action<PlayerProfile> change)
    {
        if (profile is not null)
        {
            change(profile);
        }
    }

    /// <summary>Puts items into the list box whose collect script is running.</summary>
    private void Fill(IEnumerable<MenuListItem> items)
    {
        if (Target is ListBoxTask list)
        {
            list.SetItems(items);
        }
        else if (Target is ScrollListBoxTask scroll)
        {
            scroll.SetItems(items);
        }
    }
}
