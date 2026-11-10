using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Saves;
using Xunit;

namespace OpenIGI.Game.Tests.Saves;

/// <summary>
/// A mission's save games as both titles keep them (<see cref="MissionSaves"/>): the slots and the saves a difficulty allows a mission, a
/// save that uses one of the mission's, the quick slot, and the newest save a quick load reads.
/// </summary>
public sealed class MissionSavesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "openigi-mission-saves-" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>
    /// Every difficulty has the computer's five slots and the quick save's; easy has five saves a mission, normal three, hard none.
    /// </summary>
    [Fact]
    public void A_difficulty_allows_its_saves()
    {
        Assert.Equal([5, 3, 0], Enumerable.Range(0, 3).Select(SaveRules.SavesAllowed));
        Assert.Equal("1:02:05", SaveRules.MissionTime(30 * 3725));

        (MissionSaves hard, _) = Saves(difficulty: 2);
        Assert.Equal([0, 1, 2, 3, 4, SaveRules.QuickSave], hard.Slots.Select(static s => s.Slot));
        Assert.Equal(0f, hard.BatteryLevel);
        Assert.False(hard.Save(0));
        Assert.False(hard.QuickSave());
    }

    /// <summary>A save uses one of the mission's, is listed with its mission time and when it was written, and is refused to a slot that is not one of the five.</summary>
    [Fact]
    public void A_save_uses_one_of_the_missions()
    {
        (MissionSaves saves, Level level) = Saves(difficulty: 1);
        level.MissionTime = 30 * 61;
        Assert.Equal(3, saves.SavesLeft);
        Assert.True(saves.Save(1));
        Assert.False(saves.Save(SaveRules.MenuSlots));
        Assert.Equal(2, saves.SavesLeft);
        Assert.Equal(2f / 3f, saves.BatteryLevel, 5);
        Assert.Equal(
            [(0, false, string.Empty), (1, true, "0:01:01")],
            saves.Slots.Take(2).Select(static s => (s.Slot, s.IsUsed, s.Description)));
        Assert.StartsWith("(", saves.Slots[1].When, StringComparison.Ordinal);
        Assert.Equal(string.Empty, saves.Slots[0].When);

        level.CanBeSaved = false;
        Assert.False(saves.Save(0));
        Assert.False(saves.QuickSave());
        Assert.Equal(2, saves.SavesLeft);
    }

    /// <summary>
    /// In a multiplayer session nothing is saved, loaded, listed or counted, whatever asks; once the session is over the
    /// saves made before it are there as they were.
    /// </summary>
    [Fact]
    public void A_multiplayer_session_holds_the_save_games_shut()
    {
        (MissionSaves saves, Level level) = Saves(difficulty: 0);
        level.Counter.Value = 1;
        Assert.True(saves.Save(0));
        int used = level.SavesUsed;
        string file = saves.SlotStore.PathOf(saves.SlotStore.SlotFile(saves.Owner, 0));
        DateTime written = File.GetLastWriteTimeUtc(file);
        SaveSession session = new();

        SaveGameLock.Hold();
        try
        {
            level.Counter.Value = 7;
            Assert.False(saves.Save(0));
            Assert.False(saves.Save(1));
            Assert.False(saves.QuickSave());
            Assert.False(saves.Write(2));
            Assert.False(saves.Load(0));
            Assert.False(saves.QuickLoad());
            Assert.DoesNotContain(saves.Slots, static s => s.IsUsed);
            Assert.False(saves.SlotStore.Check(saves.Owner, 0).IsValid);
            Assert.False(saves.SlotStore.TryFindNewest(saves.Owner, out _));
            Assert.False(saves.SlotStore.WriteText("saves/other.save", "x"));
            session.PendingLoadSlot = 0;
            Assert.Equal(-1, session.TakePendingLoadSlot());

            Assert.Equal(7, level.Counter.Value);
            Assert.Equal(used, level.SavesUsed);
            Assert.Equal(0, level.GameLoads);
            Assert.Equal(written, File.GetLastWriteTimeUtc(file));
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(file)!, "*.save"));
        }
        finally
        {
            SaveGameLock.Release();
        }

        Assert.True(saves.Load(0));
        Assert.Equal(1, level.Counter.Value);
    }

    /// <summary>The quick save goes to the sixth slot and counts as a save; the quick load reads the newest of the six.</summary>
    [Fact]
    public void The_quick_slot_is_saved_and_the_newest_is_loaded()
    {
        (MissionSaves saves, Level level) = Saves(difficulty: 0);
        Assert.False(saves.QuickLoad());

        level.Counter.Value = 1;
        Assert.True(saves.Save(0));
        File.SetLastWriteTimeUtc(saves.SlotStore.PathOf(saves.SlotStore.SlotFile(saves.Owner, 0)), DateTime.UtcNow.AddMinutes(-5));
        level.Counter.Value = 2;
        Assert.True(saves.QuickSave());
        Assert.True(saves.SlotStore.Check(saves.Owner, SaveRules.QuickSave).IsValid);
        Assert.Equal(3, saves.SavesLeft);

        level.Counter.Value = 9;
        Assert.True(saves.QuickLoad());
        Assert.Equal(2, level.Counter.Value);
        Assert.Equal(1, level.GameLoads);

        Assert.True(saves.Load(0));
        Assert.Equal(1, level.Counter.Value);
        Assert.Equal(1, level.SavesUsed);
    }

    private (MissionSaves Saves, Level Level) Saves(int difficulty)
    {
        Level level = new();
        Gated saves = new(new SaveSlots(_folder, "saves", ".save", 3), () => new SaveOwner(0, difficulty, 4), level);
        return (saves, level);
    }

    // The shared save games, refused when the level says it cannot be saved.
    private sealed class Gated(SaveSlots slots, Func<SaveOwner> owner, Level level) : MissionSaves(slots, owner, level)
    {
        public override bool CanSave => level.CanBeSaved;
    }

    // A level of one task that keeps a number and the mission's two counts.
    private sealed class Level : IMissionSaveSource
    {
        public Level()
        {
            TaskTree tree = new(new TaskTypeRegistry());
            Counter = (Kept)tree.New(tree.Root, tree.Types.Register("Kept", () => new Kept(this)));
            tree.TryBindId(1, Counter);
            Root = tree.Root;
        }

        public Kept Counter { get; }

        public bool CanBeSaved { get; set; } = true;

        public GameTask Root { get; }

        public int MissionTime { get; set; }

        public int SavesUsed { get; set; }

        public int GameLoads { get; set; }

        public SaveContext Context => new() { Version = 3 };
    }

    private sealed class Kept(Level level) : GameTask, IExchangedState
    {
        public int Value { get; set; }

        public void Exchange(StateExchange exchange)
        {
            Value = exchange.Int(Value);
            level.SavesUsed = exchange.Int(level.SavesUsed);
        }
    }
}
