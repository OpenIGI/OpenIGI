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

/// <summary>The ported player as the host steps it.</summary>
public sealed partial class Igi2TitlePlayer : ITitlePlayer, ITitleFirstPerson, ITitleWorldModels, IWeaponWheelPlayer, IShotTallySource, OpenIGI.Game.Replays.IGhostSource
{
    /// <summary>Gets or sets the title profile whose HUD preferences are currently active.</summary>
    public Config.Igi2GameConfig? GameConfig { get; set; }

    /// <summary>Gets or sets the cameras whose lenses and indicator lights are drawn.</summary>
    public IReadOnlyList<Interaction.Igi2SecurityCameraTask> Cameras { get; set; } = [];

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

        // The drop key is the original's in a single-player game too (HumanPlayer_RunActionIdle drops a weapon its type marks
        // isDropWeaponInSinglePlayer); its config files it with the combat keys, not the multiplayer ones.
        (PlayerAction.DropWeapon, Igi2PlayerAction.DropWeapon),
        (PlayerAction.ToggleWalkRun, Igi2PlayerAction.ToggleWalkRun),
        (PlayerAction.Binoculars, Igi2PlayerAction.Binoculars),
        (PlayerAction.MapComputer, Igi2PlayerAction.ComputerMap),
        (PlayerAction.ComputerObjectives, Igi2PlayerAction.ComputerObjectives),
        (PlayerAction.ComputerLog, Igi2PlayerAction.ComputerLog),

        // The number keys, as the original's config.qvm binds them: 1 is its third category and 3 its first, 4 to 7 their
        // own, 9 the ninth; it binds nothing to 8.
        (PlayerAction.WeaponCategory1, Igi2PlayerAction.WeaponCategory3),
        (PlayerAction.WeaponCategory2, Igi2PlayerAction.WeaponCategory2),
        (PlayerAction.WeaponCategory3, Igi2PlayerAction.WeaponCategory1),
        (PlayerAction.WeaponCategory4, Igi2PlayerAction.WeaponCategory4),
        (PlayerAction.WeaponCategory5, Igi2PlayerAction.WeaponCategory5),
        (PlayerAction.WeaponCategory6, Igi2PlayerAction.WeaponCategory6),
        (PlayerAction.WeaponCategory7, Igi2PlayerAction.WeaponCategory7),
        (PlayerAction.WeaponCategory9, Igi2PlayerAction.WeaponCategory9),
    ];

    private readonly Igi2PlayerInputReader _reader = new();

    /// <summary>Keeps the click that chose something on a screen from firing the gun, the input's <c>isFreezeFire</c>.</summary>
    internal void FreezeFire() => _reader.FreezeFire = true;
    private readonly float[] _frame = new float[Igi2HumanChannels.Count];
    private readonly Igi2FirstPersonFrame _firstPerson = new();
    private long _ticks;
    private TitleFirstPersonView? _held;
    private long _heldTick = -1;
    private TitleWorldModel[] _lying = [];
    private long _lyingTick = -1;
    private long _lyingFrame = -1;
    private TitleWorldModel[] _outline = [];
    private long _outlineTick = -1;
    private TitleWorldSprite[] _lights = [];
    private long _lightsTick = -1;

    /// <inheritdoc/>
    public IReadOnlyList<TitleWorldSprite> WorldSprites
    {
        get
        {
            if (_lightsTick != _ticks)
            {
                _lights = [.. Cameras.Select(static c => c.Indicator()).OfType<TitleWorldSprite>()];
                _lightsTick = _ticks;
            }
            return _lights;
        }
    }

    /// <inheritdoc/>
    public OpenIGI.Game.World.Particles.Particle2DModule? Particles => Human.Surroundings.Particles?.Module;

    private TitleSurfaceMark[] _surfaceMarks = [];
    private long _surfaceMarksTick = -1;
    private readonly Igi2LaserDot _laserDot = new();

    /// <inheritdoc/>
    /// <remarks>
    /// Each body's mark where it shows (<see cref="OpenIGI.Game.World.HumanShadow"/>) -- in I.G.I. 2 a dead body's pool of blood --
    /// the player's, the soldiers' and the other players' in a co-op session (whose pools showed on no screen): coloured by the
    /// light where it lay, or nine tenths grey, times its strength above the ground (<c>Shadow_MagicObjEnterHandler</c>). Then the
    /// player's weapon's laser dot where it points, when the weapon shows one (<see cref="Igi2LaserDot"/>).
    /// </remarks>
    public IReadOnlyList<TitleSurfaceMark> SurfaceMarks
    {
        get
        {
            if (_surfaceMarksTick != _ticks)
            {
                IEnumerable<Humans.Igi2Human> bodies = Soldiers?.Bodies ?? [];
                IEnumerable<Humans.Igi2Human> others = _networkHumans.Select(static n => (Humans.Igi2Human)n.Human);
                IEnumerable<TitleSurfaceMark?> marks = bodies.Prepend(Human).Concat(others).Select(static h => ShadowMark(h.Shadow));
                _surfaceMarks = [.. marks.Append(Weapons is { } weapons ? _laserDot.Place(weapons.Firing) : null).OfType<TitleSurfaceMark>()];
                _surfaceMarksTick = _ticks;
            }

            return _surfaceMarks;
        }
    }

    private static TitleSurfaceMark? ShadowMark(OpenIGI.Game.World.HumanShadow shadow)
    {
        if (!shadow.IsEnabled || !shadow.IsPositionValid || shadow.Size <= 0f)
        {
            return null;
        }

        float shade = (shadow.Light ?? 0.9f) * shadow.Strength;
        byte channel = (byte)Math.Clamp((int)(shade * 255f), 0, 255);
        return new TitleSurfaceMark(
            "LOCAL:common/textures/" + shadow.Texture,
            shadow.Position,
            shadow.Normal,
            shadow.Size,
            OpenIGI.Game.World.HumanShadow.Hover,
            OpenIGI.Core.Graphics.Bgra32.FromRgba(channel, channel, channel, 255));
    }

    private readonly Igi2LevelSurroundings _surroundings;

    private Igi2TitlePlayer(Igi2HumanPlayer human, Igi2PlayerWeapons? weapons, Igi2LevelSurroundings surroundings)
    {
        Human = human;
        Weapons = weapons;
        _surroundings = surroundings;
        surroundings.StatusMessage = key => ShowStatusMessage(key);
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

    /// <summary>
    /// Gets or sets the level's player task when another game's player stands for it (<see cref="IHostedHuman.Driver"/>), or
    /// <see langword="null"/> while this player is the level's. A player stood for is not run, struck or seen: the other game's
    /// is, where the level finds it (<see cref="StandIn"/>).
    /// </summary>
    public IHostedHuman? StoodFor { get; set; }

    /// <summary>Gets the other game's player standing for this one, as the level's AI looks out for it; <see langword="null"/> for none.</summary>
    public Game.Ai.IAiPlayer? StandIn => StoodFor?.StandIn as Game.Ai.IAiPlayer;

    /// <summary>
    /// Gets or sets the other game's player standing for this one (<see cref="StoodFor"/>): run with the tick's input where the
    /// level's walk reaches the player's task, in this player's stead, and whose eyes the view is.
    /// </summary>
    public ITitlePlayer? StandInPlayer { get; set; }

    /// <inheritdoc/>
    OpenIGI.Game.World.IExplosionScene? ITitleWorldModels.Explosions => Explosions;

    /// <summary>Gets the level's dynamic tasks, <c>Level_ptDynamicQTask</c>: the grenades, explosions and what they leave.</summary>
    public OpenIGI.Game.World.DynamicTasks Dynamic { get; } = new();

    /// <summary>Gets or sets the level's soldiers, or <see langword="null"/> before the level made them.</summary>
    public Soldiers.Igi2Soldiers? Soldiers { get; set; }

    /// <summary>Gets or sets the level's AI, which gives the soldiers their minds, or <see langword="null"/>.</summary>
    public Ai.Runtime.Igi2AiWorld? Ai { get; set; }

    /// <summary>Gets the level's sounds.</summary>
    public Igi2Sounds Sounds { get; private init; } = new(new Igi2SoundDefinitions(), null);

    /// <summary>Gets the screen the level's status messages are printed on, which runs after the level each tick.</summary>
    public Igi2StatusMessageScreen StatusMessages { get; } = new();

    /// <summary>Gets the things in the level the player may activate, in the order the stationary tree offers them.</summary>
    public List<Interaction.IIgi2Activatable> Activatables => _surroundings.Activatables;

    /// <summary>Gets the collision world the player moves through.</summary>
    public Igi2CollisionWorld CollisionWorld => _surroundings.World;

    /// <inheritdoc/>
    /// <remarks>
    /// The world as the player's eye looks across it: the player's own body is no wall. A line from the eye starts inside the
    /// head's bones, so the world itself would answer with the player, as the crosshair's trace also passes him (<see cref="EnemyUnderAim"/>).
    /// </remarks>
    ICollisionWorld? ITitlePlayer.Collision => _eyeWorld ??= new Igi2EyeCollision(this);

    private Igi2EyeCollision? _eyeWorld;

    /// <summary>Gets the level's conveyor belts the player may stand on, by level id.</summary>
    public Dictionary<int, Interaction.Igi2ConveyorBeltTask> Conveyors => _surroundings.Conveyors;

    /// <summary>Gets or sets the session's materials, which name what the player stands on.</summary>
    public Igi2GameMaterials? Materials
    {
        get => _surroundings.Materials;
        set => _surroundings.Materials = value;
    }

    /// <inheritdoc/>
    public IPlacementMotion? Motions { get; set; }

    /// <inheritdoc/>
    public IPlacementDestruction? Destruction { get; set; }

    /// <inheritdoc/>
    public IPlacementPresence? Presence { get; set; }

    /// <inheritdoc/>
    public IPlacementVariant? Variants { get; set; }

    /// <inheritdoc/>
    public IPlacementVisibility? Visibility { get; set; }

    /// <summary>Gets or sets the level's tick the player runs inside, or <see langword="null"/> to run the player alone.</summary>
    public Igi2LevelRun? Level { get; set; }

    /// <summary>Gets or sets the game-wide switches the level's scripts flip, or <see langword="null"/> for none.</summary>
    public Igi2FlowState? Flow { get; set; }

    /// <summary>Gets or sets what tells the session's events what each tick changed, or <see langword="null"/> for none.</summary>
    internal Igi2EventWatch? EventWatch { get; set; }

    /// <summary>Gets or sets the player's input task, or <see langword="null"/> when the level gives the player none.</summary>
    public Igi2HumanPlayerInputTask? Input { get; set; }

    /// <summary>Gets or sets the map computer, or <see langword="null"/>; the player opens it.</summary>
    public Computer.Igi2Computer? Computer
    {
        get => Human.Computer;
        set => Human.Computer = value;
    }

    /// <inheritdoc/>
    /// <remarks><c>CutScene_isRunning</c> as the last tick left it.</remarks>
    public bool IsCutscenePlaying => Level?.IsCutsceneRunning ?? false;

    /// <inheritdoc/>
    /// <remarks>The cutscene's camera changed this tick (<c>CutScene_InterpolateCamera</c>'s just-changed flag), which starts its filters afresh too.</remarks>
    public bool IsViewCut => Level?.CutsceneView?.IsCameraJustChanged == true;

    /// <inheritdoc/>
    public bool IsMenuEnabled => Flow?.IsMenuEnabled ?? true;

    /// <inheritdoc/>
    public Vector3d Position => ActingStandIn?.Position ?? Human.Position;

    /// <inheritdoc/>
    /// <remarks>
    /// A cutscene's camera, while one is placed, outranks the player's eyes; a mounted gun's seat, while the view is overridden,
    /// stands in for them (the human camera's mode 4). Another game's player standing in has its own eyes.
    /// </remarks>
    public Vector3d EyePosition => Level?.CutsceneView is { } cut
        ? cut.Position
        : IsMapShown && ComputerDisplay?.Map is { } map ? map.Position
        : ActingStandIn is { } standIn ? standIn.EyePosition
        : (Human.CameraMode is 3 or 5 ? _movementCamera.Position
        : Human.View.IsOverride ? Human.View.OverridePosition : Human.View.Position) + ViewError.Offset(0f);

    /// <inheritdoc/>
    /// <remarks>
    /// The view's columns are its right, forward and up axes; the camera wants them as rows, up before forward. A cutscene
    /// camera's columns are right, down and forward, the engine camera's own (<c>HumanCamera_FirstPersonSetCamera</c>,
    /// <c>igi2 0x4D4730</c>, turns the view a quarter about its right to get there).
    /// </remarks>
    public Matrix3f ViewOrientation
    {
        get
        {
            if (Level?.CutsceneView is { } cut)
            {
                IReadOnlyList<float> c = cut.Orientation;
                return Matrix3f.FromRows(
                    new Vector3f(c[0], c[3], c[6]),
                    new Vector3f(-c[1], -c[4], -c[7]),
                    new Vector3f(c[2], c[5], c[8]));
            }

            if (IsMapShown)
            {
                return OpenIGI.Game.Igi2.Computer.Igi2ComputerMap.Orientation;
            }

            if (ActingStandIn is { } standIn)
            {
                return standIn.ViewOrientation;
            }

            Matrix3f m = Human.CameraMode is 3 or 5 ? _movementCamera.Orientation
                : Human.View.IsOverride ? Human.View.OverrideOrientation : Human.View.Orientation;
            return Matrix3f.FromRows(
                new Vector3f(m.M11, m.M21, m.M31),
                new Vector3f(m.M13, m.M23, m.M33),
                new Vector3f(m.M12, m.M22, m.M32));
        }
    }

    /// <inheritdoc/>
    /// <remarks>The view's <c>vFOV</c>, which a zoom steers; a cutscene camera's own while one is placed.</remarks>
    public float FieldOfViewScale => Level?.CutsceneView is { } cut ? cut.FieldOfView
        : IsMapShown && ComputerDisplay?.Map is { } map ? map.FieldOfView
        : ActingStandIn is { } standIn ? standIn.FieldOfViewScale
        : Human.CameraMode is 3 or 5 ? 1f : Human.View.FieldOfView;

    /// <inheritdoc/>
    /// <remarks>
    /// Built once a tick, on the first ask after it, from the view as the tick left it; none while a cutscene's camera is placed,
    /// which is not the player's eyes. Another game's player standing in holds its own, or none.
    /// </remarks>
    public TitleFirstPersonView? FirstPerson
    {
        get
        {
            if (ActingStandIn is { } acting)
            {
                return Level?.CutsceneView is null && !IsComputerShown ? (acting as ITitleFirstPerson)?.FirstPerson : null;
            }

            if (Level?.CutsceneView is not null || Human.CameraMode != 1 || Human.IsThirdPerson || IsComputerShown)
            {
                return null;
            }

            if (_heldTick != _ticks)
            {
                _held = _firstPerson.Build(Human, Inventory?.Current?.WeaponType.FixedModel, _ticks, ModelAttachments, Weapons?.Firing.Flashes);
                _heldTick = _ticks;
            }

            return _held;
        }
    }

    /// <summary>Gets or sets the mission's resolver for the held model's nested magic objects.</summary>
    public Igi2ModelAttachments? ModelAttachments { get; set; }

    /// <summary>Gets or sets the spent casings in flight, the player's and the soldiers'; <see langword="null"/> for none.</summary>
    public Weapons.Igi2GunCasings? Casings { get; set; }

    /// <summary>
    /// Throws a casing from the player's weapon object: the one the first-person view holds, or, while the body is drawn working,
    /// the one in the body's hand.
    /// </summary>
    /// <param name="ejection">What the shot drew for it.</param>
    internal void EjectCasing(Weapons.Igi2CasingEjection ejection)
    {
        if (Casings is not { } casings || Inventory?.Current?.WeaponType.FixedModel is not { } held)
        {
            return;
        }

        if (IsBodyShown && Human.Model is { } body)
        {
            EjectFrom(casings, ejection, Igi2BodyFrame.Build(Human, Human.PresentedPose(), body.Name, held, _ticks), held, Human.TotalSpeed);
            return;
        }

        if (_firstPerson.Build(Human, held, _ticks, ModelAttachments) is not { } view)
        {
            return;
        }

        int index = 0;
        foreach (TitleViewModelPart part in view.Parts)
        {
            if (part.Pose is not null || !string.Equals(part.Model, held, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index++ == ejection.WeaponObject)
            {
                Vector3f local = view.Orientation.Transposed().Transform(part.Position);
                Vector3d at = new(view.Origin.X + local.X, view.Origin.Y + local.Y, view.Origin.Z + local.Z);
                casings.Eject(ejection, part.Model, at, view.Orientation * part.Orientation, Human.TotalSpeed);
                return;
            }
        }
    }

    /// <summary>Throws a casing from the weapon object a body's frame holds, the weapon objects counted in the frame's order.</summary>
    /// <param name="casings">Where it flies.</param>
    /// <param name="ejection">What the shot drew for it.</param>
    /// <param name="frame">The body's models as this tick poses them.</param>
    /// <param name="held">The weapon in hand's model.</param>
    /// <param name="holderSpeed">The holder's speed.</param>
    internal static void EjectFrom(
        Weapons.Igi2GunCasings casings,
        in Weapons.Igi2CasingEjection ejection,
        IReadOnlyList<TitleWorldModel> frame,
        string held,
        Vector3f holderSpeed)
    {
        int index = 0;
        foreach (TitleWorldModel model in frame)
        {
            if (model.Pose is null && string.Equals(model.Model, held, StringComparison.OrdinalIgnoreCase) && index++ == ejection.WeaponObject)
            {
                casings.Eject(ejection, model.Model, model.Position, model.Orientation, holderSpeed);
                return;
            }
        }
    }

    /// <summary>Gets or sets the level's automatic guns.</summary>
    public IReadOnlyList<Weapons.Igi2AiStationaryGunHolderTask> Turrets { get; set; } = [];

    /// <summary>Gets or sets the level's in-game menu, which the shell presents.</summary>
    public Menus.Igi2InGameMenu? Menu { get; set; }

    /// <summary>Gets or sets the mission's save games.</summary>
    public Saves.Igi2MissionSaves? Saves { get; set; }

    /// <summary>Gets or sets the level's panes of glass.</summary>
    public Interaction.Igi2GlassContainer Glass { get; set; } = new();

    /// <inheritdoc/>
    public ITitleSaveGames? SaveGames => Saves;

    /// <inheritdoc/>
    /// <remarks>The level flow's <c>Complete</c> and <c>Failed</c> scripts, failure first (<c>LevelFlow_RunHandler</c>).</remarks>
    public TitleMissionOutcome Outcome => Level?.LevelFlow switch
    {
        { IsFailed: true } => TitleMissionOutcome.Failed,
        { IsComplete: true } => TitleMissionOutcome.Completed,
        _ => TitleMissionOutcome.Running,
    };

    /// <inheritdoc/>
    /// <remarks>The player's weapons are left for the mission that follows, <c>Game_SaveCarryover</c> (<c>Game_RunHandler</c>).</remarks>
    public void MissionCompleted(int nextMissionId)
    {
        if (Saves is { } saves && Level is { } level && PlayerSpawn.FindGameplayTask(level.Root) is Igi2HumanPlayerTask task)
        {
            saves.SaveCarryOver(task, nextMissionId);
        }
    }

    /// <summary>Gets or sets the level's cars.</summary>
    public IReadOnlyList<Vehicles.Igi2CarTask> Cars { get; set; } = [];

    /// <summary>Gets or sets the level's mounted guns, whose barrels are drawn with the soldiers.</summary>
    public IReadOnlyList<Weapons.Igi2StationaryGunTask> StationaryGuns { get; set; } = [];

    /// <summary>Gets or sets the level's list of what humans tell their place to, <c>GameFunctions_ptNotifyQTaskList</c>.</summary>
    public Weapons.Igi2NotifyList Notified { get; set; } = new();

    /// <summary>Gets or sets the level the player's mines are laid in, or <see langword="null"/>.</summary>
    public Igi2LevelMines? Mines { get; set; }

    /// <summary>Gets or sets the overlay the scripts have put on the game screen, which the HUD draws.</summary>
    public Igi2ScreenOverlay? ScreenOverlay { get; set; }

    /// <summary>Gets or sets the level's countdowns, which the HUD draws while each is shown (<see cref="Igi2MissionTimerTask.IsShown"/>).</summary>
    public IReadOnlyList<Igi2MissionTimerTask> MissionTimers { get; set; } = [];

    /// <summary>Gets or sets the level's figures (<see cref="World.Igi2EditBoneObjTask"/>), drawn with the soldiers.</summary>
    public IReadOnlyList<World.Igi2EditBoneObjTask> Figures { get; set; } = [];

    /// <inheritdoc/>
    /// <remarks>
    /// The level's pickups not yet taken, each the weapon's <c>Fixed Model</c> where it lies, then the grenades in flight, each
    /// its <c>ProjectileModel</c>, then the soldiers, each posed with the weapon in its hand, then the figures; built once a tick,
    /// and again whenever the sector cull decides a new frame, because it may leave a soldier out between two ticks.
    /// </remarks>
    public IReadOnlyList<TitleWorldModel> WorldModels
    {
        get
        {
            Igi2SectorVisibility? cull = Visibility as Igi2SectorVisibility;
            long frame = cull?.Frame ?? -1;
            if (_lyingTick != _ticks || _lyingFrame != frame)
            {
                bool infra = Human.IsInfraEnabled;
                _lying = Gather(cull is null ? null : body => cull.IsHumanVisible(body, infra));
                _lyingTick = _ticks;
                _lyingFrame = frame;
            }

            return _lying;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Every soldier, whatever the sector cull left out of <see cref="WorldModels"/>.</remarks>
    public IReadOnlyList<TitleWorldModel> OutlineModels
    {
        get
        {
            if (Visibility is not Igi2SectorVisibility)
            {
                return WorldModels;
            }

            if (_outlineTick != _ticks)
            {
                _outline = Gather(null);
                _outlineTick = _ticks;
            }

            return _outline;
        }
    }

    private TitleWorldModel[] Gather(Func<Soldiers.Igi2HumanSoldier, bool>? isDrawn)
    {
        // Everything but the soldiers is the tick's and is gathered once a tick; only the soldiers the sector cull lets through
        // change between two ticks, and the cull decides a new frame for every frame drawn.
        if (_gatheredTick != _ticks)
        {
            GatherTick();
            _gatheredTick = _ticks;
        }

        IReadOnlyList<TitleWorldModel> soldiers = Soldiers?.Models(_ticks, isDrawn) ?? [];
        TitleWorldModel[] all = new TitleWorldModel[_gatheredBefore.Length + soldiers.Count + _gatheredAfter.Length];
        _gatheredBefore.CopyTo(all, 0);
        for (int i = 0; i < soldiers.Count; i++)
        {
            all[_gatheredBefore.Length + i] = soldiers[i];
        }

        _gatheredAfter.CopyTo(all, _gatheredBefore.Length + soldiers.Count);
        return all;
    }

    private long _gatheredTick = long.MinValue;
    private TitleWorldModel[] _gatheredBefore = [];
    private TitleWorldModel[] _gatheredAfter = [];

    // The tick's models either side of the soldiers, in the order Gather hands them out.
    private void GatherTick()
    {
        long tick = _ticks;
        IEnumerable<TitleWorldModel> pickups = Igi2LevelModels.Lying(Human.Pickups, tick);
        IEnumerable<TitleWorldModel> flying = Igi2LevelModels.InFlight(Grenades, tick);
        IEnumerable<TitleWorldModel> figures = Figures.SelectMany(f => f.Models(_ticks));
        // A container that holds them out of the world holds their models out too, as it does the placements'.
        IEnumerable<TitleWorldModel> barrels = StationaryGuns
            .Where(static g => OpenIGI.Game.World.ConditionalContainerGate.IsTaskActive(g))
            .Select(g => g.Barrel(tick)).OfType<TitleWorldModel>();
        IEnumerable<TitleWorldModel> wrecks = Cars
            .Where(static c => OpenIGI.Game.World.ConditionalContainerGate.IsTaskActive(c))
            .Select(c => c.Wreck(tick)).OfType<TitleWorldModel>();
        IEnumerable<TitleWorldModel> laid = Mines is { } mineWorld
            ? mineWorld.Laid.Select(m => new TitleWorldModel(m.Explosive.Model, m.Position, Rows([.. m.Frame]), Instance: m, Tick: tick))
            : [];
        // human.c Human_GetHumanCameraInfoHandler; humancamera.c HumanCamera_MovementSetCamera:
        // interaction mode draws the working body rather than the first-person weapon, as the remaster's view over the
        // shoulder does.
        IReadOnlyList<TitleWorldModel> working = IsBodyShown && Human.Model is { } body
            ? Igi2BodyFrame.Build(Human, Human.PresentedPose(), body.Name, Inventory?.Current?.WeaponType.FixedModel, _ticks, Weapons?.Firing.Flashes) : [];
        IEnumerable<TitleWorldModel> casings = Casings?.Models(tick) ?? [];

        // Explosion0's thrown pieces: each container's model, at the tenth of its size each piece was given, moving between ticks.
        IEnumerable<TitleWorldModel> pieces = Explosions is { } explosions ? ExplosionPieceModels.Of(explosions, _ticks) : [];
        _gatheredBefore = [.. pickups, .. flying];
        _gatheredAfter = [.. figures, .. barrels, .. wrecks, .. laid, .. working, .. casings, .. pieces, .. NetworkModels()];
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
        OpenIGI.Game.Ai.AiDifficulty difficulty,
        Igi2AnimationLibrary? clips = null,
        Igi2BoneHierarchy? rig = null,
        Igi2PlayerWeapons? weapons = null,
        Igi2BoneHierarchy? firstPersonRig = null,
        Igi2Sounds? sounds = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(general);

        sounds ??= new Igi2Sounds(new Igi2SoundDefinitions(), null);
        Igi2LevelSurroundings surroundings = new(world, sounds);
        IIgi2PlayerWeaponControl weapon = weapons ?? (IIgi2PlayerWeaponControl)new Igi2NoWeaponControl();
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
        float[] facing = EngineEuler.Rotation(0f, 0f, gamma);
        human.Orientation = new Matrix3f(
            facing[0], facing[1], facing[2],
            facing[3], facing[4], facing[5],
            facing[6], facing[7], facing[8]);
        human.Create(difficulty);
        human.MaintainCollisionZOffset();

        // The view would first be placed on the first tick; placing it now gives the host's first frames the eyes
        // rather than the world's origin. It draws nothing from the run stream: a new view does not shake.
        human.UpdateViewBody(updateAnimation: false);
        return new Igi2TitlePlayer(human, weapons, surroundings) { Sounds = sounds };
    }

    /// <inheritdoc/>
    public void Tick(in TitlePlayerInput input)
    {
        // The weapon wheel's pick since the last tick, taken as the tick begins as a number key's would be.
        if (input.WheelPick is { } pick)
        {
            ChooseFromWheel(pick.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        TakeTickAim(input);
        ViewError.Tick();
        TakeCheats();
        Igi2PlayerAction held = Map(input.Held) | (Igi2PlayerAction)(input.TitleHeld | input.TitlePressed);
        Igi2PlayerAction pressed = Map(input.Pressed) | (Igi2PlayerAction)input.TitlePressed;
        _pressedThisTick = pressed;
        _reader.MouseSensitivity = input.MouseSensitivity;
        Human.SteadyTurns = GameplayCVars.SteadyTurns.Value;
        Human.Loadout = GameplayCVars.Igi2Loadout.Value;
        Human.GrenadeCook = GameplayCVars.GrenadeCook.Value;
        TakeMovementChoice(Igi2MovementChoice.Current);
        // Retail keeps the profile's flag the other way round from its menu (config.c: isInvertMouse is 1 by default, and the menu's
        // get and set negate it); the host's says what the player chose.
        _reader.InvertMouse = !input.InvertMouse;
        Human.InvertMouse = !input.InvertMouse;
        // Project I.G.I.'s movement looks as that game does, one mouse count one step whatever the frame pacing: Smooth Motion's
        // unfiltered look, without the tick averaging, the fine-aim curve and the clamp.
        Human.UnfilteredMouse = (input.UnfilteredMouse || Human.Movement == Igi2PlayerMovement.Igi1) && CanPreviewLook;
        _lookInput = input;
        _rawKeys = input.Raw;
        _reader.Read(new Igi2PlayerInputState(
            Analog(input.MouseX, Human.UnfilteredMouse), Analog(input.MouseY, Human.UnfilteredMouse), held, pressed), _frame);
        TakeWheelPick();
        if (Level is { } level)
        {
            Flow?.IsBreakKeyDown = FollowedBreakKey ?? input.BreakCutscene;
            level.Tick();

            // The glass container is the level's last child, made as the first pane was: its panes' glass tick follows the level's.
            Glass.Run();
            StatusMessages.Run();
        }
        else
        {
            RunPlayer();
            Dynamic.Run();
            RunPlayerDelayed();

            // Standing in another game's level, the player's own screen prints what is said to it.
            StatusMessages.Run();
        }

        _movementCamera.Run(Human, CollisionWorld, input.MouseX * CountsToAnalog, input.MouseY * CountsToAnalog);
        ResetLook();
        Casings?.Run();
        ReadBinoculars();
        Sounds.Run();
        _ticks++;

        // What the tick changed, told once it is over; nothing in it reads what a listener does.
        if (Level is not null)
        {
            EventWatch?.Observe();
        }
    }

    /// <summary>
    /// The player's run handler: the shots its weapon fired last tick are traced, and the rest of its tick is queued for the
    /// delayed pass (<c>HumanPlayer_RunHandler</c>, <c>igi2 0x4B8A30</c>).
    /// </summary>
    internal void RunPlayer()
    {
        if (StoodFor is not null && !CarryStandInOnGun())
        {
            if (StandInPlayer is { } standIn)
            {
                standIn.Tick(_lookInput);

                // The level's documents, key cards and disks are collected by the player standing in, as by this one.
                if (StandIn is not { IsDead: true })
                {
                    Human.OfferGenericPickups(standIn.EyePosition);
                    DesignateForStandIn(standIn);
                    ToggleThermalForStandIn();
                }
            }

            return;
        }

        Weapons?.RunGunShot();
        RunNetworkGunShots();
        if (Level is { } level)
        {
            // The player's first child, HumanPlayerInput, reads the controls into the frame it sends up as the player's update.
            if (Input is { } input)
            {
                input.Run(level);
                input.Apply(_frame);
                Computer?.IsEnabled = input.IsComputerEnabled;
            }

            level.Delay(RunPlayerDelayed);
        }
    }

    /// <summary>The player's delayed half: its pickups, update, zoom and delayed handler (<c>HumanPlayer_RunDelayedHandler</c>, <c>igi2 0x4B8A50</c>).</summary>
    private void RunPlayerDelayed()
    {
        Human.Pickups?.Run();
        if (Human.UnfilteredMouse) { WriteSmoothLook(); }
        Human.Ticks = (int)_ticks;
        TakeOverShoulderAim();

        // A co-op client's player draws from a stream of its own, set from the command's seed before each tick, as the server's
        // copy of it is set before it runs the command: the same rounds scatter the same way on both.
        uint seed = 0;
        if (SeedsCommands)
        {
            seed = Multiplayer.Igi2Command.SeedFor(_ticks);
            Multiplayer.Igi2Command.Reseed(Human.Random, seed);
        }

        CommandStart = new Multiplayer.Igi2CommandStart(
            Human.IdealGamma,
            Human.View.IdealPitch,
            Human.UnfilteredMouse,
            Human.SteadyTurns,
            Human.InvertMouse,
            Human.Movement,
            Human.Igi1Weight,
            seed,
            Human.Loadout,
            Human.GrenadeCook,
            Human.AimConvergence);
        Human.Update(_frame);

        // HumanView_RunHandler: the view is a child of the player run after HumanPlayerInput, whose run handler reads the
        // channels and updates the player at once, so a zoom fired this tick takes its first step this tick.
        Human.View.RunZoom();
        Human.RunDelayed();

        // HumanPlayer_RunDelayedHandler: in the world, the inventory key pressed shows or hides the inventory, a profile option.
        if (Human.IsActive && Human.Channels.WasPressed(Humans.Igi2HumanChannel.Inventory) && GameConfig?.Active is { } profile)
        {
            profile.HideInventory = !profile.HideInventory;
        }

        RunNetworkHumans();

        // The computer's menu, a task of the game screen, runs after the player, and its screens with it.
        RunComputerSaves();
        Computer?.Run();
        RunComputerDisplay();

        // The original's render of the tick takes the player's pose: here, once, whoever draws it or not.
        Human.TakeTickPose();
    }

    /// <summary>Nine floats row by row as a matrix.</summary>
    private static Matrix3f Rows(float[] m) => new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);

    /// <summary>The mouse's counts as a 255th each; retail clamps to one, while Smooth Motion retains the raw range.</summary>
    private static float Analog(float counts, bool unfilteredMouse = false)
    {
        float analog = ((Fpu)(double)counts * CountsToAnalog).F;
        return unfilteredMouse ? analog : analog < -1f ? -1f : analog > 1f ? 1f : analog;
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
