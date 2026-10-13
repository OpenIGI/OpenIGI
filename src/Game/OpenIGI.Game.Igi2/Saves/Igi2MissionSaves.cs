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
                list.Add(new TitleSaveSlot(slot, info.IsValid, info.IsValid ? Igi2SaveSlots.MissionTime(info.Ticks) : string.Empty));
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
        return _slots.Save(whose, slot, _level.Root, _player.MissionTime);
    }

    /// <inheritdoc/>
    /// <remarks>A load that succeeds adds one to the games loaded, <c>nGameLoads</c>, after the save's own count is put back.</remarks>
    public bool Load(int slot)
    {
        if (!_slots.Load(_owner(), slot, _level.Root, _level.Natives, _level.Functions).IsValid)
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
}
