using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Ai.Runtime;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Player;
using OpenIGI.Game.Saves;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <content>
/// Soldiers read back from a save as <c>HumanSoldier_DeSerializeHandler</c> leaves them: a body dead in the save lies dead at the end of
/// the clip it died by, and one alive in it stands, whatever became of either after the save.
/// </content>
public sealed partial class RetailIgi2LevelAiTests
{
    // The soldier the tests kill: the one the fight tests face.
    private const int Victim = 400;

    /// <summary>
    /// A soldier killed after the save, the save loaded over the same level: he stands again, his pool a shadow, not dying, and his
    /// mind alive -- it hears and thinks again, and has its first update once more as retail's load leaves it to.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_soldier_killed_after_the_save_stands_again_once_it_is_loaded()
    {
        (RetailIgi2Mission1 m, _, _, _) = Build();
        SkipIntro(m);
        Run(m, 300);
        string script = SaveScript.Create(m.Tree[0], Saves.Igi2SaveFormat.Info(11, 0), Saves.Igi2SaveFormat.Context())!;
        Igi2HumanSoldier body = Body(m);
        Igi2HumanAi mind = Assert.IsType<Igi2HumanAi>(body.Mind);
        Assert.False(body.IsDead);

        body.KillOutright();
        Run(m, 200);
        Assert.True(body.IsDead);
        Assert.True(mind.IsDead);

        SaveScript.Load(script, m.Tree[0], Saves.Igi2SaveFormat.Context());

        Assert.False(body.IsDead);
        Assert.False(mind.IsDead);
        Assert.False(mind.HasStarted);
        Assert.False(body.IsBloodPuddle);
        Assert.Equal(0, body.LifeState);
        Assert.Equal(Igi2SoldierState.Stand, body.Locomotion.CurrentState);

        Run(m, 60);
        Assert.False(body.IsDead);
        Assert.True(mind.HasStarted);
    }

    /// <summary>
    /// A soldier dead in the save, read onto the mission built afresh where he still stands: he lies dead where he fell, at the end of
    /// the clip his death last set (<c>iStateLastUsedToSetMainAnimation</c>), in his pool, his mind dead with no death run again, and
    /// the squads that had seen his body still have.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_soldier_dead_in_the_save_lies_dead_by_his_death_clip_on_a_fresh_level()
    {
        (RetailIgi2Mission1 played, _, _, _) = Build();
        SkipIntro(played);
        Run(played, 60);
        Igi2HumanSoldier killed = Body(played);
        killed.KillOutright();
        Run(played, 300);
        Assert.True(killed.IsDead);
        int deathClip = killed.Animation.LastState;
        Core.Numerics.Vector3d where = killed.Position;
        Igi2AiSquad squad = ((Igi2HumanAi)killed.Mind!).Squad!;
        killed.ReportTo(squad);
        string script = SaveScript.Create(played.Tree[0], Saves.Igi2SaveFormat.Info(11, 0), Saves.Igi2SaveFormat.Context())!;

        (RetailIgi2Mission1 fresh, _, _, _) = Build();
        Igi2HumanSoldier body = Body(fresh);
        Igi2HumanAi mind = Assert.IsType<Igi2HumanAi>(body.Mind);
        Assert.False(body.IsDead);
        SaveScript.Load(script, fresh.Tree[0], Saves.Igi2SaveFormat.Context());

        Assert.True(body.IsDead);
        Assert.True(mind.IsDead);
        Assert.True(body.IsBloodPuddle);
        Assert.Equal(Igi2HumanSoldier.DeadAndStill, body.LifeState);
        Assert.Equal(Igi2SoldierState.Dead, body.Locomotion.CurrentState);
        Assert.Equal(deathClip, body.Animation.LastState);
        Assert.Equal(where, body.Position);
        Assert.True(body.HasReportedTo(mind.Squad!));

        Run(fresh, 60);
        Assert.True(body.IsDead);
        Assert.True(Math.Abs(body.Position.Z - where.Z) < 64.0, $"The body moved from {where} to {body.Position}.");
    }

    /// <summary>
    /// A save loaded over the level played on puts its humans back as they were saved and leaves them there: the player walked away
    /// stands where it was saved and does not slide on with the speed it walked off with (<c>HumanPlayer_DeSerializeHandler</c>
    /// clears <c>tAccumulatedSpeed</c> and <c>tWorldSpeed</c>); a guard killed since stands at his full height, every controller
    /// rid of his death's <c>0x40</c>; and no guard's gun sounds its firing's end, as only a weapon still firing is let go.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_load_puts_its_humans_back_where_they_were_saved_and_keeps_them_there()
    {
        (RetailIgi2Mission1 m, Igi2Soldiers soldiers, Igi2AiWorld ai, Igi2TitlePlayer player) = Build();
        m.Tree.OfType<Igi2HumanPlayerTask>().Single().Human = player.Human;
        Collision.Igi2LevelGrounds grounds = new(m.Tree[0], m.Collision!, () => ai.Graphs);
        foreach (Humans.Igi2Human human in soldiers.Bodies.Cast<Humans.Igi2Human>().Append(player.Human))
        {
            ((Igi2LevelSurroundings)human.Surroundings).Grounds = grounds;
        }

        SkipIntro(m);
        Run(m, player, 300, RetailIgi2Mission1.Idle);
        Igi2HumanSoldier victim = Body(m);
        double standingHeight = Height(victim);
        Core.Numerics.Vector3d saved = player.Human.Position;
        string script = SaveScript.Create(m.Tree[0], Saves.Igi2SaveFormat.Info(11, 0), Saves.Igi2SaveFormat.Context())!;

        victim.KillOutright();
        Run(m, player, 300, RetailIgi2Mission1.Idle with { Held = PlayerAction.MoveUp });
        Assert.NotEqual(saved, player.Human.Position);

        HashSet<Audio.Igi2Sound> before = [.. m.Sounds.Playing];
        SaveScript.Load(script, m.Tree[0], Saves.Igi2SaveFormat.Context());
        Run(m, player, 3, RetailIgi2Mission1.Idle);

        Assert.Equal(saved, player.Human.Position);
        Assert.False(victim.IsDead);
        Assert.Equal(standingHeight, Height(victim), 1.0);
        Assert.DoesNotContain(m.Sounds.Playing, s => !before.Contains(s) && s.Name.EndsWith("_loop_e", StringComparison.Ordinal));

        static double Height(Igi2HumanSoldier body) => body.BonePosition("center").Z - body.Position.Z;
    }

    /// <summary>
    /// A guard saved while his death still threw him through the air, the save loaded over the level after he came to rest: he
    /// falls on as a death does and comes to rest where one left to fall rests, not sunk to the ground with his limbs in it. Not
    /// retail, whose load takes every body read back dead for one at rest.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_guard_saved_falling_dead_falls_on_and_rests_as_one_left_to_fall()
    {
        (RetailIgi2Mission1 left, _, _, Igi2TitlePlayer leftPlayer) = Build();
        SkipIntro(left);
        Run(left, leftPlayer, 300, RetailIgi2Mission1.Idle);
        Igi2HumanSoldier fell = Body(left);
        fell.KillOutright();
        Run(left, leftPlayer, 300, RetailIgi2Mission1.Idle);
        Assert.True(fell.IsDeathPositionStabilized);

        (RetailIgi2Mission1 m, _, _, Igi2TitlePlayer player) = Build();
        SkipIntro(m);
        Run(m, player, 300, RetailIgi2Mission1.Idle);
        Igi2HumanSoldier body = Body(m);
        body.KillOutright();
        Run(m, player, 10, RetailIgi2Mission1.Idle);
        Assert.NotEqual(default, body.Speed);
        string script = SaveScript.Create(m.Tree[0], Saves.Igi2SaveFormat.Info(11, 0), Saves.Igi2SaveFormat.Context())!;
        Run(m, player, 300, RetailIgi2Mission1.Idle);

        SaveScript.Load(script, m.Tree[0], Saves.Igi2SaveFormat.Context());
        Assert.False(body.IsDeathPositionStabilized);
        Run(m, player, 300, RetailIgi2Mission1.Idle);

        // Both lie still, the loaded one's hips where the other's are: the loaded body wears its clip's last frame lifted by its
        // root (the controller its load turns to has no 0x40, as in retail), the other its clip unlifted, so their places differ.
        Assert.True(body.IsDeathPositionStabilized);
        double rested = body.Position.Z;
        Run(m, player, 10, RetailIgi2Mission1.Idle);
        Assert.Equal(rested, body.Position.Z);
        Assert.Equal(fell.BonePosition("center").Z, body.BonePosition("center").Z, 160.0);
    }

    /// <summary>
    /// The look the host gathered from the mouse since the last tick is let go of once a save is read: the loaded player faces the
    /// way the save has it on the next tick, not back to where it faced before the load turned by the mouse.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_loaded_player_faces_the_way_the_save_has_it()
    {
        (RetailIgi2Mission1 m, _, _, Igi2TitlePlayer player) = Build();
        m.Tree.OfType<Igi2HumanPlayerTask>().Single().Human = player.Human;
        player.Human.Movement = Igi2PlayerMovement.Igi1;
        string folder = Path.Combine(Path.GetTempPath(), "openigi-igi2-look-" + Guid.NewGuid().ToString("N"));
        try
        {
            List<SaveReport> reports = [];
            Saves.Igi2MissionSaves saves = new(new Saves.Igi2SaveSlots(folder), static () => new SaveOwner(0, 0, 11), m.Run, player.Human)
            {
                Loaded = player.ResetLook,
                Reported = report =>
                {
                    reports.Add(report);
                    player.TellSaveReport(report);
                },
            };
            SkipIntro(m);
            Run(m, player, 60, RetailIgi2Mission1.Idle with { MouseX = 30f, MouseY = 15f });
            Run(m, player, 5, RetailIgi2Mission1.Idle);
            (float gamma, float pitch) = (player.Human.Gamma, player.Human.View.IdealPitch);
            Assert.True(saves.Write(SaveRules.QuickSave));

            Run(m, player, 60, RetailIgi2Mission1.Idle with { MouseX = -50f, MouseY = -40f });
            player.GatherLook(40f, 20f, 0.5f, invertMouse: false);
            Assert.True(saves.Load(SaveRules.QuickSave));
            Run(m, player, 1, RetailIgi2Mission1.Idle);

            Assert.Equal(gamma, player.Human.Gamma, 0.0001f);
            Assert.Equal(pitch, player.Human.View.IdealPitch, 0.0001f);
            Assert.Equal([SaveReport.Saved, SaveReport.Loaded], reports);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    private static void Run(RetailIgi2Mission1 m, Igi2TitlePlayer player, int ticks, TitlePlayerInput input)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            m.Run.Tick();
            player.Tick(input);
        }
    }

    // EditVariable_2112 ends the intro and lets the soldiers' containers run.
    private static void SkipIntro(RetailIgi2Mission1 m) =>
        m.Tree.OfType<OpenIGI.Game.Flow.EditVariableTask>().Single(static v => v.Id == 2112).Value = 1;

    private static void Run(RetailIgi2Mission1 m, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            m.Run.Tick();
        }
    }

    private static Igi2HumanSoldier Body(RetailIgi2Mission1 m) =>
        m.Tree.OfType<Igi2HumanSoldierTask>().Single(static t => t.Id == Victim).Body!;
}
