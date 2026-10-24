namespace OpenIGI.Presentation.Hud.Remastered;

/// <summary>The parts of the display a frame shows.</summary>
[Flags]
public enum HudParts
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Health, stance and how visible the player is.</summary>
    Status = 1 << 0,

    /// <summary>The weapon in hand and its rounds.</summary>
    Weapon = 1 << 1,

    /// <summary>The weapons carried.</summary>
    Inventory = 1 << 2,

    /// <summary>What may be done where the player stands.</summary>
    Actions = 1 << 3,

    /// <summary>The messages.</summary>
    Messages = 1 << 4,

    /// <summary>The aiming mark.</summary>
    Crosshair = 1 << 5,

    /// <summary>The mission's clock.</summary>
    Timer = 1 << 6,

    /// <summary>A timed action's bar.</summary>
    Progress = 1 << 7,

    /// <summary>The objective being worked on.</summary>
    Objective = 1 << 8,

    /// <summary>The heading strip.</summary>
    Compass = 1 << 9,

    /// <summary>Everything.</summary>
    All = Status | Weapon | Inventory | Actions | Messages | Crosshair | Timer | Progress | Objective | Compass,
}

/// <summary>How the player holds itself, as the status display shows it.</summary>
public enum HudStance
{
    /// <summary>Not shown.</summary>
    None,

    /// <summary>Standing.</summary>
    Standing,

    /// <summary>Running.</summary>
    Running,

    /// <summary>Crouching.</summary>
    Crouching,

    /// <summary>Lying flat.</summary>
    Prone,

    /// <summary>Off the ground.</summary>
    Flying,
}

/// <summary>How a weapon's loaded rounds are counted on the display.</summary>
public enum HudGauge
{
    /// <summary>Not counted: a knife, binoculars.</summary>
    None,

    /// <summary>A magazine of rounds.</summary>
    Clip,

    /// <summary>A few barrels or tubes, each loaded or not.</summary>
    Barrel,

    /// <summary>Shells loaded one by one.</summary>
    Shell,

    /// <summary>One rocket or grenade at a time.</summary>
    Rocket,
}

/// <summary>What the weapon in hand is doing.</summary>
public enum HudWeaponState
{
    /// <summary>Ready to fire.</summary>
    Ready,

    /// <summary>Being reloaded.</summary>
    Reloading,

    /// <summary>Being put away or drawn.</summary>
    Switching,

    /// <summary>Out of rounds.</summary>
    Empty,
}

/// <summary>A picture the title supplies by name, from its own sprites (<see cref="IHudIconSource"/>).</summary>
/// <param name="Name">The title's sprite name; empty for none.</param>
public readonly record struct HudIcon(string Name)
{
    /// <summary>Gets no picture.</summary>
    public static HudIcon None { get; } = new(string.Empty);

    /// <summary>Gets a value indicating whether there is a picture.</summary>
    public bool IsSome => !string.IsNullOrEmpty(Name);
}

/// <summary>The weapon in hand.</summary>
/// <param name="Key">What identifies it from frame to frame, so a change of weapon can be seen.</param>
/// <param name="Name">Its name, as the title shows it.</param>
/// <param name="FireMode">Its fire mode, or empty.</param>
/// <param name="Icon">Its picture.</param>
/// <param name="Magazine">The rounds loaded, or <see langword="null"/> where it loads none.</param>
/// <param name="Capacity">How many it loads at most.</param>
/// <param name="Reserve">The rounds carried for it beyond those loaded, or <see langword="null"/> where it is not counted.</param>
/// <param name="Secondary">Its second weapon's rounds, such as an under-barrel launcher's, or <see langword="null"/>.</param>
/// <param name="Gauge">How its loaded rounds are counted.</param>
/// <param name="State">What it is doing.</param>
public readonly record struct HudWeaponView(
    string Key,
    string Name,
    string FireMode,
    HudIcon Icon,
    int? Magazine,
    int Capacity,
    int? Reserve,
    int? Secondary,
    HudGauge Gauge,
    HudWeaponState State);

/// <summary>A weapon carried, in the inventory's column for its kind.</summary>
/// <param name="Column">The column it stands in; weapons of one column stack.</param>
/// <param name="KeyLabel">The key that selects the column, or empty.</param>
/// <param name="Name">Its name, short.</param>
/// <param name="Icon">Its picture, shown at the column's head.</param>
/// <param name="Count">The rounds carried for it as the title counts them, or empty for none.</param>
/// <param name="Selected">Whether it is the one in hand.</param>
/// <param name="Head">Whether it heads its column; the others below show their names only.</param>
public readonly record struct HudSlotView(int Column, string KeyLabel, string Name, HudIcon Icon, string Count, bool Selected, bool Head);

/// <summary>One thing that may be done, on the action row.</summary>
/// <param name="Icon">Its badge.</param>
/// <param name="Second">A second picture under it, such as the weapon a swap gives up, or none.</param>
/// <param name="SecondOverlay">The badge laid over the second picture, or none.</param>
/// <param name="Key">What identifies it from frame to frame.</param>
public readonly record struct HudActionView(HudIcon Icon, HudIcon Second, HudIcon SecondOverlay, string Key);

/// <summary>A message on the display.</summary>
/// <param name="Text">What it says, as far as it has printed.</param>
/// <param name="Opacity">How far it has faded in or out, nought to one.</param>
/// <param name="Key">What identifies it from frame to frame.</param>
public readonly record struct HudMessageView(string Text, float Opacity, int Key);

/// <summary>Someone who has the player in sight: which way they are, how close they are to sure, and where to mark them.</summary>
/// <param name="Bearing">Where they are from the view's facing, in radians clockwise: nought ahead, π behind.</param>
/// <param name="Awareness">How far they are to seeing the player, nought (not at all) to one (seen).</param>
/// <param name="Mark">The point over their head the eye is drawn at, in world units, or <see langword="null"/> for none.</param>
public readonly record struct HudThreat(float Bearing, float Awareness, OpenIGI.Core.Numerics.Vector3d? Mark = null);

/// <summary>One weapon on the weapon wheel.</summary>
/// <param name="Icon">Its picture.</param>
/// <param name="Name">Its name, short.</param>
/// <param name="Count">Its rounds as the title counts them, or empty.</param>
public readonly record struct HudWheelEntry(HudIcon Icon, string Name, string Count);

/// <summary>The weapon wheel as the display draws it: its weapons, which is pointed at and which is in hand, and how far it is open.</summary>
public sealed class HudWheelView
{
    private readonly List<HudWheelEntry> _entries = [];

    /// <summary>Gets the weapons round the ring, the first straight up and the rest clockwise.</summary>
    public IReadOnlyList<HudWheelEntry> Entries => _entries;

    /// <summary>Gets or sets how far the wheel is open, nought (shut, not drawn) to one.</summary>
    public float Openness { get; set; }

    /// <summary>Gets or sets the weapon pointed at, or -1.</summary>
    public int Hovered { get; set; } = -1;

    /// <summary>Gets or sets the weapon in hand, or -1.</summary>
    public int Current { get; set; } = -1;

    /// <summary>Gets or sets the way the pointer points, in radians clockwise from straight up, or <see langword="null"/> for none.</summary>
    public float? PointerAngle { get; set; }

    /// <summary>Adds a weapon round the ring.</summary>
    /// <param name="entry">The weapon.</param>
    public void Add(in HudWheelEntry entry) => _entries.Add(entry);

    /// <summary>Empties it, shut, for a new frame.</summary>
    public void Reset()
    {
        _entries.Clear();
        Openness = 0f;
        Hovered = -1;
        Current = -1;
        PointerAngle = null;
    }

    /// <summary>Takes the wheel's state from the player's: how open it is, which is pointed at and which is in hand.</summary>
    /// <param name="wheel">The player's wheel.</param>
    public void From(OpenIGI.Game.Player.WeaponWheel wheel)
    {
        ArgumentNullException.ThrowIfNull(wheel);
        Openness = wheel.Openness;
        Hovered = wheel.Hovered;
        Current = wheel.Current;
        PointerAngle = wheel.PointerAngle;
    }
}

/// <summary>Where the player was last hit from, and how hard: what washes the display's corners.</summary>
/// <param name="TopLeft">The top left corner's wash, nought to one.</param>
/// <param name="TopRight">The top right corner's.</param>
/// <param name="BottomLeft">The bottom left corner's.</param>
/// <param name="BottomRight">The bottom right corner's.</param>
public readonly record struct HudDamage(float TopLeft, float TopRight, float BottomLeft, float BottomRight)
{
    /// <summary>Gets the strongest corner.</summary>
    public float Peak => MathF.Max(MathF.Max(TopLeft, TopRight), MathF.Max(BottomLeft, BottomRight));
}

/// <summary>
/// Everything the remastered display shows for one frame, in no one game's terms: each title fills it from its own player, and
/// the shared presenter draws it in that title's theme.
/// </summary>
/// <remarks>
/// One instance is kept and refilled each frame (<see cref="Reset"/>), so building it allocates nothing once its lists have
/// grown to the frame's size.
/// </remarks>
public sealed class HudModel
{
    private readonly List<(OpenIGI.Core.Numerics.Vector3d At, float Strength)> _glints = [];

    private readonly List<HudSlotView> _slots = [];
    private readonly List<HudActionView> _actions = [];
    private readonly List<HudMessageView> _messages = [];
    private readonly List<HudThreat> _threats = [];

    /// <summary>Gets or sets the parts shown.</summary>
    public HudParts Show { get; set; }

    /// <summary>Gets or sets the player's health, nought to one.</summary>
    public float Health { get; set; } = 1f;

    /// <summary>Gets or sets how the player holds itself.</summary>
    public HudStance Stance { get; set; }

    /// <summary>Gets or sets how visible the player is, nought to one, or <see langword="null"/> where the title shows none.</summary>
    public float? Visibility { get; set; }

    /// <summary>Gets or sets the visibility past which the meter warns, nought to one.</summary>
    public float VisibilityAlert { get; set; } = 0.75f;

    /// <summary>Gets or sets the weapon in hand, or <see langword="null"/> for none.</summary>
    public HudWeaponView? Weapon { get; set; }

    /// <summary>Gets the weapons carried, column by column.</summary>
    public IReadOnlyList<HudSlotView> Slots => _slots;

    /// <summary>Gets what may be done, the first taken by the activate key.</summary>
    public IReadOnlyList<HudActionView> Actions => _actions;

    /// <summary>Gets the messages, oldest first.</summary>
    public IReadOnlyList<HudMessageView> Messages => _messages;

    /// <summary>Gets or sets a timed action's progress, nought to one, or <see langword="null"/> for none.</summary>
    public float? Progress { get; set; }

    /// <summary>Gets or sets the mission's clock in seconds left, or <see langword="null"/> for none.</summary>
    public int? TimerSeconds { get; set; }

    /// <summary>Gets or sets where the player faces, in radians clockwise from north, or <see langword="null"/>.</summary>
    public float? HeadingRadians { get; set; }

    /// <summary>Gets or sets the objective being worked on, or <see langword="null"/>.</summary>
    public string? Objective { get; set; }

    /// <summary>Gets or sets where the objective being worked on is, in the level's units, or <see langword="null"/> where it has no place.</summary>
    public OpenIGI.Core.Numerics.Vector3d? ObjectivePosition { get; set; }

    /// <summary>Gets or sets how far the objective is from the player, in metres.</summary>
    public float ObjectiveDistanceMetres { get; set; }

    /// <summary>Gets or sets the wash in each corner from a hit.</summary>
    public HudDamage Damage { get; set; }

    /// <summary>Gets or sets where the view looks up or down, in radians, up positive; with the heading, how fast it turns.</summary>
    public float? PitchRadians { get; set; }

    /// <summary>Gets or sets how far the soldiers are onto the player, as the title's AI tells it.</summary>
    public OpenIGI.Game.Ai.PlayerAlertStatus AlertStatus { get; set; }

    /// <summary>Gets the scopes of snipers aiming at the player: where each is in the level, and how bright its glint is.</summary>
    public IReadOnlyList<(OpenIGI.Core.Numerics.Vector3d At, float Strength)> Glints => _glints;

    /// <summary>Adds a sniper's scope.</summary>
    /// <param name="at">Where it is, in the level's units.</param>
    /// <param name="strength">How bright its glint is, nought to one.</param>
    public void AddGlint(OpenIGI.Core.Numerics.Vector3d at, float strength) => _glints.Add((at, strength));

    /// <summary>Gets the weapon wheel, shut unless a title fills it.</summary>
    public HudWheelView Wheel { get; } = new();

    /// <summary>Gets those noticing the player.</summary>
    public IReadOnlyList<HudThreat> Threats => _threats;

    /// <summary>Gets the most any of them has noticed, nought to one.</summary>
    public float ThreatLevel
    {
        get
        {
            float most = 0f;
            foreach (HudThreat threat in _threats)
            {
                most = MathF.Max(most, threat.Awareness);
            }

            return most;
        }
    }

    /// <summary>Empties it for a new frame.</summary>
    public void Reset()
    {
        Show = HudParts.None;
        Health = 1f;
        Stance = HudStance.None;
        Visibility = null;
        VisibilityAlert = 0.75f;
        Weapon = null;
        _slots.Clear();
        _actions.Clear();
        _messages.Clear();
        Progress = null;
        TimerSeconds = null;
        HeadingRadians = null;
        Objective = null;
        ObjectivePosition = null;
        ObjectiveDistanceMetres = 0f;
        Damage = default;
        PitchRadians = null;
        _threats.Clear();
        _glints.Clear();
        AlertStatus = OpenIGI.Game.Ai.PlayerAlertStatus.None;
        Wheel.Reset();
    }

    /// <summary>Adds a weapon carried.</summary>
    /// <param name="slot">The weapon.</param>
    public void AddSlot(in HudSlotView slot) => _slots.Add(slot);

    /// <summary>Adds a thing that may be done.</summary>
    /// <param name="action">The thing.</param>
    public void AddAction(in HudActionView action) => _actions.Add(action);

    /// <summary>Adds someone noticing the player.</summary>
    /// <param name="threat">Them.</param>
    public void AddThreat(in HudThreat threat) => _threats.Add(threat);

    /// <summary>Adds a message.</summary>
    /// <param name="message">The message.</param>
    public void AddMessage(in HudMessageView message) => _messages.Add(message);
}
