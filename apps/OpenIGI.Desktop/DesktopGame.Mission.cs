using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Ai;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

internal sealed partial class DesktopGame
{
    private void StartMission(int id)
    {
        if (!_missions.TryGet(id, out MissionDescriptor? chosen))
        {
            _logger.Warning("Mission", $"Asked to start mission {id}, which is not registered.");
            return;
        }

        _logger.Information("Mission", $"Starting {chosen}.");
        LevelTask levelRoot = (LevelTask)_loop.Tasks.New(_loop.Tasks.Root, _loop.Types.Get(EngineTaskTypes.Level));
        LevelLoader levelLoader = new(levelRoot, _loop.TaskCommands, _loop.Natives, _resources, _logger);
        TerrainLoader terrainLoader = new(_resources, _loop.Natives, _logger);
        MissionLoader missionLoader = new(_missions, _fileSystem, _resources, levelLoader, _logger, terrainLoader);
        LoadedMission loaded = missionLoader.Load(id);

        // Evaluate the level's ConditionalContainer conditions against the initial playable state, so the
        // scene, spawn and soldier walks below skip the subtrees the original leaves unregistered (the
        // intro/outro cut-scene props and actors). Runs after the tree is built and its variables exported,
        // and before anything walks it.
        new ConditionalContainerGate(_loop.Variables, _logger).Prepare(levelRoot);

        _scene = WorldScene.Build(loaded, levelRoot, _magic, _logger);
        _world = new WorldRenderer(_device, _resources, _logger) { Materials = _materials, DetailMaps = _detailMaps };

        // The walls. Built here because two consumers want it: the player's collision, and the
        // soldiers' ground probe - a guard's declared Position is only where it starts, and without
        // the settle the retail integrator performs, floor-authored spawns bury the body to the waist.
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(_scene.Placements);
        _worldSolids = solids;

        // The soldiers the level places. Each names its own model, skeleton and stand animation
        // (player.md section 15); the sets they index were loaded when the packs opened. They are not
        // WorldScene placements - a human task is a typed task, and the original draws it through the
        // bone path, not the static one.
        _soldiers?.Dispose();
        _soldiers = new SoldierRenderer(_device, _resources, _logger) { DetailMaps = _detailMaps };
        _soldiers.Load(
            loaded,
            levelRoot,
            loaded.Terrain is { IsUsable: true } standing ? standing : null,
            solids.Caster());

        // The level's nav graphs. A level ships ONE PER AIGraph TASK, named by that task's id (ai.md 1.1),
        // and every HumanAI names the one it walks through its own "Graph ID" parameter (ai.md 7.6).
        //
        // Level 1 ships eleven of them. Picking a single graph -- the one with the most nodes -- and giving
        // it to every soldier routed each one over node ids belonging to a graph that was not its own,
        // which is why guards walked to places no patrol goes and off the edge of the level.
        try
        {
            string graphDirectory = Path.Combine(
                _gamePath, loaded.Descriptor.MissionDirectory, "graphs");
            Dictionary<int, GraphFile> graphs = [];
            if (Directory.Exists(graphDirectory))
            {
                foreach (string file in Directory.GetFiles(graphDirectory, "graph*.dat"))
                {
                    string stem = Path.GetFileNameWithoutExtension(file);
                    if (!int.TryParse(stem.AsSpan("graph".Length), out int graphId))
                    {
                        continue;
                    }

                    try
                    {
                        GraphFile graph = GraphFile.Read(File.ReadAllBytes(file));

                        // The graph file stores node positions in the graph's own frame. The world
                        // position is node + graphTask.Position — what 0x4F9770 adds every time.
                        Vector3d graphPos = TaskGeometry.FindPosition(levelRoot, graphId);
                        graph.ApplyWorldOffset(graphPos);

                        graphs[graphId] = graph;
                    }
                    catch (GraphFormatException ex)
                    {
                        _logger.Warning("Ai", $"Nav graph '{stem}' did not parse: {ex.Message}");
                    }
                }
            }

            if (graphs.Count > 0)
            {
                _logger.Information(
                    "Ai",
                    $"Nav graphs: {graphs.Count} — " +
                    string.Join(", ", graphs.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value.Nodes.Count}n")));
                // The difficulty every guard reads its combat row at. Faithfully the ACTIVE PLAYER PROFILE's
                // (sub_406F70 takes it from sub_406220()'s first field), so the difficulty the player picked
                // in the menu actually reaches the AI -- which it did not while this was a hardcoded default,
                // leaving every guard on GD_2/Normal whatever the profile said. -difficulty still overrides,
                // for testing a difficulty without editing a profile.
                AiDifficulty effectiveDifficulty = _options.Difficulty
                    ?? (AiDifficulty)Math.Clamp(
                        _configuration.Active?.Difficulty ?? (int)AiDifficulty.Normal,
                        (int)AiDifficulty.Easy,
                        (int)AiDifficulty.Hard);
                _logger.Information(
                    "Ai",
                    $"Difficulty {HumanAiConfigRegistry.NameOf(effectiveDifficulty)} ({effectiveDifficulty}) "
                        + (_options.Difficulty is null
                            ? $"from profile '{_configuration.Active?.Name ?? "(none)"}'."
                            : "from -difficulty override."));
                // The sight and bullet rays test placed objects AND the terrain, as the original's
                // 0x4CDB20 does (both of the sight ray's passes send the -1 mask whose sign bit turns
                // the terrain arm on -- player.md 7.6). Objects alone let guards see and shoot through
                // hills. Nearest contact wins.
                WorldRayCast objectCast = solids.Caster();
                WorldRayCast levelCast = (a, b, out blockedAt) =>
                {
                    bool hitObject = objectCast(a, b, out WorldRayHit onObject);
                    if (_playerTerrain is { } ground
                        && TerrainSightBlock.TryBlock(ground, a, b, out WorldRayHit onGround)
                        && (!hitObject || onGround.Distance < onObject.Distance))
                    {
                        blockedAt = onGround;
                        return true;
                    }

                    blockedAt = onObject;
                    return hitObject;
                };
                ApplyColourLookup(_options.StartMission);

                // The level's doors, before the brains: a walker asks the registry for the nearest door
                // on a door hop, and an open door is switched out of the collision index so the
                // doorway is walkable. Both have to exist before Animate hands each brain its query.

                DoorRegistry levelDoors = DoorRegistry.Build(levelRoot);
                _soldiers.SetDoors(levelDoors, solids);
                if (levelDoors.Doors.Count > 0)
                {
                    _logger.Information("World", $"{levelDoors.Doors.Count} door(s) the AI can open.");
                }

                _soldiers.Animate(
                    graphs, levelCast, _resources, _loop.Natives, levelRoot, _aiConfig, effectiveDifficulty);

                // A guard's round: it raised the trigger, the weapon's cadence let one out, and this is
                // where it becomes a cast. The AI's hit probability decides whether the round is on
                // target -- the original hands that same figure to the gun with every shot (ai.md 11.6),
                // so a miss is a round that went somewhere else rather than a round that never existed.
                _soldiers.SetRoundHandler((from, at, probability, weapon) =>
                {
                    _guardRounds++;
                    if (_player is null)
                    {
                        return;
                    }

                    // The ray runs where the GUN points -- the tracked aim (or a patrol's own facing) --
                    // extended past the target, exactly as the retail round is a cast from the muzzle to
                    // the end of its remaining range (GunShot per-tick 0x47B750). A moving player walks
                    // out of the lagging aim and the round geometrically misses; that is retail's miss.
                    Vector3d to = new(
                        from.X + ((at.X - from.X) * 4.0),
                        from.Y + ((at.Y - from.Y) * 4.0),
                        from.Z + ((at.Z - from.Z) * 4.0));

                    Vector3d feet = _player.Body.Position;

                    // Resolve the guard's round against the player's OWN skinned body (limb hits do less,
                    // and the hitbox is the real body, not a fat cylinder). Only if the body is not loaded
                    // does it fall back to the cylinder-and-trunk stand-in.
                    HumanHit hit = default;
                    bool met = _soldiers is not null
                        && _soldiers.TryHitPlayerBody(from, to, feet, _player.Body.Yaw, out hit);
                    if (!met)
                    {
                        Vector3f facing = new(-MathF.Sin(_player.Body.Yaw), MathF.Cos(_player.Body.Yaw), 0f);
                        met = HumanHitTest.TryCast(from, to, feet, facing, pose: null, out hit);
                    }

                    // Where the world stops this ray, over its whole length -- one cast serves cover,
                    // fly-pasts and outright misses alike, and EVERY round that meets a surface leaves
                    // the same mark a player's round does. In retail a miss is visible: the round lands
                    // on the wall or the dirt near you (10.4's decals) -- rounds that silently vanished
                    // are why the guards seemed to hit with every bullet.
                    bool worldBlocked = levelCast(from, to, out WorldRayHit worldHit);
                    void MarkTheImpact()
                    {
                        if (worldBlocked && _worldMaterials is { } table)
                        {
                            _player.Marks.TryAdd(worldHit, table);
                        }
                    }

                    if (!met)
                    {
                        // Geometric miss -- aim lag against a moving target, or a running patrol's
                        // spray. The round flies on and lands where the world says.
                        MarkTheImpact();
                        return;
                    }

                    // The world is in the way: the retail cast meets the nearest surface first, so a wall
                    // or a crate between the muzzle and the body simply takes the round (0x47B750 -- the
                    // round penetrates or stops per the surface, but it does not reach the player). The
                    // small margin keeps the player's own contact from re-testing the surface it stands on.
                    if (worldBlocked && worldHit.Distance < hit.Distance - 40.0)
                    {
                        MarkTheImpact();
                        return;
                    }

                    // Only now the probability gate: retail rolls `random > probability -> no hit` inside
                    // the cast's evaluate when the trace actually MEETS a human (the context carries
                    // Human+1236, loaded at spawn by sub_474360). A failed roll is a round that flew past
                    // -- through to whatever stands behind the player, where it marks.
                    if (_shotRandom.NextDouble() > probability)
                    {
                        MarkTheImpact();
                        return;
                    }

                    if (_playerHealth.IsDead)
                    {
                        // Already down. Nothing kills you twice, and without this the log filled with
                        // hundreds of "DEAD" lines while the guards kept shooting the corpse.
                        return;
                    }

                    _playerHealth.Apply(hit.Zone, weapon.DamageFactor);
                    _guardHits++;

                    // The screen goes red. A full wash per hit, so a burst holds it solid and it fades
                    // over a second and a half once the fire stops -- the retail decay (sub_411300).
                    _damageFlash.Hit();

                    // Every connecting round SHOVES the player 5 km/h along its travel -- the hit
                    // handler (0x412440) normalises the round's direction and adds x/y x 189.62962
                    // into the slide accumulator, whose 0.9-a-tick damping then bleeds it off. This is
                    // what makes sustained fire stagger the player in retail.
                    if (!_playerHealth.IsDead)
                    {
                        double shoveX = to.X - from.X;
                        double shoveY = to.Y - from.Y;
                        double shoveZ = to.Z - from.Z;
                        double length = Math.Sqrt((shoveX * shoveX) + (shoveY * shoveY) + (shoveZ * shoveZ));
                        if (length > 1e-6)
                        {
                            _player.Body.AddHitShove(
                                (float)(shoveX / length * PlayerBody.HitShoveUnitsPerTick),
                                (float)(shoveY / length * PlayerBody.HitShoveUnitsPerTick));
                        }
                    }

                    _logger.Information(
                        "Weapon",
                        $"A guard's {weapon.ScriptId} hit the player in the {HumanDamage.ZoneName(hit.Zone)} "
                            + $"({_guardHits} of {_guardRounds} round(s); "
                            + $"{_playerHealth.RemainingFraction:P0} left).");

                    if (_playerHealth.IsDead)
                    {
                        // Locomotion state 47 (0x40E560): the clip comes from the LAST ZONE STRUCK's row
                        // in the player's own hit-zone table, one of its two reactions by rand(2). The
                        // player stops taking input from here on -- it is a state, not a message.
                        _playerDead = true;
                        int fall = HumanPlayerDeath.Choose(
                            hit.Zone, HumanHitZoneTable.Player, _shotRandom.Next(2));

                        // Hold the screen red. The wash decays a forty-fifth a tick, so on death it is
                        // pinned to full and left to fade -- the last thing the player sees is red.
                        _damageFlash.Hit();

                        // Which way the body is thrown. The fall clip carries the body along its own
                        // backward axis, so the facing decides where "backward" points -- and it has to
                        // point AWAY FROM THE SHOOTER, so a hit in the stomach throws you onto your back.
                        // The soldier death does exactly this (SoldierDeath.TurnToTheShot, 0x45DAA0):
                        // it faces the body at the incoming round. Retail's player death instead falls
                        // along the look direction (0x40E560 does not turn), which only reads right when
                        // you were looking straight at the shooter -- so we take the soldier's shot-facing,
                        // which is right however you were facing. The heading whose backward axis (the
                        // clip's -Y, rotated) points from the shooter to the body is atan2(dx, -dy).
                        double throwX = _player.Body.Position.X - from.X;
                        double throwY = _player.Body.Position.Y - from.Y;
                        double throwLength = Math.Sqrt((throwX * throwX) + (throwY * throwY));
                        float throwFacing = throwLength > 1e-6
                            ? (float)Math.Atan2(throwX / throwLength, -(throwY / throwLength))
                            : _player.Body.Yaw;

                        // Locomotion state 47 (0x40E560) plays the fall clip on the body rig; here that is
                        // the player's third-person body, posed with the clip the last zone struck chose.
                        // The camera hands itself to the death view and pulls back off the eye, so the fall
                        // is seen from the outside -- the original's death sequence.
                        _soldiers?.KillPlayerBody(fall, _player.Body.Position, throwFacing);
                        WorldRayCast deathCaster = solids.Caster();
                        _deathCamera.Begin(
                            _player.Body.Position,
                            throwFacing,
                            (f, t, out h) =>
                            {
                                bool blocked = deathCaster(f, t, out WorldRayHit wall);
                                h = blocked ? wall.Position : default;
                                return blocked;
                            });

                        // The death SOUND is still not played -- there is no audio subsystem yet, so the
                        // three 'player_death_' variants have nothing to play through. Reported, not faked.
                        _logger.Information(
                            "Weapon",
                            $"The player is DEAD after {_guardHits} hit(s) from {_guardRounds} round(s), "
                                + $"struck in zone {hit.Zone}. Falling with animation {fall}; the death "
                                + $"camera has pulled back. One of {HumanPlayerDeath.SoundVariants} "
                                + $"'{HumanPlayerDeath.SoundPrefix}' sounds is NOT PLAYED (no audio yet).");
                    }
                });
            }
            else
            {
                _logger.Warning("Ai", $"No nav graph under '{graphDirectory}', so no soldier will patrol.");
            }
        }
        catch (Exception ex) when (ex is IOException or GraphFormatException)
        {
            _logger.Warning("Ai", $"The nav graph could not be read: {ex.Message}");
        }

        // A level whose terrain files are not live data — level 14 is exactly that — draws its objects
        // over nothing rather than following offsets into whatever the geometry blob happens to hold.
        // The surface comes from the level's own TextureModifier tasks, so it needs the loaded tree.
        _terrain?.Dispose();
        _terrain = loaded.Terrain is { IsUsable: true } usable
            ? new TerrainRenderer(_device, TerrainSurface.Build(usable, levelRoot, _logger), _logger, _detailMaps)
            : null;

        // The sky is script data on the level's task tree; its fog colour is also what the frame
        // clears to, so the horizon meets sky-coloured backing rather than black.
        _sky?.Dispose();
        _sky = new SkyRenderer(_device, _fileSystem, levelRoot, FarPlane, _logger);
        _shadowPass = new ShadowCasterPass(_device);

        if (DirectionalLightSettings.TryRead(levelRoot, out DirectionalLightSettings readSun))
        {
            _sun = readSun;
            _logger.Information(
                "Light",
                FormattableString.Invariant(
                    $"Sun at {MathF.Asin(Math.Clamp(readSun.Direction.Z, -1f, 1f)) * 180f / MathF.PI:F1} degrees ")
                + FormattableString.Invariant(
                    $"elevation, time {readSun.TimeOfDay:F0}, colour ({readSun.Colour.X:F2}, {readSun.Colour.Y:F2}, {readSun.Colour.Z:F2}), ")
                + FormattableString.Invariant(
                    $"ambient ({readSun.Ambient.X:F2}, {readSun.Ambient.Y:F2}, {readSun.Ambient.Z:F2})."));

            if (readSun.IsBelowHorizon)
            {
                _logger.Warning("Light", "This level's authored sun is below the horizon; see rendering.md section 12.");
            }
        }
        else
        {
            _logger.Information("Light", "The level declares no Dirlight; the modern path will light it with a default sun.");
        }

        if (_sky.HasSky)
        {
            _scheduler.ClearColour = new Bgra32(
                (byte)Math.Clamp((int)(_sky.FogColour.Z * 255f), 0, 255),
                (byte)Math.Clamp((int)(_sky.FogColour.Y * 255f), 0, 255),
                (byte)Math.Clamp((int)(_sky.FogColour.X * 255f), 0, 255),
                255);
        }

        // Open on the middle of what was placed, backed off and looking down slightly, so the first
        // frame shows the level rather than the inside of whichever object happens to be at the origin.
        // -campos overrides that with an exact spot, which is how a bug at a known position is looked at.
        _camera = _options.CameraPosition is { } at
            ? new FreeCamera(new Vector3d(at.X, at.Y, at.Z), _options.CameraYaw, _options.CameraPitch)
            : FreeCameraFactory.Create(_scene, _options.CameraYaw, _options.CameraPitch, _options.CameraHeight);

        // The player stands where the camera looks from, dropped onto whatever is under it. A level
        // whose terrain is not live data has nothing to stand on, so the player stays unavailable and
        // F1 does nothing.
        _playerTerrain = loaded.Terrain is { IsUsable: true } standable ? standable : null;
        _player = null;
        _playerMode = false;

        // A fresh mission is a fresh life: the last one's death, its red wash and its death camera do not
        // carry over.
        _playerHealth = new(HumanHitZoneTable.Player, _playerMaximumHealth)
        {
            DamageScale = HumanHealth.DifficultyDamageScale(_playerDifficulty),
        };
        _playerDead = false;
        _damageFlash = new();
        _deathCamera.Reset();

        if (_playerTerrain is not null)
        {
            PlayerCamera candidate = new();

            // 000.iff, kept so the player's third-person body can be built from the same set it walks on
            // (HumanPlayerBody: the body is skeleton 0, and this is skeleton 0's set).
            Formats.Animation.BoneAnimationSet? playerBodySet = null;

            // Walking is the animation's own root motion, so without a set the player can stand and
            // fall but not walk. 000.iff is the set the player's skeleton indexes.
            try
            {
                string animations = Path.Combine(_gamePath, "common", "anims", "000.iff");
                if (File.Exists(animations))
                {
                    playerBodySet = Formats.Animation.BoneAnimationSet.Read(File.ReadAllBytes(animations));
                    candidate.UseAnimations(playerBodySet);
                }
                else
                {
                    _logger.Warning("Player", $"No animation set at {animations}; the player cannot walk.");
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                _logger.Warning("Player", $"Could not read the player's animations: {ex.Message}");
            }

            // The walls. Terrain alone gives the player ground to stand on but nothing to stop it
            // walking through crates and buildings; the solidity is in each model's own HSMC mesh.
            // The index is the one built above for the soldiers' ground probe.
            candidate.UseWalls(solids);
            _logger.Information(
                "Player",
                $"{solids.Count} placed object(s) are solid, {solids.TriangleCount} collision triangle(s).");

            // The ladders the level attaches to its buildings, so the player can climb them (E to mount,
            // W/S up and down, E held to slide).
            if (_scene is not null)
            {
                candidate.UseLadders(_scene.Ladders);
                _logger.Information("Player", $"{_scene.Ladders.Count} ladder(s) to climb.");
            }

            // ORDER MATTERS, and it is the original's. WEAPON_ID_* and AMMO_ID_* are pattern-registered
            // symbols that get their values as the two tables are read, so the rosters have to load
            // before humanplayer.qvm runs -- otherwise every id in its weapon cycle is a placeholder
            // and the cycle names weapons nobody carries. section 8.
            WeaponTypeRegistry weaponRoster = new(_logger);
            AmmoTypeRegistry ammoTable = new(_logger);
            GameMaterialRegistry materialTable = new(_logger);
            try
            {
                weaponRoster.Register(_loop.Natives);
                weaponRoster.Load(_fileSystem, _resources, _loop.Natives);
                ammoTable.Register(_loop.Natives);
                ammoTable.Load(_fileSystem, _resources, _loop.Natives);

                // What every surface in the world is made of, and therefore what a round leaves on it.
                materialTable.Register(_loop.Natives);
                if (_fileSystem.Exists(GameMaterialRegistry.ScriptResource))
                {
                    _resources.Register(
                        GameMaterialRegistry.ScriptResource,
                        _fileSystem.ReadAllBytes(GameMaterialRegistry.ScriptResource));
                }
                else
                {
                    _logger.Warning("Material", $"No material table at {GameMaterialRegistry.ScriptResource}.");
                }

                int materialCount = materialTable.Load(_resources, _loop.Natives);
                candidate.UseMaterials(materialTable);
                _worldMaterials = materialTable;
                _logger.Information("Material", $"Read {materialCount} game material(s).");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException)
            {
                _logger.Warning("Weapon", $"Could not read the weapon or ammunition tables: {ex.Message}");
            }

            // The guards' weapons, which have to wait for the roster: a soldier's gun is an authored
            // Gun child task naming a WEAPON_ID_*, and only the table knows which model that draws.
            _soldiers?.Arm(loaded, weaponRoster);

            // The player's tuning is not in the executable: humanplayer.qvm is the whole of it, and it
            // is what says how high a jump goes and how far a step travels.
            try
            {
                HumanPlayerConfig config = new();
                config.Register(_loop.Natives);
                config.Load(_resources, _loop.Natives);

                if (config.PlaceholderIds.Count > 0)
                {
                    _logger.Warning(
                        "Player",
                        $"humanplayer.qvm left {config.PlaceholderIds.Count} id(s) unresolved; the weapon "
                            + "cycle may name weapons that do not exist.");
                }

                if (config.Parameters is { } tuning)
                {
                    candidate.UseParameters(tuning);

                    // Argument 7 is the player's maximum health (Human+600, 0x410370's overwrite) --
                    // 3.0 in retail, and the single number that sets the player's time-to-kill.
                    _playerMaximumHealth = tuning.MaximumHealth;
                    _playerHealth = new(HumanHitZoneTable.Player, _playerMaximumHealth)
                    {
                        DamageScale = HumanHealth.DifficultyDamageScale(_playerDifficulty),
                    };
                }
                else
                {
                    _logger.Warning("Player", "humanplayer.qvm published no parameters; using the retail defaults.");
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
                _logger.Warning("Player", $"Could not run humanplayer.qvm: {ex.Message}");
            }

            // The first-person rig. Every Human builds one in its constructor and gives it skeleton 5
            // and the model 005_01_1 (0x45EB30, 0x464660); it is what the held weapon hangs off, so
            // without it there is nothing to draw a gun on.
            try
            {
                string viewRig = Path.Combine(_gamePath, "common", "anims", "005.iff");
                if (File.Exists(viewRig))
                {
                    candidate.UseViewRig(
                        Formats.Animation.BoneAnimationSet.Read(File.ReadAllBytes(viewRig)));

                    // The arms are the other half of what 0x4644A0 draws, and the half that needs CPU
                    // skinning: 005_01_1 is mesh format 1.
                    _arms?.Dispose();
                    _arms = new ViewModelRenderer(_device, _resources, _logger);
                    if (!_arms.Load(loaded))
                    {
                        _arms.Dispose();
                        _arms = null;
                    }
                }
                else
                {
                    _logger.Warning("Player", $"No view rig at {viewRig}; no weapon can be held.");
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                _logger.Warning("Player", $"Could not read the first-person rig: {ex.Message}");
            }

            // What the mission arms you with is declared, not computed: weapon tasks hang off the
            // level's own HumanPlayer, and every WEAPON_ID_* resolves to a roster entry naming a
            // Gun Model. With the rig in place that model can finally be put where the game puts it.
            _heldWeapon?.Dispose();
    _hud?.Dispose();
    _marks?.Dispose();
            _heldWeapon = null;

            if (PlayerLoadout.TryRead(levelRoot, out PlayerLoadout? loadout) && loadout is not null)
            {
                _logger.Information("Player", $"Carrying {loadout}.");
                _heldWeapon = CarryLoadout(candidate, loadout, loaded, weaponRoster, ammoTable);
                _loadedMissionForWeapon = loaded;

                // The player's rounds are offered to the soldiers, which own their own health.
                if (_soldiers is { } guards)
                {
                    candidate.Humans = guards.Shooter();
                }

                // The in-game HUD owns its own archive, exactly as StatusScreen's constructor does.
                _marks?.Dispose();
                _marks = new BulletMarkRenderer(_device, _resources, _logger);

                _hud?.Dispose();
                _hud = new StatusScreenRenderer(_device, _logger);
                if (!_hud.Load(_fileSystem))
                {
                    _hud.Dispose();
                    _hud = null;
                }
            }

            // The level places its own player: every mission's objects.qvm declares a HumanPlayer task
            // with a Position and a Gamma. Standing there is standing where the game starts you.
            bool placed = PlayerSpawn.TryFind(levelRoot, out PlayerSpawnPoint spawn)
                ? candidate.PlaceAt(_playerTerrain, spawn)
                : candidate.Place(_playerTerrain, _camera.Position, _options.CameraYaw);

            if (placed)
            {
                _player = candidate;

                // The player's own third-person body -- the rig the death camera reveals. Built from the
                // model the level's HumanPlayer declares, on skeleton 0's set (000.iff), the same one the
                // player walks on. It stays invisible until the player dies. Without the spawn's model or
                // the set there is simply no body to show, and death falls back to the frozen view.
                if (_soldiers is { } bodies
                    && playerBodySet is { } bodySet
                    && !string.IsNullOrEmpty(spawn.Model))
                {
                    bodies.LoadPlayerBody(loaded, spawn.Model, bodySet, spawn.Position, spawn.Gamma);
                }

                // Walking is what starting a mission means. The free camera is the debugging tool, so
                // it is the thing you switch to rather than the thing you start in.
                _playerMode = !_options.StartFlying;
                _logger.Information(
                    "Player",
                    _playerMode
                        ? $"Standing at {candidate.Position}; F1 for the free camera."
                        : $"Free camera; F1 to stand at {candidate.Position}.");
            }
            else
            {
                _logger.Warning(
                    "Player",
                    "Nothing to stand on at the level's spawn, so this mission opens in the free camera.");
            }
        }

        // The menus are done; without dropping the renderer the last screen would keep drawing over
        // the world.
        _renderer = null;

        // The menu's backdrop, and with it the private resource context a preview was built in, is not
        // needed once a real mission is up; letting it go releases that level's packs.
        _backdrop = null;
        _logger.Information("Mission", $"Loaded {loaded}.");
    }

    // Puts a weapon in the player's hands and loads the model its roster entry names.
    //
    // Hands the player everything the mission declares, with the ammunition each weapon draws from.
    // WHICH of them ends up in hand is no longer decided here: the weapon cycler's index starts at -1
    // and the first tick lands on entry 0 of DefineHumanPlayerWeaponCycle, stepping on until it finds
    // one that is carried. That retires the divergence this function used to carry -- see
    // the original. The renderer's model follows whatever ends up in hand.
    private HeldWeaponRenderer? CarryLoadout(
        PlayerCamera candidate,
        PlayerLoadout loadout,
        LoadedMission loadedMission,
        WeaponTypeRegistry roster,
        AmmoTypeRegistry ammunition)
    {
        if (candidate.View is null || loadout.Weapons.Count == 0)
        {
            return null;
        }

        foreach (LoadoutWeapon declared in loadout.Weapons)
        {
            if (!roster.TryGetByScriptId(declared.WeaponId, out WeaponType weapon))
            {
                _logger.Warning("Weapon", $"{declared.WeaponId} is not in the roster; it is not carried.");
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

        return new HeldWeaponRenderer(_device, _resources, _logger);
    }

    // Keeps the drawn model on whatever the cycle has put in hand. Called every frame; it only does
    // anything on the tick the weapon actually changed.
    private void FollowHeldWeapon(PlayerCamera candidate, HeldWeaponRenderer? held, LoadedMission loadedMission)
    {
        if (held is null || !candidate.WeaponChanged)
        {
            return;
        }

        candidate.AcknowledgeWeaponChange();

        if (candidate.HeldWeaponType is not { } weapon)
        {
            return;
        }

        if (held.Load(loadedMission, weapon.GunModel))
        {
            _logger.Information("Weapon", $"Holding {weapon.ScriptId} ('{weapon.GunModel}') on view bone "
                + $"{HumanAttachPoints.FirstPersonWeapon}.");
        }
    }
}
