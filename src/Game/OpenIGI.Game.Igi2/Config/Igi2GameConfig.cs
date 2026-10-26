using OpenIGI.Engine.Resources;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Config;

/// <summary>The graphics options, <c>GfxOptions_s</c>: what the detail settings came to.</summary>
public sealed class Igi2GfxOptions
{
    /// <summary>Gets or sets the texture detail, a <c>GFXOPTIONS_TEXTUREDETAIL_*</c> value.</summary>
    public int TextureDetail { get; set; }

    /// <summary>Gets or sets the model detail, nought to one.</summary>
    public float ModelDetail { get; set; } = 1f;

    /// <summary>Gets or sets a value indicating whether shadows are stencilled.</summary>
    public bool StencilShadows { get; set; }

    /// <summary>Gets or sets a value indicating whether textures are compressed.</summary>
    public bool TextureCompression { get; set; }

    /// <summary>Gets or sets a value indicating whether forests are drawn at high quality.</summary>
    /// <remarks>On in every default row but the lowest (<c>GfxOptions_atDefaultOptions</c>), so a new profile has it.</remarks>
    public bool HighQualityForests { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether forests sway in the wind.</summary>
    public bool WindOnForests { get; set; }

    /// <summary>Gets or sets a value indicating whether lens flares are drawn.</summary>
    public bool LensFlares { get; set; }

    /// <summary>Gets or sets a value indicating whether bullet hits throw effects.</summary>
    public bool BulletHitEffects { get; set; }

    /// <summary>Gets or sets the water quality, a <c>WATERRENDER_QUALITY_*</c> value.</summary>
    public int WaterQuality { get; set; }

    /// <summary>Gets or sets the terrain detail, nought to one.</summary>
    public float TerrainDetail { get; set; } = 1f;

    /// <summary>A copy, as <c>GfxOptions_ApplyCurrentOptionsToLastPlayerProfile</c> takes one.</summary>
    /// <returns>The copy.</returns>
    public Igi2GfxOptions Copy() => (Igi2GfxOptions)MemberwiseClone();
}

/// <summary>A sound channel's setting, <c>tSoundOptions</c>: whether it plays and how loud.</summary>
/// <param name="IsOn">Whether it plays.</param>
/// <param name="Volume">How loud, nought to one.</param>
public readonly record struct Igi2SoundSetting(bool IsOn, float Volume);

/// <summary>One control's binding, as <c>GOInRemap</c> writes it: the button, the device and the source, by value.</summary>
/// <param name="Button">The input button.</param>
/// <param name="Device">The input device.</param>
/// <param name="Source">The key or mouse source.</param>
public readonly record struct Igi2InputBinding(int Button, int Device, int Source);

/// <summary>A player's record of one mission, as <c>GOPlayerMissionStats</c> writes it: ten counts and three rows of five figures.</summary>
/// <param name="Counts">The counts, in the order the file writes them.</param>
/// <param name="Figures">The figures, three rows of five.</param>
public sealed record Igi2MissionStats(IReadOnlyList<int> Counts, IReadOnlyList<float> Figures);

/// <summary>One player profile, <c>PlayerProfile_s</c>.</summary>
public sealed class Igi2PlayerProfile
{
    /// <summary>The longest call sign a profile keeps.</summary>
    public const int CallSignLength = 31;

    /// <summary>Gets or sets the player's call sign.</summary>
    public string CallSign { get; set; } = string.Empty;

    /// <summary>Gets the furthest mission reached at each difficulty.</summary>
    public int[] ActiveMission { get; } = new int[3];

    /// <summary>Gets or sets a value indicating whether the mouse is inverted.</summary>
    public bool InvertMouse { get; set; } = true;

    /// <summary>Gets or sets the mouse sensitivity.</summary>
    public float MouseSensitivity { get; set; } = 0.4f;

    /// <summary>Gets the control bindings, in the order the file lists them.</summary>
    public List<Igi2InputBinding> Bindings { get; } = [];

    /// <summary>Gets or sets the display mode: width, height and depth.</summary>
    public (int Width, int Height, int Depth) Display { get; set; }

    /// <summary>Gets or sets the display device's name.</summary>
    public string Device { get; set; } = string.Empty;

    /// <summary>Gets or sets the gamma.</summary>
    public float Gamma { get; set; }

    /// <summary>Gets or sets the crosshair's opacity (config.c Config_DefaultGameOptions).</summary>
    public float CrossHairAlpha { get; set; } = 1f;

    /// <summary>Gets or sets a value indicating whether the crosshair is drawn thick.</summary>
    public bool ThickCrossHair { get; set; }

    /// <summary>Gets or sets the crosshair's colour.</summary>
    public (float R, float G, float B) CrossHairColour { get; set; } = (1f, 0.25f, 0f);

    /// <summary>Gets or sets a value indicating whether vertices are processed in software.</summary>
    public bool ForceSoftwareVertexProcessing { get; set; }

    /// <summary>Gets or sets the language, a <see cref="Igi2ConfigConstants.Languages"/> place.</summary>
    public int Language { get; set; }

    /// <summary>Gets or sets the difficulty, a <see cref="Igi2ConfigConstants.Difficulties"/> place.</summary>
    public int Difficulty { get; set; } = 1;

    /// <summary>Gets or sets a value indicating whether blood is shown.</summary>
    public bool Blood { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the inventory is hidden.</summary>
    public bool HideInventory { get; set; }

    /// <summary>Gets or sets the speech setting.</summary>
    public Igi2SoundSetting Speech { get; set; } = new(true, 1f);

    /// <summary>Gets or sets the music setting.</summary>
    public Igi2SoundSetting Music { get; set; } = new(true, 0.8f);

    /// <summary>Gets or sets the sound effects setting.</summary>
    public Igi2SoundSetting SoundEffects { get; set; } = new(true, 1f);

    /// <summary>Gets or sets the 3D sound provider.</summary>
    public int Sound3DProvider { get; set; }

    /// <summary>Gets or sets the speaker setup.</summary>
    public int SpeakerSetup { get; set; }

    /// <summary>Gets or sets the graphics detail level, a <c>GFXOPTIONS_DETAIL_*</c> value.</summary>
    public int GfxDetailLevel { get; set; }

    /// <summary>Gets or sets the graphics options the profile keeps.</summary>
    public Igi2GfxOptions Gfx { get; set; } = new();

    /// <summary>Gets or sets the antialiasing mode.</summary>
    public int AntialiasingMode { get; set; }

    /// <summary>Gets or sets the texture filtering mode.</summary>
    public int TextureFilteringMode { get; set; }

    /// <summary>Gets the player's mission records, in the order the file lists them.</summary>
    public List<Igi2MissionStats> MissionStats { get; } = [];
}

/// <summary>
/// I.G.I. 2's configuration, <c>Config_tConfig</c> (config.c): the player profiles, which is active, and the graphics options in force,
/// read at start-up from <c>config.qvm</c> (<c>Config_LoadConfig</c>).
/// </summary>
/// <remarks>
/// <para>
/// The file is a compiled script of <c>GO*</c> calls: <c>GOVersion</c>, <c>GOStart</c> (the active profile), <c>GOContentControlPW</c>,
/// then for each profile <c>GOPlayer</c> and its settings, and <c>GOComplete</c> at the end. A file that never reaches
/// <c>GOComplete</c> is not taken: the defaults stand (<c>Config_DefaultGameOptions</c>). A file older than version five enables
/// <c>BreakScript</c>, which stops it.
/// </para>
/// <para>
/// The <c>GfxOptions_*</c> calls set the options in force; <c>GfxOptions_ApplyCurrentOptionsToLastPlayerProfile</c> files them with
/// the profile just read.
/// </para>
/// </remarks>
public sealed class Igi2GameConfig
{
    /// <summary>The file.</summary>
    public const string Script = "LOCAL:config.qvm";

    /// <summary>The version below which a file's <c>BreakScript</c> stops it, <c>Config_ParseVersion</c>.</summary>
    public const int BreakVersion = 5;

    private readonly List<Igi2PlayerProfile> _profiles = [];
    private bool _isComplete;
    private bool _isBreakEnabled;

    /// <summary>Initializes a new instance of the <see cref="Igi2GameConfig"/> class with the defaults.</summary>
    public Igi2GameConfig() => SetDefaults();

    /// <summary>Gets the profiles.</summary>
    public IReadOnlyList<Igi2PlayerProfile> Profiles => _profiles;

    /// <summary>Gets or sets the active profile's place.</summary>
    public int ActiveIndex { get; set; }

    /// <summary>Gets the active profile, or <see langword="null"/> when its place holds none.</summary>
    public Igi2PlayerProfile? Active => (uint)ActiveIndex < (uint)_profiles.Count ? _profiles[ActiveIndex] : null;

    /// <summary>Gets or sets the content control password.</summary>
    public string ContentControlPassword { get; set; } = string.Empty;

    /// <summary>Gets the graphics options in force.</summary>
    public Igi2GfxOptions Gfx { get; private set; } = new();

    /// <summary>Gets or sets the graphics detail level in force, a <c>GFXOPTIONS_DETAIL_*</c> value.</summary>
    public int GfxDetailLevel { get; set; }

    /// <summary>Gets or sets what saving the configuration does; nothing writes into the original installation unless told to.</summary>
    public Action<Igi2GameConfig>? Save { get; set; }

    /// <summary>Sets the defaults, <c>Config_DefaultGameOptions</c>: one profile, <c>David Jones</c>, at the middle difficulty.</summary>
    public void SetDefaults()
    {
        _profiles.Clear();
        _profiles.Add(new Igi2PlayerProfile { CallSign = "David Jones" });
        ActiveIndex = 0;
    }

    /// <summary>Registers the file's natives and the settings natives the scripts call.</summary>
    /// <param name="natives">The script namespace.</param>
    /// <param name="functions">The function table level expressions call, through which the menu's scripts reach the settings.</param>
    public void Register(NativeRegistry natives, Scripting.Igi2ScriptFunctions? functions = null)
    {
        ArgumentNullException.ThrowIfNull(natives);
        RegisterReader(natives);
        RegisterGfxOptions(natives);
        RegisterSettings(natives, functions);
        natives.RegisterFunction("BreakScript", c =>
        {
            if (_isBreakEnabled)
            {
                c.Interpreter.Break();
            }

            return QvmValue.Zero;
        }, 0);
    }

    /// <summary>Reads the file, <c>Config_LoadConfig</c>: a file that is missing or never completes leaves the defaults.</summary>
    /// <param name="resources">Where the file is read from.</param>
    /// <param name="natives">The script namespace, with <see cref="Register"/> done.</param>
    public void Load(ResourceRegistry resources, NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);
        SetDefaults();
        _isComplete = false;
        _isBreakEnabled = false;
        if (resources.TryLoad(Script, out ReadOnlyMemory<byte> bytes))
        {
            try
            {
                new QvmInterpreter(QvmProgram.Read(bytes), natives).Run();
            }
            catch (QvmFormatException)
            {
                _isComplete = false;
            }
        }

        if (!_isComplete)
        {
            SetDefaults();
        }
    }

    private static QvmValue Done() => QvmValue.Zero;

    private Igi2PlayerProfile? Last => _profiles.Count > 0 ? _profiles[^1] : null;

    private void RegisterReader(NativeRegistry natives)
    {
        natives.RegisterFunction("GOVersion", c =>
        {
            _isBreakEnabled = c.GetInt32(0) < BreakVersion;
            return Done();
        }, 1);
        natives.RegisterFunction("GOStart", c =>
        {
            _profiles.Clear();
            ActiveIndex = c.GetInt32(0);
            return Done();
        }, 1);
        natives.RegisterFunction("GOContentControlPW", c =>
        {
            ContentControlPassword = c.GetString(0);
            return Done();
        }, 1);
        natives.RegisterFunction("GOPlayer", c =>
        {
            string name = c.GetString(0);
            _profiles.Add(new Igi2PlayerProfile
            {
                CallSign = name.Length > Igi2PlayerProfile.CallSignLength ? name[..Igi2PlayerProfile.CallSignLength] : name,
                InvertMouse = false,
                MouseSensitivity = 0f,
                Difficulty = 0,
                Blood = false,
                Speech = default,
                Music = default,
                SoundEffects = default,
            });
            return Done();
        }, 1);
        natives.RegisterFunction("GOComplete", _ =>
        {
            _isComplete = true;
            return Done();
        }, 0);
        Profile(natives, "GOActiveMission", 2, (p, c) =>
        {
            if ((uint)c.GetInt32(0) <= 2u)
            {
                p.ActiveMission[c.GetInt32(0)] = c.GetInt32(1);
            }
        });
        Profile(natives, "GOInMouInv", 1, (p, c) => p.InvertMouse = c.GetInt32(0) != 0);
        Profile(natives, "GOInMouSens", 1, (p, c) => p.MouseSensitivity = c.GetSingle(0));
        Profile(natives, "GOInRemap", 3, (p, c) => p.Bindings.Add(new Igi2InputBinding(c.GetInt32(0), c.GetInt32(1), c.GetInt32(2))));
        Profile(natives, "GOGfxDisp", 3, (p, c) => p.Display = (c.GetInt32(0), c.GetInt32(1), c.GetInt32(2)));
        Profile(natives, "GOGfxDevice", 1, (p, c) => p.Device = c.GetString(0));
        Profile(natives, "GOGfxGamma", 1, (p, c) => p.Gamma = c.GetSingle(0));
        Profile(natives, "GOGfxCrossHairAlpha", 1, (p, c) => p.CrossHairAlpha = c.GetSingle(0));
        Profile(natives, "GoGfxCrossHairThickness", 1, (p, c) => p.ThickCrossHair = c.GetInt32(0) != 0);
        Profile(natives, "GoGfxCrossHairColour", 3, (p, c) => p.CrossHairColour = (c.GetSingle(0), c.GetSingle(1), c.GetSingle(2)));
        Profile(natives, "GOGfxForceSoftwareVP", 1, (p, c) => p.ForceSoftwareVertexProcessing = c.GetInt32(0) != 0);
        Profile(natives, "GOGameLang", 1, (p, c) => p.Language = c.GetInt32(0));
        Profile(natives, "GOGameDiff", 1, (p, c) => p.Difficulty = c.GetInt32(0));
        Profile(natives, "GOIsBlood", 1, (p, c) => p.Blood = c.GetInt32(0) != 0);
        Profile(natives, "GOGameIsHideInventory", 1, (p, c) => p.HideInventory = c.GetInt32(0) != 0);
        Profile(natives, "GOSoundSpeech", 2, (p, c) => p.Speech = new Igi2SoundSetting(c.GetInt32(0) != 0, c.GetSingle(1)));
        Profile(natives, "GOSoundMusic", 2, (p, c) => p.Music = new Igi2SoundSetting(c.GetInt32(0) != 0, c.GetSingle(1)));
        Profile(natives, "GOSoundFX", 2, (p, c) => p.SoundEffects = new Igi2SoundSetting(c.GetInt32(0) != 0, c.GetSingle(1)));
        Profile(natives, "GO3DSoundProvider", 1, (p, c) => p.Sound3DProvider = c.GetInt32(0));
        Profile(natives, "GOSpeakerSetup", 1, (p, c) => p.SpeakerSetup = c.GetInt32(0));
        Profile(natives, "GOPlayerMissionStats", 25, (p, c) =>
        {
            // Config_ParsePlayerMissionData takes a record of exactly twenty-five values and no other.
            if (c.ArgumentCount != 25)
            {
                return;
            }

            int[] counts = new int[10];
            for (int i = 0; i < counts.Length; i++)
            {
                counts[i] = c.GetInt32(i);
            }

            float[] figures = new float[15];
            for (int i = 0; i < figures.Length; i++)
            {
                figures[i] = c.GetSingle(10 + i);
            }

            p.MissionStats.Add(new Igi2MissionStats(counts, figures));
        });
    }

    private void RegisterGfxOptions(NativeRegistry natives)
    {
        GfxSetter(natives, "GfxOptions_SetStencilShadows", (g, c) => g.StencilShadows = c.GetInt32(0) != 0);
        GfxSetter(natives, "GfxOptions_SetTextureCompression", (g, c) => g.TextureCompression = c.GetInt32(0) != 0);
        GfxSetter(natives, "GfxOptions_SetHighQualityForests", (g, c) => g.HighQualityForests = c.GetInt32(0) != 0);
        GfxSetter(natives, "GfxOptions_SetWindOnForests", (g, c) => g.WindOnForests = c.GetInt32(0) != 0);
        GfxSetter(natives, "GfxOptions_SetLensFlares", (g, c) => g.LensFlares = c.GetInt32(0) != 0);
        GfxSetter(natives, "GfxOptions_SetBulletHitEffects", (g, c) => g.BulletHitEffects = c.GetInt32(0) != 0);
        GfxSetter(natives, "GfxOptions_SetTextureDetailFromScript", (g, c) => g.TextureDetail = c.GetInt32(0));
        GfxSetter(natives, "GfxOptions_SetModelDetail", (g, c) => g.ModelDetail = c.GetSingle(0));
        GfxSetter(natives, "GfxOptions_SetWaterQualityLevel", (g, c) => g.WaterQuality = c.GetInt32(0));
        GfxSetter(natives, "GfxOptions_SetTerrainDetailLevel", (g, c) => g.TerrainDetail = Math.Clamp(c.GetSingle(0), 0f, 1f));
        natives.RegisterFunction("GfxOptions_SetDetailLevelFromConfigFile", c =>
        {
            // GFXOPTIONS_DETAIL_HIGH below nought, the level itself up to three, custom above.
            int level = c.GetInt32(0);
            GfxDetailLevel = level < 0 ? Igi2GfxDetail.High : level <= 3 ? level : Igi2GfxDetail.Custom;
            return Done();
        }, 1);
        Profile(natives, "GfxOptions_ConfigSetAntialiasingMode", 1, (p, c) => p.AntialiasingMode = c.GetInt32(0));
        Profile(natives, "GfxOptions_ConfigSetTextureFilteringMode", 1, (p, c) => p.TextureFilteringMode = c.GetInt32(0));
        Profile(natives, "GfxOptions_ApplyCurrentOptionsToLastPlayerProfile", 0, (p, _) =>
        {
            p.GfxDetailLevel = GfxDetailLevel;
            p.Gfx = Gfx.Copy();
        });
    }

    private void RegisterSettings(NativeRegistry natives, Scripting.Igi2ScriptFunctions? functions)
    {
        void Fn(string name, Scripting.Igi2ScriptFunction function, int arguments) =>
            Scripting.Igi2ScriptCall.Register(natives, functions, name, function, arguments);

        Fn("Config_GetActiveDifficultyLevel", _ => QvmValue.FromInt32(Active?.Difficulty ?? 0), 0);
        Fn("Config_SetActiveDifficultyLevel", c => Set(p => p.Difficulty = c.GetInt32(0)), 1);
        Fn("Config_GameOptionsInputGetInvertMouse", _ => QvmValue.FromInt32(Active?.InvertMouse == true ? 1 : 0), 0);
        Fn("Config_GameOptionsInputSetInvertMouse", c => Set(p => p.InvertMouse = c.GetInt32(0) != 0), 1);
        Fn("Config_GameOptionsInputGetMouseSensitivity", _ => QvmValue.FromSingle(Active?.MouseSensitivity ?? 0f), 0);
        Fn("Config_GameOptionsInputSetMouseSensitivity", c => Set(p => p.MouseSensitivity = c.GetSingle(0)), 1);
        Fn("Config_GameOptionsGraphicsGetThickCrosshair", _ => QvmValue.FromInt32(Active?.ThickCrossHair == true ? 1 : 0), 0);
        Fn("Config_GameOptionsGraphicsSetThickCrosshair", c => Set(p => p.ThickCrossHair = c.GetInt32(0) != 0), 1);
        Fn("Config_GraphicOptionsGetGamma", _ => QvmValue.FromSingle(Active?.Gamma ?? 0f), 0);
        Fn("Config_GraphicOptionsSetGamma", c => Set(p => p.Gamma = c.GetSingle(0)), 1);
        Fn("Config_GraphicOptionsGetCrossHairAlpha", _ => QvmValue.FromSingle(Active?.CrossHairAlpha ?? 0f), 0);
        Fn("Config_GraphicOptionsSetCrossHairAlpha", c => Set(p => p.CrossHairAlpha = c.GetSingle(0)), 1);
        Fn("Config_GraphicOptionsGetCrossHairColourR", _ => QvmValue.FromSingle(Active?.CrossHairColour.R ?? 0f), 0);
        Fn("Config_GraphicOptionsGetCrossHairColourG", _ => QvmValue.FromSingle(Active?.CrossHairColour.G ?? 0f), 0);
        Fn("Config_GraphicOptionsGetCrossHairColourB", _ => QvmValue.FromSingle(Active?.CrossHairColour.B ?? 0f), 0);
        Fn("Config_GraphicOptionsSetCrossHairColourR", c => Set(p => p.CrossHairColour = p.CrossHairColour with { R = c.GetSingle(0) }), 1);
        Fn("Config_GraphicOptionsSetCrossHairColourG", c => Set(p => p.CrossHairColour = p.CrossHairColour with { G = c.GetSingle(0) }), 1);
        Fn("Config_GraphicOptionsSetCrossHairColourB", c => Set(p => p.CrossHairColour = p.CrossHairColour with { B = c.GetSingle(0) }), 1);
        Fn("Config_SoundOptionsGetMusicVolume", _ => QvmValue.FromSingle(Active?.Music.Volume ?? 0f), 0);
        Fn("Config_SoundOptionsSetMusicVolume", c => Set(p => p.Music = p.Music with { Volume = c.GetSingle(0) }), 1);
        Fn("Config_SoundOptionsGetSoundsEffectsVolume", _ => QvmValue.FromSingle(Active?.SoundEffects.Volume ?? 0f), 0);
        Fn("Config_SoundOptionsSetSoundsEffectsVolume", c => Set(p => p.SoundEffects = p.SoundEffects with { Volume = c.GetSingle(0) }), 1);
        Fn("Config_GetActiveSpeakerSetup", _ => QvmValue.FromInt32(Active?.SpeakerSetup ?? 0), 0);
        Fn("Config_SetActiveSpeakerSetup", c => Set(p => p.SpeakerSetup = c.GetInt32(0)), 1);
        Fn("Config_GetActive3DProvider", _ => QvmValue.FromInt32(Active?.Sound3DProvider ?? 0), 0);
        Fn("Config_SetActive3DProvider", c => Set(p => p.Sound3DProvider = c.GetInt32(0)), 1);
        Fn("Config_SaveConfig", _ =>
        {
            Save?.Invoke(this);
            return Done();
        }, 0);
    }

    private QvmValue Set(Action<Igi2PlayerProfile> change)
    {
        if (Active is { } profile)
        {
            change(profile);
        }

        return Done();
    }

    /// <summary>A setter of the profile the file last began; with none yet, the original writes before its first profile, and here nothing is written.</summary>
    private void Profile(NativeRegistry natives, string name, int arguments, Action<Igi2PlayerProfile, QvmCallContext> set) =>
        natives.RegisterFunction(name, c =>
        {
            if (Last is { } profile)
            {
                set(profile, c);
            }

            return Done();
        }, arguments);

    private void GfxSetter(NativeRegistry natives, string name, Action<Igi2GfxOptions, QvmCallContext> set) =>
        natives.RegisterFunction(name, c =>
        {
            set(Gfx, c);
            return Done();
        }, 1);
}

/// <summary>The graphics detail levels, <c>GFXOPTIONS_DETAIL_*</c>.</summary>
public static class Igi2GfxDetail
{
    /// <summary>The high level.</summary>
    public const int High = 0;

    /// <summary>A level of the player's own settings.</summary>
    public const int Custom = 3;
}
