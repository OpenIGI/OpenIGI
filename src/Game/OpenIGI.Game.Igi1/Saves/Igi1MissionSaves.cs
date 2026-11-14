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

    /// <summary>What a save of a level is written and read with: its registries' states and the level's own statements.</summary>
    /// <param name="world">The played mission, as it stands.</param>
    /// <returns>The context.</returns>
    public static SaveContext Context(MissionWorld world) =>
        new() { Version = Version, States = new Igi1SaveStates(world), Sections = [.. Igi1SaveStates.Sections(world)], RootId = RootId };

    /// <summary>The id the level's root is written under, which no task of a level has.</summary>
    public const int RootId = -2;

    /// <summary>The slots, under the user's folder, or under the directory <see cref="DirectoryVariable"/> names.</summary>
    /// <returns>The slots.</returns>
    public static SaveSlots Slots() =>
        new(Environment.GetEnvironmentVariable(DirectoryVariable) is { Length: > 0 } elsewhere ? elsewhere : SettingsPaths.GetUserDirectory(), Folder, Extension, Version);

    /// <summary>The environment variable that names another directory for the slots' folder: a test's, so it writes nothing of the player's.</summary>
    public const string DirectoryVariable = "OPENIGI_SAVES_DIR";
}

/// <summary>
/// What a Project I.G.I. level's tasks keep in a save game. The level's state lies in the registries it was built into, each of
/// which answers for the tasks it was built from.
/// </summary>
/// <remarks>
/// A task with a level id is written against itself; one without against the nearest task above it that has one, by its place
/// among its siblings. A task that keeps state in more than one registry keeps each after the last, in the registries' order.
/// </remarks>
public sealed class Igi1SaveStates : ISaveStates
{
    private readonly Dictionary<GameTask, ISavedState> _states = new(ReferenceEqualityComparer.Instance);

    /// <summary>Initializes a new instance of the <see cref="Igi1SaveStates"/> class.</summary>
    /// <param name="world">The played mission.</param>
    public Igi1SaveStates(MissionWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        foreach (ISavedStateSource source in Sources(world))
        {
            foreach ((GameTask task, ISavedState state) in source.SavedStates(world.Solids))
            {
                if (_states.TryGetValue(task, out ISavedState? known))
                {
                    ((SavedStateGroup)known).Add(state);
                }
                else
                {
                    _states[task] = new SavedStateGroup([state]) { IsSavedWithParent = task.Id == GameTask.NoId };
                }
            }
        }
    }

    /// <summary>The parts of a mission that keep state for a save game, in the order their states are kept.</summary>
    /// <param name="world">The played mission.</param>
    /// <returns>The parts the level has.</returns>
    public static IEnumerable<ISavedStateSource> Sources(MissionWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return new object?[]
        {
            world.Elevators, world.Doors, world.Interactions, world.GenericPickups, world.AlarmSpeakers, world.Destructibles,
            world.SecurityCameras, world.RotatingObjects, world.Objectives, world.Cutscenes, world.Soldiers,
        }.OfType<ISavedStateSource>();
    }

    /// <summary>The statements a save holds beside the tasks' states: what belongs to the level as a whole.</summary>
    /// <param name="world">The played mission.</param>
    /// <returns>The sections the level has, in the order they are written.</returns>
    public static IEnumerable<ISaveSection> Sections(MissionWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        yield return Igi1PlayerSave.Section(world);
        yield return Igi1LevelSave.Section(world);
        if (world.Objectives is { } objectives)
        {
            yield return objectives.SaveSection;
        }

        if (world.Cutscenes is { } walk)
        {
            yield return walk.SaveSection;
        }

        if (world.GunPickups is { } guns)
        {
            yield return guns.SaveSection(index =>
                world.WeaponTypes is { } weapons && index >= 0 && index < weapons.Weapons.Count ? weapons.Weapons[index] : null);
        }
    }

    /// <inheritdoc/>
    public ISavedState? StateOf(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return _states.GetValueOrDefault(task);
    }
}

/// <summary>
/// A running Project I.G.I. mission's save games: the shared slots, limits, save and load (<see cref="MissionSaves"/>). A load
/// reads the save onto the level that is up, as I.G.I. 2's does (<c>Game_LoadFromSlot</c>): nothing of the mission is brought up
/// again.
/// </summary>
/// <remarks>
/// A level played on is not the level a save was made of -- its dead lie where they fell, its minds are in their fights, its
/// player may be dead -- so the level is first made ready (<see cref="Igi1Gameplay.MakeReadyForSave"/>), and each part a save
/// keeps then reads itself back whatever became of it. Should a save not read, the mission is brought up afresh with it, as
/// I.G.I. 2's menus do (<c>Game_nMenuLoadFromSlot</c>), rather than a level be left half read.
/// </remarks>
public sealed class Igi1MissionSaves : MissionSaves
{
    private readonly SaveSession _session;
    private readonly MissionWorld _world;
    private readonly IMissionSetupHost _host;
    private readonly Level _level;
    private bool _restarted;

    /// <summary>Initializes a new instance of the <see cref="Igi1MissionSaves"/> class.</summary>
    /// <param name="world">The played mission.</param>
    /// <param name="session">What is kept of the save games across a restart.</param>
    /// <param name="host">What restarts the mission, and says whose saves these are.</param>
    /// <param name="makeReady">Makes the level that is up ready for a save to be read onto it, or <see langword="null"/>.</param>
    public Igi1MissionSaves(MissionWorld world, SaveSession session, IMissionSetupHost host, Action? makeReady = null)
        : this(world, session, host, new Level(world, makeReady))
    {
    }

    private Igi1MissionSaves(MissionWorld world, SaveSession session, IMissionSetupHost host, Level level)
        : base(
            Igi1SaveFormat.Slots(),
            () => new SaveOwner(host.ActiveProfileIndex, (int)host.Difficulty, world.MissionId),
            level)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world;
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _level = level;
        PlayedMods = host.PlayedMods;
    }

    /// <inheritdoc/>
    /// <remarks>Only when the last load could not be read onto the level that was up.</remarks>
    public override bool LoadRestartsMission => _restarted;

    /// <inheritdoc/>
    /// <remarks>Not with the player dead, a cut-scene playing, or the level's menus switched off for one.</remarks>
    public override bool CanSave =>
        !SaveGameLock.IsHeld && !_world.IsPlayerDead && _world.Cutscenes?.IsPlaying != true && _world.MenusEnabled;

    /// <inheritdoc/>
    /// <remarks>
    /// Onto the level that is up. A save that checks but does not read leaves the level made ready and unread: the mission is
    /// then brought up afresh, and the slot kept for the level that makes.
    /// </remarks>
    public override bool Load(int slot)
    {
        _restarted = false;
        if (SaveGameLock.IsHeld || !SlotStore.Check(Owner, slot).IsValid || _world.LevelRoot is null)
        {
            return false;
        }

        if (base.Load(slot))
        {
            _host.SaveReadOntoLevel();
            return true;
        }

        _restarted = true;
        _session.PendingLoadSlot = slot;
        _host.RestartMission();
        return true;
    }

    /// <summary>Reads the slot a load asked for onto the level just made, if one was asked for.</summary>
    /// <returns><see langword="true"/> when a save was read.</returns>
    public bool ApplyPending()
    {
        if (_session.TakePendingLoadSlot() is not (var slot and >= 0))
        {
            return false;
        }

        // A level just made is as a save expects it: nothing of it is made ready.
        _level.IsFresh = true;
        try
        {
            return base.Load(slot);
        }
        finally
        {
            _level.IsFresh = false;
        }
    }

    // The played mission as the shared save games ask of it.
    private sealed class Level(MissionWorld world, Action? makeReady) : IMissionSaveSource
    {
        public bool IsFresh { get; set; }

        public void BeforeLoad()
        {
            if (!IsFresh)
            {
                makeReady?.Invoke();
            }
        }

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

        public SaveContext Context => Igi1SaveFormat.Context(world);
    }
}
