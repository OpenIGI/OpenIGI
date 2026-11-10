using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Player;

namespace OpenIGI.Game.Igi2.Saves;

/// <summary>
/// A running I.G.I. 2 mission's save games: the shared slots, limits, save and load (<see cref="MissionSaves"/>) over the level's
/// tasks, its panes of glass and the weapons lying in it; the lines the computer's save screen names a slot by; and the weapons a
/// mission leaves the next.
/// </summary>
public sealed class Igi2MissionSaves : MissionSaves
{
    private readonly Igi2SaveSlots _slots;
    private readonly Level _level;
    private readonly Igi2HumanPlayer _player;

    /// <summary>Initializes a new instance of the <see cref="Igi2MissionSaves"/> class.</summary>
    /// <param name="slots">Where the slots are kept.</param>
    /// <param name="owner">Whose saves these are: the profile, its difficulty and the mission.</param>
    /// <param name="level">The running level.</param>
    /// <param name="player">The player.</param>
    public Igi2MissionSaves(Igi2SaveSlots slots, Func<SaveOwner> owner, Igi2LevelRun level, Igi2HumanPlayer player)
        : this(slots, owner, new Level(level, player), player)
    {
    }

    private Igi2MissionSaves(Igi2SaveSlots slots, Func<SaveOwner> owner, Level level, Igi2HumanPlayer player)
        : base(slots, owner, level)
    {
        _slots = slots;
        _level = level;
        _player = player ?? throw new ArgumentNullException(nameof(player));
    }

    /// <summary>Gets where the slots are kept, with the load list and the carry-overs that are this game's own.</summary>
    public new Igi2SaveSlots SlotStore => _slots;

    /// <summary>Gets or sets the level's panes of glass, which a save keeps and a load puts back.</summary>
    public Interaction.Igi2GlassContainer? Glass
    {
        get => _level.Glass;
        set => _level.Glass = value;
    }

    /// <summary>Gets or sets the level's pickups: the weapons lying in it, which a save keeps and a load makes again.</summary>
    public Pickups.Igi2PickupList? Pickups
    {
        get => _level.Pickups;
        set => _level.Pickups = value;
    }

    /// <summary>Gets or sets what answers for the mines laid in the level, which a save keeps and a load makes again.</summary>
    public Func<Igi2LevelMines?> Mines
    {
        get => _level.Mines;
        set => _level.Mines = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// A slot as the computer's save screen names it (<c>ComputerSaveMenu_NewSaveSlot</c>): its mission time, <c>Mission Time: h:mm:ss</c>
    /// (<c>Game_GetSaveGameInfoString</c>), and when it was written, <c>(date time)</c> (<c>Game_GetSaveGameTimeDateString</c>); an
    /// empty slot neither.
    /// </summary>
    /// <param name="slot">The slot.</param>
    /// <param name="translate">The localized text of a key.</param>
    /// <returns>Whether it holds a save, its time and its date.</returns>
    public (bool IsUsed, string Info, string When) SlotLabel(int slot, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(translate);
        SaveOwner whose = Owner;
        SaveInfo info = _slots.Check(whose, slot);
        if (!info.IsValid)
        {
            return (false, string.Empty, string.Empty);
        }

        DateTime written = _slots.WrittenAt(whose, slot);
        string when = string.Create(System.Globalization.CultureInfo.CurrentCulture, $"({written:d} {written:T})");
        return (true, $"{translate(Igi2SaveSlots.MissionTimeKey)}: {SaveRules.MissionTime(info.Ticks)}", when);
    }

    /// <summary>
    /// Gives the player the weapons the last mission left, <c>Game_LoadCarryOver</c>, when its level asks for them
    /// (<c>Carry over weapons from previous mission</c>).
    /// </summary>
    /// <param name="task">The player's level task.</param>
    /// <returns><see langword="true"/> when weapons were carried over.</returns>
    public bool LoadCarryOver(Igi2HumanPlayerTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return task.CarryOverWeapons && _slots.LoadCarryOver(Owner, _level.Root).IsValid;
    }

    /// <summary>Leaves the player's weapons for the mission that follows, <c>Game_SaveCarryover</c>, unless the player is dead.</summary>
    /// <param name="task">The player's level task.</param>
    /// <param name="nextMission">The mission that follows.</param>
    /// <returns><see langword="true"/> once written.</returns>
    public bool SaveCarryOver(GameTask task, int nextMission) =>
        !_player.IsDead && _slots.SaveCarryOver(Owner with { Mission = nextMission }, task);

    // The running level as the shared save games ask of it.
    private sealed class Level(Igi2LevelRun level, Igi2HumanPlayer player) : IMissionSaveSource
    {
        public Interaction.Igi2GlassContainer? Glass { get; set; }

        public Pickups.Igi2PickupList? Pickups { get; set; }

        public Func<Igi2LevelMines?> Mines { get; set; } = static () => null;

        public GameTask Root => level.Root;

        public int MissionTime => player.MissionTime;

        public int SavesUsed
        {
            get => player.SavesUsed;
            set => player.SavesUsed = value;
        }

        public int GameLoads
        {
            get => player.GameLoads;
            set => player.GameLoads = value;
        }

        // Its tasks, its dropped weapons, its mines and its glass.
        public SaveContext Context => Igi2SaveFormat.Context(Glass, Pickups, level.Root, Mines());

        // Game_LoadFromSlot: every mine and every pickup in the level is deleted, and the load makes again each the save holds.
        public void BeforeLoad()
        {
            Mines()?.DeleteAll();
            Pickups?.DeleteAll();
        }
    }
}
