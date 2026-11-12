using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// Makes Project I.G.I.'s player for a loaded level: the rig, what it walks into and climbs, the weapon, ammunition and
/// material tables, the guns lying in the level, and <c>humanplayer.qvm</c>'s tuning.
/// </summary>
/// <param name="mission">The mission's world, which keeps the tables and the player's health.</param>
/// <param name="harm">What a fall and a powered fence do to the player.</param>
/// <param name="audio">The pickups' cues.</param>
/// <param name="sounds">The clatter of a dropped gun landing.</param>
/// <param name="animations">The animation sets the rig walks on and its first-person rig is posed by.</param>
/// <param name="host">What shows and plays it.</param>
public sealed partial class Igi1PlayerSetup(
    MissionWorld mission, Igi1PlayerHarm harm, Igi1MissionAudio audio, Igi1HumanSounds sounds, Igi1PlayerAnimations animations, IPlayerSetupHost host) : IPlayerBringUp
{
    /// <inheritdoc/>
    public BoneAnimationSet? BodyAnimations => animations.Body;

    /// <summary>Gets the weapon table, once <see cref="Begin"/> has read it.</summary>
    public WeaponTypeRegistry Weapons { get; private set; } = null!;

    /// <summary>Gets the ammunition table, once <see cref="Begin"/> has read it.</summary>
    public AmmoTypeRegistry Ammunition { get; private set; } = null!;

    /// <summary>Gets the rig being brought up, once <see cref="Begin"/> has made it.</summary>
    public PlayerCamera Candidate { get; private set; } = null!;

    // humanplayer.qvm as Configure read it, which tunes a co-op session's other players the same way.
    private HumanPlayerConfig? _config;

    // Where a glancing round whizzes, as the mission's own player's rounds were connected; a partner's are connected alike.
    private Action<Vector3d>? _deflected;

    // The first-person rig's set, which a partner's weapon is worked on as the player's is.
    private BoneAnimationSet? _viewRig;

    // What Begin was given, which a co-op session's other players are brought up over.
    private (LoadedMission Loaded, LevelTask LevelRoot, ObjectCollisionIndex Solids, MissionStringResolver Text)? _bringUp;

    /// <summary>Makes another player over what <see cref="Begin"/> was given, once it has run.</summary>
    /// <returns>The rig and its health, or <see langword="null"/> before <see cref="Begin"/>.</returns>
    public (PlayerCamera Player, HumanHealth Health)? BeginExtra() =>
        _bringUp is { } b ? BeginExtra(b.Loaded, b.LevelRoot, b.Solids, b.Text) : null;

    /// <summary>Gets where the level starts its player, for standing a co-op session's other players beside it.</summary>
    /// <returns>The spawn, or <see langword="null"/> before <see cref="Begin"/> or for a level that declares none.</returns>
    public PlayerSpawnPoint? LevelSpawn() =>
        _bringUp is { } b && PlayerSpawn.TryFindGameplay(b.LevelRoot, out PlayerSpawnPoint spawn) ? spawn : null;


    /// <inheritdoc/>
    void IPlayerBringUp.Begin(LoadedMission loaded, LevelTask levelRoot, ObjectCollisionIndex solids, MissionStringResolver missionText) =>
        Begin(loaded, levelRoot, solids, missionText);

    /// <summary>Arms the level's vehicles from the weapon table, against the rig being brought up.</summary>
    /// <param name="loaded">The mission's descriptor, packs and terrain.</param>
    /// <param name="levelRoot">The level's task tree.</param>
    public void ArmVehicles(LoadedMission loaded, LevelTask levelRoot) => ArmVehicles(loaded, levelRoot, Weapons, Candidate);

    /// <summary>Tunes the rig being brought up from <c>humanplayer.qvm</c>.</summary>
    public void Configure() => Configure(Candidate);

    /// <summary>Gives the rig being brought up the first-person rig every Human builds (skeleton 5, model 005_01_1).</summary>
    /// <returns>Whether the game has its set.</returns>
    public bool UseViewRig()
    {
        if (animations.ViewRig is not { } viewRig)
        {
            return false;
        }

        _viewRig = viewRig;
        Candidate.UseViewRig(viewRig);
        return true;
    }

    /// <summary>Hands the rig being brought up what the level's own <c>HumanPlayer</c> declares it carries.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="carried">Whether a weapon was put in its hands.</param>
    /// <returns>Whether the level declares a loadout at all.</returns>
    public bool CarryDeclaredLoadout(LevelTask levelRoot, out bool carried)
    {
        carried = false;
        if (!PlayerLoadout.TryRead(levelRoot, out PlayerLoadout? loadout) || loadout is null)
        {
            return false;
        }

        host.Logger.Information("Player", $"Carrying {loadout}.");
        carried = Carry(Candidate, loadout, Weapons, Ammunition);
        return true;
    }

    /// <summary>Offers the rig's rounds to the level's soldiers, when it has any.</summary>
    /// <param name="deflected">Where a glancing round whizzes.</param>
    public void ConnectSoldiers(Action<Vector3d> deflected)
    {
        _deflected = deflected;
        if (mission.SoldierPresenter is { } guards)
        {
            ConnectSoldiers(Candidate, guards, deflected);
        }
    }

    /// <summary>
    /// Stands the rig being brought up where the level places its player, and makes it the mission's player.
    /// </summary>
    /// <param name="levelRoot">The level's task tree, whose <c>HumanPlayer</c> declares a Position and a Gamma.</param>
    /// <param name="ground">The ground it stands on.</param>
    /// <param name="solids">The collision index, whose floors it may stand on.</param>
    /// <param name="opening">Where the opening camera looks from, for a level that declares no spawn.</param>
    /// <param name="openingYaw">The opening camera's heading.</param>
    /// <param name="spawn">The level's declared spawn, when it has one.</param>
    /// <returns>Whether it found something to stand on.</returns>
    public bool Stand(
        LevelTask levelRoot,
        IGroundHeightField? ground,
        ObjectCollisionIndex solids,
        Vector3d? opening,
        float openingYaw,
        out PlayerSpawnPoint spawn)
    {
        ArgumentNullException.ThrowIfNull(solids);

        // The level places its own player: every mission's objects.qvm declares a HumanPlayer task
        // with a Position and a Gamma. Standing there is standing where the game starts you.
        // Without a declared spawn the player is stood where the opening camera looks from, so that
        // fallback needs a camera to read.
        bool placed = PlayerSpawn.TryFindGameplay(levelRoot, out spawn)
            ? Candidate.PlaceAt(ground, spawn, solids.PlayerRegistrationCaster())
            : opening is { } at && Candidate.Place(ground, at, openingYaw);

        if (placed)
        {
            mission.Player = Candidate;

            // The task the rounds and blasts that reach the player are sent to, with HumanPlayer's own handlers.
            mission.PlayerTask = PlayerSpawn.FindGameplayTask(levelRoot);
            if (mission.PlayerTask is { } task)
            {
                task.Events = mission.PlayerEvents;
                task.Body = new Igi1PlayerBody(() => mission.Player, task);
            }
        }

        return placed;
    }

    // The message an AmmoPickup posts, with the ammunition box's portrait (0x46D990).
    private const string AmmoPickupMessage = "PICKEDUPAMMO";

    // STATUSSCREEN:ammo.spr, by its name in the status archive.
    private const string AmmoPickupPortrait = "ammo.spr";

    // A missile is the original's physics object of its model's type, flying by its missile type (0x429A30).
    private RetailMissileBody? FireMissile(LoadedMission loaded, string model, Vector3d at, Matrix3f frame, float speed, Vector3d target) =>
        mission.VehiclePhysics?.Types is { } types
        && types.TryFind(model, out OpenIGI.Game.Physics.PhysicsObjectType type)
        && OpenIGI.Game.Physics.MissileTypeDefinition.Of<X87>(type) is { } missile
            ? RetailMissileBody.Fire(
                type,
                missile,
                loaded.TryGetModel(model, out MefModel? mef) ? mef : null,
                at,
                frame,
                speed,
                target,
                null,
                mission.MissileStream.Random)
            : null;

    /// <summary>Makes the player's rig and reads the tables it and the soldiers use.</summary>
    /// <param name="loaded">The mission's descriptor, packs and terrain.</param>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="solids">The collision index over the placed objects.</param>
    /// <param name="missionText">The mission's text.</param>
    /// <returns>The rig, not yet stood in the level.</returns>
    public PlayerCamera Begin(LoadedMission loaded, LevelTask levelRoot, ObjectCollisionIndex solids, MissionStringResolver missionText)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(solids);
        ArgumentNullException.ThrowIfNull(missionText);
        // 000.iff, kept so the player's third-person body can be built from the same set it walks on (HumanPlayerBody: the body
        // is skeleton 0, and this is skeleton 0's set). Read once for the installation rather than per mission
        // (Igi1Gameplay.PlayerAnimations): it lives under common/, so it is the same 1.5 MB of IFF for all fourteen. The walls
        // are the index built for the soldiers' ground probe.
        PlayerCamera candidate = Igi1PlayerRig.Build(
            Igi1PlayerWorld.Of(solids, host.Terrain, host.Scene?.Ladders ?? []), animations.Body, host.Logger);
        Candidate = candidate;
        _bringUp = (loaded, levelRoot, solids, missionText);
        WirePlayer(candidate, loaded, solids, missionText);

        // ORDER MATTERS, and it is the original's: Igi1Arsenal reads the tables before humanplayer.qvm runs, or every id in
        // its weapon cycle is a placeholder and the cycle names weapons nobody carries. Empty until then, so a mission whose
        // tables cannot be read still has tables to ask.
        Weapons = new(host.Logger);
        Ammunition = new(host.Logger);
        try
        {
            Igi1Arsenal arsenal = Igi1Arsenal.Load(host.Game, host.FileSystem, host.Resources, host.Logger);
            Weapons = arsenal.Weapons;
            Ammunition = arsenal.Ammunition;
            WeaponTypeRegistry weaponRoster = Weapons;
            AmmoTypeRegistry ammoTable = Ammunition;
            GameMaterialRegistry materialTable = arsenal.Materials;

            // Shared with the guard-round handler: the ammunition record is what colours the retail tracer.
            mission.AmmoTypes = ammoTable;

            // Kept beside it for the all-weapons cheat, which can be switched on mid-mission and
            // then needs the whole roster, not just what the level declared.
            mission.WeaponTypes = weaponRoster;

            // What every surface in the world is made of, and therefore what a round leaves on it.
            candidate.UseMaterials(materialTable);
            mission.Soldiers?.UseMaterials(materialTable);
            mission.Materials = materialTable;
            host.Logger.Information("Material", $"Read {arsenal.MaterialCount} game material(s).");

            // What the open ground is made of. The terrain declares it the same way a mesh face
            // does -- see FootstepSounds.TerrainGameMaterialIndex -- and it is Snow in the Arctic
            // missions rather than Ground, which is the whole difference in how they sound.
            int? terrainRow = loaded.Terrain is { } terrainData
                ? FootstepSounds.TerrainGameMaterialIndex(terrainData.Materials)
                : null;
            candidate.TerrainGameMaterialIndex = terrainRow;
            host.TerrainRowRead(terrainRow);
            if (mission.Soldiers is { } soldiers)
            {
                soldiers.TerrainGameMaterialIndex = terrainRow;
            }

            if (terrainRow is int row && materialTable.TryGet(row, out GameMaterial groundRow))
            {
                host.Logger.Information("Material", $"Terrain walks on '{groundRow.Name}' (row {row}).");
            }

            mission.GunPickups = GunPickupRegistry.Build(levelRoot, weaponRoster, ammoTable, host.Game.Variables);
            GunPickupRegistry gunPickups = mission.GunPickups;

            // A taken gun's task is deleted by its own update where the level walk reaches it (0x46CEA0), not by the
            // touch. The walk was bound before the player came up, so the pickups join it here.
            if (mission.Cutscenes is { } levelWalk)
            {
                gunPickups.AttachTo(levelWalk.Walker);
            }
            gunPickups.Landed = sounds.PlayWeaponDrop;
            WirePickups(candidate, missionText);
            mission.Soldiers?.UseDroppedWeaponPickups(gunPickups);

            // Every drop is drawn, a soldier's here or a co-op server's laid down on a client.
            gunPickups.Spawned += pickup => host.WeaponDropped(loaded, pickup);
            gunPickups.Removed += host.WeaponRemoved;
            mission.Soldiers?.SetWeaponDropSink((weapon, position, orientation, velocity) => gunPickups.Spawn(position, orientation, weapon, velocity));

            // What a dropped gun lands on: the same solids and terrain a round strikes, as triangles
            // for the realistic body and as one ray for the original's fifty-metre drop probe.
            ObjectCollisionIndex? dropSolids = mission.Solids;
            TerrainData? dropTerrain = host.Terrain;
            mission.Soldiers?.SetDroppedWeaponWorld(
                (min, max, into) =>
                {
                    dropSolids?.CollectTriangles(min, max, into);
                    if (dropTerrain is not null)
                    {
                        TerrainRayTrace.CollectTriangles(dropTerrain, min, max, into);
                    }
                },
                mission.Trace is { } dropTrace ? WorldTrace.AsRayCast(dropTrace) : null);
            if (mission.GunPickups.Items.Count > 0)
            {
                host.Logger.Information("Weapon", $"{mission.GunPickups.Items.Count} gun pickup(s) available.");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException)
        {
            host.Logger.Warning("Weapon", $"Could not read the weapon or ammunition tables: {ex.Message}");
        }

        host.EndStage("the player's animation sets, walls, ladders and the weapon tables");
        return candidate;
    }

    /// <summary>Wires a player's rig into the level: its animations, walls, grenades and missiles, ladders, interactions and pickups.</summary>
    /// <param name="candidate">The rig.</param>
    /// <param name="loaded">The mission's descriptor, packs and terrain.</param>
    /// <param name="solids">The collision index over the placed objects.</param>
    /// <param name="missionText">The mission's text.</param>
    /// <remarks>Everything <see cref="Begin"/> does for the one rig it makes; a co-op session's other players are wired the same way.</remarks>
    private void WirePlayer(PlayerCamera candidate, LoadedMission loaded, ObjectCollisionIndex solids, MissionStringResolver missionText)
    {
        // The mine weapon lays nothing on a pane, a door or a vehicle (0x4755A0); its model's bound and magic vertex 0 are
        // what a blast is measured against and where its own starts.
        candidate.MineSurfaceRefused = hit => RefusesMine(hit);
        candidate.Grenades.Solids = (owner, model) =>
            loaded.TryGetModel(model, out MefModel? solidModel) && solidModel is not null
            && MefCollisionMesh.TryRead(solidModel.Container, meshIndex: 0, out MefCollisionMesh? solidMesh)
            && solidMesh is { Faces.Length: > 0 }
                ? new RetailRayModel(owner, new RetailRayMesh(solidMesh), solidModel.NativeMarkerBound)
                : null;
        candidate.MineModel = model => loaded.TryGetModel(model, out MefModel? mineModel) && mineModel is not null
            ? (mineModel.NativeMarkerBound, mineModel.TryGetMagicVertex(0, out Vector3f point) ? point : default)
            : (0f, default);

        // A thrown grenade is the original's physics object of its model's type (0x477040), stepping in the world the
        // cars do; it leaves the hand facing the way it was thrown (0x414A60).
        candidate.Grenades.World = () => mission.RetailWorld;
        candidate.Missiles.World = () => mission.RetailWorld;
        candidate.Missiles.Bodies = (model, at, frame, speed, target) => FireMissile(loaded, model, at, frame, speed, target);
        mission.VehicleMissiles.World = () => mission.RetailWorld;
        mission.VehicleMissiles.Bodies = (model, at, frame, speed, target) => FireMissile(loaded, model, at, frame, speed, target);
        candidate.Grenades.Bodies = (model, at, velocity, frame) =>
            mission.VehiclePhysics?.Types.TryFind(model, out OpenIGI.Game.Physics.PhysicsObjectType type) == true
                ? RetailPhysicsObjectBody.Create(
                    type,
                    loaded.TryGetModel(model, out MefModel? grenadeModel) ? grenadeModel : null,
                    at,
                    frame,
                    velocity)
                : null;

        if (mission.Interactions is { } interactions)
        {
            candidate.UseInteractions(interactions);
            candidate.TaskActivated += id => mission.Soldiers?.ActivateTask(id);
            candidate.ElectrifiedFenceTouched += fence => harm.ResolvePlayerElectricFence(candidate, fence);
            candidate.UnarmedStationaryGun += gun => host.Logger.Warning(
                "Player",
                $"Mounted stationary gun {gun.TaskId}, but its {gun.WeaponId} is not bound to a weapon; it cannot fire.");
            host.Logger.Information("Player", $"{interactions.Items.Count} direct world interaction(s).");
        }
    }

    // What a player walks over goes into that player's hands -- the mission's own or a co-op partner's -- and only the player
    // this machine plays hears it and is told.
    private void WirePickups(PlayerCamera candidate, MissionStringResolver missionText)
    {
        bool Felt() => ReferenceEquals(candidate, mission.Player);
        if (mission.GunPickups is { } gunPickups)
        {
            candidate.UseGunPickups(gunPickups);
            candidate.GunPickedUp += pickup =>
            {
                if (Felt())
                {
                    audio.PlayGunPickupSound(pickup);
                    mission.Objectives?.ShowTransientMessage(missionText.Get($"PICKEDUPWEAPON{pickup.Weapon.DisplayName}"));
                }
            };
        }

        if (mission.GenericPickups is { } genericPickups)
        {
            candidate.UseGenericPickups(genericPickups);
            candidate.GenericPickedUp += pickup =>
            {
                // An AmmoPickup hands its ammunition to whoever took it (0x46D990), the award at the same edge as the cue so
                // the object, the count and the sound cannot drift apart; an explicit task payload wins over the catalogue.
                bool ammunition = string.Equals(pickup.Task.Type.Name, "AmmoPickup", StringComparison.Ordinal);
                if (candidate.Weapons is { } weapons
                    && mission.AmmoTypes is { } ammoTypes
                    && AmmoPickupCatalog.TryResolve(pickup, ammoTypes, out AmmoType ammo))
                {
                    weapons.Ammunition.Add(ammo.AmmoId, AmmoPickupCatalog.Amount(pickup, ammo.AmmoId));
                }

                if (Felt())
                {
                    audio.PlayGenericPickupSound(ammunition);

                    // An AmmoPickup posts its own message as it is taken; a GenericPickup posts none.
                    if (ammunition)
                    {
                        mission.Objectives?.ShowTransientMessage(missionText.Get(AmmoPickupMessage), portrait: AmmoPickupPortrait);
                    }
                }
            };
        }
    }

    /// <summary>
    /// Makes another player for a co-op session: a rig wired into the level as the mission's own is, tuned by the same
    /// <c>humanplayer.qvm</c>, carrying what the level declares, with health of its own.
    /// </summary>
    /// <param name="loaded">The mission's descriptor, packs and terrain.</param>
    /// <param name="levelRoot">The level's task tree, whose declared loadout the player carries.</param>
    /// <param name="solids">The collision index over the placed objects.</param>
    /// <param name="missionText">The mission's text.</param>
    /// <returns>The rig and its health.</returns>
    /// <remarks>
    /// The original has one player; a second is made with every per-player step of <see cref="Begin"/> and
    /// <see cref="Configure(PlayerCamera)"/>, and none of their mission-wide ones: the tables are read once, and the mission's own player
    /// keeps the health the HUD shows.
    /// </remarks>
    public (PlayerCamera Player, HumanHealth Health) BeginExtra(LoadedMission loaded, LevelTask levelRoot, ObjectCollisionIndex solids, MissionStringResolver missionText)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(levelRoot);
        PlayerCamera extra = Igi1PlayerRig.Build(
            Igi1PlayerWorld.Of(solids, host.Terrain, host.Scene?.Ladders ?? []), animations.Body, host.Logger);
        WirePlayer(extra, loaded, solids, missionText);
        WirePickups(extra, missionText);
        // Tuned, rigged and then armed, in the order the mission's own player is.
        if (_config is { } config)
        {
            Igi1PlayerRig.Tune(extra, config, host.Logger);
        }

        if (_viewRig is { } viewRig)
        {
            extra.UseViewRig(viewRig);
        }

        if (PlayerLoadout.TryRead(levelRoot, out PlayerLoadout? loadout) && loadout is not null && Weapons is not null && Ammunition is not null)
        {
            Carry(extra, loadout, Weapons, Ammunition);
        }

        HumanHealth health = new(HumanHitZoneTable.Player, mission.PlayerLife.MaximumHealth)
        {
            DamageScale = HumanHealth.DifficultyDamageScale(mission.PlayerDifficulty),
        };
        extra.UseHealth(health);
        extra.Body.SetFallImpactSink(impact => harm.ResolvePlayerFallImpact(extra, impact));

        // The partner's rounds reach the soldiers as the mission's own player's do.
        if (mission.SoldierPresenter is { } guards)
        {
            ConnectSoldiers(extra, guards, _deflected ?? (static _ => { }), soldiersThrowHere: false);
        }

        return (extra, health);
    }

    /// <summary>Runs <c>humanplayer.qvm</c> over the rig and hands it the player's health.</summary>
    /// <param name="candidate">The rig <see cref="Begin"/> made.</param>
    public void Configure(PlayerCamera candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        try
        {
            // Read once for the level: a co-op session's other players are tuned by the same reading (BeginExtra).
            HumanPlayerConfig config = Igi1PlayerRig.ReadTuning(host.Game, host.Resources, host.Logger);
            _config = config;

            // Its delta translation scale, its full health and a powered fence's dose are the player's life's (Igi1PlayerLife.Tune).
            if (Igi1PlayerRig.Tune(candidate, config, host.Logger) is { } tuning)
            {
                mission.PlayerLife.Tune(tuning, mission.PlayerDifficulty);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException)
        {
            host.Logger.Warning("Player", $"Could not run humanplayer.qvm: {ex.Message}");
        }

        host.EndStage("humanplayer.qvm");

        // PlayerBody resolves the landing on the tick after contact, just as retail does. Its
        // default health exists for standalone physics use; the desktop must give it the exact
        // accumulator the HUD, guard hits and death state already share. The sink crosses the
        // presentation boundary for sound, the red wash, AI hearing and lethal-fall death.
        candidate.UseHealth(mission.PlayerLife.Health);
        candidate.Body.SetFallImpactSink(impact => harm.ResolvePlayerFallImpact(candidate, impact));
    }

    /// <summary>Gives every authored vehicle that can fire the guns its model carries, and aims them at the player.</summary>
    /// <param name="loaded">The mission, whose packs hold the vehicles' models.</param>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="roster">The weapon table.</param>
    /// <param name="player">The player the guns aim at.</param>
    /// <seealso cref="Igi1VehicleArming"/>
    public void ArmVehicles(LoadedMission loaded, LevelTask levelRoot, WeaponTypeRegistry roster, PlayerCamera player)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(player);
        if (mission.Cutscenes is { } runtime && mission.MagicObjects is { } magic)
        {
            new Igi1VehicleArming(mission, host, loaded, roster, player, runtime, magic).Arm(levelRoot);
        }
    }

    /// <summary>Hands the player everything the mission declares, with the ammunition each weapon draws from.</summary>
    /// <param name="candidate">The player's rig.</param>
    /// <param name="loadout">What the level's <c>HumanPlayer</c> declares.</param>
    /// <param name="roster">The weapon table.</param>
    /// <param name="ammunition">The ammunition table.</param>
    /// <returns>Whether the player carries anything, and so needs a hand to hold it in.</returns>
    /// <remarks>
    /// Which weapon ends up in hand is not decided here: the weapon cycle's index starts at -1, and the first tick lands on
    /// entry 0 of <c>DefineHumanPlayerWeaponCycle</c> and steps on until it finds one that is carried.
    /// </remarks>
    public bool Carry(PlayerCamera candidate, PlayerLoadout loadout, WeaponTypeRegistry roster, AmmoTypeRegistry ammunition)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(loadout);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(ammunition);

        // The M16A2's launcher fires whatever AMMO_ID_M203 the player has (0x40F4A0 looks the record up in its ammo list each
        // press), not only rounds its loadout declared: an M16A2 or grenades picked up in the level -- mission 2's barracks --
        // fire too. So the record is known from the start, whatever the loadout says.
        if (ammunition.Types.FirstOrDefault(a => string.Equals(a.ScriptId, "AMMO_ID_M203", StringComparison.Ordinal)) is { } m203)
        {
            candidate.Weapons?.SetM203Ammunition(m203);
        }

        if (candidate.View is null || loadout.Weapons.Count == 0)
        {
            return false;
        }

        foreach (LoadoutWeapon declared in loadout.Weapons)
        {
            if (!roster.TryGetByScriptId(declared.WeaponId, out WeaponType weapon))
            {
                host.Logger.Warning("Weapon", $"{declared.WeaponId} is not in the roster; it is not carried.");
                continue;
            }

            // A weapon finds its ammunition by naming a Calibre ID, and -1 -- the knife's -- names
            // none at all, which is exactly why a knife never runs out.
            bool hasAmmo = ammunition.TryGetByCalibre(weapon.CalibreId, out AmmoType ammo);
            candidate.Carry(weapon, hasAmmo ? ammo : null);
        }

        foreach (LoadoutAmmo declared in loadout.Ammunition)
        {
            AmmoType? pool = ammunition.Types.FirstOrDefault(a =>
                string.Equals(a.ScriptId, declared.AmmoId, StringComparison.Ordinal));

            if (pool is not null)
            {
                candidate.Weapons?.Ammunition.Add(pool.AmmoId, declared.Count);
            }
        }

        return true;
    }

    /// <summary>Offers the player's rounds, blasts, noise and grenades to the soldiers, which own their own health.</summary>
    /// <param name="candidate">The player's rig.</param>
    /// <param name="guards">The soldiers as they are drawn, whose drawn poses the player's rounds strike.</param>
    /// <param name="deflected">Where a glancing round whizzes.</param>
    /// <param name="soldiersThrowHere">
    /// Whether the soldiers' grenades fly in this rig's pool: the mission's own player's. A co-op partner's rig keeps its
    /// own, and taking the soldiers' with it left every lob a guard made after someone joined in a pool only that partner's
    /// commands step -- frozen while it lay dead, orphaned when it left.
    /// </param>
    public void ConnectSoldiers(PlayerCamera candidate, ISoldierPresenter guards, Action<Vector3d> deflected, bool soldiersThrowHere = true)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(guards);
        ArgumentNullException.ThrowIfNull(deflected);

        // The round goes to the level's bodies -- this game's soldiers, and any a mod brings -- as the shot's trace meets them.
        candidate.Humans = (Vector3d origin, Vector3d end, float damageFactor, WeaponCategory category, double stopDistance, bool alreadyHeard, out HumanHit hit) =>
            mission.Bodies.Shoot(
                origin,
                end,
                mission.PlayerTask,
                stopDistance,
                alreadyHeard,
                struck => new Igi1WeaponHit(struck, origin, end, damageFactor, category, Shooter: null, FromPlayer: true, candidate.Shots, candidate.Weapons?.InHand?.Weapon.ScriptId).ToShared(),
                out hit);
        candidate.HumanProbes = guards.Simulation.Probe();
        HumanProximityCast soldierProximity = guards.Simulation.ProximityProbe();
        candidate.HumanProximityTargets = (Vector3d center, float radiusUnits, out HumanProximityTarget target) =>
        {
            bool found = soldierProximity(center, radiusUnits, out target);
            if (!mission.PlayerLife.Health.IsDead)
            {
                double distance = Vector3d.Distance(center, candidate.Body.Position);
                if (distance <= radiusUnits && (!found || distance < target.Distance))
                {
                    target = new HumanProximityTarget(candidate.Body.Position, distance);
                    found = true;
                }
            }

            return found;
        };
        Igi1PlayerHearing.Attach(candidate, guards.Simulation);

        // A glancing round whizzes where it turned, on the ricochet sound's own limit --
        // 0x47BD7B sounds it there directly rather than through the material's window.
        candidate.Deflected = deflected;

        // Every human's run tells the laid mines its place: the soldiers in the roster's order, then the player (0x463080),
        // whose update (0x410960) sends it dead or alive. A co-op session's players are all humans to every mine, whoever
        // laid it: the mission's own, then the others.
        List<HumanPlace> places = [];
        candidate.Grenades.MineSensors = () =>
        {
            places.Clear();
            guards.Simulation.AddHumanPlaces(places);
            float radius = guards.Simulation.PlayerHumanRadius;
            if (mission.Player is { } own)
            {
                places.Add(new HumanPlace(own.Body.Origin, radius));
            }

            foreach (Multiplayer.Igi1NetworkPlayer partner in mission.Partners)
            {
                places.Add(new HumanPlace(partner.Player.Body.Origin, radius));
            }

            // The rig being brought up, before it is the mission's own player or one of the partners.
            if (!ReferenceEquals(mission.Player, candidate) && !mission.Partners.Any(p => ReferenceEquals(p.Player, candidate)))
            {
                places.Add(new HumanPlace(candidate.Body.Origin, radius));
            }

            return places;
        };

        // One grenade pool for both throwers: the player's own and every guard's lob fly,
        // bounce and detonate through the same simulation and the same drain.
        if (soldiersThrowHere)
        {
            guards.Simulation.SetGrenades(candidate.Grenades);
        }
    }

    // 0x4755A0 refuses a contact whose task is a Glass, a Door or a Vehicle (Heli, Missile, Train, Plane, Car, KnifeStab).
    private bool RefusesMine(WorldTraceHit hit)
    {
        if (hit.Owner?.Type.Name is "Door" or "Heli" or "Missile" or "Train" or "Plane" or "Car" or "KnifeStab")
        {
            return true;
        }

        if (hit.Placement is { } placement && mission.Destructibles is { } destructibles)
        {
            foreach (DestructibleObject item in destructibles.Objects)
            {
                if (item.Kind == DestructibleObjectKind.Glass && ReferenceEquals(item.Placement, placement))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
