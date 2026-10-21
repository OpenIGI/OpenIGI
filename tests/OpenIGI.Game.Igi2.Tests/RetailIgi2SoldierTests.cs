using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Mission 1's 53 soldiers made, armed and stood on the level, as <c>HumanSoldier</c> makes and runs them.</summary>
public sealed class RetailIgi2SoldierTests
{
    /// <summary>The first soldier, <c>HumanSoldier_400</c>.</summary>
    private const int First = 400;

    /// <summary>
    /// Every soldier gets a body where the level puts it, turned by its heading, on the rig its model names; soldier 400's script
    /// gives it a Makarov, the first statement of its script being the only one run.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Every_soldier_is_made()
    {
        (RetailIgi2Mission1 m, Igi2Soldiers soldiers) = Spawn();
        Igi2HumanSoldierTask[] tasks = [.. m.Tree.OfType<Igi2HumanSoldierTask>()];

        Assert.Equal(53, soldiers.Bodies.Count);
        Assert.All(tasks, static t => Assert.NotNull(t.Body));
        Igi2HumanSoldierTask first = tasks.Single(static t => t.Id == First);
        Igi2HumanSoldier body = first.Body!;
        Assert.Equal(first.Position, body.Position);
        Assert.Equal((first.Gamma, first.Gamma), (body.Gamma, body.IdealGamma));
        Assert.Equal(1, body.Team);
        Assert.NotNull(body.Animation.Rig);
        Assert.Equal("WEAPON_ID_MAKAROV", body.Weapon.Inventory!.Current?.WeaponType.WeaponId);
    }

    /// <summary>
    /// Run two seconds with nothing on their channels, every soldier stands on the ground where it was put, playing its weapon's
    /// stand clip with its idle arms over it.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Every_soldier_stands_on_the_level()
    {
        (_, Igi2Soldiers soldiers) = Spawn();
        Vector3d[] start = [.. soldiers.Bodies.Select(static b => b.Position)];

        for (int tick = 0; tick < 60; tick++)
        {
            foreach (Igi2HumanSoldier body in soldiers.Bodies)
            {
                body.RunDelayed();
            }
        }

        for (int i = 0; i < soldiers.Bodies.Count; i++)
        {
            Igi2HumanSoldier body = soldiers.Bodies[i];
            Assert.True((body.Signal & Igi2SoldierSignal.Grounded) != 0, $"soldier {i} is not on the ground at {body.Position}");
            Assert.Equal(Igi2SoldierState.Stand, body.Locomotion.CurrentState);
            Assert.Equal(start[i].X, body.Position.X, 1.0);
            Assert.Equal(start[i].Y, body.Position.Y, 1.0);
            Assert.InRange(Math.Abs(body.Position.Z - start[i].Z), 0.0, 4096.0);
            Igi2HumanAnimation animation = Assert.IsType<Igi2HumanAnimation>(body.Animation);
            IReadOnlyList<Formats.Animation.Igi2AnimationClip?> clips = body.Weapon.BodyClips(animation.Rig)!;
            Assert.Same(clips[Igi2SoldierStateAnimations.Table[Igi2SoldierState.Stand]], animation.Main.Clip);
            Assert.NotNull(animation.UpperBody);
            Assert.Equal(Igi2HumanSoldier.UpperIdle, animation.LastUpperBodyState);
        }
    }

    /// <summary>
    /// Soldier 426 is put 919 units (22 cm) under building 4091's floor, the slab's underside 308 above its feet: its first ground
    /// probe finds a low roof (<c>Human_GroundCollision</c>, <c>igi2 0x4FD5C7</c>), so it goes down to a kneel; on the floor the roof
    /// is gone and it gets up again (<c>HumanSoldier_RunStateKneel</c>, <c>0x4E6DE0</c>).
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_soldier_under_a_floor_kneels_and_stands()
    {
        (RetailIgi2Mission1 m, _) = Spawn();
        Igi2HumanSoldier body = m.Tree.OfType<Igi2HumanSoldierTask>().Single(static t => t.Id == 426).Body!;
        List<int> states = [];

        for (int tick = 0; tick < 60; tick++)
        {
            body.RunDelayed();
            if (states.Count == 0 || states[^1] != body.Locomotion.CurrentState)
            {
                states.Add(body.Locomotion.CurrentState);
            }
        }

        Assert.Equal(
            [Igi2SoldierState.Stand, Igi2SoldierState.StandToKneel, Igi2SoldierState.Kneel, Igi2SoldierState.KneelToStand, Igi2SoldierState.Stand],
            states);
        Assert.Equal(-1731.7, body.Position.Z, 1.0);
    }

    /// <summary>
    /// A standing soldier draws as its model posed on its rig, its feet where it stands, and the clip's <c>weapon</c> draws the
    /// weapon in its hand: soldier 400's Makarov.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_soldier_draws_posed_with_its_weapon()
    {
        (RetailIgi2Mission1 m, Igi2Soldiers soldiers) = Spawn();
        Igi2HumanSoldier body = m.Tree.OfType<Igi2HumanSoldierTask>().Single(static t => t.Id == First).Body!;
        for (int tick = 0; tick < 30; tick++)
        {
            body.RunDelayed();
        }

        IReadOnlyList<Game.Player.TitleWorldModel> models = soldiers.Models(30);
        Game.Player.TitleWorldModel drawn = Assert.Single(models, m => ReferenceEquals(m.Instance, body));
        Assert.Equal("rsg3_1", drawn.Model);
        Assert.Equal(body.Position, drawn.Position);
        Assert.NotNull(drawn.Pose);
        Assert.Equal(body.Animation.Rig!.BoneCount, drawn.Pose!.BoneCount);
        string makarov = body.Weapon.Inventory!.Current!.WeaponType.FixedModel;
        Game.Player.TitleWorldModel gun = Assert.Single(models, m => m.Model == makarov && (m.Position - body.Position).Length < 12000.0);
        Assert.Null(gun.Pose);
        Assert.Same(models, soldiers.Models(30));
    }

    private static (RetailIgi2Mission1 Mission, Igi2Soldiers Soldiers) Spawn()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(m.Level);
        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(packs, resources);
        Igi2Soldiers soldiers = Igi2Gameplay.SpawnSoldiers(
            m.Tree[0], m.Services!, m.Collision!, clips, m.Level.ModelFor, m.Services!.Require<Igi2PlayerTuning>(), m.Sounds, new IgiRandom(), Game.Ai.AiDifficulty.Normal);
        return (m, soldiers);
    }
}
