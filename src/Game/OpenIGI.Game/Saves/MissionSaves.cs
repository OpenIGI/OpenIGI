using OpenIGI.Core.Content;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Saves;

/// <summary>What a running mission gives its save games: the level's tasks, the clock and the counts a save keeps.</summary>
public interface IMissionSaveSource
{
    /// <summary>Gets the task the level's state is written from and read onto.</summary>
    GameTask Root { get; }

    /// <summary>Gets the player's mission time, in ticks, which a save's header holds.</summary>
    int MissionTime { get; }

    /// <summary>Gets or sets the saves made this mission, <c>nSavesUsed</c>: each uses one of the battery's charges.</summary>
    int SavesUsed { get; set; }

    /// <summary>Gets or sets the games loaded this mission, <c>nGameLoads</c>.</summary>
    int GameLoads { get; set; }

    /// <summary>Gets what the level is written and read with, as it stands now.</summary>
    SaveContext Context { get; }

    /// <summary>
    /// Clears what a load makes again, once the slot's header has checked and before its state is read
    /// (<c>Game_LoadFromSlot</c>'s deletes).
    /// </summary>
    void BeforeLoad()
    {
    }
}

/// <summary>
/// A running mission's save games as the host's pause menu offers them: the slots the difficulty allows
/// (<c>Game_GetAvailableSaveGameSlots</c>), the saves the computer's battery has left (<c>HumanPlayer_GetBatteryLevel</c>: five a
/// mission on easy, three on normal, none on hard), a save that uses one of them (<c>HumanPlayer_BeginSaveToSlot</c>, then
/// <c>Game_SaveToSlot</c>), and a load that counts itself in the mission's figures (<c>HumanPlayer_RunDelayedHandler</c>, then
/// <c>Game_LoadFromSlot</c>).
/// </summary>
/// <param name="slots">Where the slots are kept.</param>
/// <param name="owner">Whose saves these are: the profile, its difficulty and the mission.</param>
/// <param name="source">The running mission.</param>
public class MissionSaves(SaveSlots slots, Func<SaveOwner> owner, IMissionSaveSource source) : ITitleSaveGames
{
    private readonly SaveSlots _slots = slots ?? throw new ArgumentNullException(nameof(slots));
    private readonly Func<SaveOwner> _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    private readonly IMissionSaveSource _source = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>Gets where the slots are kept.</summary>
    public SaveSlots SlotStore => _slots;

    /// <summary>Gets whose saves these are now.</summary>
    public SaveOwner Owner => _owner();

    /// <summary>Gets the running mission.</summary>
    protected IMissionSaveSource Source => _source;

    /// <summary>Gets the mods that change play this session is played with, which each save records beside itself.</summary>
    /// <remarks>
    /// Kept in a file of their own beside the slot, <c>.mods.json</c>, so the save stays the format a retail game reads.
    /// </remarks>
    public IReadOnlyList<ModReference> PlayedMods { get; init; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<TitleSaveSlot> Slots
    {
        get
        {
            SaveOwner whose = _owner();
            List<TitleSaveSlot> list = [];
            for (int slot = 0; slot < SaveRules.AvailableSlots(whose.Difficulty); slot++)
            {
                SaveInfo info = _slots.Check(whose, slot);
                IReadOnlyList<ModReference> missing = info.IsValid
                    ? ModReference.Missing(ModReference.ReadList(ModsFile(whose, slot)), PlayedMods)
                    : [];
                list.Add(new TitleSaveSlot(
                    slot,
                    info.IsValid,
                    info.IsValid ? SaveRules.MissionTime(info.Ticks) : string.Empty,
                    missing.Count > 0 ? missing : null));
            }

            return list;
        }
    }

    /// <inheritdoc/>
    public virtual bool LoadRestartsMission => false;

    /// <inheritdoc/>
    public int SavesLeft => Math.Max(SaveRules.SavesAllowed(_owner().Difficulty) - _source.SavesUsed, 0);

    /// <summary>Gets the battery's charge as a share, <c>HumanPlayer_GetBatteryLevel</c>: the saves left of the difficulty's, none on hard.</summary>
    public float BatteryLevel
    {
        get
        {
            int allowed = SaveRules.SavesAllowed(_owner().Difficulty);
            return allowed <= 0 ? 0f : Math.Max((allowed - _source.SavesUsed) / (float)allowed, 0f);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A save uses one of the battery's charges before it is written, so the count it keeps includes itself; with none left, or to a
    /// slot the difficulty does not allow, nothing is saved.
    /// </remarks>
    public bool Save(int slot)
    {
        SaveOwner whose = _owner();
        if (SavesLeft <= 0 || slot < 0 || slot >= SaveRules.AvailableSlots(whose.Difficulty))
        {
            return false;
        }

        _source.SavesUsed++;
        return Recorded(whose, slot, _slots.Save(whose, slot, _source.Root, _source.MissionTime, _source.Context));
    }

    /// <summary>
    /// Writes a slot that has already been counted against the battery, <c>Game_SaveToSlot</c> as the player's delayed run calls it
    /// once <c>HumanPlayer_FinishSaveToSlot</c> has named the slot.
    /// </summary>
    /// <param name="slot">The slot.</param>
    /// <returns><see langword="true"/> once written.</returns>
    public bool Write(int slot)
    {
        SaveOwner whose = _owner();
        return slot >= 0
            && slot <= SaveRules.QuickSave
            && Recorded(whose, slot, _slots.Save(whose, slot, _source.Root, _source.MissionTime, _source.Context));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <c>Game_LoadFromSlot</c>: once the slot's header checks, what a load makes again is cleared, then the state is read.
    /// </para>
    /// <para>A load that succeeds adds one to the games loaded, <c>nGameLoads</c>, after the save's own count is put back.</para>
    /// </remarks>
    public virtual bool Load(int slot)
    {
        SaveOwner whose = _owner();
        if (!_slots.Check(whose, slot).IsValid)
        {
            return false;
        }

        _source.BeforeLoad();
        if (!_slots.Load(whose, slot, _source.Root, _source.Context).IsValid)
        {
            return false;
        }

        _source.GameLoads++;
        return true;
    }

    // The file beside a slot that names the mods it was saved with.
    private string ModsFile(SaveOwner whose, int slot) => _slots.PathOf(_slots.SlotFile(whose, slot)) + ".mods.json";

    // Records the mods a save was just written with, beside it.
    private bool Recorded(SaveOwner whose, int slot, bool written)
    {
        if (written)
        {
            try
            {
                ModReference.WriteList(ModsFile(whose, slot), PlayedMods);
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
