using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Saves;

/// <summary>
/// A running mission's save games as the host's pause menu offers them: the slots the difficulty allows
/// (<c>Game_GetAvailableSaveGameSlots</c>), the saves the computer's battery has left (<c>HumanPlayer_GetBatteryLevel</c>: five a mission
/// on easy, three on normal, none on hard), a save that uses one of them (<c>HumanPlayer_BeginSaveToSlot</c>, then
/// <c>Game_SaveToSlot</c>), and a load that counts itself in the mission's figures (<c>HumanPlayer_RunDelayedHandler</c>, then
/// <c>Game_LoadFromSlot</c>).
/// </summary>
/// <param name="slots">Where the slots are kept.</param>
/// <param name="owner">Whose saves these are: the profile, its difficulty and the mission.</param>
/// <param name="level">The running level.</param>
/// <param name="player">The player.</param>
public sealed class Igi2MissionSaves(Igi2SaveSlots slots, Func<Igi2SaveOwner> owner, Igi2LevelRun level, Igi2HumanPlayer player) : ITitleSaveGames
{
    private readonly Igi2SaveSlots _slots = slots ?? throw new ArgumentNullException(nameof(slots));
    private readonly Func<Igi2SaveOwner> _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    private readonly Igi2LevelRun _level = level ?? throw new ArgumentNullException(nameof(level));
    private readonly Igi2HumanPlayer _player = player ?? throw new ArgumentNullException(nameof(player));

    /// <summary>The saves a mission allows at a difficulty, the battery's full charge: five, three, or none on hard.</summary>
    /// <param name="difficulty">The difficulty.</param>
    /// <returns>The saves.</returns>
    public static int SavesAllowed(int difficulty) => difficulty switch
    {
        0 => 5,
        1 => 3,
        _ => 0,
    };

    /// <summary>Gets or sets the level's panes of glass, which a save keeps and a load puts back.</summary>
    public Interaction.Igi2GlassContainer? Glass { get; set; }

    /// <summary>Gets where the slots are kept.</summary>
    public Igi2SaveSlots SlotStore => _slots;

    /// <summary>Gets the mods that change play this session is played with, which each save records beside itself.</summary>
    /// <remarks>
    /// Kept in a file of their own beside the slot, <c>.mods.json</c>, so the save stays the retail format a retail game reads.
    /// </remarks>
    public IReadOnlyList<Core.Content.ModReference> PlayedMods { get; init; } = [];

    /// <summary>Gets whose saves these are now.</summary>
    public Igi2SaveOwner Owner => _owner();

    /// <inheritdoc/>
    public IReadOnlyList<TitleSaveSlot> Slots
    {
        get
        {
            Igi2SaveOwner whose = _owner();
            List<TitleSaveSlot> list = [];
            for (int slot = 0; slot < Igi2SaveSlots.AvailableSlots(whose.Difficulty); slot++)
            {
                Igi2SaveInfo info = _slots.Check(whose, slot);
                IReadOnlyList<Core.Content.ModReference> missing = info.IsValid
                    ? Core.Content.ModReference.Missing(Core.Content.ModReference.ReadList(ModsFile(whose, slot)), PlayedMods)
                    : [];
                list.Add(new TitleSaveSlot(
                    slot,
                    info.IsValid,
                    info.IsValid ? Igi2SaveSlots.MissionTime(info.Ticks) : string.Empty,
                    missing.Count > 0 ? missing : null));
            }

            return list;
        }
    }

    /// <inheritdoc/>
    public int SavesLeft => Math.Max(SavesAllowed(_owner().Difficulty) - _player.SavesUsed, 0);

    /// <inheritdoc/>
    /// <remarks>
    /// A save uses one of the battery's charges before it is written, so the count it keeps includes itself; with none left, or to a
    /// slot the difficulty does not allow, nothing is saved.
    /// </remarks>
    public bool Save(int slot)
    {
        Igi2SaveOwner whose = _owner();
        if (SavesLeft <= 0 || slot < 0 || slot >= Igi2SaveSlots.AvailableSlots(whose.Difficulty))
        {
            return false;
        }

        _player.SavesUsed++;
        return Recorded(whose, slot, _slots.Save(whose, slot, _level.Root, _player.MissionTime, Glass));
    }

    /// <summary>
    /// Writes a slot the player's computer has already counted against the battery, <c>Game_SaveToSlot</c> as the player's delayed
    /// run calls it once <c>HumanPlayer_FinishSaveToSlot</c> has named the slot.
    /// </summary>
    /// <param name="slot">The slot.</param>
    /// <returns><see langword="true"/> once written.</returns>
    public bool Write(int slot) =>
        slot >= 0 && slot < Igi2SaveSlots.MenuSlots && Recorded(_owner(), slot, _slots.Save(_owner(), slot, _level.Root, _player.MissionTime, Glass));

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
        Igi2SaveOwner whose = _owner();
        Igi2SaveInfo info = _slots.Check(whose, slot);
        if (!info.IsValid)
        {
            return (false, string.Empty, string.Empty);
        }

        DateTime written = File.GetLastWriteTime(_slots.PathOf(Igi2SaveSlots.SlotFileName(whose, slot)));
        string when = string.Create(System.Globalization.CultureInfo.CurrentCulture, $"({written:d} {written:T})");
        return (true, $"{translate(Igi2SaveSlots.MissionTimeKey)}: {Igi2SaveSlots.MissionTime(info.Ticks)}", when);
    }

    /// <summary>The battery's charge as a share, <c>HumanPlayer_GetBatteryLevel</c>: the saves left of the difficulty's, none on hard.</summary>
    public float BatteryLevel
    {
        get
        {
            int allowed = SavesAllowed(_owner().Difficulty);
            return allowed <= 0 ? 0f : Math.Max((allowed - _player.SavesUsed) / (float)allowed, 0f);
        }
    }

    /// <inheritdoc/>
    /// <remarks>A load that succeeds adds one to the games loaded, <c>nGameLoads</c>, after the save's own count is put back.</remarks>
    public bool Load(int slot)
    {
        if (!_slots.Load(_owner(), slot, _level.Root, _level.Natives, _level.Functions, Glass).IsValid)
        {
            return false;
        }

        _player.GameLoads++;
        return true;
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
        return task.CarryOverWeapons && _slots.LoadCarryOver(_owner(), _level.Root, _level.Natives, _level.Functions).IsValid;
    }

    /// <summary>Leaves the player's weapons for the mission that follows, <c>Game_SaveCarryover</c>, unless the player is dead.</summary>
    /// <param name="task">The player's level task.</param>
    /// <param name="nextMission">The mission that follows.</param>
    /// <returns><see langword="true"/> once written.</returns>
    public bool SaveCarryOver(GameTask task, int nextMission) =>
        !_player.IsDead && _slots.SaveCarryOver(_owner() with { Mission = nextMission }, task);

    // The file beside a slot that names the mods it was saved with.
    private string ModsFile(Igi2SaveOwner whose, int slot) => _slots.PathOf(Igi2SaveSlots.SlotFileName(whose, slot)) + ".mods.json";

    // Records the mods a save was just written with, beside it.
    private bool Recorded(Igi2SaveOwner whose, int slot, bool written)
    {
        if (written)
        {
            try
            {
                Core.Content.ModReference.WriteList(ModsFile(whose, slot), PlayedMods);
            }
            catch (IOException)
            {
                // The save itself is written; only the note of its mods is not, and the game plays on.
            }
            catch (UnauthorizedAccessException)
            {
                // As above.
            }
        }

        return written;
    }
}
