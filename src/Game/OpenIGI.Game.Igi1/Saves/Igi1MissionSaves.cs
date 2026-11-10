using OpenIGI.Core.Configuration;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Saves;

namespace OpenIGI.Game.Igi1.Saves;

/// <summary>
/// Project I.G.I.'s save games. The original shipped with none; these are the engine's (<see cref="SaveScript"/>, I.G.I. 2's), kept
/// by I.G.I. 2's rules, in files of this port's own under the user's folder.
/// </summary>
public static class Igi1SaveFormat
{
    /// <summary>The version this game writes, and the least it reads.</summary>
    public const int Version = 1;

    /// <summary>The folder the slots lie in, under the user's folder.</summary>
    public const string Folder = "saves/igi1";

    /// <summary>The slot files' extension.</summary>
    public const string Extension = ".igisave";

    /// <summary>The slots, under the user's folder.</summary>
    /// <returns>The slots.</returns>
    public static SaveSlots Slots() => new(SettingsPaths.GetUserDirectory(), Folder, Extension, Version);
}

/// <summary>
/// What a Project I.G.I. level's tasks keep in a save game. The level's state lies in the registries it was built into, each of
/// which answers for the tasks it was built from.
/// </summary>
/// <param name="world">The played mission.</param>
public sealed class Igi1SaveStates(MissionWorld world) : ISaveStates
{
    private readonly MissionWorld _world = world ?? throw new ArgumentNullException(nameof(world));

    /// <inheritdoc/>
    public ISavedState? StateOf(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return _world.Doors?.StateOf(task);
    }
}

/// <summary>
/// A running Project I.G.I. mission's save games: the shared slots, limits and save (<see cref="MissionSaves"/>), and a load that
/// starts the mission afresh and puts the save onto the level as it comes up.
/// </summary>
/// <remarks>
/// A level played on is not the level a save was made of: its dead are gone from the walk, its spawned guards are extra, its broken
/// things are broken. So a load does not read onto the level that is up, as I.G.I. 2's does; it asks the host for the mission
/// again, as I.G.I. 2's menus do (<c>Game_nMenuLoadFromSlot</c>), and the save is read once that level is made.
/// </remarks>
public sealed class Igi1MissionSaves : MissionSaves
{
    private readonly SaveSession _session;
    private readonly IMissionSetupHost _host;

    /// <summary>Initializes a new instance of the <see cref="Igi1MissionSaves"/> class.</summary>
    /// <param name="world">The played mission.</param>
    /// <param name="session">What is kept of the save games across a restart.</param>
    /// <param name="host">What restarts the mission, and says whose saves these are.</param>
    public Igi1MissionSaves(MissionWorld world, SaveSession session, IMissionSetupHost host)
        : base(
            Igi1SaveFormat.Slots(),
            () => new SaveOwner(host.ActiveProfileIndex, host.ScriptDifficulty, world.MissionId),
            new Level(world))
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        PlayedMods = host.PlayedMods;
    }

    /// <inheritdoc/>
    public override bool LoadRestartsMission => true;

    /// <inheritdoc/>
    /// <remarks>The slot is kept for the level the restart makes; nothing of the level that is up is touched.</remarks>
    public override bool Load(int slot)
    {
        if (!SlotStore.Check(Owner, slot).IsValid)
        {
            return false;
        }

        _session.PendingLoadSlot = slot;
        _host.RestartMission();
        return true;
    }

    /// <summary>Reads the slot a load asked for onto the level just made, if one was asked for.</summary>
    /// <returns><see langword="true"/> when a save was read.</returns>
    public bool ApplyPending() => _session.TakePendingLoadSlot() is var slot and >= 0 && base.Load(slot);

    // The played mission as the shared save games ask of it.
    private sealed class Level(MissionWorld world) : IMissionSaveSource
    {
        public GameTask Root => world.LevelRoot ?? throw new InvalidOperationException("No level is up to save.");

        public int MissionTime => (int)world.PlayerTicks;

        public int SavesUsed
        {
            get => world.SavesUsed;
            set => world.SavesUsed = value;
        }

        public int GameLoads
        {
            get => world.GameLoads;
            set => world.GameLoads = value;
        }

        public SaveContext Context => new() { Version = Igi1SaveFormat.Version, States = new Igi1SaveStates(world) };
    }
}
