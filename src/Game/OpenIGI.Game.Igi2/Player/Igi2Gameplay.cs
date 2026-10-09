using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>I.G.I. 2's own player for the host: the ported <c>HumanPlayer</c> over the level's collision.</summary>
/// <remarks>
/// <para>
/// The level's <c>HumanPlayer</c> task says where the player stands and which way it faces. The collision world is
/// built from the same placements and ground the scene walk produced; the tuning is the one <c>humanplayer.qvm</c>
/// declared at start-up. The player is made as <c>HumanPlayer_CreateHandler</c> makes it and ticked as the original
/// ticks it: the channels read, <c>HumanPlayer_Update</c>, then the delayed half.
/// </para>
/// <para>
/// The body plays the clips the level's packs load, picked by state through the held weapon
/// (<see cref="Igi2HumanAnimation"/>), and the view carries the level's first-person model with the weapon in hand,
/// which the host draws through <see cref="ITitleFirstPerson"/>. The level's sound definitions are read as the level is,
/// and what the player and its weapons sound plays through the host's mixer. Not yet behind it: water, AI events and
/// conveyors.
/// </para>
/// </remarks>
public sealed class Igi2Gameplay : ITitleGameplay
{
    /// <summary>What a terrain hit reports as struck.</summary>
    public const string TerrainOwner = "Terrain";

    /// <summary>The model the first-person view falls back to, <c>005_01_1</c>.</summary>
    public const string FirstPersonFallbackModel = "005_01_1";

    /// <summary>Gets the shared instance.</summary>
    public static Igi2Gameplay Instance { get; } = new();

    /// <inheritdoc/>
    public ITitlePlayer? CreatePlayer(in TitlePlayerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Services);
        if (!PlayerSpawn.TryFindGameplay(request.World.LevelRoot, out PlayerSpawnPoint spawn))
        {
            return null;
        }

        Igi2CollisionWorld world = BuildCollision(request.World, request.Built);
        request.Services.TryGet(out Igi2PlayerTuning? tuning);
        Igi2HumanPlayerGeneral general = tuning?.General ?? Igi2HumanPlayerGeneral.Unset;
        ArgumentNullException.ThrowIfNull(request.World.Mission);
        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(request.World.Mission.PacksInLoadOrder, request.World.Resources);
        Func<string, Formats.World.MefModel?> models = Models(request.World);
        Igi2BoneHierarchy? rig = models(spawn.Model)?.BoneHierarchy;
        string? armsModel = FirstPersonModel(models, PlayerSpawn.FindGameplayTask(request.World.LevelRoot) as Igi2HumanPlayerTask);
        Igi2BoneHierarchy? arms = armsModel is null ? null : models(armsModel)?.BoneHierarchy;
        Igi2PlayerWeapons? weapons = Arm(request.Services, request.World.LevelRoot, tuning, clips);
        Igi2Sounds sounds = LevelSounds(request);
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(world, general, spawn.Position, spawn.Gamma, request.Difficulty, clips, rig, weapons, arms, sounds);
        if (weapons is not null)
        {
            weapons.Effects = new Igi2LevelWeaponEffects(sounds, () => player.Human.View.Position);
            weapons.ImpactEffects = new Igi2LevelImpactEffects(sounds);
        }

        if (request.Services.TryGet(out Igi2GameMaterials? materials))
        {
            if (weapons is not null)
            {
                weapons.World = new Igi2LevelGunShotWorld(world, materials);
                if (request.Services.TryGet(out Physics.Igi2PhysicsObjectTypes? physicsTypes))
                {
                    Weapons.Igi2ExplosionWorld explosions = new(player.Human.Random, world, materials, player.Dynamic)
                    {
                        Effects = new Igi2LevelExplosionEffects(sounds),
                    };
                    explosions.AddTarget(player.Human);
                    Weapons.Igi2Grenades grenades = new(physicsTypes, models, new Physics.Igi2PhysicsWorld(world, materials), player.Dynamic)
                    {
                        Effects = explosions,
                    };
                    weapons.Grenades = grenades;
                    player.Grenades = grenades;
                    player.Explosions = explosions;
                }
            }

            if (request.Services.TryGet(out Igi2HumanWeapons? armoury))
            {
                player.Human.Pickups = Pickups.Igi2PickupList.Build(request.World.LevelRoot, armoury.Tables, models, world, materials);
            }
        }

        if (request.Services.TryGet(out Igi2AnimTriggers? triggers))
        {
            player.Human.AnimTriggers = triggers;
        }

        if (models(spawn.Model) is { } body)
        {
            // BoneDynCubeObj_SetLODVirModel (igi2 0x567BE0): the root's bind translation and the mesh's radius.
            player.Human.ModelRadius = body.NativeMarkerBound;
            if (body.BoneHierarchy is { BoneCount: > 0 } bones)
            {
                player.Human.RootTranslation = bones.Translations[0];
            }
        }

        player.Human.View.ObjectModel = armsModel ?? string.Empty;
        player.Human.WeaponCycle = tuning?.WeaponCycle ?? [];
        player.Level = LevelRun(request, player);
        return player;
    }

    /// <summary>The level's tick, with the player bound to its <c>HumanPlayer</c> task, as <c>Flow_RunChildren</c> (<c>igi2 0x401A50</c>) runs it.</summary>
    /// <param name="request">The level and the session.</param>
    /// <param name="player">The player.</param>
    /// <returns>The level's run, or <see langword="null"/> when the level's scripts are not at hand or it places no player in its tree.</returns>
    public static Igi2LevelRun? LevelRun(in TitlePlayerRequest request, Igi2TitlePlayer player)
    {
        ArgumentNullException.ThrowIfNull(request.Services);
        ArgumentNullException.ThrowIfNull(player);
        if (request.Natives is not { } natives
            || request.World.LevelRoot is not { } root
            || !request.Services.TryGet(out Scripting.Igi2ScriptFunctions? functions)
            || PlayerSpawn.FindGameplayTask(root) is not { } task)
        {
            return null;
        }

        if (request.Services.TryGet(out Igi2FlowState? flow))
        {
            flow.Reset();
            player.Flow = flow;
        }

        Igi2LevelRun level = new(root, natives, functions) { Dynamic = player.Dynamic };
        level.Bind(task, player.RunPlayer);
        return level;
    }

    /// <summary>Reads the level's sound definitions and hands them to the host's mixer, as <c>Game_CreateHandler</c> (<c>igi2 0x4070E0</c>) does.</summary>
    /// <param name="request">The level and the session.</param>
    /// <returns>The level's sounds.</returns>
    public static Igi2Sounds LevelSounds(in TitlePlayerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Services);
        ArgumentNullException.ThrowIfNull(request.World.Mission);
        if (!request.Services.TryGet(out Igi2SoundDefinitions? definitions))
        {
            definitions = new Igi2SoundDefinitions(request.World.Logger);
        }

        IReadOnlyList<string> directories = Igi2SoundDefinitions.Directories(request.World.Mission.Descriptor);
        definitions.Load(request.World.Resources, directories);
        request.Audio?.UseDefinitions(definitions, Igi2SoundDefinitions.BankDirectories(request.World.Mission.Descriptor));
        return new Igi2Sounds(definitions, request.Audio);
    }

    /// <summary>The rig of the model the first-person view carries.</summary>
    /// <param name="models">The level's models, by name.</param>
    /// <param name="player">The level's player, whose <c>1st Person Model</c> names it.</param>
    /// <returns>The rig, or <see langword="null"/> when neither that model nor the fallback has bones.</returns>
    /// <remarks><c>HumanView_CreateHandler</c> (<c>igi2 0x4D5A30</c>): a missing or unboned model falls back to <c>005_01_1</c>.</remarks>
    public static Igi2BoneHierarchy? FirstPersonRig(Func<string, Formats.World.MefModel?> models, Igi2HumanPlayerTask? player)
    {
        ArgumentNullException.ThrowIfNull(models);
        return FirstPersonModel(models, player) is { } name ? models(name)?.BoneHierarchy : null;
    }

    /// <summary>The model the first-person view carries.</summary>
    /// <param name="models">The level's models, by name.</param>
    /// <param name="player">The level's player, whose <c>1st Person Model</c> names it.</param>
    /// <returns>Its name, or <see langword="null"/> when neither that model nor the fallback has bones.</returns>
    /// <remarks><c>HumanView_CreateHandler</c> (<c>igi2 0x4D5A30</c>): a missing or unboned model falls back to <c>005_01_1</c>.</remarks>
    public static string? FirstPersonModel(Func<string, Formats.World.MefModel?> models, Igi2HumanPlayerTask? player)
    {
        ArgumentNullException.ThrowIfNull(models);
        if (player is { FirstPersonModel.Length: > 0 } && models(player.FirstPersonModel)?.BoneHierarchy is not null)
        {
            return player.FirstPersonModel;
        }

        return models(FirstPersonFallbackModel)?.BoneHierarchy is not null ? FirstPersonFallbackModel : null;
    }

    /// <summary>Makes the player's inventory, fills it from the level's <c>Weapon</c> script and puts it behind the weapon control.</summary>
    /// <param name="services">The session's tables.</param>
    /// <param name="levelRoot">The level, whose <c>HumanPlayer</c> carries the script.</param>
    /// <param name="tuning">The player tuning, whose ammunition limits bound the inventory.</param>
    /// <param name="clips">The level's clips, which the held weapon's body clips come from.</param>
    /// <returns>The weapons, or <see langword="null"/> when the session has none.</returns>
    /// <remarks>
    /// <c>Human_CreateHandler</c> (<c>igi2 0x4FDC90</c>) makes the inventory within the limits; the script runs once the level
    /// is loaded (<c>Human_UpdateInternalDataAfterCarryoverHandler</c>, <c>0x4FF440</c>).
    /// </remarks>
    public static Igi2PlayerWeapons? Arm(
        Engine.Titles.IServiceCatalog services,
        Engine.Tasks.GameTask? levelRoot,
        Igi2PlayerTuning? tuning,
        Igi2AnimationLibrary? clips = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!services.TryGet(out Igi2HumanWeapons? weapons))
        {
            return null;
        }

        Igi2Inventory inventory = weapons.CreateInventory(tuning?.AmmoLimits ?? []);
        if (levelRoot is not null && PlayerSpawn.FindGameplayTask(levelRoot) is Igi2HumanPlayerTask player)
        {
            weapons.RunWeaponScript(inventory, player.WeaponScript);
        }

        return new Igi2PlayerWeapons(inventory, weapons.FireTaskTypes, clips);
    }

    /// <summary>Builds the collision a level's player moves through: its solid placements and its ground.</summary>
    /// <param name="world">The request the scene was built from.</param>
    /// <param name="built">What the scene walk produced; its ground is reused.</param>
    /// <returns>The collision world.</returns>
    public static Igi2CollisionWorld BuildCollision(in WorldBuildRequest world, in WorldBuildResult built)
    {
        ArgumentNullException.ThrowIfNull(world.Mission);
        ArgumentNullException.ThrowIfNull(world.LevelRoot);

        Func<string, Formats.World.MefModel?> models = Models(world);
        Igi2WorldScene scene = Igi2WorldScene.Build(world.LevelRoot, models);
        Igi2TerrainSurface? ground = built.Ground is Igi2Ground level ? level.Surface : null;
        Igi2Forest forests = Igi2Forest.Build(world.LevelRoot, world.Resources);
        return Igi2CollisionWorld.Build(scene, models, ground, TerrainOwner, forests);
    }

    /// <summary>Resolves a placed model name to its mesh through the mission's packs.</summary>
    private static Func<string, Formats.World.MefModel?> Models(in WorldBuildRequest world)
    {
        ArgumentNullException.ThrowIfNull(world.Mission);
        List<Formats.World.MtpPack> mtp = [.. world.Mission.PacksInLoadOrder.Select(static pack => pack.Pack)];
        return Igi2DrawableScene.Models(Igi2ModelTable.Create(mtp), world.Mission.PacksInLoadOrder);
    }
}

/// <summary>The ported player as the host steps it.</summary>
public sealed class Igi2TitlePlayer : ITitlePlayer, ITitleFirstPerson, ITitleWorldModels
{
    /// <summary>DirectInput's relative counts to the analog axis, <c>Mouse_Update</c> (<c>igi2ed 0x55ED60</c>): a 255th each.</summary>
    private const double CountsToAnalog = 0.00392156862745098;

    private static readonly (PlayerAction Host, Igi2PlayerAction Title)[] Actions =
    [
        (PlayerAction.MoveLeft, Igi2PlayerAction.MoveLeft),
        (PlayerAction.MoveRight, Igi2PlayerAction.MoveRight),
        (PlayerAction.MoveUp, Igi2PlayerAction.MoveForwards),
        (PlayerAction.MoveDown, Igi2PlayerAction.MoveBackwards),
        (PlayerAction.Fire, Igi2PlayerAction.Fire),
        (PlayerAction.AlternateFire, Igi2PlayerAction.AlternateFire),
        (PlayerAction.Reload, Igi2PlayerAction.Reload),
        (PlayerAction.Activate, Igi2PlayerAction.Activate),
        (PlayerAction.Crouch, Igi2PlayerAction.Crouch),
        (PlayerAction.Jump, Igi2PlayerAction.Jump),
        (PlayerAction.Peek, Igi2PlayerAction.Peek),
        (PlayerAction.ZoomIn, Igi2PlayerAction.ZoomIn),
        (PlayerAction.ZoomOut, Igi2PlayerAction.ZoomOut),
        (PlayerAction.NextWeapon, Igi2PlayerAction.NextWeapon),
        (PlayerAction.PreviousWeapon, Igi2PlayerAction.PrevWeapon),
        (PlayerAction.ToggleWalkRun, Igi2PlayerAction.ToggleWalkRun),
        (PlayerAction.Binoculars, Igi2PlayerAction.Binoculars),
        (PlayerAction.MapComputer, Igi2PlayerAction.ComputerMap),
    ];

    private readonly Igi2PlayerInputReader _reader = new();
    private readonly float[] _frame = new float[Igi2HumanChannels.Count];
    private readonly Igi2FirstPersonFrame _firstPerson = new();
    private long _ticks;
    private TitleFirstPersonView? _held;
    private long _heldTick = -1;
    private TitleWorldModel[] _lying = [];
    private long _lyingTick = -1;

    private Igi2TitlePlayer(Igi2HumanPlayer human, Igi2PlayerWeapons? weapons)
    {
        Human = human;
        Weapons = weapons;
    }

    /// <summary>Gets the ported player.</summary>
    public Igi2HumanPlayer Human { get; }

    /// <summary>Gets the player's weapons, or <see langword="null"/> when the session has none.</summary>
    public Igi2PlayerWeapons? Weapons { get; }

    /// <summary>Gets what the player carries, or <see langword="null"/> when the session has no weapons.</summary>
    public Igi2Inventory? Inventory => Weapons?.Inventory;

    /// <summary>Gets or sets the grenades the player has thrown that are still in flight, or <see langword="null"/> for none.</summary>
    public Weapons.Igi2Grenades? Grenades { get; set; }

    /// <summary>Gets or sets the explosions the level's blasts go off in, or <see langword="null"/> for none.</summary>
    public Weapons.Igi2ExplosionWorld? Explosions { get; set; }

    /// <summary>Gets the level's dynamic tasks, <c>Level_ptDynamicQTask</c>: the grenades, explosions and what they leave.</summary>
    public World.Igi2DynamicTasks Dynamic { get; } = new();

    /// <summary>Gets the level's sounds.</summary>
    public Igi2Sounds Sounds { get; private init; } = new(new Igi2SoundDefinitions(), null);

    /// <summary>Gets or sets the level's tick the player runs inside, or <see langword="null"/> to run the player alone.</summary>
    public Igi2LevelRun? Level { get; set; }

    /// <summary>Gets or sets the game-wide switches the level's scripts flip, or <see langword="null"/> for none.</summary>
    public Igi2FlowState? Flow { get; set; }

    /// <inheritdoc/>
    /// <remarks><c>CutScene_isRunning</c> as the last tick left it.</remarks>
    public bool IsCutscenePlaying => Level?.IsCutsceneRunning ?? false;

    /// <inheritdoc/>
    public bool IsMenuEnabled => Flow?.IsMenuEnabled ?? true;

    /// <inheritdoc/>
    public Vector3d Position => Human.Position;

    /// <inheritdoc/>
    public Vector3d EyePosition => Human.View.Position;

    /// <inheritdoc/>
    /// <remarks>The view's columns are its right, forward and up axes; the camera wants them as rows, up before forward.</remarks>
    public Matrix3f ViewOrientation
    {
        get
        {
            Matrix3f m = Human.View.Orientation;
            return Matrix3f.FromRows(
                new Vector3f(m.M11, m.M21, m.M31),
                new Vector3f(m.M13, m.M23, m.M33),
                new Vector3f(m.M12, m.M22, m.M32));
        }
    }

    /// <inheritdoc/>
    /// <remarks>The view's <c>vFOV</c>, which a zoom steers.</remarks>
    public float FieldOfViewScale => Human.View.FieldOfView;

    /// <inheritdoc/>
    /// <remarks>Built once a tick, on the first ask after it, from the view as the tick left it.</remarks>
    public TitleFirstPersonView? FirstPerson
    {
        get
        {
            if (_heldTick != _ticks)
            {
                _held = _firstPerson.Build(Human, Inventory?.Current?.WeaponType.FixedModel, _ticks);
                _heldTick = _ticks;
            }

            return _held;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The level's pickups not yet taken, each the weapon's <c>Fixed Model</c> where it lies, then the grenades in flight, each
    /// its <c>ProjectileModel</c>; built once a tick.
    /// </remarks>
    public IReadOnlyList<TitleWorldModel> WorldModels
    {
        get
        {
            if (_lyingTick != _ticks)
            {
                IEnumerable<TitleWorldModel> pickups = Human.Pickups is { } lying
                    ? lying.Items.Select(static p => new TitleWorldModel(p.WeaponType.FixedModel, p.Position, Rows(p.Orientation)))
                    : [];
                IEnumerable<TitleWorldModel> flying = Grenades is { } grenades
                    ? grenades.Flying.Select(static g => new TitleWorldModel(g.Model, g.Position, Rows(g.Orientation)))
                    : [];
                _lying = [.. pickups, .. flying];
                _lyingTick = _ticks;
            }

            return _lying;
        }
    }

    /// <summary>Makes the player at its spawn, as <c>HumanPlayer_CreateHandler</c> does, over a collision world.</summary>
    /// <param name="world">The level's collision.</param>
    /// <param name="general">The player tuning.</param>
    /// <param name="position">Where the level places the player's feet.</param>
    /// <param name="gamma">The heading the level gives it.</param>
    /// <param name="difficulty">The active difficulty.</param>
    /// <param name="clips">The level's clips, or <see langword="null"/> for none.</param>
    /// <param name="rig">The player model's rig, or <see langword="null"/> for a model without bones.</param>
    /// <param name="weapons">The player's weapons, or <see langword="null"/> for none.</param>
    /// <param name="firstPersonRig">The first-person model's rig, or <see langword="null"/> for none.</param>
    /// <param name="sounds">The level's sounds, or <see langword="null"/> for a level that defines none.</param>
    /// <returns>The player.</returns>
    public static Igi2TitlePlayer Spawn(
        Igi2CollisionWorld world,
        Igi2HumanPlayerGeneral general,
        Vector3d position,
        float gamma,
        Ai.AiDifficulty difficulty,
        Igi2AnimationLibrary? clips = null,
        Igi2BoneHierarchy? rig = null,
        Igi2PlayerWeapons? weapons = null,
        Igi2BoneHierarchy? firstPersonRig = null,
        Igi2Sounds? sounds = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(general);

        sounds ??= new Igi2Sounds(new Igi2SoundDefinitions(), null);
        Igi2PlayerSurroundings surroundings = new(world, sounds);
        IIgi2PlayerWeaponControl weapon = weapons ?? (IIgi2PlayerWeaponControl)Igi2NoWeaponControl.Instance;
        Igi2HumanAnimation animation = new(clips, rig, Igi2PlayerStateAnimations.Table, () => weapon.BodyClips(rig));
        Igi2HumanPlayer human = new(weapon, animation, surroundings)
        {
            General = general,
            Position = position,
            Gamma = gamma,
            IdealGamma = gamma,
        };
        surroundings.Owner = human;
        human.View.ObjectRig = firstPersonRig;
        animation.TriggerTarget = human.BodyTriggers;

        // Human_UpdateInternalDataHandler (igi2 0x4FF2F0): the level's Gamma is the heading and the ideal heading
        // both, and the body stands upright turned by it.
        float[] facing = Igi2Pose.Rotation(0f, 0f, gamma);
        human.Orientation = new Matrix3f(
            facing[0], facing[1], facing[2],
            facing[3], facing[4], facing[5],
            facing[6], facing[7], facing[8]);
        human.Create(difficulty);
        human.MaintainCollisionZOffset();

        // The view would first be placed on the first tick; placing it now gives the host's first frames the eyes
        // rather than the world's origin. It draws nothing from the run stream: a new view does not shake.
        human.UpdateViewBody(updateAnimation: false);
        return new Igi2TitlePlayer(human, weapons) { Sounds = sounds };
    }

    /// <inheritdoc/>
    public void Tick(in TitlePlayerInput input)
    {
        Igi2PlayerAction held = Map(input.Held);
        Igi2PlayerAction pressed = Map(input.Pressed);
        _reader.MouseSensitivity = input.MouseSensitivity;
        _reader.InvertMouse = input.InvertMouse;
        _reader.Read(new Igi2PlayerInputState(Analog(input.MouseX), Analog(input.MouseY), held, pressed), _frame);
        if (Level is { } level)
        {
            Flow?.IsBreakKeyDown = input.BreakCutscene;
            level.Tick();
        }
        else
        {
            RunPlayer();
            Dynamic.Run();
            RunPlayerDelayed();
        }

        Sounds.Run();
        _ticks++;
    }

    /// <summary>
    /// The player's run handler: the shots its weapon fired last tick are traced, and the rest of its tick is queued for the
    /// delayed pass (<c>HumanPlayer_RunHandler</c>, <c>igi2 0x4B8A30</c>).
    /// </summary>
    internal void RunPlayer()
    {
        Weapons?.RunGunShot();
        if (Level is { } level)
        {
            level.Delay(RunPlayerDelayed);
        }
    }

    /// <summary>The player's delayed half: its pickups, zoom, update and delayed handler (<c>HumanPlayer_RunDelayedHandler</c>, <c>igi2 0x4B8A50</c>).</summary>
    private void RunPlayerDelayed()
    {
        Human.Pickups?.Run();
        Human.View.RunZoom();
        Human.Ticks = (int)_ticks;
        Human.Update(_frame);
        Human.RunDelayed();
    }

    /// <summary>Nine floats row by row as a matrix.</summary>
    private static Matrix3f Rows(float[] m) => new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);

    /// <summary>The mouse's counts as the input port's analog axis: a 255th each, held within one either way.</summary>
    private static float Analog(float counts)
    {
        float analog = ((Fpu)(double)counts * CountsToAnalog).F;
        return analog < -1f ? -1f : analog > 1f ? 1f : analog;
    }

    private static Igi2PlayerAction Map(PlayerAction actions)
    {
        Igi2PlayerAction mapped = Igi2PlayerAction.None;
        foreach ((PlayerAction host, Igi2PlayerAction title) in Actions)
        {
            if ((actions & host) != 0)
            {
                mapped |= title;
            }
        }

        return mapped;
    }
}

/// <summary>The level a bullet flies through: the collision world and the session's materials.</summary>
/// <remarks>Nothing in the collision world is water or a boned object yet; both arrive with their colliders.</remarks>
internal sealed class Igi2LevelGunShotWorld(IIgi2CollisionQuery collision, Igi2GameMaterials materials) : IIgi2GunShotWorld
{
    public IIgi2CollisionQuery Collision { get; } = collision;

    public Igi2GameMaterials Materials { get; } = materials;

    public bool IsWater(IIgi2Collider struck) => false;

    public bool IsBoned(IIgi2Collider struck) => false;

    public bool IsHuman(IIgi2Collider struck) => false;
}

/// <summary>What the player's states ask of the world, answered from the collision world and the level's sounds.</summary>
internal sealed class Igi2PlayerSurroundings(Igi2CollisionWorld world, Igi2Sounds sounds) : IIgi2HumanSurroundings
{
    /// <summary>Gets or sets the body these surroundings are asked about.</summary>
    public Igi2Human? Owner { get; set; }

    public IIgi2CollisionQuery Collision { get; } = world;

    public bool IsOnElevator => false;

    public bool FitsLyingFlat() => Owner?.FitsWhereItStands() ?? true;

    public void SendWalkEvent()
    {
    }

    public void SendLandingEvent()
    {
    }

    public bool IsUnderWater(out float depth)
    {
        depth = 0f;
        return false;
    }

    public void StirWater(bool isRunning)
    {
    }

    public Vector3f? PushOnConveyor(IIgi2Collider ground, Igi2Intersection footing) => null;

    public void BumpHumans(Vector3d start, Vector3d destination)
    {
    }

    public string? LandingSound(int material) => null;

    public void PlaySound(string name) => StartSound(name);

    public void ShowStatusMessage(string key)
    {
    }

    public IIgi2SoundInstance StartSound(string name) =>
        sounds.New(name, View(), View) ?? (IIgi2SoundInstance)SilentSound.Instance;

    private Vector3d View() => Owner?.View.Position ?? default;

    private sealed class SilentSound : IIgi2SoundInstance
    {
        public static SilentSound Instance { get; } = new();

        public bool IsAlive => false;

        public void MoveToHuman()
        {
        }

        public void Delete()
        {
        }
    }
}
