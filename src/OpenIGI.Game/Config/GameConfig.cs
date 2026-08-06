namespace OpenIGI.Game.Config;

/// <summary>
/// Everything the configuration screens read and write: the profile list and the few global settings.
/// </summary>
/// <remarks>
/// <para>
/// The original holds this as one block of statics — the profile count at <c>0xBC2380</c>, the active
/// index at <c>0xBC2384</c>, and sixteen 1380-byte records from <c>0xBC2388</c>. The save file is
/// <c>config.qvm</c>, a compiled script whose whole body is calls into the <c>GO*</c> loaders; see
/// <see cref="ConfigScript"/>.
/// </para>
/// <para>
/// Only the content-control password is global. Everything else is per profile, including the
/// graphics and sound settings.
/// </para>
/// </remarks>
public sealed class GameConfig
{
    /// <summary>The most profiles the original will hold.</summary>
    /// <remarks><c>Config_CreateNewPlayerProfile</c> refuses at sixteen.</remarks>
    public const int MaxProfiles = 16;

    /// <summary>The name the default profile is created with.</summary>
    /// <remarks><c>Config_ResetToDefaults</c> (<c>0x405300</c>) writes it literally.</remarks>
    public const string DefaultProfileName = "Joe";

    private readonly List<PlayerProfile> _profiles = [];
    private int _activeIndex;

    /// <summary>Gets the profiles, in the order the save file lists them.</summary>
    public IReadOnlyList<PlayerProfile> Profiles => _profiles;

    /// <summary>Gets or sets the profile the menus are acting on.</summary>
    /// <remarks>Clamped rather than validated, which is what indexing the original's array amounts to.</remarks>
    public int ActiveIndex
    {
        get => _activeIndex;
        set => _activeIndex = _profiles.Count == 0 ? 0 : Math.Clamp(value, 0, _profiles.Count - 1);
    }

    /// <summary>Gets the profile currently being edited, or <see langword="null"/> when there are none.</summary>
    /// <remarks>This is <c>Config_GetActiveProfile</c> (<c>0x406220</c>).</remarks>
    public PlayerProfile? Active =>
        _activeIndex >= 0 && _activeIndex < _profiles.Count ? _profiles[_activeIndex] : null;

    /// <summary>Gets or sets the content-control password.</summary>
    /// <remarks>
    /// <c>GOContentControlPW</c>, and the one setting that is <em>not</em> per profile — it gates the
    /// blood option for the whole installation. An empty string means content control is off. The
    /// original stores it in the clear, and so does this: it is a parental lock on a menu, not a
    /// credential, and hashing it would make the two save files mutually unreadable.
    /// </remarks>
    public string ContentControlPassword { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether this is the German build.</summary>
    /// <remarks>
    /// <c>Config_IsGermany</c>. In the original it is decided at start-up from the machine's locale,
    /// and it forces difficulty 5 and blood off. The menu reads it to hide the blood toggle.
    /// </remarks>
    public bool IsGermany { get; set; }

    /// <summary>Adds a profile.</summary>
    /// <param name="profile">The profile to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is <see langword="null"/>.</exception>
    public void Add(PlayerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profiles.Add(profile);
    }

    /// <summary>Creates a profile, as <c>Config_CreateNewPlayerProfile</c> (<c>0x4050A0</c>) does.</summary>
    /// <param name="name">The name, which is trimmed of trailing spaces and tabs.</param>
    /// <param name="difficulty">The difficulty constant.</param>
    /// <returns><see langword="false"/> when the list is full, the name is empty, or the name is taken.</returns>
    /// <remarks>
    /// The new profile is a copy of the <em>active</em> one, so it inherits that player's settings and
    /// key bindings rather than the defaults, and then becomes active itself. The first profile in an
    /// empty list is the exception: there is nothing to copy, so it takes the defaults.
    /// </remarks>
    public bool CreateProfile(string? name, int difficulty)
    {
        if (_profiles.Count >= MaxProfiles)
        {
            return false;
        }

        string trimmed = (name ?? string.Empty).TrimEnd(' ', '\t');
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (_profiles.Exists(p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        PlayerProfile created = Active?.Clone() ?? PlayerProfile.CreateDefault();
        created.Name = Truncate(trimmed);
        created.Difficulty = difficulty;

        _profiles.Add(created);
        _activeIndex = _profiles.Count - 1;
        return true;
    }

    /// <summary>Removes a profile.</summary>
    /// <param name="index">The profile to remove.</param>
    /// <returns><see langword="false"/> when there is no such profile.</returns>
    public bool DeleteProfile(int index)
    {
        if (index < 0 || index >= _profiles.Count)
        {
            return false;
        }

        _profiles.RemoveAt(index);
        _activeIndex = _profiles.Count == 0 ? 0 : Math.Clamp(_activeIndex, 0, _profiles.Count - 1);
        return true;
    }

    /// <summary>Repairs profiles produced by older builds that omitted their default bindings.</summary>
    public void RestoreMissingProfileBindings()
    {
        foreach (PlayerProfile profile in _profiles)
        {
            profile.RestoreDefaultBindingsWhenEmpty();
        }
    }

    /// <summary>Builds the configuration the original starts from when there is no save file.</summary>
    /// <returns>A configuration holding one default profile.</returns>
    /// <remarks>
    /// <c>Config_ResetToDefaults</c> (<c>0x405300</c>): one profile named Joe, difficulty 1, with
    /// <c>GOActiveMission</c> initialized to mission 1.
    /// </remarks>
    public static GameConfig CreateDefault()
    {
        GameConfig config = new();
        PlayerProfile profile = PlayerProfile.CreateDefault();
        profile.Name = DefaultProfileName;
        profile.Difficulty = 1;
        config.Add(profile);
        return config;
    }

    /// <summary>Truncates a name to the original's buffer.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The name, at most <see cref="PlayerProfile.NameCapacity"/> minus one characters.</returns>
    public static string Truncate(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Length < PlayerProfile.NameCapacity ? name : name[..(PlayerProfile.NameCapacity - 1)];
    }
}
