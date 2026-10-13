using OpenIGI.Engine.Titles;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2;

/// <summary>I.G.I. 2: Covert Strike's content, as the modules a session registers.</summary>
/// <remarks>
/// The list grows one module per gameplay milestone. Everything not covered by a module still loads:
/// a type level data declares with no class behind it becomes a <c>DeclaredTask</c> placeholder with its
/// parameters intact, so a level is always structurally complete and only ever behaviourally partial.
/// </remarks>
public static class Igi2ContentModules
{
    /// <summary>Gets every module, in the order a session registers them.</summary>
    /// <remarks>
    /// Order is behaviour: a module whose natives another module's tables call has to declare first, and
    /// the original's own start-up order is the specification for it.
    /// </remarks>
    public static IReadOnlyList<IContentModule> All { get; } =
    [
        new Igi2ScriptVocabularyModule(),
        new Igi2SoundContentModule(),
        new Igi2FlowContentModule(),
        new Igi2WeaponContentModule(),
        new Igi2MaterialContentModule(),
        new Igi2AiContentModule(),
        new Igi2PlayerContentModule(),
        new Igi2AnimTriggerContentModule(),
        new Igi2PhysicsObjectContentModule(),
        new Igi2TerrainContentModule(),
        new Igi2ForestContentModule(),
        new Igi2WaterContentModule(),
        new Igi2SkyContentModule(),
        new Igi2MagicObjContentModule(),
        new Igi2ModelLodContentModule(),
        new Igi2TaskTypeConstantsModule(),
    ];
}

/// <summary>The script identifiers every I.G.I. 2 level depends on.</summary>
/// <remarks>
/// <para>
/// <b>There are only five, and they were counted rather than discovered.</b> Reading the symbol tables
/// of all 24 shipped <c>objects.qvm</c> together gives the complete vocabulary a level rests on: the two
/// task commands and <c>TRUE</c>/<c>FALSE</c>, which the engine already provides, plus these. Finding
/// them one failing level at a time would have taken as many passes as there are levels and still left
/// no evidence the list was complete.
/// </para>
/// <para>
/// The values come from <c>igi2.exe</c> — <c>AISquad_RegisterScriptValues</c> (<c>0x49A880</c>) for the
/// AI types and <c>OverlayHolder_Open</c> (<c>0x533BC0</c>) for the overlays. Reading them mattered more
/// than it looks: a wrong value does not fail. It lands as a parameter on a task, the tree still builds,
/// and it resurfaces much later looking like a gameplay bug with no trace back to here.
/// </para>
/// </remarks>
public sealed class Igi2ScriptVocabularyModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Script vocabulary";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Natives.RegisterConstant("AIType_Defensive", 0);
        context.Natives.RegisterConstant("AIType_Offensive", 1);
        context.Natives.RegisterConstant("AIType_OnVehicle", 2);

        context.Natives.RegisterConstant("OVERLAY_BINOCULAR", 0);
        context.Natives.RegisterConstant("OVERLAY_SNIPER", 2);

        // The criteria an AreaActivate is written in, each the bit its module registers (AreaActivateQTask_RegisterCriteria).
        Flow.Igi2AreaCriteria.Register(context.Natives);

        // Config_Open: the input buttons, devices and key sources the controls name, the languages and the difficulties.
        Config.Igi2ConfigConstants.Register(context.Natives);
        Config.Igi2GameConstants.Register(context.Natives);

        // Config_Open: the configuration's file natives, its graphics options and the settings the menus and scripts read.
        context.Services.GetOrProvide(() => new Config.Igi2GameConfig()).Register(context.Natives);
    }

    /// <inheritdoc/>
    /// <remarks>At start-up, <c>Config_LoadConfig</c>: the player profiles out of <c>config.qvm</c>.</remarks>
    public void LoadTables(ContentLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Services.Require<Config.Igi2GameConfig>().Load(context.Resources, context.Natives);
    }
}

/// <summary>The player the level places.</summary>
/// <remarks>
/// <para>
/// The player is the first game's <see cref="HumanPlayerTask"/> with the sequel's six further parameters bound
/// (<see cref="Player.Igi2HumanPlayerTask"/>): its starting weapons, first-person model, visibility triple and carry-over
/// flag. What the class carries that a placeholder cannot is the level's own spawn: <c>PlayerSpawn</c> looks for a
/// <c>HumanPlayerTask</c>, and without one a level that declares its player perfectly well appears to declare none.
/// </para>
/// <para>
/// <b>That absence was not a missing feature but a failing mission.</b> Dropped where the debugging
/// camera opens rather than where the level puts him, the player stands outside the
/// <c>AreaActivate</c> volume the mission is played inside; <c>location1/level1</c>'s LevelFlow reads
/// <c>!AreaActivate_178.nActive</c> as "left the area", sends its LEFT_AREA message, and fails the
/// mission about two and a half seconds in -- the grace period MainMenu state 9 counts out.
/// </para>
/// </remarks>
public sealed class Igi2PlayerContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Characters";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Types.Register<Player.Igi2HumanPlayerTask>("HumanPlayer");
        context.Types.Register<Player.HumanPlayerConfigItemTask>(Player.HumanPlayerConfigItemTask.TypeName);
        context.Types.Register<Player.Igi2HumanPlayerInputTask>(Player.Igi2HumanPlayerInputTask.TypeName);
        Scripting.Igi2ScriptFunctions functions = context.Services.GetOrProvide(() => new Scripting.Igi2ScriptFunctions());
        context.Services.GetOrProvide(() => new Player.Igi2PlayerTuning(context.Logger)).RegisterNatives(context.Natives, functions);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The player's tuning, out of <c>humanplayer.qvm</c>, as <c>Flow_CreateHandler</c> (<c>igi2 0x401600</c>) reads
    /// it before any level. A character's model and animations arrive with the level's packs.
    /// </remarks>
    public void LoadTables(ContentLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Services.Require<Player.Igi2PlayerTuning>().Load(
            context.Resources, context.Natives, context.Tasks, context.Services.Require<Scripting.Igi2ScriptFunctions>());
    }
}

/// <summary>The ammunition and weapons: their task types, the constants their scripts use, and the two scripts.</summary>
/// <remarks>
/// Ahead of the player's module, because <c>Flow_CreateHandler</c> (<c>igi2 0x401600</c>) loads the weapons before the
/// player's tuning and the tuning names them by their <c>WEAPON_ID_*</c> and <c>AMMO_ID_*</c> constants.
/// </remarks>
public sealed class Igi2WeaponContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Weapons";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Types.Register<Weapons.Igi2AmmoTypeTask>(Weapons.Igi2AmmoTypeTask.TypeName);
        context.Types.Register<Weapons.Igi2WeaponTypeTask>(Weapons.Igi2WeaponTypeTask.TypeName);
        context.Types.Register<Weapons.Igi2WeaponTypeParameterContainerTask>(Weapons.Igi2WeaponTypeParameterContainerTask.TypeName);
        context.Types.Register<Weapons.Igi2TagItemInt32Task>(Weapons.Igi2TagItemInt32Task.TypeName);
        context.Types.Register<Weapons.Igi2TagItemReal32Task>(Weapons.Igi2TagItemReal32Task.TypeName);
        context.Types.Register<Weapons.Igi2TagItemString16Task>(Weapons.Igi2TagItemString16Task.TypeName);
        context.Types.Register<Weapons.Igi2TagItemBool8Task>(Weapons.Igi2TagItemBool8Task.TypeName);
        context.Types.Register<Weapons.Igi2GunTask>(Weapons.Igi2GunTask.TypeName);
        context.Types.Register<Weapons.Igi2ZoomTask>(Weapons.Igi2ZoomTask.TypeName);
        context.Types.Register<Pickups.Igi2GunPickupTask>(Pickups.Igi2GunPickupTask.TypeName);
        context.Types.Register<Pickups.Igi2GenericPickupTask>(Pickups.Igi2GenericPickupTask.TypeName);
        context.Types.Register<Weapons.Igi2ProjectileLauncherTask>(Weapons.Igi2ProjectileLauncherTask.TypeName);
        context.Types.Register<Weapons.Igi2C4BombTask>(Weapons.Igi2C4BombTask.TypeName);
        context.Types.Register<Weapons.Igi2ProximityMineTask>(Weapons.Igi2ProximityMineTask.TypeName);
        context.Types.Register<Weapons.Igi2RemoteMineTask>(Weapons.Igi2RemoteMineTask.TypeName);
        context.Types.Register<Weapons.Igi2MedipackTask>(Weapons.Igi2MedipackTask.TypeName);
        context.Types.Register<Weapons.Igi2KnifeTask>(Weapons.Igi2KnifeTask.TypeName);
        context.Types.Register<Weapons.Igi2LaserDesignatorTask>(Weapons.Igi2LaserDesignatorTask.TypeName);
        context.Types.Register<Weapons.Igi2PullPinTask>(Weapons.Igi2PullPinTask.TypeName);
        context.Types.Register<Weapons.Igi2FireModeTask>(Weapons.Igi2FireModeTask.TypeName);
        context.Types.Register<Weapons.Igi2LaserDotSwitchTask>(Weapons.Igi2LaserDotSwitchTask.TypeName);
        Weapons.Igi2WeaponConstants.Register(context.Natives, context.Types);
        Weapons.Igi2WeaponTables tables = context.Services.GetOrProvide(() => new Weapons.Igi2WeaponTables(context.Logger));
        Scripting.Igi2ScriptFunctions functions = context.Services.GetOrProvide(() => new Scripting.Igi2ScriptFunctions());
        context.Services.GetOrProvide(() => new Weapons.Igi2HumanWeapons(context.Natives, functions, tables));
    }

    /// <inheritdoc/>
    public void LoadTables(ContentLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Services.Require<Weapons.Igi2WeaponTables>().Load(context.Resources, context.Natives, context.Tasks);
    }
}

/// <summary>
/// The level's flow: the containers its tasks run under, the variables and clocks its scripts read, and the natives those
/// scripts flip (<see cref="Flow.Igi2LevelRun"/>).
/// </summary>
public sealed class Igi2FlowContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Level flow";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Types.Register<Flow.Igi2ContainerTask>(Flow.Igi2ContainerTask.TypeName);
        context.Types.Register<Flow.Igi2ConditionalContainerTask>(Flow.Igi2ConditionalContainerTask.TypeName);
        context.Types.Register<Flow.Igi2EditVariableTask>(Flow.Igi2EditVariableTask.TypeName);
        context.Types.Register<Flow.Igi2MissionScoreSettingsTask>(Flow.Igi2MissionScoreSettingsTask.TypeName);
        context.Types.Register<World.Igi2BuildingTask>(World.Igi2BuildingTask.TypeName);
        context.Types.Register<World.Igi2LevelInfoTask>(World.Igi2LevelInfoTask.TypeName);
        context.Types.Register<World.Igi2LightmapInfoTask>(World.Igi2LightmapInfoTask.TypeName);
        context.Types.Register<Audio.Igi2AmbientAreaTask>(Audio.Igi2AmbientAreaTask.TypeName);
        context.Types.Register<World.Igi2RotateAttachmentTask>(World.Igi2RotateAttachmentTask.TypeName);
        context.Types.Register<World.Igi2SplineObjTask>(World.Igi2SplineObjTask.TypeName);
        context.Types.Register<World.Igi2SplineObjWaypointTask>(World.Igi2SplineObjWaypointTask.TypeName);
        context.Types.Register<Interaction.Igi2MineFieldTask>(Interaction.Igi2MineFieldTask.TypeName);
        context.Types.Register<World.Igi2SmokeTask>(World.Igi2SmokeTask.TypeName);
        context.Types.Register<World.Igi2EditBoneObjTask>(World.Igi2EditBoneObjTask.TypeName);

        // The map computer's level data: its objectives, its limit and its highlights, and the markers they name.
        context.Types.Register<Computer.Igi2DefineComputerObjectiveTask>(Computer.Igi2DefineComputerObjectiveTask.TypeName);
        context.Types.Register<Computer.Igi2DefineComputerLimitTask>(Computer.Igi2DefineComputerLimitTask.TypeName);
        context.Types.Register<Computer.Igi2ComputerPlanTask>(Computer.Igi2ComputerPlanTask.TypeName);
        context.Types.Register<Computer.Igi2ComputerHilightTask>(Computer.Igi2ComputerHilightTask.TypeName);
        Computer.Igi2Computer.RegisterConstants(context.Natives);
        context.Services.GetOrProvide(static () => new Computer.Igi2Computer());
        context.Types.Register<Flow.Igi2CutSceneTask>(Flow.Igi2CutSceneTask.TypeName);
        context.Types.Register<Flow.Igi2EditCameraTask>(Flow.Igi2EditCameraTask.TypeName);
        context.Types.Register<Flow.Igi2LevelFlowTask>(Flow.Igi2LevelFlowTask.TypeName);
        context.Types.Register<Flow.Igi2SequenceCommandTask>(Flow.Igi2SequenceCommandTask.TypeName);
        context.Types.Register<Flow.Igi2SoundGeneratorTask>(Flow.Igi2SoundGeneratorTask.TypeName);
        context.Types.Register<Interaction.Igi2SwitchTask>(Interaction.Igi2SwitchTask.TypeName);
        context.Types.Register<Interaction.Igi2DoorTask>(Interaction.Igi2DoorTask.TypeName);
        context.Types.Register<Interaction.Igi2LiftTask>(Interaction.Igi2LiftTask.TypeName);
        context.Types.Register<Interaction.Igi2FloorTask>(Interaction.Igi2FloorTask.TypeName);
        context.Types.Register<Interaction.Igi2ConveyorBeltTask>(Interaction.Igi2ConveyorBeltTask.TypeName);
        context.Types.Register<Interaction.Igi2ExplodeObjectTask>(Interaction.Igi2ExplodeObjectTask.TypeName);
        context.Types.Register<Interaction.Igi2GenericTbaTask>(Interaction.Igi2GenericTbaTask.TypeName);
        context.Types.Register<Interaction.Igi2TerminalTask>(Interaction.Igi2TerminalTask.TypeName);
        context.Types.Register<Interaction.Igi2LadderTask>(Interaction.Igi2LadderTask.TypeName);
        context.Types.Register<Interaction.Igi2FenceTask>(Interaction.Igi2FenceTask.TypeName);
        context.Types.Register<Interaction.Igi2WireTask>(Interaction.Igi2WireTask.TypeName);
        context.Types.Register<Interaction.Igi2CabinetTask>(Interaction.Igi2CabinetTask.TypeName);
        context.Types.Register<Interaction.Igi2AlarmControlTask>(Interaction.Igi2AlarmControlTask.TypeName);
        context.Types.Register<Interaction.Igi2EditRigidObjTask>(Interaction.Igi2EditRigidObjTask.TypeName);
        context.Types.Register<Paths.Igi2SplinePathNodeTask>(Paths.Igi2SplinePathNodeTask.TypeName);
        context.Types.Register<Paths.Igi2SplinePathTask>(Paths.Igi2SplinePathTask.TypeName);
        context.Types.Register<Paths.Igi2SplinePathGuideTask>(Paths.Igi2SplinePathGuideTask.TypeName);
        context.Types.Register<Flow.Igi2StatusMessageTask>(Flow.Igi2StatusMessageTask.TypeName);
        context.Types.Register<Flow.Igi2AreaActivateTask>(Flow.Igi2AreaActivateTask.TypeName);
        context.Types.Register<Flow.Igi2LevelTimerTask>(Flow.Igi2LevelTimerTask.TypeName);
        context.Types.Register<Flow.Igi2OverlayHolderTask>(Flow.Igi2OverlayHolderTask.TypeName);
        context.Types.Register<Flow.Igi2AnimTask>(Flow.Igi2AnimTask.TypeName);
        context.Types.Register<World.Igi2RainEffectTask>(World.Igi2RainEffectTask.TypeName);
        context.Types.Register<World.Igi2DirlightTask>(World.Igi2DirlightTask.TypeName);
        context.Types.Register<World.Igi2DirlightKeyframeTask>(World.Igi2DirlightKeyframeTask.TypeName);
        context.Types.Register<World.Igi2GlobalLightTask>(World.Igi2GlobalLightTask.TypeName);
        context.Types.Register<World.Igi2GlobalLightKeyframeTask>(World.Igi2GlobalLightKeyframeTask.TypeName);
        context.Types.Register<World.Igi2LensflareTask>(World.Igi2LensflareTask.TypeName);
        context.Types.Register<World.Igi2LensflareItemTask>(World.Igi2LensflareItemTask.TypeName);
        context.Types.Register<World.Igi2BirdFlockTask>(World.Igi2BirdFlockTask.TypeName);
        context.Services.GetOrProvide(static () => new World.Igi2FlockRandom());
        context.Types.Register<Vehicles.Igi2TrainTask>(Vehicles.Igi2TrainTask.TypeName);
        context.Services.GetOrProvide(static () => new Flow.Igi2ScreenOverlay());
        Flow.Igi2ScreenOverlay.RegisterConstants(context.Natives);
        context.Types.Register<Flow.Igi2MissionTimerTask>(Flow.Igi2MissionTimerTask.TypeName);
        context.Types.Register<Interaction.Igi2SecurityCameraTask>(Interaction.Igi2SecurityCameraTask.TypeName);

        Scripting.Igi2ScriptFunctions functions = context.Services.GetOrProvide(() => new Scripting.Igi2ScriptFunctions());
        context.Services.GetOrProvide(() => new Flow.Igi2FlowState()).Register(context.Natives, functions);
        context.Services.GetOrProvide(() => new Flow.Igi2LevelNatives()).Register(context.Natives, functions);
        Flow.Igi2SequenceCommandTask.Register(context.Natives, functions);
        Paths.Igi2SplinePathGuideTask.Register(context.Natives, functions);
        context.Services.GetOrProvide(static () => new Flow.Igi2CutsceneShake());
        Interaction.Igi2ConveyorBeltTask.Register(context.Natives, functions);
        Flow.Igi2SoundGeneratorTask.Register(context.Natives, functions);
    }

    /// <inheritdoc/>
    public void LoadTables(ContentLoadContext context)
    {
    }
}

/// <summary>The sound definitions' four natives, bound to the table each level fills (<see cref="Audio.Igi2SoundDefinitions"/>).</summary>
/// <remarks>
/// <c>SoundSys_Open</c> (<c>igi2 0x42E9A0</c>) registers them at start-up; nothing is read until a level is, when
/// <c>Game_CreateHandler</c> (<c>0x4070E0</c>) runs the game's, the location's and the mission's scripts.
/// </remarks>
public sealed class Igi2SoundContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Sound definitions";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Services.GetOrProvide(() => new Audio.Igi2SoundDefinitions(context.Logger)).Register(context.Natives);
    }

    /// <inheritdoc/>
    /// <remarks>Nothing at start-up: the definitions are a level's.</remarks>
    public void LoadTables(ContentLoadContext context)
    {
    }
}

/// <summary>The session's materials: what every surface is made of, and what a bullet does to it.</summary>
/// <remarks>
/// <c>Flow_CreateHandler</c> (<c>igi2 0x401600</c>) reads them straight after the weapons, through
/// <c>GameMaterial_LoadParameters</c> (<c>0x433940</c>).
/// </remarks>
public sealed class Igi2MaterialContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Materials";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Services.GetOrProvide(() => new Igi2GameMaterials(context.Logger)).Register(context.Natives, context.Types);
    }

    /// <inheritdoc/>
    public void LoadTables(ContentLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Services.Require<Igi2GameMaterials>().Load(context.Resources, context.Natives);
    }
}

/// <summary>
/// The AI: its task types, the constants its scripts use, its tuning out of <c>settings.qvm</c>, and the module the level's soldiers
/// and squads think in, with its natives and default scripts.
/// </summary>
/// <remarks>
/// After the materials and before the player's tuning, the order <c>Flow_CreateHandler</c> (<c>igi2 0x401600</c>) loads the AI's
/// settings in.
/// </remarks>
public sealed class Igi2AiContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "AI";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Types.Register<Ai.Igi2HumanAiConfigTask>(Ai.Igi2HumanAiConfigTask.TypeName);
        context.Types.Register<Ai.Igi2HumanAiConfigItemTask>(Ai.Igi2HumanAiConfigItemTask.TypeName);
        context.Types.Register<Ai.Igi2AiGraphTask>(Ai.Igi2AiGraphTask.TypeName);
        context.Types.Register<Ai.Igi2HumanSoldierTask>(Ai.Igi2HumanSoldierTask.TypeName);
        context.Types.Register<Ai.Igi2HumanAiTask>(Ai.Igi2HumanAiTask.TypeName);
        context.Types.Register<Ai.Igi2AiSquadTask>(Ai.Igi2AiSquadTask.TypeName);
        context.Types.Register<Ai.Igi2PatrolPathTask>(Ai.Igi2PatrolPathTask.TypeName);
        context.Types.Register<Ai.Igi2PatrolPathCommandTask>(Ai.Igi2PatrolPathCommandTask.TypeName);
        Ai.Igi2AiConstants.Register(context.Natives);
        Ai.Igi2PatrolPathCommandTask.RegisterCommands(context.Natives);
        context.Services.GetOrProvide(() => new Scripting.Igi2ScriptFunctions());
        context.Services.GetOrProvide(() => new Ai.Igi2HumanAiConfig());

        // HumanAI_Open and AISquad_Open bind the soldiers' and squads' natives once, for every level the game plays.
        Ai.Runtime.Igi2AiWorld ai = context.Services.GetOrProvide(static () => new Ai.Runtime.Igi2AiWorld());
        ai.Natives = context.Natives;
        new Ai.Runtime.Igi2HumanAiNatives(ai).Register(context.Natives);
        new Ai.Runtime.Igi2AiSquadNatives(ai).Register(context.Natives);
    }

    /// <inheritdoc/>
    public void LoadTables(ContentLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Services.Require<Ai.Igi2HumanAiConfig>().Load(
            context.Resources, context.Natives, context.Services.Require<Scripting.Igi2ScriptFunctions>(), context.Tasks);

        // ... and read their default scripts (Script_New of common/ai/default.qsc and squaddefault.qsc).
        context.Services.Require<Ai.Runtime.Igi2AiWorld>().LoadScripts(context.Resources);
    }
}

/// <summary>The anim trigger types the clips fire, out of <c>animtrigger.qvm</c> (<see cref="Animation.Igi2AnimTriggers"/>).</summary>
/// <remarks>After the player's module, so the <c>HumanPlayer</c> type the script names is the player's own class.</remarks>
public sealed class Igi2AnimTriggerContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Anim triggers";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Services.GetOrProvide(() => new Animation.Igi2AnimTriggers(context.Logger)).Register(context.Natives, context.Types);

        // iLastWhizzbySoundTick (igi2 0x79748FC) lives for the whole run of the game, whatever level is played.
        context.Services.GetOrProvide(static () => new Player.Igi2WhizzbyClock());

        // iDeadBodySoundIndex (igi2 0x7D460CC) likewise.
        context.Services.GetOrProvide(static () => new Humans.Igi2DeadBodySoundCycle());
    }

    /// <inheritdoc/>
    /// <remarks>At start-up, as <c>AnimTrigger_ParseConfigFile</c> reads it before any level.</remarks>
    public void LoadTables(ContentLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Services.Require<Animation.Igi2AnimTriggers>().Load(context.Resources, context.Natives);
    }
}

/// <summary>The physics object types, out of the scripts under <c>LOCAL:physicsobj</c> (<see cref="Physics.Igi2PhysicsObjectTypes"/>).</summary>
/// <remarks>After the weapons' module, whose <c>TASKTYPE_GRENADE</c> and <c>TASKTYPE_MISSILE</c> the scripts name.</remarks>
public sealed class Igi2PhysicsObjectContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Physics objects";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Services.GetOrProvide(() => new Physics.Igi2PhysicsObjectTypes(context.Logger)).Register(context.Natives, context.Types);
    }

    /// <inheritdoc/>
    /// <remarks>At start-up, as <c>Flow_CreateHandler</c> (<c>igi2 0x401600</c>) walks the directory before any level.</remarks>
    public void LoadTables(ContentLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Services.Require<Physics.Igi2PhysicsObjectTypes>().Load(context.FileSystem, context.Resources, context.Natives);
    }
}

/// <summary>The terrain's task types: the tile grid, its placement, its material table and its holes.</summary>
/// <remarks>
/// <para>
/// Without these the four types load as placeholders — the tree is right and the parameter values are
/// there, but nothing knows a tile has an id, so no <c>.thm</c>, <c>.tmm</c> or <c>.tlm</c> is ever
/// asked for and the level renders with no ground.
/// </para>
/// <para>
/// <see cref="DiscardTask"/> fails the other way round: unregistered it loads as a placeholder and the
/// ground it should have taken away is drawn, so the hillside runs straight through the vehicle ramp
/// cut into it. Four of the shipped levels need it.
/// </para>
/// </remarks>
public sealed class Igi2TerrainContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Terrain";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Types.Register<TerrainTask>(TerrainTask.TypeName);
        context.Types.Register<TerrainMapTask>(TerrainMapTask.TypeName);
        context.Types.Register<TerrainMaterialTask>(TerrainMaterialTask.TypeName);
        context.Types.Register<DiscardTask>(DiscardTask.TypeName);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Nothing to load. A terrain's tables are its own child tasks and its tiles' map files, both of
    /// which arrive with the level rather than before it.
    /// </remarks>
    public void LoadTables(ContentLoadContext context)
    {
    }
}

/// <summary>The forest's task types: a stand of trees and the wind that moves it.</summary>
/// <remarks>
/// Without these the two types load as placeholders, so nothing knows a stand has a level id, and
/// without the id nothing can name its <c>forest_%d.dat</c> — the placement file is named by the task
/// rather than by a parameter, so an unrecognised task means an unreachable file.
/// </remarks>
public sealed class Igi2ForestContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Forest";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Types.Register<ForestTask>(ForestTask.TypeName);
        context.Types.Register<WindTask>(WindTask.TypeName);
    }

    /// <inheritdoc/>
    /// <remarks>Nothing to load: a stand's placement file arrives with the level, not before it.</remarks>
    public void LoadTables(ContentLoadContext context)
    {
    }
}

/// <summary>The water's task types: a sheet and the wave trains summed over it.</summary>
/// <remarks>
/// Without these the two types load as placeholders, so nothing knows a sheet has a level id or a
/// resource list, and its baked environment maps -- named from both -- are unreachable.
/// </remarks>
public sealed class Igi2WaterContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Water";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Types.Register<WaterTask>(WaterTask.TypeName);
        context.Types.Register<WaterLayerTask>(WaterLayerTask.TypeName);
    }

    /// <inheritdoc/>
    /// <remarks>Nothing to load: a sheet's environment maps arrive with the level, not before it.</remarks>
    public void LoadTables(ContentLoadContext context)
    {
    }
}

/// <summary>The sky's task types: the dome, its scrolling layers, and its colour keyframes.</summary>
/// <remarks>
/// Without these the three load as placeholders, and a sky layer's texture stays the string the level
/// authored rather than the one the engine actually opens — <c>FlatSkyLayer_FixFilename</c> rebases
/// every name onto <c>MISSION:textures/</c>, and only the registered class knows to do that.
/// </remarks>
public sealed class Igi2SkyContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Sky";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Types.Register<FlatSkyTask>(FlatSkyTask.TypeName);
        context.Types.Register<FlatSkyLayerTask>(FlatSkyLayerTask.TypeName);
        context.Types.Register<FlatSkyKeyframeTask>(FlatSkyKeyframeTask.TypeName);
    }

    /// <inheritdoc/>
    /// <remarks>Nothing to load: a sky's textures arrive with the level, not before it.</remarks>
    public void LoadTables(ContentLoadContext context)
    {
    }
}

/// <summary>The magic object types, out of <c>magicobjconfig.qvm</c> (<see cref="World.Igi2MagicObjTypes"/>).</summary>
public sealed class Igi2MagicObjContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Magic objects";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Types.Register<World.Igi2MagicObjConfigContainerTask>(World.Igi2MagicObjConfigContainerTask.TypeName);
        context.Types.Register<World.Igi2MagicObjConfigTask>(World.Igi2MagicObjConfigTask.TypeName);
        context.Services.GetOrProvide(static () => new World.Igi2MagicObjTypes());
    }

    /// <inheritdoc/>
    /// <remarks>At start-up, after every module has registered the types its <c>TASKTYPE_*</c> values name.</remarks>
    public void LoadTables(ContentLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Services.Require<World.Igi2MagicObjTypes>().Load(context.Resources, context.Natives, context.Tasks);
    }
}

/// <summary>The model LOD settings: the game's file at start-up (<see cref="World.Igi2ModelLodTable"/>), and the types a level's adds.</summary>
public sealed class Igi2ModelLodContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Model LOD settings";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Types.Register<World.Igi2ModelLodSettingsContainerTask>(World.Igi2ModelLodSettingsContainerTask.TypeName);
        context.Types.Register<World.Igi2LocalModelLodSettingsContainerTask>(World.Igi2LocalModelLodSettingsContainerTask.TypeName);
        context.Types.Register<World.Igi2ModelLodSettingsTask>(World.Igi2ModelLodSettingsTask.TypeName);
        context.Services.GetOrProvide(static () => new World.Igi2ModelLodTable());
    }

    /// <inheritdoc/>
    /// <remarks><c>Flow_CreateHandler</c> makes the game's container, which loads <c>lod.qsc</c> as it is made.</remarks>
    public void LoadTables(ContentLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        World.Igi2ModelLodTable table = context.Services.Require<World.Igi2ModelLodTable>();
        Engine.Tasks.GameTask container = context.Tasks.New(context.Tasks.Root, context.Tasks.Types.Get(World.Igi2ModelLodSettingsContainerTask.TypeName));
        table.Load(container, World.Igi2ModelLodTable.GameFile, context.Resources, context.Natives);
        table.KeepAsGame();
    }
}

/// <summary>The <c>TASKTYPE_*</c> constants, registered last so each is worth the id of the class every module before has registered.</summary>
public sealed class Igi2TaskTypeConstantsModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Task type constants";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Config.Igi2GameConstants.RegisterTaskTypes(context.Natives, context.Types);
    }

    /// <inheritdoc/>
    public void LoadTables(ContentLoadContext context)
    {
    }
}
