using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.Systems;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Level;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>The ported player as the host steps it.</summary>
public sealed partial class Igi2TitlePlayer : ITitlePlayer, ITitleFirstPerson, ITitleWorldModels
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
        (PlayerAction.ToggleWalkRun, Igi2PlayerAction.ToggleWalkRun),
        (PlayerAction.Binoculars, Igi2PlayerAction.Binoculars),
        (PlayerAction.MapComputer, Igi2PlayerAction.ComputerMap),
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

    private TitleGroundDecal[] _decals = [];
    private long _decalsTick = -1;

    /// <inheritdoc/>
    /// <remarks>
    /// Each body's mark where it shows (<see cref="Humans.Igi2HumanShadow"/>), the player's and the soldiers': coloured by the
    /// light where it lay, or nine tenths grey, times its strength above the ground (<c>Shadow_MagicObjEnterHandler</c>).
    /// </remarks>
    public IReadOnlyList<TitleGroundDecal> GroundDecals
    {
        get
        {
            if (_decalsTick != _ticks)
            {
                IEnumerable<Humans.Igi2Human> bodies = Soldiers?.Bodies ?? [];
                _decals = [.. bodies.Prepend(Human).Select(static h => Decal(h.Shadow)).OfType<TitleGroundDecal>()];
                _decalsTick = _ticks;
            }

            return _decals;
        }
    }

    private static TitleGroundDecal? Decal(Humans.Igi2HumanShadow shadow)
    {
        if (!shadow.IsEnabled || !shadow.IsPositionValid || shadow.Size <= 0f)
        {
            return null;
        }

        float shade = (shadow.Light ?? 0.9f) * shadow.Strength;
        byte channel = (byte)Math.Clamp((int)(shade * 255f), 0, 255);
        return new TitleGroundDecal(
            "LOCAL:common/textures/" + shadow.Texture,
            shadow.Position,
            shadow.Normal,
            shadow.Size,
            Humans.Igi2HumanShadow.Hover,
            OpenIGI.Core.Graphics.Bgra32.FromRgba(channel, channel, channel, 255));
    }

    private readonly Igi2LevelSurroundings _surroundings;

    private Igi2TitlePlayer(Igi2HumanPlayer human, Igi2PlayerWeapons? weapons, Igi2LevelSurroundings surroundings)
    {
        Human = human;
        Weapons = weapons;
        _surroundings = surroundings;
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
    public IPlacementVariant? Variants { get; set; }

    /// <summary>Gets or sets the level's tick the player runs inside, or <see langword="null"/> to run the player alone.</summary>
    public Igi2LevelRun? Level { get; set; }

    /// <summary>Gets or sets the game-wide switches the level's scripts flip, or <see langword="null"/> for none.</summary>
    public Igi2FlowState? Flow { get; set; }

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
    public bool IsMenuEnabled => Flow?.IsMenuEnabled ?? true;

    /// <inheritdoc/>
    public Vector3d Position => Human.Position;

    /// <inheritdoc/>
    /// <remarks>
    /// A cutscene's camera, while one is placed, outranks the player's eyes; a mounted gun's seat, while the view is overridden,
    /// stands in for them (the human camera's mode 4).
    /// </remarks>
    public Vector3d EyePosition => Level?.CutsceneView is { } cut
        ? cut.Position
        : IsMapShown && ComputerDisplay?.Map is { } map ? map.Position
        : Human.CameraMode is 3 or 5 ? _movementCamera.Position
        : Human.View.IsOverride ? Human.View.OverridePosition : Human.View.Position;

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
        : Human.CameraMode is 3 or 5 ? 1f : Human.View.FieldOfView;

    /// <inheritdoc/>
    /// <remarks>
    /// Built once a tick, on the first ask after it, from the view as the tick left it; none while a cutscene's camera is placed,
    /// which is not the player's eyes.
    /// </remarks>
    public TitleFirstPersonView? FirstPerson
    {
        get
        {
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

        if (Human.CameraMode is 3 or 5 && Human.Model is { } body)
        {
            EjectFrom(casings, ejection, Igi2BodyFrame.Build(Human, Human.BodyPose(), body.Name, held, _ticks), held, Human.TotalSpeed);
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

    /// <summary>Gets or sets the level's figures (<see cref="World.Igi2EditBoneObjTask"/>), drawn with the soldiers.</summary>
    public IReadOnlyList<World.Igi2EditBoneObjTask> Figures { get; set; } = [];

    /// <inheritdoc/>
    /// <remarks>
    /// The level's pickups not yet taken, each the weapon's <c>Fixed Model</c> where it lies, then the grenades in flight, each
    /// its <c>ProjectileModel</c>, then the soldiers, each posed with the weapon in its hand, then the figures; built once a tick.
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
                        .Concat(grenades.Dynamic.Tasks.OfType<Weapons.Igi2ImpactGrenade>()
                            .Where(static g => g.Parameters.Model.Length != 0)
                            .Select(static g => new TitleWorldModel(g.Parameters.Model, g.Position, Rows(g.Orientation))))
                        .Concat(grenades.Dynamic.Tasks.OfType<Weapons.Igi2Missile>()
                            .Select(static m => new TitleWorldModel(m.Model, m.Position, Rows(m.Body.Orientation))))
                    : [];
                IEnumerable<TitleWorldModel> soldiers = Soldiers?.Models(_ticks) ?? [];
                IEnumerable<TitleWorldModel> figures = Figures.SelectMany(f => f.Models(_ticks));
                IEnumerable<TitleWorldModel> barrels = StationaryGuns.Select(static g => g.Barrel()).OfType<TitleWorldModel>();
                IEnumerable<TitleWorldModel> wrecks = Cars.Select(static c => c.Wreck()).OfType<TitleWorldModel>();
                IEnumerable<TitleWorldModel> laid = Mines is { } mineWorld
                    ? mineWorld.Laid.Select(static m => new TitleWorldModel(m.Explosive.Model, m.Position, Rows([.. m.Frame])))
                    : [];
                // human.c Human_GetHumanCameraInfoHandler; humancamera.c HumanCamera_MovementSetCamera:
                // interaction mode draws the working body rather than the first-person weapon.
                IReadOnlyList<TitleWorldModel> working = Human.CameraMode is 3 or 5 && Human.Model is { } body
                    ? Igi2BodyFrame.Build(Human, Human.BodyPose(), body.Name, Inventory?.Current?.WeaponType.FixedModel, _ticks, Weapons?.Firing.Flashes) : [];
                IEnumerable<TitleWorldModel> casings = Casings?.Models() ?? [];
                _lying = [.. pickups, .. flying, .. soldiers, .. figures, .. barrels, .. wrecks, .. laid, .. working, .. casings];
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
        return new Igi2TitlePlayer(human, weapons, surroundings) { Sounds = sounds };
    }

    /// <inheritdoc/>
    public void Tick(in TitlePlayerInput input)
    {
        Igi2PlayerAction held = Map(input.Held) | (Igi2PlayerAction)(input.TitleHeld | input.TitlePressed);
        Igi2PlayerAction pressed = Map(input.Pressed) | (Igi2PlayerAction)input.TitlePressed;
        _reader.MouseSensitivity = input.MouseSensitivity;
        _reader.InvertMouse = input.InvertMouse;
        Human.InvertMouse = input.InvertMouse;
        Human.UnfilteredMouse = input.UnfilteredMouse && CanPreviewLook;
        _lookInput = input;
        _rawKeys = input.Raw;
        _reader.Read(new Igi2PlayerInputState(
            Analog(input.MouseX, Human.UnfilteredMouse), Analog(input.MouseY, Human.UnfilteredMouse), held, pressed), _frame);
        if (Level is { } level)
        {
            Flow?.IsBreakKeyDown = input.BreakCutscene;
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
        }

        _movementCamera.Run(Human, CollisionWorld, input.MouseX * CountsToAnalog, input.MouseY * CountsToAnalog);
        ResetLook();
        Casings?.Run();
        ReadBinoculars();
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

    /// <summary>The player's delayed half: its pickups, zoom, update and delayed handler (<c>HumanPlayer_RunDelayedHandler</c>, <c>igi2 0x4B8A50</c>).</summary>
    private void RunPlayerDelayed()
    {
        Human.Pickups?.Run();
        Human.View.RunZoom();
        if (Human.UnfilteredMouse) { WriteSmoothLook(); }
        Human.Ticks = (int)_ticks;
        Human.Update(_frame);
        Human.RunDelayed();

        // The computer's menu, a task of the game screen, runs after the player, and its screens with it.
        RunComputerSaves();
        Computer?.Run();
        RunComputerDisplay();
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
