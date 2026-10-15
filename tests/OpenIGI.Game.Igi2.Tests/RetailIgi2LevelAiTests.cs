using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Ai.Runtime;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Mission 1's soldiers given their minds and squads, and run, as <c>HumanAI</c> and <c>AISquad</c> load and run them.</summary>
public sealed class RetailIgi2LevelAiTests
{
    /// <summary>A registration-only backdrop must not redirect the playing AI's script namespace.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Preview_registration_keeps_the_playing_ai_namespace()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2AiWorld ai = m.Services!.Require<Igi2AiWorld>();
        Assert.Same(m.Run.Natives, ai.Natives);
        Engine.Flow.GameLoop preview = new(new Engine.Time.ManualTimeSource(1000),
            structuralTypes: Igi2Runtime.Instance.Layout.StructuralTaskTypes);
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        Engine.Titles.TitleSession.Start(Igi2Runtime.Instance.Modules, preview, files, new ResourceRegistry(files),
            loadTables: false, inherited: m.Services);
        Assert.Same(m.Run.Natives, ai.Natives);
    }

    /// <summary>The desktop gameplay factory starts living minds and advances their animation after skipping the intro.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Gameplay_factory_starts_living_guards()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(m.Level);
        resources.FileSystem.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location1/level1/");
        // The menu builds a second loop with inherited tables, then promotes that loop for play.
        Engine.Flow.GameLoop promoted = new(new Engine.Time.ManualTimeSource(1000),
            structuralTypes: Igi2Runtime.Instance.Layout.StructuralTaskTypes);
        Engine.Titles.TitleSession.Start(Igi2Runtime.Instance.Modules, promoted, resources.FileSystem, resources,
            loadTables: false, inherited: m.Services);
        Engine.Tasks.LevelTask root = (Engine.Tasks.LevelTask)promoted.Tasks.New(promoted.Tasks.Root,
            promoted.Types.Get(Engine.Tasks.EngineTaskTypes.Level));
        new Engine.Level.LevelLoader(root, promoted.TaskCommands, promoted.Natives, resources).LoadScript("MISSION:objects.qvm");
        Assert.Same(m.Run.Natives, m.Services!.Require<Igi2AiWorld>().Natives);
        MissionDescriptor descriptor = new(11, "igi2", string.Empty, string.Empty,
            MissionDescriptor.NextMissionUndefined, "LOCAL:missions/location1/level1/", packs[0].Directory,
            packs[^1].Directory, packs[0].Name, packs[^1].Name, string.Empty);
        LoadedMission mission = new(descriptor, packs[0], packs[^1], [], Packs: packs);
        WorldBuildRequest world = new(mission, root, resources, null,
            Core.Diagnostics.NullLogger.Instance, Services: m.Services);
        WorldBuildResult built = Igi2WorldBuilder.Instance.Build(world);
        // The desktop's shared soldier renderer registers the first game's AI before standing the title player.
        _ = new Game.Ai.AiScriptHost(promoted.Natives);
        Igi2TitlePlayer player = Assert.IsType<Igi2TitlePlayer>(Igi2Gameplay.Instance.CreatePlayer(
            new TitlePlayerRequest(world, built, m.Services!, Game.Ai.AiDifficulty.Normal, Natives: promoted.Natives)));
        Assert.NotNull(player.Level);
        Assert.NotNull(player.Ai);
        Assert.Same(promoted.Natives, player.Ai.Natives);
        Igi2HumanAi[] minds = [.. player.Soldiers!.Bodies.Select(static body => body.Mind).OfType<Igi2HumanAi>()];
        Assert.NotEmpty(minds);
        Assert.All(minds, static mind => Assert.NotNull(mind.Script));
        player.Human.Damage.MaxDamage = 1000000f;
        Vector3d[] initialPositions = [.. minds.Select(static mind => mind.Soldier.Position)];
        for (int tick = 0; tick < 1200; tick++)
        {
            player.Tick(RetailIgi2Mission1.Idle with { BreakCutscene = true });
        }
        Assert.True(minds.Where((mind, index) => Math.Abs(mind.Soldier.Position.X - initialPositions[index].X)
            + Math.Abs(mind.Soldier.Position.Y - initialPositions[index].Y) > 4096.0).Any(),
            string.Join(Environment.NewLine, player.DescribeActors()) + Environment.NewLine
            + string.Join(Environment.NewLine, minds.Select(static mind => $"{mind.Task.Id}: actions={mind.Actions.Top?.Run?.Method.Name}, patrols={mind.Patrols.Count}, enemies={mind.Detection.Enemies.Count}, team={mind.Soldier.Team}"))
            + $" flowTick={player.Level.LevelFlow?.Tick}, playerTeam={player.Human.Team}");
        Igi2HumanAi facing = Assert.Single(minds, static mind => mind.Task.Parent?.Id == 400);
        Matrix3f orientation = facing.Soldier.Orientation;
        Vector3d soldierAt = facing.Soldier.Position;
        player.Human.Position = new Vector3d(soldierAt.X + (orientation.M12 * 61440.0),
            soldierAt.Y + (orientation.M22 * 61440.0), soldierAt.Z);
        player.Human.UpdateViewBody(updateAnimation: false);
        player.Human.Damage.MaxDamage = 1000000f;
        int ammunition = facing.AmmoInWeapon();
        Assert.True(ammunition > 0);
        bool detected = false;
        int shots = 0, lastAmmo = ammunition;
        bool reloaded = false;
        float damageBefore = player.Human.Damage.Damage;
        for (int tick = 0; tick < 600; tick++)
        {
            player.Tick(RetailIgi2Mission1.Idle with { BreakCutscene = true });
            detected |= facing.Squad!.Targets.Count > 0;
            int ammo = facing.AmmoInWeapon();
            shots += Math.Max(lastAmmo - ammo, 0);
            reloaded |= ammo > lastAmmo;
            lastAmmo = ammo;
        }
        Assert.True(detected);
        Assert.True(reloaded);
        Assert.True(shots > ammunition, $"Guard fired {shots} rounds with an initial magazine of {ammunition}.");
        Assert.True(player.Human.Damage.Damage > damageBefore, "Guard bullets must damage the player.");
        using (RecordingGraphicsDevice device = new())
        {
            OpenIGI.Rendering.SpriteBatch batch = new(device);
            Presentation.ITitleHudPresenter hud = Assert.IsAssignableFrom<Presentation.ITitleHudPresenter>(
                Igi2PresentationProvider.Instance.CreateTitleHud(new Presentation.HudPresentationRequest(device, Core.Diagnostics.NullLogger.Instance)));
            player.GameConfig = new Config.Igi2GameConfig();
            batch.Begin();
            hud.Draw(batch, player, 1280, 720);
            batch.End();
            Assert.NotEmpty(device.Draws);
            Assert.All(device.Draws.SelectMany(static draw => draw.Vertices), static vertex => Assert.True(vertex.Color.A > 0));
            device.Draws.Clear();
            player.GameConfig.Active!.CrossHairAlpha = 0f;
            batch.Begin();
            hud.Draw(batch, player, 1280, 720);
            batch.End();
            Assert.Empty(device.Draws);
        }
        Assert.All(minds, static mind => Assert.True(mind.HasStarted,
            $"AI {mind.Task.Id}: graph={mind.Task.GraphId}/{mind.Graph is not null}, active={mind.Soldier.IsActive}, time={mind.Soldier.MissionTime}"));
        Assert.All(minds, static mind => Assert.True(mind.Soldier.Animation.MainTime > 0,
            $"AI {mind.Task.Id}: animation={mind.Soldier.Animation.MainTime}, step={mind.Soldier.Animation.MainTimeStep}, state={mind.Soldier.Locomotion.CurrentState}"));
    }

    /// <summary>
    /// Every squad gets its soldiers as one team, in its children's order, and every soldier with a <c>HumanAI</c> gets a mind: its
    /// script read from <c>MISSION:AI/&lt;id&gt;</c>, its graph and the node nearest it, and its type's tuning.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Every_soldier_gets_its_mind_and_squad()
    {
        (RetailIgi2Mission1 m, Igi2Soldiers soldiers, Igi2AiWorld ai, _) = Build();
        Igi2HumanSoldierTask[] tasks = [.. m.Tree.OfType<Igi2HumanSoldierTask>()];

        Assert.NotNull(ai.Default);
        Assert.NotNull(ai.SquadDefault);
        Assert.NotEmpty(ai.Squads);
        foreach (Igi2HumanSoldierTask task in tasks.Where(static t => t.Ai is not null))
        {
            Igi2HumanAi mind = Assert.IsType<Igi2HumanAi>(task.Body!.Mind);
            Assert.NotNull(mind.Script);
            Assert.NotNull(mind.Squad);
            if (mind.Graph is not null)
            {
                Assert.Equal(mind.OriginalNode, mind.CurrentNode);
            }
        }

        foreach (Igi2AiSquad squad in ai.Squads)
        {
            Igi2HumanAi[] members = [.. squad.Task.Soldiers.Select(static s => s.Body?.Mind).OfType<Igi2HumanAi>().Take(Igi2AiSquadTeam.Capacity)];
            Assert.Equal(members, squad.Teams[0].Members);
            Assert.Equal(members.Length > 0 ? 1 : 0, squad.TeamCount);
        }

        Assert.All(soldiers.Bodies, body => Assert.Contains(body, ai.Detection!.Detectables));
    }

    /// <summary>
    /// Run ten seconds with the player out of sight, every mind starts, its script having run for its start event, and no
    /// script faults: every native the soldiers' and squads' scripts call is bound.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_minds_run_their_scripts()
    {
        (RetailIgi2Mission1 m, Igi2Soldiers soldiers, Igi2AiWorld _, _) = Build();
        for (int tick = 0; tick < 300; tick++)
        {
            m.Run.Tick();
        }

        foreach (Igi2HumanSoldier body in soldiers.Bodies.Where(static b => b.Mind is Igi2HumanAi))
        {
            Igi2HumanAi mind = (Igi2HumanAi)body.Mind!;
            Assert.True(mind.HasStarted, $"the mind of soldier {mind.Task.Id} never started");
        }
    }

    /// <summary>
    /// With the intro skipped (<c>EditVariable_2112</c>, which ends <c>ConditionalContainer_3532</c> and lets the soldiers' containers
    /// run) and the player fifteen metres in front of soldier 400, his squad lists the player as a target and he fires his
    /// Makarov, reloads from the soldier's unlimited reserve and resumes firing.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_soldier_fights_the_player_in_front_of_him()
    {
        (RetailIgi2Mission1 m, _, _, Igi2TitlePlayer player) = Build(facing: 400);
        m.Tree.OfType<Flow.Igi2EditVariableTask>().Single(static v => v.Id == 2112).Value = 1;
        Igi2HumanAi mind = (Igi2HumanAi)m.Tree.OfType<Igi2HumanSoldierTask>().Single(static t => t.Id == 400).Body!.Mind!;
        bool seen = false;
        int shots = 0, last = mind.AmmoInWeapon();
        for (int tick = 0; tick < 1800; tick++)
        {
            m.Run.Tick();
            player.Tick(RetailIgi2Mission1.Idle);
            seen |= mind.Squad!.Targets.Count > 0;
            int ammo = mind.AmmoInWeapon();
            shots += Math.Max(last - ammo, 0);
            last = ammo;
        }

        Assert.True(seen);
        Assert.True(shots > 8, $"The guard must fire again after his first magazine; shots={shots}.");
    }

    /// <summary>
    /// Three hundred ticks in with the intro skipped, the soldiers, their minds and their squads are saved, and loaded into the mission built afresh: each
    /// soldier stands where it stood, alive or dead as it was; each mind is on its node with its flags and its patrols at the same
    /// commands; each squad has its flags, suspicion and actions.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Soldiers_and_squads_saved_and_loaded_are_as_they_were()
    {
        (RetailIgi2Mission1 played, Igi2Soldiers bodies, _, _) = Build();
        played.Tree.OfType<Flow.Igi2EditVariableTask>().Single(static v => v.Id == 2112).Value = 1;
        for (int tick = 0; tick < 300; tick++)
        {
            played.Run.Tick();
        }

        string script = Saves.Igi2SaveGame.Create(played.Tree[0], new Saves.Igi2SaveInfo(Saves.Igi2SaveInfo.CurrentVersion, 11, 0))!;
        Assert.Contains("\"HumanAI\"", script, StringComparison.Ordinal);
        Assert.Contains("\"AISquad\"", script, StringComparison.Ordinal);

        (RetailIgi2Mission1 fresh, _, _, _) = Build();
        Saves.Igi2SaveGame.Load(script, fresh.Tree[0], fresh.Run.Natives, fresh.Run.Functions);
        Assert.Equal(Soldiers(played), Soldiers(fresh));
        Assert.Equal(Squads(played), Squads(fresh));
        Assert.Contains(bodies.Bodies, static b => b.Mind is Igi2HumanAi { Patrols.Count: > 0 });

        static IEnumerable<string> Soldiers(RetailIgi2Mission1 m) =>
            m.Tree.OfType<Igi2HumanSoldierTask>().Where(static t => t.Body is not null).Select(static t =>
            {
                Igi2HumanSoldier body = t.Body!;
                Igi2HumanAi? mind = body.Mind as Igi2HumanAi;
                return string.Join(
                    ' ',
                    t.Id,
                    body.Position,
                    body.IsDead,
                    mind?.CurrentNode,
                    mind?.Flags,
                    string.Join(',', mind?.Patrols.Items.Select(static p => p.Current?.Id) ?? []));
            });

        static IEnumerable<string> Squads(RetailIgi2Mission1 m) =>
            m.Tree.OfType<Igi2AiSquadTask>().Where(static t => t.Squad is not null).Select(static t =>
                string.Join(' ', t.Id, t.Squad!.Flags, t.Squad.SuspicionLevel, t.Squad.TenseTimer, string.Join(',', t.Squad.Actions.Select(static a => a.Kind))));
    }

    /// <summary>
    /// A save the retail game made of mission 11 (<c>savegames/0-0-11-0.igi2save</c>, compiled in place) is read: its header says
    /// version 63 and mission 11, and run into the mission built afresh it puts the level's clock and its tasks back where it was.
    /// Every retail save of the mission is read to exactly the end of each task's arguments, the player's and soldiers' included.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_retail_save_loads_into_its_mission()
    {
        string file = Path.Combine(GameDataSource.Igi2.RequireRoot(), "savegames", "0-0-11-0.igi2save");
        if (!File.Exists(file))
        {
            return;
        }

        byte[] bytes = File.ReadAllBytes(file);
        Saves.Igi2SaveInfo header = Saves.Igi2SaveGame.CheckFile(bytes);
        Assert.Equal(63, header.Version);
        Assert.Equal(11, header.MissionId);

        (RetailIgi2Mission1 m, _, _, Igi2TitlePlayer player) = Build();
        m.Tree.OfType<Igi2HumanPlayerTask>().Single().Human = player.Human;
        int before = m.Run.LevelFlow!.Tick;
        List<string> misread = [];
        Saves.Igi2SaveInfo loaded = Saves.Igi2SaveGame.LoadFile(bytes, m.Tree[0], m.Run.Natives, m.Run.Functions, misread);
        Assert.Equal(header, loaded);
        Assert.NotEqual(before, m.Run.LevelFlow.Tick);
        foreach (string other in Directory.GetFiles(Path.GetDirectoryName(file)!, "0-0-11-*.igi2save"))
        {
            (RetailIgi2Mission1 n, _, _, Igi2TitlePlayer np) = Build();
            n.Tree.OfType<Igi2HumanPlayerTask>().Single().Human = np.Human;
            Saves.Igi2SaveGame.LoadFile(File.ReadAllBytes(other), n.Tree[0], n.Run.Natives, n.Run.Functions, misread);
        }

        Assert.Empty(misread);
    }

    /// <summary>
    /// The slots: a save to slot 1 is listed with its mission time and found as the newest, its header checks, and it loads back; the
    /// load list ends with the restart line (12345678); the quick save is tagged; hard allows no slots.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Slots_save_list_and_load()
    {
        string folder = Path.Combine(Path.GetTempPath(), "openigi-igi2-slots-" + Guid.NewGuid().ToString("N"));
        try
        {
            (RetailIgi2Mission1 m, _, _, Igi2TitlePlayer player) = Build();
            m.Tree.OfType<Igi2HumanPlayerTask>().Single().Human = player.Human;
            Saves.Igi2SaveSlots slots = new(folder);
            Saves.Igi2SaveOwner owner = new(0, 1, 11);
            Assert.True(slots.Save(owner, 1, m.Tree[0], 30 * 3725));
            Assert.True(slots.Save(owner, Saves.Igi2SaveSlots.QuickSave, m.Tree[0], 30));
            File.SetLastWriteTimeUtc(slots.PathOf(Saves.Igi2SaveSlots.SlotFileName(owner, Saves.Igi2SaveSlots.QuickSave)), DateTime.UtcNow.AddHours(-1));
            Assert.True(File.Exists(Path.Combine(folder, "savegames", "0-1-11-1.igi2save")));

            Assert.Equal(new Saves.Igi2SaveInfo(63, 11, 30 * 3725), slots.Check(owner, 1));
            Assert.Equal(1, slots.CountValid(owner));
            Assert.True(slots.TryFindNewest(owner, out int newest));
            Assert.Equal(1, newest);
            IReadOnlyList<Saves.Igi2SaveListing> lines = slots.Listings(owner, static key => key == Saves.Igi2SaveSlots.QuickSaveKey ? "Quicksave" : "Mission Time");
            Assert.Equal([1, Saves.Igi2SaveSlots.QuickSave, 12345678], lines.Select(static l => l.Slot));
            Assert.EndsWith(" Mission Time: 1:02:05", lines[0].Text, StringComparison.Ordinal);
            Assert.Contains("[Quicksave]", lines[1].Text, StringComparison.Ordinal);
            Assert.Equal(new Saves.Igi2SaveListing(12345678, "menusystem/Restart Mission", true), lines[2]);

            (RetailIgi2Mission1 fresh, _, _, Igi2TitlePlayer again) = Build();
            fresh.Tree.OfType<Igi2HumanPlayerTask>().Single().Human = again.Human;
            Assert.True(slots.Load(owner, 1, fresh.Tree[0], fresh.Run.Natives, fresh.Run.Functions).IsValid);
            Assert.False(slots.Load(owner, 2, fresh.Tree[0], fresh.Run.Natives, fresh.Run.Functions).IsValid);
            Assert.Equal([3, 2, 0], Enumerable.Range(0, 3).Select(Saves.Igi2SaveSlots.AvailableSlots));
            Assert.True(Saves.Igi2SaveSlots.TryParseSlotFileName("savegames/0-1-11-5.igi2save", out Saves.Igi2SaveOwner parsed, out int slot));
            Assert.Equal((owner, 5), (parsed, slot));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// Retail's own slots for mission 11 list four saves and the restart line; the carry-over retail left for mission 12 loads onto the
    /// player's weapon with every value read and arms it, and one written here reads back the same way.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Retail_slots_and_carry_over_read()
    {
        string root = GameDataSource.Igi2.RequireRoot();
        if (!File.Exists(Path.Combine(root, "savegames", "missions", "co000012.qsc")))
        {
            return;
        }

        Saves.Igi2SaveSlots retail = new(root);
        Saves.Igi2SaveOwner owner = new(0, 0, 11);
        Assert.Equal(5, retail.Listings(owner, static key => key).Count);
        Assert.True(retail.TryFindNewest(owner, out _));

        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2PlayerWeapons weapons = Igi2Gameplay.Arm(m.Services!, m.Tree[0], m.Services!.Require<Igi2PlayerTuning>())!;
        (Igi2HumanPlayer armed, _, _) = Igi2PlayerFakes.OnFloor(weapons);
        m.Tree.OfType<Igi2HumanPlayerTask>().Single().Human = armed;
        List<string> misread = [];
        byte[] carry = File.ReadAllBytes(Path.Combine(root, "savegames", "missions", "co000012.qsc"));
        Assert.Equal(12, Saves.Igi2SaveGame.CheckFile(carry).MissionId);
        Saves.Igi2SaveGame.LoadFile(carry, m.Tree[0], m.Run.Natives, m.Run.Functions, misread);
        Assert.Empty(misread);
        Assert.Contains(weapons.Inventory.Weapons, static w => w.IsActive);

        string folder = Path.Combine(Path.GetTempPath(), "openigi-igi2-carry-" + Guid.NewGuid().ToString("N"));
        try
        {
            Saves.Igi2SaveSlots slots = new(folder);
            Saves.Igi2SaveOwner next = new(0, 0, 12);
            Assert.True(slots.SaveCarryOver(next, m.Tree.OfType<Igi2HumanPlayerTask>().Single()));
            byte[] written = File.ReadAllBytes(slots.PathOf(Saves.Igi2SaveSlots.CarryOverFileName(next)));
            Assert.EndsWith("co000012.qsc", Saves.Igi2SaveSlots.CarryOverFileName(next), StringComparison.Ordinal);
            Saves.Igi2SaveGame.LoadFile(written, m.Tree[0], m.Run.Natives, m.Run.Functions, misread);
            Assert.Empty(misread);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// The pause menu's saves: on normal two slots and three saves a mission, each save using one and none once they are spent; a load
    /// puts the mission back and counts itself; hard keeps no slots.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission_saves_spend_the_battery_and_count_loads()
    {
        string folder = Path.Combine(Path.GetTempPath(), "openigi-igi2-mission-saves-" + Guid.NewGuid().ToString("N"));
        try
        {
            (RetailIgi2Mission1 m, _, _, Igi2TitlePlayer player) = Build();
            m.Tree.OfType<Igi2HumanPlayerTask>().Single().Human = player.Human;
            int difficulty = 1;
            Saves.Igi2MissionSaves saves = new(new Saves.Igi2SaveSlots(folder), () => new Saves.Igi2SaveOwner(0, difficulty, 11), m.Run, player.Human);
            Assert.Equal(2, saves.Slots.Count);
            Assert.Equal(3, saves.SavesLeft);
            Assert.False(saves.Slots[0].IsUsed);

            Assert.True(saves.Save(0));
            Assert.True(saves.Save(1));
            Assert.False(saves.Save(2));
            Assert.Equal(1, saves.SavesLeft);
            Assert.True(saves.Slots[0].IsUsed);
            Assert.True(saves.Save(0));
            Assert.Equal(0, saves.SavesLeft);
            Assert.False(saves.Save(1));

            Assert.True(saves.Load(1));
            Assert.Equal(1, player.Human.GameLoads);
            Assert.False(saves.Load(4));

            difficulty = 2;
            Assert.Empty(saves.Slots);
            Assert.Equal(0, saves.SavesLeft);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// The panes of glass mission 11's buildings hang are keyed as retail keys them (<c>GlassContainer_MakeHashKey</c>): exactly the
    /// keys a retail save of the mission lists.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Glass_panes_are_keyed_as_retail_keys_them()
    {
        string file = Path.Combine(GameDataSource.Igi2.RequireRoot(), "savegames", "0-0-11-0.igi2save");
        if (!File.Exists(file))
        {
            return;
        }

        List<string> retail = [];
        OpenIGI.Scripting.Runtime.NativeRegistry calls = new();
        calls.RegisterConstant("TRUE", 1);
        calls.RegisterConstant("FALSE", 0);
        calls.RegisterFunction("SaveGameInfo", static _ => OpenIGI.Scripting.Runtime.QvmValue.Zero, 3);
        calls.RegisterFunction("BreakScript", static _ => OpenIGI.Scripting.Runtime.QvmValue.Zero);
        calls.RegisterFunction("DefineTaskState", static _ => OpenIGI.Scripting.Runtime.QvmValue.Zero);
        calls.RegisterFunction("GlassContainer", c =>
        {
            retail.Add(c.GetString(0));
            return OpenIGI.Scripting.Runtime.QvmValue.Zero;
        }, 2);
        new OpenIGI.Scripting.Runtime.QvmInterpreter(OpenIGI.Scripting.Qvm.QvmProgram.Read(File.ReadAllBytes(file)), calls).Run();

        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Collision.Igi2CollisionWorld world = Collision.Igi2CollisionWorld.Build(
            m.Level.Scene, m.Level.ModelFor, null, Igi2Gameplay.TerrainOwner, magic: m.Services!.Require<Igi2MagicObjTypes>());
        string[] ours = [.. world.Objects.Select(static o => (o.Owner as Collision.Igi2CollisionOwner)?.GlassKey).OfType<string>()];
        Assert.NotEmpty(retail);
        Assert.Equal(retail.Order(StringComparer.Ordinal), ours.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A pane takes a bullet's damage and holds until it reaches its threshold, between three quarters and one and a half; then it
    /// breaks, says so once, leaves the world on its glass tick (no solid, gone from the drawing, a 1 in the save) and is whole again
    /// when a save does not name it.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_pane_of_glass_breaks_and_comes_back()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Collision.Igi2CollisionWorld world = Collision.Igi2CollisionWorld.Build(
            m.Level.Scene, m.Level.ModelFor, null, Igi2Gameplay.TerrainOwner, magic: m.Services!.Require<Igi2MagicObjTypes>());
        Interaction.Igi2GlassContainer glass = Interaction.Igi2GlassContainer.Build(world, m.Level.ModelFor, new IgiRandom(), explosions: null);
        Assert.NotEmpty(glass.Panes);
        Assert.All(glass.Panes, static p => Assert.InRange(p.Threshold, 0.75f, 1.5f));

        Interaction.Igi2Glass pane = glass.Panes[0];
        List<Interaction.Igi2GlassShatter> shattered = [];
        pane.Shattered = shattered.Add;
        Collision.Igi2Intersection at = new() { Position = pane.Solid.Position };
        Weapons.Igi2WeaponTypeTask weapon = new();
        pane.WeaponHit(new Weapons.Igi2WeaponHit(at, 0.5f, new Vector3f(1000f, 0f, 0f), null, weapon, null));
        Assert.False(pane.IsBroken);
        pane.WeaponHit(new Weapons.Igi2WeaponHit(at, 1f, new Vector3f(1000f, 0f, 0f), null, weapon, null));
        Assert.True(pane.IsBroken);
        pane.WeaponHit(new Weapons.Igi2WeaponHit(at, 1f, new Vector3f(1000f, 0f, 0f), null, weapon, null));
        Interaction.Igi2GlassShatter shatter = Assert.Single(shattered);
        Assert.True(shatter.IsLocalImpact);
        Assert.Equal(7f, shatter.Velocity.X, 3);

        Assert.NotNull(pane.Solid.Mesh(0));
        glass.Run();
        Assert.True(pane.IsRemoved);
        Assert.Null(pane.Solid.Mesh(0));
        Assert.True(glass.IsGone(((Collision.Igi2CollisionOwner)pane.Solid.Owner).LevelId, pane.Solid.Position));
        Assert.Contains($"GlassContainer(\"{pane.Key}\", 1);", glass.SaveLines(), StringComparison.Ordinal);

        glass.Restore(new Dictionary<string, int>());
        Assert.False(pane.IsBroken);
        Assert.False(pane.IsRemoved);
        Assert.NotNull(pane.Solid.Mesh(0));
    }

    /// <summary>The pane retail's save of mission 12 says is broken (<c>0-603_08_1-450</c>) is broken once loaded, and gone after its tick.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_retail_save_breaks_its_broken_glass()
    {
        string file = Path.Combine(GameDataSource.Igi2.RequireRoot(), "savegames", "0-0-12-2.igi2save");
        if (!File.Exists(file))
        {
            return;
        }

        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(levelName: "level2");
        Collision.Igi2CollisionWorld world = Collision.Igi2CollisionWorld.Build(
            m.Level.Scene, m.Level.ModelFor, null, Igi2Gameplay.TerrainOwner, magic: m.Services!.Require<Igi2MagicObjTypes>());
        Interaction.Igi2GlassContainer glass = Interaction.Igi2GlassContainer.Build(world, m.Level.ModelFor, new IgiRandom(), explosions: null);
        Saves.Igi2SaveGame.LoadFile(File.ReadAllBytes(file), m.Tree[0], m.Run.Natives, m.Run.Functions, glass: glass);
        Interaction.Igi2Glass broken = glass.Find("0-603_08_1-450")!;
        Assert.True(broken.IsBroken);
        Assert.Single(glass.Panes, static p => p.IsBroken);
        glass.Run();
        Assert.True(broken.IsRemoved);
    }

    private static (RetailIgi2Mission1 Mission, Igi2Soldiers Soldiers, Igi2AiWorld Ai, Igi2TitlePlayer Player) Build(int? facing = null)
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        (List<LoadedPack> packs, ResourceRegistry packResources) = Igi2LevelScene.Packs(m.Level);
        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(packs, packResources);
        IgiRandom random = new();
        Igi2Soldiers soldiers = Igi2Gameplay.SpawnSoldiers(
            m.Tree[0],
            m.Services!,
            m.Collision!,
            clips,
            m.Level.ModelFor,
            m.Services!.Require<Igi2PlayerTuning>(),
            m.Sounds,
            random,
            Game.Ai.AiDifficulty.Normal,
            natives: m.Run.Natives,
            functions: m.Run.Functions);
        Assert.True(PlayerSpawn.TryFindGameplay(m.Level.Root, out PlayerSpawnPoint spawn));
        (Vector3d at, float gamma) = (spawn.Position, spawn.Gamma);
        if (facing is { } id)
        {
            // Fifteen metres along the soldier's view (the second column of his frame), turned to face him.
            Igi2HumanSoldierTask soldier = m.Tree.OfType<Igi2HumanSoldierTask>().Single(t => t.Id == id);
            float[] frame = Igi2Pose.Rotation(0f, 0f, soldier.Gamma);
            at = new Vector3d(soldier.Position.X + (frame[1] * 61440.0), soldier.Position.Y + (frame[4] * 61440.0), soldier.Position.Z);
            gamma = soldier.Gamma + MathF.PI;
        }

        Formats.World.MefModel model = m.Level.ModelFor(spawn.Model)!;
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(
            m.Collision!, Igi2Session.Start().Require<Igi2PlayerTuning>().General, at, gamma, Game.Ai.AiDifficulty.Normal, null, model.BoneHierarchy);
        player.Human.ModelRadius = model.NativeMarkerBound;
        player.Human.RootTranslation = model.BoneHierarchy!.Translations[0];
        player.Human.Model = Igi2BodyModel.From(spawn.Model, model);
        player.Human.Collider = new Collision.Igi2BoneCollider(player.Human, player.Human);
        m.Collision!.AddBody(player.Human.Collider);

        Igi2DetectionWorld detection = new(m.Collision!, m.Materials)
        {
            FlowTicks = () => m.Run.Ticks,
            LevelTick = () => m.Run.LevelFlow?.Tick ?? 0,
        };
        foreach (Engine.Tasks.GameTask node in m.Tree)
        {
            if (node is Igi2HumanSoldierTask { Body: { } soldier })
            {
                detection.Detectables.Add(soldier);
            }
            else if (node is Igi2HumanPlayerTask)
            {
                detection.Detectables.Add(player.Human);
            }
        }

        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        files.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location1/level1/");
        List<Igi2Human> humans = [player.Human, .. soldiers.Bodies];
        Igi2AiWorld ai = Igi2LevelAi.Build(new Igi2LevelAiSetup(
            m.Tree[0],
            new ResourceRegistry(files),
            m.Run.Natives,
            m.Run.Functions,
            player.Human,
            humans,
            random,
            () => m.Run.LevelFlow?.Tick ?? 0)
        {
            Detection = detection,
            FireTaskTypes = m.Services!.Require<Igi2HumanWeapons>().FireTaskTypes,
            Config = m.Services!.TryGet(out Igi2HumanAiConfig? config) ? config : null,
            Difficulty = 1,
            World = m.Services!.Require<Igi2AiWorld>(),
        });
        return (m, soldiers, ai, player);
    }
}
