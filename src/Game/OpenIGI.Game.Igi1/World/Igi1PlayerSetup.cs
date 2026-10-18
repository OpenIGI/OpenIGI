using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>What making Project I.G.I.'s player asks of whatever shows and plays it.</summary>
public interface IIgi1PlayerSetupHost
{
    /// <summary>Gets the log.</summary>
    IOpenIgiLogger Logger { get; }

    /// <summary>Gets the game's loop, whose namespace, types and tree the player's tables and scripts load into.</summary>
    GameLoop Game { get; }

    /// <summary>Gets the files the tables are read from.</summary>
    VirtualFileSystem FileSystem { get; }

    /// <summary>Gets the resources the scripts are registered as.</summary>
    ResourceRegistry Resources { get; }

    /// <summary>Gets the mission's terrain, which the grenades and the view model trace.</summary>
    TerrainData? Terrain { get; }

    /// <summary>Gets the level's built scene, whose ladders the player climbs.</summary>
    WorldScene? Scene { get; }

    /// <summary>Reads, once a session, the animation sets the player's body and its first-person rig use.</summary>
    /// <returns>The body's set, or <see langword="null"/> when there is none.</returns>
    BoneAnimationSet? ReadSharedAnimations();

    /// <summary>Records that a stage of the load has finished, for the load's timings.</summary>
    /// <param name="what">What finished.</param>
    void EndStage(string what);

    /// <summary>Answers the player touching a powered fence.</summary>
    /// <param name="player">The player.</param>
    /// <param name="fence">The fence.</param>
    void ElectricFenceTouched(PlayerCamera player, WorldInteraction fence);

    /// <summary>Plays the sound of a generic pickup being taken.</summary>
    /// <param name="pickup">The pickup.</param>
    void GenericPickedUp(GenericPickup pickup);

    /// <summary>Plays the sound of a gun being picked up.</summary>
    /// <param name="pickup">The pickup.</param>
    void GunPickedUp(GunPickup pickup);

    /// <summary>Shows a gun a soldier dropped.</summary>
    /// <param name="loaded">The mission, whose packs hold the gun's model.</param>
    /// <param name="pickup">The dropped gun.</param>
    void WeaponDropped(LoadedMission loaded, GunPickup pickup);

    /// <summary>Hands the effects the game material the open ground is made of.</summary>
    /// <param name="row">The material's row, or <see langword="null"/> when the terrain declares none.</param>
    void TerrainRowRead(int? row);

    /// <summary>Answers the player's landing: its sound, the red wash, the AI hearing it and a lethal fall.</summary>
    /// <param name="player">The player.</param>
    /// <param name="impact">The landing.</param>
    void FallImpact(PlayerCamera player, HumanFallImpact impact);
}

/// <summary>
/// Makes Project I.G.I.'s player for a loaded level: the rig, what it walks into and climbs, the weapon, ammunition and
/// material tables, the guns lying in the level, and <c>humanplayer.qvm</c>'s tuning.
/// </summary>
/// <param name="mission">The mission's world, which keeps the tables and the player's health.</param>
/// <param name="host">What shows and plays it.</param>
public sealed class Igi1PlayerSetup(MissionWorld mission, IIgi1PlayerSetupHost host)
{
    /// <summary>Gets the weapon table, once <see cref="Begin"/> has read it.</summary>
    public WeaponTypeRegistry Weapons { get; private set; } = null!;

    /// <summary>Gets the ammunition table, once <see cref="Begin"/> has read it.</summary>
    public AmmoTypeRegistry Ammunition { get; private set; } = null!;

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
        PlayerCamera candidate = new(host.Logger);

        // 000.iff, kept so the player's third-person body can be built from the same set it walks on
        // (HumanPlayerBody: the body is skeleton 0, and this is skeleton 0's set). Walking is the
        // animation's own root motion, so without it the player can stand and fall but not walk.
        //
        // Read once for the session rather than per mission: it lives under common/, so it is the
        // same 1.5 MB of IFF for all fourteen, and parsing it again on the render thread every time
        // a mission started was most of that step's cost.
        BoneAnimationSet? playerBodySet = host.ReadSharedAnimations();
        if (playerBodySet is not null)
        {
            candidate.UseAnimations(playerBodySet);
        }

        // The walls. Terrain alone gives the player ground to stand on but nothing to stop it
        // walking through crates and buildings; the solidity is in each model's own HSMC mesh.
        // The index is the one built above for the soldiers' ground probe.
        candidate.UseWalls(solids);
        host.Logger.Information(
            "Player",
            $"{solids.Count} placed object(s) are solid, {solids.TriangleCount} collision triangle(s).");

        // The grenade's own cast INCLUDES the terrain: the body meets the ground through its
        // ground query, but a grenade has no such query and would fall through the world.
        candidate.GrenadeWalls = host.Terrain is { } grenadeGround
            ? WorldTrace.AsRayCast(WorldTrace.Nearest(solids.Tracer(), TerrainRayTrace.Caster(grenadeGround)))
            : solids.Caster();
        candidate.GrenadeTrace = host.Terrain is { } grenadeTraceGround
            ? WorldTrace.Nearest(solids.Tracer(), TerrainRayTrace.Caster(grenadeTraceGround))
            : solids.Tracer();

        // The view model's forward sweep, which wants a ray through the terrain rather than the
        // height under a point. Null for a title whose ground nothing can trace yet, and the
        // pullback then meets placed objects only.
        candidate.TerrainTrace = host.Terrain is { } viewModelGround
            ? TerrainRayTrace.Caster(viewModelGround)
            : null;

        // The ladders the level attaches to its buildings, so the player can climb them (E to mount,
        // W/S up and down, E held to slide).
        if (host.Scene is not null)
        {
            candidate.UseLadders(host.Scene.Ladders);
            host.Logger.Information("Player", $"{host.Scene.Ladders.Count} ladder(s) to climb.");
        }
        if (mission.Interactions is { } interactions)
        {
            candidate.UseInteractions(interactions);
            candidate.TaskActivated += id => mission.Soldiers?.ActivateTask(id);
            candidate.ElectrifiedFenceTouched += fence => host.ElectricFenceTouched(candidate, fence);
            candidate.UnarmedStationaryGun += gun => host.Logger.Warning(
                "Player",
                $"Mounted stationary gun {gun.TaskId}, but its {gun.WeaponId} is not bound to a weapon; it cannot fire.");
            host.Logger.Information("Player", $"{interactions.Items.Count} direct world interaction(s).");
        }
        if (mission.GenericPickups is { } genericPickups)
        {
            candidate.UseGenericPickups(genericPickups);
            candidate.GenericPickedUp += host.GenericPickedUp;
            host.Logger.Information("Player", $"{genericPickups.Items.Count} generic pickup(s) available.");
        }

        // ORDER MATTERS, and it is the original's. WEAPON_ID_* and AMMO_ID_* are pattern-registered
        // symbols that get their values as the two tables are read, so the rosters have to load
        // before humanplayer.qvm runs -- otherwise every id in its weapon cycle is a placeholder
        // and the cycle names weapons nobody carries. section 8.
        Weapons = new(host.Logger);
        Ammunition = new(host.Logger);
        WeaponTypeRegistry weaponRoster = Weapons;
        AmmoTypeRegistry ammoTable = Ammunition;
        GameMaterialRegistry materialTable = new(host.Logger);
        try
        {
            weaponRoster.Register(host.Game.Natives);
            weaponRoster.Load(host.FileSystem, host.Resources, host.Game.Natives);
            WeaponConfigRegistry.RegisterTypes(host.Game.Types);
            WeaponConfigRegistry weaponConfig = new(host.Logger);
            if (weaponConfig.Load(host.Resources, host.Game.Natives, host.Game.Tasks) == WeaponConfigRegistry.NoScript)
            {
                host.Logger.Warning(
                    "Weapon",
                    $"No weapon configuration at {WeaponConfigRegistry.ScriptResource}; this title keeps it elsewhere.");
            }
            else
            {
                weaponConfig.AttachTo(weaponRoster);
            }
            ammoTable.Register(host.Game.Natives);
            ammoTable.Load(host.FileSystem, host.Resources, host.Game.Natives);

            // Shared with the guard-round handler: the ammunition record is what colours a
            // ballistic streak, exactly as it colours the retail tracer.
            mission.AmmoTypes = ammoTable;

            // Kept beside it for the all-weapons cheat, which can be switched on mid-mission and
            // then needs the whole roster, not just what the level declared.
            mission.WeaponTypes = weaponRoster;

            // What every surface in the world is made of, and therefore what a round leaves on it.
            materialTable.Register(host.Game.Natives);
            if (host.FileSystem.Exists(GameMaterialRegistry.ScriptResource))
            {
                host.Resources.Register(
                    GameMaterialRegistry.ScriptResource,
                    host.FileSystem.ReadAllBytes(GameMaterialRegistry.ScriptResource));
            }
            else
            {
                host.Logger.Warning("Material", $"No material table at {GameMaterialRegistry.ScriptResource}.");
            }

            int materialCount = materialTable.Load(host.Resources, host.Game.Natives);
            candidate.UseMaterials(materialTable);
            mission.Soldiers?.UseMaterials(materialTable);
            mission.Materials = materialTable;
            host.Logger.Information("Material", $"Read {materialCount} game material(s).");

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

            mission.GunPickups = GunPickupRegistry.Build(levelRoot, weaponRoster, ammoTable);
            GunPickupRegistry gunPickups = mission.GunPickups;
            candidate.UseGunPickups(gunPickups);
            candidate.GunPickedUp += pickup =>
            {
                host.GunPickedUp(pickup);
                string key = $"PICKEDUPWEAPON{pickup.Weapon.DisplayName}";
                mission.Objectives?.ShowTransientMessage(missionText.Get(key));
            };
            mission.Soldiers?.UseDroppedWeaponPickups(gunPickups);
            mission.Soldiers?.SetWeaponDropSink((weapon, position, orientation, velocity) =>
            {
                GunPickup pickup = gunPickups.Spawn(position, orientation, weapon, velocity);
                host.WeaponDropped(loaded, pickup);
            });

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

    /// <summary>Runs <c>humanplayer.qvm</c> over the rig and hands it the player's health.</summary>
    /// <param name="candidate">The rig <see cref="Begin"/> made.</param>
    public void Configure(PlayerCamera candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        // The player's tuning is not in the executable: humanplayer.qvm is the whole of it, and it
        // is what says how high a jump goes and how far a step travels.
        try
        {
            HumanPlayerConfig config = new();
            config.Register(host.Game.Natives);
            config.Load(host.Resources, host.Game.Natives);

            if (config.PlaceholderIds.Count > 0)
            {
                host.Logger.Warning(
                    "Player",
                    $"humanplayer.qvm left {config.PlaceholderIds.Count} id(s) unresolved; the weapon "
                        + "cycle may name weapons that do not exist.");
            }

            if (config.Parameters is { } tuning)
            {
                candidate.UseParameters(tuning);

                // Argument 0 scales the animation's delta translation -- the player's, and Ekk's.
                mission.PlayerDeltaTranslationScale = tuning.DeltaTranslationScale;

                // Argument 7 is the player's maximum health (Human+600, 0x410370's overwrite) --
                // 3.0 in retail, and the single number that sets the player's time-to-kill.
                mission.PlayerMaximumHealth = tuning.MaximumHealth;

                // Argument 8 is the dose one powered fence contact adds (0x412F80's fmul against
                // the human's damage scale) -- 0.5 in retail. Read it for the same reason argument
                // 7 is read: a retuned humanplayer.qvm owns its own shock, not the shipped number.
                mission.ElectricFenceDose = tuning.ElectricFenceDose;
                mission.PlayerHealth = new(HumanHitZoneTable.Player, mission.PlayerMaximumHealth)
                {
                    DamageScale = HumanHealth.DifficultyDamageScale(mission.PlayerDifficulty),
                };
            }
            else
            {
                host.Logger.Warning("Player", "humanplayer.qvm published no parameters; using the retail defaults.");
            }

            // The same script says which weapon starts in hand, and it is not a guess any more:
            // 0x40F760's cycle index begins at -1, so the first update lands on entry 0 of
            // DefineHumanPlayerWeaponCycle and steps on until it finds one you carry. The shipped
            // list starts with the knife. section 16.7.
            candidate.UseWeaponCycle(config.WeaponCycle);

            // And the nine categories the number keys select — 0x411000 reads exactly this table.
            candidate.Weapons?.UseCategories(config.Categories);
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
        candidate.UseHealth(mission.PlayerHealth);
        candidate.Body.SetFallImpactSink(impact => host.FallImpact(candidate, impact));
    }
}
