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
    /// Makarov's whole clip at it. He carries no more than that clip (<c>Weapon_EnableWeaponType</c> gives a gun pickup's worth), so
    /// he then asks for a reload that never comes, as retail's does.
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
        Assert.Equal(8, shots);
        Assert.Equal(1f, mind.Soldier.Channels[Igi2SoldierChannel.Reload]);
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
